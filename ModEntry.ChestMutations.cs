using StardewModdingAPI;
using StardewValley.Objects;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private const int MaximumChestMutationsPerLock = 64;

    private readonly Dictionary<Chest, PendingChestMutationQueue> pendingChestMutationQueues =
        new(ReferenceEqualityComparer.Instance);
    private int chestMutationGeneration;

    private enum ChestMutationQueueResult
    {
        Queued,
        CompletedSuccessfully,
        CompletedUnsuccessfully
    }

    private sealed class PendingChestMutation
    {
        public Func<bool> ExecuteLocked { get; init; } = null!;
        public Action LockFailed { get; init; } = null!;
        public string Description { get; init; } = "";
        public bool Completed { get; set; }
        public bool Result { get; set; }
    }

    private sealed class PendingChestMutationQueue
    {
        public Chest Chest { get; init; } = null!;
        public int Generation { get; init; }
        public Queue<PendingChestMutation> Operations { get; } = new();
        public bool RequestInFlight { get; set; }
        public bool Cancelled { get; set; }
    }

    /// <summary>
    /// Serialize mutations by the exact live chest instance. Only the callback
    /// which acquired the mutex releases it; an unrelated lock already held by
    /// this peer is never adopted or released here.
    /// </summary>
    private ChestMutationQueueResult QueueChestMutation(
        Chest chest,
        Func<bool> executeLocked,
        Action lockFailed,
        string description)
    {
        ArgumentNullException.ThrowIfNull(chest);
        ArgumentNullException.ThrowIfNull(executeLocked);
        ArgumentNullException.ThrowIfNull(lockFailed);

        if (!this.pendingChestMutationQueues.TryGetValue(
                chest,
                out PendingChestMutationQueue? queue)
            || queue.Cancelled
            || queue.Generation != this.chestMutationGeneration)
        {
            queue = new PendingChestMutationQueue
            {
                Chest = chest,
                Generation = this.chestMutationGeneration
            };
            this.pendingChestMutationQueues[chest] = queue;
        }

        PendingChestMutation operation = new()
        {
            ExecuteLocked = executeLocked,
            LockFailed = lockFailed,
            Description = description
        };
        queue.Operations.Enqueue(operation);
        this.StartChestMutationQueue(queue);

        if (!operation.Completed)
            return ChestMutationQueueResult.Queued;
        return operation.Result
            ? ChestMutationQueueResult.CompletedSuccessfully
            : ChestMutationQueueResult.CompletedUnsuccessfully;
    }

    private void StartChestMutationQueue(PendingChestMutationQueue queue)
    {
        if (queue.RequestInFlight
            || queue.Cancelled
            || queue.Generation != this.chestMutationGeneration
            || queue.Operations.Count == 0)
        {
            return;
        }

        // Set this before RequestLock because NetMutex may complete callbacks
        // synchronously on the authoritative peer.
        queue.RequestInFlight = true;
        try
        {
            queue.Chest.GetMutex().RequestLock(
                acquired: () => this.ProcessAcquiredChestMutationQueue(queue),
                failed: () => this.FailChestMutationQueue(queue));
        }
        catch (Exception ex)
        {
            this.Monitor.Log(
                $"Could not request a serialized companion chest lock: {ex}",
                LogLevel.Error);
            this.FailChestMutationQueue(queue);
        }
    }

    private void ProcessAcquiredChestMutationQueue(
        PendingChestMutationQueue queue)
    {
        bool ownsLock = true;
        try
        {
            if (queue.Cancelled
                || queue.Generation != this.chestMutationGeneration
                || !this.IsCurrentChestMutationQueue(queue))
            {
                return;
            }

            int batchSize = Math.Min(
                queue.Operations.Count,
                MaximumChestMutationsPerLock);
            for (int index = 0; index < batchSize; index++)
            {
                if (!queue.Operations.TryDequeue(
                        out PendingChestMutation? operation))
                {
                    break;
                }

                try
                {
                    operation.Result = operation.ExecuteLocked();
                }
                catch (Exception ex)
                {
                    operation.Result = false;
                    this.Monitor.Log(
                        $"Serialized chest mutation '{operation.Description}' failed: {ex}",
                        LogLevel.Error);
                    this.InvokeChestMutationLockFailed(operation);
                }
                finally
                {
                    operation.Completed = true;
                }
            }
        }
        finally
        {
            if (ownsLock)
            {
                try
                {
                    if (queue.Chest.GetMutex().IsLockHeld())
                        queue.Chest.GetMutex().ReleaseLock();
                }
                catch (Exception ex)
                {
                    this.Monitor.Log(
                        $"Could not release a serialized companion chest lock: {ex.Message}",
                        LogLevel.Warn);
                }
            }

            queue.RequestInFlight = false;
            if (queue.Cancelled
                || queue.Generation != this.chestMutationGeneration
                || queue.Operations.Count == 0)
            {
                this.RemoveChestMutationQueue(queue);
            }
            else
            {
                // A bounded follow-up acquisition prevents callbacks which
                // enqueue more work from monopolizing one update tick.
                this.StartChestMutationQueue(queue);
            }
        }
    }

    private void FailChestMutationQueue(PendingChestMutationQueue queue)
    {
        if (queue.Cancelled
            || queue.Generation != this.chestMutationGeneration
            || !this.IsCurrentChestMutationQueue(queue))
        {
            queue.RequestInFlight = false;
            return;
        }

        queue.RequestInFlight = false;
        while (queue.Operations.TryDequeue(
            out PendingChestMutation? operation))
        {
            operation.Result = false;
            operation.Completed = true;
            this.InvokeChestMutationLockFailed(operation);
        }
        this.RemoveChestMutationQueue(queue);
    }

    private void InvokeChestMutationLockFailed(
        PendingChestMutation operation)
    {
        try
        {
            operation.LockFailed();
        }
        catch (Exception ex)
        {
            this.Monitor.Log(
                $"Chest-lock failure handler for '{operation.Description}' failed: {ex}",
                LogLevel.Error);
        }
    }

    private bool IsCurrentChestMutationQueue(
        PendingChestMutationQueue queue)
    {
        return this.pendingChestMutationQueues.TryGetValue(
                queue.Chest,
                out PendingChestMutationQueue? current)
            && ReferenceEquals(current, queue);
    }

    private void RemoveChestMutationQueue(
        PendingChestMutationQueue queue)
    {
        if (this.IsCurrentChestMutationQueue(queue))
            this.pendingChestMutationQueues.Remove(queue.Chest);
    }

    /// <summary>Invalidate callbacks retained by NetMutex when a save is closed.</summary>
    private void CancelPendingChestMutations()
    {
        this.chestMutationGeneration =
            this.chestMutationGeneration == int.MaxValue
                ? 0
                : this.chestMutationGeneration + 1;
        foreach (PendingChestMutationQueue queue
            in this.pendingChestMutationQueues.Values)
        {
            queue.Cancelled = true;
            while (queue.Operations.TryDequeue(
                out PendingChestMutation? operation))
            {
                operation.Result = false;
                operation.Completed = true;
            }
        }
        this.pendingChestMutationQueues.Clear();
    }
}
