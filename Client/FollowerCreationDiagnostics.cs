using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using HarmonyLib;

namespace MapLootEditorLite.Client
{
    // TEMPORARY, log-only diagnostics. Observes SPT's bot-creation chain for the exact follower profile ids MEL registered.
    // It never alters arguments, results, exceptions, spawn requests, groups or AI: prefixes/postfixes only read, finalizers have no
    // return value (so the original exception is rethrown unchanged) and task continuations only read task status.
    internal static class FollowerCreationDiagnostics
    {
        private const string HarmonyId = "com.shane.mapeditorlite.followerdiag";
        private static bool _applied;
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, string> LastStage = new Dictionary<string, string>();

        public static void Apply()
        {
            if (_applied)
                return;
            _applied = true;
            try
            {
                var harmony = new Harmony(HarmonyId);
                var creator = typeof(BotCreatorClient);
                var spawner = typeof(BotSpawner);

                var method0 = AccessTools.Method(creator, "method_0");
                var createBot = AccessTools.Method(creator, "CreateBot");
                var method3 = AccessTools.Method(creator, "method_3");
                var activateCb = AccessTools.Method(spawner, "ActivateBotCallback");
                var afterCreation = AccessTools.Method(spawner, "AfterCreation");

                Patch(harmony, method0, nameof(Method0Prefix), nameof(Method0Postfix), null);
                Patch(harmony, createBot, nameof(CreateBotPrefix), nameof(CreateBotPostfix), null);
                Patch(harmony, method3, nameof(Method3Prefix), null, nameof(Method3Finalizer));
                Patch(harmony, activateCb, nameof(ActivateCallbackPrefix), null, nameof(ActivateCallbackFinalizer));
                Patch(harmony, afterCreation, nameof(AfterCreationPrefix), null, nameof(AfterCreationFinalizer));
                Plugin.Log.LogInfo("[MEL followerdiag] Diagnostic hooks installed (log-only).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[MEL followerdiag] Could not install diagnostic hooks: {ex.Message}");
            }
        }

        private static void Patch(Harmony harmony, System.Reflection.MethodBase target, string prefix, string postfix, string finalizer)
        {
            if (target == null)
            {
                Plugin.Log.LogWarning($"[MEL followerdiag] Target for {prefix ?? postfix ?? finalizer} not found; skipped.");
                return;
            }
            HarmonyMethod Hm(string n) => n == null ? null : new HarmonyMethod(AccessTools.Method(typeof(FollowerCreationDiagnostics), n));
            harmony.Patch(target, prefix: Hm(prefix), postfix: Hm(postfix), finalizer: Hm(finalizer));
        }

        public static void Track(IEnumerable<string> profileIds, string label)
        {
            lock (Sync)
            {
                foreach (var id in profileIds)
                    LastStage[id] = "requested";
            }
        }

        private static bool IsTracked(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;
            lock (Sync)
                return LastStage.ContainsKey(id);
        }

        private static void Stage(string id, string stage)
        {
            lock (Sync)
                LastStage[id] = stage;
        }

        private static string Short(string id) => id != null && id.Length > 6 ? ".." + id.Substring(id.Length - 6) : id;

        private static void Log(string message) => Plugin.Log.LogInfo($"[MEL followerdiag] {message}");

        // ---- BotCreatorClient.method_0 (the sequential per-profile loop) ----
        public static void Method0Prefix(BotCreationData __0, bool __1)
        {
            try
            {
                var profiles = __0?.Profiles;
                if (profiles == null || !profiles.Any(p => p != null && IsTracked(p.Id)))
                    return;
                Log($"method_0 ENTER: profiles={profiles.Count} [{string.Join(", ", profiles.Select(p => Short(p?.Id)))}], positions={__0.CountPositions}, shallBeGroup={__1}, spawnStopped={__0.SpawnStopped}.");
            }
            catch { }
        }

        public static void Method0Postfix(Task __result, BotCreationData __0)
        {
            try
            {
                var profiles = __0?.Profiles?.ToList();
                if (profiles == null || !profiles.Any(p => p != null && IsTracked(p.Id)) || __result == null)
                    return;
                var ids = profiles.Where(p => p != null).Select(p => p.Id).ToList();
                __result.ContinueWith(t =>
                {
                    string summary;
                    lock (Sync)
                        summary = string.Join("; ", ids.Select(i => $"{Short(i)}={(LastStage.TryGetValue(i, out var s) ? s : "?")}"));
                    var outcome = t.IsFaulted ? "FAULTED: " + t.Exception?.Flatten().ToString() : t.IsCanceled ? "CANCELED" : "COMPLETED";
                    Log($"method_0 {outcome}. Last stage per profile: {summary}");
                }, TaskContinuationOptions.ExecuteSynchronously);
            }
            catch { }
        }

