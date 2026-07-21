using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Tools;

namespace PelicanCompanions;

public sealed partial class ModEntry
{
    private const int WorkAnimationSwingTicks = 22;
    // Pending tasks are processed every five ticks. Keep the finished pose on
    // screen until the next processing pass can apply the impact.
    private const int WorkAnimationProcessingSlackTicks = 4;
    private const int WorkAnimationSuccessTicks = 42;
    private const int WorkAnimationFailureTicks = 50;

    private readonly Dictionary<string, CompanionWorkAnimationState> companionWorkAnimations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<CompanionTaskKind, Item> companionWorkAnimationTools = new();

    private enum CompanionWorkAnimationOutcome
    {
        None,
        Success,
        Failure
    }

    private sealed class CompanionWorkAnimationState
    {
        public string NpcName { get; init; } = "";
        public string LocationName { get; init; } = "";
        public CompanionTaskKind TaskKind { get; init; }
        public Vector2 TargetTile { get; init; }
        public int ActionStartedTick { get; set; }
        public CompanionWorkAnimationOutcome Outcome { get; set; }
        public int OutcomeStartedTick { get; set; } = int.MaxValue;
        public int EndsAtTick { get; set; }
    }

    /// <summary>Face the target and begin the visible tool/hand motion for one task action.</summary>
    /// <remarks>Call this only after the NPC has reached its reserved stand tile.</remarks>
    private void StartCompanionWorkAnimation(
        NPC npc,
        CompanionTaskKind taskKind,
        Vector2 targetTile,
        bool broadcast = true)
    {
        if (!IsSupportedWorkAnimationKind(taskKind) || npc.currentLocation is null)
            return;

        targetTile = NormalizeTile(targetTile);
        if (Context.IsMainPlayer)
            this.FaceTile(npc, targetTile);

        CompanionWorkAnimationState state = new()
        {
            NpcName = npc.Name,
            LocationName = npc.currentLocation.NameOrUniqueName,
            TaskKind = taskKind,
            TargetTile = targetTile,
            ActionStartedTick = Game1.ticks,
            EndsAtTick = Game1.ticks + WorkAnimationSwingTicks + WorkAnimationProcessingSlackTicks
        };
        this.companionWorkAnimations[npc.Name] = state;

        if (broadcast)
            this.BroadcastCompanionWorkVisual(state, "start");
    }

    /// <summary>
    /// Start a task's visible motion and hold its world mutation until the
    /// swing/gesture has actually been rendered for its full duration.
    /// </summary>
    private bool ShouldWaitForCompanionWorkAnimation(
        PendingCompanionTask task,
        NPC npc,
        Vector2 rawTargetTile)
    {
        Vector2 targetTile = NormalizeTile(rawTargetTile);
        if (!task.AwaitingWorkAnimation || task.WorkAnimationTargetTile != targetTile)
        {
            task.AwaitingWorkAnimation = true;
            task.WorkAnimationTargetTile = targetTile;
            task.WorkAnimationReadyTick = unchecked(Game1.ticks + WorkAnimationSwingTicks);
            task.InactiveTicks = 0;
            this.StartCompanionWorkAnimation(npc, task.Kind, targetTile);
            return true;
        }

        if (!HasTickElapsed(Game1.ticks, task.WorkAnimationReadyTick))
        {
            task.InactiveTicks = 0;
            return true;
        }

        // The caller clears this only when it actually commits the world
        // mutation. Keeping it set prevents a ready swing from restarting if
        // its original hit cooldown has a few ticks left.
        return false;
    }

    /// <summary>Queue a positive reaction after the current work motion completes.</summary>
    private void ShowCompanionWorkSuccessAnimation(
        NPC npc,
        CompanionTaskKind taskKind,
        Vector2 targetTile,
        bool broadcast = true)
    {
        this.SetCompanionWorkAnimationOutcome(
            npc,
            taskKind,
            targetTile,
            CompanionWorkAnimationOutcome.Success,
            broadcast);
    }

