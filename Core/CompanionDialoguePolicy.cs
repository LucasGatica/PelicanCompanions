namespace PelicanCompanions;

/// <summary>Defines which companion types may emit authored dialogue.</summary>
internal static class CompanionDialoguePolicy
{
    public static bool CanSpeak(bool isPet)
    {
        return !isPet;
    }
}
