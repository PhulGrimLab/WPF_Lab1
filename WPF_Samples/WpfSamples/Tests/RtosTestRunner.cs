using System.Diagnostics;
using Wpf.Lib.RTOS;

namespace WpfSamples.Tests;

internal static class RtosTestRunner
{
    public static async Task<IReadOnlyList<RtosTestResult>> RunAllAsync()
    {
        var tests = new (string Name, Func<Task> Execute)[]
        {
            ("Semaphore wait/release", SemaphoreWaitReleaseAsync),
            ("Semaphore rejects over-release", SemaphoreRejectsOverReleaseAsync),
            ("Semaphore wait observes cancellation", SemaphoreWaitObservesCancellationAsync),
            ("Semaphore throws after dispose", SemaphoreThrowsAfterDisposeAsync),
            ("MessageQueue send/receive", MessageQueueSendReceiveAsync),
            ("MessageQueue preserves FIFO order", MessageQueuePreservesFifoOrderAsync),
            ("MessageQueue receive timeout", MessageQueueReceiveTimeoutAsync),
            ("MessageQueue throws after dispose", MessageQueueThrowsAfterDisposeAsync),
            ("EventFlags wait-any completes when flag is set", EventFlagsWaitAnyCompletesAsync),
            ("EventFlags wait-all waits for all flags", EventFlagsWaitAllCompletesAsync),
            ("EventFlags wait observes timeout", EventFlagsWaitObservesTimeoutAsync),
            ("EventFlags wait observes cancellation", EventFlagsWaitObservesCancellationAsync),
            ("EventFlags dispose cancels pending wait", EventFlagsDisposeCancelsPendingWaitAsync),
            ("Mutex tracks owner and priority inheritance", MutexTracksOwnerAndPriorityInheritanceAsync),
            ("Mutex throws after dispose", MutexThrowsAfterDisposeAsync),
            ("SoftwareTimer one-shot fires once", SoftwareTimerOneShotFiresOnceAsync),
            ("SoftwareTimer periodic fires repeatedly", SoftwareTimerPeriodicFiresRepeatedlyAsync),
            ("SoftwareTimer periodic survives callback errors", SoftwareTimerPeriodicSurvivesCallbackErrorsAsync),
            ("SoftwareTimer dispose does not throw while callback runs", SoftwareTimerDisposeDoesNotThrowWhileCallbackRunsAsync),
            ("TickCounter converts time to ticks", TickCounterAsync),
            ("TickCounter rejects overflow tick", TickCounterRejectsOverflowTickAsync),
            ("Scheduler executes periodic task", SchedulerExecutesPeriodicTaskAsync),
            ("Scheduler runs higher priority task first", SchedulerRunsHigherPriorityTaskFirstAsync),
            ("Scheduler executes one-shot task once", SchedulerExecutesOneShotTaskOnceAsync),
            ("Scheduler stop timeout returns false", SchedulerStopTimeoutReturnsFalseAsync),
            ("Scheduler self stop with infinite timeout returns false", SchedulerSelfStopInfiniteTimeoutReturnsFalseAsync),
            ("Scheduler stop honors cancellation", SchedulerStopHonorsCancellationAsync),
            ("Scheduler unregister prevents future execution", SchedulerUnregisterPreventsFutureExecutionAsync),
            ("Scheduler clear removes all tasks", SchedulerClearRemovesAllTasksAsync),
            ("Scheduler reports task errors", SchedulerReportsTaskErrorsAsync),
            ("Scheduler survives snapshot handler errors", SchedulerSurvivesSnapshotHandlerErrorsAsync),
            ("Scheduler records trace entries", SchedulerRecordsTraceEntriesAsync),
            ("Scheduler snapshot contains runtime statistics", SchedulerSnapshotContainsRuntimeStatisticsAsync),
            ("Scheduler overrun task does not overlap", SchedulerOverrunTaskDoesNotOverlapAsync),
            ("Scheduler fixed-delay overrun waits after completion", SchedulerFixedDelayOverrunWaitsAfterCompletionAsync),
            ("Scheduler skip-missed overrun advances schedule", SchedulerSkipMissedOverrunAdvancesScheduleAsync),
            ("Scheduler snapshot contains task state", SchedulerSnapshotContainsTaskStateAsync),
            ("Scheduler snapshot survives faulty task properties", SchedulerSnapshotSurvivesFaultyTaskPropertiesAsync),
        };

        var results = new List<RtosTestResult>();

        foreach (var test in tests)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await test.Execute().ConfigureAwait(false);
                stopwatch.Stop();
                results.Add(new RtosTestResult(test.Name, true, "OK", stopwatch.Elapsed));
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                results.Add(new RtosTestResult(test.Name, false, ex.Message, stopwatch.Elapsed));
            }
        }

        return results;
    }

    private static async Task SemaphoreWaitReleaseAsync()
    {
        using var semaphore = new RtosSemaphore(initialCount: 1, maxCount: 1);

        var first = await semaphore.WaitAsync(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
        var second = await semaphore.WaitAsync(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);

        RtosAssert.True(first, "첫 번째 WaitAsync는 성공해야 합니다.");
        RtosAssert.True(!second, "카운트가 0이면 두 번째 WaitAsync는 timeout되어야 합니다.");

        semaphore.Release();
        var third = await semaphore.WaitAsync(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
        RtosAssert.True(third, "Release 이후 WaitAsync는 다시 성공해야 합니다.");
    }

    private static Task SemaphoreRejectsOverReleaseAsync()
    {
        using var semaphore = new RtosSemaphore(initialCount: 1, maxCount: 1);

        RtosAssert.Throws<SemaphoreFullException>(
            semaphore.Release,
            "Semaphore should reject Release when it is already full.");

        return Task.CompletedTask;
    }

    private static async Task SemaphoreWaitObservesCancellationAsync()
    {
        using var semaphore = new RtosSemaphore(initialCount: 0, maxCount: 1);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await RtosAssert.ThrowsAsync<OperationCanceledException>(
            () => semaphore.WaitAsync(TimeSpan.FromSeconds(1), cts.Token),
            "Semaphore WaitAsync should observe cancellation.").ConfigureAwait(false);
    }

    private static async Task MessageQueueSendReceiveAsync()
    {
        using var queue = new RtosMessageQueue<int>(capacity: 1);

        var sent = await queue.SendAsync(42, TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
        var fullSend = await queue.SendAsync(99, TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
        var received = await queue.ReceiveAsync(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);

        RtosAssert.True(sent, "첫 번째 SendAsync는 성공해야 합니다.");
        RtosAssert.True(!fullSend, "큐가 가득 차면 SendAsync는 timeout되어야 합니다.");
        RtosAssert.True(received.Success, "ReceiveAsync는 성공해야 합니다.");
        RtosAssert.Equal(42, received.Message, "수신한 메시지가 송신한 값과 같아야 합니다.");
    }

    private static async Task MessageQueuePreservesFifoOrderAsync()
    {
        using var queue = new RtosMessageQueue<int>(capacity: 3);

        await queue.SendAsync(1, TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
        await queue.SendAsync(2, TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
        await queue.SendAsync(3, TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);

        var first = await queue.ReceiveAsync(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
        var second = await queue.ReceiveAsync(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
        var third = await queue.ReceiveAsync(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);

        RtosAssert.Equal(1, first.Message, "Message queue should receive the first sent item first.");
        RtosAssert.Equal(2, second.Message, "Message queue should receive the second sent item second.");
        RtosAssert.Equal(3, third.Message, "Message queue should receive the third sent item third.");
    }

    private static async Task MessageQueueReceiveTimeoutAsync()
    {
        using var queue = new RtosMessageQueue<int>(capacity: 1);

        var received = await queue.ReceiveAsync(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);

        RtosAssert.True(!received.Success, "Empty queue receive should timeout.");
    }

    private static async Task SemaphoreThrowsAfterDisposeAsync()
    {
        var semaphore = new RtosSemaphore(initialCount: 1, maxCount: 1);

        semaphore.Dispose();

        await RtosAssert.ThrowsAsync<ObjectDisposedException>(
            () => semaphore.WaitAsync(TimeSpan.FromMilliseconds(10)),
            "Disposed semaphore should reject WaitAsync.").ConfigureAwait(false);
    }

    private static async Task MessageQueueThrowsAfterDisposeAsync()
    {
        var queue = new RtosMessageQueue<int>(capacity: 1);

        queue.Dispose();

        await RtosAssert.ThrowsAsync<ObjectDisposedException>(
            () => queue.SendAsync(42, TimeSpan.FromMilliseconds(10)),
            "Disposed message queue should reject SendAsync.").ConfigureAwait(false);
    }

    private static async Task EventFlagsWaitAnyCompletesAsync()
    {
        var eventFlags = new RtosEventFlags();
        var waitTask = eventFlags.WaitAnyAsync(0b0011, autoClear: true, TimeSpan.FromSeconds(1));

        eventFlags.Set(0b0010);

        var flags = await waitTask.ConfigureAwait(false);

        RtosAssert.Equal(0b0010u, flags, "WaitAny should complete with the matched flag.");
        RtosAssert.Equal(0u, eventFlags.CurrentFlags, "AutoClear should clear the matched flag.");
    }

    private static async Task EventFlagsWaitAllCompletesAsync()
    {
        var eventFlags = new RtosEventFlags();
        var waitTask = eventFlags.WaitAllAsync(0b0011, autoClear: false, TimeSpan.FromSeconds(1));

        eventFlags.Set(0b0001);
        RtosAssert.True(!waitTask.IsCompleted, "WaitAll should wait until every requested flag is set.");

        eventFlags.Set(0b0010);
        var flags = await waitTask.ConfigureAwait(false);

        RtosAssert.Equal(0b0011u, flags, "WaitAll should complete with all requested flags.");
        RtosAssert.Equal(0b0011u, eventFlags.CurrentFlags, "Flags should remain set when AutoClear is false.");
    }

    private static async Task EventFlagsWaitObservesTimeoutAsync()
    {
        var eventFlags = new RtosEventFlags();

        await RtosAssert.ThrowsAsync<TimeoutException>(
            () => eventFlags.WaitAnyAsync(0b0001, autoClear: false, TimeSpan.FromMilliseconds(20)),
            "Event flag wait should throw timeout when timeout elapses.").ConfigureAwait(false);
    }

    private static async Task EventFlagsWaitObservesCancellationAsync()
    {
        var eventFlags = new RtosEventFlags();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await RtosAssert.ThrowsAsync<OperationCanceledException>(
            () => eventFlags.WaitAllAsync(0b0011, autoClear: false, Timeout.InfiniteTimeSpan, cts.Token),
            "Event flag wait should observe external cancellation.").ConfigureAwait(false);
    }

    private static async Task EventFlagsDisposeCancelsPendingWaitAsync()
    {
        var eventFlags = new RtosEventFlags();
        var waitTask = eventFlags.WaitAllAsync(0b0011, autoClear: false, Timeout.InfiniteTimeSpan);

        eventFlags.Dispose();

        await RtosAssert.ThrowsAsync<ObjectDisposedException>(
            () => waitTask,
            "Disposing EventFlags should complete pending waits with ObjectDisposedException.").ConfigureAwait(false);

        await RtosAssert.ThrowsAsync<ObjectDisposedException>(
            () => eventFlags.WaitAnyAsync(0b0001, autoClear: false, TimeSpan.FromMilliseconds(10)),
            "Disposed EventFlags should reject new waits.").ConfigureAwait(false);
    }

    private static async Task MutexTracksOwnerAndPriorityInheritanceAsync()
    {
        using var mutex = new RtosMutex();

        var acquiredByLow = await mutex.WaitAsync("LowTask", Enum_TaskPriority.Low, TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
        var highWait = mutex.WaitAsync("HighTask", Enum_TaskPriority.Critical, TimeSpan.FromMilliseconds(80));

        await Task.Delay(20).ConfigureAwait(false);

        RtosAssert.True(acquiredByLow, "Low priority owner should acquire the mutex.");
        RtosAssert.Equal("LowTask", mutex.Owner, "Mutex owner should be tracked.");
        RtosAssert.Equal(Enum_TaskPriority.Critical, mutex.EffectiveOwnerPriority, "Waiting high priority task should boost effective owner priority.");

        mutex.Release("LowTask");
        var acquiredByHigh = await highWait.ConfigureAwait(false);

        RtosAssert.True(acquiredByHigh, "High priority waiter should acquire the mutex after release.");
        RtosAssert.Equal("HighTask", mutex.Owner, "Mutex owner should move to the next acquirer.");

        mutex.Release("HighTask");
    }

    private static async Task MutexThrowsAfterDisposeAsync()
    {
        var mutex = new RtosMutex();
        mutex.Dispose();

        await RtosAssert.ThrowsAsync<ObjectDisposedException>(
            () => mutex.WaitAsync("DisposedOwner", Enum_TaskPriority.Normal, TimeSpan.FromMilliseconds(10)),
            "Disposed mutex should reject WaitAsync.").ConfigureAwait(false);

        RtosAssert.Throws<ObjectDisposedException>(
            () => mutex.Release("DisposedOwner"),
            "Disposed mutex should reject Release.");
    }

    private static async Task SoftwareTimerOneShotFiresOnceAsync()
    {
        var fireCount = 0;
        await using var timer = new RtosSoftwareTimer(
            TimeSpan.FromMilliseconds(20),
            isPeriodic: false,
            _ =>
            {
                Interlocked.Increment(ref fireCount);
                return Task.CompletedTask;
            });

        timer.Start();
        await Task.Delay(80).ConfigureAwait(false);

        RtosAssert.Equal(1, Volatile.Read(ref fireCount), "One-shot software timer should fire once.");
    }

    private static async Task SoftwareTimerPeriodicFiresRepeatedlyAsync()
    {
        var fireCount = 0;
        await using var timer = new RtosSoftwareTimer(
            TimeSpan.FromMilliseconds(15),
            isPeriodic: true,
            _ =>
            {
                Interlocked.Increment(ref fireCount);
                return Task.CompletedTask;
            });

        timer.Start();
        await Task.Delay(70).ConfigureAwait(false);
        await timer.StopAsync().ConfigureAwait(false);

        RtosAssert.True(Volatile.Read(ref fireCount) >= 2, "Periodic software timer should fire repeatedly.");
    }

    private static async Task SoftwareTimerPeriodicSurvivesCallbackErrorsAsync()
    {
        var fireCount = 0;
        var errorCount = 0;

        await using var timer = new RtosSoftwareTimer(
            TimeSpan.FromMilliseconds(10),
            isPeriodic: true,
            _ =>
            {
                var count = Interlocked.Increment(ref fireCount);
                if (count == 1)
                {
                    throw new InvalidOperationException("First tick failure");
                }

                return Task.CompletedTask;
            });

        timer.TimerError += (_, _) => Interlocked.Increment(ref errorCount);

        timer.Start();
        await Task.Delay(80).ConfigureAwait(false);
        await timer.StopAsync().ConfigureAwait(false);

        RtosAssert.True(Volatile.Read(ref fireCount) >= 2, "Periodic timer should continue after callback errors.");
        RtosAssert.True(Volatile.Read(ref errorCount) >= 1, "Timer should report callback errors via TimerError.");
    }

    private static async Task SoftwareTimerDisposeDoesNotThrowWhileCallbackRunsAsync()
    {
        using var callbackStarted = new ManualResetEventSlim();
        var timer = new RtosSoftwareTimer(
            TimeSpan.FromMilliseconds(10),
            isPeriodic: true,
            async _ =>
            {
                callbackStarted.Set();
                await Task.Delay(80).ConfigureAwait(false);
            });

        timer.Start();
        callbackStarted.Wait(TimeSpan.FromSeconds(1));

        timer.Dispose();
        await Task.Delay(120).ConfigureAwait(false);

        RtosAssert.True(!timer.IsRunning, "Disposed software timer should no longer report running.");
    }

    private static Task TickCounterAsync()
    {
        var startedAt = new DateTimeOffset(2026, 5, 22, 0, 0, 0, TimeSpan.Zero);
        var tickCounter = new RtosTickCounter(TimeSpan.FromMilliseconds(10));

        tickCounter.Reset(startedAt);
        var tick = tickCounter.AdvanceTo(startedAt + TimeSpan.FromMilliseconds(35));

        RtosAssert.Equal(3L, tick, "35ms는 10ms tick 기준으로 3 tick이어야 합니다.");
        RtosAssert.Equal(startedAt + TimeSpan.FromMilliseconds(30), tickCounter.GetTimeForTick(3), "3 tick의 시각이 맞아야 합니다.");

        return Task.CompletedTask;
    }

    private static Task TickCounterRejectsOverflowTickAsync()
    {
        var tickCounter = new RtosTickCounter(TimeSpan.FromMilliseconds(10));

        RtosAssert.Throws<ArgumentOutOfRangeException>(
            () => tickCounter.GetTimeForTick(long.MaxValue),
            "TickCounter should reject ticks that overflow DateTimeOffset range.");

        return Task.CompletedTask;
    }

    private static async Task SchedulerExecutesPeriodicTaskAsync()
    {
        var runCount = 0;
        await using var scheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(20));

        scheduler.Register(new ScheduledTask(
            "Test Periodic",
            Enum_TaskPriority.Normal,
            TimeSpan.FromMilliseconds(10),
            Enum_TaskExecutionMode.Periodic,
            (_, _) =>
            {
                runCount++;
                return Task.CompletedTask;
            }));

        scheduler.Start();
        await Task.Delay(60).ConfigureAwait(false);
        await scheduler.StopAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        RtosAssert.True(runCount >= 2, "주기 태스크가 2회 이상 실행되어야 합니다.");
    }

    private static async Task SchedulerRunsHigherPriorityTaskFirstAsync()
    {
        var executionOrder = new List<string>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var scheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(50));

        scheduler.Register(CreateOrderedOneShotTask("Low", Enum_TaskPriority.Low, executionOrder, completed, expectedCount: 3));
        scheduler.Register(CreateOrderedOneShotTask("Critical", Enum_TaskPriority.Critical, executionOrder, completed, expectedCount: 3));
        scheduler.Register(CreateOrderedOneShotTask("High", Enum_TaskPriority.High, executionOrder, completed, expectedCount: 3));

        scheduler.Start();

        var finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
        await scheduler.StopAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        RtosAssert.True(finished == completed.Task, "Priority order test tasks should complete.");
        RtosAssert.Equal("Critical", executionOrder[0], "Critical task should run first.");
        RtosAssert.Equal("High", executionOrder[1], "High task should run second.");
        RtosAssert.Equal("Low", executionOrder[2], "Low task should run last.");
    }

    private static async Task SchedulerExecutesOneShotTaskOnceAsync()
    {
        var runCount = 0;

        await using var scheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(20));

        scheduler.Register(new ScheduledTask(
            "OneShot",
            Enum_TaskPriority.Normal,
            TimeSpan.FromMilliseconds(10),
            Enum_TaskExecutionMode.OneShot,
            (_, _) =>
            {
                runCount++;
                return Task.CompletedTask;
            }));

        scheduler.Start();
        await Task.Delay(80).ConfigureAwait(false);
        await scheduler.StopAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        RtosAssert.Equal(1, runCount, "One-shot task should run exactly once.");
    }

    private static async Task SchedulerStopTimeoutReturnsFalseAsync()
    {
        await using var scheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(20));

        scheduler.Register(new ScheduledTask(
            "Slow Stop",
            Enum_TaskPriority.Normal,
            TimeSpan.FromMilliseconds(10),
            Enum_TaskExecutionMode.OneShot,
            async (_, _) => await Task.Delay(200).ConfigureAwait(false)));

        scheduler.Start();
        await Task.Delay(20).ConfigureAwait(false);

        var stopped = await scheduler.StopAsync(TimeSpan.FromMilliseconds(20)).ConfigureAwait(false);

        RtosAssert.True(!stopped, "StopAsync(timeout) should return false when a task ignores cancellation and keeps running.");
    }

    private static async Task SchedulerSelfStopInfiniteTimeoutReturnsFalseAsync()
    {
        var selfStopResult = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var scheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(20));

        scheduler.Register(new ScheduledTask(
            "Self Stop",
            Enum_TaskPriority.Normal,
            TimeSpan.FromMilliseconds(10),
            Enum_TaskExecutionMode.OneShot,
            async (_, _) =>
            {
                var stopped = await scheduler.StopAsync(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
                selfStopResult.TrySetResult(stopped);
            }));

        scheduler.Start();

        var completed = await Task.WhenAny(selfStopResult.Task, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
        RtosAssert.True(completed == selfStopResult.Task, "Self-stop result should complete without deadlock.");
        RtosAssert.True(!selfStopResult.Task.Result, "Self-stop with infinite timeout should return false to avoid deadlock.");

        var stoppedOutside = await scheduler.StopAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        RtosAssert.True(stoppedOutside, "Scheduler should stop normally when requested outside the scheduler execution context.");
    }

    private static async Task SchedulerStopHonorsCancellationAsync()
    {
        await using var scheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(20));

        scheduler.Register(new ScheduledTask(
            "Cancelable Stop",
            Enum_TaskPriority.Normal,
            TimeSpan.FromMilliseconds(10),
            Enum_TaskExecutionMode.OneShot,
            async (_, cancellationToken) => await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false)));

        scheduler.Start();
        await Task.Delay(20).ConfigureAwait(false);

        var stopped = await scheduler.StopAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        RtosAssert.True(stopped, "StopAsync should complete when a task observes cancellation.");
    }

    private static async Task SchedulerUnregisterPreventsFutureExecutionAsync()
    {
        var runCount = 0;
        await using var scheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(20));

        var task = new ScheduledTask(
            "Unregister",
            Enum_TaskPriority.Normal,
            TimeSpan.FromMilliseconds(10),
            Enum_TaskExecutionMode.Periodic,
            (_, _) =>
            {
                Interlocked.Increment(ref runCount);
                return Task.CompletedTask;
            });

        scheduler.Register(task);
        scheduler.Start();
        await Task.Delay(50).ConfigureAwait(false);

        scheduler.Unregister(task);
        var runCountAfterUnregister = Volatile.Read(ref runCount);
        await Task.Delay(60).ConfigureAwait(false);
        await scheduler.StopAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        RtosAssert.Equal(runCountAfterUnregister, Volatile.Read(ref runCount), "Unregistered task should not run again.");
    }

    private static async Task SchedulerClearRemovesAllTasksAsync()
    {
        var runCount = 0;
        await using var scheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(20));

        scheduler.Register(new ScheduledTask(
            "Clear A",
            Enum_TaskPriority.Normal,
            TimeSpan.FromMilliseconds(10),
            Enum_TaskExecutionMode.Periodic,
            (_, _) =>
            {
                Interlocked.Increment(ref runCount);
                return Task.CompletedTask;
            }));

        scheduler.Register(new ScheduledTask(
            "Clear B",
            Enum_TaskPriority.Normal,
            TimeSpan.FromMilliseconds(10),
            Enum_TaskExecutionMode.Periodic,
            (_, _) =>
            {
                Interlocked.Increment(ref runCount);
                return Task.CompletedTask;
            }));

        scheduler.Start();
        await Task.Delay(50).ConfigureAwait(false);

        scheduler.Clear();
        var runCountAfterClear = Volatile.Read(ref runCount);
        await Task.Delay(60).ConfigureAwait(false);
        await scheduler.StopAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        RtosAssert.Equal(runCountAfterClear, Volatile.Read(ref runCount), "Cleared tasks should not run again.");
    }

    private static async Task SchedulerReportsTaskErrorsAsync()
    {
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var scheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(20));

        scheduler.SchedulerError += (_, exception) => reported.TrySetResult(exception);
        scheduler.Register(new ScheduledTask(
            "Fault",
            Enum_TaskPriority.Normal,
            TimeSpan.FromMilliseconds(10),
            Enum_TaskExecutionMode.OneShot,
            (_, _) => throw new InvalidOperationException("Task failed.")));

        scheduler.Start();

        var completed = await Task.WhenAny(reported.Task, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
        await scheduler.StopAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        RtosAssert.True(completed == reported.Task, "SchedulerError should be raised for task exceptions.");
        RtosAssert.True(reported.Task.Result is InvalidOperationException, "SchedulerError should carry the task exception.");
    }

    private static async Task SchedulerSurvivesSnapshotHandlerErrorsAsync()
    {
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var healthyHandlerCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var scheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(20));

        scheduler.SchedulerError += (_, exception) => reported.TrySetResult(exception);
        scheduler.SnapshotChanged += (_, _) => throw new InvalidOperationException("Snapshot handler failed.");
        scheduler.SnapshotChanged += (_, _) => healthyHandlerCalled.TrySetResult();
        scheduler.Register(new ScheduledTask(
            "Snapshot",
            Enum_TaskPriority.Normal,
            TimeSpan.FromMilliseconds(10),
            Enum_TaskExecutionMode.OneShot,
            (_, _) => Task.CompletedTask));

        var errorCompleted = await Task.WhenAny(reported.Task, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
        var healthyCompleted = await Task.WhenAny(healthyHandlerCalled.Task, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);

        RtosAssert.True(errorCompleted == reported.Task, "Snapshot handler exceptions should be reported.");
        RtosAssert.True(healthyCompleted == healthyHandlerCalled.Task, "A failing snapshot handler should not prevent other handlers.");
    }

    private static async Task SchedulerRecordsTraceEntriesAsync()
    {
        await using var scheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(20));

        scheduler.Register(new ScheduledTask(
            "Trace",
            Enum_TaskPriority.Normal,
            TimeSpan.FromMilliseconds(10),
            Enum_TaskExecutionMode.OneShot,
            (_, _) => Task.CompletedTask));

        scheduler.Start();
        await Task.Delay(40).ConfigureAwait(false);
        await scheduler.StopAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        var trace = scheduler.TraceLog.Snapshot();

        RtosAssert.True(trace.Any(entry => entry.Message.Contains("registered", StringComparison.OrdinalIgnoreCase)), "Trace log should record task registration.");
        RtosAssert.True(trace.Any(entry => entry.Message.Contains("completed", StringComparison.OrdinalIgnoreCase)), "Trace log should record task completion.");
    }

    private static async Task SchedulerSnapshotContainsRuntimeStatisticsAsync()
    {
        SchedulerSnapshot? latestSnapshot = null;

        await using var scheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(20));

        scheduler.SnapshotChanged += (_, snapshot) => latestSnapshot = snapshot;
        scheduler.Register(new ScheduledTask(
            "Stats",
            Enum_TaskPriority.Normal,
            TimeSpan.FromMilliseconds(5),
            Enum_TaskExecutionMode.Periodic,
            async (_, _) => await Task.Delay(15).ConfigureAwait(false)));

        scheduler.Start();
        await Task.Delay(80).ConfigureAwait(false);
        await scheduler.StopAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        var taskSnapshot = latestSnapshot?.Tasks.FirstOrDefault(task => task.Name == "Stats");

        RtosAssert.True(taskSnapshot is not null, "Stats task should be present in snapshot.");
        RtosAssert.True(taskSnapshot!.MaxDuration is not null, "Snapshot should contain max duration.");
        RtosAssert.True(taskSnapshot.AverageDuration is not null, "Snapshot should contain average duration.");
        RtosAssert.True(taskSnapshot.DeadlineMissCount > 0, "Long-running task should report deadline misses.");
    }

    private static async Task SchedulerOverrunTaskDoesNotOverlapAsync()
    {
        var runCount = 0;
        var concurrentCount = 0;
        var maxConcurrentCount = 0;

        await using var scheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(20));

        scheduler.Register(new ScheduledTask(
            "Overrun",
            Enum_TaskPriority.Normal,
            TimeSpan.FromMilliseconds(10),
            Enum_TaskExecutionMode.Periodic,
            async (_, _) =>
            {
                var current = Interlocked.Increment(ref concurrentCount);
                maxConcurrentCount = Math.Max(maxConcurrentCount, current);

                try
                {
                    Interlocked.Increment(ref runCount);
                    await Task.Delay(35).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref concurrentCount);
                }
            }));

        scheduler.Start();
        await Task.Delay(120).ConfigureAwait(false);
        await scheduler.StopAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        RtosAssert.True(runCount >= 2, "Overrun task should run more than once.");
        RtosAssert.Equal(1, maxConcurrentCount, "Cooperative scheduler should not overlap executions of the same task.");
    }

    private static async Task SchedulerFixedDelayOverrunWaitsAfterCompletionAsync()
    {
        var firstCompletedAt = DateTimeOffset.MinValue;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScheduledTask? task = null;

        await using var scheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(20));

        task = new ScheduledTask(
            "Fixed Delay",
            Enum_TaskPriority.Normal,
            TimeSpan.FromMilliseconds(50),
            Enum_TaskExecutionMode.Periodic,
            async (_, _) =>
            {
                await Task.Delay(30).ConfigureAwait(false);
                firstCompletedAt = DateTimeOffset.Now;
                completed.TrySetResult();
            },
            overrunPolicy: Enum_TaskOverrunPolicy.FixedDelay);

        scheduler.Register(task);
        scheduler.Start();

        var finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
        await Task.Delay(20).ConfigureAwait(false);
        await scheduler.StopAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        RtosAssert.True(finished == completed.Task, "Fixed-delay task should complete once.");
        RtosAssert.True(task.NextRunAt >= firstCompletedAt + TimeSpan.FromMilliseconds(50), "Fixed-delay policy should schedule from task completion time.");
    }

    private static async Task SchedulerSkipMissedOverrunAdvancesScheduleAsync()
    {
        var firstCompletedAt = DateTimeOffset.MinValue;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScheduledTask? task = null;

        await using var scheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(20));

        task = new ScheduledTask(
            "Skip Missed",
            Enum_TaskPriority.Normal,
            TimeSpan.FromMilliseconds(10),
            Enum_TaskExecutionMode.Periodic,
            async (_, _) =>
            {
                await Task.Delay(35).ConfigureAwait(false);
                firstCompletedAt = DateTimeOffset.Now;
                completed.TrySetResult();
            },
            overrunPolicy: Enum_TaskOverrunPolicy.SkipMissedTicks);

        scheduler.Register(task);
        scheduler.Start();

        var finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
        await Task.Delay(20).ConfigureAwait(false);
        await scheduler.StopAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        RtosAssert.True(finished == completed.Task, "Skip-missed task should complete once.");
        RtosAssert.True(task.NextRunAt > firstCompletedAt, "Skip-missed policy should advance the next run after the overrun completion time.");
    }

    private static async Task SchedulerSnapshotContainsTaskStateAsync()
    {
        SchedulerSnapshot? latestSnapshot = null;

        await using var scheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(20));

        scheduler.SnapshotChanged += (_, snapshot) => latestSnapshot = snapshot;
        scheduler.Register(new ScheduledTask(
            "State Test",
            Enum_TaskPriority.Normal,
            TimeSpan.FromMilliseconds(50),
            Enum_TaskExecutionMode.Periodic,
            (_, _) => Task.CompletedTask));

        scheduler.Start();
        await Task.Delay(80).ConfigureAwait(false);
        await scheduler.StopAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        var taskSnapshot = latestSnapshot?.Tasks.FirstOrDefault(task => task.Name == "State Test");
        RtosAssert.True(taskSnapshot is not null, "스냅샷에 테스트 태스크가 있어야 합니다.");
        RtosAssert.True(
            taskSnapshot!.State is Enum_TaskState.Ready or Enum_TaskState.Blocked,
            "태스크 상태가 Ready 또는 Blocked로 표시되어야 합니다.");
    }

    private static Task SchedulerSnapshotSurvivesFaultyTaskPropertiesAsync()
    {
        SchedulerSnapshot? latestSnapshot = null;

        using var scheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(20));

        scheduler.SnapshotChanged += (_, snapshot) => latestSnapshot = snapshot;
        scheduler.Register(new FaultySnapshotTask());

        var taskSnapshot = latestSnapshot?.Tasks.FirstOrDefault(task => task.Name == nameof(FaultySnapshotTask));

        RtosAssert.True(taskSnapshot is not null, "Faulty task should still be visible in the scheduler snapshot.");
        RtosAssert.True(
            taskSnapshot!.Status.StartsWith("Status error:", StringComparison.Ordinal),
            "Faulty task status should be reported as a status error.");

        return Task.CompletedTask;
    }

    private static ScheduledTask CreateOrderedOneShotTask(
        string name,
        Enum_TaskPriority priority,
        List<string> executionOrder,
        TaskCompletionSource completed,
        int expectedCount)
    {
        return new ScheduledTask(
            name,
            priority,
            TimeSpan.FromMilliseconds(10),
            Enum_TaskExecutionMode.OneShot,
            (_, _) =>
            {
                executionOrder.Add(name);
                if (executionOrder.Count == expectedCount)
                {
                    completed.TrySetResult();
                }

                return Task.CompletedTask;
            });
    }

    private sealed class FaultySnapshotTask : IScheduledTask
    {
        public string Name => throw new InvalidOperationException("Name is unavailable.");

        public Enum_TaskPriority Priority => Enum_TaskPriority.Normal;

        public TimeSpan Period => TimeSpan.FromMilliseconds(10);

        public Enum_TaskExecutionMode Mode => Enum_TaskExecutionMode.Periodic;

        public Enum_TaskOverrunPolicy OverrunPolicy => Enum_TaskOverrunPolicy.FixedRate;

        public DateTimeOffset NextRunAt { get; set; } = DateTimeOffset.MaxValue;

        public bool IsEnabled => true;

        public string Status => throw new InvalidOperationException("Status is unavailable.");

        public Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
        public void SetEnabled(bool isEnabled)
        {
            // 테스트 태스크이므로 no-op
        }
    }
}
