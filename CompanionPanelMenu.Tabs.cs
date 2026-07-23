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
        int commandHeight = area.Height >= 180
            ? 44
            : Math.Clamp(area.Height / 5, 28, 38);
        int commandY = area.Bottom - commandHeight;
        int gap = area.Width >= 520 ? 8 : 5;
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

        Rectangle detailContent = details;
        if (details.Width >= 80 && details.Height >= 38)
        {
            this.DrawMenuCard(b, details, RowColor, this.GetMemberStatusColor(member));
            detailContent = new Rectangle(
                details.X + 17,
                details.Y + 8,
                Math.Max(1, details.Width - 29),
                Math.Max(1, details.Height - 16));
        }

        IReadOnlyList<string> lines = this.getDetailLines(member);
        int lineHeight = GetScaledLineHeight(Game1.tinyFont, PanelTextScale) + 2;
        int maxLines = Math.Max(1, detailContent.Height / lineHeight);
        int y = detailContent.Y + 1;
        foreach (string line in lines.Take(maxLines))
        {
            DrawPanelText(
                b,
                FitText(line, Game1.tinyFont, Math.Max(1, detailContent.Width - 3), PanelTextScale),
                Game1.tinyFont,
                new Vector2(detailContent.X, y),
                MutedTextColor,
                PanelTextScale,
                shadow: true);
            y += lineHeight;
        }

        if (showLocation)
            this.DrawLocationCard(b, this.getMapInfo(member), location);
    }

    private void DrawWork(SpriteBatch b, SquadMemberState member, Rectangle area)
    {
        bool roomyHeading = area.Width >= 320 && area.Height >= 180;
        SpriteFont headingFont = Game1.tinyFont;
        float headingScale = roomyHeading ? PanelHeadingTextScale : PanelTextScale;
        DrawPanelText(
            b,
            FitText(this.translate("companion.panel.work_orders", null), headingFont, area.Width, headingScale),
            headingFont,
            new Vector2(area.X + 2, area.Y + 2),
            TextColor,
            headingScale,
            shadow: true);

        int top = area.Y + (roomyHeading ? 27 : 24);
        int gap = area.Width >= 520 ? 8 : 6;
        int buttonHeight = area.Height >= 220
            ? 42
            : Math.Clamp(area.Height / 6, 28, 38);
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
            DrawPanelText(
                b,
                FitText(this.translate("companion.panel.work_hint", null), Game1.tinyFont, area.Width - 4, PanelMetaTextScale),
                Game1.tinyFont,
                new Vector2(area.X + 2, hintY),
                MutedTextColor,
                PanelMetaTextScale,
                shadow: true);
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
        int branchProgressLineHeight = GetScaledLineHeight(Game1.tinyFont, PanelCompactNumericTextScale);
        if (branchArea.Height < branchLineHeight + branchProgressLineHeight + 4)
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
            int textBlockHeight = branchLineHeight + branchProgressLineHeight + 4;
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
                PanelCompactNumericTextScale);
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
        CompanionInventoryWorkspace workspace = this.GetInventoryWorkspaceForPanel(member);
        bool compact = area.Height < 260;
        bool minimal = area.Height < 190;
        int gap = compact ? 3 : 5;
        int filterTop;
        int filterRight = area.Right;
        if (minimal)
        {
            int compactControlHeight = Math.Clamp(area.Height / 4, 18, 24);
            int controlCount = EquipmentSlotOrder.Length + 2;
            int compactControlWidth = Math.Max(
                1,
                (area.Width - gap * (controlCount - 1)) / controlCount);
            this.hatSlot = new Rectangle(
                area.X,
                area.Y,
                compactControlWidth,
                compactControlHeight);
            this.DrawFlatPanel(
                b,
                this.hatSlot,
                new Color(235, 220, 193),
                AccentGold,
                1);
            Item? equippedHat = this.getEquippedHat(member);
            if (equippedHat is not null)
            {
                float hatScale = Math.Clamp(
                    (compactControlHeight - 3) / 64f,
                    0.18f,
                    0.42f);
                DrawItemCenteredInBounds(
                    b,
                    equippedHat,
                    this.hatSlot,
                    hatScale);
            }
            else
            {
                DrawCenteredPanelText(
                    b,
                    this.hasEquippedHat(member) ? "?" : "–",
                    Game1.tinyFont,
                    this.hatSlot,
                    MutedTextColor,
                    PanelMetaTextScale,
                    2,
                    1);
            }

            for (int index = 0; index < EquipmentSlotOrder.Length; index++)
            {
                int x = this.hatSlot.Right + gap
                    + index * (compactControlWidth + gap);
                this.DrawEquipmentSlot(
                    b,
                    member,
                    EquipmentSlotOrder[index],
                    new Rectangle(
                        x,
                        area.Y,
                        compactControlWidth,
                        compactControlHeight));
            }

            int withdrawX = area.X
                + (controlCount - 1) * (compactControlWidth + gap);
            this.withdrawAllButton = new Rectangle(
                withdrawX,
                area.Y,
                Math.Max(1, area.Right - withdrawX),
                compactControlHeight);
            this.DrawButton(
                b,
                this.withdrawAllButton,
                this.translate("companion.inventory.withdraw_all", null),
                false,
                danger: false);
            filterTop = area.Y + compactControlHeight + gap;
            filterRight = area.Right;
        }
        else
        {
            int headerHeight = Math.Clamp(area.Height / 9, compact ? 22 : 34, 44);
            int hatSize = Math.Min(headerHeight, 44);
            this.hatSlot = new Rectangle(area.X, area.Y, hatSize, hatSize);
            this.DrawFlatPanel(b, this.hatSlot, new Color(235, 220, 193), AccentGold, 2);

            Item? equippedHat = this.getEquippedHat(member);
            if (equippedHat is not null)
            {
                float hatScale = Math.Clamp((hatSize - 7) / 64f, 0.35f, 0.75f);
                DrawItemCenteredInBounds(b, equippedHat, this.hatSlot, hatScale);
            }
            else if (this.hasEquippedHat(member))
            {
                DrawCenteredPanelText(b, "?", Game1.smallFont, this.hatSlot, MutedTextColor, PanelTextScale, 4, 4);
            }

            int buttonHeight = Math.Min(34, headerHeight);
            int buttonWidth = Math.Min(150, Math.Max(70, area.Width / 4));
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
                int labelLineHeight = GetScaledLineHeight(Game1.tinyFont, PanelTextScale);
                DrawPanelText(
                    b,
                    FitText(hatLabel, Game1.tinyFont, labelWidth, PanelTextScale),
                    Game1.tinyFont,
                    new Vector2(labelX, area.Y + Math.Max(3, (headerHeight - labelLineHeight) / 2)),
                    TextColor,
                    PanelTextScale,
                    shadow: true);
            }
            this.DrawButton(
                b,
                this.withdrawAllButton,
                this.translate("companion.inventory.withdraw_all", null),
                false,
                danger: false);

            int equipmentTop = area.Y + headerHeight + gap;
            int equipmentCardHeight = Math.Clamp(area.Height / 10, compact ? 22 : 34, 48);
            int equipmentCardWidth = Math.Max(1, (area.Width - gap * 3) / 4);
            int equipmentBottom = equipmentTop + equipmentCardHeight;
            for (int index = 0; index < EquipmentSlotOrder.Length; index++)
            {
                int x = area.X + index * (equipmentCardWidth + gap);
                Rectangle bounds = new(
                    x,
                    equipmentTop,
                    index == EquipmentSlotOrder.Length - 1 ? Math.Max(1, area.Right - x) : equipmentCardWidth,
                    equipmentCardHeight);
                this.DrawEquipmentSlot(b, member, EquipmentSlotOrder[index], bounds);
            }

            filterTop = equipmentBottom + gap;
        }

        int filterHeight = minimal
            ? this.withdrawAllButton.Height
            : Math.Clamp(area.Height / 12, 20, 30);
        CompanionInventoryFilter[] filters =
        {
            CompanionInventoryFilter.DepositWood,
            CompanionInventoryFilter.DepositMinerals,
            CompanionInventoryFilter.KeepFood
        };
        int filterAreaWidth = Math.Max(1, filterRight - area.X);
        int filterWidth = Math.Max(1, (filterAreaWidth - gap * (filters.Length - 1)) / filters.Length);
        for (int index = 0; index < filters.Length; index++)
        {
            int x = area.X + index * (filterWidth + gap);
            Rectangle bounds = new(
                x,
                filterTop,
                index == filters.Length - 1 ? Math.Max(1, filterRight - x) : filterWidth,
                filterHeight);
            CompanionInventoryFilter filter = filters[index];
            this.inventoryFilterButtons.Add((bounds, filter));
            this.DrawButton(
                b,
                bounds,
                this.translate(GetInventoryFilterTranslationKey(filter), null),
                this.getInventoryFilter(member, filter),
                danger: false);
        }

        int panesTop = filterTop + filterHeight + gap;
        if (panesTop >= area.Bottom - 12)
            return;

        int paneCount = workspace.ChestAvailable ? 3 : 2;
        int paneWidth = Math.Max(1, (area.Width - gap * (paneCount - 1)) / paneCount);
        Rectangle playerPane = new(
            area.X,
            panesTop,
            paneWidth,
            Math.Max(1, area.Bottom - panesTop));
        Rectangle companionPane = new(
            playerPane.Right + gap,
            panesTop,
            paneCount == 2 ? Math.Max(1, area.Right - playerPane.Right - gap) : paneWidth,
            playerPane.Height);
        this.DrawInventoryPane(
            b,
            CompanionInventoryEndpoint.Player,
            this.translate("companion.inventory.player", null),
            workspace.PlayerItems,
            Math.Max(12, workspace.PlayerItems.Count),
            playerPane);
        this.DrawInventoryPane(
            b,
            CompanionInventoryEndpoint.Companion,
            member.DisplayName,
            workspace.CompanionItems.Cast<Item?>().ToList(),
            this.inventorySlots,
            companionPane);
        if (workspace.ChestAvailable)
        {
            Rectangle chestPane = new(
                companionPane.Right + gap,
                panesTop,
                Math.Max(1, area.Right - companionPane.Right - gap),
                playerPane.Height);
            this.DrawInventoryPane(
                b,
                CompanionInventoryEndpoint.Chest,
                string.IsNullOrWhiteSpace(workspace.ChestDisplayName)
                    ? this.translate("companion.inventory.chest", null)
                    : workspace.ChestDisplayName,
                workspace.ChestItems,
                Math.Max(12, workspace.ChestItems.Count),
                chestPane);
        }
    }

    private void DrawInventoryPane(
        SpriteBatch b,
        CompanionInventoryEndpoint endpoint,
        string title,
        IReadOnlyList<Item?> items,
        int capacity,
        Rectangle bounds)
    {
        this.DrawMenuCard(b, bounds, RowColor, endpoint switch
        {
            CompanionInventoryEndpoint.Player => AccentBlue,
            CompanionInventoryEndpoint.Companion => AccentGreen,
            CompanionInventoryEndpoint.Chest => AccentGold,
            _ => SurfaceBorder
        });
        this.inventoryPaneBounds.Add((bounds, endpoint));

        int inset = bounds.Width < 100 ? 3 : 6;
        int titleHeight = Math.Clamp(bounds.Height / 8, 14, 24);
        Rectangle grid = new(
            bounds.X + inset,
            bounds.Y + titleHeight,
            Math.Max(1, bounds.Width - inset * 2),
            Math.Max(1, bounds.Height - titleHeight - inset));
        int gap = grid.Width < 130 ? 2 : 3;
        int totalSlots = Math.Max(1, capacity);
        int columns = Math.Clamp(
            (grid.Width + gap) / 34,
            1,
            endpoint == CompanionInventoryEndpoint.Companion ? 5 : 8);
        int slotByWidth = Math.Max(
            1,
            (grid.Width - gap * (columns - 1)) / columns);
        int preferredSlotSize = Math.Min(48, slotByWidth);
        int rowTargetSize = Math.Max(12, Math.Min(36, preferredSlotSize));
        int visibleRows = Math.Max(
            1,
            (grid.Height + gap) / (rowTargetSize + gap));
        int slotByHeight = Math.Max(
            1,
            (grid.Height - gap * (visibleRows - 1)) / visibleRows);
        int slotSize = Math.Max(
            1,
            Math.Min(preferredSlotSize, slotByHeight));
        int visibleCapacity = Math.Max(1, columns * visibleRows);
        int totalRows = (int)Math.Ceiling(totalSlots / (double)columns);
        int maxStartRow = Math.Max(0, totalRows - visibleRows);
        int maxOffset = maxStartRow * columns;
        int offset = Math.Clamp(
            this.inventoryPageOffsets.GetValueOrDefault(endpoint),
            0,
            maxOffset);
        offset -= offset % columns;
        this.inventoryPageOffsets[endpoint] = offset;
        this.inventoryPanePages.Add(
            new InventoryPanePageState(
                bounds,
                endpoint,
                columns,
                visibleCapacity,
                totalSlots,
                offset));

        string pageTitle = maxOffset > 0
            ? $"{title} · {offset + 1}–{Math.Min(totalSlots, offset + visibleCapacity)}/{totalSlots}"
            : title;
        DrawPanelText(
            b,
            FitText(pageTitle, Game1.tinyFont, Math.Max(1, bounds.Width - inset * 2), PanelTextScale),
            Game1.tinyFont,
            new Vector2(bounds.X + inset, bounds.Y + Math.Min(4, Math.Max(1, titleHeight / 4))),
            TextColor,
            PanelTextScale,
            shadow: true);

        int visibleSlots = Math.Min(visibleCapacity, totalSlots - offset);
        for (int visibleIndex = 0; visibleIndex < visibleSlots; visibleIndex++)
        {
            int index = offset + visibleIndex;
            int column = visibleIndex % columns;
            int row = visibleIndex / columns;
            Rectangle slot = new(
                grid.X + column * (slotSize + gap),
                grid.Y + row * (slotSize + gap),
                slotSize,
                slotSize);
            if (slot.Right > grid.Right || slot.Bottom > grid.Bottom)
                continue;

            this.DrawFlatPanel(b, slot, new Color(235, 220, 193), SurfaceBorder, slotSize >= 24 ? 2 : 1);
            Item? item = index < items.Count ? items[index] : null;
            if (item is null)
                continue;

            switch (endpoint)
            {
                case CompanionInventoryEndpoint.Player:
                    this.playerInventorySlotsBounds.Add((slot, index));
                    break;
                case CompanionInventoryEndpoint.Companion:
                    this.inventorySlotsBounds.Add((slot, index));
                    break;
                case CompanionInventoryEndpoint.Chest:
                    this.chestInventorySlotsBounds.Add((slot, index));
                    break;
            }

            float scale = Math.Clamp((slotSize - 5) / 64f, 0.18f, 0.75f);
            DrawItemCenteredInBounds(b, item, slot, scale);
            if (item.Stack > 1 && slotSize >= 20)
            {
                string count = item.Stack.ToString();
                float countScale = slotSize < 28 ? PanelCompactTextScale : PanelCompactNumericTextScale;
                Vector2 countSize = MeasureScaledText(count, Game1.tinyFont, countScale);
                DrawPanelText(
                    b,
                    count,
                    Game1.tinyFont,
                    new Vector2(slot.Right - countSize.X - 2, slot.Bottom - countSize.Y),
                    Color.White,
                    countScale,
                    shadow: true);
            }
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
        Color fill = hasPersistedItem ? SelectedRowColor : ButtonIdle;
        Color accent = hasPersistedItem ? AccentGreen : SurfaceBorder;
        bool texturedCard = bounds.Width >= 64 && bounds.Height >= 40;
        if (texturedCard)
        {
            this.DrawTexturedPanel(b, bounds, fill);
            if (hasPersistedItem)
            {
                b.Draw(
                    Game1.staminaRect,
                    new Rectangle(bounds.X + 8, bounds.Bottom - 5, Math.Max(1, bounds.Width - 16), 3),
                    accent);
            }
        }
        else
            this.DrawFlatPanel(b, bounds, fill, accent, 2);
        this.equipmentSlotsBounds.Add((bounds, slot));

        int contentInset = texturedCard ? 8 : 4;
        string label = this.translate(GetEquipmentSlotTranslationKey(slot), null);
        float labelScale = texturedCard ? PanelTextScale : PanelMetaTextScale;
        DrawPanelText(
            b,
            FitText(label, Game1.tinyFont, Math.Max(1, bounds.Width - contentInset * 2), labelScale),
            Game1.tinyFont,
            new Vector2(bounds.X + contentInset, bounds.Y + (texturedCard ? 5 : 1)),
            TextColor,
            labelScale,
            shadow: true);

        int labelLineHeight = GetScaledLineHeight(Game1.tinyFont, labelScale);
        int contentTopOffset = texturedCard
            ? Math.Max(labelLineHeight + 5, bounds.Height / 3)
            : Math.Min(labelLineHeight, Math.Max(12, bounds.Height / 3));
        int contentTop = bounds.Y + Math.Min(Math.Max(1, bounds.Height - 3), contentTopOffset);
        int contentHeight = Math.Max(1, bounds.Bottom - contentTop - 3);
        int iconSize = Math.Min(40, contentHeight);
        Rectangle iconBounds = new(bounds.X + contentInset, contentTop, iconSize, iconSize);
        if (item is not null)
        {
            float scale = Math.Clamp((iconSize - 3) / 64f, 0.3f, 0.7f);
            DrawItemCenteredInBounds(b, item, iconBounds, scale);
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
        int detailsWidth = Math.Max(1, bounds.Right - detailsX - contentInset);
        string name = item?.DisplayName
            ?? this.translate(hasPersistedItem ? "companion.equipment.unavailable" : "companion.equipment.empty", null);
        DrawPanelText(
            b,
            FitText(name, Game1.tinyFont, detailsWidth, PanelMetaTextScale),
            Game1.tinyFont,
            new Vector2(detailsX, contentTop),
            hasPersistedItem ? TextColor : MutedTextColor,
            PanelMetaTextScale,
            shadow: true);

        int detailLineHeight = GetScaledLineHeight(Game1.tinyFont, PanelMetaTextScale);
        if (item is Tool tool && contentHeight >= detailLineHeight * 2)
        {
            string detail = tool is WateringCan wateringCan
                ? this.translate("companion.equipment.water", new
                {
                    current = wateringCan.WaterLeft,
                    capacity = CompanionEquipmentPolicy.GetWateringCanCapacity(wateringCan.UpgradeLevel)
                })
                : this.translate("companion.equipment.upgrade", new { level = tool.UpgradeLevel });
            DrawPanelText(
                b,
                FitText(detail, Game1.tinyFont, detailsWidth, PanelMetaTextScale),
                Game1.tinyFont,
                new Vector2(detailsX, contentTop + detailLineHeight),
                MutedTextColor,
                PanelMetaTextScale,
                shadow: true);
        }
    }

    private static void DrawItemCenteredInBounds(SpriteBatch b, Item item, Rectangle bounds, float scale)
    {
        const float MenuIconSize = 64f;
        Vector2 position = new(
            bounds.X + (bounds.Width - MenuIconSize * scale) / 2f,
            bounds.Y + (bounds.Height - MenuIconSize * scale) / 2f);
        item.drawInMenu(b, position, scale);
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
            Color accent = i switch
            {
                0 => AccentGreen,
                1 => AccentBlue,
                _ => AccentGold
            };
            this.DrawMenuCard(b, chip, HeaderCardColor, accent);
            int lineHeight = GetScaledLineHeight(Game1.tinyFont, PanelTextScale);
            DrawPanelText(
                b,
                FitText(lines[i], Game1.tinyFont, Math.Max(1, chip.Width - 24), PanelTextScale),
                Game1.tinyFont,
                new Vector2(chip.X + 17, chip.Y + Math.Max(3, (chip.Height - lineHeight) / 2)),
                MutedTextColor,
                PanelTextScale,
                shadow: true);
        }
    }

    private void DrawLocationCard(SpriteBatch b, CompanionPanelMapInfo info, Rectangle area)
    {
        Color statusColor = this.GetMapStatusColor(info.StatusKey);
        this.DrawMenuCard(b, area, RowColor, statusColor);
        string status = this.translate(info.StatusKey, null);
        DrawPanelText(
            b,
            FitText(status, Game1.tinyFont, Math.Max(1, area.Width - 30), PanelTextScale),
            Game1.tinyFont,
            new Vector2(area.X + 17, area.Y + 7),
            TextColor,
            PanelTextScale,
            shadow: true);

        int lineY = area.Bottom - 20;
        int startX = area.X + 18;
        int endX = area.Right - 12;
        b.Draw(Game1.staminaRect, new Rectangle(startX, lineY, Math.Max(1, endX - startX), 2), Color.Black * 0.18f);
        b.Draw(Game1.staminaRect, new Rectangle(startX - 3, lineY - 3, 8, 8), AccentBlue);
        int npcX = info.SameLocation
            ? Math.Clamp(startX + (info.NpcX - info.OwnerX) * 5, startX, endX - 6)
            : endX - 6;
        b.Draw(Game1.staminaRect, new Rectangle(npcX, lineY - 3, 8, 8), statusColor);
    }

    private void DrawTargetPreview(SpriteBatch b, SquadMemberState member, Rectangle bounds)
    {
        this.DrawMenuCard(b, bounds, RowColor, AccentBlue);
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
        int lineHeight = GetScaledLineHeight(Game1.tinyFont, PanelTextScale);
        DrawPanelText(
            b,
            FitText(text, Game1.tinyFont, Math.Max(1, bounds.Width - 30), PanelTextScale),
            Game1.tinyFont,
            new Vector2(bounds.X + 17, bounds.Y + Math.Max(4, (bounds.Height - lineHeight) / 2)),
            MutedTextColor,
            PanelTextScale,
            shadow: true);
    }

    private void DrawDirectiveButton(SpriteBatch b, Rectangle bounds, string label, bool active, CompanionDirective directive)
    {
        Point mouse = new(Game1.getMouseX(), Game1.getMouseY());
        Color fill = active ? ButtonActive : bounds.Contains(mouse) ? RowHoverColor : ButtonIdle;
        if (active && bounds.Contains(mouse))
            fill = Color.Lerp(fill, Color.White, 0.14f);
        if (bounds.Height < 30)
            this.DrawFlatPanel(b, bounds, fill, active ? AccentGreen : SurfaceBorder, 1);
        else
            this.DrawTexturedPanel(b, bounds, fill);
        Rectangle indicator = new(bounds.X + 7, bounds.Center.Y - 6, 5, 12);
        b.Draw(Game1.staminaRect, indicator, active ? Color.White : Color.Black * 0.22f);
        float labelScale = GetTextScaleForBox(
            label,
            Game1.tinyFont,
            PanelTextScale,
            Math.Max(1, bounds.Width - 24),
            Math.Max(1, bounds.Height - 6),
            minimumScale: PanelMetaTextScale);
        string text = FitText(label, Game1.tinyFont, bounds.Width - 24, labelScale);
        Vector2 textSize = MeasureScaledText(text, Game1.tinyFont, labelScale);
        DrawPanelText(
            b,
            text,
            Game1.tinyFont,
            new Vector2(bounds.X + 17, bounds.Y + Math.Max(3, (bounds.Height - textSize.Y) / 2)),
            active ? Color.White : TextColor,
            labelScale,
            shadow: true);
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
                string resources = FitText(costAndPoints, Game1.tinyFont, inner.Right - resourcesX, PanelTextScale);
                Vector2 resourcesSize = MeasureScaledText(resources, Game1.tinyFont, PanelTextScale);
                DrawPanelText(
                    b,
                    resources,
                    Game1.tinyFont,
                    new Vector2(resourcesX, stateBadge.Center.Y - resourcesSize.Y / 2f),
                    MutedTextColor,
                    PanelTextScale);
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
