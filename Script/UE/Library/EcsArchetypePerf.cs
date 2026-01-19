using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Script.Library;

public static class EcsArchetypePerf
{
    private static readonly int[] DefaultArchetypeLengths = { 120_000, 35_000, 9_000, 2_500 };

    [StructLayout(LayoutKind.Sequential)]
    public struct ArchetypeDesc
    {
        public nint Position;
        public nint Velocity;
        public int Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SliceDesc
    {
        public int ArchetypeIndex;
        public int Start;
        public int Length;
    }

    private sealed class Data : IDisposable
    {
        public NativeBuffer<int>[] Positions { get; }
        public NativeBuffer<int>[] Velocities { get; }
        public NativeBuffer<ArchetypeDesc> Archetypes { get; }
        public NativeBuffer<SliceDesc> Slices { get; }
        public int ArchetypeCount { get; }
        public int SliceCount { get; }

        public Data(
            NativeBuffer<int>[] positions,
            NativeBuffer<int>[] velocities,
            NativeBuffer<ArchetypeDesc> archetypes,
            NativeBuffer<SliceDesc> slices)
        {
            Positions = positions;
            Velocities = velocities;
            Archetypes = archetypes;
            Slices = slices;
            ArchetypeCount = positions.Length;
            SliceCount = slices.Length;
        }

        public void Dispose()
        {
            for (var i = 0; i < Positions.Length; i++)
            {
                Positions[i].Dispose();
                Velocities[i].Dispose();
            }

            Archetypes.Dispose();
            Slices.Dispose();
        }
    }

    public static double MeasureTaskGraph(
        int taskCount,
        int iterations,
        int warmup,
        int minParallelChunkSize,
        int dt,
        out long sum,
        int[]? archetypeLengths = null)
    {
        var lengths = archetypeLengths ?? DefaultArchetypeLengths;
        using var data = BuildData(lengths, taskCount, minParallelChunkSize);

        sum = 0;
        for (var i = 0; i < warmup; i++)
        {
            sum = FNativeBufferTaskGraphEcsImplementation
                .FNativeBufferTaskGraphEcs_UpdatePosVelSlicesParallelImplementation(
                    data.Archetypes.Ptr,
                    data.ArchetypeCount,
                    data.Slices.Ptr,
                    data.SliceCount,
                    dt);
        }

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            sum = FNativeBufferTaskGraphEcsImplementation
                .FNativeBufferTaskGraphEcs_UpdatePosVelSlicesParallelImplementation(
                    data.Archetypes.Ptr,
                    data.ArchetypeCount,
                    data.Slices.Ptr,
                    data.SliceCount,
                    dt);
        }
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds;
    }

    public static double MeasureParallelFor(
        int taskCount,
        int iterations,
        int warmup,
        int minParallelChunkSize,
        int dt,
        out long sum,
        int[]? archetypeLengths = null)
    {
        var lengths = archetypeLengths ?? DefaultArchetypeLengths;
        using var data = BuildData(lengths, taskCount, minParallelChunkSize);

        var safeTaskCount = Math.Clamp(taskCount, 1, data.SliceCount);
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = safeTaskCount
        };

