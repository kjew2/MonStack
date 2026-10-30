using System;
using System.Collections.Generic;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;

namespace IdleRng.Sim.State
{
    /// <summary>
    /// The complete save payload (specs/03 section 6, merged with the economy fields of specs/04).
    /// Plain mutable object graph: public get/set properties, string-keyed dictionaries, arrays.
    /// Serialisation, schema versions and migrations belong to IdleRng.Runtime (specs/06).
    /// Derived values (DPS, gear BasePct, hero levels, multipliers) are never stored.
    /// </summary>
    public sealed class GameState
    {
        public const int CurrentSchemaVersion = 2;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string ContentVersion { get; set; } = "";
        public Guid ProfileId { get; set; }
        public long CreatedAtServerSec { get; set; }

        /// <summary>Lifetime ticks simulated.</summary>
        public long Tick { get; set; }

        public WallClockState Clock { get; set; } = new WallClockState();
        public DailyState Daily { get; set; } = new DailyState();
        public Currencies Currencies { get; set; } = new Currencies();
        public ProgressionState Progression { get; set; } = new ProgressionState();
        public StageState Stage { get; set; } = new StageState();
        public CombatState Combat { get; set; } = new CombatState();
        public GearState Gear { get; set; } = new GearState();
        public HeroState Heroes { get; set; } = new HeroState();
        public BannerState HeroBanner { get; set; } = new BannerState();
        public RngState Rng { get; set; } = new RngState();
        public LifetimeStats Lifetime { get; set; } = new LifetimeStats();

        /// <summary>Profile seed from the GUID (specs/03 section 5.6).</summary>
        public static ulong SeedFromProfileId(Guid id)
        {
            var b = id.ToByteArray();
            ulong lo = BitConverter.ToUInt64(b, 0), hi = BitConverter.ToUInt64(b, 8);
            if (!BitConverter.IsLittleEndian) { lo = Swap(lo); hi = Swap(hi); }
            return SplitMix64.Mix(lo ^ hi);
        }

        static ulong Swap(ulong v)
        {
            ulong r = 0;
            for (int i = 0; i < 8; i++) { r = (r << 8) | (v & 0xFF); v >>= 8; }
            return r;
        }

        public static GameState NewProfile(ContentDb content, Guid profileId, long createdServerSec) =>
            NewProfile(content, profileId, SeedFromProfileId(profileId), createdServerSec);

        /// <summary>
        /// Fresh profile at stage 1 with the starter companion in party slot 0 (specs/11 section 4.5). No tutorial
        /// grants (call Simulation.ApplyTutorialGrants for those).
        /// Daily.DayKey starts at -1 so the first tick (or SyncWallClock) rolls the first day: login quest,
        /// daily free pulls.
        /// </summary>
        public static GameState NewProfile(ContentDb content, Guid profileId, ulong profileSeed, long createdServerSec)
        {
            var k = content.Constants;
            var s = new GameState
            {
                ContentVersion = content.Version,
                ProfileId = profileId,
                CreatedAtServerSec = createdServerSec,
                Clock = new WallClockState { LastSyncServerMs = createdServerSec * 1000L, TickAtLastSync = 0 },
                Daily = new DailyState
                {
                    DayKey = -1,
                    Progress = new int[content.DailyQuests.Count],
                    Rewarded = new bool[content.DailyQuests.Count],
                },
                Progression = new ProgressionState { ChestTimerStart = createdServerSec },
                Combat = new CombatState { Phase = CombatPhase.Spawning, SpawnTimer = k.SpawnDelayTicks },
                Gear = new GearState { Slots = new GearPiece[content.GearSlotCount] },
                Heroes = new HeroState { Equipped = new string[k.HeroSlots] },
                Rng = new RngState { ProfileSeed = profileSeed },
            };
            foreach (var name in StreamNames.All)
                s.Rng.Streams[name] = new RngStream(name, StreamNames.StreamSeed(profileSeed, name));
            if (content.StarterHeroId != null && s.Heroes.Equipped.Length > 0)
            {
                s.Heroes.Copies[content.StarterHeroId] = 1;
                s.Heroes.Equipped[0] = content.StarterHeroId;
            }
            return s;
        }

        public GameState DeepClone()
        {
            var c = (GameState)MemberwiseClone();
            c.Clock = new WallClockState { LastSyncServerMs = Clock.LastSyncServerMs, TickAtLastSync = Clock.TickAtLastSync };
            c.Daily = new DailyState
            {
                DayKey = Daily.DayKey,
                Progress = (int[])Daily.Progress?.Clone(),
                Rewarded = (bool[])Daily.Rewarded?.Clone(),
            };
            c.Currencies = Currencies.Clone();
            c.Progression = Progression.Clone();
            c.Stage = Stage.Clone();
            c.Combat = Combat.Clone();
            c.Gear = new GearState { Slots = (GearPiece[])Gear.Slots?.Clone() };   // GearPiece is immutable
            c.Heroes = new HeroState
            {
                Copies = new Dictionary<string, int>(Heroes.Copies, StringComparer.Ordinal),
                Equipped = (string[])Heroes.Equipped?.Clone(),
                AutoEquipBest = Heroes.AutoEquipBest,
                Promotions = new Dictionary<string, int>(Heroes.Promotions, StringComparer.Ordinal),
            };
            c.HeroBanner = new BannerState { PityCounter = HeroBanner.PityCounter, TotalPulls = HeroBanner.TotalPulls };
            c.Rng = new RngState { ProfileSeed = Rng.ProfileSeed };
            foreach (var kv in Rng.Streams) c.Rng.Streams[kv.Key] = kv.Value.Clone();
            c.Lifetime = Lifetime.Clone();
            return c;
        }
    }

