using System.Collections.Generic;

namespace IdleRng.Sim.Battle
{
    /// <summary>
    /// A 4v1 boss battle in progress (specs/11 section 8). Lives in CombatState.Battle while a boss is being fought and is
    /// saved with the rest of the state, so a battle resumes exactly after a reload. Plain mutable graph, like GameState.
    /// </summary>
    public sealed class BattleState
    {
        /// <summary>Stage the battle is on (K and boss stats were derived from it at the start).</summary>
        public int Stage { get; set; }

        /// <summary>DEF mitigation constant for this stage: DEF_K_MULT * HP(stage).</summary>
        public double DefK { get; set; }

        /// <summary>Boss actions so far (every AoeEvery-th one is an AoE).</summary>
        public int BossActions { get; set; }

        /// <summary>Length HERO_SLOTS; null where the party slot was empty at the start.</summary>
        public BattleUnit[] Party { get; set; } = new BattleUnit[0];

        public BattleUnit Boss { get; set; }

        /// <summary>Scratch list for trigger collection (not saved, not hashed).</summary>
        internal List<TriggeredEffect> Scratch => _scratch ??= new List<TriggeredEffect>();
        List<TriggeredEffect> _scratch;

        public BattleState Clone()
        {
            var c = (BattleState)MemberwiseClone();
            c._scratch = null;
            c.Party = new BattleUnit[Party.Length];
            for (int i = 0; i < Party.Length; i++) c.Party[i] = Party[i]?.Clone();
            c.Boss = Boss?.Clone();
            return c;
        }
    }

    /// <summary>One combatant. HeroId is null for the boss. Stats are snapshotted at the start of the battle.</summary>
    public sealed class BattleUnit
    {
        public string HeroId { get; set; }

        /// <summary>Promotions at the start: decides which kit skills are unlocked.</summary>
        public int Promotions { get; set; }

        public string Element { get; set; }
        public double MaxHp { get; set; }
        public double Hp { get; set; }
        public double Atk { get; set; }
        public double Def { get; set; }
        public double Spd { get; set; }
        public double CritRate { get; set; }
        public double CritDmg { get; set; }

        /// <summary>ATB gauge; the unit acts each time it reaches BATTLE_GAUGE.</summary>
        public double Gauge { get; set; }

        public double Shield { get; set; }
        public int ShieldTicks { get; set; }
        public int TauntTicks { get; set; }

        /// <summary>Own actions left before each kit slot can be used again (indexed like ContentDb.Kits[HeroId]).</summary>
        public int[] Cooldowns { get; set; } = new int[0];

        public List<BattleEffect> Effects { get; set; } = new List<BattleEffect>();

        public bool Alive => Hp > 0;

        public BattleUnit Clone()
        {
            var c = (BattleUnit)MemberwiseClone();
            c.Cooldowns = (int[])Cooldowns.Clone();
            c.Effects = new List<BattleEffect>(Effects.Count);
            foreach (var e in Effects) c.Effects.Add(e.Clone());
            return c;
        }
    }

    /// <summary>A timed buff or debuff. Frac is a fraction of base for hp/atk/def/spd and is added directly to crit_rate/crit_dmg.</summary>
    public sealed class BattleEffect
    {
        public string Stat { get; set; }
        public double Frac { get; set; }

        /// <summary>Ticks left; -1 = permanent (passives).</summary>
        public int TicksLeft { get; set; }

        /// <summary>Skill id that applied it, so the AI does not re-apply an active effect.</summary>
        public string Source { get; set; }

        public BattleEffect Clone() => (BattleEffect)MemberwiseClone();
    }
}
