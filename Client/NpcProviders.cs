using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using EFT;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public enum NpcKind
    {
        Bot,
        Follower
    }

    public sealed class NpcOption
    {
        public string Source;
        public string Id;          // exact identifier stored in wildSpawnType
        public string Label;
        public NpcKind Kind;
        public string Category;
        public string BaseArchetype;
        public int EnumId;
        public bool Active;
        public bool TemplateRegistered;
        public List<string> FollowerIds = new List<string>();
    }

    public interface INpcProvider
    {
        string Name { get; }
        bool Detected { get; }
        bool IsLoading { get; }
        bool IsStale { get; }
        IList<NpcOption> Options { get; }
        bool Owns(string id);
        // Returns true when the id can be spawned right now; otherwise reason explains why.
        bool IsAvailable(string id, out string reason);
        NpcOption Find(string id);
        NpcOption FindKnown(string id);
        IEnumerator RefreshCoroutine(Action<bool> completed);
    }

    // Small registry so further optional NPC providers can be added without touching the editor or spawner.
    public static class NpcProviderRegistry
    {
        private static readonly List<INpcProvider> Providers = new List<INpcProvider> { new BotGenNpcProvider() };
        private static int _refreshGeneration;
        internal static readonly float[] StartupDelays = { 2f, 10f, 30f };

        public static IEnumerable<INpcProvider> All
        {
            get { return Providers; }
        }

        public static INpcProvider FindOwner(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Providers.FirstOrDefault(p => p.Owns(id));
        }

        public static IEnumerator RefreshAll(Action<bool> completed)
        {
            _refreshGeneration++;
            var successful = true;
            foreach (var provider in Providers)
            {
                var providerSucceeded = false;
                yield return provider.RefreshCoroutine(result => providerSucceeded = result);
                successful &= providerSucceeded;
            }
            completed?.Invoke(successful);
        }

        public static IEnumerator RefreshAtStartup(Action<bool> completed)
        {
            var generation = _refreshGeneration;
            for (var attempt = 0; attempt < StartupDelays.Length; attempt++)
            {
                yield return new WaitForSecondsRealtime(StartupDelays[attempt]);
                // A manual refresh supersedes the pending startup schedule, even if it fails.
                if (_refreshGeneration != generation)
                    yield break;
                var successful = false;
                generation++;
                yield return RefreshAll(result => successful = result);
                if (_refreshGeneration != generation)
                    yield break;
                if (successful)
                {
                    completed?.Invoke(true);
                    yield break;
                }
                Plugin.Log.LogWarning(BotGenNpcProvider.Prefix + " Startup refresh attempt " + (attempt + 1)
                    + "/" + StartupDelays.Length + " failed; "
                    + (attempt + 1 < StartupDelays.Length
                        ? "retry in " + StartupDelays[attempt + 1] + "s."
                        : "automatic retries exhausted; use Refresh BotGen Registry to try again."));
            }
            completed?.Invoke(false);
        }
    }

    // Optional BotGen integration. Uses BotGen's server registry endpoint without a BotGen assembly reference.
    public sealed class BotGenNpcProvider : INpcProvider
    {
        public const string Prefix = "[MEL BotGen]";
        private const string RolePrefix = "botgen_";
        private static readonly Regex RoleNamePattern = new Regex("^botgen_[a-z0-9_]+$", RegexOptions.Compiled);

        private List<NpcOption> _options = new List<NpcOption>();
        private readonly Dictionary<string, NpcOption> _knownOptions = new Dictionary<string, NpcOption>(StringComparer.Ordinal);
        private readonly HashSet<string> _warned = new HashSet<string>();
        private bool _hasSnapshot;
        private readonly Func<Task<BotGenRegistryResponse>> _fetch;

        public BotGenNpcProvider()
            : this(() => BotGenRegistryTransport.FetchAsync(Plugin.BotGenBackendUrl.Value))
        {
        }

        internal BotGenNpcProvider(Func<Task<BotGenRegistryResponse>> fetch)
        {
            _fetch = fetch;
        }

        public string Name { get { return "BotGen"; } }
        public bool Detected { get; private set; }
        public bool IsLoading { get; private set; }
        public bool IsStale { get; private set; }
        public string StatusNote { get; private set; }
        public IList<NpcOption> Options { get { return _options; } }

        public bool Owns(string id)
        {
            return !string.IsNullOrEmpty(id) && id.StartsWith(RolePrefix, StringComparison.OrdinalIgnoreCase);
        }

        public NpcOption Find(string id)
        {
            return string.IsNullOrEmpty(id) ? null : _options.FirstOrDefault(o => o.Id.Equals(id, StringComparison.Ordinal));
        }

        public NpcOption FindKnown(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            return _knownOptions.TryGetValue(id, out var option) ? option : null;
        }

        public bool IsAvailable(string id, out string reason)
        {
            reason = null;
            if (IsLoading)
            {
                reason = "BotGen registry refresh is in progress";
                return false;
            }
            if (IsStale)
            {
                reason = "the latest BotGen registry request failed; role availability cannot be verified";
                return false;
            }

            var opt = Find(id);
            if (opt == null)
            {
                var known = FindKnown(id);
                if (known == null)
                    reason = Detected
                        ? "role is not in BotGen's current registry (it may have been deleted or deactivated)"
                        : "BotGen registry has not been loaded (" + (StatusNote ?? "no successful response") + ")";
                else if (!known.Active)
                    reason = "role is inactive in BotGen";
                else if (!known.TemplateRegistered)
                    reason = "role has no registered BotGen template";
                else
                    reason = "role is not registered in the running game's WildSpawnType enum";
                return false;
            }
            if (!opt.Active)
            {
                reason = "role is inactive in BotGen";
                return false;
            }
            if (!opt.TemplateRegistered)
            {
                reason = "role has no registered BotGen template";
                return false;
            }
            string live;
            try { live = Enum.GetName(typeof(WildSpawnType), opt.EnumId); }
            catch { live = null; }
            if (!string.Equals(live, opt.Id, StringComparison.Ordinal))
            {
                reason = "role is not registered in the running game (BotGen prepatcher/client plugin not loaded, or the game was not restarted after the role was added)";
                return false;
            }
            return true;
        }

        public void Warn(string key, string message)
        {
            if (_warned.Add(key))
                Plugin.Log.LogWarning(Prefix + " " + message);
        }

        public IEnumerator RefreshCoroutine(Action<bool> completed)
        {
            if (IsLoading)
            {
                completed?.Invoke(false);
                yield break;
            }

            IsLoading = true;
            var successful = false;
            StatusNote = _hasSnapshot ? "refreshing the last successful registry snapshot" : "connecting to BotGen registry";
            try
            {
                var request = _fetch();
                while (!request.IsCompleted)
                    yield return null;
                var response = request.GetAwaiter().GetResult();

                if (response.Failure != null)
                {
                    MarkRefreshFailed(response.Failure + "; " + response.Context);
                }
                else if (!TryParseRegistry(response.Json, out var replacement, out var error))
                {
                    MarkRefreshFailed("response-validation-error: " + error + "; " + response.Context);
                }
                else
                {
                    foreach (var option in replacement.Known)
                        _knownOptions[option.Id] = option;
                    _options = replacement.Available;
                    _hasSnapshot = true;
                    Detected = true;
                    IsStale = false;
                    StatusNote = _options.Count == 0 ? "BotGen registry successfully returned zero active NPCs" : null;
                    _warned.Clear();
                    Plugin.Log.LogInfo(Prefix + " Replaced registry snapshot: " + _options.Count + " active, registered NPC role(s); " +
                        replacement.Known.Count + " role(s) reported; result="
                        + (replacement.Known.Count == 0 ? "valid-empty-registry" : "valid-registry") + "; " + response.Context);
                    successful = true;
                }
            }
            finally
            {
                IsLoading = false;
            }
            completed?.Invoke(successful);
        }

        private void MarkRefreshFailed(string reason)
        {
            IsStale = true;
            StatusNote = reason;
            Plugin.Log.LogWarning(Prefix + " Registry refresh failed: " + reason +
                (_hasSnapshot ? "; preserving the last successful snapshot." : "; no authoritative registry is available."));
        }

        private sealed class RegistrySnapshot
        {
            public List<NpcOption> Available = new List<NpcOption>();
            public List<NpcOption> Known = new List<NpcOption>();
        }

        private static bool TryParseRegistry(string json, out RegistrySnapshot snapshot, out string error)
        {
            snapshot = null;
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(json))
                {
                    error = "response body was empty";
                    return false;
                }

                var document = JObject.Parse(json);
                if ((int?)document["version"] != 1
                    || !string.Equals((string)document["roleNamePrefix"], RolePrefix, StringComparison.Ordinal)
                    || !(document["entries"] is JArray entries))
                {
                    error = "response has an unsupported version or invalid registry schema";
                    return false;
                }

                var known = new List<NpcOption>();
                var available = new List<NpcOption>();
                var rolesByNpcId = new Dictionary<string, NpcOption>(StringComparer.Ordinal);
                var roleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var enumIds = new HashSet<int>();

                foreach (var token in entries)
                {
                    if (!(token is JObject entry))
                    {
                        error = "response contains a non-object registry entry";
                        return false;
                    }

                    var npcId = (string)entry["npcId"];
                    var roleName = (string)entry["roleName"];
                    var enumId = (int?)entry["enumId"];
                    var active = (bool?)entry["active"];
                    var templateRegistered = (bool?)entry["templateRegistered"];
                    var baseArchetype = (string)entry["baseArchetype"];
                    var category = (string)entry["category"];
                    var side = (string)entry["side"];
                    var followers = entry["followers"] as JArray;
                    if (string.IsNullOrWhiteSpace(npcId) || string.IsNullOrWhiteSpace(roleName)
                        || !RoleNamePattern.IsMatch(roleName) || !enumId.HasValue || enumId.Value <= 0
                        || !active.HasValue || !templateRegistered.HasValue
                        || string.IsNullOrWhiteSpace(baseArchetype) || string.IsNullOrWhiteSpace(category)
                        || string.IsNullOrWhiteSpace(side)
                        || followers == null || !(entry["supportedRegistrationModes"] is JArray)
                        || !roleNames.Add(roleName) || !enumIds.Add(enumId.Value))
                    {
                        error = "response contains an invalid, duplicate, or incompatible role entry";
                        return false;
                    }

                    var option = new NpcOption
                    {
                        Source = "BotGen",
                        Id = roleName,
                        Label = (string)entry["nickname"] ?? npcId,
                        Kind = NpcKind.Bot,
                        Category = category,
                        BaseArchetype = baseArchetype,
                        EnumId = enumId.Value,
                        Active = active.Value,
                        TemplateRegistered = templateRegistered.Value
                    };
                    known.Add(option);
                    rolesByNpcId.Add(npcId, option);
                }

                foreach (var token in entries)
                {
                    var entry = (JObject)token;
                    var npcId = (string)entry["npcId"];
                    var option = rolesByNpcId[npcId];
                    var followers = (JArray)entry["followers"];
                    foreach (var follower in followers)
                    {
                        if (follower.Type != JTokenType.String)
                        {
                            error = "response contains a non-string follower reference";
                            return false;
                        }
                        var followerId = (string)follower;
                        if (string.IsNullOrWhiteSpace(followerId))
                            continue;
                        if (rolesByNpcId.TryGetValue(followerId, out var followerRole))
                            option.FollowerIds.Add(followerRole.Id);
                        else
                            option.FollowerIds.Add(followerId);
                    }
                }

                foreach (var option in known)
                {
                    if (option.Active && option.TemplateRegistered && IsEnumRoleRegistered(option))
                    {
                        option.Kind = known.Any(candidate => candidate.FollowerIds.Contains(option.Id, StringComparer.OrdinalIgnoreCase))
                            ? NpcKind.Follower
                            : NpcKind.Bot;
                        available.Add(option);
                    }
                    else if (option.Active && option.TemplateRegistered)
                    {
                        Plugin.Log.LogWarning(Prefix + " Role '" + option.Id +
                            "' is active on the server but is not registered in the current client enum; it will not be selectable.");
                    }
                }

                snapshot = new RegistrySnapshot { Available = available, Known = known };
                return true;
            }
            catch (Newtonsoft.Json.JsonReaderException ex)
            {
                error = "invalid JSON: " + ex.Message;
                return false;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidCastException
                || ex is FormatException || ex is OverflowException)
            {
                error = "incompatible registry schema: " + ex.Message;
                return false;
            }
        }

        private static bool IsEnumRoleRegistered(NpcOption option)
        {
            try
            {
                return string.Equals(Enum.GetName(typeof(WildSpawnType), option.EnumId), option.Id, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }
}
