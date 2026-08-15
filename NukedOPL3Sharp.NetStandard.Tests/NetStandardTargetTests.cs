using System.Reflection;
using System.Runtime.Versioning;

namespace NukedOPL3Sharp.NetStandard.Tests;

/// <summary>
///     Guards the test project's forced target selection so parity always exercises the netstandard assembly.
/// </summary>
public sealed class NetStandardTargetTests
{
    /// <summary>
    ///     Fails if project-reference negotiation silently selects a runtime-specific build.
    /// </summary>
    [Fact]
    public void ReferencedLibrary_UsesNetStandard21Target()
    {
        var targetFramework = typeof(Opl3Chip).Assembly.GetCustomAttribute<TargetFrameworkAttribute>();

        Assert.NotNull(targetFramework);
        Assert.Equal(".NETStandard,Version=v2.1", targetFramework.FrameworkName);
    }
}
