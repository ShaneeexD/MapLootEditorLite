using System.Collections;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using MapLootEditorLite.Client;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using SPT.Common.Utils;
using UnityEngine;

internal static class Program
{
    private static int _passed;
    private const string Empty = "{\"version\":1,\"roleNamePrefix\":\"botgen_\",\"entries\":[]}";

    private static async Task Main()
    {
        TestActivityZonePackRoundTrip();
        Check("HTTPS and non-default port", Resolve("https://127.0.0.1:6970") == "https://127.0.0.1:6970");
        Check("HTTP and remote backend", Resolve("http://server.example:8123/") == "http://server.example:8123");
        Check("base path retained", Resolve("https://server.example:9443/spt/") == "https://server.example:9443/spt");
        Check("full legacy endpoint normalized", Resolve("https://server.example:9443/spt/botgen/registry/") == "https://server.example:9443/spt");
        foreach (var value in new[] { "", "ftp://server", "relative", "https://user:pass@server",
                     "https://server?token=secret", "https://server/#secret", "https://server/botgen/registry/botgen/registry" })
            Check("invalid override rejected: " + value, !BotGenRegistryTransport.TryResolveBackend(value, out _, out _));
        Check("old default migrates to automatic", BotGenRegistryTransport.MigrateLegacyUrl(BotGenRegistryTransport.LegacyDefault) == "");
        Check("custom legacy endpoint preserved", BotGenRegistryTransport.MigrateLegacyUrl("http://custom:8123/botgen/registry") == "http://custom:8123");
        Check("invalid legacy endpoint not silently defaulted", BotGenRegistryTransport.MigrateLegacyUrl("invalid") == "invalid");

        RequestHandler.Host = "https://127.0.0.1:6970/";
        RequestHandler.HttpClient = new SPT.Common.Http.Client(RequestHandler.Host, RequestHandler.SessionId);
        var sends = 0;
        string target = null;
        SPT.Common.Http.Client.Respond = async (request, token) =>
        {
            sends++;
            target = request.RequestUri.AbsoluteUri;
            Check("POST method", request.Method == HttpMethod.Post);
            Check("JSON content type", request.Content.Headers.ContentType.MediaType == "application/json");
            Check("JSON accept header", request.Headers.Accept.Single().MediaType == "application/json");
            Check("SPT session factory retained", request.Headers.GetValues("Cookie").Single() == "PHPSESSID=" + RequestHandler.SessionId);
            Check("per-request cancellation available", token.CanBeCanceled);
            var body = await request.Content.ReadAsByteArrayAsync();
            Check("SPT request framing", Zlib.IsCompressed(body));
            Check("empty JSON request model", Encoding.UTF8.GetString(Zlib.Decompress(body)) == "{}");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Zlib.Compress(Encoding.UTF8.GetBytes(Empty), ZlibCompression.Maximum))
            };
        };
        var response = await BotGenRegistryTransport.FetchAsync("");
        Check("configured SPT backend used, no duplicate slash", target == "https://127.0.0.1:6970/botgen/registry");
        Check("SPT response framing", response.Json == Empty && response.Failure == null);
        Check("automatic diagnostics", response.Context.Contains("automatic SPT discovery") && response.Context.Contains("HTTP=200"));
        response = await BotGenRegistryTransport.FetchAsync("http://custom.example:8123/spt/botgen/registry/");
        Check("override without duplicate route", target == "http://custom.example:8123/spt/botgen/registry");
        Check("manual diagnostics", response.Context.Contains("manual override"));
        var before = sends;
        response = await BotGenRegistryTransport.FetchAsync("https://user:secret@server/?token=secret");
        Check("invalid override sends nothing", sends == before && response.Failure.StartsWith("Backend configuration invalid"));
        Check("invalid URL secrets not logged", !response.Context.Contains("secret") && !response.Failure.Contains("secret"));
        RequestHandler.Host = null;
        response = await BotGenRegistryTransport.FetchAsync("");
        Check("missing automatic backend classified without sending", sends == before && response.Context.Contains("backend-unavailable"));
        RequestHandler.Host = "http://remote.example:8234";
        RequestHandler.HttpClient = new SPT.Common.Http.Client(RequestHandler.Host, RequestHandler.SessionId);
        response = await BotGenRegistryTransport.FetchAsync("");
        Check("automatic HTTP/non-default backend used", target == "http://remote.example:8234/botgen/registry");

        SPT.Common.Http.Client.Respond = (_, _) =>
        {
            sends++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        };
        response = await BotGenRegistryTransport.FetchAsync("");
        Check("HTTP failure classification and status", response.Failure.Contains("503") && response.Context.Contains("result=http-error"));
        before = sends;
        SPT.Common.Http.Client.Respond = (_, _) =>
        {
            sends++;
            throw new HttpRequestException("secret " + RequestHandler.SessionId, new SocketException(10061));
        };
        response = await BotGenRegistryTransport.FetchAsync("");
        Check("connection refused classified", response.Failure.StartsWith("connection-refused"));
        Check("native diagnostic retained", response.Failure.Contains("NativeError="));
        Check("no hidden immediate retries", sends == before + 1);
        Check("exception message/session not leaked", !response.Failure.Contains("secret") && !response.Failure.Contains(RequestHandler.SessionId));
        Check("timeout classified", BotGenRegistryTransport.ClassifyFailure(new TaskCanceledException()) == "timeout");
        Check("uninitialized backend classified", BotGenRegistryTransport.ClassifyFailure(new TypeInitializationException("RequestHandler", new Exception())) == "backend-unavailable");
        Check("DNS failure classified", BotGenRegistryTransport.ClassifyFailure(new SocketException(11001)) == "backend-unavailable");

        var next = Success(Populated());
        var fetches = 0;
        var provider = new BotGenNpcProvider(() =>
        {
            fetches++;
            return Task.FromResult(next);
        });
        Refresh(provider, expected: true);
        Check("validated populated registry", provider.Options.Count == 2 && provider.IsAvailable("botgen_boss", out _));
        Check("exact NPC follower reference", provider.Find("botgen_boss").FollowerIds.SequenceEqual(new[] { "botgen_follower" }));
        Check("follower kind", provider.Find("botgen_follower").Kind == NpcKind.Follower);
        next = new BotGenRegistryResponse { Failure = "connection-refused", Context = "HTTP=unavailable elapsedMs=2" };
        Refresh(provider, expected: false);
        Check("connection failure preserves stale snapshot", provider.IsStale && provider.Options.Count == 2 && provider.Detected);
        Check("stale roles cannot spawn", !provider.IsAvailable("botgen_boss", out _));
        Check("underlying failure in UI status", provider.StatusNote.Contains("connection-refused") && provider.StatusNote.Contains("elapsedMs="));
        next = Success("{not json");
        Refresh(provider, expected: false);
        Check("invalid JSON preserves snapshot", provider.Options.Count == 2 && provider.StatusNote.Contains("invalid JSON"));
        next = Success(Empty.Replace("\"version\":1", "\"version\":2"));
        Refresh(provider, expected: false);
        Check("incompatible schema rejected", provider.Options.Count == 2 && provider.StatusNote.Contains("schema"));
        next = Success("");
        Refresh(provider, expected: false);
        Check("empty body not authoritative empty", provider.Options.Count == 2);
        next = Success(Populated());
        Refresh(provider, expected: true);
        Check("success restores stale provider", !provider.IsStale && provider.IsAvailable("botgen_boss", out _));
        next = Success(Empty);
        Refresh(provider, expected: true);
        Check("valid empty clears available roles", provider.Options.Count == 0 && provider.Detected && !provider.IsStale);
        Check("saved role metadata retained", provider.FindKnown("botgen_boss") != null && !provider.IsAvailable("botgen_boss", out _));
        Check("unknown/deleted role rejected", !provider.IsAvailable("botgen_deleted", out var missing) && missing.Contains("deleted or deactivated"));
        provider.Warn("missing", "missing role");
        provider.Warn("missing", "missing role");
        Check("missing role warnings deduplicated", Plugin.Log.Lines.Count(line => line.EndsWith("missing role")) == 1);

        next = Success(Populated(registered: false));
        Refresh(provider, expected: true);
        Check("unregistered template not selectable", provider.Options.Count == 0);
        next = Success(Populated().Replace("970000", "970002"));
        Refresh(provider, expected: true);
        Check("unregistered client enum role not selectable", provider.Find("botgen_boss") == null);
        var malformed = JObject.Parse(Populated());
        ((JArray)malformed["entries"]).Add(malformed["entries"][0].DeepClone());
        next = Success(malformed.ToString());
        Refresh(provider, expected: false);
        Check("duplicate roles reject entire snapshot", provider.IsStale && provider.Options.Count == 1);
        malformed = JObject.Parse(Populated());
        malformed["entries"][0]["followers"] = new JArray(5);
        next = Success(malformed.ToString());
        Refresh(provider, expected: false);
        Check("malformed follower rejects entire snapshot", provider.IsStale);
        malformed = JObject.Parse(Populated());
        malformed["entries"][1]["npcId"] = "exact-boss-id";
        next = Success(malformed.ToString());
        Refresh(provider, expected: false);
        Check("duplicate NPC IDs reject snapshot", provider.IsStale);
        malformed = JObject.Parse(Populated());
        malformed["entries"][0]["active"] = false;
        next = Success(malformed.ToString());
        Refresh(provider, expected: true);
        Check("inactive roles not selectable/spawnable", provider.Find("botgen_boss") == null && !provider.IsAvailable("botgen_boss", out _));
        malformed = JObject.Parse(Populated());
        malformed["entries"][0]["followers"] = new JArray("botgen_follower", "missing-exact-id");
        next = Success(malformed.ToString());
        Refresh(provider, expected: true);
        Check("role ID follower retained exactly", provider.Find("botgen_boss").FollowerIds[0] == "botgen_follower");
        Check("missing follower not remapped to vanilla", provider.Find("botgen_boss").FollowerIds[1] == "missing-exact-id"
            && !provider.IsAvailable("missing-exact-id", out _));

        var pending = new TaskCompletionSource<BotGenRegistryResponse>();
        var inFlight = 0;
        provider = new BotGenNpcProvider(() => { inFlight++; return pending.Task; });
        var first = provider.RefreshCoroutine(null);
        Check("first refresh starts", first.MoveNext() && provider.IsLoading);
        Refresh(provider, expected: false);
        Check("overlapping refresh suppressed", inFlight == 1 && provider.IsLoading);
        pending.SetResult(Success(Empty));
        Drain(first);
        Check("loading flag reset on completion", !provider.IsLoading);

        sends = 0;
        SPT.Common.Http.Client.Respond = (_, _) =>
        {
            sends++;
            throw new HttpRequestException("unavailable");
        };
        var waits = new List<float>();
        var startupSucceeded = true;
        Drain(NpcProviderRegistry.RefreshAtStartup(value => startupSucceeded = value), waits);
        Check("startup attempts capped at three", sends == 3 && !startupSucceeded);
        Check("startup uses realtime backoff, no aggressive polling", waits.SequenceEqual(new[] { 2f, 10f, 30f }));
        var startup = NpcProviderRegistry.RefreshAtStartup(null);
        Check("startup initially waits", startup.MoveNext() && startup.Current is WaitForSecondsRealtime);
        var sharedProvider = NpcProviderRegistry.All.Single();
        SPT.Common.Http.Client.Respond = (_, _) =>
        {
            sends++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Empty) });
        };
        Drain(NpcProviderRegistry.RefreshAll(null));
        before = sends;
        Drain(startup);
        Check("manual refresh supersedes pending startup retry", sends == before && !sharedProvider.IsStale);
        SPT.Common.Http.Client.Respond = (_, _) =>
        {
            sends++;
            throw new HttpRequestException("unavailable");
        };
        startup = NpcProviderRegistry.RefreshAtStartup(null);
        Check("retry schedule starts with wait", startup.MoveNext());
        Check("startup attempt is nested refresh", startup.MoveNext() && startup.Current is IEnumerator);
        Drain((IEnumerator)startup.Current);
        Check("failed attempt schedules delayed retry", startup.MoveNext() && startup.Current is WaitForSecondsRealtime);
        Drain(NpcProviderRegistry.RefreshAll(null));
        before = sends;
        Drain(startup);
        Check("failed manual refresh also supersedes pending retries", sends == before && sharedProvider.IsStale);
        SPT.Common.Http.Client.Respond = (_, _) =>
        {
            sends++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Empty) });
        };
        waits.Clear();
        Drain(NpcProviderRegistry.RefreshAtStartup(null), waits);
        Check("successful startup stops after one attempt", sends == before + 1 && waits.SequenceEqual(new[] { 2f }));
        Check("logs contain no session", Plugin.Log.Lines.All(line => !line.Contains(RequestHandler.SessionId)));
        Console.WriteLine($"PASS: {_passed} checks; no SPT/game process or real network used.");
    }

    private static void TestActivityZonePackRoundTrip()
    {
        var oldMap = System.Text.Json.JsonSerializer.Deserialize<MapLootEditorLite.Server.MapData>(
            "{\"botActivityZones\":[{\"zoneId\":\"zone_old\",\"shape\":\"Square\",\"sideLength\":32,\"position\":{\"x\":1,\"y\":2,\"z\":3},\"rotation\":{\"x\":0,\"y\":45,\"z\":0}}]}");
        var oldZone = oldMap?.BotActivityZones?.SingleOrDefault();
        Check("legacy activity zone loads without patrol fields", oldZone != null && oldZone.Width == 0f && oldZone.Depth == 0f &&
            !oldZone.PatrolEnabled && oldZone.PatrolPoints.Count == 0 && oldZone.SideLength == 32f);

        var map = new MapLootEditorLite.Server.MapData
        {
            BotActivityZones = new List<MapLootEditorLite.Server.BotActivityZone>
            {
                new MapLootEditorLite.Server.BotActivityZone
                {
                    ZoneId = "zone_origin",
                    Shape = "Square",
                    SideLength = 35f,
                    Width = 20f,
                    Depth = 35f,
                    PatrolEnabled = true,
                    PatrolPoints = new List<MapLootEditorLite.Server.BotActivityPatrolPoint>
                    {
                        new MapLootEditorLite.Server.BotActivityPatrolPoint
                        {
                            Id = "patrol_001",
                            Name = "Overlook",
                            Position = new MapLootEditorLite.Server.TransformData { X = 10f, Y = 4f, Z = -6f },
                            DestinationZoneId = "zone_destination",
                            DwellSeconds = 120f,
                            Enabled = true
                        }
                    }
                }
            }
        };
        var json = System.Text.Json.JsonSerializer.Serialize(map);
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<MapLootEditorLite.Server.MapData>(json);
        var zone = roundTrip?.BotActivityZones?.SingleOrDefault();
        var point = zone?.PatrolPoints?.SingleOrDefault();
        Check("activity-zone dimensions round-trip independently", zone != null && zone.Width == 20f && zone.Depth == 35f);
        Check("patrol route settings round-trip", zone != null && zone.PatrolEnabled && point?.Id == "patrol_001" &&
            point.Name == "Overlook" && point.DestinationZoneId == "zone_destination" && point.DwellSeconds == 120f &&
            point.Position.X == 10f && point.Position.Y == 4f && point.Position.Z == -6f && point.Enabled);
    }

    private static string Resolve(string value)
    {
        if (!BotGenRegistryTransport.TryResolveBackend(value, out var backend, out var error))
            throw new Exception(error);
        return backend;
    }

    private static BotGenRegistryResponse Success(string json) => new BotGenRegistryResponse { Json = json, Context = "HTTP=200 elapsedMs=1" };

    private static string Populated(bool registered = true)
    {
        JObject Entry(string npc, string role, int id, params string[] followers) => new JObject
        {
            ["npcId"] = npc, ["roleName"] = role, ["enumId"] = id, ["active"] = true,
            ["templateRegistered"] = registered, ["baseArchetype"] = "assault", ["category"] = "boss",
            ["side"] = "Savage", ["followers"] = new JArray(followers), ["supportedRegistrationModes"] = new JArray("bossWave")
        };
        return new JObject
        {
            ["version"] = 1, ["roleNamePrefix"] = "botgen_",
            ["entries"] = new JArray(Entry("exact-boss-id", "botgen_boss", 970000, "exact-follower-id"),
                Entry("exact-follower-id", "botgen_follower", 970001))
        }.ToString();
    }

    private static void Refresh(BotGenNpcProvider provider, bool expected)
    {
        bool? result = null;
        Drain(provider.RefreshCoroutine(value => result = value));
        Check("refresh completion callback", result == expected && !provider.IsLoading || !expected && result == false);
    }

    private static void Drain(IEnumerator coroutine, List<float> waits = null)
    {
        using (coroutine as IDisposable)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (coroutine.MoveNext())
            {
                if (DateTime.UtcNow > deadline) throw new Exception("Test coroutine did not complete");
                if (coroutine.Current is IEnumerator nested) Drain(nested, waits);
                else if (coroutine.Current is WaitForSecondsRealtime wait) waits?.Add(wait.Seconds);
                else Thread.Yield();
            }
        }
    }

    private static void Check(string name, bool passed)
    {
        if (!passed) throw new Exception("FAIL: " + name);
        _passed++;
    }
}
