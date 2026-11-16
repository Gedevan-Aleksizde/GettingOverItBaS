using System;
using System.Collections.Generic;
using System.Linq;
using ThunderRoad;
using ThunderRoad.Manikin;
using UnityEngine;

namespace GettingOverIt
{
    public sealed class ModeOutfit : IDisposable
    {
        private readonly Creature creature;
        private readonly Dictionary<ManikinLocations.LocationKey, ManikinWardrobeData> original;
        private readonly List<ItemContent> stripped = new List<ItemContent>();
        private bool disposed;
        private bool partsReady;
        private readonly Action updateParts;

        public ModeOutfit(Creature creature, Action updateParts = null)
        {
            this.creature = creature;
            this.updateParts = updateParts ?? creature.equipment.UpdateParts;
            original = new Dictionary<ManikinLocations.LocationKey, ManikinWardrobeData>(creature.manikinLocations.partLocations);
            creature.equipment.onCreaturePartChanged += OnPartsChanged;
        }

        public static Dictionary<ManikinLocations.LocationKey, T> BuildLayout<T>(
            IEnumerable<KeyValuePair<ManikinLocations.LocationKey, T>> basis,
            IEnumerable<KeyValuePair<ManikinLocations.LocationKey, T>> body,
            IEnumerable<KeyValuePair<ManikinLocations.LocationKey, T>> pot)
        {
            var result = new Dictionary<ManikinLocations.LocationKey, T>();
            foreach (var part in basis.Concat(body))
                result[part.Key] = part.Value;
            foreach (var part in pot) result[part.Key] = part.Value;
            return result;
        }

        private IEnumerable<KeyValuePair<ManikinLocations.LocationKey, ManikinWardrobeData>> Slots(ManikinWardrobeData data)
        {
            if (data == null) yield break;
            for (int i = 0; i < data.channels.Length; i++)
                yield return new KeyValuePair<ManikinLocations.LocationKey, ManikinWardrobeData>(
                    new ManikinLocations.LocationKey(data.channels[i], data.layers[i]), data);
        }

        public void Enforce(ItemContent pot)
        {
            if (disposed || creature == null) return;
            // Equipment can arrive after container.contentLoaded, or be equipped by another script.
            foreach (var content in creature.equipment.GetWornContents())
            {
                if (ReferenceEquals(content, pot)) continue;
                var module = content.data.GetModule<ItemModuleWardrobe>();
                if (module == null || module.category == Equipment.WardRobeCategory.Body) continue;
                var wardrobe = module.GetWardrobe(creature);
                if (wardrobe?.manikinWardrobeData == null) continue;
                if (!stripped.Contains(content))
                {
                    stripped.Add(content);
                    // Keep late-loaded outfits for restoration too.
                    foreach (var slot in Slots(wardrobe.manikinWardrobeData)) original[slot.Key] = slot.Value;
                }
                creature.equipment.UnequipWardrobe(content, updateParts: false);
            }

            var basis = (creature.orgWardrobeLocations.wardrobe ?? new ManikinWardrobeData[0]).SelectMany(Slots);
            var body = creature.equipment.GetWornContents()
                .Select(c => c.data.GetModule<ItemModuleWardrobe>())
                .Where(m => m != null && m.category == Equipment.WardRobeCategory.Body)
                .SelectMany(m => Slots(m.GetWardrobe(creature)?.manikinWardrobeData));
            var potData = pot?.data.GetModule<ItemModuleWardrobe>()?.GetWardrobe(creature)?.manikinWardrobeData;
            var target = BuildLayout(basis, body, Slots(potData));
            if (!target.Values.Any(d => d.name.StartsWith(creature.data.name + "_Base_Torso", StringComparison.Ordinal)))
                throw new InvalidOperationException("Base torso missing from the native wardrobe; refusing to hide the body");
            var current = creature.manikinLocations.partLocations;
            if (current.Count != target.Count || target.Any(p => !current.TryGetValue(p.Key, out var value) || value != p.Value))
            {
                current.Clear();
                foreach (var slot in target) current[slot.Key] = slot.Value;
                creature.manikinParts.disableRenderersDuringUpdate = false;
                partsReady = false;
                updateParts();
                Debug.Log("[GettingOverIt] Outfit reconciled: " + target.Count + " native body/pot slots, " + stripped.Count + " garments stored");
            }
            ResetOcclusion();
        }

        private void OnPartsChanged(EventTime time)
        {
            if (!disposed && time == EventTime.OnEnd)
            {
                partsReady = true;
                ResetOcclusion();
            }
        }

        public bool Verify(ItemContent pot)
        {
            if (!partsReady || creature.equipment.GetPendingApparelLoading() != 0
                || creature.manikinLocations.PendingHandles() != 0) return false;
            if (!pot.HasState<ContentStateWorn>()) throw new InvalidOperationException("Pot is not equipped");
            foreach (var content in creature.equipment.GetWornContents())
                if (!ReferenceEquals(content, pot) && content.data.GetModule<ItemModuleWardrobe>()?.category == Equipment.WardRobeCategory.Apparel)
                    throw new InvalidOperationException("Apparel remains equipped: " + content.referenceID);
            var renderers = creature.manikinLocations.GetPartsAtChannel("Torso")
                .SelectMany(part => part.GetComponentsInChildren<SkinnedMeshRenderer>(true)).ToArray();
            if (!renderers.Any(r => r != null && r.sharedMesh != null && r.enabled))
                throw new InvalidOperationException("Native torso renderer did not reappear after rebuilding the outfit");
            foreach (var renderer in renderers)
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null || !material.shader.isSupported)
                        throw new InvalidOperationException("Native torso has an unsupported material");
                    if (creature.data.name == "HumanMale" && material.HasProperty("_Bitmask") && material.GetFloat("_Bitmask") != 0)
                        throw new InvalidOperationException("Native torso still has a clothing occlusion mask");
                }
            Debug.Log("[GettingOverIt] Verified: pot equipped, no apparel, " + renderers.Length + " native torso renderers, body masks cleared");
            return true;
        }

        private void ResetOcclusion()
        {
            if (creature == null || creature.manikinProperties == null) return;
            // Old clothing masks must not continue clipping the preserved torso after removing clothing.
            if (creature.data.name == "HumanMale") creature.manikinProperties.occlusionBitmasks.Clear();
            creature.manikinProperties.UpdateProperties();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (creature == null) return;
            creature.equipment.onCreaturePartChanged -= OnPartsChanged;
            foreach (var content in stripped) creature.equipment.EquipWardrobe(content, updateParts: false);
            var slots = creature.manikinLocations.partLocations;
            slots.Clear();
            foreach (var entry in original) if (entry.Value != null) slots[entry.Key] = entry.Value;
            creature.manikinParts.disableRenderersDuringUpdate = false;
            updateParts();
        }
    }
}
