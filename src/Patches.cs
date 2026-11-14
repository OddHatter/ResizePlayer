using HarmonyLib;

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

    /// <summary>A giant needs to zoom out further to see itself; scales the max camera distance with Overall (never below vanilla).</summary>
    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateCamera))]
    internal static class GameCamera_UpdateCamera_Patch
    {
        private static GameCamera s_camera;
        private static float s_maxDistance;
        private static float s_maxDistanceBoat;

        private static void Prefix(GameCamera __instance)
        {
            if (__instance != s_camera)
            {
                s_camera = __instance;
                s_maxDistance = __instance.m_maxDistance;
                s_maxDistanceBoat = __instance.m_maxDistanceBoat;
            }
            float factor = BodyParts.ReachFactor(PlayerScaler.LocalOverall);
            __instance.m_maxDistance = s_maxDistance * factor;
            __instance.m_maxDistanceBoat = s_maxDistanceBoat * factor;
        }
    }
}
