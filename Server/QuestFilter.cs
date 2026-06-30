using System.Linq;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;

namespace MapLootEditorLite.Server;

public static class QuestFilter
{
    private static ProfileHelper? _profileHelper;

    public static void Initialize(ProfileHelper profileHelper)
    {
        _profileHelper = profileHelper;
    }

    public static bool IsQuestActive(string questId)
    {
        if (string.IsNullOrWhiteSpace(questId))
            return false;

        var sessionId = QuestFilterContext.CurrentSessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            ServerPlugin.Logger?.Warning($"[MLEL] Quest-only check for '{questId}' skipped: no current session ID available.");
            return false;
        }

        return IsQuestActive(sessionId, questId);
    }

    public static bool IsQuestActive(string sessionId, string questId)
    {
        if (_profileHelper == null)
            return false;

        try
        {
            var profile = _profileHelper.GetFullProfile(new MongoId(sessionId));
            var pmcData = profile?.CharacterData?.PmcData;
            if (pmcData?.Quests == null)
                return false;

            var quest = pmcData.Quests.FirstOrDefault(q => q.QId.ToString() == questId);
            if (quest == null)
                return false;

            return quest.Status == QuestStatusEnum.AvailableForStart
                || quest.Status == QuestStatusEnum.Started
                || quest.Status == QuestStatusEnum.AvailableForFinish;
        }
        catch (System.Exception ex)
        {
            ServerPlugin.Logger?.Error($"[MLEL] Failed to check quest status for '{questId}': {ex.Message}");
            return false;
        }
    }
}
