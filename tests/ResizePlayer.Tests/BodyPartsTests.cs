using System;
using System.Linq;
using Oddhatter.ResizePlayer;
using Xunit;

namespace ResizePlayer.Tests
{
    public class BodyPartsTests
    {
        [Fact] public void Clamp_inRange_unchanged() => Assert.Equal(2.5f, BodyParts.Clamp(2.5f));

        [Fact] public void Clamp_low() { Assert.Equal(0.25f, BodyParts.Clamp(0f)); Assert.Equal(0.25f, BodyParts.Clamp(-1f)); }

        [Fact] public void StrideFactor_is_leg_length_overall_times_legs() => Assert.Equal(6f, BodyParts.StrideFactor(3f, 2f));
        [Fact] public void StrideFactor_normal_is_1() => Assert.Equal(1f, BodyParts.StrideFactor(1f, 1f));
        [Fact] public void StrideFactor_capped_at_size_range() { Assert.Equal(10f, BodyParts.StrideFactor(10f, 10f)); Assert.Equal(0.25f, BodyParts.StrideFactor(0.25f, 0.25f)); }
        // A leg chain from the sole up to the hips: (vertical of each segment as it is now, size factor of the bone it hangs from).
        // Legs 2 standing: sole -0.1 (foot 1x), shin -0.8 and thigh -0.8 (both 2x, vanilla 0.4 each), hip joint -0.1 (1x).
        private static readonly (float, float)[] Legs2Standing = { (-0.1f, 1f), (-0.8f, 2f), (-0.8f, 2f), (-0.1f, 1f) };

        [Fact] public void CurrentVertical_sums_segments() => Assert.Equal(-1.8f, BodyParts.CurrentVertical(Legs2Standing), 5);
        [Fact] public void VanillaVertical_divides_each_segment_by_its_size() => Assert.Equal(-1.0f, BodyParts.VanillaVertical(Legs2Standing), 5);

        [Fact] public void GroundOffset_long_legs_lift_body_by_extra_leg_length()
            => Assert.Equal(0.8f, BodyParts.GroundOffset(new[] { (-1.8f, -1.0f) }), 5);

        [Fact] public void GroundOffset_keeps_lowest_foot_on_ground() // walking: one foot raised
            => Assert.Equal(0.8f, BodyParts.GroundOffset(new[] { (-1.8f, -1.0f), (-1.2f, -0.7f) }), 5);

        [Fact] public void GroundOffset_lying_down_is_0() // in bed legs are horizontal: no vertical, no lift
        {
            var lying = new[] { (0f, 1f), (0.01f, 10f), (-0.01f, 10f), (0f, 1f) };
            Assert.Equal(0f, BodyParts.GroundOffset(new[] { (BodyParts.CurrentVertical(lying), BodyParts.VanillaVertical(lying)) }), 2);
        }

        [Fact] public void GroundOffset_big_feet_stand_on_their_soles() // Feet 10: sole 1.0 below the ankle instead of 0.1
        {
            var feet10 = new[] { (-1.0f, 10f), (-0.4f, 1f), (-0.4f, 1f), (-0.1f, 1f) };
            Assert.Equal(0.9f, BodyParts.GroundOffset(new[] { (BodyParts.CurrentVertical(feet10), BodyParts.VanillaVertical(feet10)) }), 5);
        }

        [Fact] public void GroundOffset_wide_hips_lift_body() // Hips 10: hip joints 1.0 below the pelvis instead of 0.1
        {
            var hips10 = new[] { (-0.1f, 1f), (-0.4f, 1f), (-0.4f, 1f), (-1.0f, 10f) };
            Assert.Equal(0.9f, BodyParts.GroundOffset(new[] { (BodyParts.CurrentVertical(hips10), BodyParts.VanillaVertical(hips10)) }), 5);
        }

        [Fact] public void GroundOffset_no_legs_is_0() => Assert.Equal(0f, BodyParts.GroundOffset(new (float, float)[0]));

