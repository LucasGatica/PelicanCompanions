namespace PelicanCompanions;

/// <summary>
/// Keeps a bounded, per-player window of processed command IDs.
/// </summary>
/// <remarks>
/// Multiplayer messages can be delivered more than once. Keeping this concern
/// outside <see cref="ModEntry"/> makes idempotency independent from the rest of
/// the save/runtime state and gives it one explicit reset point.
/// </remarks>
internal sealed class CommandReplayGuard
{
    private const int MaximumCommandIdLength = 64;

    private readonly int capacityPerPlayer;
    private readonly Dictionary<long, PlayerCommandWindow> windows = new();

    public CommandReplayGuard(int capacityPerPlayer = 128)
    {
        if (capacityPerPlayer <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacityPerPlayer), "The replay window must contain at least one command.");

        this.capacityPerPlayer = capacityPerPlayer;
    }

    public bool TryRegister(long playerId, string? commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId) || commandId.Length > MaximumCommandIdLength)
            return false;

        if (!this.windows.TryGetValue(playerId, out PlayerCommandWindow? window))
        {
            window = new PlayerCommandWindow();
            this.windows.Add(playerId, window);
        }

        if (!window.Ids.Add(commandId))
            return false;

        window.OrderedIds.Enqueue(commandId);
        while (window.OrderedIds.Count > this.capacityPerPlayer)
            window.Ids.Remove(window.OrderedIds.Dequeue());

        return true;
    }

    public void RemovePlayer(long playerId)
    {
        this.windows.Remove(playerId);
    }

    public void Clear()
    {
        this.windows.Clear();
    }

    private sealed class PlayerCommandWindow
    {
        public Queue<string> OrderedIds { get; } = new();

        public HashSet<string> Ids { get; } = new(StringComparer.Ordinal);
    }
}
