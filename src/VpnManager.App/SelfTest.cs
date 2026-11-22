using Core.Execution;
using Core.Paths;
using VpnManager.Core.Configuration;
using VpnManager.Core.Validation;

namespace VpnManager.App;

/// <summary>
/// The display-free claims the published Native AOT binary proves with
/// <c>--self-test</c>: the parser, the validator, the path identity, the launch
/// options and the process redaction. It needs no display, no network and no
/// writable path outside <c>/tmp</c>.
/// </summary>
internal static class SelfTest
{
    internal static int Run(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var failures = 0;
        Check(output, "version is not empty", !string.IsNullOrWhiteSpace(Program.Version), ref failures);
        Check(output, "launch option is parsed", LaunchOptions.ReadValue(["--config-dir", "/tmp/tunnels"], LaunchOptions.ConfigDirectoryFlag) == "/tmp/tunnels", ref failures);
        Check(output, "read-only flag is parsed", LaunchOptions.IsReadOnly(["--read-only"]), ref failures);

        Check(output, "tunnel name accepts wg0", TunnelNameRules.IsValid("wg0"), ref failures);
        Check(output, "tunnel name refuses a leading dash", !TunnelNameRules.IsValid("-wg0"), ref failures);
        Check(output, "tunnel name refuses a space", !TunnelNameRules.IsValid("wg 0"), ref failures);

        var privateKey = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray());
        var peerKey = Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray());
        var text = string.Concat(
            "[Interface]\n",
            "PrivateKey = ", privateKey, "\n",
            "Address = 10.10.0.2/24\n",
            "DNS = 10.10.0.1\n",
            "[Peer]\n",
            "PublicKey = ", peerKey, "\n",
            "AllowedIPs = 0.0.0.0/0\n",
            "Endpoint = vpn.example.com:51820\n");

        var parsed = WireGuardConfigParser.Parse("wg0", text);
        Check(output, "parser reads one peer", parsed.TryGetValue(out var configuration) && configuration.Peers.Count == 1, ref failures);

        if (parsed.TryGetValue(out var value))
        {
            var validated = TunnelValidator.Validate(value);
            Check(output, "validator accepts a complete profile", validated.IsSuccess, ref failures);

            var rendered = WireGuardConfigWriter.Write(value);
            var reparsed = WireGuardConfigParser.Parse("wg0", rendered);
            Check(output, "writer round-trips", reparsed.TryGetValue(out var again) && again.Peers.Count == value.Peers.Count, ref failures);
        }

        var hooked = string.Concat(
            "[Interface]\n",
            "PrivateKey = ", privateKey, "\n",
            "Address = 10.10.0.2/24\n",
            "PostUp = echo hook\n",
            "[Peer]\n",
            "PublicKey = ", peerKey, "\n",
            "AllowedIPs = 0.0.0.0/0\n");
        var hookParsed = WireGuardConfigParser.Parse("wg0", hooked);
        Check(
            output,
            "parser preserves a hook for the connection policy",
            hookParsed.TryGetValue(out var hookValue) && hookValue.Interface.PostUp.Count == 1,
            ref failures);

        var request = new ProcessRequest("wg", ["pubkey"], StandardInput: "private-key-material");
        Check(output, "process request redacts standard input", !request.ToString().Contains("private-key-material", StringComparison.Ordinal), ref failures);

        var paths = ApplicationPaths.Create(
            new PathSet("/tmp/config", "/tmp/state", "/tmp/data", "/tmp/cache", null, [], []),
            "benmoussa",
            "vpn-manager");
        Check(
            output,
            "application paths are namespaced",
            paths.TryGetValue(out var applicationPaths) &&
            applicationPaths.StateFile("state.json") == "/tmp/state/benmoussa/vpn-manager/state.json",
            ref failures);

        if (failures == 0)
        {
            output.WriteLine("SELF-TEST PASS");
            return 0;
        }

        return 1;
    }

    private static void Check(TextWriter output, string name, bool passed, ref int failures)
    {
        if (!passed)
        {
            output.WriteLine($"SELF-TEST FAIL: {name}");
            failures++;
        }
    }
}
