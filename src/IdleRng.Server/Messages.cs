using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IdleRng.Server
{
    /// <summary>Why a call was turned down (specs/27 section 4); <see cref="None"/> for an accepted one.</summary>
    public enum Rejection
    {
        None,
        /// <summary>Another device holds the account (section 6.1).</summary>
        NotHolder,
        /// <summary>The server is past the client's base (a second tab, an old copy), or no account exists yet.</summary>
        Stale,
        /// <summary>The client runs other content than the server.</summary>
        ContentStale,
        /// <summary>The commands applied, but the result differs from the client's hash.</summary>
        Mismatch,
        /// <summary>A command was refused (illegal, malformed, an input outside the rules, a debug command not allowed).</summary>
        Refused,
    }

    /// <summary>One command as the client sends it: the bridge name and its positional arguments (web/wasm/BRIDGE.md).</summary>
    public sealed class WireCommand
    {
        [JsonProperty("k")] public string K { get; set; }
        [JsonProperty("a")] public JArray A { get; set; }

        public WireCommand() { }
        public WireCommand(string k, params object[] args) { K = k; A = args == null ? new JArray() : JArray.FromObject(args); }

        public override string ToString() => K + (A == null ? "[]" : A.ToString(Formatting.None));
    }

    /// <summary>Open the account (specs/27 section 5.1).</summary>
    public sealed class OpenRequest
    {
        public string DeviceId { get; set; }

        /// <summary>Take the account from the device that holds it (a sign-in or a guest's first open).</summary>
        public bool Claim { get; set; }
    }

    public sealed class OpenResult
    {
        public Rejection Code { get; set; }
        public bool Accepted => Code == Rejection.None;

        /// <summary>The account as the AccountSave text the engine loads (null when rejected).</summary>
        public string Account { get; set; }

        /// <summary>
        /// Null, or a sentence for the player when loading under this server's content gave something up (a run in progress
        /// that no longer loads, specs/06 section 5). The stored account is migrated at its next commit.
        /// </summary>
        public string Notice { get; set; }

        /// <summary>The stored account's commit number and hash: the client's base for its next commit.</summary>
        public int Seq { get; set; }
        public string Hash { get; set; }

        /// <summary>The device that holds the account.</summary>
        public string Holder { get; set; }

        /// <summary>True when this call created the account (genesis).</summary>
        public bool Created { get; set; }

        /// <summary>The server's content version: a client on another reloads.</summary>
        public string ContentVersion { get; set; }

        public long ServerSec { get; set; }

        /// <summary>The Dungeon day (UTC day number, specs/21 section 1) by the server's clock.</summary>
        public int Day { get; set; }
    }

    /// <summary>The commands since the last accepted commit (specs/27 section 4).</summary>
    public sealed class CommitRequest
    {
        public string DeviceId { get; set; }
        public int FromSeq { get; set; }
        public string FromHash { get; set; }
        public List<WireCommand> Commands { get; set; } = new List<WireCommand>();

        /// <summary>The account hash after the client applied every command (AccountParts.AccountHash).</summary>
        public string ToHash { get; set; }
        public string ContentVersion { get; set; }
    }

    public sealed class CommitResult
    {
        public Rejection Code { get; set; }
        public bool Accepted => Code == Rejection.None;

        /// <summary>The refused command's position in the commit, or -1 when the rejection is not about one command.</summary>
        public int Index { get; set; } = -1;
        public string Reason { get; set; }

        /// <summary>Accepted: the new commit number and hash. Mismatch: the hash the server computed.</summary>
        public int Seq { get; set; }
        public string Hash { get; set; }

        public long ServerSec { get; set; }
        public int Day { get; set; }

        internal static CommitResult Reject(Rejection code, int index, string reason) =>
            new CommitResult { Code = code, Index = index, Reason = reason };
    }

    /// <summary>
    /// The server's settings (configuration, not code). Clock and random sources are injectable so tests and the local test
    /// server are deterministic.
    /// </summary>
    public sealed class ServerConfig
    {
        /// <summary>COMMIT_MAX_COMMANDS (specs/27 section 4, placeholder).</summary>
        public int CommitMaxCommands { get; set; } = 500;

        /// <summary>CLOCK_SLACK_SEC: how far past the server's time a seal time may be (section 6.2, placeholder).</summary>
        public int ClockSlackSec { get; set; } = 300;

        /// <summary>Player ids allowed debug commands (section 6.3; kj's ids).</summary>
        public ISet<string> DebugPlayers { get; set; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The server's time, Unix milliseconds.</summary>
        public Func<long> NowMs { get; set; } = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>Random 64-bit values: account seeds at genesis and the tag in each commit's item keys.</summary>
        public Func<ulong> Random { get; set; } = SecureRandom;

        /// <summary>The longest device id accepted.</summary>
        public int MaxDeviceIdLength { get; set; } = 128;

        static ulong SecureRandom()
        {
            var b = new byte[8];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(b);
            return BitConverter.ToUInt64(b, 0);
        }
    }

    /// <summary>A stored item failed its hash, or the head names an item that is gone: the server's copy is damaged.</summary>
    public sealed class AccountDamagedException : Exception
    {
        public AccountDamagedException(string message, Exception inner = null) : base(message, inner) { }
    }
}
