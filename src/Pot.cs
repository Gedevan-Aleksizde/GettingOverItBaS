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
            yield return PotMesh.LoadMaterial();
            if (wardrobes.TrueForAll(w => w.manikinWardrobeData != null)) yield break;
            yield return base.LoadAddressableAssetsCoroutine(data);
            foreach (var wardrobe in wardrobes)
            {
                var source = wardrobe.manikinWardrobeData;
                var clone = Object.Instantiate(source);
                clone.name = "GettingOverItPot_" + wardrobe.creatureName;
                clone.assetPrefab = new AssetReferenceManikinPart("");
                clone.dependencies = new ManikinWardrobeData[0];
                clone.fullyOccludedLayers = new int[clone.layers.Length];
                clone.partialOccludedLayers = new int[clone.layers.Length];
                clone.partialOccludedMasks = new int[clone.layers.Length];
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
            foreach (var renderer in item.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            PotMesh.Attach(item.transform, 0.22f);
        }
    }

    public class PotApparel : ItemModuleApparel
    {
        public override void OnEquip(Creature creature, ApparelModuleType equippedOn, ItemModuleWardrobe.CreatureWardrobe wardrobeData)
        {
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
            // The locomotion origin stays at the feet while the torso crouches into the pot.
            visual = PotMesh.Attach(creature.currentLocomotion.transform, 1f);
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

    public static class PotMesh
    {
        private static GameObject materialPrefab;
        private static Material materialSource;

        public static IEnumerator LoadMaterial()
        {
            if (materialSource != null) yield break;
            var data = Catalog.GetData<ItemData>("MetalPottery_01", true);
            if (data == null) throw new System.InvalidOperationException("MetalPottery_01 material source missing");
            yield return Catalog.LoadAssetCoroutine<GameObject>(data.prefabAddress, prefab =>
            {
                materialPrefab = prefab;
                if (prefab == null) return;
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                        if (material != null && material.shader != null && material.shader.isSupported)
                        {
                            materialSource = material;
                            return;
                        }
            }, "GettingOverIt pot material");
            if (materialSource == null) throw new System.InvalidOperationException("No supported material on MetalPottery_01");
        }

        public static void ReleaseMaterial()
        {
            materialSource = null;
            if (materialPrefab != null) Catalog.ReleaseAsset(materialPrefab);
            materialPrefab = null;
        }
        // Lathed cross-section closes the bottom and rim, with an open, hollow interior.
        public static readonly Vector2[] Profile = {
            new Vector2(0, 0.04f), new Vector2(0.27f, 0.04f),
            new Vector2(0.39f, 0.18f), new Vector2(0.43f, 0.43f),
            new Vector2(0.38f, 0.72f), new Vector2(0.34f, 0.79f),
            new Vector2(0.30f, 0.79f), new Vector2(0.34f, 0.70f),
            new Vector2(0.38f, 0.43f), new Vector2(0.34f, 0.20f),
            new Vector2(0.24f, 0.10f), new Vector2(0, 0.10f)
        };
        public static GameObject Attach(Transform bone, float scale)
        {
            var go = new GameObject("GettingOverIt_Pot");
            go.transform.SetParent(bone, false);
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            const int segments = 48;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var weights = new List<BoneWeight>();
            var uv = new List<Vector2>();
            for (int ring = 0; ring < Profile.Length; ring++)
                for (int i = 0; i <= segments; i++)
                {
                    float angle = i * Mathf.PI * 2 / segments;
                    vertices.Add(new Vector3(Mathf.Cos(angle) * Profile[ring].x, Profile[ring].y, Mathf.Sin(angle) * Profile[ring].x) * scale);
                    weights.Add(new BoneWeight { boneIndex0 = 0, weight0 = 1 });
                    uv.Add(new Vector2((float)i / segments, (float)ring / (Profile.Length - 1)));
                    if (ring == 0 || i == segments) continue;
                    int a = (ring - 1) * (segments + 1) + i, b = ring * (segments + 1) + i;
                    triangles.Add(a); triangles.Add(b); triangles.Add(a + 1);
                    triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);
                }
            var mesh = new Mesh { name = "GettingOverIt_PotMesh" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uv);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = new[] { bone.worldToLocalMatrix * go.transform.localToWorldMatrix };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { bone }; renderer.rootBone = bone;
            renderer.updateWhenOffscreen = true;
            if (materialSource == null) throw new System.InvalidOperationException("Pot material not loaded");
            var material = new Material(materialSource) { name = "GettingOverIt_PotMaterial" };
            if (material.HasProperty("_Bitmask")) material.SetFloat("_Bitmask", 0);
            renderer.sharedMaterial = material;
            var resources = go.AddComponent<PotResources>(); resources.Mesh = mesh; resources.Material = material;
            return go;
        }
    }

    public class PotResources : MonoBehaviour
    {
        public Mesh Mesh;
        public Material Material;
        private void OnDestroy() { if (Mesh != null) Destroy(Mesh); if (Material != null) Destroy(Material); }
    }
}
