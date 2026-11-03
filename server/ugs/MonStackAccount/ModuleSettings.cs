using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IdleRng.Server;
using IdleRng.Sim.Content;

namespace MonStackAccount
{
    /// <summary>
    /// The module's configuration (specs/27 section 6.3): which players may send debug commands. It is set when the module is
    /// built, not in code: <c>server/ugs/deploy.sh</c> passes <c>MONSTACK_DEBUG_PLAYERS</c> (kj's UGS player ids, separated by
    /// spaces or commas; a GitHub Actions variable in pages.yml) as the MSBuild property <c>MonStackDebugPlayers</c>, which the
    /// project stamps into the assembly as metadata. Empty means nobody (agent, 2026-11-03: no runtime call to Remote Config or
    /// the Secret Manager on every request; a change needs a redeploy; kj can veto).
    /// </summary>
    public sealed class ModuleSettings
    {
        public const string DebugPlayersKey = "MonStack.DebugPlayers";

        public ISet<string> DebugPlayers { get; set; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The server's clock and random source; null for the real ones (tests fix them).</summary>
        public Func<long> NowMs { get; set; }
        public Func<ulong> Random { get; set; }

        /// <summary>A fresh core configuration for one call.</summary>
        public ServerConfig ServerConfig()
        {
            var c = new ServerConfig { DebugPlayers = new HashSet<string>(DebugPlayers, StringComparer.Ordinal) };
            if (NowMs != null) c.NowMs = NowMs;
            if (Random != null) c.Random = Random;
            return c;
        }

        /// <summary>Player ids from a list separated by spaces, commas, semicolons or new lines.</summary>
        public static HashSet<string> ParseList(string text) =>
            new HashSet<string>((text ?? "").Split(new[] { ' ', ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

        /// <summary>The settings this build was made with.</summary>
        public static ModuleSettings FromAssembly(Assembly asm = null)
        {
            asm = asm ?? typeof(ModuleSettings).Assembly;
            string list = asm.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == DebugPlayersKey)?.Value;
            return new ModuleSettings { DebugPlayers = ParseList(list) };
        }
    }

    /// <summary>
    /// The game content the module checks commits with: <c>Content/*.json</c> from the same commit as the site, embedded in the
    /// assembly (specs/27 section 7), loaded once per worker with its hashes verified.
    /// </summary>
    public static class ModuleContent
    {
        const string Prefix = "Content/";

        static readonly Lazy<ContentDb> _db = new Lazy<ContentDb>(Load);

        public static ContentDb Db => _db.Value;

        static ContentDb Load()
        {
            var asm = typeof(ModuleContent).Assembly;
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (!name.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                using (var s = asm.GetManifestResourceStream(name))
                using (var r = new System.IO.StreamReader(s, new System.Text.UTF8Encoding(false)))
                    files[name.Substring(Prefix.Length)] = r.ReadToEnd();
            }
            if (!files.ContainsKey("manifest.json")) throw new InvalidOperationException("the module was built without Content/manifest.json");
            return ContentDb.Load(new DictionaryContentSource(files), verifyHashes: true);
        }
    }
}
