namespace PelicanCompanions;

/// <summary>The task-planning lane selected after applying the hourly routine.</summary>
internal enum CompanionRoutinePlanningLane
{
    None = 0,
    ExplicitDirective = 1,
    ConfiguredAutonomy = 2
}

/// <summary>Pure clock and normalization rules for the RimWorld-style daily routine grid.</summary>
internal static class CompanionRoutinePolicy
{
    public const int FirstHour = 6;
    public const int LastHour = 25;
    public const int HourCount = LastHour - FirstHour + 1;
    private const string ConfigurationPayloadVersion = "2";
    private const int MaximumEncodedLocationNameBytes = 1024;

    public static IReadOnlyList<CompanionRoutineHourState> NormalizeHours(
        IEnumerable<CompanionRoutineHourState>? incoming)
    {
        Dictionary<int, CompanionRoutineActivity> selected = new();
        foreach (CompanionRoutineHourState? hour in incoming ?? Enumerable.Empty<CompanionRoutineHourState>())
        {
            if (hour is null
                || hour.Hour is < FirstHour or > LastHour
                || !Enum.IsDefined(hour.Activity))
            {
                continue;
            }

            selected[hour.Hour] = hour.Activity;
        }

        return Enumerable.Range(FirstHour, HourCount)
            .Select(hour => new CompanionRoutineHourState
            {
                Hour = hour,
                Activity = selected.TryGetValue(hour, out CompanionRoutineActivity activity)
                    ? activity
                    : CompanionRoutineActivity.Follow
            })
            .ToList();
    }

    public static CompanionRoutineActivity GetActivity(
        IEnumerable<CompanionRoutineHourState>? hours,
        int timeOfDay)
    {
        int hour = Math.Clamp(timeOfDay / 100, FirstHour, LastHour);
        CompanionRoutineHourState? selected = hours?
            .LastOrDefault(entry => entry is not null && entry.Hour == hour && Enum.IsDefined(entry.Activity));
        return selected?.Activity ?? CompanionRoutineActivity.Follow;
    }

    public static int GetBlockStartHour(
        IEnumerable<CompanionRoutineHourState>? hours,
        int timeOfDay)
    {
        IReadOnlyList<CompanionRoutineHourState> normalized = NormalizeHours(hours);
        int hour = Math.Clamp(timeOfDay / 100, FirstHour, LastHour);
        CompanionRoutineActivity activity = normalized[hour - FirstHour].Activity;
        while (hour > FirstHour
            && normalized[hour - FirstHour - 1].Activity == activity)
        {
            hour--;
        }

        return hour;
    }

    public static bool ShouldRun(CompanionRoutineState? routine, int dayIndex)
    {
        return routine?.Enabled == true
            && (routine.RepeatDaily || routine.ScheduledDayIndex == dayIndex);
    }

    /// <summary>
    /// Select the only planning lane allowed for this update. Explicit player
    /// directives may override a block, but an active routine without one owns
    /// the companion and must not fall through to generic autonomy.
    /// </summary>
    public static CompanionRoutinePlanningLane SelectPlanningLane(
        CompanionRoutineState? routine,
        int dayIndex,
        bool hasExplicitDirective)
    {
        if (hasExplicitDirective)
            return CompanionRoutinePlanningLane.ExplicitDirective;
        return ShouldRun(routine, dayIndex)
            ? CompanionRoutinePlanningLane.None
            : CompanionRoutinePlanningLane.ConfiguredAutonomy;
    }

    public static CompanionRoutineActivity GetCompletionActivity(
        CompanionRoutineCompletionBehavior behavior)
    {
        return behavior switch
        {
            CompanionRoutineCompletionBehavior.Wait => CompanionRoutineActivity.Wait,
            CompanionRoutineCompletionBehavior.VanillaRoutine => CompanionRoutineActivity.VanillaRoutine,
            _ => CompanionRoutineActivity.Follow
        };
    }

    /// <summary>Whether the companion's durable mode already represents this non-operational routine activity.</summary>
    public static bool IsActivityModeActive(
        CompanionRoutineActivity activity,
        CompanionMode mode)
    {
        return activity switch
        {
            CompanionRoutineActivity.Follow => mode == CompanionMode.Following,
            CompanionRoutineActivity.Wait => mode == CompanionMode.Waiting,
            CompanionRoutineActivity.VanillaRoutine => mode == CompanionMode.OriginalRoutine,
            _ => false
        };
    }

    public static bool ShouldApplyCompletionAfterEdit(
        CompanionRoutineState? current,
        CompanionRoutineState? edited)
    {
        return current?.Enabled == true && edited?.Enabled != true;
    }

