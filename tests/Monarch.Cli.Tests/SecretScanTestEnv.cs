using System.Runtime.CompilerServices;

namespace Monarch.Cli.Tests;

internal static class SecretScanTestEnv
{
#pragma warning disable CA2255 // test assembly: hermetic git config once per process
    [ModuleInitializer]
    internal static void Init()
    {
        // the developer's global git config (e.g. core.hooksPath) must not leak into the tests
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", "/dev/null");
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
    }
#pragma warning restore CA2255
}
