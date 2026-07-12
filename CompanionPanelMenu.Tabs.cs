using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace PelicanCompanions;

internal sealed partial class CompanionPanelMenu
{
    private void DrawOverview(SpriteBatch b, SquadMemberState member, Rectangle area)
    {
        int commandHeight = Math.Clamp(area.Height / 5, 28, 38);
        int commandY = area.Bottom - commandHeight;
        int gap = 5;
        int commandWidth = Math.Max(1, (area.Width - gap * 2) / 3);
        this.waitButton = new Rectangle(area.X, commandY, commandWidth, commandHeight);
        this.recallButton = new Rectangle(this.waitButton.Right + gap, commandY, commandWidth, commandHeight);
        this.dismissButton = new Rectangle(this.recallButton.Right + gap, commandY, Math.Max(1, area.Right - this.recallButton.Right - gap), commandHeight);

        this.DrawButton(
            b,
            this.waitButton,
            this.translate(member.Mode == CompanionMode.Following ? "management.wait" : "management.resume", null),
            member.Mode != CompanionMode.Following,
            danger: false);
        this.DrawButton(b, this.recallButton, this.translate("management.recall", null), false, danger: false);
        this.DrawButton(b, this.dismissButton, this.translate("management.dismiss", null), false, danger: true);

        Rectangle content = new(area.X, area.Y, area.Width, Math.Max(1, commandY - area.Y - 7));
        if (content.Height < 22)
            return;

        IReadOnlyList<string> summary = this.getSummaryLines();
        int summaryHeight = content.Height >= 130 ? 32 : 0;
        if (summaryHeight > 0 && summary.Count > 0)
        {
            Rectangle summaryArea = new(content.X, content.Y, content.Width, summaryHeight);
            this.DrawSummaryStrip(b, summaryArea, summary);
            content = new Rectangle(content.X, summaryArea.Bottom + 7, content.Width, Math.Max(1, content.Bottom - summaryArea.Bottom - 7));
        }

        bool showLocation = content.Width >= 430 && content.Height >= 54;
        Rectangle location = showLocation
            ? new Rectangle(content.Right - 144, content.Y, 144, Math.Min(66, content.Height))
            : new Rectangle();
        Rectangle details = showLocation
            ? new Rectangle(content.X, content.Y, Math.Max(1, location.X - content.X - 8), content.Height)
            : content;

        IReadOnlyList<string> lines = this.getDetailLines(member);
        int lineHeight = 19;
        int maxLines = Math.Max(1, details.Height / lineHeight);
        int y = details.Y + 2;
        foreach (string line in lines.Take(maxLines))
        {
            Utility.drawTextWithShadow(b, FitText(line, Game1.tinyFont, details.Width - 6), Game1.tinyFont, new Vector2(details.X + 3, y), MutedTextColor);
            y += lineHeight;
        }

        if (showLocation)
            this.DrawLocationCard(b, this.getMapInfo(member), location);
    }

    private void DrawWork(SpriteBatch b, SquadMemberState member, Rectangle area)
    {
        Utility.drawTextWithShadow(
            b,
            FitText(this.translate("companion.panel.work_orders", null), Game1.tinyFont, area.Width),
            Game1.tinyFont,
            new Vector2(area.X + 2, area.Y + 2),
            TextColor);

        int top = area.Y + 24;
        int gap = 6;
        int buttonHeight = Math.Clamp(area.Height / 4, 28, 38);
        int buttonWidth = Math.Max(1, (area.Width - gap * 2) / 3);
        this.DrawDirectiveButton(b, new Rectangle(area.X, top, buttonWidth, buttonHeight), this.translate("companion.directive.wood.short", null), member.SearchWood, CompanionDirective.SearchWood);
        this.DrawDirectiveButton(b, new Rectangle(area.X + buttonWidth + gap, top, buttonWidth, buttonHeight), this.translate("companion.directive.mining.short", null), member.SearchMining, CompanionDirective.SearchMining);
        this.DrawDirectiveButton(b, new Rectangle(area.X + (buttonWidth + gap) * 2, top, Math.Max(1, area.Right - (area.X + (buttonWidth + gap) * 2)), buttonHeight), this.translate("companion.directive.clear.short", null), member.ClearArea, CompanionDirective.ClearArea);

        int previewTop = top + buttonHeight + 8;
        int previewHeight = Math.Min(50, Math.Max(1, area.Bottom - previewTop));
        if (previewHeight >= 24)
            this.DrawTargetPreview(b, member, new Rectangle(area.X, previewTop, area.Width, previewHeight));

        int hintY = previewTop + previewHeight + 7;
        if (area.Bottom - hintY >= 18)
        {
            Utility.drawTextWithShadow(
                b,
                FitText(this.translate("companion.panel.work_hint", null), Game1.tinyFont, area.Width - 4),
                Game1.tinyFont,
                new Vector2(area.X + 2, hintY),
                MutedTextColor);
        }
    }

