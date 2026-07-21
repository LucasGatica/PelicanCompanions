#if PELICAN_TESTS
using PelicanCompanions;

namespace PelicanCompanions.Tests;

internal static class Program
{
    private static readonly TestCase[] Cases =
    {
        new("ModConfig.Validate restaura atalhos nulos", ModConfigValidateRestoresNullKeybinds),
        new("ModConfig.Validate limita valores abaixo do minimo", ModConfigValidateClampsMinimumValues),
        new("ModConfig.Validate limita valores acima do maximo", ModConfigValidateClampsMaximumValues),
        new("ModConfig.Validate normaliza enums invalidos", ModConfigValidateNormalizesInvalidEnums),
        new("CompanionProgression expoe thresholds esperados", CompanionProgressionHasExpectedThresholds),
        new("CompanionProgression respeita fronteiras de nivel", CompanionProgressionRespectsLevelBoundaries),
        new("CompanionProgression limita consultas de XP por nivel", CompanionProgressionClampsLevelQueries),
        new("CompanionProgression reembolsa skills legadas uma unica vez", CompanionProgressionRefundsLegacySkillsOnce),
        new("Catalogo de skills forma tres trilhas validas", CompanionSkillCatalogFormsThreeValidTracks),
        new("CompanionSkillTreePolicy diferencia todos os estados", CompanionSkillTreePolicyClassifiesEveryState),
        new("CompanionSkillTreePolicy respeita caixa e limite de pontos", CompanionSkillTreePolicyHonorsCasingAndPointBoundary),
        new("IGenericModConfigMenuApiCompat e publica", GenericModConfigMenuCompatibilityApiIsPublic),
        new("CompanionActionWheelHitTest mapeia setores variaveis e limites", CompanionActionWheelHitTestMapsSegmentsAndBounds),
        new("CompanionActionWheelPagination pagina doze companions sem perdas", CompanionActionWheelPaginationMapsTwelveCompanions),
        new("CompanionActionWheelNavigation navega e ignora slots vazios", CompanionActionWheelNavigationMovesFocusAndSkipsEmptySlots),
        new("CompanionActionWheelTextLayout preserva e equilibra rotulos", CompanionActionWheelTextLayoutPreservesAndBalancesLabels),
        new("CompanionWorkAreaPolicy usa geometria circular com borda inclusiva", CompanionWorkAreaPolicyUsesCircularInclusiveGeometry),
        new("CompanionWorkAreaPolicy aplica padding sem encolher a area", CompanionWorkAreaPolicyAppliesNonNegativePadding),
        new("CompanionWorkAreaPolicy limita raios entre tres e vinte", CompanionWorkAreaPolicyNormalizesRadiusBounds),
        new("CompanionWorkAreaPolicy restringe tarefas por especialidade", CompanionWorkAreaPolicyRestrictsTasksBySpecialty),
        new("CompanionWorkAreaPolicy valida estado persistido ativo", CompanionWorkAreaPolicyValidatesPersistedState),
        new("RecruitmentContextPolicy permite recrutamento distante no mesmo mapa", RecruitmentContextPolicyAllowsAnyDistanceOnSameMap),
        new("CompanionDialoguePolicy mantem pets silenciosos", CompanionDialoguePolicyKeepsPetsSilent),
        new("CompanionDialogueSelectionPolicy evita repeticao e usa a menos recente", CompanionDialogueSelectionPolicyAvoidsRecentLines),
        new("CompanionDialogueScheduler compartilha cooldown por owner", CompanionDialogueSchedulerSharesOwnerCooldown),
        new("CompanionDialogueScheduler prioriza e deduplica pedidos", CompanionDialogueSchedulerPrioritizesAndDeduplicates),
        new("FollowNavigationPolicy reseta recall apenas quando necessario", FollowNavigationPolicyResetsRecallOnlyWhenNecessary),
        new("FollowNavigationPolicy posterga e limita probes", FollowNavigationPolicyDefersAndThrottlesConnectivityProbes),
        new("FollowNavigationPolicy preserva controller e orcamento", FollowNavigationPolicyPreservesControllerAndBudget),
        new("TaskNavigationPolicy reutiliza stand validado sem novo probe", TaskNavigationPolicyReusesValidatedStand),
        new("TaskNavigationPolicy limita criacao e reinicio de rotas", TaskNavigationPolicyBudgetsPathStarts),
        new("TaskPlanningPolicy prioriza e percorre membros sem starvation", TaskPlanningPolicyPrioritizesAndRotatesFairly),
        new("GroundCommandPolicy abre contexto local seguro sem raio de follow", GroundCommandPolicyOpensSafeLocalContext),
        new("GroundCommandPolicy lista membros locais fora da formacao", GroundCommandPolicyListsLocalMembers),
        new("CommandReplayGuard rejeita replay por jogador", CommandReplayGuardRejectsReplayPerPlayer),
        new("CommandReplayGuard isola jogadores", CommandReplayGuardIsolatesPlayers),
        new("CommandReplayGuard expulsa o comando mais antigo por capacidade", CommandReplayGuardEvictsOldestAtCapacity),
        new("CommandReplayGuard.Clear esquece comandos registrados", CommandReplayGuardClearForgetsCommands),
        new("SavedItemStackIdentity e estavel para a ordem de ModData", SavedItemStackIdentityIsStableAcrossModDataOrder),
        new("SavedItemStackIdentity distingue stack", SavedItemStackIdentityDistinguishesStack),
        new("SavedItemStackIdentity distingue quality", SavedItemStackIdentityDistinguishesQuality),
        new("SavedItemStackIdentity distingue ModData", SavedItemStackIdentityDistinguishesModData)
    };

    public static int Main()
    {
        int passed = 0;
        List<string> failures = new();

        Console.WriteLine($"PelicanCompanions.Tests: executando {Cases.Length} testes\n");
        foreach (TestCase test in Cases)
        {
            try
            {
                test.Body();
                passed++;
                Console.WriteLine($"[PASS] {test.Name}");
            }
            catch (Exception ex)
            {
                failures.Add($"{test.Name}: {ex.Message}");
                Console.WriteLine($"[FAIL] {test.Name}");
                Console.WriteLine($"       {ex.Message}");
            }
        }

        Console.WriteLine($"\nResultado: {passed}/{Cases.Length} passaram; {failures.Count} falharam.");
        if (failures.Count == 0)
            return 0;

        Console.WriteLine("\nFalhas:");
        foreach (string failure in failures)
            Console.WriteLine($" - {failure}");

        return 1;
    }

    private static void ModConfigValidateRestoresNullKeybinds()
    {
        ModConfig config = new()
        {
            QuickActionWheelKey = null!,
            ControllerQuickActionWheelKey = null!,
            RecruitKey = null!,
            ManualTaskKey = null!,
            OpenSquadInventoryKey = null!,
            TasksToggleKey = null!,
            OpenCompanionPanelKey = null!,
            RecallAllCompanionsKey = null!
        };

        config.Validate();

        Assert.NotNull(config.QuickActionWheelKey, nameof(config.QuickActionWheelKey));
        Assert.NotNull(config.ControllerQuickActionWheelKey, nameof(config.ControllerQuickActionWheelKey));
        Assert.NotNull(config.RecruitKey, nameof(config.RecruitKey));
        Assert.NotNull(config.ManualTaskKey, nameof(config.ManualTaskKey));
        Assert.NotNull(config.OpenSquadInventoryKey, nameof(config.OpenSquadInventoryKey));
        Assert.NotNull(config.TasksToggleKey, nameof(config.TasksToggleKey));
        Assert.NotNull(config.OpenCompanionPanelKey, nameof(config.OpenCompanionPanelKey));
        Assert.NotNull(config.RecallAllCompanionsKey, nameof(config.RecallAllCompanionsKey));
    }

