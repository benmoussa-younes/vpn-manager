using VpnManager.Helper;

if (args is ["--self-test"])
{
    return HelperSelfTest.Run(Console.Out);
}

var command = HelperCommand.Parse(args);
if (command.Error is { } error)
{
    Console.Error.WriteLine(error);
    return 2;
}

switch (command.Verb)
{
    case HelperVerb.SysctlSrcValidMark:
        var applied = SrcValidMark.Apply();
        if (applied.TryGetValue(out var value))
        {
            Console.WriteLine(value);
            return 0;
        }

        Console.Error.WriteLine(applied.Error.Message);
        return 1;

    case HelperVerb.SelfTest:
        return HelperSelfTest.Run(Console.Out);

    default:
        Console.WriteLine("vpn-manager-helper sysctl-src-valid-mark");
        Console.WriteLine("vpn-manager-helper --self-test");
        Console.WriteLine("vpn-manager-helper --help");
        return args.Length == 0 ? 2 : 0;
}
