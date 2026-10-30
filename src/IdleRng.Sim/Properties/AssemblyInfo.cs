using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("IdleRng.Sim.Tests")]

namespace System.Runtime.CompilerServices
{
    /// <summary>Polyfill so C# 9 records and init accessors compile on netstandard2.1.</summary>
    internal static class IsExternalInit { }
}
