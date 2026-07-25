using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace MapLootEditorLite.Server;

public static class RandX
{
    private static readonly ulong[] _state = new ulong[4];
    private static long _entropyPool;
    private static long _entropyCounter;
    private static readonly object _stateLock = new object();
    private static readonly object _entropyLock = new object();
    private static volatile bool _collecting = true;

    private const int EntropyRefreshInterval = 1000;

    static RandX()
    {
        InitializeState();
        StartEntropyCollector();
    }

    public static double Next()
    {
        return (NextLong() >>> 11) / (double)(1L << 53);
    }

    public static int NextInt(int max)
    {
        return Range(0, max - 1);
    }

    public static int Range(int min, int max)
    {
        if (min > max)
        {
            var temp = min;
            min = max;
            max = temp;
        }
        if (min == max)
            return min;

        var rangeSize = (long)max - min + 1;
        var bits = NextLong() & long.MaxValue;
        return (int)(min + (bits % rangeSize));
    }

    public static double Range(double min, double max)
    {
        if (min > max)
        {
            var temp = min;
            min = max;
            max = temp;
        }
        var normalized = (NextLong() >>> 11) / (double)(1L << 53);
        return min + normalized * (max - min);
    }

    public static bool Bool()
    {
        return (NextLong() & 1) == 1;
    }

    public static void Refresh()
    {
        lock (_stateLock)
        {
            _state[0] ^= (ulong)HarvestTimingEntropy();
            _state[1] ^= (ulong)HarvestMemoryEntropy();
            _state[2] ^= (ulong)HarvestThreadEntropy();
            _state[3] ^= (ulong)Stopwatch.GetTimestamp();
            for (var i = 0; i < 10; i++)
                ChaChaRound();
        }
    }

    public static void Shutdown()
    {
        _collecting = false;
    }

    private static void InitializeState()
    {
        _state[0] = (ulong)HarvestTimingEntropy();
        _state[1] = (ulong)HarvestMemoryEntropy();
        _state[2] = (ulong)HarvestThreadEntropy();
        _state[3] = (ulong)HarvestSystemEntropy();
        for (var i = 0; i < 20; i++)
            ChaChaRound();
    }

    private static long HarvestTimingEntropy()
    {
        long entropy = 0;
        long prev = Stopwatch.GetTimestamp();
        for (var i = 0; i < 64; i++)
        {
            long now = Stopwatch.GetTimestamp();
            entropy = (entropy << 1) | ((now - prev) & 1);
            prev = now;
            // consume a little CPU to generate timing jitter
            _ = Math.Sin(i * 0.1);
        }
        return entropy ^ Stopwatch.GetTimestamp();
    }

    private static long HarvestMemoryEntropy()
    {
        long allocated = GC.GetTotalMemory(false);
        long workingSet = Process.GetCurrentProcess().WorkingSet64;
        long entropy = allocated ^ workingSet;
        var noise = new object[100];
        for (var i = 0; i < noise.Length; i++)
        {
            noise[i] = new byte[(int)(Stopwatch.GetTimestamp() & 0xFF)];
            entropy ^= Stopwatch.GetTimestamp();
        }
        entropy ^= RuntimeHelpers.GetHashCode(noise);
        return entropy;
    }

    private static long HarvestThreadEntropy()
    {
        var current = Process.GetCurrentProcess();
        long entropy = Environment.CurrentManagedThreadId;
        entropy ^= current.TotalProcessorTime.Ticks;
        entropy ^= (long)Environment.TickCount64 << 32;
        entropy ^= Stopwatch.GetTimestamp();
        return entropy;
    }

    private static long HarvestSystemEntropy()
    {
        long entropy = 0;
        entropy ^= Environment.UserName?.GetHashCode() ?? 0;
        entropy ^= ((long)(Environment.OSVersion?.ToString().GetHashCode() ?? 0)) << 32;
        entropy ^= Environment.ProcessorCount;
        entropy ^= Environment.ProcessId;
        entropy ^= Stopwatch.GetTimestamp();
        return entropy;
    }

    private static void ChaChaRound()
    {
        _state[0] += _state[1]; _state[3] ^= _state[0]; _state[3] = BitOperations.RotateLeft(_state[3], 32);
        _state[2] += _state[3]; _state[1] ^= _state[2]; _state[1] = BitOperations.RotateLeft(_state[1], 24);
        _state[0] += _state[1]; _state[3] ^= _state[0]; _state[3] = BitOperations.RotateLeft(_state[3], 16);
        _state[2] += _state[3]; _state[1] ^= _state[2]; _state[1] = BitOperations.RotateLeft(_state[1], 7);
        _state[0] += _state[3]; _state[1] ^= _state[0]; _state[1] = BitOperations.RotateLeft(_state[1], 32);
        _state[2] += _state[1]; _state[3] ^= _state[2]; _state[3] = BitOperations.RotateLeft(_state[3], 24);
        _state[0] += _state[3]; _state[1] ^= _state[0]; _state[1] = BitOperations.RotateLeft(_state[1], 16);
        _state[2] += _state[1]; _state[3] ^= _state[2]; _state[3] = BitOperations.RotateLeft(_state[3], 7);
    }

    private static void StartEntropyCollector()
    {
        var collector = new Thread(() =>
        {
            while (_collecting)
            {
                try
                {
                    Thread.Sleep(10);
                    long fresh = HarvestTimingEntropy() ^ Stopwatch.GetTimestamp();
                    lock (_entropyLock)
                    {
                        _entropyPool ^= unchecked((long)BitOperations.RotateLeft((ulong)fresh, 17));
                    }
                }
                catch (ThreadInterruptedException)
                {
                    break;
                }
            }
        })
        { IsBackground = true, Name = "RandX-EntropyCollector" };
        collector.Start();
    }

    private static long NextLong()
    {
        lock (_stateLock)
        {
            var counter = ++_entropyCounter;
            _state[(int)(counter % 4)] ^= (ulong)Interlocked.Read(ref _entropyPool);
            _state[(int)((counter + 1) % 4)] ^= (ulong)Stopwatch.GetTimestamp();

            if (counter % EntropyRefreshInterval == 0)
            {
                _state[0] ^= (ulong)HarvestTimingEntropy();
                _state[1] ^= (ulong)HarvestMemoryEntropy();
                _state[2] ^= (ulong)HarvestThreadEntropy();
                _state[3] ^= (ulong)HarvestSystemEntropy();
            }

            ChaChaRound();
            return (long)(_state[0] ^ _state[1] ^ _state[2] ^ _state[3]);
        }
    }
}
