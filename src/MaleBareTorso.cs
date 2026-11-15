using System;
using System.Collections.Generic;
using System.Linq;
using ThunderRoad.Manikin;

namespace GettingOverIt
{
    // Identify garments by asset identity, not numeric layers that other mods may change.
    // Remove clothing locations, never the body mesh or its ethnicity/skin properties.
    public sealed class MaleBareTorso : IDisposable
    {
        private readonly ManikinLocations locations;
        private readonly Dictionary<ManikinLocations.LocationKey, ManikinWardrobeData> removed
            = new Dictionary<ManikinLocations.LocationKey, ManikinWardrobeData>();
        private bool disposed;
        private readonly Dictionary<ManikinLocations.LocationKey, ManikinWardrobeData> body
            = new Dictionary<ManikinLocations.LocationKey, ManikinWardrobeData>();

        public MaleBareTorso(ManikinLocations locations)
        {
            this.locations = locations;
            if (locations == null) return;
            foreach (var entry in locations.partLocations)
                if (entry.Value != null && entry.Value.name.StartsWith("HumanMale_Base_Torso", StringComparison.Ordinal))
                    body[entry.Key] = entry.Value;
        }

        public static bool IsClothing(string creatureName, string channel, int layer, string wardrobeName)
        {
            return string.Equals(creatureName, "HumanMale", StringComparison.Ordinal)
                && channel == "Torso"
                && !string.IsNullOrEmpty(wardrobeName)
                && wardrobeName.StartsWith("HumanMale_Shirt", StringComparison.Ordinal);
        }

        public void Apply(string creatureName)
        {
            if (disposed || locations == null || creatureName != "HumanMale") return;
            bool restoredBody = false;
            foreach (var entry in body)
                if (entry.Value != null && !locations.partLocations.ContainsKey(entry.Key))
                {
                    locations.partLocations[entry.Key] = entry.Value;
                    restoredBody = true;
                }
            var clothing = new HashSet<ManikinWardrobeData>(locations.partLocations
                .Where(p => p.Value != null && IsClothing(creatureName, p.Key.channel, p.Key.layer, p.Value.name))
                .Select(p => p.Value));
            if (clothing.Count == 0)
            {
                if (restoredBody) locations.UpdateParts();
                return;
            }
            // Shirts can occupy both torso and sleeve locations. Remove the whole garment.
            foreach (var entry in locations.partLocations.Where(p => clothing.Contains(p.Value)).ToArray())
            {
                if (!removed.ContainsKey(entry.Key)) removed.Add(entry.Key, entry.Value);
                locations.RemovePart(entry.Key);
            }
            locations.UpdateParts();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (locations == null || removed.Count == 0) return;
            foreach (var entry in removed)
                if (entry.Value != null) locations.partLocations[entry.Key] = entry.Value;
            locations.UpdateParts();
            removed.Clear();
        }
    }
}
