using EFT;
using HarmonyLib;

namespace MapLootEditorLite.Client
{
    [HarmonyPatch(typeof(GameWorld), "OnDestroy")]
    public static class RaidEndResetPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            Plugin.Log.LogInfo("[MLEL Runtime] GameWorld.OnDestroy detected, resetting raid state.");
            RuntimeStaticObjectSpawner.Instance?.ResetState();
            RuntimeInteractiveObjectSpawner.Instance?.ResetState();
            RuntimeExtractZoneSpawner.Instance?.ResetState();
            RuntimeBotSpawnSpawner.Instance?.ResetState();
            RuntimeLightZoneSpawner.Instance?.ResetState();
            MapEditorController.Instance?.ResetState();
        }
    }
}
