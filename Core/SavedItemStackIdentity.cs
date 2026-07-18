using System.Security.Cryptography;
using System.Text;

namespace PelicanCompanions;

/// <summary>Creates a stable identity token for a serialized item stack.</summary>
/// <remarks>
/// A qualified item ID alone is insufficient for a remote withdrawal: quality,
/// preserved parent, color, stack size, and mod data can all distinguish two
/// visible stacks. The token intentionally includes every persisted property and
/// orders mod-data keys so serialization order cannot change the result.
/// </remarks>
internal static class SavedItemStackIdentity
{
    public static string CreateToken(SavedItemStack item)
    {
        ArgumentNullException.ThrowIfNull(item);

        StringBuilder canonical = new();
        Append(canonical, item.QualifiedItemId);
        Append(canonical, item.Stack.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(canonical, item.Quality.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(canonical, item.PreservedParentItemId);
        Append(canonical, item.HasColor ? "1" : "0");
        Append(canonical, item.ColorR.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(canonical, item.ColorG.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(canonical, item.ColorB.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(canonical, item.ColorA.ToString(System.Globalization.CultureInfo.InvariantCulture));

        foreach ((string key, string value) in (item.ModData ?? new Dictionary<string, string>())
            .OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            Append(canonical, key);
            Append(canonical, value);
        }

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(digest);
    }

    public static bool Matches(SavedItemStack item, string? token)
    {
        return !string.IsNullOrWhiteSpace(token)
            && string.Equals(CreateToken(item), token, StringComparison.Ordinal);
    }

    private static void Append(StringBuilder target, string? value)
    {
        if (value is null)
        {
            target.Append("-1:");
            return;
        }

        target.Append(value.Length)
            .Append(':')
            .Append(value);
    }
}
