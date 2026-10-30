using System;
using System.Runtime.InteropServices.JavaScript;

namespace IdleRng.Web
{
    /// <summary>
    /// The wasm bridge (web/wasm/BRIDGE.md). One engine per page. Arguments and results are strings, ints and doubles;
    /// structured data is JSON text. Exceptions never cross the boundary except from RunInit, which throws on purpose so the
    /// page can show a load error (bad content); every other method returns {"error":"..."} instead.
    /// </summary>
    public static partial class Engine
    {
        private const string VersionText = "IdleRng.Sim netstandard2.1 (C# via .NET 8 wasm)";

        [JSExport]
        public static string Version() => VersionText + (_run != null ? " content " + _run.ContentVersion : "");

        // ---- monster-trainer run (src/IdleRng.Web/RunHost.cs, contract in web/wasm/BRIDGE.md "Run bridge")
        private static RunHost _run;

        [JSExport]
        public static void RunInit(string contentJson)
        {
            _run = null;
            _run = new RunHost(contentJson);
        }

        [JSExport] public static string RunContent() => GuardRun(h => h.Content());
        [JSExport] public static string AccountNew(string seedHex) => GuardRun(h => h.NewAccount(seedHex));
        [JSExport] public static string AccountLoad(string saveText) => GuardRun(h => h.LoadAccount(saveText));
        [JSExport] public static string AccountSave() => GuardRun(h => h.SaveAccount());
        [JSExport] public static string AccountView() => GuardRun(h => h.AccountView());
        [JSExport] public static string AccountCmd(string name, string argsJson) => GuardRun(h => h.AccountCmd(name, argsJson));
        [JSExport] public static string RunState() => GuardRun(h => h.State());
        [JSExport] public static string RunCmd(string name, string argsJson) => GuardRun(h => h.Cmd(name, argsJson));

        private static string GuardRun(Func<RunHost, string> f)
        {
            if (_run == null) return JsonOut.Error("run engine not initialised");
            try { return f(_run); }
            catch (Exception e) { return JsonOut.Error(e.GetType().Name + ": " + e.Message); }
        }
    }
}