    /// <summary>Show a clear failure reaction without changing task or save state.</summary>
    private void ShowCompanionWorkFailureAnimation(
        NPC npc,
        CompanionTaskKind taskKind,
        Vector2 targetTile,
        bool broadcast = true)
    {
        this.SetCompanionWorkAnimationOutcome(
            npc,
            taskKind,
            targetTile,
            CompanionWorkAnimationOutcome.Failure,
            broadcast);
    }

    private void SetCompanionWorkAnimationOutcome(
        NPC npc,
        CompanionTaskKind taskKind,
        Vector2 targetTile,
        CompanionWorkAnimationOutcome outcome,
        bool broadcast)
    {
        if (!IsSupportedWorkAnimationKind(taskKind) || npc.currentLocation is null)
            return;

        targetTile = NormalizeTile(targetTile);
        int now = Game1.ticks;
        bool continuesCurrentSwing = this.companionWorkAnimations.TryGetValue(npc.Name, out CompanionWorkAnimationState? state)
            && state.TaskKind == taskKind
            && state.TargetTile == targetTile
            && string.Equals(state.LocationName, npc.currentLocation.NameOrUniqueName, StringComparison.Ordinal)
            && unchecked((uint)(now - state.ActionStartedTick))
                <= WorkAnimationSwingTicks + WorkAnimationProcessingSlackTicks;
        if (!continuesCurrentSwing)
        {
            state = new CompanionWorkAnimationState
            {
                NpcName = npc.Name,
                LocationName = npc.currentLocation.NameOrUniqueName,
                TaskKind = taskKind,
                TargetTile = targetTile,
                ActionStartedTick = now - WorkAnimationSwingTicks
            };
            this.companionWorkAnimations[npc.Name] = state;
        }

        state!.Outcome = outcome;
        // Success/failure feedback begins at the real mutation result, never
        // at the animation's nominal end if task processing landed later.
        state.OutcomeStartedTick = now;
        state.EndsAtTick = state.OutcomeStartedTick + (outcome == CompanionWorkAnimationOutcome.Success
            ? WorkAnimationSuccessTicks
            : WorkAnimationFailureTicks);

        if (Context.IsMainPlayer)
        {
            this.FaceTile(npc, targetTile);
            if (outcome == CompanionWorkAnimationOutcome.Success)
                npc.jumpWithoutSound(3f);
            else
                npc.shake(260);
        }

        if (broadcast)
            this.BroadcastCompanionWorkVisual(state, outcome == CompanionWorkAnimationOutcome.Success ? "success" : "failure");
    }

    private void DrawCompanionWorkAnimations(SpriteBatch b)
    {
        if (!Context.IsWorldReady || Game1.currentLocation is null || this.companionWorkAnimations.Count == 0)
            return;

        int now = Game1.ticks;
        foreach (string expiredNpcName in this.companionWorkAnimations
            .Where(pair => HasTickElapsed(now, pair.Value.EndsAtTick))
            .Select(pair => pair.Key)
            .ToList())
        {
            this.companionWorkAnimations.Remove(expiredNpcName);
        }

        string currentLocationName = Game1.currentLocation.NameOrUniqueName;
        foreach (CompanionWorkAnimationState state in this.companionWorkAnimations.Values.ToList())
        {
            if (!string.Equals(state.LocationName, currentLocationName, StringComparison.Ordinal))
                continue;

            NPC? npc = this.GetNpcByName(state.NpcName);
            if (npc?.currentLocation != Game1.currentLocation || npc.IsInvisible)
                continue;

            if (state.Outcome == CompanionWorkAnimationOutcome.None || now < state.OutcomeStartedTick)
                this.DrawCompanionWorkMotion(b, npc, state, now);
            else if (state.Outcome == CompanionWorkAnimationOutcome.Success)
                this.DrawCompanionWorkSuccess(b, npc, state, now);
            else
                this.DrawCompanionWorkFailure(b, npc, state, now);
        }
    }

