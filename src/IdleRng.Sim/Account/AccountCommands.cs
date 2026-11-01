using System;
using System.Collections.Generic;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;

namespace IdleRng.Sim.Account
{
    /// <summary>
    /// The parts the account is stored in on the server (specs/27 section 3.1). A command declares the parts it reads and writes
    /// (<see cref="AccountCommands.Reads"/>, <see cref="AccountCommands.Writes"/>), so a server loads and rewrites only those.
    /// </summary>
    [Flags]
    public enum AccountPart
    {
        None = 0,
        /// <summary>Seed, RunsStarted, the talent loadout, the run in progress, the post-run vendor while open.</summary>
        Training = 1,
        /// <summary>Shards and Skill Books.</summary>
        Wallet = 2,
        /// <summary>Owned species and talent ranks.</summary>
        Collection = 4,
        /// <summary>Achievements and records.</summary>
        Progress = 8,
        /// <summary>Dungeon tickets, attempts and bests.</summary>
        Dungeon = 16,
        /// <summary>The Vault index: entry order and locks.</summary>
        Vault = 32,
        /// <summary>The Vault entries themselves (one stored item each).</summary>
        Entries = 64,
    }

    /// <summary>Every command that changes the account, by its bridge name (web/wasm/BRIDGE.md).</summary>
    public enum CommandKind
    {
        StartRun, FinishRun, Abandon, BuyOffer, RerollVendor, CloseVendor, SetLoadout, Dismantle, Lock, RunTower, DebugGrantBook,
        Drill, Rest, Fight, Take, Discard, Reroll, Spend, Book,
    }

    /// <summary>
    /// Positional command arguments as a host decoded them (the browser bridge with System.Text.Json, a server with its own JSON
    /// library). The sim reads no JSON itself (specs/00 contract 1). Missing and null both read as absent.
    /// </summary>
    public interface ICommandArgs
    {
        bool Has(int i);
        string Str(int i);
        double Num(int i);
        bool Bool(int i);
        List<string> StrList(int i);
        List<int> IntList(int i);
    }

    /// <summary>One command with its arguments, ready for <see cref="AccountCommands.Apply"/>.</summary>
    public sealed class AccountCommand
    {
        public CommandKind Kind { get; set; }

        /// <summary>startRun: species. drill: stat. book, debugGrantBook: book id.</summary>
        public string Id { get; set; }

        /// <summary>startRun: books brought (null = none). setLoadout: talents switched off.</summary>
        public List<string> Ids { get; set; }

        /// <summary>startRun: talents switched off (null = the saved loadout).</summary>
        public List<string> TalentsOff { get; set; }

        /// <summary>runTower: the team as Vault positions, in slot order.</summary>
        public List<int> Team { get; set; }

        /// <summary>buyOffer: offer. dismantle, lock: Vault position. take: choice. spend: slot. runTower: day. debugGrantBook: count.</summary>
        public int A { get; set; }

        /// <summary>take: slot.</summary>
        public int B { get; set; }

        /// <summary>finishRun: the seal time, Unix ms (display only).</summary>
        public long Time { get; set; }

        /// <summary>lock: locked or not.</summary>
        public bool Flag { get; set; }

        static readonly Dictionary<string, CommandKind> ByName = new Dictionary<string, CommandKind>(StringComparer.Ordinal)
        {
            ["startRun"] = CommandKind.StartRun, ["finishRun"] = CommandKind.FinishRun, ["abandon"] = CommandKind.Abandon,
            ["buyOffer"] = CommandKind.BuyOffer, ["rerollVendor"] = CommandKind.RerollVendor, ["closeVendor"] = CommandKind.CloseVendor,
            ["setLoadout"] = CommandKind.SetLoadout, ["dismantle"] = CommandKind.Dismantle, ["lock"] = CommandKind.Lock,
            ["runTower"] = CommandKind.RunTower, ["debugGrantBook"] = CommandKind.DebugGrantBook,
            ["drill"] = CommandKind.Drill, ["rest"] = CommandKind.Rest, ["fight"] = CommandKind.Fight, ["take"] = CommandKind.Take,
            ["discard"] = CommandKind.Discard, ["reroll"] = CommandKind.Reroll, ["spend"] = CommandKind.Spend, ["book"] = CommandKind.Book,
        };