    public static bool TryGetWorkSpecialty(
        CompanionRoutineActivity activity,
        out CompanionWorkSpecialty specialty)
    {
        specialty = activity switch
        {
            CompanionRoutineActivity.Water => CompanionWorkSpecialty.Watering,
            CompanionRoutineActivity.Lumber => CompanionWorkSpecialty.Wood,
            CompanionRoutineActivity.Mine => CompanionWorkSpecialty.Mining,
            CompanionRoutineActivity.Clear => CompanionWorkSpecialty.ClearArea,
            _ => CompanionWorkSpecialty.ClearArea
        };
        return activity is CompanionRoutineActivity.Water
            or CompanionRoutineActivity.Lumber
            or CompanionRoutineActivity.Mine
            or CompanionRoutineActivity.Clear;
    }

    public static bool IsValidAreaPreset(CompanionRoutineAreaPreset? area)
    {
        return area is not null
            && Enum.IsDefined(area.Specialty)
            && CompanionWorkAreaPolicy.IsRoutineRegionValid(
                area.RegionKind,
                area.LocationName,
                area.CenterX,
                area.CenterY,
                area.Radius,
                area.MinX,
                area.MinY,
                area.Size);
    }

    public static IReadOnlyList<CompanionRoutineAreaPreset> NormalizeAreaPresets(
        IEnumerable<CompanionRoutineAreaPreset>? incoming)
    {
        Dictionary<CompanionWorkSpecialty, CompanionRoutineAreaPreset> selected = new();
        foreach (CompanionRoutineAreaPreset? area in incoming ?? Enumerable.Empty<CompanionRoutineAreaPreset>())
        {
            if (!IsValidAreaPreset(area))
                continue;

            selected[area!.Specialty] = new CompanionRoutineAreaPreset
            {
                Specialty = area.Specialty,
                RegionKind = area.RegionKind,
                LocationName = area.LocationName,
                CenterX = area.CenterX,
                CenterY = area.CenterY,
                Radius = area.Radius,
                MinX = area.MinX,
                MinY = area.MinY,
                Size = area.Size
            };
        }

        return selected.Values.OrderBy(area => area.Specialty).ToList();
    }

    public static CompanionRoutineAreaPreset? GetAreaPreset(
        CompanionRoutineState? routine,
        CompanionWorkSpecialty specialty)
    {
        return NormalizeAreaPresets(routine?.AreaPresets)
            .FirstOrDefault(area => area.Specialty == specialty);
    }

    public static void UpsertAreaPreset(
        CompanionRoutineState routine,
        CompanionRoutineAreaPreset preset)
    {
        ArgumentNullException.ThrowIfNull(routine);
        ArgumentNullException.ThrowIfNull(preset);
        if (!IsValidAreaPreset(preset))
            throw new ArgumentException("The routine area preset is invalid.", nameof(preset));

        List<CompanionRoutineAreaPreset> normalized = NormalizeAreaPresets(routine.AreaPresets)
            .Where(area => area.Specialty != preset.Specialty)
            .ToList();
        normalized.Add(new CompanionRoutineAreaPreset
        {
            Specialty = preset.Specialty,
            RegionKind = preset.RegionKind,
            LocationName = preset.LocationName,
            CenterX = preset.CenterX,
            CenterY = preset.CenterY,
            Radius = preset.Radius,
            MinX = preset.MinX,
            MinY = preset.MinY,
            Size = preset.Size
        });
        routine.AreaPresets = normalized.OrderBy(area => area.Specialty).ToList();
    }

    public static void RemoveAreaPreset(
        CompanionRoutineState routine,
        CompanionWorkSpecialty specialty)
    {
        ArgumentNullException.ThrowIfNull(routine);
        routine.AreaPresets = NormalizeAreaPresets(routine.AreaPresets)
            .Where(area => area.Specialty != specialty)
            .ToList();
    }

    public static void ApplyWorkUntilSixPm(
        CompanionRoutineState routine,
        CompanionRoutineActivity workActivity)
    {
        ArgumentNullException.ThrowIfNull(routine);
        if (!TryGetWorkSpecialty(workActivity, out _))
            workActivity = CompanionRoutineActivity.Clear;

        CompanionRoutineActivity afterWork = GetCompletionActivity(routine.CompletionBehavior);
        routine.Hours = Enumerable.Range(FirstHour, HourCount)
            .Select(hour => new CompanionRoutineHourState
            {
                Hour = hour,
                Activity = hour < 18 ? workActivity : afterWork
            })
            .ToList();
        routine.Enabled = true;
    }

