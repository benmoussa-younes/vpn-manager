namespace VpnManager.Core.Model;

/// <summary>
/// A WireGuard key whose raw value must not be displayed or logged. Public keys
/// are carried in the same type so that no key is ever a bare
/// <see cref="string"/> that could reach a log line or a view by accident.
/// </summary>
/// <remarks>
/// <see cref="ToString"/> and string interpolation are redacted. The raw value
/// has exactly one exit, <see cref="Reveal"/>, which the configuration writer
/// uses. A test asserts that no presentation or view file calls it.
/// </remarks>
public sealed class SecretKey : IEquatable<SecretKey>
{
    /// <summary>The absent key.</summary>
    public static readonly SecretKey Empty = new(string.Empty);

    private readonly string _value;

    private SecretKey(string value) => _value = value;

    /// <summary>Wraps a key value exactly as given; no validation happens here.</summary>
    /// <param name="value">The key text, or <see langword="null"/> for the empty key.</param>
    /// <returns>The wrapper.</returns>
    public static SecretKey FromValue(string? value) => value is null ? Empty : new SecretKey(value);

    /// <summary>Gets a value indicating whether no key is present.</summary>
    public bool IsEmpty => _value.Length == 0;

    /// <summary>
    /// Returns the raw key material. This is the only way to read it, and only
    /// the configuration writer is expected to call it.
    /// </summary>
    /// <returns>The raw key text.</returns>
    public string Reveal() => _value;

    /// <inheritdoc />
    public bool Equals(SecretKey? other) => other is not null && string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SecretKey);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value);

    /// <summary>Always redacted: a key never appears in a log line.</summary>
    /// <returns><c>[redacted]</c>, or <c>(none)</c> when empty.</returns>
    public override string ToString() => IsEmpty ? "(none)" : "[redacted]";
}
