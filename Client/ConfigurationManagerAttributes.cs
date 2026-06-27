#pragma warning disable 0169, 0414, 0649

using BepInEx.Configuration;

namespace MapLootEditorLite.Client
{
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? ShowRangeAsPercent;
        public System.Action<ConfigEntryBase> CustomDrawer;
        public bool? Browsable;
        public string Category;
        public object DefaultValue;
        public bool? HideDefaultButton;
        public bool? HideSettingName;
        public string Description;
        public string DispName;
        public int? Order;
        public bool? ReadOnly;
        public bool? IsAdvanced;
        public System.Func<object, string> ObjToStr;
        public System.Func<string, object> StrToObj;
    }
}
