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
        new("Catalogo de skills forma quatro trilhas validas", CompanionSkillCatalogFormsFourValidTracks),
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
        new("Enums de trabalho preservam valores e anexam rega", CompanionWorkEnumsPreserveStableValues),
        new("CompanionWorkAreaPolicy restringe tarefas por especialidade", CompanionWorkAreaPolicyRestrictsTasksBySpecialty),
        new("CompanionWorkAreaPolicy valida estado persistido ativo", CompanionWorkAreaPolicyValidatesPersistedState),
        new("WateringTargetPolicy rejeita solo ja molhado", WateringTargetPolicyRejectsAlreadyWateredDirt),
        new("CompanionRoutinePolicy normaliza grade e identifica blocos", CompanionRoutinePolicyNormalizesGridAndFindsBlocks),
        new("CompanionRoutinePolicy ativa edicoes e reserva a rotina da autonomia", CompanionRoutinePolicyActivatesEditsAndSuppressesAutonomy),
        new("CompanionRoutinePolicy respeita execucao unica e revisao", CompanionRoutinePolicyTracksExecutionByRevision),
        new("CompanionRoutinePolicy aplica atalho 06-18 e presets unicos", CompanionRoutinePolicyAppliesShiftAndUniquePresets),
        new("CompanionRoutinePolicy codifica edicao CAS sem dados operacionais", CompanionRoutinePolicyEncodesCasConfigurationOnly),
        new("CompanionStateCopy preserva diretiva e area de rega", CompanionStateCopyPreservesWateringWorkState),
        new("FishingWaterBodyPolicy descobre componente e margens estaveis", FishingWaterBodyPolicyDiscoversStableComponentAndShore),
        new("FishingWaterBodyPolicy falha fechado em entrada invalida ou truncada", FishingWaterBodyPolicyFailsClosedForInvalidOrTruncatedDiscovery),
        new("FishingWaterBodyPolicy escolhe bobber cardinal profundo", FishingWaterBodyPolicySelectsDeepCardinalBobber),
        new("FishingWaterBodyPolicy desempata direcao e nao salta agua", FishingWaterBodyPolicyUsesStableDirectionAndContiguousWater),
        new("FishingSessionPolicy controla relogio e prontidao", FishingSessionPolicyHandlesClockAndReadiness),
        new("FishingSessionPolicy limita alcance e melhora qualidade", FishingSessionPolicyClampsCastAndUpgradesQuality),
        new("FishingSessionPolicy concede XP e limita captura extra", FishingSessionPolicyAwardsXpAndBoundsExtraCatch),
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
        new("ContextCommandPolicy mede alcance pelo stand adjacente", ContextCommandPolicyUsesAdjacentStandRange),
        new("GroundCommandPolicy abre contexto local seguro sem raio de follow", GroundCommandPolicyOpensSafeLocalContext),
        new("GroundCommandPolicy lista membros locais fora da formacao", GroundCommandPolicyListsLocalMembers),
        new("CompanionItemRoutingPolicy sempre prioriza companion e preserva world drop", CompanionItemRoutingPolicyKeepsRequiredEndpoints),
        new("CompanionItemRoutingPolicy usa squad sem depender do owner", CompanionItemRoutingPolicyUsesSquadWhenEnabled),
        new("CompanionItemRoutingPolicy usa owner somente quando disponivel", CompanionItemRoutingPolicyUsesAvailableOwnerWhenSquadIsDisabled),
        new("CompanionChestRoutingPolicy prioriza override e exige GUID", CompanionChestRoutingPolicySelectsValidOverride),
        new("CompanionChestRoutingPolicy nunca trata identidade vazia como wildcard", CompanionChestRoutingPolicyRejectsMissingExpectation),
        new("CompanionChestRoutingPolicy falha fechado para GUID ambiguo", CompanionChestRoutingPolicyRejectsAmbiguousIdentity),
        new("CommandReplayGuard rejeita replay por jogador", CommandReplayGuardRejectsReplayPerPlayer),
        new("CommandReplayGuard isola jogadores", CommandReplayGuardIsolatesPlayers),
        new("CommandReplayGuard expulsa o comando mais antigo por capacidade", CommandReplayGuardEvictsOldestAtCapacity),
        new("CommandReplayGuard.Clear esquece comandos registrados", CommandReplayGuardClearForgetsCommands),
        new("SavedItemStackIdentity e estavel para a ordem de ModData", SavedItemStackIdentityIsStableAcrossModDataOrder),
        new("SavedItemStackIdentity distingue stack", SavedItemStackIdentityDistinguishesStack),
        new("SavedItemStackIdentity distingue quality", SavedItemStackIdentityDistinguishesQuality),
        new("SavedItemStackIdentity distingue ModData", SavedItemStackIdentityDistinguishesModData),
        new("CompanionEquipmentPolicy isola owner e ignora caixa do NPC", CompanionEquipmentPolicyScopesKeysByOwnerAndNpc),
        new("CompanionEquipmentPolicy valida upgrade e capacidade do regador", CompanionEquipmentPolicyValidatesToolBoundaries),
        new("CompanionEquipmentPolicy filtra especialidades pela ferramenta", CompanionEquipmentPolicyFiltersWorkSpecialtiesByTool),
        new("SavedItemStack preserva e identifica estado fiel da ferramenta", SavedToolStateIsClonedAndTokenizedFaithfully),
        new("CompanionOperationsStateCopy clona perfil operacional profundamente", CompanionOperationsStateCopyDeepClonesProfile),
        new("CompanionProfilePolicy migra progressao sem ownership ou inventario", CompanionProfilePolicyMigratesProgressionOnly),
        new("CompanionProfilePolicy restaura progressao ao recrutar novamente", CompanionProfilePolicyRestoresProgressionOnRecruit),
        new("CompanionStateCopy clona perfil permanente profundamente", CompanionStateCopyDeepClonesProfile),
        new("NpcHatRenderPolicy acompanha bob real sem inventar movimento", NpcHatRenderPolicyTracksMeasuredWalkingBob),
        new("CompanionStateCopy clona cosmetico e chapeu profundamente", CompanionStateCopyDeepClonesNpcCosmetic)
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

    private static void CompanionSkillCatalogFormsFourValidTracks()
    {
        Assert.Equal(12, CompanionProgression.Skills.Length, "quantidade de skills ativas");
        Assert.Equal(
            12,
            CompanionProgression.Skills.Select(skill => skill.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "IDs de skill devem ser unicos");
        Assert.SequenceEqual(
            new[] { "Lumbering", "Mining", "Utility", "Fishing" },
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

        Assert.Equal(16, CompanionProgression.Skills.Sum(skill => skill.Cost), "custo total da arvore");
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
        Assert.Equal(3, CompanionWorkAreaPolicy.ClampRadiusToMaximum(20, 3), "preset respeita maximo configurado menor");
        Assert.Equal(8, CompanionWorkAreaPolicy.ClampRadiusToMaximum(8, 20), "maximo maior preserva raio do preset");

        Assert.True(CompanionWorkAreaPolicy.Contains(0, 0, 0, 3, 0), "Contains deve normalizar para tres");
        Assert.False(CompanionWorkAreaPolicy.Contains(0, 0, 0, 4, 0), "limite normalizado inferior");
        Assert.True(CompanionWorkAreaPolicy.Contains(0, 0, 99, 20, 0), "Contains deve normalizar para vinte");
        Assert.False(CompanionWorkAreaPolicy.Contains(0, 0, 99, 21, 0), "limite normalizado superior");
    }

    private static void CompanionWorkEnumsPreserveStableValues()
    {
        Assert.Equal(0, (int)CompanionMode.Following, "modo seguir legado");
        Assert.Equal(1, (int)CompanionMode.Waiting, "modo esperar legado");
        Assert.Equal(2, (int)CompanionMode.ParkedForDisconnect, "modo desconectado legado");
        Assert.Equal(3, (int)CompanionMode.OriginalRoutine, "rotina original anexada");
        Assert.Equal(0, (int)CompanionDirective.SearchWood, "diretiva madeira legada");
        Assert.Equal(1, (int)CompanionDirective.SearchMining, "diretiva mineracao legada");
        Assert.Equal(2, (int)CompanionDirective.ClearArea, "diretiva limpar legada");
        Assert.Equal(3, (int)CompanionDirective.SearchWatering, "diretiva regar anexada");
        Assert.Equal(0, (int)CompanionWorkSpecialty.ClearArea, "especialidade limpar legada");
        Assert.Equal(1, (int)CompanionWorkSpecialty.Wood, "especialidade madeira legada");
        Assert.Equal(2, (int)CompanionWorkSpecialty.Mining, "especialidade mineracao legada");
        Assert.Equal(3, (int)CompanionWorkSpecialty.Watering, "especialidade regar anexada");
    }

    private static void CompanionWorkAreaPolicyRestrictsTasksBySpecialty()
    {
        Assert.True(CompanionWorkAreaPolicy.Allows(CompanionWorkSpecialty.Wood, CompanionTaskKind.Lumbering), "madeira permite corte");
        Assert.False(CompanionWorkAreaPolicy.Allows(CompanionWorkSpecialty.Wood, CompanionTaskKind.Mining), "madeira rejeita mineracao");
        Assert.True(CompanionWorkAreaPolicy.Allows(CompanionWorkSpecialty.Mining, CompanionTaskKind.Mining), "mineracao permite pedras");
        Assert.False(CompanionWorkAreaPolicy.Allows(CompanionWorkSpecialty.Mining, CompanionTaskKind.Lumbering), "mineracao rejeita corte");
        Assert.True(CompanionWorkAreaPolicy.Allows(CompanionWorkSpecialty.Watering, CompanionTaskKind.Watering), "rega permite terra seca");
        Assert.False(CompanionWorkAreaPolicy.Allows(CompanionWorkSpecialty.Watering, CompanionTaskKind.Lumbering), "rega rejeita corte");
        Assert.False(CompanionWorkAreaPolicy.Allows(CompanionWorkSpecialty.Watering, CompanionTaskKind.Mining), "rega rejeita mineracao");
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
        Assert.True(
            CompanionWorkAreaPolicy.IsPersistedStateValid(true, "order-water", "Farm", 12, 14, 8, CompanionWorkSpecialty.Watering),
            "estado persistido aceita area de rega");

        Assert.False(CompanionWorkAreaPolicy.IsPersistedStateValid(true, " ", "Farm", 1, 1, 8, CompanionWorkSpecialty.ClearArea), "order id vazio");
        Assert.False(CompanionWorkAreaPolicy.IsPersistedStateValid(true, "order", " ", 1, 1, 8, CompanionWorkSpecialty.ClearArea), "mapa vazio");
        Assert.False(CompanionWorkAreaPolicy.IsPersistedStateValid(true, "order", "Farm", -1, 1, 8, CompanionWorkSpecialty.ClearArea), "centro X negativo");
        Assert.False(CompanionWorkAreaPolicy.IsPersistedStateValid(true, "order", "Farm", 1, -1, 8, CompanionWorkSpecialty.ClearArea), "centro Y negativo");
        Assert.False(CompanionWorkAreaPolicy.IsPersistedStateValid(true, "order", "Farm", 1, 1, 2, CompanionWorkSpecialty.ClearArea), "raio abaixo de tres");
        Assert.False(CompanionWorkAreaPolicy.IsPersistedStateValid(true, "order", "Farm", 1, 1, 21, CompanionWorkSpecialty.ClearArea), "raio acima de vinte");
        Assert.False(CompanionWorkAreaPolicy.IsPersistedStateValid(true, "order", "Farm", 1, 1, 8, (CompanionWorkSpecialty)999), "especialidade desconhecida");
    }

    private static void WateringTargetPolicyRejectsAlreadyWateredDirt()
    {
        Assert.True(WateringTargetPolicy.IsValid(cropNeedsWatering: true, dirtIsWatered: false), "cultivo seco deve ser alvo");
        Assert.False(WateringTargetPolicy.IsValid(cropNeedsWatering: true, dirtIsWatered: true), "cultivo ja molhado nao pode repetir");
        Assert.False(WateringTargetPolicy.IsValid(cropNeedsWatering: false, dirtIsWatered: false), "cultivo que dispensa agua nao deve ser alvo");
        Assert.False(WateringTargetPolicy.IsValid(cropNeedsWatering: false, dirtIsWatered: true), "solo molhado sem demanda continua invalido");
    }

    private static void CompanionRoutinePolicyNormalizesGridAndFindsBlocks()
    {
        IReadOnlyList<CompanionRoutineHourState> hours = CompanionRoutinePolicy.NormalizeHours(new[]
        {
            new CompanionRoutineHourState { Hour = 7, Activity = CompanionRoutineActivity.Water },
            new CompanionRoutineHourState { Hour = 8, Activity = CompanionRoutineActivity.Water },
            new CompanionRoutineHourState { Hour = 26, Activity = CompanionRoutineActivity.Deposit },
            new CompanionRoutineHourState { Hour = 8, Activity = CompanionRoutineActivity.Mine }
        });

        Assert.Equal(CompanionRoutinePolicy.HourCount, hours.Count, "vinte horas de 06h a 25h");
        Assert.Equal(CompanionRoutinePolicy.FirstHour, hours[0].Hour, "primeira hora");
        Assert.Equal(CompanionRoutinePolicy.LastHour, hours[^1].Hour, "ultima hora");
        Assert.Equal(CompanionRoutineActivity.Follow, hours[0].Activity, "lacuna recebe seguir");
        Assert.Equal(CompanionRoutineActivity.Water, hours[1].Activity, "atividade valida preservada");
        Assert.Equal(CompanionRoutineActivity.Mine, hours[2].Activity, "ultimo valor duplicado vence");

        Assert.Equal(7, CompanionRoutinePolicy.GetBlockStartHour(hours, 750), "inicio do bloco de rega");
        Assert.Equal(8, CompanionRoutinePolicy.GetBlockStartHour(hours, 830), "troca de atividade abre bloco");

        IReadOnlyList<CompanionRoutineHourState> lateHours = CompanionRoutinePolicy.NormalizeHours(new[]
        {
            new CompanionRoutineHourState { Hour = 23, Activity = CompanionRoutineActivity.Wait },
            new CompanionRoutineHourState { Hour = 24, Activity = CompanionRoutineActivity.Wait },
            new CompanionRoutineHourState { Hour = 25, Activity = CompanionRoutineActivity.Wait }
        });
        Assert.Equal(CompanionRoutineActivity.Follow, CompanionRoutinePolicy.GetActivity(lateHours, 600), "inicio das 06h");
        Assert.Equal(CompanionRoutineActivity.Follow, CompanionRoutinePolicy.GetActivity(lateHours, 659), "fim das 06h");
        Assert.Equal(CompanionRoutineActivity.Follow, CompanionRoutinePolicy.GetActivity(lateHours, 700), "inicio das 07h");
        Assert.Equal(CompanionRoutineActivity.Wait, CompanionRoutinePolicy.GetActivity(lateHours, 2350), "faixa das 23h");
        Assert.Equal(CompanionRoutineActivity.Wait, CompanionRoutinePolicy.GetActivity(lateHours, 2400), "inicio das 00h");
        Assert.Equal(CompanionRoutineActivity.Wait, CompanionRoutinePolicy.GetActivity(lateHours, 2450), "faixa das 00h");
        Assert.Equal(CompanionRoutineActivity.Wait, CompanionRoutinePolicy.GetActivity(lateHours, 2500), "inicio das 01h");
        Assert.Equal(CompanionRoutineActivity.Wait, CompanionRoutinePolicy.GetActivity(lateHours, 2550), "faixa das 01h");
        Assert.Equal(CompanionRoutineActivity.Wait, CompanionRoutinePolicy.GetActivity(lateHours, 2600), "relogio apos 01h permanece na ultima faixa");
        Assert.Equal(23, CompanionRoutinePolicy.GetBlockStartHour(lateHours, 2550), "bloco atravessa 23h, 00h e 01h");

        CompanionRoutineState oneDay = new() { Enabled = true, RepeatDaily = false, ScheduledDayIndex = 42 };
        Assert.True(CompanionRoutinePolicy.ShouldRun(oneDay, 42), "roda no dia agendado");
        Assert.False(CompanionRoutinePolicy.ShouldRun(oneDay, 43), "nao repete no dia seguinte");
        oneDay.RepeatDaily = true;
        Assert.True(CompanionRoutinePolicy.ShouldRun(oneDay, 43), "rotina diaria ignora dia inicial");

        CompanionRoutineState disabledEdit = new() { Enabled = false };
        Assert.True(
            CompanionRoutinePolicy.ShouldApplyCompletionAfterEdit(new CompanionRoutineState { Enabled = true }, disabledEdit),
            "desativar uma rotina ativa aplica o comportamento de conclusao");
        Assert.False(
            CompanionRoutinePolicy.ShouldApplyCompletionAfterEdit(new CompanionRoutineState { Enabled = false }, disabledEdit),
            "salvar uma rotina ja inativa nao toma controle do NPC");
    }

    private static void CompanionRoutinePolicyTracksExecutionByRevision()
    {
        CompanionRoutineState routine = new()
        {
            Enabled = true,
            Revision = 3,
            Hours = new List<CompanionRoutineHourState>
            {
                new() { Hour = 6, Activity = CompanionRoutineActivity.Water }
            }
        };
        CompanionRoutinePolicy.MarkApplied(routine, dayIndex: 12, blockStartHour: 6);
        Assert.True(
            CompanionRoutinePolicy.ShouldRetryAppliedOperationalBlock(
                routine,
                dayIndex: 12,
                blockStartHour: 6,
                CompanionRoutineActivity.Water,
                hasExplicitOverride: false,
                hasActiveRoutineWorkArea: false),
            "bloco reivindicado sem area continua tentando");
        Assert.Equal(
            CompanionRoutinePlanningLane.None,
            CompanionRoutinePolicy.SelectPlanningLane(routine, dayIndex: 12, hasExplicitDirective: false),
            "durante o retry a rotina ainda reserva o planejamento");
        Assert.False(
            CompanionRoutinePolicy.ShouldRetryAppliedOperationalBlock(
                routine,
                dayIndex: 12,
                blockStartHour: 6,
                CompanionRoutineActivity.Water,
                hasExplicitOverride: true,
                hasActiveRoutineWorkArea: false),
            "override explicito pausa o retry da rotina");
        Assert.Equal(
            CompanionRoutinePlanningLane.ExplicitDirective,
            CompanionRoutinePolicy.SelectPlanningLane(routine, dayIndex: 12, hasExplicitDirective: true),
            "override explicito recebe a lane enquanto esta ativo");
        Assert.True(
            CompanionRoutinePolicy.ShouldRetryAppliedOperationalBlock(
                routine,
                dayIndex: 12,
                blockStartHour: 6,
                CompanionRoutineActivity.Water,
                hasExplicitOverride: false,
                hasActiveRoutineWorkArea: false),
            "ao terminar o override a rotina retoma o bloco reivindicado");
        Assert.False(
            CompanionRoutinePolicy.ShouldRetryAppliedOperationalBlock(
                routine,
                dayIndex: 12,
                blockStartHour: 6,
                CompanionRoutineActivity.Water,
                hasExplicitOverride: false,
                hasActiveRoutineWorkArea: true),
            "area da rotina em andamento nao e recriada");
        Assert.Equal(
            CompanionRoutinePlanningLane.ExplicitDirective,
            CompanionRoutinePolicy.SelectPlanningLane(routine, dayIndex: 12, hasExplicitDirective: true),
            "area operacional ativa continua na lane explicita");

        CompanionRoutinePolicy.MarkCompleted(routine, dayIndex: 12, blockStartHour: 6);

        Assert.True(CompanionRoutinePolicy.IsAppliedBlock(routine, 12, 6), "bloco aplicado na revisao atual");
        Assert.True(CompanionRoutinePolicy.IsCompletedBlock(routine, 12, 6), "bloco concluido na revisao atual");
        Assert.False(
            CompanionRoutinePolicy.ShouldRetryAppliedOperationalBlock(
                routine,
                dayIndex: 12,
                blockStartHour: 6,
                CompanionRoutineActivity.Water,
                hasExplicitOverride: false,
                hasActiveRoutineWorkArea: false),
            "bloco concluido nunca tenta novamente");
        Assert.Equal(
            CompanionRoutinePlanningLane.None,
            CompanionRoutinePolicy.SelectPlanningLane(routine, dayIndex: 12, hasExplicitDirective: false),
            "bloco concluido nao libera autonomia generica no restante da hora");
        Assert.True(
            CompanionRoutinePolicy.IsActivityModeActive(
                CompanionRoutineActivity.Follow,
                CompanionMode.Following),
            "seguir ja aplicado nao precisa ser repetido a cada refresh");
        Assert.False(
            CompanionRoutinePolicy.IsActivityModeActive(
                CompanionRoutineActivity.Follow,
                CompanionMode.Waiting),
            "fim de override em espera exige restaurar o bloco seguir");
        Assert.True(
            CompanionRoutinePolicy.IsActivityModeActive(
                CompanionRoutineActivity.Wait,
                CompanionMode.Waiting),
            "espera ja aplicada e idempotente");
        Assert.False(
            CompanionRoutinePolicy.IsActivityModeActive(
                CompanionRoutineActivity.Wait,
                CompanionMode.Following),
            "fim de task em following exige restaurar o bloco esperar");
        Assert.True(
            CompanionRoutinePolicy.IsActivityModeActive(
                CompanionRoutineActivity.VanillaRoutine,
                CompanionMode.OriginalRoutine),
            "rotina vanilla ja restaurada e idempotente");
        routine.Revision++;
        Assert.False(CompanionRoutinePolicy.IsAppliedBlock(routine, 12, 6), "edicao invalida aplicacao anterior");
        Assert.False(CompanionRoutinePolicy.IsCompletedBlock(routine, 12, 6), "edicao invalida conclusao anterior");
        Assert.False(
            CompanionRoutinePolicy.ShouldRetryAppliedOperationalBlock(
                routine,
                dayIndex: 12,
                blockStartHour: 6,
                CompanionRoutineActivity.Water,
                hasExplicitOverride: false,
                hasActiveRoutineWorkArea: false),
            "revisao nova nao reaproveita reivindicacao obsoleta");

        CompanionRoutineState depositRoutine = new() { Enabled = true, Revision = 4 };
        CompanionRoutinePolicy.MarkApplied(depositRoutine, dayIndex: 12, blockStartHour: 7);
        Assert.True(
            CompanionRoutinePolicy.ShouldRetryAppliedOperationalBlock(
                depositRoutine,
                dayIndex: 12,
                blockStartHour: 7,
                CompanionRoutineActivity.Deposit,
                hasExplicitOverride: false,
                hasActiveRoutineWorkArea: false),
            "deposito reivindicado repete enquanto o destino nao estiver pronto");
        Assert.False(
            CompanionRoutinePolicy.ShouldRetryAppliedOperationalBlock(
                depositRoutine,
                dayIndex: 12,
                blockStartHour: 7,
                CompanionRoutineActivity.Deposit,
                hasExplicitOverride: true,
                hasActiveRoutineWorkArea: false),
            "override explicito tambem pausa o retry de deposito");
    }

    private static void CompanionRoutinePolicyActivatesEditsAndSuppressesAutonomy()
    {
        CompanionRoutineState routine = new();
        CompanionRoutinePolicy.PaintHour(routine, 9, CompanionRoutineActivity.Wait);

        Assert.True(routine.Enabled, "pintar uma celula ativa a rotina");
        Assert.Equal(
            CompanionRoutineActivity.Wait,
            CompanionRoutinePolicy.GetActivity(routine.Hours, 950),
            "atividade pintada e preservada");

        string encoded = CompanionRoutinePolicy.Encode(routine);
        Assert.True(
            CompanionRoutinePolicy.TryDecode(encoded, out CompanionRoutineState decoded),
            "edicao ativa sobrevive ao payload");
        Assert.True(
            CompanionRoutinePolicy.ShouldRun(decoded, 42),
            "rotina diaria editada fica executavel");
        Assert.Equal(
            CompanionRoutinePlanningLane.None,
            CompanionRoutinePolicy.SelectPlanningLane(decoded, 42, hasExplicitDirective: false),
            "rotina ativa impede autonomia generica");
        Assert.Equal(
            CompanionRoutinePlanningLane.ExplicitDirective,
            CompanionRoutinePolicy.SelectPlanningLane(decoded, 42, hasExplicitDirective: true),
            "diretiva explicita precede a rotina ativa");

        decoded.Enabled = false;
        Assert.Equal(
            CompanionRoutinePlanningLane.ConfiguredAutonomy,
            CompanionRoutinePolicy.SelectPlanningLane(decoded, 42, hasExplicitDirective: false),
            "rotina pausada devolve controle a autonomia generica");

        CompanionRoutineState once = new() { RepeatDaily = false };
        CompanionRoutinePolicy.PaintHour(once, 10, CompanionRoutineActivity.Deposit);
        Assert.True(
            CompanionRoutinePolicy.TryDecode(
                CompanionRoutinePolicy.Encode(once),
                out CompanionRoutineState decodedOnce),
            "edicao somente hoje sobrevive ao payload");
        decodedOnce.ScheduledDayIndex = 42;
        decodedOnce.Revision = 1;
        CompanionRoutinePolicy.ResetExecution(decodedOnce);
        Assert.True(CompanionRoutinePolicy.ShouldRun(decodedOnce, 42), "host agenda somente hoje no dia do commit");
        Assert.False(CompanionRoutinePolicy.ShouldRun(decodedOnce, 43), "somente hoje nao invade o dia seguinte");
    }

    private static void CompanionRoutinePolicyAppliesShiftAndUniquePresets()
    {
        CompanionRoutineState routine = new()
        {
            CompletionBehavior = CompanionRoutineCompletionBehavior.VanillaRoutine
        };
        CompanionRoutinePolicy.ApplyWorkUntilSixPm(routine, CompanionRoutineActivity.Lumber);

        Assert.True(routine.Enabled, "atalho de turno ativa a rotina");
        Assert.Equal(CompanionRoutineActivity.Lumber, CompanionRoutinePolicy.GetActivity(routine.Hours, 1750), "trabalho antes das 18h");
        Assert.Equal(CompanionRoutineActivity.VanillaRoutine, CompanionRoutinePolicy.GetActivity(routine.Hours, 1800), "conclusao a partir das 18h");

        CompanionRoutinePolicy.UpsertAreaPreset(routine, new CompanionRoutineAreaPreset
        {
            Specialty = CompanionWorkSpecialty.Wood,
            LocationName = "Farm",
            CenterX = 4,
            CenterY = 5,
            Radius = 8
        });
        CompanionRoutinePolicy.UpsertAreaPreset(routine, new CompanionRoutineAreaPreset
        {
            Specialty = CompanionWorkSpecialty.Wood,
            LocationName = "Forest",
            CenterX = 12,
            CenterY = 13,
            Radius = 6
        });

        Assert.Equal(1, routine.AreaPresets.Count, "um preset por especialidade");
        Assert.Equal("Forest", CompanionRoutinePolicy.GetAreaPreset(routine, CompanionWorkSpecialty.Wood)?.LocationName, "preset mais recente vence");
    }

    private static void CompanionRoutinePolicyEncodesCasConfigurationOnly()
    {
        CompanionRoutineState routine = new()
        {
            Enabled = true,
            RepeatDaily = false,
            ScheduledDayIndex = 99,
            Revision = 7,
            CompletionBehavior = CompanionRoutineCompletionBehavior.Wait,
            AreaPresets = new List<CompanionRoutineAreaPreset>
            {
                new()
                {
                    Specialty = CompanionWorkSpecialty.Mining,
                    LocationName = "Mine",
                    CenterX = 10,
                    CenterY = 20,
                    Radius = 5
                }
            }
        };
        CompanionRoutinePolicy.ApplyWorkUntilSixPm(routine, CompanionRoutineActivity.Mine);
        string token = CompanionRoutinePolicy.CreateStateToken(routine);
        string encoded = CompanionRoutinePolicy.Encode(routine);

        Assert.True(CompanionRoutinePolicy.TryDecode(encoded, out CompanionRoutineState decoded), "payload valido");
        Assert.True(decoded.Enabled, "enabled no payload");
        Assert.False(decoded.RepeatDaily, "repeat no payload");
        Assert.Equal(CompanionRoutineCompletionBehavior.Wait, decoded.CompletionBehavior, "conclusao no payload");
        Assert.Equal(0, decoded.AreaPresets.Count, "preset permanece host-side");
        Assert.Equal(-1, decoded.ScheduledDayIndex, "dia e definido pelo host");
        Assert.NotEqual(token, CompanionRoutinePolicy.CreateStateToken(new CompanionRoutineState
        {
            Enabled = routine.Enabled,
            RepeatDaily = routine.RepeatDaily,
            Revision = routine.Revision + 1,
            CompletionBehavior = routine.CompletionBehavior,
            Hours = CompanionRoutinePolicy.NormalizeHours(routine.Hours).ToList()
        }), "revisao participa do token CAS");
    }

    private static void CompanionStateCopyPreservesWateringWorkState()
    {
        SquadMemberState source = new()
        {
            NpcName = "Leah",
            SearchWatering = true,
            PreferredWorkSpecialty = CompanionWorkSpecialty.Watering,
            WorkAreaActive = true,
            WorkAreaOrderId = "water-order",
            WorkAreaLocationName = "Farm",
            WorkAreaCenterX = 8,
            WorkAreaCenterY = 9,
            WorkAreaRadius = 7,
            WorkAreaSpecialty = CompanionWorkSpecialty.Watering
        };

        SquadMemberState clone = CompanionStateCopy.CloneMember(source);

        Assert.True(clone.SearchWatering, "diretiva regar clonada");
        Assert.Equal(CompanionWorkSpecialty.Watering, clone.PreferredWorkSpecialty, "preferencia regar clonada");
        Assert.True(clone.WorkAreaActive, "area ativa clonada");
        Assert.Equal("water-order", clone.WorkAreaOrderId, "ordem de rega clonada");
        Assert.Equal(CompanionWorkSpecialty.Watering, clone.WorkAreaSpecialty, "especialidade da area clonada");
    }

    private static void FishingWaterBodyPolicyDiscoversStableComponentAndShore()
    {
        HashSet<FishingTile> water = new()
        {
            new(2, 2),
            new(3, 2),
            new(2, 3),
            new(3, 3),
            new(5, 2)
        };

        Assert.True(
            FishingWaterBodyPolicy.TryDiscover(
                new FishingTile(3, 3),
                mapWidth: 7,
                mapHeight: 6,
                water.Contains,
                maximumComponentTiles: 16,
                out FishingWaterBody first),
            "O clique deve descobrir o quadrado cardinal completo.");
        Assert.SequenceEqual(
            new[]
            {
                new FishingTile(2, 2),
                new FishingTile(3, 2),
                new FishingTile(2, 3),
                new FishingTile(3, 3)
            },
            first.WaterTiles,
            "A agua deve ser ordenada por linha e coluna.");
        Assert.Equal(new FishingTile(2, 2), first.Anchor, "A ancora deve ser o primeiro tile na ordem estavel.");
        Assert.SequenceEqual(
            new[]
            {
                new FishingTile(2, 1),
                new FishingTile(3, 1),
                new FishingTile(1, 2),
                new FishingTile(4, 2),
                new FishingTile(1, 3),
                new FishingTile(4, 3),
                new FishingTile(2, 4),
                new FishingTile(3, 4)
            },
            first.ShoreTiles,
            "Margens cardinais devem ser unicas e deterministicas.");
        Assert.True(first.Token.StartsWith("water|2,2|4|", StringComparison.Ordinal), "O token deve carregar ancora e tamanho estaveis.");

        Assert.True(
            FishingWaterBodyPolicy.TryDiscover(
                new FishingTile(2, 2),
                7,
                6,
                water.Contains,
                16,
                out FishingWaterBody sameComponent),
            "Outro tile do mesmo corpo deve ser aceito.");
        Assert.Equal(first.Token, sameComponent.Token, "O token nao pode depender do tile clicado.");
        Assert.Equal(first.Anchor, sameComponent.Anchor, "A ancora nao pode depender do tile clicado.");

        Assert.True(
            FishingWaterBodyPolicy.TryDiscover(
                new FishingTile(5, 2),
                7,
                6,
                water.Contains,
                16,
                out FishingWaterBody disconnected),
            "A agua separada por terra ainda forma seu proprio corpo.");
        Assert.Equal(1, disconnected.WaterTiles.Count, "O componente cardinal nao pode atravessar a terra.");
        Assert.NotEqual(first.Token, disconnected.Token, "Corpos desconectados precisam de identidades diferentes.");
    }

    private static void FishingWaterBodyPolicyFailsClosedForInvalidOrTruncatedDiscovery()
    {
        HashSet<FishingTile> water = new()
        {
            new(2, 2),
            new(3, 2),
            new(2, 3),
            new(3, 3)
        };

        Assert.False(
            FishingWaterBodyPolicy.TryDiscover(new FishingTile(2, 2), 0, 5, water.Contains, 10, out _),
            "Largura invalida deve falhar.");
        Assert.False(
            FishingWaterBodyPolicy.TryDiscover(new FishingTile(2, 2), 5, -1, water.Contains, 10, out _),
            "Altura invalida deve falhar.");
        Assert.False(
            FishingWaterBodyPolicy.TryDiscover(new FishingTile(-1, 2), 5, 5, water.Contains, 10, out _),
            "Clique fora do mapa deve falhar.");
        Assert.False(
            FishingWaterBodyPolicy.TryDiscover(new FishingTile(1, 1), 5, 5, water.Contains, 10, out _),
            "Clique em terra deve falhar.");
        Assert.False(
            FishingWaterBodyPolicy.TryDiscover(new FishingTile(2, 2), 5, 5, water.Contains, 0, out _),
            "Limite vazio deve falhar.");

        Assert.False(
            FishingWaterBodyPolicy.TryDiscover(
                new FishingTile(2, 2),
                5,
                5,
                water.Contains,
                maximumComponentTiles: 3,
                out FishingWaterBody truncated),
            "Um componente maior que o limite nao pode retornar um recorte parcial.");
        Assert.Equal<FishingWaterBody?>(null, truncated, "Descoberta truncada deve limpar a saida.");
        Assert.True(
            FishingWaterBodyPolicy.TryDiscover(new FishingTile(2, 2), 5, 5, water.Contains, 4, out FishingWaterBody exact),
            "Um componente exatamente no limite deve ser aceito.");
        Assert.Equal(4, exact.WaterTiles.Count, "O limite inclusivo deve preservar o componente completo.");
    }

    private static void FishingWaterBodyPolicySelectsDeepCardinalBobber()
    {
        HashSet<FishingTile> water = Enumerable.Range(1, 5)
            .SelectMany(y => Enumerable.Range(2, 5).Select(x => new FishingTile(x, y)))
            .ToHashSet();
        Assert.True(
            FishingWaterBodyPolicy.TryDiscover(new FishingTile(4, 3), 9, 8, water.Contains, 100, out FishingWaterBody body),
            "O lago retangular deve ser descoberto.");

        Assert.Equal(1, FishingWaterBodyPolicy.GetApproximateDepth(body, new FishingTile(4, 5)), "agua junto a margem");
        Assert.Equal(3, FishingWaterBodyPolicy.GetApproximateDepth(body, new FishingTile(4, 3)), "centro do lago");
        Assert.Equal(2, FishingWaterBodyPolicy.GetApproximateDepth(body, new FishingTile(4, 3), maximumDepth: 2), "limite de profundidade");
        Assert.Equal(0, FishingWaterBodyPolicy.GetApproximateDepth(body, new FishingTile(4, 6)), "tile fora do corpo");

        Assert.True(
            FishingWaterBodyPolicy.TrySelectBobber(body, new FishingTile(4, 6), 3, out FishingBobberSelection selection),
            "A margem sul deve permitir um arremesso cardinal.");
        Assert.Equal(new FishingTile(4, 3), selection.Tile, "O bobber deve buscar a agua mais profunda dentro do alcance.");
        Assert.Equal(3, selection.CastDistance, "distancia do arremesso");
        Assert.Equal(3, selection.ApproximateDepth, "profundidade do bobber");

        Assert.False(
            FishingWaterBodyPolicy.TrySelectBobber(body, new FishingTile(4, 6), 0, out _),
            "Alcance nulo deve falhar.");
        Assert.False(
            FishingWaterBodyPolicy.TrySelectBobber(body, new FishingTile(0, 7), 8, out _),
            "O arremesso nao pode dobrar ou atravessar terra ate o lago.");
    }

    private static void FishingWaterBodyPolicyUsesStableDirectionAndContiguousWater()
    {
        HashSet<FishingTile> cornerWater = new()
        {
            new(3, 2),
            new(4, 2),
            new(4, 3)
        };
        Assert.True(
            FishingWaterBodyPolicy.TryDiscover(new FishingTile(4, 2), 7, 7, cornerWater.Contains, 10, out FishingWaterBody cornerBody),
            "O canto de agua deve ser conectado.");
        Assert.True(
            FishingWaterBodyPolicy.TrySelectBobber(cornerBody, new FishingTile(3, 3), 1, out FishingBobberSelection tie),
            "Duas direcoes cardinais equivalentes devem produzir um bobber.");
        Assert.Equal(new FishingTile(3, 2), tie.Tile, "Empate deve preferir Norte antes de Leste.");
        Assert.True(
            FishingWaterBodyPolicy.TrySelectBobber(
                cornerBody,
                new FishingTile(3, 3),
                1,
                tile => tile != new FishingTile(3, 2),
                out FishingBobberSelection allowed),
            "Um destino de pesca proibido deve permitir outra direcao valida.");
        Assert.Equal(new FishingTile(4, 3), allowed.Tile, "O bobber deve respeitar o filtro de destino pescavel.");
        Assert.False(
            FishingWaterBodyPolicy.TrySelectBobber(
                cornerBody,
                new FishingTile(3, 3),
                1,
                _ => false,
                out _),
            "Sem destino permitido, o arremesso deve falhar fechado.");

        HashSet<FishingTile> detourWater = new()
        {
            new(1, 3),
            new(0, 3),
            new(0, 2),
            new(0, 1),
            new(1, 1)
        };
        Assert.True(
            FishingWaterBodyPolicy.TryDiscover(new FishingTile(1, 3), 5, 6, detourWater.Contains, 10, out FishingWaterBody detourBody),
            "A agua distante deve pertencer ao mesmo componente pelo desvio.");
        Assert.True(
            FishingWaterBodyPolicy.TrySelectBobber(detourBody, new FishingTile(1, 4), 3, out FishingBobberSelection contiguous),
            "A primeira agua cardinal deve continuar pescavel.");
        Assert.Equal(new FishingTile(1, 3), contiguous.Tile, "O bobber nao pode saltar a lacuna de terra na mesma coluna.");
        Assert.Equal(1, contiguous.CastDistance, "A lacuna deve encerrar o raio cardinal.");
    }

    private static void FishingSessionPolicyHandlesClockAndReadiness()
    {
        Assert.False(FishingSessionPolicy.HasDayEnded(2550), "25:50 ainda permite a sessao.");
        Assert.True(FishingSessionPolicy.HasDayEnded(2600), "26:00 encerra no limite.");
        Assert.True(FishingSessionPolicy.HasDayEnded(2710), "Horario posterior permanece encerrado.");
        Assert.Equal(60, FishingSessionPolicy.GetCatchIntervalMinutes(hasFishingSkillOne: false), "intervalo base");
        Assert.Equal(50, FishingSessionPolicy.GetCatchIntervalMinutes(hasFishingSkillOne: true), "intervalo da skill 1");

        Assert.Equal(700, FishingSessionPolicy.AddMinutes(600, 60), "uma hora exata");
        Assert.Equal(710, FishingSessionPolicy.AddMinutes(650, 20), "normalizacao de minutos");
        Assert.Equal(2410, FishingSessionPolicy.AddMinutes(2350, 20), "relogio alem de 24h");
        Assert.Equal(2650, FishingSessionPolicy.AddMinutes(2550, 60), "agendamento pode ficar alem do fim do dia");
        Assert.Throws<ArgumentOutOfRangeException>(() => FishingSessionPolicy.AddMinutes(1260, 10), "minuto HHMM invalido");
        Assert.Throws<ArgumentOutOfRangeException>(() => FishingSessionPolicy.AddMinutes(1200, -1), "adicao negativa");

        Assert.False(FishingSessionPolicy.IsCatchReady(1240, 1250), "antes do horario agendado");
        Assert.True(FishingSessionPolicy.IsCatchReady(1250, 1250), "pronto no limite inclusivo");
        Assert.True(FishingSessionPolicy.IsCatchReady(1300, 1250), "pronto depois do agendamento");
        Assert.False(FishingSessionPolicy.IsCatchReady(2600, 2500), "fim do dia precede uma captura atrasada");
    }

    private static void FishingSessionPolicyClampsCastAndUpgradesQuality()
    {
        Assert.Equal(3, FishingSessionPolicy.GetMaximumCastDistance(int.MinValue, hasFishingSkillTwo: false), "upgrade negativo");
        Assert.Equal(3, FishingSessionPolicy.GetMaximumCastDistance(0, hasFishingSkillTwo: false), "alcance base");
        Assert.Equal(5, FishingSessionPolicy.GetMaximumCastDistance(2, hasFishingSkillTwo: false), "upgrade intermediario");
        Assert.Equal(7, FishingSessionPolicy.GetMaximumCastDistance(int.MaxValue, hasFishingSkillTwo: false), "clamp do upgrade");
        Assert.Equal(8, FishingSessionPolicy.GetMaximumCastDistance(int.MaxValue, hasFishingSkillTwo: true), "bonus da skill 2 depois do clamp");

        Assert.Equal(0, FishingSessionPolicy.GetCatchQuality(-10, hasFishingSkillTwo: false), "profundidade invalida fica normal");
        Assert.Equal(0, FishingSessionPolicy.GetCatchQuality(1, hasFishingSkillTwo: false), "margem normal");
        Assert.Equal(1, FishingSessionPolicy.GetCatchQuality(2, hasFishingSkillTwo: false), "profundidade prata");
        Assert.Equal(1, FishingSessionPolicy.GetCatchQuality(3, hasFishingSkillTwo: false), "faixa prata inclusiva");
        Assert.Equal(2, FishingSessionPolicy.GetCatchQuality(4, hasFishingSkillTwo: false), "profundidade ouro");
        Assert.Equal(2, FishingSessionPolicy.GetCatchQuality(int.MaxValue, hasFishingSkillTwo: false), "qualidade base limitada a ouro");
        Assert.Equal(1, FishingSessionPolicy.GetCatchQuality(1, hasFishingSkillTwo: true), "skill 2 normal para prata");
        Assert.Equal(2, FishingSessionPolicy.GetCatchQuality(2, hasFishingSkillTwo: true), "skill 2 prata para ouro");
        Assert.Equal(4, FishingSessionPolicy.GetCatchQuality(4, hasFishingSkillTwo: true), "skill 2 ouro para iridio");
    }

    private static void FishingSessionPolicyAwardsXpAndBoundsExtraCatch()
    {
        Assert.Equal(8, FishingSessionPolicy.XpPerCatch, "XP fixo por captura concluida");
        Assert.False(FishingSessionPolicy.RollsExtraCatch(hasFishingSkillThree: false, roll: 0d), "sem skill 3");
        Assert.True(FishingSessionPolicy.RollsExtraCatch(hasFishingSkillThree: true, roll: 0d), "inicio da faixa");
        Assert.True(FishingSessionPolicy.RollsExtraCatch(hasFishingSkillThree: true, roll: 0.249999d), "abaixo de 25 por cento");
        Assert.False(FishingSessionPolicy.RollsExtraCatch(hasFishingSkillThree: true, roll: 0.25d), "25 por cento e limite exclusivo");
        Assert.False(FishingSessionPolicy.RollsExtraCatch(hasFishingSkillThree: true, roll: 1d), "fora do intervalo do RNG");
        Assert.False(FishingSessionPolicy.RollsExtraCatch(hasFishingSkillThree: true, roll: -double.Epsilon), "roll negativo");
        Assert.False(FishingSessionPolicy.RollsExtraCatch(hasFishingSkillThree: true, roll: double.NaN), "roll NaN");
        Assert.False(FishingSessionPolicy.RollsExtraCatch(hasFishingSkillThree: true, roll: double.PositiveInfinity), "roll infinito");
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

    private static void ContextCommandPolicyUsesAdjacentStandRange()
    {
        Assert.True(
            ContextCommandPolicy.HasAdjacentStandWithinRadius(0, 0, 3, 0, 3),
            "Alvo dentro do raio deve continuar valido.");
        Assert.True(
            ContextCommandPolicy.HasAdjacentStandWithinRadius(0, 0, 4, 0, 3),
            "Arvore a quatro tiles deve valer quando o stand adjacente fica a tres.");
        Assert.True(
            ContextCommandPolicy.HasAdjacentStandWithinRadius(0, 0, 0, -4, 3),
            "A regra deve funcionar igualmente em todas as direcoes.");
        Assert.True(
            ContextCommandPolicy.HasAdjacentStandWithinRadius(0, 0, 3, 2, 3),
            "Um stand cardinal que cabe no circulo deve validar o alvo diagonal.");
        Assert.False(
            ContextCommandPolicy.HasAdjacentStandWithinRadius(0, 0, 5, 0, 3),
            "Alvo sem nenhum stand adjacente dentro do raio deve ser rejeitado.");
        Assert.False(
            ContextCommandPolicy.HasAdjacentStandWithinRadius(0, 0, 3, 3, 3),
            "Distancia diagonal nao pode ser tratada como alcance quadrado.");
        Assert.False(
            ContextCommandPolicy.HasAdjacentStandWithinRadius(0, 0, 1, 0, -1),
            "Raio invalido deve falhar fechado.");
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

    private static void CompanionItemRoutingPolicyKeepsRequiredEndpoints()
    {
        foreach (bool useSquadInventory in new[] { false, true })
        {
            foreach (bool ownerAvailable in new[] { false, true })
            {
                IReadOnlyList<CompanionItemDestination> route = CompanionItemRoutingPolicy.GetRoute(
                    useSquadInventory,
                    ownerAvailable);

                Assert.Equal(CompanionItemDestination.Companion, route[0], "Todo item deve tentar primeiro o inventario individual.");
                Assert.Equal(CompanionItemDestination.WorldDrop, route[^1], "World drop deve preservar o restante no fim da rota.");
            }
        }
    }

    private static void CompanionItemRoutingPolicyUsesSquadWhenEnabled()
    {
        CompanionItemDestination[] expected =
        {
            CompanionItemDestination.Companion,
            CompanionItemDestination.Squad,
            CompanionItemDestination.WorldDrop
        };

        Assert.SequenceEqual(
            expected,
            CompanionItemRoutingPolicy.GetRoute(useSquadInventory: true, ownerAvailable: true),
            "Squad ativo nao deve encaminhar o restante ao owner.");
        Assert.SequenceEqual(
            expected,
            CompanionItemRoutingPolicy.GetRoute(useSquadInventory: true, ownerAvailable: false),
            "Squad ativo deve funcionar mesmo sem owner disponivel.");
    }

    private static void CompanionItemRoutingPolicyUsesAvailableOwnerWhenSquadIsDisabled()
    {
        Assert.SequenceEqual(
            new[]
            {
                CompanionItemDestination.Companion,
                CompanionItemDestination.Owner,
                CompanionItemDestination.WorldDrop
            },
            CompanionItemRoutingPolicy.GetRoute(useSquadInventory: false, ownerAvailable: true),
            "Squad desativado deve usar o owner disponivel como fallback.");
        Assert.SequenceEqual(
            new[]
            {
                CompanionItemDestination.Companion,
                CompanionItemDestination.WorldDrop
            },
            CompanionItemRoutingPolicy.GetRoute(useSquadInventory: false, ownerAvailable: false),
            "Squad desativado nao deve incluir um owner indisponivel.");
    }

    private static void CompanionChestRoutingPolicySelectsValidOverride()
    {
        string individualId = Guid.NewGuid().ToString("N");
        string defaultId = Guid.NewGuid().ToString("D");
        CompanionChestDestinationState individual = new()
        {
            LocationName = "Farm",
            TileX = 10,
            TileY = 11,
            ChestId = individualId
        };
        CompanionChestDestinationState ownerDefault = new()
        {
            LocationName = "Shed_1",
            TileX = 3,
            TileY = 4,
            ChestId = defaultId
        };

        Assert.True(
            ReferenceEquals(individual, CompanionChestRoutingPolicy.Select(individual, ownerDefault)),
            "override individual valido deve preceder o default do owner");
        Assert.True(
            CompanionChestRoutingPolicy.RefersToChestId(individual, new Guid(individualId).ToString("D")),
            "formatos equivalentes do mesmo GUID devem resolver a mesma identidade");
        Assert.False(
            CompanionChestRoutingPolicy.RefersTo(individual, "Farm", 99, 99),
            "coordenadas antigas nao devem fingir que o bau ainda esta no mesmo tile");

        individual.ChestId = "not-a-guid";
        Assert.True(
            ReferenceEquals(ownerDefault, CompanionChestRoutingPolicy.Select(individual, ownerDefault)),
            "override invalido deve cair no default valido");
        ownerDefault.LocationName = "";
        Assert.True(
            CompanionChestRoutingPolicy.Select(individual, ownerDefault) is null,
            "sem destino estruturalmente valido o roteamento deve falhar fechado");
    }

    private static void CompanionChestRoutingPolicyRejectsAmbiguousIdentity()
    {
        string id = Guid.NewGuid().ToString("N");
        FakeChestCandidate first = new(id);
        FakeChestCandidate duplicate = new(id);

        Assert.True(
            ReferenceEquals(
                first,
                CompanionChestRoutingPolicy.SelectUnique(new[] { first }, candidate => candidate.ChestId)),
            "uma unica ocorrencia deve ser resolvida");
        Assert.True(
            ReferenceEquals(
                first,
                CompanionChestRoutingPolicy.SelectUnique(new[] { first, first }, candidate => candidate.ChestId)),
            "a mesma instancia observada por dois caminhos nao e ambigua");
        Assert.True(
            CompanionChestRoutingPolicy.SelectUnique(new[] { first, duplicate }, candidate => candidate.ChestId) is null,
            "duas instancias com o mesmo GUID devem ser rejeitadas");
        Assert.True(
            CompanionChestRoutingPolicy.SelectUnique(Array.Empty<FakeChestCandidate>(), candidate => candidate.ChestId) is null,
            "GUID ausente deve manter o fallback atual");
    }

    private static void CompanionChestRoutingPolicyRejectsMissingExpectation()
    {
        string currentId = Guid.NewGuid().ToString("N");

        Assert.False(
            CompanionChestRoutingPolicy.MatchesExpectedIdentity("", currentId),
            "token vazio nao pode autorizar o bau encontrado no tile");
        Assert.False(
            CompanionChestRoutingPolicy.MatchesExpectedIdentity("not-a-guid", currentId),
            "token malformado deve falhar fechado");
        Assert.False(
            CompanionChestRoutingPolicy.MatchesExpectedIdentity(Guid.NewGuid().ToString("N"), currentId),
            "GUID de outro bau deve falhar fechado");
        Assert.True(
            CompanionChestRoutingPolicy.MatchesExpectedIdentity(new Guid(currentId).ToString("D"), currentId),
            "formatos textuais equivalentes devem representar a mesma identidade");
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

    private static void CompanionProfilePolicyMigratesProgressionOnly()
    {
        SquadMemberState legacy = new()
        {
            NpcName = "Abigail",
            OwnerId = 77,
            Level = 8,
            Xp = 1500,
            UnspentSkillPoints = 3,
            BonusLevelTenPointGranted = false,
            UnlockedSkillIds = new List<string> { "wood_1", "mining_1" },
            Inventory = new List<SavedItemStack> { CreateSavedItem(stack: 9, quality: 0) },
            RecentLoot = new List<RecentCompanionLoot>
            {
                new()
                {
                    QualifiedItemId = "(O)388",
                    DisplayName = "Wood",
                    Stack = 4,
                    SourceKey = "lumbering",
                    AddedAtUtcTicks = 123
                }
            }
        };

        CompanionProfileState migrated = CompanionProfilePolicy.MigrateLegacyMember(legacy);
        legacy.UnlockedSkillIds.Add("changed_after_migration");
        legacy.RecentLoot[0].Stack = 99;

        Assert.Equal("Abigail", migrated.NpcName, "NPC do perfil migrado");
        Assert.Equal(8, migrated.Level, "nivel migrado");
        Assert.Equal(1500, migrated.Xp, "XP migrado");
        Assert.Equal(3, migrated.UnspentSkillPoints, "pontos migrados");
        Assert.False(migrated.BonusLevelTenPointGranted, "marco de nivel 10 migrado");
        Assert.SequenceEqual(new[] { "wood_1", "mining_1" }, migrated.UnlockedSkillIds, "skills migradas e destacadas");
        Assert.Equal(4, migrated.RecentLoot[0].Stack, "historico de loot migrado e destacado");
        Assert.True(typeof(CompanionProfileState).GetProperty(nameof(SquadMemberState.OwnerId)) is null, "perfil nao deve persistir ownership");
        Assert.True(typeof(CompanionProfileState).GetProperty(nameof(SquadMemberState.Inventory)) is null, "perfil nao deve persistir inventario");
        Assert.False(legacy.ShouldSerializeLevel(), "nivel legado nao deve voltar ao membro no schema novo");
        Assert.False(legacy.ShouldSerializeRecentLoot(), "loot legado nao deve voltar ao membro no schema novo");
    }

    private static void CompanionProfilePolicyRestoresProgressionOnRecruit()
    {
        CompanionProfileState permanent = CompanionProfilePolicy.Create("Leah");
        SquadMemberState firstRecruitment = new()
        {
            NpcName = "Leah",
            OwnerId = 11,
            Inventory = new List<SavedItemStack> { CreateSavedItem(stack: 5, quality: 0) }
        };
        CompanionProfilePolicy.Attach(firstRecruitment, permanent);
        firstRecruitment.Level = 6;
        firstRecruitment.Xp = 900;
        firstRecruitment.UnspentSkillPoints = 2;
        firstRecruitment.BonusLevelTenPointGranted = true;
        firstRecruitment.UnlockedSkillIds.Add("gathering_1");
        firstRecruitment.RecentLoot.Add(new RecentCompanionLoot { QualifiedItemId = "(O)16", Stack = 1 });

        // A newly recruited membership has new ownership and empty carried-item
        // state, but is attached to the NPC's existing permanent profile.
        SquadMemberState secondRecruitment = new() { NpcName = "Leah", OwnerId = 22 };
        CompanionProfilePolicy.Attach(secondRecruitment, permanent);

        Assert.Equal(22L, secondRecruitment.OwnerId, "ownership pertence ao recrutamento atual");
        Assert.Equal(0, secondRecruitment.Inventory.Count, "inventario antigo nao deve ser duplicado");
        Assert.Equal(6, secondRecruitment.Level, "nivel restaurado");
        Assert.Equal(900, secondRecruitment.Xp, "XP restaurado");
        Assert.Equal(2, secondRecruitment.UnspentSkillPoints, "pontos restaurados");
        Assert.True(secondRecruitment.BonusLevelTenPointGranted, "marco de nivel 10 restaurado");
        Assert.SequenceEqual(new[] { "gathering_1" }, secondRecruitment.UnlockedSkillIds, "skills restauradas");
        Assert.Equal(1, secondRecruitment.RecentLoot.Count, "historico de loot restaurado");
        Assert.True(ReferenceEquals(firstRecruitment.Profile, secondRecruitment.Profile), "recrutamentos devem compartilhar a fonte permanente");
    }

    private static void CompanionStateCopyDeepClonesProfile()
    {
        CompanionProfileState source = new()
        {
            NpcName = "Robin",
            Level = 10,
            Xp = 2500,
            UnspentSkillPoints = 4,
            BonusLevelTenPointGranted = true,
            UnlockedSkillIds = new List<string> { "mining_1", "mining_2" },
            RecentLoot = new List<RecentCompanionLoot>
            {
                new() { QualifiedItemId = "(O)390", DisplayName = "Stone", Stack = 3, SourceKey = "mining", AddedAtUtcTicks = 456 }
            }
        };

        CompanionProfileState clone = CompanionStateCopy.CloneProfile(source);
        source.NpcName = "Changed";
        source.UnlockedSkillIds[0] = "changed";
        source.RecentLoot[0].Stack = 100;

        Assert.Equal("Robin", clone.NpcName, "nome destacado");
        Assert.Equal("mining_1", clone.UnlockedSkillIds[0], "skills destacadas");
        Assert.Equal(3, clone.RecentLoot[0].Stack, "loot destacado");
        Assert.False(ReferenceEquals(source.UnlockedSkillIds, clone.UnlockedSkillIds), "lista de skills precisa ser destacada");
        Assert.False(ReferenceEquals(source.RecentLoot, clone.RecentLoot), "lista de loot precisa ser destacada");
        Assert.False(ReferenceEquals(source.RecentLoot[0], clone.RecentLoot[0]), "entrada de loot precisa ser destacada");
    }

    private static void CompanionEquipmentPolicyScopesKeysByOwnerAndNpc()
    {
        CompanionOperationalProfileKey first = CompanionEquipmentPolicy.CreateKey(1001, "Leah");
        CompanionOperationalProfileKey same = CompanionEquipmentPolicy.CreateKey(1001, "  leAH  ");
        CompanionOperationalProfileKey otherOwner = CompanionEquipmentPolicy.CreateKey(1002, "Leah");

        Assert.Equal(first, same, "a identidade do NPC deve ignorar caixa e espacos externos");
        Assert.NotEqual(first, otherOwner, "o mesmo NPC pertence a perfis operacionais separados por owner");
    }

    private static void CompanionEquipmentPolicyValidatesToolBoundaries()
    {
        Assert.True(CompanionEquipmentPolicy.IsValidUpgradeLevel(0), "upgrade basico valido");
        Assert.True(CompanionEquipmentPolicy.IsValidUpgradeLevel(4), "upgrade iridio valido");
        Assert.False(CompanionEquipmentPolicy.IsValidUpgradeLevel(-1), "upgrade negativo invalido");
        Assert.False(CompanionEquipmentPolicy.IsValidUpgradeLevel(5), "upgrade acima de iridio invalido");
        Assert.Equal(40, CompanionEquipmentPolicy.GetWateringCanCapacity(0), "capacidade basica");
        Assert.Equal(100, CompanionEquipmentPolicy.GetWateringCanCapacity(4), "capacidade de iridio");
        Assert.True(CompanionEquipmentPolicy.IsValidWateringCanState(2, 70), "capacidade inclusiva valida");
        Assert.False(CompanionEquipmentPolicy.IsValidWateringCanState(2, 71), "agua acima da capacidade invalida");
        Assert.False(CompanionEquipmentPolicy.IsValidWateringCanState(2, -1), "agua negativa invalida");
    }

    private static void CompanionEquipmentPolicyFiltersWorkSpecialtiesByTool()
    {
        Assert.True(
            CompanionEquipmentPolicy.CanWorkSpecialty(
                CompanionWorkSpecialty.Wood,
                lumberingEnabled: true,
                miningEnabled: true,
                wateringEnabled: true,
                hasUsableAxe: true,
                hasUsablePickaxe: false,
                hasUsableWateringCan: false),
            "madeira aceita machado");
        Assert.False(
            CompanionEquipmentPolicy.CanWorkSpecialty(
                CompanionWorkSpecialty.Wood,
                lumberingEnabled: true,
                miningEnabled: true,
                wateringEnabled: true,
                hasUsableAxe: false,
                hasUsablePickaxe: true,
                hasUsableWateringCan: true),
            "madeira rejeita ferramentas de outro slot");
        Assert.True(
            CompanionEquipmentPolicy.CanWorkSpecialty(
                CompanionWorkSpecialty.ClearArea,
                lumberingEnabled: true,
                miningEnabled: true,
                wateringEnabled: true,
                hasUsableAxe: false,
                hasUsablePickaxe: true,
                hasUsableWateringCan: false),
            "limpar aceita ao menos uma ferramenta compativel");
        Assert.False(
            CompanionEquipmentPolicy.CanWorkSpecialty(
                CompanionWorkSpecialty.ClearArea,
                lumberingEnabled: true,
                miningEnabled: true,
                wateringEnabled: true,
                hasUsableAxe: false,
                hasUsablePickaxe: false,
                hasUsableWateringCan: true),
            "limpar nao usa regador");
        Assert.False(
            CompanionEquipmentPolicy.CanWorkSpecialty(
                CompanionWorkSpecialty.Watering,
                lumberingEnabled: true,
                miningEnabled: true,
                wateringEnabled: false,
                hasUsableAxe: true,
                hasUsablePickaxe: true,
                hasUsableWateringCan: true),
            "modo desativado ainda bloqueia rega");
    }

    private static void SavedToolStateIsClonedAndTokenizedFaithfully()
    {
        SavedItemStack source = new()
        {
            QualifiedItemId = "(T)WateringCan",
            Stack = 1,
            HasToolData = true,
            ToolUpgradeLevel = 2,
            WateringCanWaterLeft = 37,
            ModData = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["example/tool"] = "state"
            }
        };

        SavedItemStack clone = CompanionStateCopy.CloneItem(source);
        Assert.Equal(2, clone.ToolUpgradeLevel, "upgrade clonado");
        Assert.Equal(37, clone.WateringCanWaterLeft, "agua clonada");
        Assert.True(clone.HasToolData, "marcador de ferramenta clonado");

        clone.WateringCanWaterLeft = 36;
        AssertDifferentTokens(source, clone, "agua restante do regador");
        clone.WateringCanWaterLeft = 37;
        clone.ToolUpgradeLevel = 3;
        AssertDifferentTokens(source, clone, "upgrade da ferramenta");
    }

    private static void CompanionOperationsStateCopyDeepClonesProfile()
    {
        CompanionOperationalProfileState source = new()
        {
            OwnerId = 77,
            NpcName = "Maru",
            Equipment = new CompanionEquipmentState
            {
                Pickaxe = new SavedItemStack
                {
                    QualifiedItemId = "(T)Pickaxe",
                    Stack = 1,
                    HasToolData = true,
                    ToolUpgradeLevel = 3,
                    ModData = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["example/tool"] = "original"
                    }
                }
            },
            Routine = new CompanionRoutineState
            {
                ScheduledDayIndex = 12,
                Hours = new List<CompanionRoutineHourState>
                {
                    new() { Hour = 9, Activity = CompanionRoutineActivity.Mine }
                },
                Execution = new CompanionRoutineExecutionState { AppliedDayIndex = 12 }
            },
            ChestDestination = new CompanionChestDestinationState
            {
                LocationName = "Farm",
                TileX = 4,
                TileY = 5,
                ChestId = Guid.NewGuid().ToString("N")
            }
        };

        CompanionOperationalProfileState clone = CompanionOperationsStateCopy.CloneOperationalProfile(source);
        source.Equipment.Pickaxe!.ToolUpgradeLevel = 1;
        source.Equipment.Pickaxe.ModData["example/tool"] = "changed";
        source.Routine.Hours[0].Activity = CompanionRoutineActivity.Wait;
        source.Routine.Execution.AppliedDayIndex = 99;
        source.ChestDestination!.LocationName = "Changed";

        Assert.Equal(3, clone.Equipment.Pickaxe?.ToolUpgradeLevel, "upgrade destacado");
        Assert.Equal("original", clone.Equipment.Pickaxe?.ModData["example/tool"], "ModData destacado");
        Assert.Equal(CompanionRoutineActivity.Mine, clone.Routine.Hours[0].Activity, "hora destacada");
        Assert.Equal(12, clone.Routine.Execution.AppliedDayIndex, "execucao destacada");
        Assert.Equal("Farm", clone.ChestDestination?.LocationName, "destino destacado");
    }

    private static void CompanionStateCopyDeepClonesNpcCosmetic()
    {
        SavedItemStack sourceHat = CreateSavedItem(
            stack: 1,
            quality: 0,
            ("example.author/style", "original"));
        sourceHat.QualifiedItemId = "(H)0";
        NpcCosmeticState source = new()
        {
            NpcName = "Abigail",
            EquippedHat = sourceHat
        };

        NpcCosmeticState clone = CompanionStateCopy.CloneCosmetic(source);
        source.NpcName = "Changed";
        source.EquippedHat!.QualifiedItemId = "(H)changed";
        source.EquippedHat.ModData["example.author/style"] = "changed";

        Assert.Equal("Abigail", clone.NpcName, "nome do NPC clonado");
        Assert.Equal("(H)0", clone.EquippedHat?.QualifiedItemId, "ID do chapeu clonado");
        Assert.Equal(
            "original",
            clone.EquippedHat?.ModData["example.author/style"],
            "ModData do chapeu clonado");
        Assert.False(ReferenceEquals(source.EquippedHat, clone.EquippedHat), "a pilha cosmetica precisa ser destacada");
        Assert.False(
            ReferenceEquals(source.EquippedHat!.ModData, clone.EquippedHat?.ModData),
            "o ModData cosmetico precisa ser destacado");
    }

    private static void NpcHatRenderPolicyTracksMeasuredWalkingBob()
    {
        Assert.True(
            NpcHatRenderPolicy.IsVanillaWalkingStepFrame(1, 4, 16, 32),
            "primeiro frame de passo deve aceitar medicao");
        Assert.True(
            NpcHatRenderPolicy.IsVanillaWalkingStepFrame(15, 4, 16, 32),
            "ultimo frame vanilla de passo deve aceitar medicao");
        Assert.False(
            NpcHatRenderPolicy.IsVanillaWalkingStepFrame(0, 4, 16, 32),
            "frame par usa a ancora base");
        Assert.False(
            NpcHatRenderPolicy.IsVanillaWalkingStepFrame(17, 4, 16, 32),
            "animacao especial nao deve herdar bob de caminhada");
        Assert.False(
            NpcHatRenderPolicy.IsVanillaWalkingStepFrame(1, 4, 32, 32),
            "spritesheet fora do layout vanilla deve falhar fechada");

        Assert.Equal(1, NpcHatRenderPolicy.GetHeadTopDelta(5, 6), "villager que desce um pixel");
        Assert.Equal(0, NpcHatRenderPolicy.GetHeadTopDelta(7, 7), "George sem bob visual");
        Assert.Equal(2, NpcHatRenderPolicy.GetHeadTopDelta(2, 20), "delta suspeito deve ser limitado");
        Assert.Equal(
            2,
            NpcHatRenderPolicy.FindStableOpaqueTopRow(new[] { 0, 1, 7, 8 }, 3),
            "pixel isolado de cabelo nao deve mascarar a descida da cabeca");
        Assert.Equal(
            1,
            NpcHatRenderPolicy.FindStableOpaqueTopRow(new[] { 0, 1, 0 }, 3),
            "sprite estreito usa o primeiro pixel como fallback");
        Assert.Equal(4f, NpcHatRenderPolicy.ToWorldPixels(1, 1f, 4), "um pixel fonte no zoom vanilla");
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

    private sealed record FakeChestCandidate(string ChestId);
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

    public static void Throws<TException>(Action action, string context)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new TestFailureException(
                $"{context}: esperava {typeof(TException).Name}, recebeu {ex.GetType().Name}.");
        }

        throw new TestFailureException($"{context}: esperava {typeof(TException).Name}, mas nenhuma excecao foi lancada.");
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
