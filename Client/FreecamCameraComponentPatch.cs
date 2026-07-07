using HarmonyLib;
using Koenigz.PerfectCulling.EFT;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    [HarmonyPatch]
    public static class FreecamCameraComponentPatch
    {
        private const string FreecamCameraName = "MLE_FreeCam";

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ThermalVision), "OnDisable")]
        public static bool SkipThermalVisionOnDisable(ThermalVision __instance)
        {
            return !IsFreecam(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ThermalVision), "OnPreCull")]
        public static bool SkipThermalVisionOnPreCull(ThermalVision __instance)
        {
            return !IsFreecam(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ThermalVision), "OnDestroy")]
        public static bool SkipThermalVisionOnDestroy(ThermalVision __instance)
        {
            return !IsFreecam(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PerfectCullingCrossSceneSampler), "Start")]
        public static bool SkipPerfectCullingStart(PerfectCullingCrossSceneSampler __instance)
        {
            return !IsFreecam(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PerfectCullingCrossSceneSampler), "OnDestroy")]
        public static bool SkipPerfectCullingOnDestroy(PerfectCullingCrossSceneSampler __instance)
        {
            return !IsFreecam(__instance);
        }

        private static bool IsFreecam(MonoBehaviour component)
        {
            return component != null && component.gameObject.name == FreecamCameraName;
        }
    }
}