    private void DrawSkills(SpriteBatch b, SquadMemberState member, Rectangle area)
    {
        bool progressionEnabled = this.isProgressionEnabled();
        string heading = progressionEnabled
            ? this.translate("companion.panel.skills_heading", new { points = member.UnspentSkillPoints })
            : this.translate("companion.panel.skills_disabled_heading", null);
        Utility.drawTextWithShadow(b, FitText(heading, Game1.tinyFont, area.Width), Game1.tinyFont, new Vector2(area.X + 2, area.Y + 2), TextColor);

        int headingHeight = area.Height < 100 ? 18 : 24;
        Rectangle groupsArea = new(area.X, area.Y + headingHeight, area.Width, Math.Max(1, area.Height - headingHeight));
        List<IGrouping<string, CompanionSkillDefinition>> groups = CompanionProgression.Skills
            .GroupBy(p => p.Branch)
            .ToList();
        bool compactHeight = groupsArea.Height < 150;
        if (compactHeight && groupsArea.Width < groups.Count * 100)
        {
            this.DrawUltraCompactSkills(b, groupsArea, groups, member, progressionEnabled);
            return;
        }

        int columns = compactHeight && groups.Count > 0 && groupsArea.Width >= groups.Count * 100
            ? groups.Count
            : groupsArea.Width >= 300
                ? 2
                : 1;
        int rows = (int)Math.Ceiling(groups.Count / (double)columns);
        int gap = 7;
        int groupWidth = Math.Max(1, (groupsArea.Width - gap * (columns - 1)) / columns);
        int groupHeight = Math.Max(1, (groupsArea.Height - gap * (rows - 1)) / Math.Max(1, rows));

        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            int col = groupIndex % columns;
            int row = groupIndex / columns;
            int x = groupsArea.X + col * (groupWidth + gap);
            int y = groupsArea.Y + row * (groupHeight + gap);
            int width = col == columns - 1 ? groupsArea.Right - x : groupWidth;
            int height = row == rows - 1 ? groupsArea.Bottom - y : groupHeight;
            Rectangle groupArea = new(x, y, Math.Max(1, width), Math.Max(1, height));
            this.DrawSkillBranch(b, groupArea, groups[groupIndex].Key, groups[groupIndex].ToList(), member, progressionEnabled);
        }
    }

    private void DrawUltraCompactSkills(
        SpriteBatch b,
        Rectangle area,
        IReadOnlyList<IGrouping<string, CompanionSkillDefinition>> groups,
        SquadMemberState member,
        bool progressionEnabled)
    {
        int rowGap = 3;
        int rowHeight = Math.Max(1, (area.Height - rowGap * Math.Max(0, groups.Count - 1)) / Math.Max(1, groups.Count));
        int branchWidth = Math.Clamp(area.Width / 4, 36, 72);
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            IGrouping<string, CompanionSkillDefinition> group = groups[groupIndex];
            List<CompanionSkillDefinition> skills = group.ToList();
            int y = area.Y + groupIndex * (rowHeight + rowGap);
            Rectangle row = new(area.X, y, area.Width, rowHeight);
            this.DrawFlatPanel(b, row, new Color(246, 233, 207), new Color(143, 103, 64), 1);
            Utility.drawTextWithShadow(
                b,
                FitText(this.translate($"companion.skill.branch.{group.Key}", null), Game1.tinyFont, branchWidth - 8),
                Game1.tinyFont,
                new Vector2(row.X + 4, row.Y + Math.Max(1, (row.Height - Game1.tinyFont.LineSpacing) / 2)),
                TextColor);

            int nodesX = row.X + branchWidth;
            int nodesWidth = Math.Max(1, row.Right - nodesX - 3);
            int nodeGap = 3;
            int nodeWidth = Math.Max(1, (nodesWidth - nodeGap * Math.Max(0, skills.Count - 1)) / Math.Max(1, skills.Count));
            for (int skillIndex = 0; skillIndex < skills.Count; skillIndex++)
            {
                CompanionSkillDefinition skill = skills[skillIndex];
                int x = nodesX + skillIndex * (nodeWidth + nodeGap);
                int width = skillIndex == skills.Count - 1 ? row.Right - 3 - x : nodeWidth;
                Rectangle node = new(x, row.Y + 2, Math.Max(1, width), Math.Max(1, row.Height - 4));
                bool unlocked = member.UnlockedSkillIds.Contains(skill.Id, StringComparer.OrdinalIgnoreCase);
                bool availableToLearn = progressionEnabled
                    && !unlocked
                    && member.UnspentSkillPoints >= skill.Cost
                    && (string.IsNullOrWhiteSpace(skill.PrerequisiteId)
                        || member.UnlockedSkillIds.Contains(skill.PrerequisiteId, StringComparer.OrdinalIgnoreCase));
                this.DrawSkillNode(b, node, skill, unlocked, availableToLearn, (skillIndex + 1).ToString());
                this.skillButtons.Add((node, skill.Id));
            }
        }
    }

    private void DrawSkillBranch(
        SpriteBatch b,
        Rectangle area,
        string branch,
        IReadOnlyList<CompanionSkillDefinition> skills,
        SquadMemberState member,
        bool progressionEnabled)
    {
        this.DrawFlatPanel(b, area, new Color(246, 233, 207), new Color(143, 103, 64), 1);
        string title = this.translate($"companion.skill.branch.{branch}", null);
        Utility.drawTextWithShadow(b, FitText(title, Game1.tinyFont, area.Width - 16), Game1.tinyFont, new Vector2(area.X + 8, area.Y + 5), TextColor);

        bool compact = area.Height < 100;
        if (compact)
        {
            int compactGap = 3;
            int compactTop = area.Y + 22;
            int availableHeight = Math.Max(1, area.Bottom - compactTop - 5);
            int compactNodeHeight = Math.Min(31, availableHeight);
            int innerX = area.X + 6;
            int innerWidth = Math.Max(1, area.Width - 12);
            int nodeWidth = Math.Max(1, (innerWidth - Math.Max(0, skills.Count - 1) * compactGap) / Math.Max(1, skills.Count));
            int nodeY = compactTop + Math.Max(0, (availableHeight - compactNodeHeight) / 2);

            if (skills.Count > 1)
            {
                int firstCenterX = innerX + nodeWidth / 2;
                int lastCenterX = innerX + (skills.Count - 1) * (nodeWidth + compactGap) + nodeWidth / 2;
                b.Draw(
                    Game1.staminaRect,
                    new Rectangle(firstCenterX, nodeY + compactNodeHeight / 2, Math.Max(1, lastCenterX - firstCenterX), 2),
                    new Color(143, 103, 64) * 0.55f);
            }

            for (int i = 0; i < skills.Count; i++)
            {
                CompanionSkillDefinition skill = skills[i];
                int x = innerX + i * (nodeWidth + compactGap);
                int width = i == skills.Count - 1 ? area.Right - 6 - x : nodeWidth;
                Rectangle node = new(x, nodeY, Math.Max(1, width), compactNodeHeight);
                bool unlocked = member.UnlockedSkillIds.Contains(skill.Id, StringComparer.OrdinalIgnoreCase);
                bool availableToLearn = progressionEnabled
                    && !unlocked
                    && member.UnspentSkillPoints >= skill.Cost
                    && (string.IsNullOrWhiteSpace(skill.PrerequisiteId) || member.UnlockedSkillIds.Contains(skill.PrerequisiteId, StringComparer.OrdinalIgnoreCase));
                this.DrawSkillNode(b, node, skill, unlocked, availableToLearn, (i + 1).ToString());
                this.skillButtons.Add((node, skill.Id));
            }

            return;
        }

        int top = area.Y + 24;
        int available = Math.Max(1, area.Bottom - top - 6);
        int gap = 4;
        int nodeHeight = Math.Min(31, Math.Max(12, (available - Math.Max(0, skills.Count - 1) * gap) / Math.Max(1, skills.Count)));
        int totalHeight = skills.Count * nodeHeight + Math.Max(0, skills.Count - 1) * gap;
        if (totalHeight > available)
            return;

        int lineX = area.X + 18;
        if (skills.Count > 1)
            b.Draw(Game1.staminaRect, new Rectangle(lineX, top + nodeHeight / 2, 2, Math.Max(1, totalHeight - nodeHeight)), new Color(143, 103, 64) * 0.55f);

        for (int i = 0; i < skills.Count; i++)
        {
            CompanionSkillDefinition skill = skills[i];
            Rectangle node = new(area.X + 7, top + i * (nodeHeight + gap), Math.Max(1, area.Width - 14), nodeHeight);
            bool unlocked = member.UnlockedSkillIds.Contains(skill.Id, StringComparer.OrdinalIgnoreCase);
            bool availableToLearn = progressionEnabled
                && !unlocked
                && member.UnspentSkillPoints >= skill.Cost
                && (string.IsNullOrWhiteSpace(skill.PrerequisiteId) || member.UnlockedSkillIds.Contains(skill.PrerequisiteId, StringComparer.OrdinalIgnoreCase));
            this.DrawSkillNode(b, node, skill, unlocked, availableToLearn);
            this.skillButtons.Add((node, skill.Id));
        }
    }

    private void DrawInventory(SpriteBatch b, SquadMemberState member, Rectangle area)
    {
        int headerHeight = Math.Min(36, Math.Max(26, area.Height / 5));
        int withdrawWidth = Math.Min(150, Math.Max(84, area.Width / 3));
        this.withdrawAllButton = new Rectangle(area.Right - withdrawWidth, area.Y, withdrawWidth, headerHeight);
        Utility.drawTextWithShadow(
            b,
            FitText(this.translate("companion.inventory.title", new { npc = member.DisplayName }), Game1.tinyFont, Math.Max(1, area.Width - withdrawWidth - 10)),
            Game1.tinyFont,
            new Vector2(area.X + 2, area.Y + 8),
            TextColor);
        this.DrawButton(b, this.withdrawAllButton, this.translate("companion.inventory.withdraw_all", null), false, danger: false);

        IReadOnlyList<Item> items = this.GetCachedInventoryItems(member);
        Rectangle grid = new(area.X, area.Y + headerHeight + 7, area.Width, Math.Max(1, area.Height - headerHeight - 7));
        int gap = 6;
        int columns = Math.Clamp(grid.Width / 54, 1, Math.Min(5, this.inventorySlots));
        int rows = (int)Math.Ceiling(this.inventorySlots / (double)columns);
        int slotByWidth = Math.Max(1, (grid.Width - gap * (columns - 1)) / columns);
        int slotByHeight = Math.Max(1, (grid.Height - gap * (rows - 1)) / Math.Max(1, rows));
        int slotSize = Math.Min(60, Math.Min(slotByWidth, slotByHeight));

        for (int i = 0; i < this.inventorySlots; i++)
        {
            int col = i % columns;
            int row = i / columns;
            Rectangle slot = new(grid.X + col * (slotSize + gap), grid.Y + row * (slotSize + gap), slotSize, slotSize);
            if (slot.Right > grid.Right || slot.Bottom > grid.Bottom)
                continue;
            this.DrawFlatPanel(b, slot, new Color(235, 220, 193), SurfaceBorder, 2);
            this.inventorySlotsBounds.Add((slot, i));
            if (i >= items.Count)
                continue;

            Item item = items[i];
            float scale = Math.Clamp((slotSize - 7) / 64f, 0.35f, 0.9f);
            item.drawInMenu(b, new Vector2(slot.X + 4, slot.Y + 4), scale);
            if (item.Stack > 1 && slotSize >= 32)
            {
                string count = item.Stack.ToString();
                Vector2 countSize = Game1.tinyFont.MeasureString(count);
                Utility.drawTextWithShadow(
                    b,
                    count,
                    Game1.tinyFont,
                    new Vector2(slot.Right - countSize.X - 3, slot.Bottom - countSize.Y - 1),
                    Color.White);
            }
        }

        if (items.Count == 0 && grid.Height >= 55)
        {
            Utility.drawTextWithShadow(
                b,
                FitText(this.translate("companion.inventory.empty", null), Game1.tinyFont, grid.Width - 8),
                Game1.tinyFont,
                new Vector2(grid.X + 4, grid.Bottom - 22),
                MutedTextColor);
        }
    }

    private void DrawSummaryStrip(SpriteBatch b, Rectangle area, IReadOnlyList<string> lines)
    {
        int count = Math.Min(3, lines.Count);
        if (count <= 0)
            return;
        int gap = 4;
        int width = Math.Max(1, (area.Width - gap * (count - 1)) / count);
        for (int i = 0; i < count; i++)
        {
            int x = area.X + i * (width + gap);
            Rectangle chip = new(x, area.Y, i == count - 1 ? area.Right - x : width, area.Height);
            this.DrawFlatPanel(b, chip, new Color(244, 230, 202), new Color(143, 103, 64), 1);
            Utility.drawTextWithShadow(
                b,
                FitText(lines[i], Game1.tinyFont, chip.Width - 12),
                Game1.tinyFont,
                new Vector2(chip.X + 6, chip.Y + Math.Max(3, (chip.Height - Game1.tinyFont.LineSpacing) / 2)),
                MutedTextColor);
        }
    }

    private void DrawLocationCard(SpriteBatch b, CompanionPanelMapInfo info, Rectangle area)
    {
        this.DrawFlatPanel(b, area, new Color(237, 227, 207), new Color(143, 103, 64), 1);
        string status = this.translate(info.StatusKey, null);
        Utility.drawTextWithShadow(b, FitText(status, Game1.tinyFont, area.Width - 16), Game1.tinyFont, new Vector2(area.X + 8, area.Y + 7), TextColor);

        int lineY = area.Bottom - 20;
        int startX = area.X + 12;
        int endX = area.Right - 12;
        b.Draw(Game1.staminaRect, new Rectangle(startX, lineY, Math.Max(1, endX - startX), 2), Color.Black * 0.18f);
        b.Draw(Game1.staminaRect, new Rectangle(startX - 3, lineY - 3, 8, 8), AccentBlue);
        int npcX = info.SameLocation
            ? Math.Clamp(startX + (info.NpcX - info.OwnerX) * 5, startX, endX - 6)
            : endX - 6;
        b.Draw(Game1.staminaRect, new Rectangle(npcX, lineY - 3, 8, 8), this.GetMapStatusColor(info.StatusKey));
    }

    private void DrawTargetPreview(SpriteBatch b, SquadMemberState member, Rectangle bounds)
    {
        this.DrawFlatPanel(b, bounds, new Color(242, 230, 204), new Color(137, 99, 62), 1);
        string text = !string.IsNullOrWhiteSpace(member.PreviewTargetKey) && member.PreviewTargetX >= 0 && member.PreviewTargetY >= 0
            ? this.translate("companion.preview.target", new
            {
                target = this.translate(member.PreviewTargetKey, null),
                x = member.PreviewTargetX,
                y = member.PreviewTargetY
            })
            : this.translate("companion.preview.reason", new
            {
                reason = string.IsNullOrWhiteSpace(member.PreviewReasonKey)
                    ? this.translate("companion.preview.inactive", null)
                    : this.translate(member.PreviewReasonKey, null)
            });
        Utility.drawTextWithShadow(
            b,
            FitText(text, Game1.tinyFont, bounds.Width - 16),
            Game1.tinyFont,
            new Vector2(bounds.X + 8, bounds.Y + Math.Max(4, (bounds.Height - Game1.tinyFont.LineSpacing) / 2)),
            MutedTextColor);
    }

    private void DrawDirectiveButton(SpriteBatch b, Rectangle bounds, string label, bool active, CompanionDirective directive)
    {
        Point mouse = new(Game1.getMouseX(), Game1.getMouseY());
        Color fill = active ? ButtonActive : bounds.Contains(mouse) ? RowHoverColor : ButtonIdle;
        this.DrawFlatPanel(b, bounds, fill, active ? AccentGreen : SurfaceBorder, 2);
        Rectangle indicator = new(bounds.X + 7, bounds.Center.Y - 6, 5, 12);
        b.Draw(Game1.staminaRect, indicator, active ? AccentGreen : Color.Black * 0.22f);
        string text = FitText(label, Game1.tinyFont, bounds.Width - 24);
        Utility.drawTextWithShadow(
            b,
            text,
            Game1.tinyFont,
            new Vector2(bounds.X + 17, bounds.Y + Math.Max(3, (bounds.Height - Game1.tinyFont.LineSpacing) / 2)),
            TextColor);
        this.directiveButtons.Add((bounds, directive));
    }

    private void DrawSkillNode(
        SpriteBatch b,
        Rectangle bounds,
        CompanionSkillDefinition skill,
        bool unlocked,
        bool available,
        string? compactLabel = null)
    {
        Point mouse = new(Game1.getMouseX(), Game1.getMouseY());
        Color fill = unlocked
            ? new Color(205, 233, 201)
            : available
                ? new Color(242, 222, 151)
                : new Color(220, 211, 195);
        if (bounds.Contains(mouse))
            fill = Color.Lerp(fill, Color.White, 0.24f);
        this.DrawFlatPanel(b, bounds, fill, unlocked ? AccentGreen : SurfaceBorder, 1);

        int badgeSize = Math.Max(1, Math.Min(22, Math.Max(8, bounds.Height - 8)));
        badgeSize = Math.Min(badgeSize, Math.Max(1, bounds.Height - 4));
        Rectangle badge = new(bounds.Right - badgeSize - 4, bounds.Center.Y - badgeSize / 2, badgeSize, badgeSize);
        b.Draw(Game1.staminaRect, badge, unlocked ? AccentGreen : available ? AccentGold : Color.Black * 0.20f);
        string badgeText = unlocked ? "✓" : skill.Cost.ToString();
        Vector2 badgeTextSize = Game1.tinyFont.MeasureString(badgeText);
        Utility.drawTextWithShadow(
            b,
            badgeText,
            Game1.tinyFont,
            new Vector2(badge.Center.X - badgeTextSize.X / 2f, badge.Center.Y - badgeTextSize.Y / 2f),
            unlocked || available ? Color.White : TextColor);

        string label = FitText(compactLabel ?? this.translate(skill.NameKey, null), Game1.tinyFont, bounds.Width - badgeSize - 18);
        Utility.drawTextWithShadow(
            b,
            label,
            Game1.tinyFont,
            new Vector2(bounds.X + 7, bounds.Y + Math.Max(2, (bounds.Height - Game1.tinyFont.LineSpacing) / 2)),
            TextColor);
    }
}
