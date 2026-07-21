namespace PelicanCompanions;

/// <summary>A bounded, owner-scoped queue for companion dialogue and pet expressions.</summary>
internal sealed class CompanionDialogueScheduler
{
    public const int MaxPendingPerOwner = 12;
    public const int GroupHistoryLimit = 8;
    public const int SpeakerHistoryLimit = 4;
    public const int IdentityIntervalHistoryLimit = 64;

    private readonly Dictionary<long, OwnerQueueState> owners = new();
    private long nextSequence;

    public void Clear()
    {
        this.owners.Clear();
        this.nextSequence = 0;
    }

    public bool Enqueue(CompanionDialogueRequest request)
    {
        if (request.OwnerId <= 0
            || string.IsNullOrWhiteSpace(request.NpcName)
            || string.IsNullOrWhiteSpace(request.Category)
            || request.TtlTicks <= 0)
        {
            return false;
        }

        OwnerQueueState owner = this.GetOwner(request.OwnerId);
        string dedupeKey = string.IsNullOrWhiteSpace(request.DedupeKey)
            ? $"{request.NpcName}|{request.Category}"
            : request.DedupeKey;
        PendingEntry? existing = owner.Pending.FirstOrDefault(entry =>
            string.Equals(entry.DedupeKey, dedupeKey, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (request.Priority < existing.Request.Priority)
                return false;

            existing.Request = request;
            return true;
        }

        if (owner.Pending.Count >= MaxPendingPerOwner)
        {
            PendingEntry? eviction = owner.Pending
                .OrderBy(entry => entry.Request.Priority)
                .ThenBy(entry => entry.Sequence)
                .FirstOrDefault();
            if (eviction is null || eviction.Request.Priority > request.Priority)
                return false;

            owner.Pending.Remove(eviction);
        }

        owner.Pending.Add(new PendingEntry(request, dedupeKey, this.nextSequence++));
        return true;
    }

    public bool TryDequeue(long ownerId, int nowTick, int sharedGapTicks, out CompanionDialogueRequest request)
    {
        request = default;
        if (!this.owners.TryGetValue(ownerId, out OwnerQueueState? owner))
            return false;

        owner.Pending.RemoveAll(entry => HasElapsed(nowTick, entry.Request.CreatedTick, entry.Request.TtlTicks));
        if (owner.Pending.Count == 0)
            return false;

        int requiredGapTicks = Math.Max(1, sharedGapTicks);
        if (owner.LastPresentedTick is int lastPresented
            && !HasElapsed(nowTick, lastPresented, requiredGapTicks))
        {
            return false;
        }

        PendingEntry next = owner.Pending
            .OrderByDescending(entry => entry.Request.Priority)
            .ThenBy(entry => entry.Sequence)
            .First();
        owner.Pending.Remove(next);
        request = next.Request;
        return true;
    }

    public bool CanPresent(long ownerId, int nowTick, int sharedGapTicks)
    {
        OwnerQueueState owner = this.GetOwner(ownerId);
        return owner.LastPresentedTick is not int lastPresented
            || HasElapsed(nowTick, lastPresented, Math.Max(1, sharedGapTicks));
    }

    public bool CanPresentIdentity(long ownerId, string identity, int nowTick, int minimumGapTicks)
    {
        if (minimumGapTicks <= 0)
            return true;
        if (!this.owners.TryGetValue(ownerId, out OwnerQueueState? owner))
            return true;

        PruneExpiredIdentityIntervals(owner, nowTick, identity);
        if (owner.LastPresentedByIdentity.TryGetValue(identity, out IdentityIntervalState presented))
        {
            if (!HasElapsed(nowTick, presented.LastPresentedTick, minimumGapTicks))
                return false;

            owner.LastPresentedByIdentity.Remove(identity);
        }

        // Never evict an identity whose interval is still active. If content
        // defines more simultaneous long intervals than the bounded history can
        // hold, defer unseen lines until one protected slot expires.
        return owner.LastPresentedByIdentity.Count < IdentityIntervalHistoryLimit;
    }

    public IReadOnlyList<long> GetOwnersWithPending()
    {
        return this.owners
            .Where(pair => pair.Value.Pending.Count > 0)
            .Select(pair => pair.Key)
            .ToArray();
    }

    public bool CanScheduleAmbient(long ownerId, int nowTick, int intervalTicks)
    {
        if (intervalTicks <= 0)
            return false;

        OwnerQueueState owner = this.GetOwner(ownerId);
        return owner.LastAmbientAttemptTick is not int last
            || HasElapsed(nowTick, last, intervalTicks);
    }

    public void MarkAmbientAttempt(long ownerId, int nowTick)
    {
        this.GetOwner(ownerId).LastAmbientAttemptTick = nowTick;
    }

    public void MarkPresented(
        long ownerId,
        string npcName,
        string identity,
        int nowTick,
        int minimumGapTicks = 0)
    {
        OwnerQueueState owner = this.GetOwner(ownerId);
        owner.LastPresentedTick = nowTick;
        owner.LastSpeakerName = npcName;
        PruneExpiredIdentityIntervals(owner, nowTick, identity);
        if (minimumGapTicks > 0
            && (owner.LastPresentedByIdentity.ContainsKey(identity)
                || owner.LastPresentedByIdentity.Count < IdentityIntervalHistoryLimit))
        {
            owner.LastPresentedByIdentity[identity] = new IdentityIntervalState(nowTick, minimumGapTicks);
        }
        else if (minimumGapTicks <= 0)
            owner.LastPresentedByIdentity.Remove(identity);
        Remember(owner.RecentIdentities, identity, GroupHistoryLimit);

        if (!owner.RecentBySpeaker.TryGetValue(npcName, out List<string>? speakerHistory))
        {
            speakerHistory = new List<string>();
            owner.RecentBySpeaker[npcName] = speakerHistory;
        }
        Remember(speakerHistory, identity, SpeakerHistoryLimit);
    }

    public IReadOnlyList<string> GetRecentIdentities(long ownerId, string npcName)
    {
        if (!this.owners.TryGetValue(ownerId, out OwnerQueueState? owner))
            return Array.Empty<string>();

        List<string> recent = new(owner.RecentIdentities);
        if (owner.RecentBySpeaker.TryGetValue(npcName, out List<string>? speakerHistory))
        {
            foreach (string identity in speakerHistory)
            {
                if (!recent.Contains(identity, StringComparer.OrdinalIgnoreCase))
                    recent.Add(identity);
            }
        }
        return recent;
    }

    public string? GetLastSpeaker(long ownerId)
    {
        return this.owners.TryGetValue(ownerId, out OwnerQueueState? owner)
            ? owner.LastSpeakerName
            : null;
    }

    internal static bool HasElapsed(int nowTick, int thenTick, int requiredTicks)
    {
        return unchecked((uint)(nowTick - thenTick)) >= (uint)Math.Max(0, requiredTicks);
    }

    private OwnerQueueState GetOwner(long ownerId)
    {
        if (!this.owners.TryGetValue(ownerId, out OwnerQueueState? owner))
        {
            owner = new OwnerQueueState();
            this.owners[ownerId] = owner;
        }
        return owner;
    }

    private static void PruneExpiredIdentityIntervals(
        OwnerQueueState owner,
        int nowTick,
        string? excludedIdentity = null)
    {
        foreach (string identity in owner.LastPresentedByIdentity
            .Where(pair => !string.Equals(pair.Key, excludedIdentity, StringComparison.OrdinalIgnoreCase)
                && HasElapsed(nowTick, pair.Value.LastPresentedTick, pair.Value.MinimumGapTicks))
            .Select(pair => pair.Key)
            .ToArray())
        {
            owner.LastPresentedByIdentity.Remove(identity);
        }
    }

    private static void Remember(List<string> history, string identity, int limit)
    {
        if (string.IsNullOrWhiteSpace(identity))
            return;

        history.RemoveAll(value => string.Equals(value, identity, StringComparison.OrdinalIgnoreCase));
        history.Insert(0, identity);
        if (history.Count > limit)
            history.RemoveRange(limit, history.Count - limit);
    }

    private sealed class OwnerQueueState
    {
        public List<PendingEntry> Pending { get; } = new();
        public List<string> RecentIdentities { get; } = new();
        public Dictionary<string, List<string>> RecentBySpeaker { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, IdentityIntervalState> LastPresentedByIdentity { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int? LastPresentedTick { get; set; }
        public int? LastAmbientAttemptTick { get; set; }
        public string? LastSpeakerName { get; set; }
    }

    private readonly record struct IdentityIntervalState(int LastPresentedTick, int MinimumGapTicks);

    private sealed class PendingEntry
    {
        public PendingEntry(CompanionDialogueRequest request, string dedupeKey, long sequence)
        {
            this.Request = request;
            this.DedupeKey = dedupeKey;
            this.Sequence = sequence;
        }

        public CompanionDialogueRequest Request { get; set; }
        public string DedupeKey { get; }
        public long Sequence { get; }
    }
}

internal readonly record struct CompanionDialogueRequest(
    long OwnerId,
    string NpcName,
    string Category,
    CompanionDialoguePriority Priority,
    CompanionDialogueContext Context,
    bool Force,
    int CreatedTick,
    int TtlTicks,
    string DedupeKey);
