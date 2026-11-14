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
        /// Extra gravity, in multiples of normal gravity: gravity follows stride, so a fall takes the vanilla time
        /// measured in body heights (a stride-10 giant falls 10 m in the time a normal player falls 1 m; a tiny player
        /// floats down). Without it a giant drops in slow motion and a tiny player plummets.
        /// </summary>
        public static float ExtraGravity(float stride) => stride - 1f;

        /// <summary>
        /// Jump height multiplier: a giant jumps stride times as high, clearing what it towers over. A tiny player keeps
        /// the vanilla height (small animals jump about as high as big ones; a quarter-height hop couldn't get onto
        /// full-size steps).
        /// </summary>
        public static float JumpHeight(float stride) => Math.Max(1f, stride);

        /// <summary>
        /// Jump take-off speed multiplier that reaches JumpHeight under the scaled gravity (height = v^2 / 2g).
        /// </summary>
        public static float JumpFactor(float stride) => (float)Math.Sqrt(stride * JumpHeight(stride));

        /// <summary>
        /// Foot IK distances in world metres (ankle height above the ground, raycast start above the foot, plant slip
        /// distance), at a foot this many times vanilla size. The game's values are for a vanilla foot: on a tiny player
        /// they hold the ankles too high and bend the knees into a crouch. (The IK's foot-down threshold is measured in
        /// the body's own scaled space, so it needs no scaling.)
        /// </summary>
        public static float FootIkDistance(float original, float footScale) => original * footScale;

        /// <summary>
        /// Feather-cape fall-speed cap (m/s) at this stride. The game applies it every frame, even on the ground, so there
        /// it scales like run speed (a giant running downhill sinks faster than the vanilla cap and was held up off the
        /// slope). In the air a giant's cap grows only with sqrt(stride): stride x the cap fell too fast for a cape, and
        /// the vanilla cap would let a fast giant glide far off every crest. A tiny player's cap follows stride.
        /// </summary>
        public static float MaxFallSpeed(float cap, float stride, bool onGround)
            => cap * (onGround ? stride : Math.Min(stride, (float)Math.Sqrt(stride)));

        /// <summary>
        /// The highest point of a fall, rewritten so the game's fall damage (4 m to 20 m) counts the drop in jump
        /// heights: a stride-10 giant jumps 10x as high, so its 10 m drop counts as 1 m. A tiny player jumps the vanilla
        /// height and takes vanilla fall damage, so it always lands its own jump unhurt.
        /// </summary>
        public static float LandingAltitude(float maxAirAltitude, float y, float stride)
        {
            return y + Math.Max(0f, maxAirAltitude - y) / JumpHeight(stride);
        }

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
        /// Reach and build-distance multiplier: a giant reaches what it towers over; a tiny player keeps vanilla reach
        /// (shrinking it made building with full-size pieces impractical).
        /// </summary>
        public static float ReachFactor(float overall) => Math.Max(1f, overall);

        /// <summary>
        /// Reach in metres: the vanilla reach at this size, plus how far long legs or a long torso lifted the eye the
        /// game measures reach from, so a tall character still reaches the ground. A lowered eye takes nothing off.
        /// </summary>
        public static float Reach(float original, float overall, float eyeLift)
            => original * ReachFactor(overall) + Math.Max(0f, eyeLift);

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

        /// <summary>
        /// Walking speed in water. The game takes speed - speed^2 x drag x min(1, depth / (collider height / 3)): squared
        /// speed means a 10x-speed giant is stopped dead (any speed over 20 m/s goes to zero or backwards), and the
        /// unscaled collider height means ankle-deep water drags a giant fully. Apply it at vanilla scale instead: the
        /// speed divided by stride, the depth against the scaled body, then scaled back up.
        /// </summary>
        public static float WadingSpeed(float speed, float depth, float colliderHeight, float drag, float stride, float overall)
        {
            if (depth <= 0f)
            {
                return speed;
            }
            float vanillaSpeed = speed / stride;
            float immersion = Math.Min(1f, Math.Max(0f, depth / (colliderHeight * overall / 3f)));
            return (vanillaSpeed - vanillaSpeed * vanillaSpeed * immersion * drag) * stride;
        }

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
