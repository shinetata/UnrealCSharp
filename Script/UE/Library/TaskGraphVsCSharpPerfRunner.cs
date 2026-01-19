using System;

namespace Script.Library;

public static class TaskGraphVsCSharpPerfRunner
{
    public static void RunAddOneAndSumInt32ParallelCompare(
        int length = 100_000,
        int taskCount = 8,
        int iterations = 32,
        int warmup = 3,
        int rounds = 5)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (taskCount <= 0) throw new ArgumentOutOfRangeException(nameof(taskCount));
        if (iterations <= 0) throw new ArgumentOutOfRangeException(nameof(iterations));
        if (warmup < 0) throw new ArgumentOutOfRangeException(nameof(warmup));
        if (rounds <= 0) throw new ArgumentOutOfRangeException(nameof(rounds));

        var safeTaskCount = Math.Clamp(taskCount, 1, length);
        var taskGraphMs = new double[rounds];
        var parallelForMs = new double[rounds];
        var taskRunMs = new double[rounds];

        for (var r = 0; r < rounds; r++)
        {
            var orderIndex = r % 3;
            long sumTaskGraph = 0;
            long sumParallelFor = 0;
            long sumTaskRun = 0;
            double tgMs;
            double pfMs;
            double trMs;
            string order;

            if (orderIndex == 0)
            {
                order = "TG->PF->TR";
                tgMs = TaskGraphNativeKernelPerf.MeasureAddOneAndSumInt32Parallel(
                    length,
                    safeTaskCount,
                    iterations,
                    warmup,
                    out sumTaskGraph);

                pfMs = CSharpParallelPerf.MeasureAddOneAndSumInt32ParallelFor(
                    length,
                    safeTaskCount,
                    iterations,
                    warmup,
                    out sumParallelFor);

                trMs = CSharpParallelPerf.MeasureAddOneAndSumInt32TaskRun(
                    length,
                    safeTaskCount,
                    iterations,
                    warmup,
                    out sumTaskRun);
            }
            else if (orderIndex == 1)
            {
                order = "PF->TR->TG";
                pfMs = CSharpParallelPerf.MeasureAddOneAndSumInt32ParallelFor(
                    length,
                    safeTaskCount,
                    iterations,
                    warmup,
                    out sumParallelFor);

                trMs = CSharpParallelPerf.MeasureAddOneAndSumInt32TaskRun(
                    length,
                    safeTaskCount,
                    iterations,
                    warmup,
                    out sumTaskRun);

                tgMs = TaskGraphNativeKernelPerf.MeasureAddOneAndSumInt32Parallel(
                    length,
                    safeTaskCount,
                    iterations,
                    warmup,
                    out sumTaskGraph);
            }
            else
            {
                order = "TR->TG->PF";
                trMs = CSharpParallelPerf.MeasureAddOneAndSumInt32TaskRun(
                    length,
                    safeTaskCount,
                    iterations,
                    warmup,
                    out sumTaskRun);

                tgMs = TaskGraphNativeKernelPerf.MeasureAddOneAndSumInt32Parallel(
                    length,
                    safeTaskCount,
                    iterations,
                    warmup,
                    out sumTaskGraph);

                pfMs = CSharpParallelPerf.MeasureAddOneAndSumInt32ParallelFor(
                    length,
                    safeTaskCount,
                    iterations,
                    warmup,
                    out sumParallelFor);
            }

            taskGraphMs[r] = tgMs;
            parallelForMs[r] = pfMs;
            taskRunMs[r] = trMs;

            var sumOk = sumTaskGraph == sumParallelFor && sumTaskGraph == sumTaskRun;
            Console.WriteLine(
                $"[PerfRound] round={r + 1}/{rounds} order={order} " +
                $"tg={tgMs:F3}ms pf={pfMs:F3}ms tr={trMs:F3}ms sumOk={sumOk}");
        }

        var taskGraphMedian = Median(taskGraphMs);
        var parallelForMedian = Median(parallelForMs);
        var taskRunMedian = Median(taskRunMs);
        var ratioParallelFor = parallelForMedian / Math.Max(0.000001, taskGraphMedian);
        var ratioTaskRun = taskRunMedian / Math.Max(0.000001, taskGraphMedian);

        Console.WriteLine(
            $"[PerfSummary] length={length} taskCount={safeTaskCount} iterations={iterations} warmup={warmup} rounds={rounds} " +
            $"tgMedian={taskGraphMedian:F3}ms pfMedian={parallelForMedian:F3}ms trMedian={taskRunMedian:F3}ms " +
            $"ratioPf={ratioParallelFor:F3} ratioTr={ratioTaskRun:F3}");
    }

    private static double Median(double[] values)
    {
        var copy = new double[values.Length];
        Array.Copy(values, copy, values.Length);
        Array.Sort(copy);

        var mid = copy.Length / 2;
        if (copy.Length % 2 == 1)
        {
            return copy[mid];
        }

        return (copy[mid - 1] + copy[mid]) * 0.5;
    }
}
