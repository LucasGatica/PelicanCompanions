namespace PelicanCompanions;

internal enum CompanionExpressionKind
{
    Speech,
    PetExpression
}

/// <summary>Defines which companion types may emit authored dialogue.</summary>
internal static class CompanionDialoguePolicy
{
    public static bool CanSpeak(bool isPet)
    {
        return !isPet;
    }

    public static CompanionExpressionKind GetExpressionKind(bool isPet)
    {
        return isPet ? CompanionExpressionKind.PetExpression : CompanionExpressionKind.Speech;
    }
}
