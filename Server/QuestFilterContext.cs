namespace MapLootEditorLite.Server;

public static class QuestFilterContext
{
    private static readonly System.Threading.AsyncLocal<string?> _currentSessionId = new();

    public static string? CurrentSessionId
    {
        get => _currentSessionId.Value;
        set => _currentSessionId.Value = value;
    }
}
