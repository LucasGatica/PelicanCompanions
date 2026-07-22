using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;

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
        int buttonHeight = Math.Clamp(area.Height / 6, 28, 38);
        int buttonWidth = Math.Max(1, (area.Width - gap) / 2);
        int secondRowTop = top + buttonHeight + gap;
        this.DrawDirectiveButton(b, new Rectangle(area.X, top, buttonWidth, buttonHeight), this.translate("companion.directive.wood.short", null), member.SearchWood, CompanionDirective.SearchWood);
        this.DrawDirectiveButton(b, new Rectangle(area.X + buttonWidth + gap, top, Math.Max(1, area.Right - area.X - buttonWidth - gap), buttonHeight), this.translate("companion.directive.mining.short", null), member.SearchMining, CompanionDirective.SearchMining);
        this.DrawDirectiveButton(b, new Rectangle(area.X, secondRowTop, buttonWidth, buttonHeight), this.translate("companion.directive.watering.short", null), member.SearchWatering, CompanionDirective.SearchWatering);
        this.DrawDirectiveButton(b, new Rectangle(area.X + buttonWidth + gap, secondRowTop, Math.Max(1, area.Right - area.X - buttonWidth - gap), buttonHeight), this.translate("companion.directive.clear.short", null), member.ClearArea, CompanionDirective.ClearArea);

        int previewTop = secondRowTop + buttonHeight + 8;
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
        List<IGrouping<string, CompanionSkillDefinition>> groups = CompanionProgression.Skills
            .GroupBy(p => p.Branch)
            .ToList();
        if (groups.Count == 0 || area.Width <= 1 || area.Height <= 1)
            return;

        CompanionSkillDefinition? inspected = this.GetInspectedSkill(member, progressionEnabled);
        bool useSideInspector = area.Width >= 520 && area.Height >= 200;
        if (useSideInspector)
        {
            int gap = 8;
            int inspectorWidth = Math.Clamp(
                (int)Math.Round(area.Width * 0.35f),
                220,
                Math.Min(320, Math.Max(220, area.Width - 250)));
            Rectangle treeArea = new(area.X, area.Y, Math.Max(1, area.Width - inspectorWidth - gap), area.Height);
            this.skillDetailsArea = new(treeArea.Right + gap, area.Y, inspectorWidth, area.Height);
            this.skillDetailsEmbedded = true;
            this.DrawSkillTree(b, treeArea, groups, member, progressionEnabled);
            if (inspected is not null)
                this.DrawSkillDetails(b, this.skillDetailsArea, inspected, member, progressionEnabled);
            return;
        }

        int detailsHeight = area.Height >= 300 && area.Width >= 230
            ? Math.Clamp(area.Height * 2 / 5, 140, 160)
            : 0;
        int detailsGap = detailsHeight > 0 ? 7 : 0;
        Rectangle stackedTreeArea = new(
            area.X,
            area.Y,
            area.Width,
            Math.Max(1, area.Height - detailsHeight - detailsGap));
        this.DrawSkillTree(b, stackedTreeArea, groups, member, progressionEnabled);
        if (detailsHeight <= 0 || inspected is null)
            return;

        this.skillDetailsEmbedded = true;
        this.skillDetailsArea = new(area.X, stackedTreeArea.Bottom + detailsGap, area.Width, detailsHeight);
        this.DrawSkillDetails(b, this.skillDetailsArea, inspected, member, progressionEnabled);
    }

    private void DrawSkillTree(
        SpriteBatch b,
        Rectangle area,
        IReadOnlyList<IGrouping<string, CompanionSkillDefinition>> groups,
        SquadMemberState member,
        bool progressionEnabled)
    {
        this.DrawFlatPanel(b, area, new Color(245, 234, 212), SurfaceBorder, 1);
        Rectangle inner = new(area.X + 2, area.Y + 2, Math.Max(1, area.Width - 4), Math.Max(1, area.Height - 4));
        int laneHeight = Math.Max(1, inner.Height / Math.Max(1, groups.Count));
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            int y = inner.Y + groupIndex * laneHeight;
            int height = groupIndex == groups.Count - 1 ? inner.Bottom - y : laneHeight;
            Rectangle lane = new(inner.X, y, inner.Width, Math.Max(1, height));
            this.DrawSkillBranchLane(b, lane, groups[groupIndex].Key, groups[groupIndex].ToList(), member, progressionEnabled);
            if (groupIndex < groups.Count - 1)
            {
                b.Draw(
                    Game1.staminaRect,
                    new Rectangle(inner.X + 6, lane.Bottom - 1, Math.Max(1, inner.Width - 12), 1),
                    SurfaceBorder * 0.42f);
            }
        }
    }

    private void DrawSkillBranchLane(
        SpriteBatch b,
        Rectangle area,
        string branch,
        IReadOnlyList<CompanionSkillDefinition> skills,
        SquadMemberState member,
        bool progressionEnabled)
    {
        Color accent = GetSkillBranchAccent(branch);
        Color laneFill = Color.Lerp(new Color(246, 233, 211), accent, 0.055f);
        b.Draw(Game1.staminaRect, area, laneFill);
        if (area.Width <= 2 || area.Height <= 2 || skills.Count == 0)
            return;

        int markerWidth = Math.Min(5, Math.Max(3, area.Width / 90));
        b.Draw(
            Game1.staminaRect,
            new Rectangle(area.X, area.Y + 4, markerWidth, Math.Max(1, area.Height - 8)),
            accent);

        bool compact = area.Height < 48 || area.Width < 270;
        int branchWidth = Math.Clamp(area.Width * 34 / 100, compact ? 76 : 96, compact ? 120 : 150);
        int minimumNodesWidth = skills.Count * (compact ? 18 : 28) + Math.Max(0, skills.Count - 1) * 4;
        branchWidth = Math.Min(branchWidth, Math.Max(42, area.Width - minimumNodesWidth - 12));
        Rectangle branchArea = new(
            area.X + markerWidth + 7,
            area.Y + 4,
            Math.Max(1, branchWidth - markerWidth - 8),
            Math.Max(1, area.Height - 8));
        int learnedCount = 0;
        foreach (CompanionSkillDefinition skill in skills)
        {
            if (CompanionSkillTreePolicy.GetState(skill, member.UnlockedSkillIds, member.UnspentSkillPoints, progressionEnabled)
                == CompanionSkillTreeState.Learned)
            {
                learnedCount++;
            }
        }
        string branchTitle = this.translate($"companion.skill.branch.{branch}", null);
        float branchScale = GetTextScaleForBox(
            branchTitle,
            Game1.tinyFont,
            compact ? PanelMetaTextScale : PanelTextScale,
            branchArea.Width,
            Math.Max(1, branchArea.Height - 4),
            minimumScale: 0.46f);
        int branchLineHeight = GetScaledLineHeight(Game1.tinyFont, branchScale);
        if (branchArea.Height < branchLineHeight * 2 + 4)
        {
            DrawPanelText(
                b,
                FitText(branchTitle, Game1.tinyFont, branchArea.Width, branchScale),
                Game1.tinyFont,
                new Vector2(branchArea.X, branchArea.Center.Y - MeasureScaledText(branchTitle, Game1.tinyFont, branchScale).Y / 2f),
                TextColor,
                branchScale);
        }
        else
        {
            int textBlockHeight = branchLineHeight * 2 + 4;
            int textY = branchArea.Center.Y - textBlockHeight / 2;
            DrawPanelText(
                b,
                FitText(branchTitle, Game1.tinyFont, branchArea.Width, branchScale),
                Game1.tinyFont,
                new Vector2(branchArea.X, textY),
                TextColor,
                branchScale);
            DrawPanelText(
                b,
                $"{learnedCount}/{skills.Count}",
                Game1.tinyFont,
                new Vector2(branchArea.X, textY + branchLineHeight + 4),
                Color.Lerp(MutedTextColor, accent, 0.3f),
                PanelMetaTextScale);
        }

        int nodesX = area.X + branchWidth + 6;
        int nodesRight = area.Right - 7;
        int nodesWidth = Math.Max(1, nodesRight - nodesX);
        int nodeCellWidth = Math.Max(1, nodesWidth / skills.Count);
        int nodeSize = Math.Min(
            compact ? 42 : 64,
            Math.Min(Math.Max(1, area.Height - (compact ? 8 : 16)), Math.Max(1, nodeCellWidth - 12)));
        int nodeY = area.Center.Y - nodeSize / 2;
        List<Rectangle> nodes = new(skills.Count);
        for (int index = 0; index < skills.Count; index++)
        {
            int cellX = nodesX + index * nodeCellWidth;
            int cellRight = index == skills.Count - 1 ? nodesRight : cellX + nodeCellWidth;
            int x = cellX + Math.Max(0, (cellRight - cellX - nodeSize) / 2);
            nodes.Add(new Rectangle(x, nodeY, Math.Max(1, nodeSize), Math.Max(1, nodeSize)));
        }

        for (int index = 0; index < skills.Count; index++)
        {
            CompanionSkillDefinition skill = skills[index];
            if (string.IsNullOrWhiteSpace(skill.PrerequisiteId))
                continue;
            int prerequisiteIndex = -1;
            for (int candidateIndex = 0; candidateIndex < skills.Count; candidateIndex++)
            {
                if (!string.Equals(skills[candidateIndex].Id, skill.PrerequisiteId, StringComparison.OrdinalIgnoreCase))
                    continue;
                prerequisiteIndex = candidateIndex;
                break;
            }
            if (prerequisiteIndex < 0)
                continue;

            Rectangle prerequisite = nodes[prerequisiteIndex];
            Rectangle target = nodes[index];
            int lineX = prerequisite.Right;
            int lineWidth = Math.Max(1, target.X - lineX);
            int thickness = compact ? 2 : 3;
            bool pathLearned = member.UnlockedSkillIds.Contains(skill.PrerequisiteId, StringComparer.OrdinalIgnoreCase);
            Color connector = pathLearned ? accent : Color.Lerp(SurfaceBorder, Color.White, 0.38f);
            b.Draw(
                Game1.staminaRect,
                new Rectangle(lineX, prerequisite.Center.Y - thickness / 2, lineWidth, thickness),
                connector);
            int capSize = Math.Min(5, Math.Max(2, target.Height / 10));
            b.Draw(
                Game1.staminaRect,
                new Rectangle(Math.Max(lineX, target.X - capSize), target.Center.Y - capSize / 2, capSize, capSize),
                connector);
        }

        for (int index = 0; index < skills.Count; index++)
        {
            CompanionSkillDefinition skill = skills[index];
            CompanionSkillTreeState state = CompanionSkillTreePolicy.GetState(
                skill,
                member.UnlockedSkillIds,
                member.UnspentSkillPoints,
                progressionEnabled);
            this.DrawSkillNode(b, nodes[index], skill, state, accent, index + 1);
        }
    }

    private void DrawInventory(SpriteBatch b, SquadMemberState member, Rectangle area)
    {
        // At the smallest supported split-screen viewport (426x240), the tab body
        // is only about 82px tall. Keep the hat/actions in a shallow header and
        // put all four equipment slots on one row; cargo remains available through
        // Withdraw All without pushing any equipment hitbox outside the body.
        bool ultraCompact = area.Height < 140;
        int headerHeight = ultraCompact
            ? Math.Min(Math.Max(1, area.Height - 4), Math.Clamp(area.Height / 3, 18, 28))
            : Math.Clamp(area.Height / 7, 38, 48);
        int hatSize = Math.Min(headerHeight, 48);
        this.hatSlot = new Rectangle(area.X, area.Y, hatSize, hatSize);
        this.DrawFlatPanel(b, this.hatSlot, new Color(235, 220, 193), AccentGold, 2);

        Item? equippedHat = this.getEquippedHat(member);
        if (equippedHat is not null)
        {
            float hatScale = Math.Clamp((hatSize - 7) / 64f, 0.35f, 0.75f);
            equippedHat.drawInMenu(b, new Vector2(this.hatSlot.X + 4, this.hatSlot.Y + 4), hatScale);
        }
        else if (this.hasEquippedHat(member))
        {
            DrawCenteredPanelText(b, "?", Game1.smallFont, this.hatSlot, MutedTextColor, PanelTextScale, 4, 4);
        }

        int buttonHeight = Math.Min(36, headerHeight);
        int buttonWidth = Math.Min(160, Math.Max(76, area.Width / 3));
        this.withdrawAllButton = new Rectangle(area.Right - buttonWidth, area.Y, buttonWidth, buttonHeight);
        int labelX = this.hatSlot.Right + 8;
        int labelWidth = Math.Max(1, this.withdrawAllButton.X - labelX - 8);
        if (labelWidth >= 28)
        {
            string hatLabel = equippedHat is not null
                ? this.translate("companion.hat.slot_equipped", new { hat = equippedHat.DisplayName })
                : this.hasEquippedHat(member)
                    ? this.translate("companion.hat.slot_unavailable", null)
                    : this.translate("companion.hat.slot_empty", null);
            Utility.drawTextWithShadow(
                b,
                FitText(hatLabel, Game1.tinyFont, labelWidth),
                Game1.tinyFont,
                new Vector2(labelX, area.Y + Math.Max(3, (headerHeight - Game1.tinyFont.LineSpacing) / 2)),
                TextColor);
        }
        this.DrawButton(b, this.withdrawAllButton, this.translate("companion.inventory.withdraw_all", null), false, danger: false);

        int equipmentGap = ultraCompact ? 3 : 5;
        int equipmentTop;
        if (ultraCompact)
        {
            equipmentTop = Math.Min(area.Bottom, area.Y + headerHeight + equipmentGap);
        }
        else
        {
            int equipmentTitleY = area.Y + headerHeight + 4;
            Utility.drawTextWithShadow(
                b,
                FitText(this.translate("companion.equipment.title", null), Game1.tinyFont, area.Width),
                Game1.tinyFont,
                new Vector2(area.X, equipmentTitleY),
                TextColor);
            equipmentTop = equipmentTitleY + Game1.tinyFont.LineSpacing + 1;
        }

        int equipmentColumns = ultraCompact || area.Width >= 560 ? 4 : 2;
        int equipmentRows = (int)Math.Ceiling(EquipmentSlotOrder.Length / (double)equipmentColumns);
        int availableCardHeight = ultraCompact
            ? Math.Max(1, (area.Bottom - equipmentTop - equipmentGap * (equipmentRows - 1)) / equipmentRows)
            : Math.Max(
                24,
                (area.Bottom - 52 - equipmentTop - equipmentGap * (equipmentRows - 1)) / equipmentRows);
        int equipmentCardHeight = ultraCompact
            ? availableCardHeight
            : Math.Min(area.Height >= 340 ? 58 : 46, availableCardHeight);
        int equipmentCardWidth = Math.Max(1, (area.Width - equipmentGap * (equipmentColumns - 1)) / equipmentColumns);
        int equipmentBottom = equipmentTop;
        for (int index = 0; index < EquipmentSlotOrder.Length; index++)
        {
            int column = index % equipmentColumns;
            int row = index / equipmentColumns;
            int x = area.X + column * (equipmentCardWidth + equipmentGap);
            Rectangle bounds = new(
                x,
                equipmentTop + row * (equipmentCardHeight + equipmentGap),
                column == equipmentColumns - 1 ? Math.Max(1, area.Right - x) : equipmentCardWidth,
                equipmentCardHeight);
            this.DrawEquipmentSlot(b, member, EquipmentSlotOrder[index], bounds);
            equipmentBottom = Math.Max(equipmentBottom, bounds.Bottom);
        }

        if (ultraCompact)
            return;

        IReadOnlyList<Item> items = this.GetCachedInventoryItems(member);
        Rectangle grid = new(area.X, equipmentBottom + 7, area.Width, Math.Max(1, area.Bottom - equipmentBottom - 7));
        int gap = 6;
        int columns = Math.Clamp(grid.Width / 54, 1, Math.Min(5, this.inventorySlots));
        int inventoryRows = (int)Math.Ceiling(this.inventorySlots / (double)columns);
        int slotByWidth = Math.Max(1, (grid.Width - gap * (columns - 1)) / columns);
        int slotByHeight = Math.Max(1, (grid.Height - gap * (inventoryRows - 1)) / Math.Max(1, inventoryRows));
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

    private void DrawEquipmentSlot(
        SpriteBatch b,
        SquadMemberState member,
        CompanionEquipmentSlot slot,
        Rectangle bounds)
    {
        Item? item = this.getEquipmentItem(member, slot);
        bool hasPersistedItem = item is not null || this.hasEquipmentItem(member, slot);
        this.DrawFlatPanel(
            b,
            bounds,
            hasPersistedItem ? new Color(226, 234, 207) : new Color(235, 220, 193),
            hasPersistedItem ? AccentGreen : SurfaceBorder,
            2);
        this.equipmentSlotsBounds.Add((bounds, slot));

        string label = this.translate(GetEquipmentSlotTranslationKey(slot), null);
        Utility.drawTextWithShadow(
            b,
            FitText(label, Game1.tinyFont, Math.Max(1, bounds.Width - 8)),
            Game1.tinyFont,
            new Vector2(bounds.X + 4, bounds.Y + 1),
            TextColor);

        int contentTop = bounds.Y + Math.Min(Game1.tinyFont.LineSpacing, Math.Max(12, bounds.Height / 3));
        int contentHeight = Math.Max(1, bounds.Bottom - contentTop - 3);
        int iconSize = Math.Min(40, contentHeight);
        Rectangle iconBounds = new(bounds.X + 4, contentTop, iconSize, iconSize);
        if (item is not null)
        {
            float scale = Math.Clamp((iconSize - 3) / 64f, 0.3f, 0.7f);
            item.drawInMenu(b, new Vector2(iconBounds.X + 1, iconBounds.Y + 1), scale);
        }
        else
        {
            DrawCenteredPanelText(
                b,
                hasPersistedItem ? "?" : "–",
                Game1.smallFont,
                iconBounds,
                MutedTextColor,
                PanelCompactTextScale,
                2,
                2);
        }

        int detailsX = iconBounds.Right + 3;
        int detailsWidth = Math.Max(1, bounds.Right - detailsX - 3);
        string name = item?.DisplayName
            ?? this.translate(hasPersistedItem ? "companion.equipment.unavailable" : "companion.equipment.empty", null);
        Utility.drawTextWithShadow(
            b,
            FitText(name, Game1.tinyFont, detailsWidth),
            Game1.tinyFont,
            new Vector2(detailsX, contentTop),
            hasPersistedItem ? TextColor : MutedTextColor);

        if (item is Tool tool && contentHeight >= Game1.tinyFont.LineSpacing * 2)
        {
            string detail = tool is WateringCan wateringCan
                ? this.translate("companion.equipment.water", new
                {
                    current = wateringCan.WaterLeft,
                    capacity = CompanionEquipmentPolicy.GetWateringCanCapacity(wateringCan.UpgradeLevel)
                })
                : this.translate("companion.equipment.upgrade", new { level = tool.UpgradeLevel });
            Utility.drawTextWithShadow(
                b,
                FitText(detail, Game1.tinyFont, detailsWidth),
                Game1.tinyFont,
                new Vector2(detailsX, contentTop + Game1.tinyFont.LineSpacing),
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
        CompanionSkillTreeState state,
        Color branchAccent,
        int tier)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        Point mouse = new(Game1.getMouseX(), Game1.getMouseY());
        bool selected = string.Equals(this.inspectedSkillId, skill.Id, StringComparison.OrdinalIgnoreCase);
        Color fill = state switch
        {
            CompanionSkillTreeState.Learned => Color.Lerp(new Color(226, 238, 216), branchAccent, 0.22f),
            CompanionSkillTreeState.Available => new Color(246, 226, 158),
            CompanionSkillTreeState.NeedsPoints => new Color(231, 220, 198),
            CompanionSkillTreeState.ProgressionDisabled => new Color(216, 213, 207),
            _ => new Color(222, 216, 204)
        };
        if (bounds.Contains(mouse))
            fill = Color.Lerp(fill, Color.White, 0.24f);
        Color stateColor = GetSkillStateColor(state, branchAccent);
        Color border = state is CompanionSkillTreeState.Learned or CompanionSkillTreeState.Available
            ? stateColor
            : Color.Lerp(SurfaceBorder, stateColor, 0.28f);
        if (selected && bounds.Width >= 8 && bounds.Height >= 8)
        {
            Rectangle selectionRing = new(bounds.X - 3, bounds.Y - 3, bounds.Width + 6, bounds.Height + 6);
            this.DrawFlatPanel(b, selectionRing, Color.Transparent, Color.Lerp(WindowBorder, branchAccent, 0.3f), 2);
        }
        int borderSize = state is CompanionSkillTreeState.Learned or CompanionSkillTreeState.Available ? 3 : 2;
        this.DrawFlatPanel(b, bounds, fill, border, borderSize);

        this.skillButtons.Add((bounds, skill.Id));
        if (bounds.Height < 26 || bounds.Width < 26)
        {
            this.DrawCompactSkillStateGlyph(b, bounds, state, stateColor);
            return;
        }

        string tierLabel = GetRomanTier(tier);
        float tierScale = GetTextScaleForHeight(Game1.tinyFont, PanelHeadingTextScale, bounds.Height - 14);
        string fittedTier = FitText(tierLabel, Game1.tinyFont, Math.Max(1, bounds.Width - 14), tierScale);
        Vector2 tierSize = MeasureScaledText(fittedTier, Game1.tinyFont, tierScale);
        DrawPanelText(
            b,
            fittedTier,
            Game1.tinyFont,
            new Vector2(bounds.Center.X - tierSize.X / 2f, bounds.Center.Y - tierSize.Y / 2f),
            state == CompanionSkillTreeState.Learned ? Color.White : state == CompanionSkillTreeState.ProgressionDisabled ? MutedTextColor : TextColor,
            tierScale);

        int markSize = Math.Clamp(bounds.Width / 6, 4, 9);
        Rectangle mark = new(bounds.Right - markSize - borderSize - 2, bounds.Y + borderSize + 2, markSize, markSize);
        switch (state)
        {
            case CompanionSkillTreeState.Learned:
                b.Draw(Game1.staminaRect, mark, stateColor);
                b.Draw(Game1.staminaRect, new Rectangle(mark.X + 1, mark.Center.Y, Math.Max(1, mark.Width / 3), 2), Color.White);
                b.Draw(Game1.staminaRect, new Rectangle(mark.Center.X - 1, mark.Y + 1, 2, Math.Max(1, mark.Height - 2)), Color.White);
                break;
            case CompanionSkillTreeState.Available:
                this.DrawFlatPanel(b, mark, Color.White * 0.5f, stateColor, 1);
                break;
            case CompanionSkillTreeState.NeedsPoints:
                b.Draw(Game1.staminaRect, new Rectangle(mark.Center.X - 1, mark.Y, 2, Math.Max(2, mark.Height - 3)), stateColor);
                b.Draw(Game1.staminaRect, new Rectangle(mark.Center.X - 1, mark.Bottom - 2, 2, 2), stateColor);
                break;
            default:
                b.Draw(Game1.staminaRect, new Rectangle(mark.X, mark.Center.Y - 1, mark.Width, 2), stateColor);
                break;
        }
    }

    private void DrawCompactSkillStateGlyph(
        SpriteBatch b,
        Rectangle bounds,
        CompanionSkillTreeState state,
        Color stateColor)
    {
        if (bounds.Width < 8 || bounds.Height < 6)
            return;
        int size = Math.Min(9, Math.Max(3, Math.Min(bounds.Height - 4, bounds.Width - 4)));
        int centerX = bounds.Right - size / 2 - 3;
        int centerY = bounds.Center.Y;
        switch (state)
        {
            case CompanionSkillTreeState.Learned:
                b.Draw(Game1.staminaRect, new Rectangle(centerX - size / 2, centerY - size / 2, size, size), stateColor);
                break;
            case CompanionSkillTreeState.Available:
                this.DrawFlatPanel(
                    b,
                    new Rectangle(centerX - size / 2, centerY - size / 2, size, size),
                    Color.White * 0.55f,
                    stateColor,
                    1);
                break;
            case CompanionSkillTreeState.NeedsPoints:
                b.Draw(Game1.staminaRect, new Rectangle(centerX - 1, centerY - size / 2, 2, Math.Max(2, size - 3)), stateColor);
                b.Draw(Game1.staminaRect, new Rectangle(centerX - 1, centerY + size / 2 - 1, 2, 2), stateColor);
                break;
            case CompanionSkillTreeState.ProgressionDisabled:
                b.Draw(Game1.staminaRect, new Rectangle(centerX - size / 2, centerY - 1, size, 2), stateColor);
                b.Draw(Game1.staminaRect, new Rectangle(centerX - 1, centerY - size / 2, 2, size), stateColor);
                break;
            default:
                b.Draw(Game1.staminaRect, new Rectangle(centerX - size / 2, centerY - 1, size, 2), stateColor);
                break;
        }
    }

    private CompanionSkillDefinition? GetInspectedSkill(SquadMemberState member, bool progressionEnabled)
    {
        CompanionSkillDefinition? inspected = CompanionProgression.Skills.FirstOrDefault(skill =>
            string.Equals(skill.Id, this.inspectedSkillId, StringComparison.OrdinalIgnoreCase));
        if (inspected is not null)
            return inspected;

        inspected = CompanionProgression.Skills.FirstOrDefault(skill =>
            CompanionSkillTreePolicy.GetState(skill, member.UnlockedSkillIds, member.UnspentSkillPoints, progressionEnabled)
                == CompanionSkillTreeState.Available)
            ?? CompanionProgression.Skills.FirstOrDefault(skill =>
                CompanionSkillTreePolicy.GetState(skill, member.UnlockedSkillIds, member.UnspentSkillPoints, progressionEnabled)
                    != CompanionSkillTreeState.Learned)
            ?? CompanionProgression.Skills.FirstOrDefault();
        this.inspectedSkillId = inspected?.Id;
        return inspected;
    }

    private void DrawSkillDetails(
        SpriteBatch b,
        Rectangle bounds,
        CompanionSkillDefinition skill,
        SquadMemberState member,
        bool progressionEnabled)
    {
        CompanionSkillTreeState state = CompanionSkillTreePolicy.GetState(
            skill,
            member.UnlockedSkillIds,
            member.UnspentSkillPoints,
            progressionEnabled);
        Color accent = GetSkillBranchAccent(skill.Branch);
        Color stateColor = GetSkillStateColor(state, accent);
        this.DrawFlatPanel(b, bounds, new Color(249, 238, 216), Color.Lerp(SurfaceBorder, accent, 0.45f), 1);
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 1, bounds.Y + 1, 5, Math.Max(1, bounds.Height - 2)), accent);

        Rectangle inner = new(bounds.X + 14, bounds.Y + 9, Math.Max(1, bounds.Width - 25), Math.Max(1, bounds.Height - 18));
        bool roomy = inner.Height >= 145;
        int y = inner.Y;
        if (roomy)
        {
            List<CompanionSkillDefinition> branchSkills = CompanionProgression.Skills
                .Where(candidate => string.Equals(candidate.Branch, skill.Branch, StringComparison.OrdinalIgnoreCase))
                .ToList();
            int tier = Math.Max(1, branchSkills.FindIndex(candidate => string.Equals(candidate.Id, skill.Id, StringComparison.OrdinalIgnoreCase)) + 1);
            string eyebrow = $"{this.translate($"companion.skill.branch.{skill.Branch}", null)} · {GetRomanTier(tier)}";
            DrawPanelText(
                b,
                FitText(eyebrow, Game1.tinyFont, inner.Width, PanelMetaTextScale),
                Game1.tinyFont,
                new Vector2(inner.X, y),
                Color.Lerp(MutedTextColor, accent, 0.34f),
                PanelMetaTextScale);
            y += GetScaledLineHeight(Game1.tinyFont, PanelMetaTextScale) + 3;
        }

        string fullName = this.translate(skill.NameKey, null);
        float nameScale = GetTextScaleForBox(
            fullName,
            Game1.tinyFont,
            roomy ? PanelHeadingTextScale : PanelTextScale,
            inner.Width,
            GetScaledLineHeight(Game1.tinyFont, roomy ? PanelHeadingTextScale : PanelTextScale),
            minimumScale: 0.54f);
        string name = FitText(fullName, Game1.tinyFont, inner.Width, nameScale);
        DrawPanelText(b, name, Game1.tinyFont, new Vector2(inner.X, y), TextColor, nameScale);
        y += GetScaledLineHeight(Game1.tinyFont, nameScale) + 6;

        int stateLineHeight = GetScaledLineHeight(Game1.tinyFont, PanelMetaTextScale);
        int stateRowHeight = stateLineHeight + 8;
        string stateText = this.translate(GetSkillStateTranslationKey(state), null);
        int preferredStateWidth = (int)Math.Ceiling(Game1.tinyFont.MeasureString(stateText).X * PanelMetaTextScale) + 16;
        bool stackMetadata = inner.Width < 200;
        int maximumStateWidth = stackMetadata
            ? inner.Width
            : Math.Min(132, inner.Width / 2);
        int stateWidth = Math.Clamp(preferredStateWidth, 48, Math.Max(48, maximumStateWidth));
        Rectangle stateBadge = new(inner.X, y, stateWidth, stateRowHeight);
        this.DrawFlatPanel(b, stateBadge, Color.Lerp(Color.White, stateColor, 0.34f), stateColor, 1);
        DrawCenteredPanelText(b, stateText, Game1.tinyFont, stateBadge, TextColor, PanelMetaTextScale, 10, 4);

        string costAndPoints = $"{this.translate("companion.skill.cost", new { cost = skill.Cost })} · {this.translate("companion.panel.points_short", new { points = member.UnspentSkillPoints })}";
        if (stackMetadata)
        {
            DrawPanelText(
                b,
                FitText(costAndPoints, Game1.tinyFont, inner.Width, PanelMetaTextScale),
                Game1.tinyFont,
                new Vector2(inner.X, stateBadge.Bottom + 4),
                MutedTextColor,
                PanelMetaTextScale);
            y = stateBadge.Bottom + 4 + stateLineHeight + 8;
        }
        else
        {
            int resourcesX = stateBadge.Right + 7;
            if (inner.Right - resourcesX >= 34)
            {
                string resources = FitText(costAndPoints, Game1.tinyFont, inner.Right - resourcesX, PanelMetaTextScale);
                Vector2 resourcesSize = MeasureScaledText(resources, Game1.tinyFont, PanelMetaTextScale);
                DrawPanelText(
                    b,
                    resources,
                    Game1.tinyFont,
                    new Vector2(resourcesX, stateBadge.Center.Y - resourcesSize.Y / 2f),
                    MutedTextColor,
                    PanelMetaTextScale);
            }
            y = stateBadge.Bottom + 8;
        }

        int footerLineHeight = GetScaledLineHeight(Game1.tinyFont, PanelMetaTextScale);
        string status = this.BuildSkillDetailStatus(skill, state);
        IReadOnlyList<string> footerLines = WrapText(
            status,
            Game1.tinyFont,
            inner.Width,
            roomy ? 3 : 2,
            PanelMetaTextScale);
        int footerHeight = Math.Max(1, footerLines.Count) * footerLineHeight;
        int footerY = inner.Bottom - footerHeight;
        if (footerY > y + 4)
        {
            b.Draw(Game1.staminaRect, new Rectangle(inner.X, y, inner.Width, 1), Color.Lerp(SurfaceBorder, accent, 0.26f) * 0.65f);
            y += 7;
        }

        int bodyLineHeight = GetScaledLineHeight(Game1.tinyFont, PanelTextScale);
        int maxDescriptionLines = Math.Max(0, (footerY - y - 5) / bodyLineHeight);
        IReadOnlyList<string> descriptionLines = WrapText(
            this.translate(skill.DescriptionKey, null),
            Game1.tinyFont,
            inner.Width,
            maxDescriptionLines,
            PanelTextScale);
        foreach (string line in descriptionLines)
        {
            DrawPanelText(b, line, Game1.tinyFont, new Vector2(inner.X, y), MutedTextColor, PanelTextScale);
            y += bodyLineHeight;
        }

        if (footerY < stateBadge.Bottom || footerLines.Count == 0)
            return;
        int statusY = footerY;
        foreach (string footerLine in footerLines)
        {
            DrawPanelText(
                b,
                footerLine,
                Game1.tinyFont,
                new Vector2(inner.X, statusY),
                state is CompanionSkillTreeState.Available or CompanionSkillTreeState.Learned ? stateColor : MutedTextColor,
                PanelMetaTextScale);
            statusY += footerLineHeight;
        }
    }

    private string BuildSkillDetailStatus(CompanionSkillDefinition skill, CompanionSkillTreeState state)
    {
        return state switch
        {
            CompanionSkillTreeState.Learned => this.translate("companion.skill.learned", null),
            CompanionSkillTreeState.Available => this.translate("companion.skill.learn_action", null),
            CompanionSkillTreeState.NeedsPoints => this.translate("companion.skill.no_points", null),
            CompanionSkillTreeState.ProgressionDisabled => this.translate("companion.skill.progression_disabled", null),
            CompanionSkillTreeState.LockedByPrerequisite => this.BuildSkillRequirementText(skill),
            _ => this.translate("companion.skill.locked", null)
        };
    }

    private string BuildSkillRequirementText(CompanionSkillDefinition skill)
    {
        CompanionSkillDefinition? prerequisite = CompanionProgression.Skills.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, skill.PrerequisiteId, StringComparison.OrdinalIgnoreCase));
        return prerequisite is null
            ? this.translate("companion.skill.locked", null)
            : this.translate("companion.skill.requires", new { skill = this.translate(prerequisite.NameKey, null) });
    }

    private static string GetSkillStateTranslationKey(CompanionSkillTreeState state)
    {
        return state switch
        {
            CompanionSkillTreeState.Learned => "companion.skill.state.learned",
            CompanionSkillTreeState.Available => "companion.skill.state.available",
            CompanionSkillTreeState.NeedsPoints => "companion.skill.state.no_points",
            CompanionSkillTreeState.ProgressionDisabled => "companion.skill.state.disabled",
            _ => "companion.skill.state.locked"
        };
    }

    private static Color GetSkillStateColor(CompanionSkillTreeState state, Color branchAccent)
    {
        return state switch
        {
            CompanionSkillTreeState.Learned => branchAccent,
            CompanionSkillTreeState.Available => AccentGold,
            CompanionSkillTreeState.NeedsPoints => new Color(153, 125, 80),
            CompanionSkillTreeState.ProgressionDisabled => new Color(126, 124, 119),
            _ => new Color(139, 129, 114)
        };
    }

    private static Color GetSkillBranchAccent(string branch)
    {
        return branch switch
        {
            "Lumbering" => AccentGreen,
            "Mining" => new Color(74, 113, 154),
            "Utility" => new Color(126, 91, 144),
            "Fishing" => new Color(45, 137, 166),
            _ => AccentBlue
        };
    }

    private static string GetRomanTier(int tier)
    {
        return tier switch
        {
            1 => "I",
            2 => "II",
            3 => "III",
            _ => tier.ToString()
        };
    }
}
