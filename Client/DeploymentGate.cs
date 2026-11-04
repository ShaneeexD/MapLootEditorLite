using System;
using System.Collections.Generic;
using System.Linq;

namespace MapLootEditorLite.Client
{
    public enum DeploymentQuestState
    {
        Missing,     // quest id empty/unresolvable or not present in the player's profile
        Incomplete,  // valid quest that is not Success
        Completed
    }

    // Resolves per-map NPC deployments once per raid start from SPT quest state. Active = activation completed AND NOT deactivation completed.
    // Pure logic: the quest lookup is injected so it can be unit tested without the game.
    public sealed class DeploymentGate
    {
        public const string LogPrefix = "[MEL Deployment]";

        private readonly Dictionary<string, bool> _active = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly HashSet<string> _warnedUnknown = new HashSet<string>(StringComparer.Ordinal);

        public static DeploymentGate Evaluate(IEnumerable<NpcDeployment> deployments, string mapId, Func<string, DeploymentQuestState> lookup, Action<string> info, Action<string> warn)
        {
            var gate = new DeploymentGate();
            if (deployments == null)
                return gate;

            foreach (var d in deployments)
            {
                if (d == null)
                    continue;
                var id = d.id ?? "";
                if (string.IsNullOrWhiteSpace(id))
                {
                    warn($"{LogPrefix} Deployment '{d.name}' on map '{mapId}' has no id; ignored. Entries cannot reference it.");
                    continue;
                }
                if (gate._active.ContainsKey(id))
                {
                    warn($"{LogPrefix} Duplicate deployment id '{id}' on map '{mapId}'; keeping the first definition and treating the duplicate as invalid.");
                    gate._active[id] = false;
                    continue;
                }

                var activation = Lookup(lookup, d.activationQuestId);
                var hasDeactivation = !string.IsNullOrWhiteSpace(d.deactivationQuestId);
                var deactivation = hasDeactivation ? Lookup(lookup, d.deactivationQuestId) : DeploymentQuestState.Incomplete;

                bool active;
                string reason;
                if (activation == DeploymentQuestState.Missing)
                {
                    active = false;
                    reason = string.IsNullOrWhiteSpace(d.activationQuestId) ? "activation quest id is empty" : "activation quest is missing from the player's profile";
                }
                else if (hasDeactivation && deactivation == DeploymentQuestState.Missing)
                {
                    active = false;
                    reason = "deactivation quest is configured but missing from the player's profile (fail closed)";
                }
                else if (hasDeactivation && deactivation == DeploymentQuestState.Completed)
                {
                    active = false;
                    reason = "withdrawn: deactivation quest completed";
                }
                else if (activation != DeploymentQuestState.Completed)
                {
                    active = false;
                    reason = "activation quest not completed";
                }
                else
                {
                    active = true;
                    reason = "activation completed" + (hasDeactivation ? ", withdrawal not completed" : ", no withdrawal quest");
                }

                gate._active[id] = active;
                info($"{LogPrefix} id='{id}', name='{d.name}', map='{mapId}': activation='{d.activationQuestId}' ({activation}), deactivation={(hasDeactivation ? "'" + d.deactivationQuestId + "' (" + deactivation + ")" : "none")} => {(active ? "ACTIVE" : "INACTIVE")} ({reason}).");
            }
            return gate;
        }

        private static DeploymentQuestState Lookup(Func<string, DeploymentQuestState> lookup, string questId)
        {
            if (string.IsNullOrWhiteSpace(questId))
                return DeploymentQuestState.Missing;
            try { return lookup(questId); }
            catch { return DeploymentQuestState.Missing; }
        }

        // No deploymentId = not part of a deployment (always passes). Unknown id fails closed.
        public bool IsEligible(string deploymentId, string kind, string entryName, string entryId, string mapId, Action<string> warn)
        {
            if (string.IsNullOrWhiteSpace(deploymentId))
                return true;
            bool active;
            if (!_active.TryGetValue(deploymentId, out active))
            {
                if (_warnedUnknown.Add(deploymentId + "|" + entryId))
                    warn($"{LogPrefix} {kind} '{entryName}' (id={entryId}) on map '{mapId}' references unknown deployment '{deploymentId}'; skipped (fail closed).");
                return false;
            }
            if (!active)
                warn($"{LogPrefix} Skipped {kind} '{entryName}' (id={entryId}) on map '{mapId}': deployment '{deploymentId}' is inactive.");
            return active;
        }
    }
}