    /// <summary>Anchor for the sim's virtual clock: now = LastSyncServerMs + (Tick - TickAtLastSync) * msPerTick.</summary>
    public sealed class WallClockState
    {
        public long LastSyncServerMs { get; set; }
        public long TickAtLastSync { get; set; }
    }

    public sealed class DailyState
    {
        /// <summary>floor(serverSeconds / DAY_SECONDS); -1 on a new profile.</summary>
        public long DayKey { get; set; } = -1;

        /// <summary>Indexed like ContentDb.DailyQuests.</summary>
        public int[] Progress { get; set; } = Array.Empty<int>();

        public bool[] Rewarded { get; set; } = Array.Empty<bool>();
    }

    public sealed class Currencies
    {
        public double Gold { get; set; }
        public long Gems { get; set; }

        /// <summary>Remaining daily free pulls; reset to FREE_PULLS_PER_DAY at rollover, expire unused.</summary>
        public int FreePullsDaily { get; set; }

        /// <summary>Tutorial free pulls; never expire; can pay a 10-pull.</summary>
        public int FreePullsBanked { get; set; }

        public long Shards { get; set; }
        public long EnhMaterials { get; set; }    // P1
        public int DungeonTickets { get; set; }   // P1

        public Currencies Clone() => (Currencies)MemberwiseClone();
    }

    public sealed class ProgressionState
    {
        public int StatLevel { get; set; }
        public int HighestFirstClearBossStage { get; set; }

        /// <summary>Server seconds; chest is ready at ChestTimerStart + IDLE_CHEST_HOURS.</summary>
        public long ChestTimerStart { get; set; }

        public bool TutorialGranted { get; set; }

        public ProgressionState Clone() => (ProgressionState)MemberwiseClone();
    }

    public enum BossPhase { None, Pending, Walled }

    public sealed class StageState
    {
        public int Stage { get; set; } = 1;

        /// <summary>0 .. KILLS_PER_STAGE-1.</summary>
        public int KillsInStage { get; set; }

        /// <summary>None: normal mobs. Pending: next spawn is the boss. Walled: boss failed here, farming normals.</summary>
        public BossPhase Boss { get; set; }

        public int BossFailsOnStage { get; set; }

        public StageState Clone() => (StageState)MemberwiseClone();
    }

    public enum CombatPhase { Spawning, Fighting }

    public sealed class CombatState
    {
        public CombatPhase Phase { get; set; }
        public int SpawnTimer { get; set; }
        public int AttackTimer { get; set; }
        public int BossTimer { get; set; }
        public bool EnemyIsBoss { get; set; }
        public double EnemyHp { get; set; }
        public double EnemyMaxHp { get; set; }

        /// <summary>The 4v1 battle while a boss is being fought; null otherwise (specs/11 section 8).</summary>
        public Battle.BattleState Battle { get; set; }

        public CombatState Clone()
        {
            var c = (CombatState)MemberwiseClone();
            c.Battle = Battle?.Clone();
            return c;
        }
    }

    /// <summary>Immutable gear piece. Slot is the gear_slots sort_order. BasePct is derived, never stored.</summary>
    public sealed record GearPiece(int Slot, Rarity Rarity, int Tier, int Enhance);

    public sealed class GearState
    {
        /// <summary>Length GEAR_SLOTS; null = empty.</summary>
        public GearPiece[] Slots { get; set; } = Array.Empty<GearPiece>();
    }

    public sealed class HeroState
    {
        /// <summary>Lifetime copies per hero id (content heroes.id).</summary>
        public Dictionary<string, int> Copies { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The party. Length HERO_SLOTS; null = empty; entries distinct and owned.</summary>
        public string[] Equipped { get; set; } = Array.Empty<string>();

        /// <summary>When true the sim re-picks the best party after every pull or promotion. Manual equip turns it off.</summary>
        public bool AutoEquipBest { get; set; } = true;

        /// <summary>Promotions per hero id (specs/11 section 6). Missing = 0.</summary>
        public Dictionary<string, int> Promotions { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    public sealed class BannerState
    {
        /// <summary>Pulls since last L+.</summary>
        public int PityCounter { get; set; }

        public long TotalPulls { get; set; }
    }

    public sealed class RngState
    {
        public ulong ProfileSeed { get; set; }

        /// <summary>Keyed by StreamNames.*. Missing streams are created on Simulation construction.</summary>
        public Dictionary<string, RngStream> Streams { get; set; } = new Dictionary<string, RngStream>(StringComparer.Ordinal);
    }

    public sealed class LifetimeStats
    {
        public long Kills { get; set; }
        public long BossKills { get; set; }
        public long BossFails { get; set; }
        public long GearDrops { get; set; }
        public long EnhanceAttempts { get; set; }
        public long EnhanceSuccesses { get; set; }
        public double GoldEarned { get; set; }
        public double OfflineGoldEarned { get; set; }
        public long GemsEarned { get; set; }

        /// <summary>Stage of the first BossFailed ever; 0 until then.</summary>
        public int FirstWallStage { get; set; }

        public LifetimeStats Clone() => (LifetimeStats)MemberwiseClone();
    }
}
