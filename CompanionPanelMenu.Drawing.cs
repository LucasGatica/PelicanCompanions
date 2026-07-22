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
        Color fill = danger
            ? ButtonDanger
            : active ? ButtonActive : hovered ? RowHoverColor : ButtonIdle;
        if (hovered && (danger || active))
            fill = Color.Lerp(fill, Color.White, 0.14f);

        Color border = danger ? DangerColor : active ? AccentGreen : SurfaceBorder;
        if (bounds.Height < 36 || bounds.Width < 40)
            this.DrawFlatPanel(b, bounds, fill, border, 1);
        else
            this.DrawTexturedPanel(b, bounds, fill);
        Color labelColor = danger || active ? Color.White : TextColor;
        float preferredScale = bounds.Height >= 24 ? PanelTextScale : PanelCompactTextScale;
        int horizontalPadding = bounds.Height < 24 ? 4 : 14;
        int verticalPadding = bounds.Height < 24 ? 2 : 8;
        DrawCenteredPanelText(
            b,
            text,
            Game1.tinyFont,
            bounds,
            labelColor,
            preferredScale,
            horizontalPadding,
            verticalPadding,
            minimumScale: 0.44f);
    }

    private void DrawTabButton(SpriteBatch b, Rectangle bounds, string text, bool active, string? badgeText = null, Color? badgeColor = null)
    {
        bool hovered = bounds.Contains(Game1.getMouseX(), Game1.getMouseY());
        Color fill = active ? TabActiveColor : hovered ? RowHoverColor : TabIdleColor;
        if (bounds.Height < 30)
            this.DrawFlatPanel(b, bounds, fill, active ? AccentGreen : SurfaceBorder, 1);
        else
            this.DrawTexturedPanel(b, bounds, fill);
        if (active)
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 8, bounds.Bottom - 5, Math.Max(1, bounds.Width - 16), 3), AccentGreen);
        else if (hovered)
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 8, bounds.Bottom - 5, Math.Max(1, bounds.Width - 16), 3), AccentGold);

        Rectangle contentBounds = new(bounds.X, bounds.Y, bounds.Width, Math.Max(1, bounds.Height - 5));
        int badgeSize = string.IsNullOrWhiteSpace(badgeText)
            ? 0
            : Math.Clamp(contentBounds.Height - 4, 14, 22);
        int labelWidth = Math.Max(1, bounds.Width - 10 - (badgeSize > 0 ? badgeSize + 5 : 0));
        SpriteFont labelFont = Game1.tinyFont;
        float preferredLabelScale = PanelTextScale;
        float labelScale = GetTextScaleForBox(
            text,
            labelFont,
            preferredLabelScale,
            labelWidth,
            contentBounds.Height - 6,
            minimumScale: 0.48f);
        string label = FitText(text, labelFont, labelWidth, labelScale);
        Vector2 size = MeasureScaledText(label, labelFont, labelScale);
        float contentWidth = size.X + (badgeSize > 0 ? badgeSize + 5 : 0);
        float labelX = bounds.X + Math.Max(5f, (bounds.Width - contentWidth) / 2f);
        DrawPanelText(
            b,
            label,
            labelFont,
            new Vector2(labelX, contentBounds.Y + Math.Max(2f, (contentBounds.Height - size.Y) / 2f)),
            TextColor,
            labelScale);

        if (badgeSize > 0)
        {
            Rectangle badge = new(
                (int)(labelX + size.X + 5),
                contentBounds.Center.Y - badgeSize / 2,
                badgeSize,
                badgeSize);
            Color badgeFill = badgeColor ?? AccentBlue;
            this.DrawFlatPanel(b, badge, badgeFill, Color.Lerp(badgeFill, WindowBorder, 0.35f), 1);
            DrawCenteredPanelText(
                b,
                badgeText!,
                Game1.tinyFont,
                badge,
                TextColor,
                PanelNumericTextScale,
                3,
                2,
                minimumScale: 0.56f);
        }
    }

    private void DrawBadge(SpriteBatch b, Rectangle bounds, string text, Color fill, Color border)
    {
        this.DrawFlatPanel(b, bounds, fill, border, 1);
        DrawCenteredPanelText(b, text, Game1.tinyFont, bounds, TextColor, PanelMetaTextScale, 10, 5);
    }

    private void DrawPanel(SpriteBatch b, Rectangle bounds, Color tint)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        this.DrawTexturedPanel(b, bounds, tint);
    }

    private void DrawTexturedPanel(SpriteBatch b, Rectangle bounds, Color tint)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        if (bounds.Width < 40 || bounds.Height < 32)
        {
            this.DrawFlatPanel(
                b,
                bounds,
                tint,
                Color.Lerp(tint, WindowBorder, 0.38f),
                1);
            return;
        }
        drawTextureBox(
            b,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            tint);
    }

    private void DrawMenuCard(SpriteBatch b, Rectangle bounds, Color tint, Color accent)
    {
        if (bounds.Height < 36 || bounds.Width < 64)
        {
            this.DrawFlatPanel(b, bounds, tint, accent, 1);
            return;
        }
        this.DrawTexturedPanel(b, bounds, tint);
        if (bounds.Width < 18 || bounds.Height < 18)
            return;
        b.Draw(
            Game1.staminaRect,
            new Rectangle(bounds.X + 7, bounds.Y + 9, 4, Math.Max(1, bounds.Height - 18)),
            accent);
    }

    private void DrawHeaderDivider(SpriteBatch b, int y)
    {
        int dividerWidth = Math.Clamp(this.width - 120, 96, 460);
        int dividerX = this.xPositionOnScreen + (this.width - dividerWidth) / 2;
        b.Draw(Game1.staminaRect, new Rectangle(dividerX, y, dividerWidth, 2), AccentGold * 0.72f);
        int centerWidth = Math.Min(64, dividerWidth / 3);
        b.Draw(
            Game1.staminaRect,
            new Rectangle(this.xPositionOnScreen + (this.width - centerWidth) / 2, y, centerWidth, 2),
            AccentGreen);
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

    private void DrawCompactSkillHover(SpriteBatch b, string text)
    {
        const int padding = 10;
        int maximumWidth = Math.Clamp(Game1.uiViewport.Width - 32, 140, 320);
        int contentWidth = Math.Max(1, maximumWidth - padding * 2);
        int lineHeight = GetScaledLineHeight(Game1.tinyFont, PanelMetaTextScale);
        int maximumLines = Math.Max(2, Math.Min(7, (Game1.uiViewport.Height - 32 - padding * 2) / Math.Max(1, lineHeight)));
        List<string> paragraphs = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList();
        List<string> lines = new(maximumLines);
        for (int paragraphIndex = 0; paragraphIndex < paragraphs.Count; paragraphIndex++)
        {
            if (lines.Count >= maximumLines)
                break;
            int reservedLines = Math.Min(paragraphs.Count - paragraphIndex - 1, maximumLines - lines.Count - 1);
            int availableLines = Math.Max(1, maximumLines - lines.Count - reservedLines);
            lines.AddRange(WrapText(paragraphs[paragraphIndex], Game1.tinyFont, contentWidth, availableLines, PanelMetaTextScale));
        }
        if (lines.Count == 0)
            return;

        float widest = lines.Max(line => MeasureScaledText(line, Game1.tinyFont, PanelMetaTextScale).X);
        int width = Math.Clamp((int)Math.Ceiling(widest) + padding * 2, 120, maximumWidth);
        int height = lines.Count * lineHeight + padding * 2;
        int x = Game1.getMouseX() + 18;
        int y = Game1.getMouseY() + 18;
        if (x + width > Game1.uiViewport.Width - 8)
            x = Game1.getMouseX() - width - 12;
        if (y + height > Game1.uiViewport.Height - 8)
            y = Game1.getMouseY() - height - 12;
        x = Math.Clamp(x, 8, Math.Max(8, Game1.uiViewport.Width - width - 8));
        y = Math.Clamp(y, 8, Math.Max(8, Game1.uiViewport.Height - height - 8));

        Rectangle panel = new(x, y, width, height);
        this.DrawTexturedPanel(b, panel, SurfaceTextureTint);
        int lineY = panel.Y + padding;
        for (int index = 0; index < lines.Count; index++)
        {
            DrawPanelText(
                b,
                FitText(lines[index], Game1.tinyFont, panel.Width - padding * 2, PanelMetaTextScale),
                Game1.tinyFont,
                new Vector2(panel.X + padding, lineY),
                index == 0 ? TextColor : MutedTextColor,
                PanelMetaTextScale);
            lineY += lineHeight;
        }
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
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 1, bounds.Y + 1, fillWidth, Math.Max(1, bounds.Height - 2)), AccentGreen);
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
        if (this.hatSlot.Width > 0)
            this.focusTargets.Add(this.hatSlot);
        this.focusTargets.AddRange(this.equipmentSlotsBounds.Select(p => p.Bounds));
        if (this.withdrawAllButton.Width > 0)
            this.focusTargets.Add(this.withdrawAllButton);
        this.focusTargets.AddRange(this.inventorySlotsBounds.Select(p => p.Bounds));
        this.AddRoutineFocusTargets(this.focusTargets);

        if (this.focusSkillOnNextRebuild && this.skillButtons.Count > 0)
        {
            SquadMemberState? member = this.GetSelectedMember();
            (Rectangle Bounds, string SkillId)? preferred = null;
            if (member is not null)
            {
                bool progressionEnabled = this.isProgressionEnabled();
                preferred = this.skillButtons.FirstOrDefault(button =>
                {
                    CompanionSkillDefinition? skill = CompanionProgression.Skills.FirstOrDefault(candidate => candidate.Id == button.SkillId);
                    return skill is not null
                        && CompanionSkillTreePolicy.GetState(skill, member.UnlockedSkillIds, member.UnspentSkillPoints, progressionEnabled)
                            == CompanionSkillTreeState.Available;
                });
            }
            if (preferred is null || preferred.Value.Bounds.Width <= 0)
                preferred = this.skillButtons[0];

            this.inspectedSkillId = preferred.Value.SkillId;
            this.focusedControlIndex = this.focusTargets.FindIndex(bounds => bounds == preferred.Value.Bounds);
            this.focusSkillOnNextRebuild = false;
            if (this.focusedControlIndex >= 0)
                this.performHoverAction(preferred.Value.Bounds.Center.X, preferred.Value.Bounds.Center.Y);
        }

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

    private void MoveFocusSpatial(int horizontal, int vertical)
    {
        if (this.focusTargets.Count == 0 || (horizontal == 0 && vertical == 0))
            return;
        if (this.focusedControlIndex < 0 || this.focusedControlIndex >= this.focusTargets.Count)
        {
            this.MoveFocus(1);
            return;
        }

        Rectangle current = this.focusTargets[this.focusedControlIndex];
        bool currentIsSkill = this.skillButtons.Any(button => button.Bounds == current);
        int bestIndex = -1;
        double bestScore = double.MaxValue;
        for (int index = 0; index < this.focusTargets.Count; index++)
        {
            if (index == this.focusedControlIndex)
                continue;
            Rectangle candidate = this.focusTargets[index];
            if (currentIsSkill && !this.skillButtons.Any(button => button.Bounds == candidate))
                continue;
            int deltaX = candidate.Center.X - current.Center.X;
            int deltaY = candidate.Center.Y - current.Center.Y;
            if ((horizontal < 0 && deltaX >= 0)
                || (horizontal > 0 && deltaX <= 0)
                || (vertical < 0 && deltaY >= 0)
                || (vertical > 0 && deltaY <= 0))
            {
                continue;
            }

            double primary = horizontal == 0 ? Math.Abs(deltaY) : Math.Abs(deltaX);
            double secondary = horizontal == 0 ? Math.Abs(deltaX) : Math.Abs(deltaY);
            double score = primary + secondary * 2.75d;
            if (score >= bestScore)
                continue;
            bestScore = score;
            bestIndex = index;
        }

        if (bestIndex < 0)
            return;
        this.focusedControlIndex = bestIndex;
        Rectangle target = this.focusTargets[bestIndex];
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
        int inset = Math.Min(5, Math.Max(1, bounds.Width / 4));
        int thickness = bounds.Height < 20 ? 1 : 2;
        b.Draw(
            Game1.staminaRect,
            new Rectangle(
                bounds.X + inset,
                bounds.Bottom - thickness,
                Math.Max(1, bounds.Width - inset * 2),
                thickness),
            AccentGold);
    }

    private void CycleTab(int delta)
    {
        PanelTab[] tabs = { PanelTab.Overview, PanelTab.Work, PanelTab.Skills, PanelTab.Inventory, PanelTab.Routine };
        int index = Array.IndexOf(tabs, this.currentTab);
        this.SetTab(tabs[(index + delta + tabs.Length) % tabs.Length]);
    }

    private void SetTab(PanelTab tab)
    {
        if (this.currentTab == tab)
            return;
        this.currentTab = tab;
        this.focusedControlIndex = -1;
        this.hoverText = "";
        this.focusSkillOnNextRebuild = tab == PanelTab.Skills;
        if (tab == PanelTab.Skills)
            this.inspectedSkillId = null;
        Game1.playSound("smallSelect");
    }

    private string GetTabLabel(PanelTab tab, bool compact = false)
    {
        return this.translate(tab switch
        {
            PanelTab.Overview when compact => "companion.panel.tab_overview_short",
            PanelTab.Work when compact => "companion.panel.tab_work_short",
            PanelTab.Skills when compact => "companion.panel.tab_skills_short",
            PanelTab.Inventory when compact => "companion.panel.tab_inventory_short",
            PanelTab.Routine when compact => "companion.panel.tab_routine_short",
            PanelTab.Overview => "companion.panel.tab_overview",
            PanelTab.Work => "companion.panel.tab_work",
            PanelTab.Skills => "companion.panel.tab_skills",
            PanelTab.Inventory => "companion.panel.tab_inventory",
            PanelTab.Routine => "companion.panel.tab_routine",
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
        bool progressionEnabled = this.isProgressionEnabled();
        CompanionSkillTreeState state = CompanionSkillTreePolicy.GetState(
            skill,
            member.UnlockedSkillIds,
            member.UnspentSkillPoints,
            progressionEnabled);
        List<CompanionSkillDefinition> branchSkills = CompanionProgression.Skills
            .Where(candidate => string.Equals(candidate.Branch, skill.Branch, StringComparison.OrdinalIgnoreCase))
            .ToList();
        int tier = Math.Max(1, branchSkills.FindIndex(candidate => string.Equals(candidate.Id, skill.Id, StringComparison.OrdinalIgnoreCase)) + 1);
        List<string> lines = new()
        {
            this.translate(skill.NameKey, null),
            $"{this.translate($"companion.skill.branch.{skill.Branch}", null)} · {GetRomanTier(tier)}",
            $"{this.translate(GetSkillStateTranslationKey(state), null)} · {this.BuildSkillDetailStatus(skill, state)}",
            this.translate(skill.DescriptionKey, null),
            this.translate("companion.skill.cost", new { cost = skill.Cost }),
            this.translate("companion.skill.points_available", new { points = member.UnspentSkillPoints })
        };
        return string.Join(Environment.NewLine, lines);
    }

    private Color GetMemberStatusColor(SquadMemberState member)
    {
        if (member.CurrentActivityKey == "companion.status.stuck")
            return DangerColor;
        if (member.Mode == CompanionMode.Waiting || member.Mode == CompanionMode.ParkedForDisconnect)
            return AccentGold;
        if (member.CurrentActivityKey == "companion.status.moving_to_wait")
            return AccentBlue;
        if (member.CurrentActivityKey == "companion.status.moving_to_fish")
            return AccentBlue;
        if (member.CurrentActivityKey == "companion.status.fishing")
            return new Color(45, 137, 166);
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
            "companion.map.moving_to_wait" => AccentBlue,
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
        return FitText(text, font, width, 1f);
    }

    private static string FitText(string text, SpriteFont font, int width, float scale)
    {
        if (string.IsNullOrEmpty(text) || width <= 0 || scale <= 0f)
            return "";
        float unscaledWidth = width / scale;
        if (font.MeasureString(text).X <= unscaledWidth)
            return text;
        const string suffix = "…";
        if (font.MeasureString(suffix).X > unscaledWidth)
            return "";
        int low = 0;
        int high = text.Length;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (font.MeasureString(text[..mid] + suffix).X <= unscaledWidth)
                low = mid;
            else
                high = mid - 1;
        }
        return text[..low] + suffix;
    }

    private static IReadOnlyList<string> WrapText(string text, SpriteFont font, int width, int maxLines)
    {
        return WrapText(text, font, width, maxLines, 1f);
    }

    private static IReadOnlyList<string> WrapText(string text, SpriteFont font, int width, int maxLines, float scale)
    {
        if (string.IsNullOrWhiteSpace(text) || width <= 0 || maxLines <= 0 || scale <= 0f)
            return Array.Empty<string>();

        float unscaledWidth = width / scale;
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<string> lines = new(Math.Min(maxLines, words.Length));
        int wordIndex = 0;
        while (wordIndex < words.Length && lines.Count < maxLines)
        {
            string line = "";
            while (wordIndex < words.Length)
            {
                string candidate = string.IsNullOrEmpty(line) ? words[wordIndex] : $"{line} {words[wordIndex]}";
                if (!string.IsNullOrEmpty(line) && font.MeasureString(candidate).X > unscaledWidth)
                    break;
                line = candidate;
                wordIndex++;
                if (font.MeasureString(line).X > unscaledWidth)
                {
                    line = FitText(line, font, width, scale);
                    break;
                }
            }

            if (lines.Count == maxLines - 1 && wordIndex < words.Length)
            {
                string remainder = string.Join(' ', words.Skip(wordIndex));
                line = FitText($"{line} {remainder}".Trim(), font, width, scale);
                wordIndex = words.Length;
            }
            lines.Add(line);
        }

        return lines;
    }

    private static void DrawPanelText(
        SpriteBatch b,
        string text,
        SpriteFont font,
        Vector2 position,
        Color color,
        float scale,
        bool shadow = false)
    {
        if (string.IsNullOrEmpty(text) || scale <= 0f)
            return;

        Vector2 snapped = new(MathF.Round(position.X), MathF.Round(position.Y));
        if (shadow)
        {
            b.DrawString(
                font,
                text,
                snapped + Vector2.One,
                Color.Black * 0.28f,
                0f,
                Vector2.Zero,
                scale,
                SpriteEffects.None,
                0f);
        }
        b.DrawString(font, text, snapped, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    private static void DrawCenteredPanelText(
        SpriteBatch b,
        string text,
        SpriteFont font,
        Rectangle bounds,
        Color color,
        float preferredScale,
        int horizontalPadding,
        int verticalPadding,
        float minimumScale = PanelCompactTextScale)
    {
        if (bounds.Width <= horizontalPadding || bounds.Height <= verticalPadding)
            return;

        float scale = GetTextScaleForBox(
            text,
            font,
            preferredScale,
            bounds.Width - horizontalPadding,
            bounds.Height - verticalPadding,
            minimumScale);
        string label = FitText(text, font, Math.Max(1, bounds.Width - horizontalPadding), scale);
        Vector2 size = MeasureScaledText(label, font, scale);
        DrawPanelText(
            b,
            label,
            font,
            new Vector2(bounds.Center.X - size.X / 2f, bounds.Center.Y - size.Y / 2f),
            color,
            scale);
    }

    private static Vector2 MeasureScaledText(string text, SpriteFont font, float scale)
    {
        return font.MeasureString(text) * scale;
    }

    private static int GetScaledLineHeight(SpriteFont font, float scale)
    {
        return Math.Max(1, (int)Math.Ceiling(font.LineSpacing * scale));
    }

    private static float GetTextScaleForHeight(SpriteFont font, float preferredScale, int availableHeight)
    {
        if (availableHeight <= 0 || font.LineSpacing <= 0)
            return 0f;
        return Math.Max(0f, Math.Min(preferredScale, availableHeight / (float)font.LineSpacing));
    }

    private static float GetTextScaleForBox(
        string text,
        SpriteFont font,
        float preferredScale,
        int availableWidth,
        int availableHeight,
        float minimumScale = PanelCompactTextScale)
    {
        float heightScale = GetTextScaleForHeight(font, preferredScale, availableHeight);
        float naturalWidth = string.IsNullOrEmpty(text) ? 0f : font.MeasureString(text).X;
        float widthScale = naturalWidth <= 0f || availableWidth <= 0
            ? preferredScale
            : availableWidth / naturalWidth;
        float widthConstrained = Math.Min(
            preferredScale,
            Math.Max(Math.Min(preferredScale, minimumScale), widthScale));
        return Math.Max(0f, Math.Min(heightScale, widthConstrained));
    }

    private readonly record struct PortraitCacheEntry(Texture2D? Texture, int CheckedAtTick);
    private readonly record struct InventoryDisplayCacheEntry(int Fingerprint, IReadOnlyList<Item> Items);

    private enum PanelTab
    {
        Overview,
        Work,
        Skills,
        Inventory,
        Routine
    }
}