    private static void ModConfigValidateClampsMinimumValues()
    {
        ModConfig config = new()
        {
            FriendshipRequirement = -1,
            FriendshipPointsPerHour = -1,
            MaxSquadSize = 0,
            CompanionInventorySlots = 0,
            CompanionWorkRadius = 0,
            CompanionWorkReturnDistance = 0,
            CompanionQuickHudMaxRows = 0,
            DialogueCooldownSeconds = -1,
            CommunicationGroupCooldownSeconds = -1,
            ProtectBeehouseFlowers = -1,
            ParkTimeoutMinutes = -1
        };

        config.Validate();

        Assert.Equal(0, config.FriendshipRequirement, nameof(config.FriendshipRequirement));
        Assert.Equal(0, config.FriendshipPointsPerHour, nameof(config.FriendshipPointsPerHour));
        Assert.Equal(1, config.MaxSquadSize, nameof(config.MaxSquadSize));
        Assert.Equal(1, config.CompanionInventorySlots, nameof(config.CompanionInventorySlots));
        Assert.Equal(3, config.CompanionWorkRadius, nameof(config.CompanionWorkRadius));
        Assert.Equal(3, config.CompanionWorkReturnDistance, nameof(config.CompanionWorkReturnDistance));
        Assert.Equal(1, config.CompanionQuickHudMaxRows, nameof(config.CompanionQuickHudMaxRows));
        Assert.Equal(0, config.DialogueCooldownSeconds, nameof(config.DialogueCooldownSeconds));
        Assert.Equal(1, config.CommunicationGroupCooldownSeconds, nameof(config.CommunicationGroupCooldownSeconds));
        Assert.Equal(0, config.ProtectBeehouseFlowers, nameof(config.ProtectBeehouseFlowers));
        Assert.Equal(0, config.ParkTimeoutMinutes, nameof(config.ParkTimeoutMinutes));
    }

    private static void ModConfigValidateClampsMaximumValues()
    {
        ModConfig config = new()
        {
            FriendshipRequirement = 99,
            MaxSquadSize = 99,
            CompanionInventorySlots = 99,
            CompanionWorkRadius = 99,
            CompanionWorkReturnDistance = 99,
            CompanionQuickHudMaxRows = 99,
            CommunicationGroupCooldownSeconds = 99
        };

        config.Validate();

        Assert.Equal(14, config.FriendshipRequirement, nameof(config.FriendshipRequirement));
        Assert.Equal(12, config.MaxSquadSize, nameof(config.MaxSquadSize));
        Assert.Equal(10, config.CompanionInventorySlots, nameof(config.CompanionInventorySlots));
        Assert.Equal(20, config.CompanionWorkRadius, nameof(config.CompanionWorkRadius));
        Assert.Equal(40, config.CompanionWorkReturnDistance, nameof(config.CompanionWorkReturnDistance));
        Assert.Equal(12, config.CompanionQuickHudMaxRows, nameof(config.CompanionQuickHudMaxRows));
        Assert.Equal(30, config.CommunicationGroupCooldownSeconds, nameof(config.CommunicationGroupCooldownSeconds));

        config.CompanionWorkRadius = 18;
        config.CompanionWorkReturnDistance = 7;
        config.Validate();
        Assert.Equal(18, config.CompanionWorkReturnDistance, "return distance acompanha work radius");
    }

    private static void ModConfigValidateNormalizesInvalidEnums()
    {
        ModConfig config = new()
        {
            CompanionQuickHudMode = (CompanionQuickHudMode)999,
            CompanionQuickHudSide = (CompanionQuickHudSide)999,
            CompanionFormationMode = (CompanionFormationMode)999,
            DisableInteraction = (DisableInteractionMode)999,
            DisableTrashRummagingReaction = (TrashReactionMode)999,
            FishingMode = (FishingTaskMode)999,
            AttackingMode = (TaskMode)999,
            HarvestingMode = (TaskMode)999,
            ForagingMode = (TaskMode)999,
            LumberingMode = (TaskMode)999,
            MiningMode = (TaskMode)999,
            WateringMode = (TaskMode)999,
            PettingMode = (TaskMode)999,
            ShearingMode = (TaskMode)999,
            MilkingMode = (TaskMode)999
        };

        config.Validate();

        Assert.Equal(CompanionQuickHudMode.Detailed, config.CompanionQuickHudMode, nameof(config.CompanionQuickHudMode));
        Assert.Equal(CompanionQuickHudSide.Left, config.CompanionQuickHudSide, nameof(config.CompanionQuickHudSide));
        Assert.Equal(CompanionFormationMode.Adaptive, config.CompanionFormationMode, nameof(config.CompanionFormationMode));
        Assert.Equal(DisableInteractionMode.Never, config.DisableInteraction, nameof(config.DisableInteraction));
        Assert.Equal(TrashReactionMode.Never, config.DisableTrashRummagingReaction, nameof(config.DisableTrashRummagingReaction));
        Assert.Equal(FishingTaskMode.Disabled, config.FishingMode, nameof(config.FishingMode));
        Assert.Equal(TaskMode.Disabled, config.AttackingMode, nameof(config.AttackingMode));
        Assert.Equal(TaskMode.Mimicking, config.HarvestingMode, nameof(config.HarvestingMode));
        Assert.Equal(TaskMode.Disabled, config.ForagingMode, nameof(config.ForagingMode));
        Assert.Equal(TaskMode.Mimicking, config.LumberingMode, nameof(config.LumberingMode));
        Assert.Equal(TaskMode.Mimicking, config.MiningMode, nameof(config.MiningMode));
        Assert.Equal(TaskMode.Mimicking, config.WateringMode, nameof(config.WateringMode));
        Assert.Equal(TaskMode.Mimicking, config.PettingMode, nameof(config.PettingMode));
        Assert.Equal(TaskMode.Disabled, config.ShearingMode, nameof(config.ShearingMode));
        Assert.Equal(TaskMode.Disabled, config.MilkingMode, nameof(config.MilkingMode));
    }

    private static void CompanionProgressionHasExpectedThresholds()
    {
        int[] expected = { 0, 50, 125, 250, 450, 700, 1000, 1400, 1900, 2500 };
        Assert.SequenceEqual(expected, CompanionProgression.LevelXpThresholds, nameof(CompanionProgression.LevelXpThresholds));
        Assert.Equal(expected.Length, CompanionProgression.MaxLevel, nameof(CompanionProgression.MaxLevel));
    }

    private static void CompanionProgressionRespectsLevelBoundaries()
    {
        Assert.Equal(1, CompanionProgression.GetLevelForXp(-1), "XP negativo");

        for (int level = 1; level <= CompanionProgression.MaxLevel; level++)
        {
            int threshold = CompanionProgression.GetXpForLevel(level);
            Assert.Equal(level, CompanionProgression.GetLevelForXp(threshold), $"threshold do nivel {level}");

            if (level > 1)
                Assert.Equal(level - 1, CompanionProgression.GetLevelForXp(threshold - 1), $"XP anterior ao nivel {level}");
        }

        Assert.Equal(CompanionProgression.MaxLevel, CompanionProgression.GetLevelForXp(int.MaxValue), "XP muito alto");
    }