        // ---- BotCreatorClient.CreateBot(profile, bornInfo, callback, isLocalGame, token) ----
        public static void CreateBotPrefix(Profile __0)
        {
            try
            {
                if (__0 == null || !IsTracked(__0.Id))
                    return;
                Stage(__0.Id, "CreateBot entered");
                Log($"CreateBot ENTER profile={Short(__0.Id)} role={__0.Info?.Settings?.Role}.");
            }
            catch { }
        }

        public static void CreateBotPostfix(Task __result, Profile __0)
        {
            try
            {
                if (__0 == null || !IsTracked(__0.Id) || __result == null)
                    return;
                var id = __0.Id;
                __result.ContinueWith(t =>
                {
                    string last;
                    lock (Sync)
                        last = LastStage.TryGetValue(id, out var s) ? s : "?";
                    if (t.IsFaulted)
                    {
                        Stage(id, "CreateBot FAULTED after '" + last + "'");
                        Log($"CreateBot FAULTED profile={Short(id)} (last stage '{last}'): {t.Exception?.Flatten()}");
                    }
                    else if (t.IsCanceled)
                    {
                        Stage(id, "CreateBot canceled");
                        Log($"CreateBot CANCELED profile={Short(id)}.");
                    }
                    else
                    {
                        // CreateBot returns silently without calling the callback when the player is null or cancelled; "PreActivate" never reached below means that.
                        Log($"CreateBot COMPLETED profile={Short(id)} (last stage '{last}').");
                    }
                }, TaskContinuationOptions.ExecuteSynchronously);
            }
            catch { }
        }

        // ---- BotCreatorClient.method_3(zone, bot, callback, groupAction): PreActivate + group assignment ----
        public static void Method3Prefix(BotOwner __1)
        {
            try
            {
                var id = __1?.Profile?.Id;
                if (!IsTracked(id))
                    return;
                Stage(id, "PreActivate entered");
                Log($"PreActivate/group assignment ENTER profile={Short(id)}.");
            }
            catch { }
        }

        public static void Method3Finalizer(Exception __exception, BotOwner __1)
        {
            try
            {
                var id = __1?.Profile?.Id;
                if (!IsTracked(id))
                    return;
                if (__exception != null)
                {
                    Stage(id, "PreActivate/callback THREW " + __exception.GetType().Name);
                    Log($"PreActivate/callback THREW profile={Short(id)}: {__exception}");
                }
                else
                {
                    Stage(id, "PreActivate+callback done");
                    Log($"PreActivate/callback DONE profile={Short(id)}, group={(__1.BotsGroup != null ? __1.BotsGroup.Id.ToString() : "null")}.");
                }
            }
            catch { }
        }

        // ---- BotSpawner.ActivateBotCallback(bot, data, callback, shallBeGroup, stopWatch): OnBotCreated, AfterCreation, SetBoss ----
        public static void ActivateCallbackPrefix(BotOwner __0)
        {
            try
            {
                var id = __0?.Profile?.Id;
                if (!IsTracked(id))
                    return;
                Stage(id, "ActivateBotCallback entered");
                Log($"ActivateBotCallback ENTER profile={Short(id)} (next: OnBotCreated, AfterCreation, SetBoss).");
            }
            catch { }
        }

        public static void ActivateCallbackFinalizer(Exception __exception, BotOwner __0)
        {
            try
            {
                var id = __0?.Profile?.Id;
                if (!IsTracked(id))
                    return;
                if (__exception != null)
                {
                    Stage(id, "ActivateBotCallback THREW " + __exception.GetType().Name);
                    Log($"ActivateBotCallback THREW profile={Short(id)}: {__exception}");
                }
                else
                {
                    Stage(id, "ActivateBotCallback done");
                    Log($"ActivateBotCallback DONE profile={Short(id)}, group={(__0.BotsGroup != null ? __0.BotsGroup.Id.ToString() : "null")}, iamBoss={__0.Boss?.IamBoss}.");
                }
            }
            catch { }
        }

        // ---- BotSpawner.AfterCreation(bot): group AddBot for all zone groups ----
        public static void AfterCreationPrefix(BotOwner __0)
        {
            try
            {
                var id = __0?.Profile?.Id;
                if (IsTracked(id))
                    Stage(id, "AfterCreation entered");
            }
            catch { }
        }

        public static void AfterCreationFinalizer(Exception __exception, BotOwner __0)
        {
            try
            {
                var id = __0?.Profile?.Id;
                if (__exception != null && IsTracked(id))
                    Log($"AfterCreation THREW profile={Short(id)}: {__exception}");
            }
            catch { }
        }
    }
}
