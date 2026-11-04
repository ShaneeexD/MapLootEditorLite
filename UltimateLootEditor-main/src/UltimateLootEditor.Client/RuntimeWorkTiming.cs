using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Logging;

namespace ULE.SpawnEditor
{
    // Main-thread scopes: ordinary frames allocate nothing and produce no log output.
    internal readonly struct RuntimeWorkTiming : IDisposable
    {
        private static readonly Dictionary<string, long> NextReports = new Dictionary<string, long>();
        private readonly string _stage;
        private readonly ManualLogSource _log;
        private readonly long _started;

        private RuntimeWorkTiming(string stage, ManualLogSource log)
        {
            _stage = stage;
            _log = log;
            _started = Stopwatch.GetTimestamp();
        }

        internal static RuntimeWorkTiming Measure(string stage, ManualLogSource log)
            => new RuntimeWorkTiming(stage, log);

        public void Dispose()
        {
            var now = Stopwatch.GetTimestamp();
            var milliseconds = (now - _started) * 1000d / Stopwatch.Frequency;
            if (milliseconds < 12d || _log == null) return;
            if (NextReports.TryGetValue(_stage, out var next) && now < next) return;
            NextReports[_stage] = now + 5L * Stopwatch.Frequency;
            _log.LogInfo($"[ULE] Slow runtime work: {_stage}={milliseconds:0.0}ms, editorOpen={EditorWindowState.Open}.");
        }
    }
}
