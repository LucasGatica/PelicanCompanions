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
        new("CompanionActionWheelTextLayout preserva e equilibra rotulos", CompanionActionWheelTextLayoutPreservesAndBalancesLabels),
        new("RecruitmentContextPolicy permite recrutamento distante no mesmo mapa", RecruitmentContextPolicyAllowsAnyDistanceOnSameMap),
        new("CompanionDialoguePolicy mantem pets silenciosos", CompanionDialoguePolicyKeepsPetsSilent),
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
            RecruitKey = null!,
            ManualTaskKey = null!,
            OpenSquadInventoryKey = null!,
            TasksToggleKey = null!,
            OpenCompanionPanelKey = null!,
            RecallAllCompanionsKey = null!
        };

        config.Validate();

        Assert.NotNull(config.QuickActionWheelKey, nameof(config.QuickActionWheelKey));
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
            CompanionQuickHudMaxRows = 99
        };

        config.Validate();

        Assert.Equal(14, config.FriendshipRequirement, nameof(config.FriendshipRequirement));
        Assert.Equal(12, config.MaxSquadSize, nameof(config.MaxSquadSize));
        Assert.Equal(10, config.CompanionInventorySlots, nameof(config.CompanionInventorySlots));
        Assert.Equal(20, config.CompanionWorkRadius, nameof(config.CompanionWorkRadius));
        Assert.Equal(40, config.CompanionWorkReturnDistance, nameof(config.CompanionWorkReturnDistance));
        Assert.Equal(12, config.CompanionQuickHudMaxRows, nameof(config.CompanionQuickHudMaxRows));

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
