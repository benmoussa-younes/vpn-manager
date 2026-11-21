using System.Text;
using Tmds.DBus.Protocol;
using VpnManager.App.Cli;
using VpnManager.App.Privileges;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Services;
using VpnManager.Core.State;

namespace VpnManager.App.DBus;

/// <summary>
/// The user-session D-Bus surface for enterprise orchestration. It exposes the
/// same headless operations as the CLI; it never runs on the system bus and never
/// requests privilege.
/// </summary>
internal sealed class VpnManagerDbusService : IPathMethodHandler, IAsyncDisposable
{
    internal const string ServiceName = "com.bit-crafts.VpnManager";
    internal const string ObjectPath = "/com/bit_crafts/VpnManager";
    internal const string InterfaceName = "com.bit_crafts.VpnManager1";

    internal const string ErrorName = "com.bit_crafts.VpnManager1.Failed";
    private const string IntrospectionXml = """
        <interface name="com.bit_crafts.VpnManager1">
          <method name="GetStatus"><arg type="s" direction="out"/></method>
          <method name="Version"><arg type="s" direction="out"/></method>
          <method name="Connect"><arg type="s" direction="in"/><arg type="s" direction="out"/></method>
          <method name="Disconnect"><arg type="s" direction="in"/><arg type="s" direction="out"/></method>
          <method name="SetPolicy">
            <arg type="s" direction="in"/><arg type="b" direction="in"/>
            <arg type="s" direction="in"/><arg type="s" direction="in"/>
            <arg type="s" direction="out"/>
          </method>
        </interface>
        """;

    private readonly TunnelService _service;
    private readonly CliRunner _cli;
    private DBusConnection? _connection;
    private Task<Exception?>? _disconnected;

    internal VpnManagerDbusService(
        TunnelService service,
        string configDirectory,
        IRootActionService? rootActions = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);

        _service = service;
        _cli = new CliRunner(service, configDirectory, rootActions);
    }

    /// <inheritdoc />
    public string Path => ObjectPath;

    /// <inheritdoc />
    public bool HandlesChildPaths => false;

    /// <summary>
    /// Gets a task that completes when the session bus connection ends, so the
    /// process exits with the session instead of lingering without a bus.
    /// </summary>
    public Task<Exception?> Disconnected => _disconnected ?? Task.FromResult<Exception?>(null);

    /// <summary>Connects to the session bus and tries to own the well-known name.</summary>
    /// <param name="cancellationToken">Cancels the connection.</param>
    /// <returns><see langword="true"/> when this process owns the name; a second
    /// instance must exit instead of serving requests nobody will route to it.</returns>
    public async Task<bool> TryStartAsync(CancellationToken cancellationToken)
    {
        var address = DBusAddress.Session ?? throw new InvalidOperationException("No session D-Bus address is configured.");
        var connection = new DBusConnection(address);
        await connection.ConnectAsync().ConfigureAwait(false);
        if (!await connection.TryRequestNameAsync(ServiceName, RequestNameOptions.None).ConfigureAwait(false))
        {
            connection.Dispose();
            return false;
        }

        connection.AddMethodHandler(this);
        _connection = connection;
        _disconnected = connection.DisconnectedAsync();
        return true;
    }

    /// <summary>Waits until the cancellation token is cancelled.</summary>
    /// <param name="cancellationToken">The service lifetime.</param>
    public static Task RunAsync(CancellationToken cancellationToken) =>
        Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

    /// <inheritdoc />
    public async ValueTask HandleMethodAsync(MethodContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await DispatchAsync(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The caller cancelled or went away; there is nobody to answer.
        }
        catch (Exception exception)
        {
            // A body that does not match the signature, or any unexpected
            // failure, must answer with an error instead of tearing the session
            // service down: one bad call would otherwise stop every client.
            TryReplyError(context, exception);
        }
    }

    private async Task DispatchAsync(MethodContext context)
    {
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml(
                new ReadOnlyMemory<byte>[] { Encoding.UTF8.GetBytes(IntrospectionXml) },
                Array.Empty<string>());
            return;
        }

        var member = context.Request.MemberAsString;
        var reader = context.Request.GetBodyReader();

        switch (member)
        {
            case "GetStatus":
                Reply(context, await StatusJsonAsync(context.RequestAborted).ConfigureAwait(false));
                return;
            case "Version":
                Reply(context, Program.Version);
                return;
            case "Connect":
                Reply(context, await ConnectAsync(reader.ReadString(), context.RequestAborted).ConfigureAwait(false));
                return;
            case "Disconnect":
                Reply(context, await DisconnectAsync(reader.ReadString(), context.RequestAborted).ConfigureAwait(false));
                return;
            case "SetPolicy":
                Reply(context, await SetPolicyAsync(
                    reader.ReadString(),
                    reader.ReadBool(),
                    reader.ReadString(),
                    reader.ReadString(),
                    context.RequestAborted).ConfigureAwait(false));
                return;
            default:
                context.ReplyUnknownMethodError();
                return;
        }
    }

    private async Task<string> StatusJsonAsync(CancellationToken cancellationToken)
    {
        using var output = new StringWriter();
        var code = await _cli
            .RunAsync(new CliOptions(CliCommand.Status, Json: true), output, TextWriter.Null, cancellationToken)
            .ConfigureAwait(false);
        return code == 0 ? output.ToString() : string.Empty;
    }

    private async Task<string> ConnectAsync(string name, CancellationToken cancellationToken)
    {
        var result = await _service.StartAsync(name, cancellationToken).ConfigureAwait(false);
        return result.TryGetValue(out var value) ? value : result.Error.Message;
    }

    private async Task<string> DisconnectAsync(string name, CancellationToken cancellationToken)
    {
        var result = await _service.StopAsync(name, cancellationToken).ConfigureAwait(false);
        return result.TryGetValue(out var value) ? value : result.Error.Message;
    }

    private async Task<string> SetPolicyAsync(
        string name,
        bool autoReconnect,
        string modeText,
        string searchText,
        CancellationToken cancellationToken)
    {
        var mode = modeText.ToLowerInvariant() switch
        {
            "profile" => NetworkManagerDnsMode.Profile,
            "system" => NetworkManagerDnsMode.System,
            "split" => NetworkManagerDnsMode.Split,
            "full" or "full-tunnel" or "fulltunnel" => NetworkManagerDnsMode.FullTunnel,
            _ => (NetworkManagerDnsMode?)null,
        };

        if (mode is null)
        {
            return $"Unknown DNS mode '{modeText}'.";
        }

        var search = searchText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var policy = new TunnelPolicy(autoReconnect, mode.Value, search);
        var result = await _service.SetPolicyAsync(name, policy, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? "ok" : result.Error.Message;
    }

    private static void TryReplyError(MethodContext context, Exception exception)
    {
        try
        {
            context.ReplyError(ErrorName, exception.Message);
        }
        catch (Exception)
        {
            // The connection is already unusable; there is nothing left to answer.
        }
    }

    private static void Reply(MethodContext context, string value)
    {
        using var writer = context.CreateReplyWriter("s");
        writer.WriteString(value);
        context.Reply(writer.CreateMessage());
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_connection is { } connection)
        {
            _connection = null;
            connection.Dispose();
        }
    }
}
