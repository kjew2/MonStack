// Entry point required by the WebAssembly SDK. The page never runs Main: it calls dotnet.create() and then the
// [JSExport] methods of IdleRng.Web.Engine, so nothing happens here.
[assembly: System.Runtime.Versioning.SupportedOSPlatform("browser")]

namespace IdleRng.Web
{
    internal static class Program
    {
        private static void Main() { }
    }
}
