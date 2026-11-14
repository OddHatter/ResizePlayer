using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Oddhatter.ResizePlayer
{
    /// <summary>Every player object, yours and other players', gets a PlayerScaler that sizes it each frame.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.Awake))]
    internal static class Player_Awake_Patch
    {
        private static void Postfix(Player __instance)
        {
            __instance.gameObject.AddComponent<PlayerScaler>();
        }
    }

    /// <summary>
    /// Keeps the camera framing the same at any size. The pivot (the eye point) already moves with the body; this scales
    /// the zoom: the current distance, its limits and the scroll speed follow Overall both ways, so a giant isn't viewed
    /// from inside its head and a tiny player isn't a speck. The near clip plane shrinks for a tiny player so the camera
    /// doesn't cut into them.
    /// </summary>
    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateCamera))]
    internal static class GameCamera_UpdateCamera_Patch
    {
        private static GameCamera s_camera;
        private static float s_minDistance;
        private static float s_maxDistance;
        private static float s_maxDistanceBoat;
        private static float s_zoomSens;
        private static float s_nearClipMin;
        private static float s_nearClipMax;
        private static float s_factor = 1f;

        private static void Prefix(GameCamera __instance)
        {
            if (__instance != s_camera)
            {
                s_camera = __instance;
                s_minDistance = __instance.m_minDistance;
                s_maxDistance = __instance.m_maxDistance;
                s_maxDistanceBoat = __instance.m_maxDistanceBoat;
                s_zoomSens = __instance.m_zoomSens;
                s_nearClipMin = __instance.m_nearClipPlaneMin;
                s_nearClipMax = __instance.m_nearClipPlaneMax;
                s_factor = 1f;
            }
            float factor = PlayerScaler.LocalOverall;
            if (factor == s_factor)
            {
                return;
            }
            __instance.m_distance *= factor / s_factor; // keep the player's chosen zoom, in proportion
            s_factor = factor;
            __instance.m_minDistance = s_minDistance * factor;
            __instance.m_maxDistance = s_maxDistance * factor;
            __instance.m_maxDistanceBoat = s_maxDistanceBoat * factor;
            __instance.m_zoomSens = s_zoomSens * factor;
            float clip = Mathf.Min(1f, factor);
            __instance.m_nearClipPlaneMin = s_nearClipMin * clip;
            __instance.m_nearClipPlaneMax = s_nearClipMax * clip;
        }
    }

    /// <summary>
    /// The game re-parents an equipped item (weapon, shield, helmet, hair, beard, back items) onto a joint while keeping
    /// its world size, so anything equipped while resized would come out at normal size. Scale it up by the size the
    /// joint has from this mod, as if it had been attached at normal size; after that it grows and shrinks with its joint.
    /// </summary>
    [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.AttachItem))]
    internal static class VisEquipment_AttachItem_Patch
    {
        private static void Postfix(VisEquipment __instance, GameObject __result, Transform joint)
        {
            if (__result != null && joint != null && __result.transform.parent == joint)
            {
                // Skinned items (armor, capes) are parented elsewhere and deform with the bones instead.
                AttachedItemScaling.ScaleToJoint(__instance, __result.transform);
            }
        }
    }

    /// <summary>
    /// Rigid armour pieces (chest, shoulder, leg, utility and trinket items with attach_&lt;joint&gt; parts) are
    /// instantiated on their joint keeping world size, like AttachItem. Same fix; skinned pieces are left alone.
    /// </summary>
    [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.AttachArmor))]
    internal static class VisEquipment_AttachArmor_Patch
    {
        private static void Postfix(VisEquipment __instance, List<GameObject> __result)
        {
            if (__result == null || __instance.m_bodyModel == null)
            {
                return;
            }
            Transform skinParent = __instance.m_bodyModel.transform.parent;
            foreach (GameObject item in __result)
            {
                if (item != null && item.transform.parent != skinParent)
                {
                    AttachedItemScaling.ScaleToJoint(__instance, item.transform);
                }
            }
        }
    }

    internal static class AttachedItemScaling
    {
        /// <summary>Multiplies an item just attached to a joint by the size this mod gives that joint.</summary>
        internal static void ScaleToJoint(VisEquipment equipment, Transform item)
        {
            PlayerScaler scaler = equipment.GetComponentInParent<PlayerScaler>();
            if (scaler == null || item.parent == null)
            {
                return;
            }
            float factor = scaler.ScaleFactorAt(item.parent);
            if (Mathf.Abs(factor - 1f) > 1e-4f)
            {
                item.localScale *= factor;
            }
        }
    }

    /// <summary>
    /// Fall damage (from 4 m, lethal at 20 m) counts the drop in jump heights: a giant jumps stride times as high, so it
    /// can jump off what it can jump onto. A tiny player jumps and falls like a vanilla one.
    /// UpdateGroundContact runs on the landing frame, reads the highest point of the fall, then resets it.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.UpdateGroundContact))]
    internal static class Character_UpdateGroundContact_Patch
    {
        private static void Prefix(Character __instance)
        {
            if (PlayerScaler.LocalStride != 1f && __instance.m_groundContact && __instance == Player.m_localPlayer)
            {
                __instance.m_maxAirAltitude = BodyParts.LandingAltitude(
                    __instance.m_maxAirAltitude, __instance.transform.position.y, PlayerScaler.LocalStride);
            }
        }
    }

    /// <summary>
    /// The game counts a collision as standing on the ground only if the contact is less than the collider's radius above
    /// the feet and below its centre, read from the collider unscaled. A giant's capsule touches a slope or a bump
    /// several metres up its rounded bottom, so it kept losing the ground (falling animation, no grip or control); a
    /// tiny player's counted walls and ceilings as ground. Measure against the capsule at the local player's size.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.OnCollisionStay))]
    internal static class Character_GroundContact_Patch
    {
        private static readonly MethodInfo s_radius = AccessTools.PropertyGetter(typeof(CapsuleCollider), nameof(CapsuleCollider.radius));
        private static readonly MethodInfo s_center = AccessTools.PropertyGetter(typeof(CapsuleCollider), nameof(CapsuleCollider.center));
        private static readonly MethodInfo s_scaledRadius = AccessTools.Method(typeof(Character_GroundContact_Patch), nameof(Radius));
        private static readonly MethodInfo s_scaledCenter = AccessTools.Method(typeof(Character_GroundContact_Patch), nameof(Center));

        internal static float Radius(CapsuleCollider collider) => collider.radius * FactorOf(collider);

        internal static Vector3 Center(CapsuleCollider collider) => collider.center * FactorOf(collider);

        // Runs for every character's every contact: compare references only. Only the owner runs the check, so only the
        // local player's own body needs its size.
        private static float FactorOf(CapsuleCollider collider)
        {
            Player local = Player.m_localPlayer;
            return (object)local != null && ReferenceEquals(collider, local.m_collider) ? PlayerScaler.LocalOverall : 1f;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int radii = 0, centers = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(s_radius))
                {
                    instruction.opcode = OpCodes.Call; // instance getter -> static helper taking the collider; keeps labels
                    instruction.operand = s_scaledRadius;
                    radii++;
                }
                else if (instruction.Calls(s_center))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = s_scaledCenter;
                    centers++;
                }
                yield return instruction;
            }
            if (radii != 1 || centers != 1)
            {
                ResizePlayerPlugin.Log.LogWarning(
                    $"Expected one collider radius and one centre in Character.OnCollisionStay, found {radii} and {centers}; a giant may keep losing the ground.");
            }
        }
    }

    /// <summary>
    /// Walking in water slows by speed squared (see BodyParts.WadingSpeed), which stops a fast giant dead. For the scaled
    /// local player compute it at vanilla scale; this replaces Character.ApplyLiquidResistance, tar rules included.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.ApplyLiquidResistance))]
    internal static class Character_LiquidResistance_Patch
    {
        private static bool Prefix(Character __instance, ref float speed)
        {
            if ((PlayerScaler.LocalStride == 1f && PlayerScaler.LocalOverall == 1f) || __instance != Player.m_localPlayer)
            {
                return true; // vanilla
            }
            float depth = __instance.InLiquidDepth();
            if (depth <= 0f || __instance.m_seman.HaveStatusEffect(SEMan.s_statusEffectTared))
            {
                return false;
            }
            float drag = __instance.m_tarLevel > __instance.m_waterLevel ? 0.1f : 0.05f;
            speed = BodyParts.WadingSpeed(speed, depth, __instance.m_collider.height, drag, PlayerScaler.LocalStride, PlayerScaler.LocalOverall);
            return false;
        }
    }

    /// <summary>
    /// The hover, interact, build and repair rays are cast 50 m from the camera. A giant's camera sits up to 10x
    /// further back, which would put the ground at its feet out of that ray's length; lengthen it with Overall.
    /// (Reach from the eye is still checked separately, so this only removes the camera's handicap.)
    /// </summary>
    [HarmonyPatch]
    internal static class Player_CameraRay_Patch
    {
        private const float VanillaRayLength = 50f;
        private static readonly MethodInfo s_rayLength = AccessTools.Method(typeof(Player_CameraRay_Patch), nameof(RayLength));

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Player), nameof(Player.FindHoverObject));
            yield return AccessTools.Method(typeof(Player), nameof(Player.PieceRayTest));
            yield return AccessTools.Method(typeof(Player), nameof(Player.RemovePiece));
            yield return AccessTools.Method(typeof(Player), nameof(Player.CopyPiece));
            yield return AccessTools.Method(typeof(Player), nameof(Player.UpdateWearNTearHover));
        }

        internal static float RayLength() => VanillaRayLength * Mathf.Max(1f, PlayerScaler.LocalOverall);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value && value == VanillaRayLength)
                {
                    instruction.opcode = OpCodes.Call; // keeps the instruction's labels
                    instruction.operand = s_rayLength;
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1)
            {
                ResizePlayerPlugin.Log.LogWarning(
                    $"Expected one 50 m camera ray in Player.{__originalMethod.Name}, found {replaced}; giants may not reach far from the camera.");
            }
        }
    }

    /// <summary>
    /// The game hides the local player when the camera is within 2 m of their feet (so it never clips through them). A
    /// tiny player's camera is always that close, so they'd be invisible at every zoom: shrink the distance with them.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.FixedUpdate))]
    internal static class Player_HideNearCamera_Patch
    {
        private const float VanillaHideDistance = 2f;
        private static readonly MethodInfo s_distance = AccessTools.Method(typeof(Vector3), nameof(Vector3.Distance));
        private static readonly MethodInfo s_hideDistance = AccessTools.Method(typeof(Player_HideNearCamera_Patch), nameof(HideDistance));

        internal static float HideDistance() => BodyParts.HideDistance(PlayerScaler.LocalOverall);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            CodeInstruction previous = null;
            foreach (CodeInstruction instruction in instructions)
            {
                if (previous != null && previous.Calls(s_distance) &&
                    instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value && value == VanillaHideDistance)
                {
                    instruction.opcode = OpCodes.Call; // keeps the instruction's labels
                    instruction.operand = s_hideDistance;
                    replaced++;
                }
                previous = instruction;
                yield return instruction;
            }
            if (replaced != 1)
            {
                ResizePlayerPlugin.Log.LogWarning(
                    $"Expected one camera hide distance in Player.FixedUpdate, found {replaced}; a tiny player may be invisible.");
            }
        }
    }

    /// <summary>
    /// Swimming starts in water deeper than (swim depth - 0.4 m). Swim depth follows Overall (see PlayerScaler), but the
    /// 0.4 m margin doesn't: at 0.25 the threshold would be 0 and a tiny player would swim in any puddle. Scale the margin.
    /// Only the local player: its own client decides when it swims.
    /// </summary>
    [HarmonyPatch(typeof(Character))]
    internal static class Character_SwimThreshold_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Character.InLiquidSwimDepth), new System.Type[0])]
        private static bool Current(Character __instance, ref bool __result)
        {
            if (!IsScaledLocal(__instance))
            {
                return true; // vanilla
            }
            __result = __instance.InLiquidDepth() > Threshold(__instance);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Character.InLiquidSwimDepth), typeof(float))]
        private static bool AtDepth(Character __instance, float depth, ref bool __result)
        {
            if (!IsScaledLocal(__instance))
            {
                return true; // vanilla
            }
            __result = depth > Threshold(__instance);
            return false;
        }

        private static bool IsScaledLocal(Character character)
            => PlayerScaler.LocalOverall != 1f && character == Player.m_localPlayer;

        private static float Threshold(Character character)
            => BodyParts.SwimThreshold(character.m_swimDepth, PlayerScaler.LocalOverall);
    }

    /// <summary>
    /// The game ignores a player's scale in GetRadius and GetHeight (players are never scaled in vanilla). Monsters
    /// start an attack when within range of the target's radius, and hits are placed on its capsule, so a giant's
    /// would be unreachable: give them the scaled capsule.
    /// </summary>
    [HarmonyPatch(typeof(Character))]
    internal static class Character_Size_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(Character.GetRadius))]
        private static void Radius(Character __instance, ref float __result) => __result *= OverallOf(__instance);

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Character.GetHeight))]
        private static void Height(Character __instance, ref float __result) => __result *= OverallOf(__instance);

        private static float OverallOf(Character character)
            => character is Player && character.TryGetComponent(out PlayerScaler scaler) ? scaler.RootFactor : 1f;
    }

    /// <summary>
    /// The locomotion animation picks walk/jog/run and its step rate from the speed it is given. The local player moves
    /// LocalStride times faster, so give the animation the vanilla speed: steps at the normal rate, each one longer.
    /// Goes through the synced animator, so other players see the same.
    /// </summary>
    [HarmonyPatch(typeof(ZSyncAnimation), nameof(ZSyncAnimation.SetFloat), typeof(int), typeof(float))]
    internal static class ZSyncAnimation_SetFloat_Patch
    {
        private static readonly int s_forwardSpeed = ZSyncAnimation.GetHash("forward_speed");
        private static readonly int s_sidewaySpeed = ZSyncAnimation.GetHash("sideway_speed");

        private static void Prefix(ZSyncAnimation __instance, int hash, ref float value)
        {
            if (__instance == PlayerScaler.LocalZAnim && PlayerScaler.LocalStride != 1f &&
                (hash == s_forwardSpeed || hash == s_sidewaySpeed))
            {
                value /= PlayerScaler.LocalStride;
            }
        }
    }
}