        [Fact] public void Reach_adds_eye_lift() => Assert.Equal(13f, BodyParts.Reach(5f, 1f, 8f), 5);
        [Fact] public void Reach_tiny_keeps_vanilla_and_ignores_lowered_eye() => Assert.Equal(5f, BodyParts.Reach(5f, 0.25f, -0.3f), 5);
        [Fact] public void Clamp_max_is_10() => Assert.Equal(10f, BodyParts.Max);
        [Fact] public void Clamp_keeps_values_up_to_10() => Assert.Equal(7.5f, BodyParts.Clamp(7.5f));
        [Fact] public void Clamp_high() => Assert.Equal(10f, BodyParts.Clamp(100f));

        [Fact] public void Clamp_nonFinite_isOne() { Assert.Equal(1f, BodyParts.Clamp(float.NaN)); Assert.Equal(1f, BodyParts.Clamp(float.PositiveInfinity)); }

        [Fact] public void ZdoKey_format() { Assert.Equal("oddhatter.resizeplayer.head", BodyParts.ZdoKey(Part.Head)); Assert.Equal("oddhatter.resizeplayer.overall", BodyParts.ZdoKey(Part.Overall)); }

        [Fact] public void LocalFactor_ratio() { Assert.Equal(2f, BodyParts.LocalFactor(2f, 1f)); Assert.Equal(0.5f, BodyParts.LocalFactor(1f, 2f)); }

        [Fact] public void LocalFactor_extremes() { Assert.Equal(40f, BodyParts.LocalFactor(10f, 0.25f)); Assert.Equal(1f / 40f, BodyParts.LocalFactor(0.25f, 10f), 5); }

        [Fact] public void ReachFactor_tiny_keeps_vanilla_reach() => Assert.Equal(1f, BodyParts.ReachFactor(0.25f));
        [Fact] public void ReachFactor_giant_grows() => Assert.Equal(3f, BodyParts.ReachFactor(3f));

        // Gravity follows stride, so falls look like vanilla at any size (a giant falls its own height in the vanilla time).
        [Fact] public void ExtraGravity_giant_adds_gravity() => Assert.Equal(9f, BodyParts.ExtraGravity(10f), 5);
        // A tiny player in full gravity plummeted, in quarter gravity it floated too slowly: it gets sqrt(stride), half at 0.25.
        [Fact] public void ExtraGravity_tiny_removes_some_gravity() => Assert.Equal(-0.5f, BodyParts.ExtraGravity(0.25f), 5);
        [Fact] public void ExtraGravity_normal_is_0() => Assert.Equal(0f, BodyParts.ExtraGravity(1f));

        [Fact] public void JumpHeight_giant_jumps_stride_times_as_high() => Assert.Equal(4f, BodyParts.JumpHeight(4f), 5);
        // A tiny player still jumps the vanilla height (small animals jump about as high as big ones), so it can get onto full-size steps.
        [Fact] public void JumpHeight_tiny_keeps_vanilla_jump() => Assert.Equal(1f, BodyParts.JumpHeight(0.25f));

        // Take-off speed for that height under that gravity: height = v^2 / 2g.
        [Theory, InlineData(0.25f), InlineData(0.5f), InlineData(1f), InlineData(3f), InlineData(10f)]
        public void JumpFactor_reaches_jump_height_under_scaled_gravity(float stride)
        {
            float v = BodyParts.JumpFactor(stride), g = 1f + BodyParts.ExtraGravity(stride);
            Assert.Equal(BodyParts.JumpHeight(stride), v * v / g, 4);
        }

        // Foot IK plants the ankle a fixed 0.1 m above the ground; a quarter-size foot's ankle is 0.025 m up, so the vanilla
        // value lifts the feet and bends the knees (the tiny player looks crouched).
        [Fact] public void FootIkDistance_tiny_foot_plants_ankle_lower() => Assert.Equal(0.025f, BodyParts.FootIkDistance(0.1f, 0.25f), 5);
        [Fact] public void FootIkDistance_giant_foot_plants_ankle_higher() => Assert.Equal(1f, BodyParts.FootIkDistance(0.1f, 10f), 5);

