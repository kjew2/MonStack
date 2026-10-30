using System;
using System.Runtime.InteropServices.JavaScript;

namespace IdleRng.Web
{
    /// <summary>
    /// The wasm bridge (web/wasm/BRIDGE.md). One engine per page. Arguments and results are strings, ints and doubles;
    /// structured data is JSON text. Exceptions never cross the boundary except from Init, which throws on purpose so the
    /// facade can fall back (bad save, bad content); every other method returns {"error":"..."} instead.
    /// </summary>
    public static partial class Engine
    {
        private const string VersionText = "IdleRng.Sim 0.1.0 netstandard2.1 (C# via .NET 8 wasm)";
        private static Host _h;

        [JSExport]
        public static void Init(string contentJson, string saveJson, string profileSeedHex, double nowMs, string profileId)
        {
            _h = null;
            _h = new Host(contentJson, saveJson, profileSeedHex, nowMs, profileId);
        }

        [JSExport]
        public static void Advance(int ticks)
        {
            try { _h?.Advance(ticks); }
            catch (Exception e) { _h?.PushEngineError(e.GetType().Name + ": " + e.Message); }
        }

        [JSExport]
        public static string State() => Guard(h => h.State());

        [JSExport]
        public static string Drain() => Guard(h => h.Drain());

        [JSExport]
        public static string Query(string name, string argsJson) => Guard(h => h.Query(name, argsJson));

        [JSExport]
        public static string Cmd(string name, string argsJson) => Guard(h => h.Cmd(name, argsJson));

        [JSExport]
        public static string Export() => Guard(h => h.Export());

        [JSExport]
        public static string UiContent() => Guard(h => h.UiContent());

        [JSExport]
        public static void SetEmitHits(int on)
        {
            try { _h?.SetEmitHits(on != 0); } catch (Exception) { /* nothing to report to */ }
        }

        [JSExport]
        public static string Version() => VersionText + (_h != null ? " content " + _h.ContentVersion : "");

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

        private static string Guard(Func<Host, string> f)
        {
            if (_h == null) return JsonOut.Error("not initialised");
            try { return f(_h); }
            catch (Exception e) { return JsonOut.Error(e.GetType().Name + ": " + e.Message); }
        }
    }
}
