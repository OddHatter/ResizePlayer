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
            if (__result == null || joint == null || __result.transform.parent != joint)
            {
                return; // skinned items (armor, capes) deform with the bones instead
            }
            PlayerScaler scaler = __instance.GetComponentInParent<PlayerScaler>();
            if (scaler == null)
            {
                return;
            }
            float factor = scaler.ScaleFactorAt(joint);
            if (Mathf.Abs(factor - 1f) > 1e-4f)
            {
                __result.transform.localScale *= factor;
            }
        }
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
