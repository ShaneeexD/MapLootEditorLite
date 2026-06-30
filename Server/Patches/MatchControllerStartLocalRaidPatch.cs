using System.Reflection;
using HarmonyLib;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;

namespace MapLootEditorLite.Server.Patches;

public class MatchControllerStartLocalRaidPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(MatchController), nameof(MatchController.StartLocalRaid));
    }

    [HarmonyPrefix]
    public static void Prefix(MongoId sessionId, StartLocalRaidRequestData request)
    {
        QuestFilterContext.CurrentSessionId = sessionId.ToString();
    }

    [HarmonyPostfix]
    public static void Postfix()
    {
        QuestFilterContext.CurrentSessionId = null;
    }
}
