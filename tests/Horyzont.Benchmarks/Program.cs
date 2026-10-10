using BenchmarkDotNet.Running;

namespace Horyzont.Benchmarks;

/// <summary>Entry point: <c>dotnet run -c Release --project tests/Horyzont.Benchmarks -- --filter * --job short --exporters json markdown</c>.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        return 0;
    }
}
