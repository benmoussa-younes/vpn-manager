using Core.Platform.Linux;
using VpnManager.Core.Storage;

namespace VpnManager.Core.Tests.Fakes;

internal static class TestFileSystems
{
    internal static SystemConfigFileSystem SystemConfig() =>
        new(new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());
}
