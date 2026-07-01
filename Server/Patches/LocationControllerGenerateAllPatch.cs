using System.Reflection;
using HarmonyLib;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;

namespace MapLootEditorLite.Server.Patches;

public class LocationControllerGenerateAllPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(LocationController), nameof(LocationController.GenerateAll));
    }

    [PatchPrefix]
    public static void Prefix(MongoId sessionId)
    {
        QuestFilterContext.CurrentSessionId = sessionId.ToString();
    }

    [PatchPostfix]
    public static void Postfix()
    {
        QuestFilterContext.CurrentSessionId = null;
    }
}