        // The feather cape caps falling speed in m/s; it is also applied on the ground, so it must follow stride like run speed.
        [Fact] public void MaxFallSpeed_giant_on_ground_scales_with_stride() => Assert.Equal(50f, BodyParts.MaxFallSpeed(5f, 10f, true), 4);
        // In the air the cape should still float a giant down: stride x the cap fell too fast (the world isn't scaled).
        // sqrt(stride) keeps it gentle while a fast giant running off a crest still doesn't glide far.
        [Fact] public void MaxFallSpeed_giant_in_air_scales_with_sqrt_stride() => Assert.Equal(10f, BodyParts.MaxFallSpeed(5f, 4f, false), 4);
        [Fact] public void MaxFallSpeed_tiny_in_air_scales_with_stride() => Assert.Equal(1.25f, BodyParts.MaxFallSpeed(5f, 0.25f, false), 4);
        [Fact] public void MaxFallSpeed_tiny_on_ground_scales_with_stride() => Assert.Equal(1.25f, BodyParts.MaxFallSpeed(5f, 0.25f, true), 4);

        [Fact] public void LandingAltitude_giant_fall_counts_as_shorter() => Assert.Equal(101f, BodyParts.LandingAltitude(110f, 100f, 10f), 4);

        // Falls count in jump heights: a tiny player jumps the vanilla height, so it must land its own jump unhurt.
        [Fact] public void LandingAltitude_tiny_counts_like_vanilla() => Assert.Equal(101f, BodyParts.LandingAltitude(101f, 100f, 0.25f), 4);

        [Fact] public void LandingAltitude_normal_unchanged() => Assert.Equal(107f, BodyParts.LandingAltitude(107f, 100f, 1f));

        [Fact] public void LandingAltitude_no_fall_is_no_fall() => Assert.Equal(100f, BodyParts.LandingAltitude(99f, 100f, 0.25f));

        // The game hides the player when the camera is within 2 m of their feet; a tiny player's camera is closer than that.
        [Fact] public void HideDistance_tiny_shrinks_with_size() => Assert.Equal(0.5f, BodyParts.HideDistance(0.25f), 5);
        [Fact] public void HideDistance_normal_and_giant_keep_vanilla() { Assert.Equal(2f, BodyParts.HideDistance(1f)); Assert.Equal(2f, BodyParts.HideDistance(10f)); }

        // Swimming starts at (swim depth - 0.4 m) of water; swim depth is already scaled, the 0.4 margin must be too.
        [Fact] public void SwimThreshold_tiny_needs_proportional_water() => Assert.Equal(0.4f, BodyParts.SwimThreshold(2f * 0.25f, 0.25f), 5);
        [Fact] public void SwimThreshold_normal_is_vanilla() => Assert.Equal(1.6f, BodyParts.SwimThreshold(2f, 1f), 5);
        [Fact] public void SwimThreshold_giant_scales() => Assert.Equal(16f, BodyParts.SwimThreshold(20f, 10f), 4);

        // The camera pivot follows a pose-dependent lift; low-pass it so a long-legged run cycle doesn't shake the camera.
        [Fact] public void SmoothToward_no_time_no_change() => Assert.Equal(1f, BodyParts.SmoothToward(1f, 5f, 0f, 0.5f));
        [Fact] public void SmoothToward_one_time_constant_covers_63_percent() => Assert.Equal(1f + 4f * 0.632121f, BodyParts.SmoothToward(1f, 5f, 0.5f, 0.5f), 4);
        [Fact] public void SmoothToward_long_wait_arrives() => Assert.Equal(5f, BodyParts.SmoothToward(1f, 5f, 100f, 0.5f), 4);
        [Fact] public void SmoothToward_step_rate_wobble_is_damped() // 0.15 s half-step toward +1 m moves the eye < 30 cm
            => Assert.True(BodyParts.SmoothToward(0f, 1f, 0.15f, 0.5f) < 0.3f);

        // Ankle height above the ground in the Player prefab's rest pose; anything implausible falls back to the ankle.
        [Fact] public void RestSoleDepth_plausible_is_kept() => Assert.Equal(0.08f, BodyParts.RestSoleDepth(0.08f));
        [Fact] public void RestSoleDepth_below_ground_or_too_high_is_0() { Assert.Equal(0f, BodyParts.RestSoleDepth(-0.2f)); Assert.Equal(0f, BodyParts.RestSoleDepth(0.9f)); Assert.Equal(0f, BodyParts.RestSoleDepth(float.NaN)); }

