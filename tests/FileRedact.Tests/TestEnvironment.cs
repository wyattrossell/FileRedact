using System.Runtime.CompilerServices;
using FileRedact.Core;

namespace FileRedact.Tests;

/// <summary>Points FileRedact's per-user data folder at a scratch directory so tests never touch real settings.</summary>
internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FileRedact-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable(AppPaths.DataDirOverrideVariable, dir);
    }
}
