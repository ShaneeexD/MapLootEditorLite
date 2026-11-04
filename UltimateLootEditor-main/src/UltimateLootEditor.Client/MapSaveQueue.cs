using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ULE.SpawnEditor
{
    // Plain managed state: the worker never accesses a Unity component or notification API.
    // One writer per map drains the newest queued snapshot even after the raid ends.
    internal sealed class MapSaveQueue
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, Entry> _maps = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentQueue<Completion> _completed = new ConcurrentQueue<Completion>();
        private readonly Action<MapEdits> _write;

        public MapSaveQueue(Action<MapEdits> write) { _write = write ?? throw new ArgumentNullException(nameof(write)); }

        public void Enqueue(MapEdits edits)
        {
            if (edits == null || string.IsNullOrWhiteSpace(edits.MapId))
                throw new ArgumentException("A map is required to save edits.", nameof(edits));

            // Freeze before returning to the editor, which may immediately change its draft.
            FiniteNumbers.Validate(edits);
            var snapshot = SaveManager.CloneMapEdits(edits);
            lock (_sync)
            {
                if (!_maps.TryGetValue(snapshot.MapId, out var entry))
                    _maps[snapshot.MapId] = entry = new Entry();
                entry.Revision++;
                entry.Pending = snapshot;
                entry.Latest = snapshot;
                entry.Error = null;
                if (!entry.Running)
                {
                    entry.Running = true;
                    Task.Run(() => Drain(snapshot.MapId, entry));
                }
            }
        }

        public Status GetStatus(string mapId)
        {
            lock (_sync)
            {
                return mapId != null && _maps.TryGetValue(mapId, out var entry)
                    ? new Status(entry.Running, entry.Error)
                    : new Status(false, null);
            }
        }

        public MapEdits GetUnsavedSnapshot(string mapId)
        {
            lock (_sync)
            {
                return mapId != null && _maps.TryGetValue(mapId, out var entry) && entry.Latest != null
                    ? SaveManager.CloneMapEdits(entry.Latest) : null;
            }
        }

        public bool TryTakeCompletion(out Completion completion)
        {
            while (_completed.TryDequeue(out completion))
            {
                lock (_sync)
                {
                    // Do not report an older success/failure over a newer requested save.
                    if (_maps.TryGetValue(completion.MapId, out var entry) &&
                        entry.Revision == completion.Revision && !entry.Running)
                        return true;
                }
            }
            completion = null;
            return false;
        }

        private void Drain(string mapId, Entry entry)
        {
            while (true)
            {
                MapEdits snapshot;
                long revision;
                lock (_sync)
                {
                    snapshot = entry.Pending;
                    revision = entry.Revision;
                    entry.Pending = null;
                }

                string error = null;
                try { _write(snapshot); }
                catch (Exception ex) { error = ex.GetBaseException().Message; }

                lock (_sync)
                {
                    if (entry.Revision == revision)
                    {
                        entry.Error = error;
                        if (error == null) entry.Latest = null;
                    }
                    if (entry.Pending == null)
                    {
                        entry.Running = false;
                        _completed.Enqueue(new Completion(mapId, revision, error));
                        return;
                    }
                }
            }
        }

        private sealed class Entry
        {
            public long Revision;
            public bool Running;
            public MapEdits Pending;
            public MapEdits Latest;
            public string Error;
        }

        internal sealed class Completion
        {
            public readonly string MapId;
            public readonly long Revision;
            public readonly string Error;
            public Completion(string mapId, long revision, string error)
            { MapId = mapId; Revision = revision; Error = error; }
        }

        internal readonly struct Status
        {
            public readonly bool IsSaving;
            public readonly string Error;
            public bool Failed => !string.IsNullOrEmpty(Error);
            public Status(bool isSaving, string error) { IsSaving = isSaving; Error = error; }
        }
    }
}
