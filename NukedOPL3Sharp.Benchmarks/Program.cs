using BenchmarkDotNet.Running;

namespace NukedOPL3Sharp.Benchmarks;

/// <summary>
///     Selects and runs benchmarks from this assembly.
/// </summary>
internal static class Program
{
    private static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