    private static void CompanionProgressionClampsLevelQueries()
    {
        Assert.Equal(0, CompanionProgression.GetXpForLevel(int.MinValue), "nivel abaixo do minimo");
        Assert.Equal(2500, CompanionProgression.GetXpForLevel(int.MaxValue), "nivel acima do maximo");
        Assert.Equal(50, CompanionProgression.GetNextLevelXp(1), "proximo nivel a partir do nivel 1");
        Assert.Equal(2500, CompanionProgression.GetNextLevelXp(CompanionProgression.MaxLevel), "proximo nivel no maximo");
        Assert.Equal(2500, CompanionProgression.GetNextLevelXp(int.MaxValue), "proximo nivel acima do maximo");
    }

    private static void CompanionProgressionRefundsLegacySkillsOnce()
    {
        Assert.Equal(0, CompanionProgression.GetLegacySkillPointRefund(null), "lista nula");
        Assert.Equal(0, CompanionProgression.GetLegacySkillPointRefund(Array.Empty<string>()), "lista vazia");
        Assert.Equal(
            2,
            CompanionProgression.GetLegacySkillPointRefund(new[]
            {
                "SKILL-COMBAT-001",
                "skill-combat-001",
                "SKILL-COMBAT-002",
                "SKILL-LUMBER-001",
                "",
                " "
            }),
            "skills legadas distintas, ignorando caixa e desconhecidas");
    }

