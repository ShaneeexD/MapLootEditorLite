using System.Reflection;
using HarmonyLib;
using UltimateLootEditor.Shared;
using UltimateLootEditor.Util;

namespace UltimateLootEditor.Patching;

// Icebreaker suspends foreign loot patches only during its own raid generation.
// Exempt our bridge without disabling isolation or changing the map mod's files.
internal static class IcebreakerLootCompatibility
{
    internal static void Install(Harmony harmony)
    {
        var type = AccessTools.TypeByName("Manimal.Icebreaker.Server.IcebreakerLootFirewall");
        if (type == null) return;

        var collect = AccessTools.DeclaredMethod(type, "Collect");
        var parameters = collect?.GetParameters();
        if (collect == null || !collect.IsStatic || collect.ReturnType != typeof(void) ||
            parameters?.Length != 4 || parameters[1].ParameterType != typeof(MethodBase) ||
            parameters[2].ParameterType != typeof(IEnumerable<Patch>) ||
            parameters[3].ParameterType != typeof(HarmonyPatchType))
        {
            Logger.Warn("[ULE] Icebreaker loot isolation API is unsupported; Icebreaker edits may not apply.");
            return;
        }

        try
        {
            harmony.Patch(collect, prefix: new HarmonyMethod(typeof(IcebreakerLootCompatibility), nameof(BeforeCollect)));
            Logger.Status("[ULE] Icebreaker compatibility enabled: ULE loot hooks remain active during isolated generation.");
        }
        catch (Exception ex)
        {
            Logger.Error("[ULE] Could not enable Icebreaker loot compatibility; Icebreaker edits may not apply", ex);
        }
    }

    private static void BeforeCollect(ref IEnumerable<Patch> __2)
    {
        if (__2 == null) return;
        __2 = __2.Where(patch => !(patch.owner == ModConstants.ServerModGuid &&
            patch.PatchMethod.DeclaringType == typeof(GenerateDynamicLootBridge)));
    }
}