    private void DrawCompanionWorkMotion(
        SpriteBatch b,
        NPC npc,
        CompanionWorkAnimationState state,
        int now)
    {
        float progress = Math.Clamp(
            unchecked((uint)(now - state.ActionStartedTick)) / (float)WorkAnimationSwingTicks,
            0f,
            1f);
        Vector2 direction = GetWorkAnimationDirection(npc, state.TargetTile);
        Vector2 perpendicular = new(-direction.Y, direction.X);
        Rectangle npcBounds = npc.GetBoundingBox();
        Vector2 shoulderWorld = new(npcBounds.Center.X, npcBounds.Top - 8f);
        Vector2 shoulder = Game1.GlobalToLocal(Game1.viewport, shoulderWorld);

        float arc = MathF.Sin(progress * MathHelper.Pi);
        float sweep = MathHelper.Lerp(-18f, 25f, progress);
        Vector2 implementCenter = shoulder
            + direction * (20f + progress * 25f)
            + perpendicular * sweep
            + new Vector2(0f, -24f * arc);
        Color accent = GetWorkAnimationColor(state.TaskKind);

        DrawAnimationLine(b, shoulder, implementCenter, accent * (0.35f + arc * 0.45f), 3f);
        this.DrawWorkImplement(b, state.TaskKind, implementCenter, direction, progress);

        if (progress >= 0.68f)
        {
            Vector2 targetWorld = state.TargetTile * 64f + new Vector2(32f, 26f);
            Vector2 target = Game1.GlobalToLocal(Game1.viewport, targetWorld);
            float impactAlpha = 1f - Math.Clamp((progress - 0.68f) / 0.32f, 0f, 1f);
            DrawImpactSpark(b, target, accent * impactAlpha, 8f + 8f * (1f - impactAlpha));
        }
    }

    private void DrawWorkImplement(
        SpriteBatch b,
        CompanionTaskKind taskKind,
        Vector2 center,
        Vector2 direction,
        float progress)
    {
        Item? tool = this.GetCompanionWorkAnimationTool(taskKind);
        if (tool is not null)
        {
            float pulse = 0.52f + MathF.Sin(progress * MathHelper.Pi) * 0.08f;
            tool.drawInMenu(b, center - new Vector2(32f * pulse), pulse);
            return;
        }

        Color hand = new(232, 184, 135);
        float rotation = MathF.Atan2(direction.Y, direction.X);
        b.Draw(
            Game1.staminaRect,
            center,
            sourceRectangle: null,
            hand,
            rotation,
            new Vector2(0.5f, 0.5f),
            new Vector2(20f, 12f),
            SpriteEffects.None,
            layerDepth: 0.99f);

        if (taskKind == CompanionTaskKind.Petting)
            DrawPixelHeart(b, center + new Vector2(0f, -18f), new Color(238, 111, 145));
        else if (taskKind == CompanionTaskKind.Harvesting)
            DrawImpactSpark(b, center + new Vector2(0f, -14f), new Color(222, 174, 65), 5f);
        else if (taskKind == CompanionTaskKind.Gathering)
            DrawImpactSpark(b, center + new Vector2(0f, -14f), new Color(100, 174, 91), 5f);
    }

    private Item? GetCompanionWorkAnimationTool(CompanionTaskKind taskKind)
    {
        if (this.companionWorkAnimationTools.TryGetValue(taskKind, out Item? cached))
            return cached;

        Item? tool = taskKind switch
        {
            CompanionTaskKind.Lumbering => new Axe(),
            CompanionTaskKind.Mining => new Pickaxe(),
            CompanionTaskKind.Watering => new WateringCan(),
            _ => null
        };
        if (tool is not null)
            this.companionWorkAnimationTools[taskKind] = tool;

        return tool;
    }

    private void DrawCompanionWorkSuccess(
        SpriteBatch b,
        NPC npc,
        CompanionWorkAnimationState state,
        int now)
    {
        float progress = Math.Clamp(
            unchecked((uint)(now - state.OutcomeStartedTick)) / (float)WorkAnimationSuccessTicks,
            0f,
            1f);
        float alpha = 1f - progress;
        Rectangle bounds = npc.GetBoundingBox();
        Vector2 center = Game1.GlobalToLocal(
            Game1.viewport,
            new Vector2(bounds.Center.X, bounds.Top - 22f - progress * 22f));
        Color color = Color.Lerp(GetWorkAnimationColor(state.TaskKind), new Color(125, 222, 114), 0.55f) * alpha;

        DrawImpactSpark(b, center, color, 8f + progress * 16f);
        DrawAnimationLine(b, center + new Vector2(-9f, 1f), center + new Vector2(-2f, 9f), color, 4f);
        DrawAnimationLine(b, center + new Vector2(-2f, 9f), center + new Vector2(13f, -8f), color, 4f);
    }

