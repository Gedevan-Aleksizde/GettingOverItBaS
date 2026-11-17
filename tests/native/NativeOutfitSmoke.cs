using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GettingOverIt;
using ThunderRoad;
using ThunderRoad.Manikin;
using UnityEngine;

public static class NativeOutfitSmoke
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("Native outfit: " + message);
    }

    public static string RunStartup(string modPath)
    {
        // Match ModManager: catalog ownership exists, but no ThunderScript has been constructed yet.
        var module = new PotWardrobe();
        var item = new ItemData { id = "GOIPot", owner = new ModManager.ModData { fullPath = modPath } };
        var catalogLoad = module.LoadAddressableAssetsCoroutine(item);
        Check(catalogLoad.MoveNext() && catalogLoad.Current is IEnumerator, "catalog starts custom asset loading before scripts");
        Drain((IEnumerator)catalogLoad.Current);
        var visual = OriginalPot.Attach(null, 1);
        var renderers = visual.GetComponentsInChildren<Renderer>();
        Check(renderers.Length > 0, "pot prefab loaded before ScriptLoaded");
        foreach (var renderer in renderers)
            foreach (var material in renderer.sharedMaterials)
                Check(material != null && material.shader != null && material.shader.isSupported
                    && material.enableInstancing && material.mainTexture != null && material.mainTexture.name == "Pottexture", "startup pot material supports instanced stereo");
        UnityEngine.Object.DestroyImmediate(visual);
        CheckPotAlignment();
        OriginalPot.Release();
        // A missing asset must not abort the game's catalog-loading coroutine.
        var missing = module.LoadAddressableAssetsCoroutine(new ItemData { id = "GOIPot" });
        Check(missing.MoveNext(), "missing owner still returns an asset-loading coroutine");
        Drain((IEnumerator)missing.Current);
        return "PASS: startup asset load before ThunderScript.ScriptLoaded, pot texture/shader, missing-owner recovery";
    }

    private static void CheckPotAlignment()
    {
        var origin = new GameObject("VR locomotion origin");
        origin.SetActive(false);
        origin.transform.SetPositionAndRotation(new Vector3(4, 2, -3), Quaternion.Euler(0, 53, 0));
        var body = new GameObject("Offset player body");
        body.transform.SetParent(origin.transform, false);
        body.transform.localPosition = new Vector3(-0.6f, 0, 0.3f);
        var creature = body.AddComponent<Creature>();
        creature.locomotion = origin.AddComponent<Locomotion>();
        var worn = body.AddComponent<WornPot>();
        worn.Initialize(creature);
        var pot = body.GetComponentInChildren<MeshFilter>(true);
        // Mesh vertices, not the model pivot or imported renderer bounds, define its visible centre.
        Check(pot != null, "worn pot follows the body rather than the VR origin");
        CheckPotGeometry(body.transform, pot);
        body.transform.localPosition = new Vector3(0.45f, 0, -0.2f);
        body.transform.localRotation = Quaternion.Euler(0, 127, 0);
        body.transform.localScale = Vector3.one * 1.15f;
        CheckPotGeometry(body.transform, pot);
        UnityEngine.Object.DestroyImmediate(origin);
    }

    private static void CheckPotGeometry(Transform body, MeshFilter pot)
    {
        var vertices = pot.sharedMesh.vertices;
        var matrix = body.worldToLocalMatrix * pot.transform.localToWorldMatrix;
        var bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[0]), Vector3.zero);
        foreach (var vertex in vertices) bounds.Encapsulate(matrix.MultiplyPoint3x4(vertex));
        Check(Mathf.Abs(bounds.center.x) < 0.001f && Mathf.Abs(bounds.center.z) < 0.001f,
            "actual pot mesh centre matches body centre: " + bounds.center.ToString("F5"));
        Check(Mathf.Abs(bounds.min.y) < 0.001f, "pot bottom matches body feet");
    }

    private static void Drain(IEnumerator routine)
    {
        while (routine.MoveNext())
        {
            if (routine.Current is IEnumerator nested) Drain(nested);
            else if (routine.Current is AssetBundleCreateRequest bundle) { var result = bundle.assetBundle; }
            else if (routine.Current is AssetBundleRequest asset) { var result = asset.asset; }
            else if (routine.Current != null) throw new Exception("Unexpected startup yield: " + routine.Current.GetType());
        }
    }

    public static string Run()
    {
        RunActivationEvents();
        var root = new GameObject("Native outfit test");
        root.SetActive(false); // Construct real components without starting the full game's managers.
        var creature = root.AddComponent<Creature>();
        creature.data = new CreatureData { name = "HumanMale" };
        creature.container = root.AddComponent<Container>();
        creature.container.creature = creature;
        creature.equipment = root.AddComponent<Equipment>();
        creature.equipment.creature = creature;
        creature.manikinParts = root.AddComponent<ManikinPartList>();
        creature.manikinLocations = root.AddComponent<ManikinLocations>();
        creature.manikinProperties = root.AddComponent<ManikinProperties>();
        creature.manikinProperties.properties = new ManikinProperty[0];
        var child = new GameObject("Torso mask");
        child.transform.SetParent(root.transform, false);
        var torsoProperties = child.AddComponent<ManikinProperties>();
        torsoProperties.occlusionIDHash = 123;
        torsoProperties.occlusionBitmask = 255;
        creature.manikinProperties.occlusionBitmasks[123] = 255;
        var children = (List<ManikinProperties>)typeof(ManikinProperties)
            .GetField("childrenProperties", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(creature.manikinProperties);
        children.Add(torsoProperties);
        var torso = Part("HumanMale_Base_Torso", "Torso", 7);
        var legs = Part("HumanMale_Base_Legs", "Legs", 5);
        var underwear = Part("HumanMale_Underwear", "Legs", 3);
        creature.orgWardrobeLocations = new ManikinLocations.JsonWardrobeLocations { wardrobe = new[] { torso, legs, underwear } };
        Add(creature, torso); Add(creature, legs); Add(creature, underwear);
        var shirt = Garment(creature, "ApparelShirt01", Part("HumanMale_Shirt01", "Torso", 2));
        var potPart = Part("GettingOverItPot_HumanMale", "Torso", 30);
        int rebuilds = 0;
        using (var outfit = new ModeOutfit(creature, () => rebuilds++))
        {
            outfit.Enforce(null);
            Check(!shirt.HasState<ContentStateWorn>(), "initial shirt is unequipped by real Equipment");
            Check(creature.manikinLocations.GetWardrobeData("Torso", 7) == torso, "native torso survives stripping");
            Check(creature.manikinLocations.GetWardrobeData("Legs", 3) == underwear, "lower-body underwear remains visible");
            Check(torsoProperties.occlusionBitmask == 0, "real ManikinProperties clears old torso mask");
            var armor = Garment(creature, "LateArmor", Part("HumanMale_Armor", "Torso", 1));
            var pot = Garment(creature, "GOIPot", potPart);
            outfit.Enforce(pot);
            Check(!armor.HasState<ContentStateWorn>(), "late-loaded armor is unequipped");
            Check(pot.HasState<ContentStateWorn>(), "pot stays equipped");
            Check(creature.manikinLocations.GetWardrobeData("Torso", 7) == torso, "pot does not replace torso");
            Check(creature.manikinLocations.GetWardrobeData("Torso", 30) == potPart, "pot has independent native slot");
            int stableRebuilds = rebuilds;
            outfit.Enforce(pot);
            Check(rebuilds == stableRebuilds, "stable outfit does not rebuild every frame");
            outfit.Dispose();
            Check(shirt.HasState<ContentStateWorn>(), "original shirt restored");
            Check(armor.HasState<ContentStateWorn>(), "late armor restored");
            Check(creature.manikinLocations.GetWardrobeData("Legs", 3) == underwear, "original underwear restored");
        }
        UnityEngine.Object.DestroyImmediate(root);
        return "PASS: " + checks + " Unity-native outfit assertions (real Equipment and Manikin components)";
    }

    private static void RunActivationEvents()
    {
        var script = new GettingIt();
        script.ScriptEnable();
        try
        {
            var policy = (LevelActivation)typeof(GettingIt).GetField("activation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(script);
            var level = new LevelData { id = "DungeonOutpost" };
            var mode = new LevelData.Mode { name = "CrystalHunt" };
            ModOptions.EnableCrystalHunt = false;
            EventManager.InvokeLevelLoad(level, mode, EventTime.OnStart);
            ModOptions.EnableCrystalHunt = true;
            EventManager.InvokeLevelLoad(level, mode, EventTime.OnEnd);
            Check(!policy.ShouldApply("CrystalHunt", level.id, mode.name, true), "option change during load waits for next level");
            EventManager.InvokeLevelLoad(level, mode, EventTime.OnStart);
            Check(policy.ShouldApply("CrystalHunt", level.id, mode.name, true), "native level-start event activates Crystal Hunt");
            ModOptions.EnableCrystalHunt = false;
            EventManager.InvokeLevelLoad(level, mode, EventTime.OnEnd);
            Check(policy.ShouldApply("CrystalHunt", level.id, mode.name, true), "native level-end event cannot change latched option");
            EventManager.InvokeLevelLoad(level, mode, EventTime.OnStart);
            Check(!policy.ShouldApply("CrystalHunt", level.id, mode.name, true), "next native level-start applies disable");
        }
        finally { ModOptions.EnableCrystalHunt = false; script.ScriptDisable(); }
    }

    private static ManikinWardrobeData Part(string name, string channel, int layer)
    {
        var data = ScriptableObject.CreateInstance<ManikinWardrobeData>();
        data.name = name;
        data.channels = new[] { channel }; data.layers = new[] { layer };
        data.assetPrefab = new AssetReferenceManikinPart("");
        data.fullyOccludedLayers = new int[1]; data.partialOccludedLayers = new int[1]; data.partialOccludedMasks = new int[1];
        data.dependencies = new ManikinWardrobeData[0];
        return data;
    }
    private static void Add(Creature creature, ManikinWardrobeData data)
    {
        for (int i = 0; i < data.channels.Length; i++)
            creature.manikinLocations.partLocations[new ManikinLocations.LocationKey(data.channels[i], data.layers[i])] = data;
    }
    private static ItemContent Garment(Creature creature, string id, ManikinWardrobeData part)
    {
        var module = new ItemModuleWardrobe { category = Equipment.WardRobeCategory.Apparel };
        module.wardrobes.Add(new ItemModuleWardrobe.CreatureWardrobe { creatureName = "HumanMale", manikinWardrobeData = part });
        var item = new ItemData { id = id, type = ItemData.Type.Wardrobe, modules = new List<ItemModule> { module } };
        var content = new ItemContent { referenceID = id, loadable = item, state = new ContentStateWorn() };
        creature.container.contents.Add(content);
        Add(creature, part);
        return content;
    }
}