    /// <summary>Paint one cell and make the edited schedule immediately executable when saved.</summary>
    public static void PaintHour(
        CompanionRoutineState routine,
        int hour,
        CompanionRoutineActivity activity)
    {
        ArgumentNullException.ThrowIfNull(routine);
        if (hour is < FirstHour or > LastHour)
            throw new ArgumentOutOfRangeException(nameof(hour));
        if (!Enum.IsDefined(activity))
            throw new ArgumentOutOfRangeException(nameof(activity));

        List<CompanionRoutineHourState> normalized = NormalizeHours(routine.Hours).ToList();
        normalized[hour - FirstHour].Activity = activity;
        routine.Hours = normalized;
        routine.Enabled = true;
    }

    public static string Encode(CompanionRoutineState? routine)
    {
        routine ??= new CompanionRoutineState();
        IReadOnlyList<CompanionRoutineHourState> normalized = NormalizeHours(routine.Hours);
        string activities = string.Join(',', normalized.Select(hour => ((int)hour.Activity).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        string areas = string.Join(';', NormalizeAreaPresets(routine.AreaPresets).Select(EncodeAreaPreset));
        return string.Join('|',
            ConfigurationPayloadVersion,
            routine.Enabled ? "1" : "0",
            routine.RepeatDaily ? "1" : "0",
            ((int)(Enum.IsDefined(routine.CompletionBehavior)
                ? routine.CompletionBehavior
                : CompanionRoutineCompletionBehavior.Follow)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            activities,
            areas);
    }

    public static bool TryDecode(string? encoded, out CompanionRoutineState routine)
    {
        routine = new CompanionRoutineState();
        string[] parts = (encoded ?? "").Split('|');
        bool legacyPayload = parts.Length == 4;
        int offset = legacyPayload ? 0 : 1;
        if ((!legacyPayload && (parts.Length != 6 || parts[0] != ConfigurationPayloadVersion))
            || parts[offset] is not ("0" or "1")
            || parts[offset + 1] is not ("0" or "1"))
        {
            return false;
        }
        if (!int.TryParse(parts[offset + 2], out int rawCompletion)
            || !Enum.IsDefined(typeof(CompanionRoutineCompletionBehavior), rawCompletion))
        {
            return false;
        }

        string[] rawActivities = parts[offset + 3].Split(',');
        if (rawActivities.Length != HourCount)
            return false;
        List<CompanionRoutineHourState> hours = new(HourCount);
        for (int index = 0; index < rawActivities.Length; index++)
        {
            if (!int.TryParse(rawActivities[index], out int rawActivity)
                || !Enum.IsDefined(typeof(CompanionRoutineActivity), rawActivity))
            {
                return false;
            }
            hours.Add(new CompanionRoutineHourState
            {
                Hour = FirstHour + index,
                Activity = (CompanionRoutineActivity)rawActivity
            });
        }

        List<CompanionRoutineAreaPreset> areas = new();
        if (!legacyPayload && !string.IsNullOrWhiteSpace(parts[5]))
        {
            string[] rawAreas = parts[5].Split(';');
            if (rawAreas.Length > Enum.GetValues<CompanionWorkSpecialty>().Length)
                return false;
            foreach (string rawArea in rawAreas)
            {
                if (!TryDecodeAreaPreset(rawArea, out CompanionRoutineAreaPreset area))
                    return false;
                areas.Add(area);
            }

            areas = NormalizeAreaPresets(areas).ToList();
            if (areas.Count != rawAreas.Length)
                return false;
        }

        routine = new CompanionRoutineState
        {
            Enabled = parts[offset] == "1",
            RepeatDaily = parts[offset + 1] == "1",
            CompletionBehavior = (CompanionRoutineCompletionBehavior)rawCompletion,
            Hours = hours,
            AreaPresets = areas
        };
        return true;
    }

    public static bool HasAreaConfigurationPayload(string? encoded)
    {
        return (encoded ?? "").StartsWith(ConfigurationPayloadVersion + "|", StringComparison.Ordinal);
    }

    private static string EncodeAreaPreset(CompanionRoutineAreaPreset area)
    {
        string location = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(area.LocationName));
        return string.Join(',',
            ((int)area.Specialty).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)area.RegionKind).ToString(System.Globalization.CultureInfo.InvariantCulture),
            location,
            area.CenterX.ToString(System.Globalization.CultureInfo.InvariantCulture),
            area.CenterY.ToString(System.Globalization.CultureInfo.InvariantCulture),
            area.Radius.ToString(System.Globalization.CultureInfo.InvariantCulture),
            area.MinX.ToString(System.Globalization.CultureInfo.InvariantCulture),
            area.MinY.ToString(System.Globalization.CultureInfo.InvariantCulture),
            area.Size.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static bool TryDecodeAreaPreset(
        string encoded,
        out CompanionRoutineAreaPreset area)
    {
        area = new CompanionRoutineAreaPreset();
        string[] parts = encoded.Split(',');
        if (parts.Length != 9
            || !int.TryParse(parts[0], out int rawSpecialty)
            || !Enum.IsDefined(typeof(CompanionWorkSpecialty), rawSpecialty)
            || !int.TryParse(parts[1], out int rawRegionKind)
            || !Enum.IsDefined(typeof(CompanionWorkRegionKind), rawRegionKind)
            || !int.TryParse(parts[3], out int centerX)
            || !int.TryParse(parts[4], out int centerY)
            || !int.TryParse(parts[5], out int radius)
            || !int.TryParse(parts[6], out int minX)
            || !int.TryParse(parts[7], out int minY)
            || !int.TryParse(parts[8], out int size))
        {
            return false;
        }

        string locationName;
        try
        {
            byte[] locationBytes = Convert.FromBase64String(parts[2]);
            if (locationBytes.Length is 0 or > MaximumEncodedLocationNameBytes)
                return false;
            locationName = new System.Text.UTF8Encoding(false, true).GetString(locationBytes);
        }
        catch (Exception)
        {
            return false;
        }

        CompanionRoutineAreaPreset decoded = new()
        {
            Specialty = (CompanionWorkSpecialty)rawSpecialty,
            RegionKind = (CompanionWorkRegionKind)rawRegionKind,
            LocationName = locationName,
            CenterX = centerX,
            CenterY = centerY,
            Radius = radius,
            MinX = minX,
            MinY = minY,
            Size = size
        };
        if (!IsValidAreaPreset(decoded))
            return false;

        area = decoded;
        return true;
    }

    public static string CreateStateToken(CompanionRoutineState? routine)
    {
        long revision = Math.Max(0, routine?.Revision ?? 0);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(
            $"{revision.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{Encode(routine)}");
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    public static bool IsAppliedBlock(
        CompanionRoutineState routine,
        int dayIndex,
        int blockStartHour)
    {
        CompanionRoutineExecutionState execution = routine.Execution ??= new CompanionRoutineExecutionState();
        return execution.AppliedDayIndex == dayIndex
            && execution.AppliedBlockHour == blockStartHour
            && execution.AppliedRevision == routine.Revision;
    }

    public static bool IsCompletedBlock(
        CompanionRoutineState routine,
        int dayIndex,
        int blockStartHour)
    {
        CompanionRoutineExecutionState execution = routine.Execution ??= new CompanionRoutineExecutionState();
        return execution.CompletedDayIndex == dayIndex
            && execution.CompletedBlockHour == blockStartHour
            && execution.AppliedRevision == routine.Revision;
    }

    /// <summary>
    /// Whether an already claimed work/deposit block still needs activation.
    /// A live explicit order temporarily wins, and a running routine area must
    /// not be recreated on every refresh.
    /// </summary>
    public static bool ShouldRetryAppliedOperationalBlock(
        CompanionRoutineState routine,
        int dayIndex,
        int blockStartHour,
        CompanionRoutineActivity activity,
        bool hasExplicitOverride,
        bool hasActiveRoutineWorkArea)
    {
        ArgumentNullException.ThrowIfNull(routine);
        if (!IsAppliedBlock(routine, dayIndex, blockStartHour)
            || IsCompletedBlock(routine, dayIndex, blockStartHour)
            || hasExplicitOverride)
        {
            return false;
        }

        if (activity == CompanionRoutineActivity.Deposit)
            return true;
        return TryGetWorkSpecialty(activity, out _)
            && !hasActiveRoutineWorkArea;
    }

    public static void MarkApplied(
        CompanionRoutineState routine,
        int dayIndex,
        int blockStartHour)
    {
        CompanionRoutineExecutionState execution = routine.Execution ??= new CompanionRoutineExecutionState();
        execution.AppliedDayIndex = dayIndex;
        execution.AppliedBlockHour = blockStartHour;
        execution.AppliedRevision = routine.Revision;
    }

    public static void MarkCompleted(
        CompanionRoutineState routine,
        int dayIndex,
        int blockStartHour)
    {
        MarkApplied(routine, dayIndex, blockStartHour);
        routine.Execution.CompletedDayIndex = dayIndex;
        routine.Execution.CompletedBlockHour = blockStartHour;
    }

    public static void ResetExecution(CompanionRoutineState routine)
    {
        ArgumentNullException.ThrowIfNull(routine);
        routine.Execution = new CompanionRoutineExecutionState();
    }
}