        // Wading drag is speed - speed^2 * depthFactor * drag, so at 10x speed it cancels all movement (zero at 20 m/s).
        // Apply it at vanilla scale: the giant slows by the same fraction as a vanilla player in proportionally deep water.
        [Fact] public void WadingSpeed_normal_is_vanilla() => Assert.Equal(7f - 49f * 0.05f, BodyParts.WadingSpeed(7f, 1f, 1.8f, 0.05f, 1f, 1f), 4);
        [Fact] public void WadingSpeed_shallow_normal_is_vanilla() => Assert.Equal(7f - 49f * (0.3f / 0.6f) * 0.05f, BodyParts.WadingSpeed(7f, 0.3f, 1.8f, 0.05f, 1f, 1f), 4);
        [Fact] public void WadingSpeed_giant_keeps_moving() => Assert.Equal(10f * (7f - 49f * 0.05f), BodyParts.WadingSpeed(70f, 10f, 1.8f, 0.05f, 10f, 10f), 3);
        [Fact] public void WadingSpeed_giant_in_ankle_water_barely_slows() // 1 m is 1/6 of a 10x player's third-of-height
            => Assert.Equal(10f * (7f - 49f * (1f / 6f) * 0.05f), BodyParts.WadingSpeed(70f, 1f, 1.8f, 0.05f, 10f, 10f), 3);
        [Fact] public void WadingSpeed_tiny_slows_proportionally() => Assert.Equal(0.25f * (7f - 49f * 0.05f), BodyParts.WadingSpeed(1.75f, 0.25f, 1.8f, 0.05f, 0.25f, 0.25f), 4);
        [Fact] public void WadingSpeed_dry_is_unchanged() => Assert.Equal(70f, BodyParts.WadingSpeed(70f, 0f, 1.8f, 0.05f, 10f, 10f));

        [Fact] public void All_hasEveryPartOnce() => Assert.Equal(Enum.GetValues(typeof(Part)).Length, BodyParts.All.Distinct().Count());

        [Fact] public void PartValues_identity_isAllOne() { var v = PartValues.Identity(); Assert.All(BodyParts.All, p => Assert.Equal(1f, v[p])); }

        // Particle effects on equipped items: a Local-scaling-mode system sizes itself by its own transform scale only.
        [Fact] public void EffectScale_local_system_takes_the_item_size() => Assert.Equal(0.25f, BodyParts.EffectScale(0.25f, 0, true));
        [Fact] public void EffectScale_local_system_ignores_scaled_local_parents() => Assert.Equal(4f, BodyParts.EffectScale(4f, 2, true));
        [Fact] public void EffectScale_other_transform_unchanged_when_no_local_parent() => Assert.Equal(1f, BodyParts.EffectScale(4f, 0, false));
        [Fact] public void EffectScale_child_of_scaled_local_system_cancels_it() => Assert.Equal(0.25f, BodyParts.EffectScale(4f, 1, false));
        [Fact] public void EffectScale_child_under_two_scaled_local_systems_cancels_both() => Assert.Equal(1f / 16f, BodyParts.EffectScale(4f, 2, false), 6);
        [Fact] public void EffectOffset_unchanged_when_no_local_parent() => Assert.Equal(1f, BodyParts.EffectOffset(4f, 0));
        [Fact] public void EffectOffset_cancels_scaled_local_parents() => Assert.Equal(1f / 16f, BodyParts.EffectOffset(4f, 2), 6);

        [Fact]
        public void PartValues_approxEquals()
        {
            var a = PartValues.Identity();
            var b = PartValues.Identity();
            b[Part.Hips] = 1.00005f;
            Assert.True(a.ApproximatelyEquals(b));
            b[Part.Hips] = 1.1f;
            Assert.False(a.ApproximatelyEquals(b));
            Assert.False(a.ApproximatelyEquals(null));
        }

        [Fact]
        public void PartValues_copyFrom_isIndependent()
        {
            var a = PartValues.Identity();
            var b = PartValues.Identity();
            b[Part.Head] = 3f;
            a.CopyFrom(b);
            b[Part.Head] = 1f;
            Assert.Equal(3f, a[Part.Head]);
        }
    }
}