        sum = 0;
        for (var i = 0; i < warmup; i++)
        {
            sum = RunParallelFor(data, options, dt);
        }

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            sum = RunParallelFor(data, options, dt);
        }
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds;
    }

    public static double MeasureTaskRun(
        int taskCount,
        int iterations,
        int warmup,
        int minParallelChunkSize,
        int dt,
        out long sum,
        int[]? archetypeLengths = null)
    {
        var lengths = archetypeLengths ?? DefaultArchetypeLengths;
        using var data = BuildData(lengths, taskCount, minParallelChunkSize);

        sum = 0;
        for (var i = 0; i < warmup; i++)
        {
            sum = RunTaskRun(data, dt);
        }

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            sum = RunTaskRun(data, dt);
        }
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds;
    }

    private static unsafe long RunParallelFor(Data data, ParallelOptions options, int dt)
    {
        var slices = (SliceDesc*)data.Slices.Ptr;
        var archetypes = (ArchetypeDesc*)data.Archetypes.Ptr;
        long sum = 0;

        Parallel.For(0, data.SliceCount, options,
            () => 0L,
            (sliceIndex, _, local) =>
            {
                var slice = slices[sliceIndex];
                var arch = archetypes[slice.ArchetypeIndex];
                var pos = (int*)arch.Position;
                var vel = (int*)arch.Velocity;
                var start = slice.Start;
                var end = Math.Min(start + slice.Length, arch.Length);

                for (var i = start; i < end; i++)
                {
                    pos[i] += vel[i] * dt;
                    local += pos[i];
                }

                return local;
            },
            local => Interlocked.Add(ref sum, local));

        return sum;
    }

    private static unsafe long RunTaskRun(Data data, int dt)
    {
        var slices = (SliceDesc*)data.Slices.Ptr;
        var archetypes = (ArchetypeDesc*)data.Archetypes.Ptr;
        var tasks = new Task<long>[data.SliceCount];

        for (var sliceIndex = 0; sliceIndex < data.SliceCount; sliceIndex++)
        {
            var index = sliceIndex;
            tasks[index] = Task.Run(() =>
            {
                var slice = slices[index];
                var arch = archetypes[slice.ArchetypeIndex];
                var pos = (int*)arch.Position;
                var vel = (int*)arch.Velocity;
                var start = slice.Start;
                var end = Math.Min(start + slice.Length, arch.Length);
                long local = 0;

                for (var i = start; i < end; i++)
                {
                    pos[i] += vel[i] * dt;
                    local += pos[i];
                }

                return local;
            });
        }

        Task.WaitAll(tasks);

        long sum = 0;
        for (var i = 0; i < tasks.Length; i++)
        {
            sum += tasks[i].Result;
        }

        return sum;
    }

    private static Data BuildData(int[] lengths, int taskCount, int minParallelChunkSize)
    {
        if (taskCount <= 0) throw new ArgumentOutOfRangeException(nameof(taskCount));
        if (minParallelChunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(minParallelChunkSize));

        var positions = new NativeBuffer<int>[lengths.Length];
        var velocities = new NativeBuffer<int>[lengths.Length];

        for (var a = 0; a < lengths.Length; a++)
        {
            var length = lengths[a];
            positions[a] = new NativeBuffer<int>(length);
            velocities[a] = new NativeBuffer<int>(length);

            var posSpan = positions[a].AsSpan();
            var velSpan = velocities[a].AsSpan();
            for (var i = 0; i < length; i++)
            {
                posSpan[i] = i;
                velSpan[i] = (i & 1) + 1;
            }
        }

        var archetypes = new NativeBuffer<ArchetypeDesc>(lengths.Length);
        var archetypeSpan = archetypes.AsSpan();
        for (var a = 0; a < lengths.Length; a++)
        {
            archetypeSpan[a] = new ArchetypeDesc
            {
                Position = positions[a].Ptr,
                Velocity = velocities[a].Ptr,
                Length = lengths[a]
            };
        }

        var slices = BuildSlices(lengths, taskCount, minParallelChunkSize);
        var sliceBuffer = new NativeBuffer<SliceDesc>(slices.Count);
        var sliceSpan = sliceBuffer.AsSpan();
        for (var i = 0; i < slices.Count; i++)
        {
            sliceSpan[i] = slices[i];
        }

        return new Data(positions, velocities, archetypes, sliceBuffer);
    }

    private static List<SliceDesc> BuildSlices(int[] lengths, int taskCount, int minParallelChunkSize)
    {
        var slices = new List<SliceDesc>();

        for (var archetypeIndex = 0; archetypeIndex < lengths.Length; archetypeIndex++)
        {
            var length = lengths[archetypeIndex];
            if (length <= 0)
            {
                continue;
            }

            if (length < minParallelChunkSize)
            {
                slices.Add(new SliceDesc
                {
                    ArchetypeIndex = archetypeIndex,
                    Start = 0,
                    Length = length
                });
                continue;
            }

            var sectionCount = Math.Min(taskCount, (length + minParallelChunkSize - 1) / minParallelChunkSize);
            var sectionSize = (length + sectionCount - 1) / sectionCount;

            for (var sectionIndex = 0; sectionIndex < sectionCount; sectionIndex++)
            {
                var start = sectionIndex * sectionSize;
                if (start >= length)
                {
                    break;
                }

                var sliceLength = Math.Min(sectionSize, length - start);
                slices.Add(new SliceDesc
                {
                    ArchetypeIndex = archetypeIndex,
                    Start = start,
                    Length = sliceLength
                });
            }
        }

        return slices;
    }
}
