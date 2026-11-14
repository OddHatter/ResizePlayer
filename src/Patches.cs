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
}
