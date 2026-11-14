using System;

namespace Oddhatter.ResizePlayer
{
    /// <summary>The sizes a player can set. Overall is the whole body; the rest are single body parts.</summary>
    internal enum Part
    {
        Overall,
        Head,
        Neck,
        Chest,
        Belly,
        Hips,
        Arms,
        Hands,
        Legs,
        Feet,
    }

    /// <summary>Size rules with no Unity dependency, so they can be unit tested.</summary>
    internal static class BodyParts
    {
        public const float Min = 0.25f;
        public const float Max = 10f;

        public static readonly Part[] All = (Part[])Enum.GetValues(typeof(Part));

        /// <summary>Limits a size to Min..Max. NaN or infinity (a broken config or network value) counts as normal size.</summary>
        public static float Clamp(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
            {
                return 1f;
            }
            return Math.Min(Max, Math.Max(Min, v));
        }

        /// <summary>Key under which a player's size for this part is stored on their network object (ZDO).</summary>
        public static string ZdoKey(Part p) => "oddhatter.resizeplayer." + p.ToString().ToLowerInvariant();

        /// <summary>
        /// Scale to give a bone so the part ends up at its own size: a bone inherits its parent part's scale, so it gets
        /// own / parent. Both inputs are already clamped, so parent is never 0.
        /// </summary>
        public static float LocalFactor(float own, float parent) => own / parent;

        /// <summary>Reach and camera distance grow with a giant but don't shrink for a tiny player.</summary>
        public static float ReachFactor(float overall) => Math.Max(1f, overall);
    }

    /// <summary>One size per Part.</summary>
    internal sealed class PartValues
    {
        private const float Tolerance = 1e-4f;

        private readonly float[] _values = new float[BodyParts.All.Length];

        public float this[Part p]
        {
            get => _values[(int)p];
            set => _values[(int)p] = value;
        }

        public static PartValues Identity()
        {
            var v = new PartValues();
            for (int i = 0; i < v._values.Length; i++)
            {
                v._values[i] = 1f;
            }
            return v;
        }

        public bool ApproximatelyEquals(PartValues other)
        {
            if (other == null)
            {
                return false;
            }
            for (int i = 0; i < _values.Length; i++)
            {
                if (Math.Abs(_values[i] - other._values[i]) > Tolerance)
                {
                    return false;
                }
            }
            return true;
        }

        public void CopyFrom(PartValues other) => Array.Copy(other._values, _values, _values.Length);
    }
}
