using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ULE.SpawnEditor
{
    // One selected map, shared by prewarming and the raid manager.
    internal sealed class MapLoadCache<T>
    {
        private readonly object _sync = new object();
        private readonly Func<string, CancellationToken, T> _load;
        private readonly Func<T, bool> _isSuccess;
        private readonly Func<double> _seconds;
        private Entry _entry;

        internal MapLoadCache(Func<string, CancellationToken, T> load, Func<T, bool> isSuccess,
            Func<double> seconds = null)
        {
            _load = load;
            _isSuccess = isSuccess;
            _seconds = seconds ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
        }

        internal Task<T> Get(string mapId)
        {
            var key = mapId.Trim();
            lock (_sync)
            {
                if (_entry != null && string.Equals(_entry.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (!_entry.Task.IsCompleted ||
                        (_entry.Task.Status == TaskStatus.RanToCompletion && _isSuccess(_entry.Task.Result)) ||
                        _seconds() < _entry.RetryAfter)
                        return _entry.Task;
                }

                ReleaseEntry();
                var entry = new Entry { Key = key };
                var token = entry.Cancellation.Token;
                _entry = entry;
                entry.Task = Task.Run(() =>
                {
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        var result = _load(key, token);
                        token.ThrowIfCancellationRequested();
                        return result;
                    }
                    finally
                    {
                        lock (_sync) entry.RetryAfter = _seconds() + 5d;
                    }
                });
                return entry.Task;
            }
        }

        internal void Release(string mapId, Task<T> expectedTask = null)
        {
            lock (_sync)
            {
                if (_entry != null && string.Equals(_entry.Key, mapId?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    (expectedTask == null || ReferenceEquals(expectedTask, _entry.Task)))
                    ReleaseEntry();
            }
        }

        private void ReleaseEntry()
        {
            if (_entry == null) return;
            _entry.Cancellation.Cancel();
            _entry.Cancellation.Dispose();
            // Observe faults even when a prewarm or an abandoned raid has no consumer.
            _entry.Task.ContinueWith(t => { var ignored = t.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            _entry = null;
        }

        private sealed class Entry
        {
            internal string Key;
            internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            internal Task<T> Task;
            internal double RetryAfter;
        }
    }
}
