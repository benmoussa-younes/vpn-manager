using System.Diagnostics.CodeAnalysis;

namespace VpnManager.Core.Validation;

/// <summary>
/// WireGuard key shape: 32 bytes, base64 encoded. Both the private key and the
/// preshared key use this shape; the public key is derived from the private key.
/// </summary>
public static class WireGuardKey
{
    /// <summary>The raw key length in bytes.</summary>
    public const int KeyBytes = 32;

    /// <summary>The base64 length of a key, including one <c>=</c> of padding.</summary>
    public const int EncodedLength = 44;

    /// <summary>
    /// Decides whether the text is a well-formed key: exactly 44 base64
    /// characters decoding to exactly 32 bytes.
    /// </summary>
    /// <param name="value">The candidate text.</param>
    /// <returns><see langword="true"/> when the shape is valid.</returns>
    public static bool IsValid([NotNullWhen(true)] string? value)
    {
        if (value is null || value.Length != EncodedLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            var isBase64 = char.IsAsciiLetterOrDigit(character) || character is '+' or '/' or '=';
            if (!isBase64)
            {
                return false;
            }
        }

        Span<byte> decoded = stackalloc byte[KeyBytes + 4];
        return Convert.TryFromBase64String(value, decoded, out var written) && written == KeyBytes;
    }
}
