using System;
using System.Globalization;

namespace ULE.SpawnEditor
{
    internal static class FiniteNumbers
    {
        internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool TryParse(string text, NumberStyles style, IFormatProvider provider, out float value)
            => float.TryParse(text, style, provider, out value) && IsFinite(value);
        internal static void Validate(MapEdits map)
        {
            if (map?.BySpawnId == null) return;
            foreach (var edit in map.BySpawnId.Values)
            {
                if (edit == null) continue;
                if (edit.SpawnChance.HasValue) Require(edit.SpawnChance.Value);
                if (edit.Position != null) { Require(edit.Position.X); Require(edit.Position.Y); Require(edit.Position.Z); }
                if (edit.Rotation != null) { Require(edit.Rotation.X); Require(edit.Rotation.Y); Require(edit.Rotation.Z); }
                if (edit.Items != null) foreach (var item in edit.Items) if (item != null) Require(item.Weight);
            }
        }
        private static void Require(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value > float.MaxValue || value < -float.MaxValue)
                throw new ArgumentException("Changes contain an invalid numeric value. Enter finite numbers before saving.");
        }
    }
}
