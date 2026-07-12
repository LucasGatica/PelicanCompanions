using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace PelicanCompanions;

internal sealed partial class CompanionPanelMenu
{
    private void DrawButton(SpriteBatch b, Rectangle bounds, string text, bool active, bool danger)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        bool hovered = bounds.Contains(Game1.getMouseX(), Game1.getMouseY());
        Color fill = danger ? new Color(231, 191, 176) : active ? ButtonActive : ButtonIdle;
        if (hovered)
            fill = Color.Lerp(fill, Color.White, 0.22f);
        Color border = danger ? DangerColor : active ? AccentGreen : SurfaceBorder;
        this.DrawFlatPanel(b, bounds, fill, border, hovered ? 2 : 1);
        string label = FitText(text, Game1.tinyFont, bounds.Width - 12);
        Vector2 size = Game1.tinyFont.MeasureString(label);
        Utility.drawTextWithShadow(
            b,
            label,
            Game1.tinyFont,
            new Vector2(bounds.X + (bounds.Width - size.X) / 2f, bounds.Y + Math.Max(2, (bounds.Height - size.Y) / 2f)),
            TextColor);
    }

    private void DrawTabButton(SpriteBatch b, Rectangle bounds, string text, bool active)
    {
        bool hovered = bounds.Contains(Game1.getMouseX(), Game1.getMouseY());
        Color fill = active ? new Color(204, 226, 238) : hovered ? RowHoverColor : ButtonIdle;
        this.DrawFlatPanel(b, bounds, fill, active ? AccentBlue : SurfaceBorder, active ? 2 : 1);
        if (active)
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 5, bounds.Bottom - 4, Math.Max(1, bounds.Width - 10), 3), AccentBlue);
        string label = FitText(text, Game1.tinyFont, bounds.Width - 10);
        Vector2 size = Game1.tinyFont.MeasureString(label);
        Utility.drawTextWithShadow(
            b,
            label,
            Game1.tinyFont,
            new Vector2(bounds.X + (bounds.Width - size.X) / 2f, bounds.Y + Math.Max(2, (bounds.Height - size.Y) / 2f)),
            TextColor);
    }

    private void DrawBadge(SpriteBatch b, Rectangle bounds, string text, Color fill, Color border)
    {
        this.DrawFlatPanel(b, bounds, fill, border, 1);
        string label = FitText(text, Game1.tinyFont, bounds.Width - 10);
        Vector2 size = Game1.tinyFont.MeasureString(label);
        Utility.drawTextWithShadow(
            b,
            label,
            Game1.tinyFont,
            new Vector2(bounds.X + (bounds.Width - size.X) / 2f, bounds.Y + Math.Max(2, (bounds.Height - size.Y) / 2f)),
            TextColor);
    }

    private void DrawPanel(SpriteBatch b, Rectangle bounds, Color fill, Color border)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), bounds.X, bounds.Y, bounds.Width, bounds.Height, border);
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 5, bounds.Y + 5, Math.Max(1, bounds.Width - 10), Math.Max(1, bounds.Height - 10)), fill);
    }

    private void DrawFlatPanel(SpriteBatch b, Rectangle bounds, Color fill, Color border, int borderSize)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        b.Draw(Game1.staminaRect, bounds, border);
        b.Draw(
            Game1.staminaRect,
            new Rectangle(bounds.X + borderSize, bounds.Y + borderSize, Math.Max(1, bounds.Width - borderSize * 2), Math.Max(1, bounds.Height - borderSize * 2)),
            fill);
    }

    private void DrawMemberListScrollbar(SpriteBatch b, Rectangle area)
    {
        if (this.memberListMaxScroll <= 0)
            return;
        Rectangle track = new(area.Right - 5, area.Y + 5, 4, Math.Max(1, area.Height - 10));
        int visible = this.GetVisibleMemberRowCount();
        int total = Math.Max(visible, this.getMembers().Count());
        int thumbHeight = Math.Clamp(track.Height * visible / total, 18, track.Height);
        int travel = Math.Max(1, track.Height - thumbHeight);
        int thumbY = track.Y + travel * this.memberListScrollOffset / Math.Max(1, this.memberListMaxScroll);
        b.Draw(Game1.staminaRect, track, Color.Black * 0.13f);
        b.Draw(Game1.staminaRect, new Rectangle(track.X, thumbY, track.Width, thumbHeight), SurfaceBorder);
    }

    private void DrawPortrait(SpriteBatch b, NPC? npc, Rectangle bounds)
    {
        Texture2D? portrait = this.GetPortrait(npc);
        if (portrait is not null)
        {
            b.Draw(portrait, new Rectangle(bounds.X + 3, bounds.Y + 3, Math.Max(1, bounds.Width - 6), Math.Max(1, bounds.Height - 6)), new Rectangle(0, 0, 64, 64), Color.White);
            return;
        }
        if (npc?.Sprite?.Texture is null)
            return;
        int width = Math.Max(1, Math.Min(bounds.Width - 6, 30));
        int height = Math.Max(1, Math.Min(bounds.Height - 4, 46));
        b.Draw(npc.Sprite.Texture, new Rectangle(bounds.Center.X - width / 2, bounds.Bottom - height - 2, width, height), npc.Sprite.SourceRect, Color.White);
    }

    private Texture2D? GetPortrait(NPC? npc)
    {
        if (npc is null)
            return null;
        if (this.portraitCache.TryGetValue(npc.Name, out PortraitCacheEntry cached))
        {
            if (cached.Texture is not null && !cached.Texture.IsDisposed)
                return cached.Texture;
            if (cached.Texture is null && Game1.ticks - cached.CheckedAtTick < PortraitRetryTicks)
                return null;
        }
        try
        {
            Texture2D portrait = Game1.content.Load<Texture2D>($"Portraits/{npc.Name}");
            this.portraitCache[npc.Name] = new PortraitCacheEntry(portrait, Game1.ticks);
            return portrait;
        }
        catch
        {
            this.portraitCache[npc.Name] = new PortraitCacheEntry(null, Game1.ticks);
            return null;
        }
    }

    private void DrawXpBar(SpriteBatch b, Rectangle bounds, SquadMemberState member)
    {
        int current = CompanionProgression.GetXpForLevel(member.Level);
        int next = CompanionProgression.GetNextLevelXp(member.Level);
        float progress = next <= current ? 1f : Math.Clamp((member.Xp - current) / (float)(next - current), 0f, 1f);
        this.DrawFlatPanel(b, bounds, new Color(102, 77, 60), new Color(70, 48, 34), 1);
        int fillWidth = (int)((bounds.Width - 2) * progress);
        if (fillWidth > 0)
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 1, bounds.Y + 1, fillWidth, Math.Max(1, bounds.Height - 2)), AccentBlue);
    }

    private void RebuildFocusTargets()
    {
        this.focusTargets.Clear();
        if (this.closeButton.Width > 0)
            this.focusTargets.Add(this.closeButton);
        if (this.previousMemberButton.Width > 0)
            this.focusTargets.Add(this.previousMemberButton);
        if (this.nextMemberButton.Width > 0)
            this.focusTargets.Add(this.nextMemberButton);
        this.focusTargets.AddRange(this.memberRows.Select(p => p.Bounds));
        this.focusTargets.AddRange(this.tabButtons.Select(p => p.Bounds));
        if (this.waitButton.Width > 0)
            this.focusTargets.Add(this.waitButton);
        if (this.recallButton.Width > 0)
            this.focusTargets.Add(this.recallButton);
        if (this.dismissButton.Width > 0)
            this.focusTargets.Add(this.dismissButton);
        this.focusTargets.AddRange(this.directiveButtons.Select(p => p.Bounds));
        this.focusTargets.AddRange(this.skillButtons.Select(p => p.Bounds));
        if (this.withdrawAllButton.Width > 0)
            this.focusTargets.Add(this.withdrawAllButton);
        this.focusTargets.AddRange(this.inventorySlotsBounds.Select(p => p.Bounds));
        if (this.focusTargets.Count == 0)
            this.focusedControlIndex = -1;
        else if (this.focusedControlIndex >= this.focusTargets.Count)
            this.focusedControlIndex = this.focusTargets.Count - 1;
    }

    private void MoveFocus(int delta)
    {
        if (this.focusTargets.Count == 0)
            return;
        if (this.focusedControlIndex < 0)
            this.focusedControlIndex = 0;
        else
            this.focusedControlIndex = (this.focusedControlIndex + delta + this.focusTargets.Count) % this.focusTargets.Count;
        Rectangle target = this.focusTargets[this.focusedControlIndex];
        this.performHoverAction(target.Center.X, target.Center.Y);
        Game1.playSound("shiny4");
    }

    private void ActivateFocusedControl()
    {
        if (this.focusedControlIndex < 0 || this.focusedControlIndex >= this.focusTargets.Count)
        {
            this.MoveFocus(1);
            return;
        }
        Rectangle target = this.focusTargets[this.focusedControlIndex];
        this.receiveLeftClick(target.Center.X, target.Center.Y);
    }

    private void DrawFocus(SpriteBatch b)
    {
        if (this.focusedControlIndex < 0 || this.focusedControlIndex >= this.focusTargets.Count)
            return;
        Rectangle bounds = this.focusTargets[this.focusedControlIndex];
        Color color = Color.White * 0.95f;
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, 2), color);
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X - 2, bounds.Bottom, bounds.Width + 4, 2), color);
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X - 2, bounds.Y, 2, bounds.Height), color);
        b.Draw(Game1.staminaRect, new Rectangle(bounds.Right, bounds.Y, 2, bounds.Height), color);
    }

    private void CycleTab(int delta)
    {
        PanelTab[] tabs = { PanelTab.Overview, PanelTab.Work, PanelTab.Skills, PanelTab.Inventory };
        int index = Array.IndexOf(tabs, this.currentTab);
        this.SetTab(tabs[(index + delta + tabs.Length) % tabs.Length]);
    }

    private void SetTab(PanelTab tab)
    {
        if (this.currentTab == tab)
            return;
        this.currentTab = tab;
        this.focusedControlIndex = -1;
        Game1.playSound("smallSelect");
    }

    private string GetTabLabel(PanelTab tab)
    {
        return this.translate(tab switch
        {
            PanelTab.Overview => "companion.panel.tab_overview",
            PanelTab.Work => "companion.panel.tab_work",
            PanelTab.Skills => "companion.panel.tab_skills",
            PanelTab.Inventory => "companion.panel.tab_inventory",
            _ => "companion.panel.tab_overview"
        }, null);
    }

    private SquadMemberState? GetSelectedMember(List<SquadMemberState>? members = null)
    {
        members ??= this.getMembers().ToList();
        return members.FirstOrDefault(p => string.Equals(p.NpcName, this.selectedNpcName, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureSelection(List<SquadMemberState> members)
    {
        if (members.Any(p => string.Equals(p.NpcName, this.selectedNpcName, StringComparison.OrdinalIgnoreCase)))
            return;
        this.selectedNpcName = members[0].NpcName;
    }

    private bool SelectRelativeMember(int delta)
    {
        List<SquadMemberState> members = this.getMembers().ToList();
        if (members.Count == 0)
            return false;
        int current = members.FindIndex(p => string.Equals(p.NpcName, this.selectedNpcName, StringComparison.OrdinalIgnoreCase));
        if (current < 0)
            current = 0;
        int next = Math.Clamp(current + delta, 0, members.Count - 1);
        return this.SelectMemberByIndex(next, members);
    }

    private bool SelectMemberByIndex(int index, List<SquadMemberState>? members = null)
    {
        members ??= this.getMembers().ToList();
        if (members.Count == 0)
            return false;
        int selectedIndex = Math.Clamp(index, 0, members.Count - 1);
        string npcName = members[selectedIndex].NpcName;
        if (string.Equals(this.selectedNpcName, npcName, StringComparison.OrdinalIgnoreCase))
            return false;
        this.selectedNpcName = npcName;
        this.EnsureSelectedMemberVisible(members, selectedIndex);
        return true;
    }

    private int GetVisibleMemberRowCount()
    {
        if (!this.wideLayout || this.memberListArea.Height <= 0)
            return 1;
        int stride = MemberRowHeight + MemberRowGap;
        return Math.Max(1, (this.memberListArea.Height - MemberScrollPadding * 2 + MemberRowGap) / stride);
    }

    private void EnsureSelectedMemberVisible(List<SquadMemberState> members, int selectedIndex)
    {
        if (!this.wideLayout || this.memberListArea.Height <= 0)
            return;
        int stride = MemberRowHeight + MemberRowGap;
        int visible = this.GetVisibleMemberRowCount();
        int first = this.memberListScrollOffset / stride;
        if (selectedIndex < first)
            first = selectedIndex;
        else if (selectedIndex >= first + visible)
            first = selectedIndex - visible + 1;
        first = Math.Clamp(first, 0, Math.Max(0, members.Count - visible));
        this.memberListScrollOffset = first * stride;
        this.memberListMaxScroll = Math.Max(0, (members.Count - visible) * stride);
    }

    private string BuildSkillHoverText(SquadMemberState member, CompanionSkillDefinition skill)
    {
        List<string> lines = new()
        {
            this.translate(skill.NameKey, null),
            this.translate(skill.DescriptionKey, null),
            this.translate("companion.skill.cost", new { cost = skill.Cost }),
            this.translate("companion.skill.points_available", new { points = member.UnspentSkillPoints })
        };
        if (!string.IsNullOrWhiteSpace(skill.PrerequisiteId))
        {
            CompanionSkillDefinition? prerequisite = CompanionProgression.Skills.FirstOrDefault(p => p.Id == skill.PrerequisiteId);
            if (prerequisite is not null)
                lines.Add(this.translate("companion.skill.requires", new { skill = this.translate(prerequisite.NameKey, null) }));
        }
        bool unlocked = member.UnlockedSkillIds.Contains(skill.Id, StringComparer.OrdinalIgnoreCase);
        bool progressionEnabled = this.isProgressionEnabled();
        if (unlocked)
            lines.Add(this.translate("companion.skill.learned", null));
        if (!progressionEnabled)
            lines.Add(this.translate("companion.skill.progression_disabled", null));
        else if (!unlocked)
        {
            if (!string.IsNullOrWhiteSpace(skill.PrerequisiteId) && !member.UnlockedSkillIds.Contains(skill.PrerequisiteId, StringComparer.OrdinalIgnoreCase))
                lines.Add(this.translate("companion.skill.locked", null));
            else if (member.UnspentSkillPoints < skill.Cost)
                lines.Add(this.translate("companion.skill.no_points", null));
            else
                lines.Add(this.translate("companion.skill.learn", null));
        }
        return string.Join(Environment.NewLine, lines);
    }

    private Color GetMemberStatusColor(SquadMemberState member)
    {
        if (member.CurrentActivityKey == "companion.status.stuck")
            return DangerColor;
        if (member.Mode == CompanionMode.Waiting || member.Mode == CompanionMode.ParkedForDisconnect)
            return AccentGold;
        if (member.CurrentActivityKey == "companion.status.working")
            return new Color(207, 133, 50);
        if (member.CurrentActivityKey == "companion.status.returning")
            return AccentBlue;
        if (member.Inventory.Count >= this.inventorySlots)
            return AccentGold;
        return AccentGreen;
    }

    private Color GetMapStatusColor(string key)
    {
        return key switch
        {
            "companion.map.working" => new Color(207, 133, 50),
            "companion.map.returning" => AccentBlue,
            "companion.map.stuck" => DangerColor,
            "companion.map.other_location" => new Color(124, 102, 149),
            _ => AccentGreen
        };
    }

    private void CloseMenu()
    {
        Game1.activeClickableMenu = null;
        Game1.playSound("bigDeSelect");
    }

    private static string FitText(string text, SpriteFont font, int width)
    {
        if (string.IsNullOrEmpty(text) || width <= 0)
            return "";
        if (font.MeasureString(text).X <= width)
            return text;
        const string suffix = "…";
        if (font.MeasureString(suffix).X > width)
            return "";
        int low = 0;
        int high = text.Length;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (font.MeasureString(text[..mid] + suffix).X <= width)
                low = mid;
            else
                high = mid - 1;
        }
        return text[..low] + suffix;
    }

    private readonly record struct PortraitCacheEntry(Texture2D? Texture, int CheckedAtTick);
    private readonly record struct InventoryDisplayCacheEntry(int Fingerprint, IReadOnlyList<Item> Items);

    private enum PanelTab
    {
        Overview,
        Work,
        Skills,
        Inventory
    }
}
