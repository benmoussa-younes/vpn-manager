using System.Diagnostics.CodeAnalysis;

namespace VpnManager.Core.Validation;

/// <summary>
/// The tunnel name is the configuration file stem and the kernel interface
/// name, so it must satisfy what <c>wg-quick</c> and Linux accept.
/// </summary>
public static class TunnelNameRules
{
    /// <summary>The longest accepted name: <c>IFNAMSIZ - 1</c>.</summary>
    public const int MaximumLength = 15;

    /// <summary>
    /// Decides whether the name is 1 to 15 characters of
    /// <c>[A-Za-z0-9_=+.-]</c>, the set <c>wg-quick</c> accepts.
    /// </summary>
    /// <param name="name">The candidate name.</param>
    /// <returns><see langword="true"/> when the name is usable.</returns>
    public static bool IsValid([NotNullWhen(true)] string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaximumLength)
        {
            return false;
        }

        // A leading '-' would be read as an option by wg and wg-quick, which take
        // the name as a positional argument.
        if (name[0] == '-')
        {
            return false;
        }

        foreach (var character in name)
        {
            var allowed = char.IsAsciiLetterOrDigit(character) || character is '_' or '=' or '+' or '.' or '-';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Decides whether the name may be used as a configuration file stem: it must
    /// satisfy <see cref="IsValid"/> and must not carry the <c>..</c> traversal
    /// marker, which the character rules allow but which has no place in a path a
    /// root helper builds.
    /// </summary>
    /// <param name="name">The candidate name.</param>
    /// <returns><see langword="true"/> when the name is confined and usable.</returns>
    public static bool IsConfined([NotNullWhen(true)] string? name) =>
        IsValid(name) && !name.Contains("..", StringComparison.Ordinal);
}