        /// <summary>True when <paramref name="name"/> is a command (not a read such as gauntlet or towerInfo).</summary>
        public static bool IsCommand(string name) => name != null && ByName.ContainsKey(name);

        /// <summary>The bridge name of a kind ("startRun", "drill", ...).</summary>
        public static string NameOf(CommandKind kind)
        {
            foreach (var kv in ByName) if (kv.Value == kind) return kv.Key;
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        /// <summary>
        /// A command from its bridge name and positional arguments, the one mapping every host uses. Throws ArgumentException for
        /// an unknown name or a missing or mistyped argument.
        /// </summary>
        public static AccountCommand Parse(string name, ICommandArgs a)
        {
            if (name == null || !ByName.TryGetValue(name, out var kind)) throw new ArgumentException("unknown command");
            if (a == null) throw new ArgumentNullException(nameof(a));
            var c = new AccountCommand { Kind = kind };
            switch (kind)
            {
                case CommandKind.StartRun:
                    c.Id = a.Str(0);
                    c.Ids = a.Has(1) ? a.StrList(1) : null;
                    c.TalentsOff = a.Has(2) ? a.StrList(2) : null;
                    break;
                case CommandKind.FinishRun: c.Time = (long)a.Num(0); break;
                case CommandKind.BuyOffer: case CommandKind.Dismantle: case CommandKind.Spend: c.A = Int(a, 0); break;
                case CommandKind.SetLoadout: c.Ids = a.Has(0) ? a.StrList(0) : new List<string>(); break;
                case CommandKind.Lock: c.A = Int(a, 0); c.Flag = a.Bool(1); break;
                case CommandKind.RunTower: c.A = Int(a, 0); c.Team = a.IntList(1); break;
                case CommandKind.DebugGrantBook: c.Id = a.Str(0); c.A = a.Has(1) ? Int(a, 1) : 1; break;
                case CommandKind.Drill: case CommandKind.Book: c.Id = a.Str(0); break;
                case CommandKind.Take: c.A = Int(a, 0); c.B = Int(a, 1); break;
            }
            return c;
        }

        static int Int(ICommandArgs a, int i) => (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Floor(a.Num(i))));
    }

    /// <summary>buyOffer's result: the shards paid and the talent that gained a rank.</summary>
    public sealed class OfferBought
    {
        public long Cost { get; set; }
        public string Talent { get; set; }
    }

    /// <summary>
    /// The one dispatcher for account and run commands (specs/27 section 4): the browser bridge, the server and the tests apply
    /// commands through it, so a command means the same everywhere. Refused commands throw (InvalidOperationException or
    /// ArgumentException) and change nothing.
    /// </summary>
    public static class AccountCommands
    {
        /// <summary>A run command (it needs a run in progress and touches only the run).</summary>
        public static bool IsRunCommand(CommandKind k) => k >= CommandKind.Drill;

        /// <summary>The parts a command may change (specs/27 section 3.1).</summary>
        public static AccountPart Writes(CommandKind k)
        {
            switch (k)
            {
                case CommandKind.StartRun: return AccountPart.Training | AccountPart.Wallet;
                case CommandKind.FinishRun:
                    return AccountPart.Training | AccountPart.Wallet | AccountPart.Collection | AccountPart.Progress | AccountPart.Dungeon
                           | AccountPart.Vault | AccountPart.Entries;
                case CommandKind.Abandon: return AccountPart.Training | AccountPart.Wallet;
                case CommandKind.BuyOffer: return AccountPart.Training | AccountPart.Wallet | AccountPart.Collection;
                case CommandKind.RerollVendor: return AccountPart.Training | AccountPart.Wallet;
                case CommandKind.CloseVendor: case CommandKind.SetLoadout: return AccountPart.Training;
                case CommandKind.Dismantle: return AccountPart.Wallet | AccountPart.Vault | AccountPart.Entries;
                case CommandKind.Lock: return AccountPart.Vault;
                case CommandKind.RunTower: return AccountPart.Wallet | AccountPart.Dungeon;
                case CommandKind.DebugGrantBook: return AccountPart.Wallet;
                default: return AccountPart.Training;   // run commands
            }
        }

