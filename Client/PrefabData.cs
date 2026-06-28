using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace MapLootEditorLite.Client
{
    public class PrefabData
    {
        public string name = "Prefab";
        public string description = "";
        public string version = "1";
        public List<PrefabEntry> markers = new List<PrefabEntry>();
    }

    public class PrefabEntry
    {
        public string kind;
        public JObject data;
    }
}