    private void DrawCompanionWorkFailure(
        SpriteBatch b,
        NPC npc,
        CompanionWorkAnimationState state,
        int now)
    {
        float progress = Math.Clamp(
            unchecked((uint)(now - state.OutcomeStartedTick)) / (float)WorkAnimationFailureTicks,
            0f,
            1f);
        float alpha = 1f - progress;
        Rectangle bounds = npc.GetBoundingBox();
        float wobble = MathF.Sin(progress * MathHelper.TwoPi * 3f) * 4f * alpha;
        Vector2 center = Game1.GlobalToLocal(
            Game1.viewport,
            new Vector2(bounds.Center.X + wobble, bounds.Top - 24f - progress * 10f));
        Color color = new Color(218, 91, 82) * alpha;

        Rectangle bubble = new((int)center.X - 14, (int)center.Y - 15, 28, 28);
        b.Draw(Game1.staminaRect, bubble, new Color(255, 240, 213) * (alpha * 0.92f));
        Utility.drawTextWithShadow(
            b,
            "?",
            Game1.smallFont,
            new Vector2(bubble.X + 6f, bubble.Y - 3f),
            color);
        DrawAnimationLine(b, center + new Vector2(-17f, 17f), center + new Vector2(-7f, 27f), color, 3f);
        DrawAnimationLine(b, center + new Vector2(-7f, 17f), center + new Vector2(-17f, 27f), color, 3f);
    }

    private void BroadcastCompanionWorkVisual(CompanionWorkAnimationState state, string outcome)
    {
        if (!Context.IsMainPlayer || !Context.IsMultiplayer)
            return;

        try
        {
            this.Helper.Multiplayer.SendMessage(
                new CompanionWorkVisualMessage
                {
                    NpcName = state.NpcName,
                    LocationName = state.LocationName,
                    TaskKind = state.TaskKind,
                    TargetX = (int)state.TargetTile.X,
                    TargetY = (int)state.TargetTile.Y,
                    Outcome = outcome
                },
                MessageCompanionWorkVisual,
                modIDs: new[] { this.ModManifest.UniqueID });
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not replicate companion work animation: {ex.Message}", LogLevel.Trace);
        }
    }

    private bool TryHandleCompanionWorkVisualMessage(ModMessageReceivedEventArgs e)
    {
        if (e.Type != MessageCompanionWorkVisual)
            return false;

        if (Context.IsOnHostComputer || e.FromPlayerID != Game1.MasterPlayer.UniqueMultiplayerID)
            return true;

        try
        {
            CompanionWorkVisualMessage message = e.ReadAs<CompanionWorkVisualMessage>();
            if (message is null
                || string.IsNullOrWhiteSpace(message.NpcName)
                || message.NpcName.Length > 128
                || string.IsNullOrWhiteSpace(message.LocationName)
                || message.LocationName.Length > 256
                || !Enum.IsDefined(message.TaskKind)
                || !IsSupportedWorkAnimationKind(message.TaskKind)
                || message.TargetX is < -100000 or > 100000
                || message.TargetY is < -100000 or > 100000)
            {
                return true;
            }

            NPC? npc = this.GetNpcByName(message.NpcName);
            if (npc?.currentLocation is null
                || !string.Equals(npc.currentLocation.NameOrUniqueName, message.LocationName, StringComparison.Ordinal))
            {
                return true;
            }

            Vector2 targetTile = new(message.TargetX, message.TargetY);
            switch (message.Outcome?.ToLowerInvariant())
            {
                case "work":
                case "start":
                    this.StartCompanionWorkAnimation(npc, message.TaskKind, targetTile, broadcast: false);
                    break;
                case "success":
                    this.ShowCompanionWorkSuccessAnimation(npc, message.TaskKind, targetTile, broadcast: false);
                    break;
                case "failure":
                    this.ShowCompanionWorkFailureAnimation(npc, message.TaskKind, targetTile, broadcast: false);
                    break;
            }
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Ignored invalid companion work animation message: {ex.Message}", LogLevel.Trace);
        }

        return true;
    }