        /// <summary>The parts a command reads besides those it writes.</summary>
        public static AccountPart Reads(CommandKind k)
        {
            switch (k)
            {
                case CommandKind.StartRun: return AccountPart.Collection | AccountPart.Vault;   // owned species, talents, the Vault cap
                case CommandKind.RerollVendor: return AccountPart.Collection;                    // maxed talents
                case CommandKind.SetLoadout: return AccountPart.Collection;
                case CommandKind.Dismantle: return AccountPart.None;
                case CommandKind.RunTower: return AccountPart.Vault | AccountPart.Entries;
                default: return AccountPart.None;
            }
        }

        /// <summary>
        /// Apply one command. Returns what the command produced for feedback: the new run (startRun), a FinishResult, the books
        /// returned (abandon), an OfferBought, the shards spent (rerollVendor) or refunded (dismantle) as a long, a TowerRunResult,
        /// the run command's own result (DrillResult, RestResult, RankFightResult, TakeResult, SpendResult), or null.
        /// <paramref name="onAction"/> and <paramref name="onStart"/> watch a rank fight (playback only; they change nothing).
        /// </summary>
        public static object Apply(ContentDb c, AccountState a, AccountCommand cmd, Action<FightAction> onAction = null, Action<FightState> onStart = null)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));
            RunState s = null;
            if (IsRunCommand(cmd.Kind)) s = a.Active ?? throw new InvalidOperationException("no run");
            switch (cmd.Kind)
            {
                case CommandKind.StartRun: return AccountOps.StartRun(c, a, cmd.Id, cmd.Ids, cmd.TalentsOff);
                case CommandKind.FinishRun: return AccountOps.FinishRun(c, a, cmd.Time);
                case CommandKind.Abandon: return AccountOps.AbandonRun(c, a);
                case CommandKind.BuyOffer:
                {
                    var offer = a.Vendor != null && cmd.A >= 0 && cmd.A < a.Vendor.Offers.Count ? a.Vendor.Offers[cmd.A] : null;
                    long cost = AccountOps.BuyOffer(c, a, cmd.A);
                    return new OfferBought { Cost = cost, Talent = offer?.Talent };
                }
                case CommandKind.RerollVendor: return AccountOps.RerollVendor(c, a);
                case CommandKind.CloseVendor: AccountOps.CloseVendor(c, a); return null;
                case CommandKind.SetLoadout: AccountOps.SetLoadout(c, a, cmd.Ids ?? new List<string>()); return null;
                case CommandKind.Dismantle: return AccountOps.Dismantle(c, a, cmd.A);
                case CommandKind.Lock: AccountOps.SetLocked(c, a, cmd.A, cmd.Flag); return null;
                case CommandKind.RunTower:
                {
                    var team = cmd.Team ?? throw new ArgumentException("argument 1 missing");
                    foreach (var i in team)
                        if (i < 0 || i >= a.Vault.Count) throw new ArgumentException("no Vault entry " + i);
                    return AccountOps.RunTower(c, a, team, cmd.A);
                }
                case CommandKind.DebugGrantBook: AccountOps.GrantBooks(c, a, cmd.Id, cmd.A); return null;
                case CommandKind.Drill: return TrainingRun.Drill(c, s, cmd.Id);
                case CommandKind.Rest: return TrainingRun.Rest(c, s);
                case CommandKind.Fight: return TrainingRun.Fight(c, s, onAction, onStart);
                case CommandKind.Take: return TrainingRun.TakeTree(c, s, cmd.A, cmd.B);
                case CommandKind.Discard: TrainingRun.DiscardOffer(c, s); return null;
                case CommandKind.Reroll: TrainingRun.RerollDraft(c, s); return null;
                case CommandKind.Spend: return TrainingRun.SpendPoint(c, s, cmd.A);
                case CommandKind.Book: TrainingRun.UseBook(c, s, cmd.Id); return null;
                default: throw new ArgumentException("unknown command");
            }
        }
    }
}
