using System.Collections.Generic;
using System.Linq;

namespace MapLootEditorLite.Client
{
    public static class WttStaticDataConverter
    {
        public static object ToWttConfig(WTTStaticObject obj, string mapId)
        {
            return new
            {
                questId = obj.questId,
                locationID = mapId,
                bundleName = obj.bundleName,
                prefabName = obj.prefabName,
                position = new
                {
                    x = obj.position.x,
                    y = obj.position.y,
                    z = obj.position.z
                },
                rotation = new
                {
                    x = obj.rotation.x,
                    y = obj.rotation.y,
                    z = obj.rotation.z
                },
                requiredQuestStatuses = obj.requiredQuestStatuses ?? new List<string>(),
                excludedQuestStatuses = obj.excludedQuestStatuses ?? new List<string>(),
                questMustExist = obj.questMustExist,
                linkedQuestId = string.IsNullOrEmpty(obj.linkedQuestId) ? null : obj.linkedQuestId,
                linkedRequiredStatuses = obj.linkedRequiredStatuses ?? new List<string>(),
                linkedExcludedStatuses = obj.linkedExcludedStatuses ?? new List<string>(),
                linkedQuestMustExist = obj.linkedQuestMustExist,
                requiredItemInInventory = string.IsNullOrEmpty(obj.requiredItemInInventory) ? null : obj.requiredItemInInventory,
                requiredLevel = obj.requiredLevel == 0 ? (int?)null : obj.requiredLevel,
                requiredFaction = string.IsNullOrEmpty(obj.requiredFaction) ? null : obj.requiredFaction,
                requiredBossSpawned = string.IsNullOrEmpty(obj.requiredBossSpawned) ? null : obj.requiredBossSpawned
            };
        }
    }
}
