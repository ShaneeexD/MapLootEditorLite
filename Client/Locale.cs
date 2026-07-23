using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MapLootEditorLite.Client
{
    public static class Locale
    {
        private static readonly Dictionary<string, string> _strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static string Current { get; private set; } = "en";
        public static string CurrentName { get; private set; } = "English";

        public static void Initialize(string language, string directory)
        {
            Current = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim();
            _strings.Clear();

            Directory.CreateDirectory(directory);
            var defaultPath = Path.Combine(directory, "en.json");
            if (!File.Exists(defaultPath))
                WriteDefaultEnglishFile(defaultPath);

            var path = Path.Combine(directory, $"{Current}.json");
            if (!File.Exists(path) && !string.Equals(Current, "en", StringComparison.OrdinalIgnoreCase))
            {
                Plugin.Log.LogWarning($"Localization '{Current}' not found at {path}; falling back to en.json");
                path = defaultPath;
            }

            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var jObj = JsonConvert.DeserializeObject<JObject>(json);
                    if (jObj != null)
                    {
                        CurrentName = jObj["_name"]?.ToString() ?? Current;
                        foreach (var prop in jObj.Properties())
                        {
                            if (prop.Name == "_name")
                                continue;
                            _strings[prop.Name] = prop.Value?.ToString() ?? string.Empty;
                        }
                    }
                    Plugin.Log.LogInfo($"Loaded localization '{CurrentName}' ({_strings.Count} entries)");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Failed to load localization {path}: {ex.Message}");
                }
            }
            else
            {
                Plugin.Log.LogInfo($"No localization file found at {path}; using English UI text.");
            }
        }

        public static List<(string code, string name)> GetAvailableLanguages(string directory)
        {
            var result = new List<(string code, string name)>();
            if (!Directory.Exists(directory))
                return result;

            foreach (var file in Directory.GetFiles(directory, "*.json"))
            {
                var code = Path.GetFileNameWithoutExtension(file);
                var name = code;
                try
                {
                    var json = File.ReadAllText(file);
                    var jObj = JsonConvert.DeserializeObject<JObject>(json);
                    if (jObj != null && jObj["_name"] != null)
                        name = jObj["_name"].ToString();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Failed to read language file {file}: {ex.Message}");
                }
                result.Add((code, string.IsNullOrWhiteSpace(name) ? code : name));
            }
            return result.OrderBy(x => x.code, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string Get(string key, string fallback = null)
        {
            if (string.IsNullOrEmpty(key))
                return fallback ?? string.Empty;
            if (_strings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                return value;
            return fallback ?? key;
        }

                private static void WriteDefaultEnglishFile(string path)
        {
            var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            defaults["_name"] = "English";
            defaults["-"] = "-";
            defaults["%"] = "%";
            defaults["[Del] Delete  |  [Ctrl+Z] Undo  |  [Ctrl+Y] Redo  |  [Ctrl+C] Copy  |  [Ctrl+V] Paste  |  [1] Move  |  [2] Rotate  |  [3] Scale  |  [F] Focus  |  [MMB] Toggle Cursor"] = "[Del] Delete  |  [Ctrl+Z] Undo  |  [Ctrl+Y] Redo  |  [Ctrl+C] Copy  |  [Ctrl+V] Paste  |  [1] Move  |  [2] Rotate  |  [3] Scale  |  [F] Focus  |  [MMB] Toggle Cursor";
            defaults["<"] = "<";
            defaults[">"] = ">";
            defaults["Add"] = "Add";
            defaults["Add Group"] = "Add Group";
            defaults["Add Item"] = "Add Item";
            defaults["Add Requirement"] = "Add Requirement";
            defaults["Add Spawn"] = "Add Spawn";
            defaults["AIFollowEvent"] = "AIFollowEvent";
            defaults["Airdrop"] = "Airdrop";
            defaults["Allowed Side"] = "Allowed Side";
            defaults["Amount"] = "Amount";
            defaults["Any"] = "Any";
            defaults["►"] = "►";
            defaults["✕"] = "✕";
            defaults["Apply"] = "Apply";
            defaults["Apply Preview"] = "Apply Preview";
            defaults["Assign"] = "Assign";
            defaults["Assign Group to Selection"] = "Assign Group to Selection";
            defaults["Blocker"] = "Blocker";
            defaults["Blocker Name"] = "Blocker Name";
            defaults["Bot Spawn Chance"] = "Bot Spawn Chance";
            defaults["Bot Spawn Point"] = "Bot Spawn Point";
            defaults["Bot Spawn Zone"] = "Bot Spawn Zone";
            defaults["Bot Type"] = "Bot Type";
            defaults["Bot Zone Name"] = "Bot Zone Name";
            defaults["botkillzone"] = "botkillzone";
            defaults["Box"] = "Box";
            defaults["bundle"] = "bundle";
            defaults["Bundle Name"] = "Bundle Name";
            defaults["Cancel"] = "Cancel";
            defaults["Capsule"] = "Capsule";
            defaults["Chance"] = "Chance";
            defaults["Check Interval"] = "Check Interval";
            defaults["Clear"] = "Clear";
            defaults["Clear Group from Selection"] = "Clear Group from Selection";
            defaults["Clear Prev"] = "Clear Prev";
            defaults["Clear Source"] = "Clear Source";
            defaults["Click an object in the world"] = "Click an object in the world";
            defaults["Click an object to select it"] = "Click an object to select it";
            defaults["Click object in world..."] = "Click object in world...";
            defaults["clone"] = "clone";
            defaults["Clone to Pack"] = "Clone to Pack";
            defaults["Color A"] = "Color A";
            defaults["Color B"] = "Color B";
            defaults["Color G"] = "Color G";
            defaults["Color R"] = "Color R";
            defaults["Common"] = "Common";
            defaults["Confirm"] = "Confirm";
            defaults["Container"] = "Container";
            defaults["Container Id"] = "Container Id";
            defaults["Container Template"] = "Container Template";
            defaults["Cooldown (sec)"] = "Cooldown (sec)";
            defaults["Coop"] = "Coop";
            defaults["Copy"] = "Copy";
            defaults["Copy WTT Static Data"] = "Copy WTT Static Data";
            defaults["Copy Zone Data"] = "Copy Zone Data";
            defaults["Count"] = "Count";
            defaults["Culling Object Radius"] = "Culling Object Radius";
            defaults["Custom"] = "Custom";
            defaults["Custom Container"] = "Custom Container";
            defaults["Custom Door"] = "Custom Door";
            defaults["Custom Stationary Weapon"] = "Custom Stationary Weapon";
            defaults["Cut Volume Name"] = "Cut Volume Name";
            defaults["Cylinder"] = "Cylinder";
            defaults["Debug"] = "Debug";
            defaults["Default"] = "Default";
            defaults["Del"] = "Del";
            defaults["Delay"] = "Delay";
            defaults["Delay (sec)"] = "Delay (sec)";
            defaults["Delete"] = "Delete";
            defaults["Delete selected marker(s)?"] = "Delete selected marker(s)?";
            defaults["Directional"] = "Directional";
            defaults["Disable"] = "Disable";
            defaults["Disable Camera Occlusion"] = "Disable Camera Occlusion";
            defaults["Disable Culling Objects"] = "Disable Culling Objects";
            defaults["Door"] = "Door";
            defaults["Dump door IDs"] = "Dump door IDs";
            defaults["Dump keyed door IDs"] = "Dump keyed door IDs";
            defaults["Duplicate"] = "Duplicate";
            defaults["Editor Mode"] = "Editor Mode";
            defaults["Empty"] = "Empty";
            defaults["EmptyOrSize"] = "EmptyOrSize";
            defaults["Enable"] = "Enable";
            defaults["Enabled"] = "Enabled";
            defaults["Enter pack name:"] = "Enter pack name:";
            defaults["Excluded Statuses"] = "Excluded Statuses";
            defaults["Exfil Time"] = "Exfil Time";
            defaults["Exfil Type"] = "Exfil Type";
            defaults["Exit Name"] = "Exit Name";
            defaults["ExitActivate"] = "ExitActivate";
            defaults["Export"] = "Export";
            defaults["Export As"] = "Export As";
            defaults["Extract Zone"] = "Extract Zone";
            defaults["Filter"] = "Filter";
            defaults["Flare Type"] = "Flare Type";
            defaults["flarezone"] = "flarezone";
            defaults["Force Player Spawn (off = bot/any)"] = "Force Player Spawn (off = bot/any)";
            defaults["Force Shadows"] = "Force Shadows";
            defaults["Forced"] = "Forced";
            defaults["From List"] = "From List";
            defaults["GameObjects"] = "GameObjects";
            defaults["Gen"] = "Gen";
            defaults["Go"] = "Go";
            defaults["Group"] = "Group";
            defaults["Groups"] = "Groups";
            defaults["Hard"] = "Hard";
            defaults["HasItem"] = "HasItem";
            defaults["Hierarchy"] = "Hierarchy";
            defaults["Hybrid"] = "Hybrid";
            defaults["Import"] = "Import";
            defaults["Individual"] = "Individual";
            defaults["Inspector"] = "Inspector";
            defaults["Intensity"] = "Intensity";
            defaults["Interactive"] = "Interactive";
            defaults["Invert (keep inside)"] = "Invert (keep inside)";
            defaults["Item Tpl"] = "Item Tpl";
            defaults["Items (chance does not need to add to 100):"] = "Items (chance does not need to add to 100):";
            defaults["Key Template Id"] = "Key Template Id";
            defaults["Key Template Id (optional)"] = "Key Template Id (optional)";
            defaults["Light"] = "Light";
            defaults["Light Action"] = "Light Action";
            defaults["Light Zone"] = "Light Zone";
            defaults["Light Zone Actions"] = "Light Zone Actions";
            defaults["Link Lights"] = "Link Lights";
            defaults["Linked Quest Id"] = "Linked Quest Id";
            defaults["Load"] = "Load";
            defaults["Loot Mode"] = "Loot Mode";
            defaults["Loot Spawn"] = "Loot Spawn";
            defaults["Loot Zone"] = "Loot Zone";
            defaults["Manage Renderers"] = "Manage Renderers";
            defaults["Manual"] = "Manual";
            defaults["Map Loot Editor Lite"] = "Map Loot Editor Lite";
            defaults["Max"] = "Max";
            defaults["Max distance to render vanilla gizmos (0 = unlimited):"] = "Max distance to render vanilla gizmos (0 = unlimited):";
            defaults["Max Raid Time"] = "Max Raid Time";
            defaults["Max Visible Distance"] = "Max Visible Distance";
            defaults["Min Raid Time"] = "Min Raid Time";
            defaults["Mode"] = "Mode";
            defaults["Name"] = "Name";
            defaults["No groups."] = "No groups.";
            defaults["No marker selected."] = "No marker selected.";
            defaults["No messages yet."] = "No messages yet.";
            defaults["No prefabs saved yet."] = "No prefabs saved yet.";
            defaults["No selection"] = "No selection";
            defaults["No vanilla objects marked for removal."] = "No vanilla objects marked for removal.";
            defaults["None"] = "None";
            defaults["OK"] = "OK";
            defaults["Once Per Player"] = "Once Per Player";
            defaults["One Time"] = "One Time";
            defaults["Other"] = "Other";
            defaults["Output"] = "Output";
            defaults["Pack"] = "Pack";
            defaults["Passage Requirement"] = "Passage Requirement";
            defaults["Pick from Scene"] = "Pick from Scene";
            defaults["Place Here"] = "Place Here";
            defaults["placeitem"] = "placeitem";
            defaults["Pmc"] = "Pmc";
            defaults["PMC Spawn Zone"] = "PMC Spawn Zone";
            defaults["Point"] = "Point";
            defaults["Position"] = "Position";
            defaults["Potential"] = "Potential";
            defaults["Prefab Name"] = "Prefab Name";
            defaults["Prefab Path"] = "Prefab Path";
            defaults["Prefabs"] = "Prefabs";
            defaults["Preset"] = "Preset";
            defaults["Prev"] = "Prev";
            defaults["Preview Light"] = "Preview Light";
            defaults["Preview Object"] = "Preview Object";
            defaults["Preview Random"] = "Preview Random";
            defaults["Preview Random In Zone"] = "Preview Random In Zone";
            defaults["Preview Spawn"] = "Preview Spawn";
            defaults["Preview Spawns"] = "Preview Spawns";
            defaults["Quest"] = "Quest";
            defaults["Quest completed"] = "Quest completed";
            defaults["Quest ID"] = "Quest ID";
            defaults["Quest Must Exist"] = "Quest Must Exist";
            defaults["Quest only"] = "Quest only";
            defaults["QuestActive"] = "QuestActive";
            defaults["QuestCompleted"] = "QuestCompleted";
            defaults["R"] = "R";
            defaults["Radius"] = "Radius";
            defaults["Random Groups (one picked per raid):"] = "Random Groups (one picked per raid):";
            defaults["Random Rotation"] = "Random Rotation";
            defaults["Range"] = "Range";
            defaults["Raycast Cull"] = "Raycast Cull";
            defaults["Raycast Mask"] = "Raycast Mask";
            defaults["Reference"] = "Reference";
            defaults["Refresh"] = "Refresh";
            defaults["Remove"] = "Remove";
            defaults["Removed"] = "Removed";
            defaults["Ren"] = "Ren";
            defaults["Renderer Radius"] = "Renderer Radius";
            defaults["Repeatable"] = "Repeatable";
            defaults["Required Boss"] = "Required Boss";
            defaults["Required Faction"] = "Required Faction";
            defaults["Required Item"] = "Required Item";
            defaults["Required Slot"] = "Required Slot";
            defaults["Required Statuses"] = "Required Statuses";
            defaults["Requirement Tip"] = "Requirement Tip";
            defaults["Requirements:"] = "Requirements:";
            defaults["Respawnable"] = "Respawnable";
            defaults["Restore"] = "Restore";
            defaults["Restore All"] = "Restore All";
            defaults["Rot"] = "Rot";
            defaults["Rotation"] = "Rotation";
            defaults["S"] = "S";
            defaults["salvage"] = "salvage";
            defaults["Save"] = "Save";
            defaults["Saved Prefabs (click to place)"] = "Saved Prefabs (click to place)";
            defaults["Scale"] = "Scale";
            defaults["Scan Scene"] = "Scan Scene";
            defaults["Scanning scene... please wait"] = "Scanning scene... please wait";
            defaults["Scatter"] = "Scatter";
            defaults["Scatter (select LootZone)"] = "Scatter (select LootZone)";
            defaults["Scav"] = "Scav";
            defaults["ScavCooperation"] = "ScavCooperation";
            defaults["Scene Object"] = "Scene Object";
            defaults["Search"] = "Search";
            defaults["SecretTransferItem"] = "SecretTransferItem";
            defaults["Select"] = "Select";
            defaults["Select an object"] = "Select an object";
            defaults["Select an object to view details."] = "Select an object to view details.";
            defaults["Set Source"] = "Set Source";
            defaults["Shadow Bias"] = "Shadow Bias";
            defaults["Shadow Normal Bias"] = "Shadow Normal Bias";
            defaults["Shadow Strength"] = "Shadow Strength";
            defaults["Shadows"] = "Shadows";
            defaults["Shape"] = "Shape";
            defaults["SharedTimer"] = "SharedTimer";
            defaults["Side"] = "Side";
            defaults["SkillLevel"] = "SkillLevel";
            defaults["Snap"] = "Snap";
            defaults["Snap to ground"] = "Snap to ground";
            defaults["Soft"] = "Soft";
            defaults["Spawn Chance"] = "Spawn Chance";
            defaults["Spawn Mode"] = "Spawn Mode";
            defaults["Spawn Type"] = "Spawn Type";
            defaults["Sphere"] = "Sphere";
            defaults["Spot"] = "Spot";
            defaults["Spot Angle"] = "Spot Angle";
            defaults["Start On"] = "Start On";
            defaults["Static Object"] = "Static Object";
            defaults["StationaryWeapon"] = "StationaryWeapon";
            defaults["Switch"] = "Switch";
            defaults["T"] = "T";
            defaults["Timer"] = "Timer";
            defaults["Toggle"] = "Toggle";
            defaults["Tools"] = "Tools";
            defaults["Tpl"] = "Tpl";
            defaults["Train"] = "Train";
            defaults["TransferItem"] = "TransferItem";
            defaults["Trigger activated"] = "Trigger activated";
            defaults["Trigger Chance"] = "Trigger Chance";
            defaults["Trigger Zone"] = "Trigger Zone";
            defaults["Trigger Zone Name"] = "Trigger Zone Name";
            defaults["Type"] = "Type";
            defaults["Use Gravity"] = "Use Gravity";
            defaults["Use Parent"] = "Use Parent";
            defaults["Vanilla"] = "Vanilla";
            defaults["Vanilla reference (read-only)"] = "Vanilla reference (read-only)";
            defaults["Vanilla Render Distance"] = "Vanilla Render Distance";
            defaults["Object Render Distance"] = "Object Render Distance";
            defaults["visit"] = "visit";
            defaults["Volume Name"] = "Volume Name";
            defaults["Weapon Template"] = "Weapon Template";
            defaults["WearsItem"] = "WearsItem";
            defaults["Wild Type"] = "Wild Type";
            defaults["WorldEvent"] = "WorldEvent";
            defaults["WTT"] = "WTT";
            defaults["WTT Quest Area"] = "WTT Quest Area";
            defaults["WTT Static Object"] = "WTT Static Object";
            defaults["X"] = "X";
            defaults["Y Offset"] = "Y Offset";
            defaults["Zone Id"] = "Zone Id";
            defaults["Zone Location"] = "Zone Location";
            defaults["Zone Name"] = "Zone Name";
            defaults["Zone Type"] = "Zone Type";
            defaults["Cut Volume"] = "Cut Volume";
            defaults["File"] = "File";
            defaults["Import Vanilla Loot"] = "Import Vanilla Loot";
            defaults["Language"] = "Language";
            defaults["Max Group Size"] = "Max Group Size";
            defaults["Max H"] = "Max H";
            defaults["meters"] = "meters";
            defaults["Min Group Size"] = "Min Group Size";
            defaults["Min H"] = "Min H";
            defaults["New name"] = "New name";
            defaults["Pack name"] = "Pack name";
            defaults["Players Count"] = "Players Count";
            defaults["Required Level"] = "Required Level";
            defaults["Spawn Count"] = "Spawn Count";
            defaults["Toggle Pack Gizmos"] = "Toggle Pack Gizmos";
            defaults["Toggle Vanilla Gizmos"] = "Toggle Vanilla Gizmos";
            defaults["type to filter..."] = "type to filter...";
            defaults["View"] = "View";
            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(defaults, Formatting.Indented));
                Plugin.Log.LogInfo($"Wrote default English localization to {path}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to write default localization: {ex.Message}");
            }
        }

    }
}