    private static void CompanionSkillCatalogFormsThreeValidTracks()
    {
        Assert.Equal(9, CompanionProgression.Skills.Length, "quantidade de skills ativas");
        Assert.Equal(
            9,
            CompanionProgression.Skills.Select(skill => skill.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "IDs de skill devem ser unicos");
        Assert.SequenceEqual(
            new[] { "Lumbering", "Mining", "Utility" },
            CompanionProgression.Skills.Select(skill => skill.Branch).Distinct(),
            "ordem dos ramos ativos");
        Assert.False(
            CompanionProgression.Skills.Any(skill => skill.Branch == "Combat"),
            "Combat inerte nao pode reaparecer na arvore ativa");

        foreach (IGrouping<string, CompanionSkillDefinition> branch in CompanionProgression.Skills.GroupBy(skill => skill.Branch))
        {
            CompanionSkillDefinition[] skills = branch.ToArray();
            Assert.Equal(3, skills.Length, $"quantidade de tiers em {branch.Key}");
            Assert.SequenceEqual(new[] { 1, 1, 2 }, skills.Select(skill => skill.Cost), $"custos de {branch.Key}");
            Assert.Equal<string?>(null, skills[0].PrerequisiteId, $"raiz de {branch.Key}");
            for (int index = 1; index < skills.Length; index++)
                Assert.Equal(skills[index - 1].Id, skills[index].PrerequisiteId, $"pre-requisito do tier {index + 1} em {branch.Key}");
        }

        Assert.Equal(12, CompanionProgression.Skills.Sum(skill => skill.Cost), "custo total da arvore");
    }

    private static void CompanionSkillTreePolicyClassifiesEveryState()
    {
        CompanionSkillDefinition root = CompanionProgression.Skills[0];
        CompanionSkillDefinition child = CompanionProgression.Skills[1];

        Assert.Equal(
            CompanionSkillTreeState.ProgressionDisabled,
            CompanionSkillTreePolicy.GetState(root, Array.Empty<string>(), 10, progressionEnabled: false),
            "raiz nao aprendida com progressao desligada");
        Assert.Equal(
            CompanionSkillTreeState.ProgressionDisabled,
            CompanionSkillTreePolicy.GetState(child, Array.Empty<string>(), 0, progressionEnabled: false),
            "progressao desligada deve preceder pre-requisito e pontos ausentes");
        Assert.Equal(
            CompanionSkillTreeState.Learned,
            CompanionSkillTreePolicy.GetState(root, new[] { root.Id }, 0, progressionEnabled: false),
            "skill aprendida continua visivel com progressao desligada");
        Assert.Equal(
            CompanionSkillTreeState.LockedByPrerequisite,
            CompanionSkillTreePolicy.GetState(child, Array.Empty<string>(), 0, progressionEnabled: true),
            "pre-requisito ausente deve preceder falta de pontos");
        Assert.Equal(
            CompanionSkillTreeState.NeedsPoints,
            CompanionSkillTreePolicy.GetState(root, null, 0, progressionEnabled: true),
            "raiz sem pontos");
        Assert.Equal(
            CompanionSkillTreeState.Available,
            CompanionSkillTreePolicy.GetState(root, Array.Empty<string>(), root.Cost, progressionEnabled: true),
            "raiz aprendivel");
    }

    private static void CompanionSkillTreePolicyHonorsCasingAndPointBoundary()
    {
        CompanionSkillDefinition second = CompanionProgression.Skills[1];
        CompanionSkillDefinition third = CompanionProgression.Skills[2];
        string unlockedRoot = second.PrerequisiteId!.ToLowerInvariant();

        Assert.Equal(
            CompanionSkillTreeState.Available,
            CompanionSkillTreePolicy.GetState(second, new[] { unlockedRoot }, second.Cost, progressionEnabled: true),
            "pre-requisito deve ignorar caixa e aceitar pontos exatos");
        Assert.Equal(
            CompanionSkillTreeState.Learned,
            CompanionSkillTreePolicy.GetState(second, new[] { second.Id.ToLowerInvariant() }, 0, progressionEnabled: true),
            "skill aprendida deve ignorar caixa");
        Assert.Equal(
            CompanionSkillTreeState.NeedsPoints,
            CompanionSkillTreePolicy.GetState(third, new[] { third.PrerequisiteId!.ToLowerInvariant() }, third.Cost - 1, progressionEnabled: true),
            "tier final abaixo do custo");
        Assert.Equal(
            CompanionSkillTreeState.Available,
            CompanionSkillTreePolicy.GetState(third, new[] { third.PrerequisiteId!.ToLowerInvariant() }, third.Cost, progressionEnabled: true),
            "tier final no custo exato");
    }

    private static void GenericModConfigMenuCompatibilityApiIsPublic()
    {
        Assert.True(typeof(IGenericModConfigMenuApiCompat).IsPublic, "A interface precisa ser public para o proxy de API do SMAPI.");
    }

    private static void CompanionActionWheelHitTestMapsSegmentsAndBounds()
    {
        const float inner = 43f;
        const float outer = 132f;
        const float firstCenter = -MathF.PI / 2f;

        Assert.Equal<int?>(0, CompanionActionWheelHitTest.GetSegment(0f, -80f, inner, outer, 4, firstCenter), "setor superior");
        Assert.Equal<int?>(1, CompanionActionWheelHitTest.GetSegment(80f, 0f, inner, outer, 4, firstCenter), "setor direito");
        Assert.Equal<int?>(2, CompanionActionWheelHitTest.GetSegment(0f, 80f, inner, outer, 4, firstCenter), "setor inferior");
        Assert.Equal<int?>(3, CompanionActionWheelHitTest.GetSegment(-80f, 0f, inner, outer, 4, firstCenter), "setor esquerdo");
        Assert.Equal<int?>(null, CompanionActionWheelHitTest.GetSegment(60f, -60f, inner, outer, 4, firstCenter, 0.035f), "separador diagonal");

        for (int index = 0; index < 5; index++)
        {
            float angle = firstCenter + index * MathF.PI * 2f / 5f;
            Assert.Equal<int?>(
                index,
                CompanionActionWheelHitTest.GetSegment(MathF.Cos(angle) * 80f, MathF.Sin(angle) * 80f, inner, outer, 5, firstCenter, 0.035f),
                $"centro do setor {index} em roda de cinco opcoes");
        }

        Assert.Equal<int?>(0, CompanionActionWheelHitTest.GetSegment(-80f, 0f, inner, outer, 1, firstCenter), "roda de uma opcao");
        Assert.Equal<int?>(null, CompanionActionWheelHitTest.GetSegment(0f, 0f, inner, outer, 4, firstCenter), "dead zone");
        Assert.Equal<int?>(null, CompanionActionWheelHitTest.GetSegment(inner, 0f, inner, outer, 4, firstCenter), "borda interna");
        Assert.Equal<int?>(1, CompanionActionWheelHitTest.GetSegment(outer, 0f, inner, outer, 4, firstCenter), "borda externa inclusiva");
        Assert.Equal<int?>(null, CompanionActionWheelHitTest.GetSegment(outer + 0.1f, 0f, inner, outer, 4, firstCenter), "fora do circulo");
        Assert.Equal<int?>(null, CompanionActionWheelHitTest.GetSegment(float.NaN, 0f, inner, outer, 4, firstCenter), "coordenada invalida");
        Assert.Equal<int?>(null, CompanionActionWheelHitTest.GetSegment(80f, 0f, inner, outer, 0, firstCenter), "quantidade de setores invalida");
    }

    private static void CompanionActionWheelPaginationMapsTwelveCompanions()
    {
        CompanionActionWheelPageLayout unpaged = CompanionActionWheelPagination.Create(
            optionCount: 4,
            pinnedOptionCount: 1,
            requestedPageIndex: 8);
        Assert.Equal(1, unpaged.PageCount, "tres companions nao precisam de paginacao");
        Assert.Equal(4, unpaged.Slots.Length, "layout legado deve continuar compacto");

        List<int> companionOptionIndexes = new();
        for (int pageIndex = 0; pageIndex < 4; pageIndex++)
        {
            CompanionActionWheelPageLayout page = CompanionActionWheelPagination.Create(
                optionCount: 13,
                pinnedOptionCount: 1,
                requestedPageIndex: pageIndex);
            Assert.Equal(4, page.PageCount, "doze companions devem formar quatro paginas");
            Assert.Equal(6, page.Slots.Length, "pagina deve preservar seis setores geometricos");
            Assert.Equal(
                new CompanionActionWheelSlot(CompanionActionWheelSlotKind.Option, 0),
                page.Slots[0],
                "acao global deve permanecer fixa");
            Assert.Equal(CompanionActionWheelSlotKind.NextPage, page.Slots[1].Kind, "proxima pagina no setor superior direito");
            Assert.Equal(CompanionActionWheelSlotKind.PreviousPage, page.Slots[5].Kind, "pagina anterior no setor superior esquerdo");

            companionOptionIndexes.AddRange(
                page.Slots
                    .Where(slot => slot.Kind == CompanionActionWheelSlotKind.Option && slot.OptionIndex > 0)
                    .Select(slot => slot.OptionIndex));
        }

        Assert.SequenceEqual(
            Enumerable.Range(1, 12),
            companionOptionIndexes,
            "cada companion deve aparecer exatamente uma vez");

        CompanionActionWheelPageLayout wrapped = CompanionActionWheelPagination.Create(13, 1, -1);
        Assert.Equal(3, wrapped.PageIndex, "pagina anterior a primeira deve voltar para a ultima");
        Assert.SequenceEqual(
            new[] { 10, 11, 12 },
            wrapped.Slots.Where(slot => slot.Kind == CompanionActionWheelSlotKind.Option && slot.OptionIndex > 0)
                .Select(slot => slot.OptionIndex),
            "ultima pagina deve preservar os tres companions finais");

        List<int> groundPagedOptionIndexes = new();
        for (int pageIndex = 0; pageIndex < 6; pageIndex++)
        {
            CompanionActionWheelPageLayout groundPage = CompanionActionWheelPagination.Create(
                optionCount: 14,
                pinnedOptionCount: 2,
                requestedPageIndex: pageIndex);
            Assert.Equal(6, groundPage.PageCount, "duas acoes globais e doze companions devem formar seis paginas");
            Assert.Equal(
                new CompanionActionWheelSlot(CompanionActionWheelSlotKind.Option, 0),
                groundPage.Slots[0],
                "primeira acao global deve permanecer fixa");
            Assert.Equal(
                new CompanionActionWheelSlot(CompanionActionWheelSlotKind.Option, 1),
                groundPage.Slots[1],
                "segunda acao global deve permanecer fixa");
            groundPagedOptionIndexes.AddRange(
                groundPage.Slots
                    .Where(slot => slot.Kind == CompanionActionWheelSlotKind.Option && slot.OptionIndex >= 2)
                    .Select(slot => slot.OptionIndex));
        }

        Assert.SequenceEqual(
            Enumerable.Range(2, 12),
            groundPagedOptionIndexes,
            "as duas acoes globais nao podem ocultar nenhum dos doze companions");
    }

    private static void CompanionActionWheelNavigationMovesFocusAndSkipsEmptySlots()
    {
        const float firstCenter = -MathF.PI / 2f;
        CompanionActionWheelPageLayout full = CompanionActionWheelPagination.Create(13, 1, 0);

        Assert.Equal<int?>(0, CompanionActionWheelNavigation.MoveFocus(full.Slots, null, 0f, -1f, firstCenter), "cima escolhe setor superior");
        Assert.Equal<int?>(1, CompanionActionWheelNavigation.MoveFocus(full.Slots, 0, 1f, 0f, firstCenter), "direita alcanca proxima pagina");
        Assert.Equal<int?>(2, CompanionActionWheelNavigation.MoveFocus(full.Slots, 1, 0f, 1f, firstCenter), "baixo alcanca primeiro companion");
        Assert.Equal<int?>(3, CompanionActionWheelNavigation.MoveFocus(full.Slots, 2, 0f, 1f, firstCenter), "baixo continua espacialmente");
        Assert.Equal<int?>(2, CompanionActionWheelNavigation.MoveFocus(full.Slots, 2, 0f, 0f, firstCenter), "vetor neutro preserva foco");

        CompanionActionWheelPageLayout partial = CompanionActionWheelPagination.Create(
            optionCount: 11,
            pinnedOptionCount: 1,
            requestedPageIndex: 3);
        Assert.Equal(CompanionActionWheelSlotKind.Option, partial.Slots[2].Kind, "ultimo companion permanece selecionavel");
        Assert.Equal(CompanionActionWheelSlotKind.Empty, partial.Slots[3].Kind, "primeiro excedente deve ser vazio");
        Assert.Equal(CompanionActionWheelSlotKind.Empty, partial.Slots[4].Kind, "segundo excedente deve ser vazio");
        Assert.Equal<int?>(2, CompanionActionWheelNavigation.MoveFocus(partial.Slots, null, 0f, 1f, firstCenter), "foco inicial deve ignorar placeholders");
    }

    private static void CompanionActionWheelTextLayoutPreservesAndBalancesLabels()
    {
        static float Measure(string text)
        {
            return text.Split('\n').Max(line => line.Length * 16f);
        }

        static string Compact(string text)
        {
            return new string(text.Where(character => !char.IsWhiteSpace(character)).ToArray());
        }

        foreach (string label in new[] { "Perfil", "Trabalhar", "Parar", "Dispensar", "Seguir" })
        {
            CompanionActionWheelTextFit fit = CompanionActionWheelTextLayout.Fit(
                label,
                maxWidth: 92f,
                allowWrap: true,
                maxLines: 2,
                measureWidth: Measure);
            Assert.Equal(label, Compact(fit.Text), $"rotulo {label} deve permanecer inteiro");
            Assert.False(fit.Text.Contains('…'), $"rotulo {label} nao deve ser truncado");
            Assert.True(
                fit.Text.Split('\n').All(line => Measure(line) <= 92f),
                $"rotulo {label} deve caber sem mudar o tamanho da fonte");
        }

        CompanionActionWheelTextFit namedAction = CompanionActionWheelTextLayout.Fit(
            "Mandar Abigail",
            maxWidth: 108f,
            allowWrap: true,
            maxLines: 3,
            measureWidth: Measure);
        Assert.Equal(
            "MandarAbigail",
            Compact(namedAction.Text),
            "acao e nome devem permanecer completos");
        Assert.True(
            namedAction.Text.Split('\n').All(line => Measure(line) <= 108f),
            "acao nomeada deve caber sem mudar o tamanho da fonte");

        CompanionActionWheelTextFit localizedAction = CompanionActionWheelTextLayout.Fit(
            "Dispensar todos",
            maxWidth: 108f,
            allowWrap: true,
            maxLines: 3,
            measureWidth: Measure);
        Assert.Equal(
            "Dispensartodos",
            Compact(localizedAction.Text),
            "frase localizada deve permanecer completa");
        Assert.True(
            localizedAction.Text.Split('\n').All(line => Measure(line) <= 108f),
            "frase localizada deve caber sem mudar o tamanho da fonte");
    }

    private static void CompanionWorkAreaPolicyUsesCircularInclusiveGeometry()
    {
        const int centerX = 10;
        const int centerY = 20;

        Assert.True(CompanionWorkAreaPolicy.Contains(centerX, centerY, 3, centerX, centerY), "centro");
        Assert.True(CompanionWorkAreaPolicy.Contains(centerX, centerY, 3, centerX + 3, centerY), "borda cardinal inclusiva");
        Assert.True(CompanionWorkAreaPolicy.Contains(centerX, centerY, 3, centerX + 2, centerY + 2), "diagonal interna");
        Assert.False(CompanionWorkAreaPolicy.Contains(centerX, centerY, 3, centerX + 3, centerY + 1), "fora do circulo mas dentro do quadrado");
        Assert.False(CompanionWorkAreaPolicy.Contains(centerX, centerY, 3, centerX - 4, centerY), "fora da borda negativa");
    }

    private static void CompanionWorkAreaPolicyAppliesNonNegativePadding()
    {
        const int centerX = 7;
        const int centerY = 11;

        Assert.False(CompanionWorkAreaPolicy.Contains(centerX, centerY, 3, centerX + 4, centerY), "sem padding");
        Assert.True(CompanionWorkAreaPolicy.Contains(centerX, centerY, 3, centerX + 4, centerY, padding: 1), "padding cardinal");
        Assert.False(CompanionWorkAreaPolicy.Contains(centerX, centerY, 3, centerX + 4, centerY, padding: -10), "padding negativo deve valer zero");
        Assert.True(CompanionWorkAreaPolicy.Contains(centerX, centerY, 3, centerX + 3, centerY + 4, padding: 2), "borda 3-4-5 com padding");
        Assert.False(CompanionWorkAreaPolicy.Contains(centerX, centerY, 3, centerX + 4, centerY + 4, padding: 2), "padding tambem permanece circular");
    }

    private static void CompanionWorkAreaPolicyNormalizesRadiusBounds()
    {
        Assert.Equal(3, CompanionWorkAreaPolicy.NormalizeRadius(int.MinValue), "raio abaixo do minimo");
        Assert.Equal(3, CompanionWorkAreaPolicy.NormalizeRadius(3), "raio minimo");
        Assert.Equal(20, CompanionWorkAreaPolicy.NormalizeRadius(20), "raio maximo");
        Assert.Equal(20, CompanionWorkAreaPolicy.NormalizeRadius(int.MaxValue), "raio acima do maximo");

        Assert.True(CompanionWorkAreaPolicy.Contains(0, 0, 0, 3, 0), "Contains deve normalizar para tres");
        Assert.False(CompanionWorkAreaPolicy.Contains(0, 0, 0, 4, 0), "limite normalizado inferior");
        Assert.True(CompanionWorkAreaPolicy.Contains(0, 0, 99, 20, 0), "Contains deve normalizar para vinte");
        Assert.False(CompanionWorkAreaPolicy.Contains(0, 0, 99, 21, 0), "limite normalizado superior");
    }

    private static void CompanionWorkAreaPolicyRestrictsTasksBySpecialty()
    {
        Assert.True(CompanionWorkAreaPolicy.Allows(CompanionWorkSpecialty.Wood, CompanionTaskKind.Lumbering), "madeira permite corte");
        Assert.False(CompanionWorkAreaPolicy.Allows(CompanionWorkSpecialty.Wood, CompanionTaskKind.Mining), "madeira rejeita mineracao");
        Assert.True(CompanionWorkAreaPolicy.Allows(CompanionWorkSpecialty.Mining, CompanionTaskKind.Mining), "mineracao permite pedras");
        Assert.False(CompanionWorkAreaPolicy.Allows(CompanionWorkSpecialty.Mining, CompanionTaskKind.Lumbering), "mineracao rejeita corte");
        Assert.True(CompanionWorkAreaPolicy.Allows(CompanionWorkSpecialty.ClearArea, CompanionTaskKind.Lumbering), "limpar permite corte");
        Assert.True(CompanionWorkAreaPolicy.Allows(CompanionWorkSpecialty.ClearArea, CompanionTaskKind.Mining), "limpar permite mineracao");
        Assert.False(CompanionWorkAreaPolicy.Allows(CompanionWorkSpecialty.ClearArea, CompanionTaskKind.Watering), "area fixa nao inclui rega");
        Assert.False(CompanionWorkAreaPolicy.Allows((CompanionWorkSpecialty)999, CompanionTaskKind.Lumbering), "especialidade desconhecida");
    }

    private static void CompanionWorkAreaPolicyValidatesPersistedState()
    {
        Assert.True(
            CompanionWorkAreaPolicy.IsPersistedStateValid(false, null, null, -1, -1, -1, (CompanionWorkSpecialty)999),
            "estado inativo nao exige payload");
        Assert.False(
            CompanionWorkAreaPolicy.IsActiveStateValid(false, null, null, -1, -1, -1, (CompanionWorkSpecialty)999),
            "estado inativo valido nao pode ser interpretado como area ativa");
        Assert.True(
            CompanionWorkAreaPolicy.IsActiveStateValid(true, "order-active", "Farm", 4, 5, 8, CompanionWorkSpecialty.ClearArea),
            "helper ativo exige flag e payload validos");
        Assert.True(
            CompanionWorkAreaPolicy.IsPersistedStateValid(true, "order-1", "Farm", 0, 0, 3, CompanionWorkSpecialty.Wood),
            "estado valido no limite inferior");
        Assert.True(
            CompanionWorkAreaPolicy.IsPersistedStateValid(true, "order-2", "Mine", 80, 120, 20, CompanionWorkSpecialty.Mining),
            "estado valido no limite superior");

        Assert.False(CompanionWorkAreaPolicy.IsPersistedStateValid(true, " ", "Farm", 1, 1, 8, CompanionWorkSpecialty.ClearArea), "order id vazio");
        Assert.False(CompanionWorkAreaPolicy.IsPersistedStateValid(true, "order", " ", 1, 1, 8, CompanionWorkSpecialty.ClearArea), "mapa vazio");
        Assert.False(CompanionWorkAreaPolicy.IsPersistedStateValid(true, "order", "Farm", -1, 1, 8, CompanionWorkSpecialty.ClearArea), "centro X negativo");
        Assert.False(CompanionWorkAreaPolicy.IsPersistedStateValid(true, "order", "Farm", 1, -1, 8, CompanionWorkSpecialty.ClearArea), "centro Y negativo");
        Assert.False(CompanionWorkAreaPolicy.IsPersistedStateValid(true, "order", "Farm", 1, 1, 2, CompanionWorkSpecialty.ClearArea), "raio abaixo de tres");
        Assert.False(CompanionWorkAreaPolicy.IsPersistedStateValid(true, "order", "Farm", 1, 1, 21, CompanionWorkSpecialty.ClearArea), "raio acima de vinte");
        Assert.False(CompanionWorkAreaPolicy.IsPersistedStateValid(true, "order", "Farm", 1, 1, 8, (CompanionWorkSpecialty)999), "especialidade desconhecida");
    }

    private static void RecruitmentContextPolicyAllowsAnyDistanceOnSameMap()
    {
        Assert.True(
            RecruitmentContextPolicy.IsLocationValid(ownerHasCurrentLocation: true, npcSharesCurrentLocation: true),
            "A elegibilidade de local nao deve receber nem limitar a distancia entre jogador e NPC.");
        Assert.False(
            RecruitmentContextPolicy.IsLocationValid(ownerHasCurrentLocation: false, npcSharesCurrentLocation: true),
            "O jogador precisa estar em um mapa carregado.");
        Assert.False(
            RecruitmentContextPolicy.IsLocationValid(ownerHasCurrentLocation: true, npcSharesCurrentLocation: false),
            "O NPC precisa estar no mesmo mapa do jogador.");
    }

    private static void CompanionDialoguePolicyKeepsPetsSilent()
    {
        Assert.False(CompanionDialoguePolicy.CanSpeak(isPet: true), "pet");
        Assert.True(CompanionDialoguePolicy.CanSpeak(isPet: false), "NPC comum");
        Assert.Equal(CompanionExpressionKind.PetExpression, CompanionDialoguePolicy.GetExpressionKind(isPet: true), "expressao de pet");
        Assert.Equal(CompanionExpressionKind.Speech, CompanionDialoguePolicy.GetExpressionKind(isPet: false), "fala de NPC");
    }

    private static void CompanionDialogueSelectionPolicyAvoidsRecentLines()
    {
        CompanionDialogueLine first = new() { Id = "first", TextKey = "dialogue.first" };
        CompanionDialogueLine second = new() { Id = "second", TextKey = "dialogue.second" };
        CompanionDialogueLine third = new() { Id = "third", TextKey = "dialogue.third" };
        CompanionDialogueLine? fresh = CompanionDialogueSelectionPolicy.Select(
            new[] { first, second, third },
            new[] { "first", "second" },
            weightedRoll: 0);
        Assert.Equal("third", fresh?.Id, "linha ainda nao usada");

        CompanionDialogueLine? oldest = CompanionDialogueSelectionPolicy.Select(
            new[] { first, second, third },
            new[] { "first", "second", "third" },
            weightedRoll: 0);
        Assert.Equal("third", oldest?.Id, "linha usada ha mais tempo");
    }

    private static void CompanionDialogueSchedulerSharesOwnerCooldown()
    {
        CompanionDialogueScheduler scheduler = new();
        CompanionDialogueContext context = new();
        scheduler.Enqueue(new CompanionDialogueRequest(1, "Abigail", "Idle", CompanionDialoguePriority.Ambient, context, false, 100, 600, "a"));
        scheduler.Enqueue(new CompanionDialogueRequest(1, "Sebastian", "Idle", CompanionDialoguePriority.Ambient, context, false, 100, 600, "b"));

        Assert.True(scheduler.TryDequeue(1, 100, 180, out CompanionDialogueRequest first), "primeira fala deve sair");
        scheduler.MarkPresented(1, first.NpcName, "line.a", 100, minimumGapTicks: 60);
        Assert.False(scheduler.CanPresent(1, 279, 180), "consulta de cooldown deve compartilhar o mesmo limite");
        Assert.False(scheduler.CanPresentIdentity(1, "line.a", 159, 60), "intervalo minimo deve bloquear somente a identidade recente");
        Assert.True(scheduler.CanPresentIdentity(1, "line.a", 160, 60), "intervalo da identidade deve vencer no limite");
        Assert.True(scheduler.CanPresentIdentity(1, "line.b", 101, 600), "outra identidade nao deve herdar o intervalo");
        Assert.False(scheduler.TryDequeue(1, 279, 180, out _), "segundo NPC deve respeitar cooldown do grupo");
        Assert.True(scheduler.TryDequeue(1, 280, 180, out CompanionDialogueRequest second), "cooldown deve vencer no limite");
        Assert.Equal("Sebastian", second.NpcName, "segundo speaker");

        CompanionDialogueScheduler boundedIntervals = new();
        for (int i = 0; i < CompanionDialogueScheduler.IdentityIntervalHistoryLimit; i++)
        {
            Assert.True(
                boundedIntervals.CanPresentIdentity(1, $"line.{i}", i, 10_000),
                "identidade nova deve caber enquanto houver slot protegido");
            boundedIntervals.MarkPresented(1, "Leah", $"line.{i}", i, minimumGapTicks: 10_000);
        }
        Assert.False(
            boundedIntervals.CanPresentIdentity(1, "overflow", CompanionDialogueScheduler.IdentityIntervalHistoryLimit, 10_000),
            "historico cheio nao deve expulsar um intervalo ainda ativo");
        Assert.False(
            boundedIntervals.CanPresentIdentity(1, "line.0", 9_999, 10_000),
            "identidade protegida deve respeitar todo o intervalo mesmo com o historico cheio");
        Assert.True(
            boundedIntervals.CanPresentIdentity(1, "overflow", 10_000, 10_000),
            "um slot deve ser liberado assim que o intervalo mais antigo vencer");
    }

    private static void CompanionDialogueSchedulerPrioritizesAndDeduplicates()
    {
        CompanionDialogueScheduler scheduler = new();
        CompanionDialogueContext context = new();
        scheduler.Enqueue(new CompanionDialogueRequest(1, "Leah", "Idle", CompanionDialoguePriority.Ambient, context, false, 10, 600, "same"));
        scheduler.Enqueue(new CompanionDialogueRequest(1, "Leah", "TaskFailure", CompanionDialoguePriority.Command, context, false, 11, 600, "same"));
        scheduler.Enqueue(new CompanionDialogueRequest(1, "Robin", "Lumbering", CompanionDialoguePriority.Task, context, false, 12, 600, "other"));

        Assert.True(scheduler.TryDequeue(1, 12, 180, out CompanionDialogueRequest selected), "pedido prioritario deve sair");
        Assert.Equal("TaskFailure", selected.Category, "dedupe deve substituir pedido inferior");
        scheduler.MarkPresented(1, selected.NpcName, "failure", 12);
        Assert.True(scheduler.TryDequeue(1, 192, 180, out CompanionDialogueRequest remaining), "outro pedido deve permanecer na fila");
        Assert.Equal("Lumbering", remaining.Category, "fila deduplicada deve manter somente o pedido distinto");
        Assert.False(scheduler.TryDequeue(1, 400, 180, out _), "nenhuma copia do pedido substituido deve restar");
    }

    private static void FollowNavigationPolicyResetsRecallOnlyWhenNecessary()
    {
        Assert.False(
            FollowNavigationPolicy.ShouldResetForRecall(true, 2f, 3f, wasStuckOrReturning: false),
            "Seguidor saudavel dentro do raio nao precisa descartar a rota antes de registrar o recall.");
        Assert.False(
            FollowNavigationPolicy.ShouldResetForRecall(true, 3f, 3f, wasStuckOrReturning: false),
            "O limite do raio comum deve ser inclusivo.");
        Assert.True(
            FollowNavigationPolicy.ShouldResetForRecall(true, 3.01f, 3f, wasStuckOrReturning: false),
            "Distancia acima do raio deve descartar a navegacao anterior.");
        Assert.True(
            FollowNavigationPolicy.ShouldResetForRecall(false, 0f, 3f, wasStuckOrReturning: false),
            "Outro mapa deve descartar a navegacao anterior.");
        Assert.True(
            FollowNavigationPolicy.ShouldResetForRecall(true, 1f, 3f, wasStuckOrReturning: true),
            "Estado de recuperacao deve reiniciar a navegacao mesmo perto do dono.");
    }

    private static void FollowNavigationPolicyDefersAndThrottlesConnectivityProbes()
    {
        Assert.False(
            FollowNavigationPolicy.ShouldProbeConnectivity(false, 18, 18, 1000, null, 60),
            "Sem movimento pendente nao deve haver probe.");
        Assert.False(
            FollowNavigationPolicy.ShouldProbeConnectivity(true, 17, 18, 1000, null, 60),
            "O probe deve esperar o limiar de falta de progresso.");
        Assert.True(
            FollowNavigationPolicy.ShouldProbeConnectivity(true, 18, 18, 1000, null, 60),
            "O primeiro probe pode ocorrer exatamente no limiar.");
        Assert.False(
            FollowNavigationPolicy.ShouldProbeConnectivity(true, 18, 18, 1000, 950, 60),
            "Probe recente deve respeitar cooldown.");
        Assert.True(
            FollowNavigationPolicy.ShouldProbeConnectivity(true, 18, 18, 1000, 940, 60),
            "O cooldown deve vencer no limite exato.");
        Assert.True(
            FollowNavigationPolicy.ShouldProbeConnectivity(
                true,
                18,
                18,
                int.MinValue + 5,
                int.MaxValue - 4,
                10),
            "O cooldown deve continuar correto quando o contador de ticks transbordar.");
    }

    private static void FollowNavigationPolicyPreservesControllerAndBudget()
    {
        Assert.False(
            FollowNavigationPolicy.ShouldStartPath(false, false, false, false, true, true),
            "Sem movimento pendente nao deve criar rota.");
        Assert.False(
            FollowNavigationPolicy.ShouldStartPath(true, true, false, false, true, true),
            "Nao deve criar rota para o tile atual.");
        Assert.False(
            FollowNavigationPolicy.ShouldStartPath(true, false, true, false, true, true),
            "Probe e criacao de rota nao devem compartilhar a mesma atualizacao.");
        Assert.False(
            FollowNavigationPolicy.ShouldStartPath(true, false, false, true, true, true),
            "Controller compativel deve ser preservado.");
        Assert.False(
            FollowNavigationPolicy.ShouldStartPath(true, false, false, false, false, true),
            "Cooldown pendente deve adiar a rota.");
        Assert.False(
            FollowNavigationPolicy.ShouldStartPath(true, false, false, false, true, false),
            "Orcamento esgotado deve adiar a rota.");
        Assert.True(
            FollowNavigationPolicy.ShouldStartPath(true, false, false, false, true, true),
            "Rota ausente com cooldown e orcamento disponiveis deve iniciar.");
    }

    private static void TaskNavigationPolicyReusesValidatedStand()
    {
        Assert.True(
            TaskNavigationPolicy.CanReuseStandTile(true, true, true, false, false, false, false),
            "Um stand estruturalmente valido e ainda reservado pelo proprio NPC deve ser reutilizado.");
        Assert.False(
            TaskNavigationPolicy.CanReuseStandTile(false, true, true, false, false, false, false),
            "Stand estruturalmente bloqueado precisa ser recalculado.");
        Assert.False(
            TaskNavigationPolicy.CanReuseStandTile(true, false, true, false, false, false, false),
            "Stand que deixou de ser adjacente ao alvo nao pode ser reutilizado.");
        Assert.False(
            TaskNavigationPolicy.CanReuseStandTile(true, true, false, false, false, false, false),
            "Stand fora do raio de trabalho nao pode ser reutilizado.");
        Assert.False(
            TaskNavigationPolicy.CanReuseStandTile(true, true, true, true, false, false, false),
            "Reserva de outro companion invalida o stand.");
        Assert.False(
            TaskNavigationPolicy.CanReuseStandTile(true, true, true, false, false, false, true),
            "Stand cuja rota ja foi rejeitada precisa dar lugar a outro quadrante.");
        Assert.True(
            TaskNavigationPolicy.CanReuseStandTile(true, true, true, false, false, true, true),
            "Controller esperado prova que a tentativa ainda esta em andamento.");
        Assert.True(
            TaskNavigationPolicy.CanReuseStandTile(true, true, true, false, true, false, true),
            "NPC que ja chegou pode trabalhar mesmo depois de uma tentativa anterior.");
    }

    private static void TaskNavigationPolicyBudgetsPathStarts()
    {
        Assert.True(
            TaskNavigationPolicy.ShouldStartPath(false, false, false, true, true),
            "Rota ausente deve iniciar com cooldown e orcamento disponiveis.");
        Assert.False(
            TaskNavigationPolicy.ShouldStartPath(true, false, false, true, true),
            "Nao deve criar rota para o tile atual.");
        Assert.False(
            TaskNavigationPolicy.ShouldStartPath(false, false, true, true, true),
            "Controller de tarefa compativel deve ser preservado.");
        Assert.False(
            TaskNavigationPolicy.ShouldStartPath(false, false, false, false, true),
            "Retry normal deve respeitar cooldown.");
        Assert.False(
            TaskNavigationPolicy.ShouldStartPath(false, true, true, false, false),
            "Nem recuperacao forcada pode ultrapassar o orcamento do frame.");
        Assert.True(
            TaskNavigationPolicy.ShouldStartPath(false, true, true, false, true),
            "Recuperacao com orcamento pode substituir controller travado sem esperar cooldown.");
    }

    private static void TaskPlanningPolicyPrioritizesAndRotatesFairly()
    {
        string[] names = Enumerable.Range(1, 12).Select(index => $"npc-{index:00}").ToArray();
        HashSet<string> priority = new(StringComparer.OrdinalIgnoreCase) { "npc-12" };
        IReadOnlyList<string> first = TaskPlanningPolicy.SelectMembers(
            names,
            priority,
            cursor: 0,
            budget: 3,
            out int cursor);
        Assert.Equal("npc-12", first[0], "Comando explicito deve ser o primeiro planejamento.");
        Assert.Equal(3, first.Count, "O budget deve limitar a primeira varredura.");

        HashSet<string> crowdedPriority = new(StringComparer.OrdinalIgnoreCase)
        {
            "npc-01",
            "npc-02",
            "npc-03"
        };
        IReadOnlyList<string> mixed = TaskPlanningPolicy.SelectMembers(
            new[] { "npc-01", "npc-02", "npc-03", "npc-04" },
            crowdedPriority,
            cursor: 0,
            budget: 3,
            out _);
        Assert.True(mixed.Contains("npc-04"), "Prioridades nao podem consumir todas as vagas e causar starvation.");

        HashSet<string> visited = new(first, StringComparer.OrdinalIgnoreCase);
        priority.Clear();
        for (int scan = 0; scan < 4; scan++)
        {
            IReadOnlyList<string> selected = TaskPlanningPolicy.SelectMembers(
                names,
                priority,
                cursor,
                budget: 3,
                out cursor);
            Assert.True(selected.Count <= 3, "Nenhuma varredura pode ultrapassar o budget.");
            foreach (string name in selected)
                visited.Add(name);
        }

        Assert.Equal(12, visited.Count, "Round-robin deve alcançar todos os 12 companions sem starvation.");
    }

    private static void GroundCommandPolicyOpensSafeLocalContext()
    {
        Assert.True(
            GroundCommandPolicy.CanOpen(true, true, true),
            "Chao seguro no mapa atual deve abrir independentemente do raio de formacao.");
        Assert.False(GroundCommandPolicy.CanOpen(false, true, true), "Sem companion proprio nao ha comando.");
        Assert.False(GroundCommandPolicy.CanOpen(true, false, true), "Outro mapa nao e um destino local.");
        Assert.False(GroundCommandPolicy.CanOpen(true, true, false), "Tile inseguro continua bloqueado.");
    }

    private static void GroundCommandPolicyListsLocalMembers()
    {
        Assert.True(
            GroundCommandPolicy.CanListMember(true, false, true),
            "Membro local seguindo, esperando ou trabalhando deve aparecer mesmo fora da formacao.");
        Assert.False(GroundCommandPolicy.CanListMember(false, false, true), "Companion de outro jogador nao pode aparecer.");
        Assert.False(GroundCommandPolicy.CanListMember(true, true, true), "Companion estacionado por desconexao nao pode receber ordem.");
        Assert.False(GroundCommandPolicy.CanListMember(true, false, false), "Companion em outro mapa nao pode receber ordem local.");
    }

    private static void CommandReplayGuardRejectsReplayPerPlayer()
    {
        CommandReplayGuard guard = new();

        Assert.True(guard.TryRegister(101, "command-a"), "O primeiro registro deveria ser aceito.");
        Assert.False(guard.TryRegister(101, "command-a"), "Um replay do mesmo jogador deveria ser rejeitado.");
    }

    private static void CommandReplayGuardIsolatesPlayers()
    {
        CommandReplayGuard guard = new();

        Assert.True(guard.TryRegister(101, "shared-command"), "O primeiro jogador deveria ser aceito.");
        Assert.True(guard.TryRegister(202, "shared-command"), "O mesmo ID pertence a um namespace diferente por jogador.");
        Assert.False(guard.TryRegister(101, "shared-command"), "O replay ainda deveria ser rejeitado para o primeiro jogador.");
        Assert.False(guard.TryRegister(202, "shared-command"), "O replay ainda deveria ser rejeitado para o segundo jogador.");
    }

    private static void CommandReplayGuardEvictsOldestAtCapacity()
    {
        CommandReplayGuard guard = new(capacityPerPlayer: 2);

        Assert.True(guard.TryRegister(101, "command-a"), "registro A");
        Assert.True(guard.TryRegister(101, "command-b"), "registro B");
        Assert.True(guard.TryRegister(101, "command-c"), "registro C");
        Assert.False(guard.TryRegister(101, "command-c"), "C permanece registrado");
        Assert.True(guard.TryRegister(101, "command-a"), "A deveria ter sido expulso quando C entrou");

        Assert.True(guard.TryRegister(202, "command-a"), "A capacidade deve ser independente para outro jogador");
        Assert.True(guard.TryRegister(202, "command-b"), "segundo registro do outro jogador");
        Assert.False(guard.TryRegister(202, "command-a"), "A entrada do primeiro jogador nao deve expulsar entradas do segundo");
    }

    private static void CommandReplayGuardClearForgetsCommands()
    {
        CommandReplayGuard guard = new();
        Assert.True(guard.TryRegister(101, "command-a"), "registro inicial");

        guard.Clear();

        Assert.True(guard.TryRegister(101, "command-a"), "registro depois de Clear");
    }

    private static void SavedItemStackIdentityIsStableAcrossModDataOrder()
    {
        SavedItemStack first = CreateSavedItem(
            stack: 7,
            quality: 2,
            ("example.author/alpha", "one"),
            ("example.author/beta", "two"));
        SavedItemStack reversed = CreateSavedItem(
            stack: 7,
            quality: 2,
            ("example.author/beta", "two"),
            ("example.author/alpha", "one"));

        string firstToken = SavedItemStackIdentity.CreateToken(first);
        string reversedToken = SavedItemStackIdentity.CreateToken(reversed);

        Assert.Equal(firstToken, reversedToken, "A ordem de insercao de ModData nao pode alterar o token.");
        Assert.True(SavedItemStackIdentity.Matches(reversed, firstToken), "Matches deve usar a mesma identidade canonica.");
    }

    private static void SavedItemStackIdentityDistinguishesStack()
    {
        AssertDifferentTokens(CreateSavedItem(stack: 1, quality: 0), CreateSavedItem(stack: 2, quality: 0), "stack");
    }

    private static void SavedItemStackIdentityDistinguishesQuality()
    {
        AssertDifferentTokens(CreateSavedItem(stack: 1, quality: 0), CreateSavedItem(stack: 1, quality: 2), "quality");
    }

    private static void SavedItemStackIdentityDistinguishesModData()
    {
        AssertDifferentTokens(
            CreateSavedItem(stack: 1, quality: 0, ("example.author/key", "old")),
            CreateSavedItem(stack: 1, quality: 0, ("example.author/key", "new")),
            "valor de ModData");
        AssertDifferentTokens(
            CreateSavedItem(stack: 1, quality: 0, ("example.author/key-a", "same")),
            CreateSavedItem(stack: 1, quality: 0, ("example.author/key-b", "same")),
            "chave de ModData");
    }

    private static SavedItemStack CreateSavedItem(
        int stack,
        int quality,
        params (string Key, string Value)[] modData)
    {
        return new SavedItemStack
        {
            QualifiedItemId = "(O)388",
            Stack = stack,
            Quality = quality,
            ModData = modData.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            PreservedParentItemId = "(O)340",
            HasColor = true,
            ColorR = 12,
            ColorG = 34,
            ColorB = 56,
            ColorA = 78
        };
    }

    private static void AssertDifferentTokens(SavedItemStack first, SavedItemStack second, string difference)
    {
        string firstToken = SavedItemStackIdentity.CreateToken(first);
        string secondToken = SavedItemStackIdentity.CreateToken(second);
        Assert.NotEqual(firstToken, secondToken, $"Tokens devem distinguir {difference}.");
        Assert.False(SavedItemStackIdentity.Matches(second, firstToken), $"Matches deve distinguir {difference}.");
    }

    private sealed record TestCase(string Name, Action Body);
}

internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new TestFailureException(message);
    }

    public static void False(bool condition, string message)
    {
        True(!condition, message);
    }

    public static void NotNull(object? value, string message)
    {
        if (value is null)
            throw new TestFailureException($"Esperava valor nao nulo: {message}.");
    }

    public static void Equal<T>(T expected, T actual, string context)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new TestFailureException($"{context}: esperado <{expected}>, recebido <{actual}>.");
    }

    public static void NotEqual<T>(T notExpected, T actual, string context)
    {
        if (EqualityComparer<T>.Default.Equals(notExpected, actual))
            throw new TestFailureException($"{context} Ambos eram <{actual}>.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string context)
    {
        T[] expectedArray = expected.ToArray();
        T[] actualArray = actual.ToArray();
        if (!expectedArray.SequenceEqual(actualArray))
        {
            throw new TestFailureException(
                $"{context}: esperado [{string.Join(", ", expectedArray)}], recebido [{string.Join(", ", actualArray)}].");
        }
    }
}

internal sealed class TestFailureException : Exception
{
    public TestFailureException(string message)
        : base(message)
    {
    }
}
#endif