    private void ResetCompanionWorkAnimations()
    {
        this.companionWorkAnimations.Clear();
        this.companionWorkAnimationTools.Clear();
    }

    private static bool IsSupportedWorkAnimationKind(CompanionTaskKind taskKind)
    {
        return taskKind is CompanionTaskKind.Lumbering
            or CompanionTaskKind.Mining
            or CompanionTaskKind.Watering
            or CompanionTaskKind.Gathering
            or CompanionTaskKind.Harvesting
            or CompanionTaskKind.Petting;
    }

    private static bool HasTickElapsed(int now, int deadline)
    {
        return unchecked((int)((uint)now - (uint)deadline)) >= 0;
    }

    private static Vector2 GetWorkAnimationDirection(NPC npc, Vector2 targetTile)
    {
        Vector2 npcCenter = new(npc.GetBoundingBox().Center.X, npc.GetBoundingBox().Center.Y);
        Vector2 targetCenter = NormalizeTile(targetTile) * 64f + new Vector2(32f, 32f);
        Vector2 delta = targetCenter - npcCenter;
        if (delta.LengthSquared() > 0.001f)
        {
            delta.Normalize();
            return delta;
        }

        return npc.FacingDirection switch
        {
            0 => new Vector2(0f, -1f),
            1 => new Vector2(1f, 0f),
            2 => new Vector2(0f, 1f),
            _ => new Vector2(-1f, 0f)
        };
    }

    private static Color GetWorkAnimationColor(CompanionTaskKind taskKind)
    {
        return taskKind switch
        {
            CompanionTaskKind.Lumbering => new Color(119, 158, 82),
            CompanionTaskKind.Mining => new Color(142, 149, 177),
            CompanionTaskKind.Watering => new Color(82, 158, 224),
            CompanionTaskKind.Gathering => new Color(92, 174, 99),
            CompanionTaskKind.Harvesting => new Color(224, 172, 66),
            CompanionTaskKind.Petting => new Color(235, 126, 158),
            _ => Color.White
        };
    }

    private static void DrawAnimationLine(
        SpriteBatch b,
        Vector2 from,
        Vector2 to,
        Color color,
        float thickness)
    {
        Vector2 delta = to - from;
        float length = delta.Length();
        if (length <= 0.01f)
            return;

        b.Draw(
            Game1.staminaRect,
            from,
            sourceRectangle: null,
            color,
            MathF.Atan2(delta.Y, delta.X),
            new Vector2(0f, 0.5f),
            new Vector2(length, Math.Max(1f, thickness)),
            SpriteEffects.None,
            layerDepth: 0.99f);
    }

    private static void DrawImpactSpark(SpriteBatch b, Vector2 center, Color color, float radius)
    {
        DrawAnimationLine(b, center + new Vector2(-radius, 0f), center + new Vector2(radius, 0f), color, 2f);
        DrawAnimationLine(b, center + new Vector2(0f, -radius), center + new Vector2(0f, radius), color, 2f);
        float diagonal = radius * 0.7f;
        DrawAnimationLine(b, center + new Vector2(-diagonal, -diagonal), center + new Vector2(diagonal, diagonal), color, 2f);
        DrawAnimationLine(b, center + new Vector2(diagonal, -diagonal), center + new Vector2(-diagonal, diagonal), color, 2f);
    }

    private static void DrawPixelHeart(SpriteBatch b, Vector2 center, Color color)
    {
        const int unit = 3;
        int x = (int)center.X - unit * 2;
        int y = (int)center.Y - unit * 2;
        Rectangle[] pixels =
        {
            new(x, y, unit, unit),
            new(x + unit * 3, y, unit, unit),
            new(x - unit, y + unit, unit * 6, unit * 2),
            new(x, y + unit * 3, unit * 4, unit),
            new(x + unit, y + unit * 4, unit * 2, unit)
        };
        foreach (Rectangle pixel in pixels)
            b.Draw(Game1.staminaRect, pixel, color);
    }
}
