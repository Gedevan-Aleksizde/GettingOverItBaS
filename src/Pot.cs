using System.Collections;
using System.Collections.Generic;
using ThunderRoad;
using ThunderRoad.Manikin;
using UnityEngine;

namespace GettingOverIt
{
    // Keep the current game's torso channels/layers, but supply our own rigged mesh.
    public class PotWardrobe : ItemModuleWardrobe
    {
        public override IEnumerator LoadAddressableAssetsCoroutine(ItemData data)
        {
            // The catalog loads assets BEFORE ThunderScript.ScriptLoaded runs.
            yield return OriginalPot.LoadAssets(data.owner?.fullPath);
            if (wardrobes.TrueForAll(w => w.manikinWardrobeData != null)) yield break;
            yield return base.LoadAddressableAssetsCoroutine(data);
            foreach (var wardrobe in wardrobes)
            {
                var source = wardrobe.manikinWardrobeData;
                var clone = Object.Instantiate(source);
                clone.name = "GettingOverItPot_" + wardrobe.creatureName;
                clone.assetPrefab = new AssetReferenceManikinPart("");
                clone.dependencies = new ManikinWardrobeData[0];
                clone.channels = new[] { "Torso" };
                clone.layers = new[] { 30 };
                clone.fullyOccludedLayers = new int[1];
                clone.partialOccludedLayers = new int[1];
                clone.partialOccludedMasks = new int[1];
                clone.occlusionID = "";
                clone.occlusionIDHash = 0;
                wardrobe.manikinWardrobeData = clone;
                Catalog.ReleaseAsset(source);
            }
        }

        public override void ReleaseAddressableAssets()
        {
            foreach (var wardrobe in wardrobes)
            {
                if (wardrobe.manikinWardrobeData != null) Object.Destroy(wardrobe.manikinWardrobeData);
                wardrobe.manikinWardrobeData = null;
            }
        }

        public override void OnItemLoaded(Item item)
        {
            base.OnItemLoaded(item);
            if (!OriginalPot.IsReady) return;
            foreach (var renderer in item.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            OriginalPot.Attach(item.transform, 0.22f);
        }
    }

    public class PotApparel : ItemModuleApparel
    {
        public override void OnEquip(Creature creature, ApparelModuleType equippedOn, ItemModuleWardrobe.CreatureWardrobe wardrobeData)
        {
            if (!OriginalPot.IsReady) return;
            var pot = creature.GetComponent<WornPot>() ?? creature.gameObject.AddComponent<WornPot>();
            pot.Initialize(creature);
        }

        public override void OnUnequip(Creature creature, ApparelModuleType equippedOn, ItemModuleWardrobe.CreatureWardrobe wardrobeData)
        {
            var pot = creature.GetComponent<WornPot>();
            if (pot != null) pot.Restore();
        }
    }

    public class WornPot : MonoBehaviour
    {
        private Creature creature;
        private GameObject visual;
        private bool oldMove, oldJump, restored;
        public void Initialize(Creature target)
        {
            if (visual != null) Restore();
            restored = false;
            creature = target;
            // Player.UpdateBody offsets the creature from the VR locomotion origin to follow
            // the headset. The creature root is its actual foot position and stays at floor
            // height when crouching; the locomotion origin is only the playspace reference.
            visual = OriginalPot.Attach(creature.transform, 1f);
            if (creature.isPlayer && Player.local != null)
            {
                oldMove = Player.local.locomotion.allowMove;
                oldJump = Player.local.locomotion.allowJump;
                Player.TogglePlayerMovement(false);
                Player.TogglePlayerJump(false);
            }
        }
        private void LateUpdate()
        {
            if (!restored && creature != null && creature.isPlayer && Player.currentCreature == creature)
            {
                Player.TogglePlayerMovement(false);
                Player.TogglePlayerJump(false);
            }
        }
        public void Restore()
        {
            if (restored) return;
            restored = true;
            if (visual != null) Object.Destroy(visual);
            if (creature != null && creature.isPlayer && Player.currentCreature == creature && Player.local != null)
            {
                Player.TogglePlayerMovement(oldMove);
                Player.TogglePlayerJump(oldJump);
            }
        }
        private void OnDestroy() { Restore(); }
    }

    public static class OriginalPot
    {
        private static AssetBundle bundle;
        private static GameObject prefab;
        private static AssetBundleCreateRequest pendingBundle;
        private static AssetBundleRequest pendingPrefab;
        public static bool IsReady => prefab != null;
        public static string GetBundlePath(string modPath) => System.IO.Path.Combine(modPath, "Models", "gettingoverit-original-pot.bundle");
        public static IEnumerator LoadAssets(string owningModPath)
        {
            if (prefab != null) yield break;
            if (string.IsNullOrWhiteSpace(owningModPath))
            {
                Debug.LogError("[GettingOverIt] Pot item has no owning mod path; skipping pot assets without blocking catalog loading");
                yield break;
            }
            var bundlePath = GetBundlePath(owningModPath);
            if (!System.IO.File.Exists(bundlePath))
            {
                Debug.LogError("[GettingOverIt] Pot bundle missing: " + bundlePath);
                yield break;
            }
            if (bundle == null)
            {
                var load = pendingBundle ?? (pendingBundle = AssetBundle.LoadFromFileAsync(bundlePath));
                yield return load;
                bundle = load.assetBundle;
                pendingBundle = null;
            }
            if (bundle == null)
            {
                Debug.LogError("[GettingOverIt] Cannot load original pot bundle: " + bundlePath);
                yield break;
            }
            var request = pendingPrefab ?? (pendingPrefab = bundle.LoadAssetAsync<GameObject>("Assets/Original/GettingOverItPot.prefab"));
            yield return request;
            prefab = request.asset as GameObject;
            pendingPrefab = null;
            if (prefab == null) Debug.LogError("[GettingOverIt] Original pot prefab missing from bundle");
        }
        public static GameObject Attach(Transform bone, float scale)
        {
            if (prefab == null) throw new System.InvalidOperationException("Original pot prefab not loaded");
            var visual = Object.Instantiate(prefab, bone, false);
            visual.name = "GettingOverIt_OriginalPot";
            visual.transform.localScale *= scale;
            return visual;
        }
        public static void Release()
        {
            prefab = null;
            if (bundle != null) bundle.Unload(false);
            bundle = null;
        }
    }
}
