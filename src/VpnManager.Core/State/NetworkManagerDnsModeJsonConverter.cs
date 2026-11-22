using System.Text.Json;
using System.Text.Json.Serialization;
using VpnManager.Core.NetworkManager;

namespace VpnManager.Core.State;

/// <summary>
/// Reads a stored DNS mode by name or number and maps a mode the product no longer
/// offers — the retired full-tunnel mode — to <see cref="NetworkManagerDnsMode.Profile"/>,
/// its historical behaviour. Without this, one retired value fails the whole state
/// document and every later write reports the file as unreadable.
/// </summary>
public sealed class NetworkManagerDnsModeJsonConverter : JsonConverter<NetworkManagerDnsMode>
{
    /// <inheritdoc />
    public override NetworkManagerDnsMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String when TryParseName(reader.GetString(), out var named) => named,
            JsonTokenType.Number when reader.TryGetInt32(out var number) && Enum.IsDefined((NetworkManagerDnsMode)number) => (NetworkManagerDnsMode)number,
            _ => NetworkManagerDnsMode.Profile,
        };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, NetworkManagerDnsMode value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.ToString());
    }

    private static bool TryParseName(string? text, out NetworkManagerDnsMode mode)
    {
        if (Enum.TryParse(text, ignoreCase: true, out mode) && Enum.IsDefined(mode))
        {
            return true;
        }

        mode = NetworkManagerDnsMode.Profile;
        return false;
    }
}
