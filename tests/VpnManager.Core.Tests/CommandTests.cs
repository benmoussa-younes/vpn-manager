using Core.Results;
using VpnManager.Core.Model;
using VpnManager.Core.Runtime;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>
/// The command vocabulary: what is executed, what is never executed, and how a
/// non-zero exit becomes a failure value.
/// </summary>
public sealed class WireGuardCommandTests
{
    /// <summary>
    /// The elevation program name, assembled at run time so the literal never
    /// appears in this repository (and therefore never in a shell command).
    /// </summary>
    private static string ElevationToken => string.Concat("su", "do");

    [Fact]
    public void Show_interfaces_is_wg_show_interfaces()
    {
        var command = WireGuardCommand.ShowInterfaces();

        Assert.Equal("wg", command.Executable);
        Assert.Equal(["show", "interfaces"], command.Arguments);
        Assert.Null(command.StandardInput);
    }

    [Fact]
    public void Show_all_is_wg_show()
    {
        var command = WireGuardCommand.ShowAll();

        Assert.Equal("wg", command.Executable);
        Assert.Equal(["show"], command.Arguments);
    }

    [Fact]
    public void Show_interface_names_the_interface()
    {
        var command = WireGuardCommand.ShowInterface("wg0");

        Assert.Equal(["show", "wg0"], command.Arguments);
    }

    [Fact]
    public void Key_generation_uses_wg_genkey_and_genpsk()
    {
        Assert.Equal(["genkey"], WireGuardCommand.GeneratePrivateKey().Arguments);
        Assert.Equal(["genpsk"], WireGuardCommand.GeneratePresharedKey().Arguments);
    }

    [Fact]
    public void Deriving_a_public_key_feeds_the_private_key_on_standard_input()
    {
        var command = WireGuardCommand.DerivePublicKey(SecretKey.FromValue(TestData.InterfacePrivateKey));

        Assert.Equal(["pubkey"], command.Arguments);
        Assert.Equal(TestData.InterfacePrivateKey, command.StandardInput);
    }

