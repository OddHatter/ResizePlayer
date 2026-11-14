using System;
using System.Collections.Generic;

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

        /// <summary>
        /// Movement speed multiplier: stride length follows leg length, which is Overall x Legs, at the normal step rate.
        /// Kept within the size range so stacked extremes (10 x 10) don't produce physics-breaking speeds.
        /// </summary>
        public static float StrideFactor(float overall, float legs) => Clamp(overall * legs);

        /// <summary>
        /// Jump speed multiplier: jump height goes with the square of take-off speed, so sqrt(stride) makes a jump
        /// stride times as high (and as long). A giant clears what it towers over; a tiny player hops.
        /// </summary>
        public static float JumpFactor(float stride) => (float)Math.Sqrt(stride);

        /// <summary>
        /// The highest point of a fall, rewritten so the game's fall damage (4 m to 20 m) counts the drop in body
        /// lengths: a stride-10 giant's 10 m drop counts as 1 m; a stride-0.25 tiny player's 1 m drop counts as 4 m.
        /// </summary>
        public static float LandingAltitude(float maxAirAltitude, float y, float stride) => y + Math.Max(0f, maxAirAltitude - y) / stride;

        /// <summary>Vertical extent of a bone chain as it is now: the sum of its segments' verticals.</summary>
        public static float CurrentVertical(IReadOnlyList<(float vertical, float parentFactor)> chain)
        {
            float sum = 0f;
            foreach ((float vertical, float _) in chain)
            {
                sum += vertical;
            }
            return sum;
        }

        /// <summary>
        /// Vertical extent of the same chain in this pose at vanilla proportions: each segment stretches with the size
        /// this mod gives the bone it hangs from, so divide that back out. The pose (bone rotations) is the animation's.
        /// </summary>
        public static float VanillaVertical(IReadOnlyList<(float vertical, float parentFactor)> chain)
        {
            float sum = 0f;
            foreach ((float vertical, float parentFactor) in chain)
            {
                sum += parentFactor > 0f ? vertical / parentFactor : vertical;
            }
            return sum;
        }

        /// <summary>
        /// How far to raise the skeleton (negative = lower) so the lowest sole is where vanilla's lowest sole is in this
        /// pose. Each entry is a sole's height relative to the hips, (now, vanilla). Pose-aware: standing on long legs
        /// lifts by the extra leg length, lying in bed lifts by about nothing.
        /// </summary>
        public static float GroundOffset(IReadOnlyList<(float current, float vanilla)> soles)
        {
            if (soles.Count == 0)
            {
                return 0f;
            }
            float lowestNow = float.MaxValue, lowestVanilla = float.MaxValue;
            foreach ((float current, float vanilla) in soles)
            {
                lowestNow = Math.Min(lowestNow, current);
                lowestVanilla = Math.Min(lowestVanilla, vanilla);
            }
            return lowestVanilla - lowestNow;
        }

        /// <summary>
        /// Reach and build-distance multiplier: follows size both ways (a giant reaches what it towers over, a tiny
        /// player only what is near). While steering a ship a tiny player keeps vanilla reach, or the game would let go
        /// of the helm (it checks helm distance against this reach).
        /// </summary>
        public static float ReachFactor(float overall, bool steering) => steering ? Math.Max(1f, overall) : overall;

        /// <summary>
        /// Reach in metres: the vanilla reach at this size, plus how far long legs or a long torso lifted the eye the
        /// game measures reach from, so a tall character still reaches the ground. A lowered eye takes nothing off.
        /// </summary>
        public static float Reach(float original, float overall, float eyeLift, bool steering)
            => original * ReachFactor(overall, steering) + Math.Max(0f, eyeLift);

        /// <summary>The game hides the local player when the camera is closer than 2 m to their feet; a tiny player's camera always is.</summary>
        public static float HideDistance(float overall) => 2f * Math.Min(1f, overall);

        /// <summary>
        /// Water depth at which swimming starts: the game uses (swim depth - 0.4 m). Swim depth is already scaled with
        /// Overall; scale the 0.4 m margin too, or a tiny player would swim in a puddle.
        /// </summary>
        public static float SwimThreshold(float swimDepth, float overall) => Math.Max(0f, swimDepth - 0.4f * overall);

        /// <summary>Moves current toward target as a first-order low-pass with the given time constant (seconds).</summary>
        public static float SmoothToward(float current, float target, float dt, float timeConstant)
            => timeConstant <= 0f ? target : current + (target - current) * (1f - (float)Math.Exp(-dt / timeConstant));

        /// <summary>The Player prefab's rest-pose ankle height (metres); 0 (stand on the ankle) if it isn't plausible.</summary>
        public static float RestSoleDepth(float ankleHeight) => ankleHeight > 0f && ankleHeight <= 0.5f ? ankleHeight : 0f;
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