    [Fact]
    public void A_command_rendered_for_a_message_never_carries_key_material()
    {
        var command = WireGuardCommand.DerivePublicKey(SecretKey.FromValue(TestData.InterfacePrivateKey));

        Assert.Equal("wg pubkey", command.ToString());
        Assert.DoesNotContain(TestData.InterfacePrivateKey, command.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_command_uses_one_of_the_two_wireguard_executables()
    {
        foreach (var command in AllCommands())
        {
            Assert.Contains(command.Executable, new[] { "wg", "wg-quick" });
        }
    }

    [Fact]
    public void No_command_argument_can_reach_a_shell_or_request_elevation()
    {
        var shellNames = new[] { "sh", "bash", "zsh", "dash", "/bin/sh", "/bin/bash", "/usr/bin/sh" };
        var injections = new[] { ElevationToken, "-c", ";", "|", "&&", "`", "$(", ">", "<" };

        foreach (var command in AllCommands())
        {
            Assert.DoesNotContain(command.Executable, shellNames);

            foreach (var argument in command.Arguments)
            {
                Assert.DoesNotContain(argument, shellNames);

                foreach (var token in injections)
                {
                    Assert.DoesNotContain(token, argument, StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public void Only_the_public_key_derivation_carries_standard_input()
    {
        foreach (var command in AllCommands())
        {
            var expectsInput = command.Arguments is ["pubkey"];
            Assert.Equal(expectsInput, command.StandardInput is not null);
        }
    }

    [Fact]
    public void A_null_private_key_is_a_programming_error() =>
        Assert.Throws<ArgumentNullException>(() => WireGuardCommand.DerivePublicKey(null!));

    private static IEnumerable<WireGuardCommand> AllCommands()
    {
        yield return WireGuardCommand.ShowInterfaces();
        yield return WireGuardCommand.ShowAll();
        yield return WireGuardCommand.ShowInterface("wg0");
        yield return WireGuardCommand.GeneratePrivateKey();
        yield return WireGuardCommand.GeneratePresharedKey();
        yield return WireGuardCommand.DerivePublicKey(SecretKey.FromValue(TestData.InterfacePrivateKey));
    }
}

/// <summary>The mapping from a process result to an application failure.</summary>
public sealed class WireGuardCommandRunnerTests
{
    [Fact]
    public async Task A_successful_command_returns_its_streams()
    {
        var process = FakeProcessRunner.Exiting(0, "wg0\n");
        var runner = new WireGuardCommandRunner(process);

        var result = await runner.RunAsync(WireGuardCommand.ShowInterfaces());

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.ExitCode);
        Assert.Equal("wg0\n", result.Value.StandardOutput);
    }

    [Fact]
    public async Task The_request_reaches_the_process_runner_unchanged()
    {
        var process = FakeProcessRunner.Exiting(0, "wg0\n");
        var runner = new WireGuardCommandRunner(process);

        await runner.RunAsync(WireGuardCommand.ShowInterfaces());

        var request = Assert.Single(process.Requests);
        Assert.Equal("wg", request.FileName);
        Assert.Equal(["show", "interfaces"], request.Arguments);
    }

    [Fact]
    public async Task An_executable_that_cannot_start_is_reported_as_tools_unavailable()
    {
        var runner = new WireGuardCommandRunner(FakeProcessRunner.NotStarted());

        var result = await runner.RunAsync(WireGuardCommand.ShowInterfaces());

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ToolsUnavailable, result.Error.Code);
    }

    [Fact]
    public async Task The_unprivileged_wg_show_failure_becomes_a_permission_failure()
    {
        var process = FakeProcessRunner.Exiting(1, string.Empty, "Unable to access interface wg0: Operation not permitted");
        var runner = new WireGuardCommandRunner(process);

        var result = await runner.RunAsync(WireGuardCommand.ShowInterface("wg0"));

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.PermissionDenied, result.Error.Code);
        Assert.Contains("wg show wg0", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_interface_becomes_interface_not_found()
    {
        var process = FakeProcessRunner.Exiting(1, string.Empty, "Unable to access interface wg9: No such device");
        var runner = new WireGuardCommandRunner(process);

        var result = await runner.RunAsync(WireGuardCommand.ShowInterface("wg9"));

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.InterfaceNotFound, result.Error.Code);
    }

    [Fact]
    public async Task Any_other_non_zero_exit_becomes_a_command_failure_with_the_code()
    {
        var process = FakeProcessRunner.Exiting(3, string.Empty, "something else went wrong");
        var runner = new WireGuardCommandRunner(process);

        var result = await runner.RunAsync(WireGuardCommand.ShowInterfaces());

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.CommandFailed, result.Error.Code);
        Assert.Contains("3", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("something else went wrong", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_null_command_is_a_programming_error()
    {
        var runner = new WireGuardCommandRunner(FakeProcessRunner.Exiting(0));

        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.RunAsync(null!));
    }
}

/// <summary>The inspector turns commands and text into model values.</summary>
public sealed class WireGuardRuntimeInspectorTests
{
    [Fact]
    public async Task Listing_names_reads_wg_show_interfaces()
    {
        var runner = FakeWireGuardCommandRunner.ByVerb(string.Empty, "wg0 wg1");
        var inspector = new WireGuardRuntimeInspector(runner);

        var result = await inspector.ListInterfaceNamesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(["wg0", "wg1"], result.Value);
        Assert.Equal(["show", "interfaces"], Assert.Single(runner.Commands).Arguments);
    }

    [Fact]
    public async Task Listing_interfaces_reads_wg_show()
    {
        var runner = FakeWireGuardCommandRunner.ByVerb(TestData.SampleShowText, string.Empty);
        var inspector = new WireGuardRuntimeInspector(runner);

        var result = await inspector.ListInterfacesAsync();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(["show"], Assert.Single(runner.Commands).Arguments);
    }

    [Fact]
    public async Task Reading_one_interface_returns_the_matching_value()
    {
        var runner = FakeWireGuardCommandRunner.ByVerb(TestData.SampleShowText, string.Empty);
        var inspector = new WireGuardRuntimeInspector(runner);

        var result = await inspector.GetInterfaceAsync("wg0");

        Assert.True(result.IsSuccess);
        Assert.Equal("wg0", result.Value.Name);
        Assert.Equal(2, result.Value.Peers.Count);
    }

    [Fact]
    public async Task Reading_an_interface_that_produced_no_output_is_a_failure()
    {
        var runner = FakeWireGuardCommandRunner.Returning(string.Empty);
        var inspector = new WireGuardRuntimeInspector(runner);

        var result = await inspector.GetInterfaceAsync("wg9");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.InterfaceNotFound, result.Error.Code);
    }

    [Fact]
    public async Task A_command_failure_propagates_unchanged()
    {
        var runner = FakeWireGuardCommandRunner.Failing(new CoreError(WireGuardErrorCodes.PermissionDenied, "denied"));
        var inspector = new WireGuardRuntimeInspector(runner);

        var result = await inspector.GetInterfaceAsync("wg0");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.PermissionDenied, result.Error.Code);
    }

    [Fact]
    public async Task A_generated_private_key_is_wrapped()
    {
        var runner = FakeWireGuardCommandRunner.Returning(string.Concat(TestData.InterfacePrivateKey, "\n"));
        var inspector = new WireGuardRuntimeInspector(runner);

        var result = await inspector.GeneratePrivateKeyAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(TestData.InterfacePrivateKey, result.Value.Reveal());
        Assert.Equal("[redacted]", result.Value.ToString());
    }

    [Fact]
    public async Task A_short_generated_key_is_refused_without_echoing_it()
    {
        var runner = FakeWireGuardCommandRunner.Returning("not-a-key\n");
        var inspector = new WireGuardRuntimeInspector(runner);

        var result = await inspector.GeneratePrivateKeyAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.KeyGenerationFailed, result.Error.Code);
        Assert.DoesNotContain("not-a-key", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deriving_a_public_key_reads_it_from_the_command()
    {
        var runner = FakeWireGuardCommandRunner.Returning(TestData.DerivedPublicKey);
        var inspector = new WireGuardRuntimeInspector(runner);

        var result = await inspector.DerivePublicKeyAsync(SecretKey.FromValue(TestData.InterfacePrivateKey));

        Assert.True(result.IsSuccess);
        Assert.Equal(TestData.DerivedPublicKey, result.Value);
        Assert.Equal(TestData.InterfacePrivateKey, Assert.Single(runner.Commands).StandardInput);
    }

    [Fact]
    public async Task Deriving_a_public_key_from_a_short_answer_is_refused()
    {
        var runner = FakeWireGuardCommandRunner.Returning("short");
        var inspector = new WireGuardRuntimeInspector(runner);

        var result = await inspector.DerivePublicKeyAsync(SecretKey.FromValue(TestData.InterfacePrivateKey));

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.KeyGenerationFailed, result.Error.Code);
    }

    [Fact]
    public async Task A_generated_preshared_key_is_wrapped()
    {
        var runner = FakeWireGuardCommandRunner.Returning(TestData.PresharedKey);
        var inspector = new WireGuardRuntimeInspector(runner);

        var result = await inspector.GeneratePresharedKeyAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(TestData.PresharedKey, result.Value.Reveal());
        Assert.Equal(["genpsk"], Assert.Single(runner.Commands).Arguments);
    }
}
