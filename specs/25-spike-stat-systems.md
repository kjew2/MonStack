# 25 - Spike: stat systems, DEX, LUK and single-stat builds

Status: **spike report (agent, 2026-10-31); kj answered section 7 the same day, the DEX and LUK retune is built (content 4.6, section 9), and so are signature skills for the all-in builds that still lagged (content 4.7, section 10).** Only the answers in section 7 are agreed design; the rest is research. kj asked for field research before changing any stat: DEX needs a tweak, LUK should be more interesting, every all-in single-stat build should at least kind of work, and "magic cannot be dodged" looks like a problem, but kj wants to see other stat systems before fixing it. Section 7 has the questions for kj.

## 1. Scope and method

- **Baseline (section 2).** The bot CLI has new opt-in run policies `mono-<stat>` (`specs/09` section 4). Each one drills a single stat until it caps, then falls back to `focus`. The baseline is the `balance` verb over 200 seeds per species and policy, at content 4.4, with no books. A second pass looked at the stat sheet each run ended with: 8 seeds x slime, drake and sprite.
- **Research (sections 3-5).** Three surveys:
  - DEX, accuracy and evasion, and how games treat magic against evasion.
  - LUK and luck.
  - Whole stat systems: how games make every stat, or a dump-everything-into-one build, viable.
- **Sources.** Most of it is the researchers' own knowledge, with web checks where noted (sources in section 8). Formulas quoted from memory are marked (≈). They are inspiration, not specs.

## 2. Baseline: all-in builds today

### 2.1 Results

What a run reaches: a mono build ends with about 515-645 in its stat (range 270-875). Its other stats stay near species base, so max(STR, INT) is about 145. Mean grade (E = 0 ... S = 5) and mean gauntlet floor:

| Policy | slime | drake | sprite | mandrake | shade_hound | Gauntlet floor (mean of the 5) |
|---|---|---|---|---|---|---|
| `focus` (reference) | 4.67 | 2.41 | 3.00 | 2.31 | 2.99 | 38 |
| `mono-hp` | 4.67 | 2.16 | 3.00 | 2.31 | 2.92 | 42 |
| `mono-str` | 2.10 | 2.41 | 1.96 | 1.58 | 2.44 | 26 |
| `mono-int` | 2.30 | 1.24 | 3.00 | 1.82 | 1.24 | 25 |
| `mono-wis` | 2.47 | 1.02 | 2.69 | 1.95 | 1.46 | 24 |
| `mono-dex` | 1.78 | 1.14 | 1.91 | 1.25 | 2.99 | 23 |
| `mono-def` | 1.97 | 1.14 | 2.02 | 1.51 | 1.77 | 21 |
| `mono-spd` | 3.62 | 3.01 | 4.51 | 2.67 | 2.86 | 39 |
| `mono-luk` | 2.56 | 1.26 | 2.37 | 1.75 | 1.74 | 24 |

`focus` follows best growth, so it is the same as `mono-hp` for slime, `mono-str` for drake, `mono-int` for sprite and `mono-dex` for shade_hound.

### 2.2 What about 550 points buys against a B-rank opponent

B-rank opponents have about STR 310, DEX 300, DEF 300, SPD 250, LUK 400.

| Stat | What 550 does | Size |
|---|---|---|
| SPD | 2.2x the opponent's actions | Multiplies everything: damage, heals, buffs |
| HP | 5,500 max HP | About 1.8x a B opponent's 3,100 |
| DEF | Physical damage taken x 0.35 (x 0.67 at base) | About 2x effective HP, physical only |
| WIS | The same against magic, plus heals and shields | About 2x effective HP, magic only |
| STR / INT | About 3.5x the basic attack | No survivability |
| LUK | Crit 0.05 + 0.275 = 33%, x 1.5 | +16% expected damage |
| DEX | Evade 0.05 + (550 - 300) x 0.0005 = 17.5% | 1.2x effective HP, physical only, zero against magic |

### 2.3 Findings

1. **DEX is the weakest point in the game.** DEX and DEF are the two bottom rows of the grade table. 550 DEX buys about a fifth of what 550 DEF buys. Three reasons:
   - Evasion depends on the DEX *difference*, and opponents' DEX climbs from 100 (E) to 475 (S). So the edge a mono-DEX monster has shrinks with every rank.
   - It does nothing against magic.
   - It adds no damage.

   Shade Hound is the exception (2.99): its `umbral_veil` capstone extends evasion to magic and raises the cap.
2. **LUK is a small damage multiplier and nothing else.** +16% expected damage on a base of 145 is about +23 damage per hit. Opponents' LUK is the highest stat on every rank sheet (825 at S), so the player meets crits far more than they land them.
3. **No build deals zero, but most deal very little.** Species base STR or INT keeps every basic attack alive. "Kind of works" fails on damage per action, not on zero damage.
4. **SPD is the strongest all-in build for 3 of 5 species, and it beats `focus` for drake and sprite.** Gauge fill is linear in SPD, so it multiplies everything else. This is not what kj asked about, but any DEX or LUK change will be compared against it. Flagged in Q6.
5. **HP is strong; STR alone is weak.** Mono-STR (515 STR) does worse than mono-HP and mono-SPD for every species except drake. Damage without survival or tempo loses the race against healers and casters (issue #114 saw the same for drake `chaser`).

## 3. Field research: DEX, evasion, and magic against evasion

### 3.1 Four ways games split physical and magic avoidance

| Family | How it works | Games | Fit here |
|---|---|---|---|
| A. Two parallel pairs | Separate evade and magic evade stats | FF6 Evade / MBlock, FF9, FF12; FF Tactics *Faith* scales both the caster's and the target's magic | A second evade stat is clunky with 8 stats. FFT's "one stat scales both sides of a channel" is elegant. |
| B. One evasion for everything | Agility / avoid checks every attack, spells included | Persona / SMT (Agility), Pokemon (accuracy and evasion stages on every move), Fire Emblem (magic uses the same avoid in most entries), Darkest Dungeon (ACC vs DODGE on every skill), Monster Rancher (SKI = hit, SPD = evade, for every technique), DCSS (bolt spells check EV) | The simplest fix: DEX dodges everything. Risk: binary dodge against everything is the most frustrating version (evasion moves are banned in competitive Pokemon). |
| C. Saves: a different mechanic against magic | Magic is not dodged; DEX makes it hurt less | D&D 5e DEX save for half (Evasion feature: half becomes zero), Pathfinder Reflex, Path of Exile spell suppression (chance to take 40-50% less spell damage; it replaced full spell dodge in 3.16 because stacking full dodge was degenerate) | Fixes "magic is undodgeable" while magic stays reliable. Strong fit. |
| D. Magic always hits | Evasion is a physical identity | Dragon Quest, Dota (evasion physical only, countered by true strike), Diablo 2, old WoW, **this game today** | Only works when the physical identity pays off elsewhere, which ours does not. |

### 3.2 What DEX could do besides a binary dodge

| Pattern | Mechanic | Games | Magic? | Pure-DEX damage? |
|---|---|---|---|---|
| Graze / glancing | Rolls just outside the evade window deal 25-50% | XCOM Long War 2, WoW glancing | Yes, if magic can graze but never be fully dodged | No |
| Reflex save / suppression | DEX-scaled chance to halve magic damage | D&D, Pathfinder, PoE | **Yes** | No |
| Precision surplus | Attacker DEX above target DEX adds damage, or promotes graze -> hit -> crit | XCOM LW2 crit promotion, Disgaea (HIT scales guns and bows) | - | **Yes**, strongest against clumsy tanks |
| Finesse attack | Basic attack or some skills use DEX instead of STR | D&D finesse, Dark Souls DEX weapons, Ragnarok bows | - | **Yes, directly** (mirrors our "magical when INT > STR" rule) |
| Riposte | A dodge can trigger an instant counterattack | FFT reactions (Brave%), Battle Brothers Riposte, Monster Rancher counters | Only if magic can be graze-countered | Yes, against physical attackers |
| Double strike | DEX surplus gives a chance at a follow-up hit | Epic Seven dual attack, FE doubling | - | Multiplies whatever damage exists; overlaps SPD |
| Status accuracy | DEX = effect hit, WIS = effect resist | Summoners War, Epic Seven, Honkai: Star Rail | Indirectly | Through DoTs, once statuses exist |
| Evade -> buff | Each dodge stacks something (gauge, damage, a guaranteed crit) | WoW Overpower, PoE | - | Somewhat |
| Anti-frustration | Evasion builds entropy and lands hits on a cadence; or evasion decays with each consecutive dodge | PoE entropy evasion, Battle Brothers defense loss per attacker, FE "true hit" averaging two rolls | - | No; it stops a capped evader being invincible |

Ragnarok Online is the canonical case of a pure-DEX build that works. DEX there gives accuracy, bow damage and cast speed (≈ cast x (1 - DEX/150)), which made DEX hunters and DEX wizards. We should not copy the cast-speed part: SPD already owns tempo. Keep Monster Rancher's split: SPD for tempo, DEX for precision and avoidance.

## 4. Field research: LUK

| # | Pattern | Mechanic | Games | Pure-LUK damage? | Touches other stats' RNG? |
|---|---|---|---|---|---|
| A | **Fortune (advantage)** | A chance to roll twice and keep the better result on your rolls (variance, crit, evade, procs, endure), and the worse result on enemy rolls against you | D&D advantage and the Lucky feat, PoE "lucky", Risk of Rain 2 Clover (`1 - (1-p)^(n+1)`), Terraria, *Mewgenics* (2026, a cat-breeding tactics roguelite: each Luck point above 5 is about a 10% extra die) | Indirectly | **Yes, every roll.** Luck as mastery over the RNG, in a game named idle-rng. |
| B | Crit avoidance | Defender LUK lowers incoming crit | Fire Emblem (Lck = crit avoid), Ragnarok (≈ -LUK/5), Persona | No | Crits only. Today, opponents' huge LUK would make this matter a lot. |
| C | Crits that pierce or add flat damage | A crit ignores part of DEF/WIS, or adds LUK x k | Kingdom Death (a crit wounds regardless of toughness), Pokemon (crits ignore defensive buffs) | **Yes** | Weakens mitigation on crits |
| D | Gamble attack | A LUK-scaled dice skill that ignores STR and INT; doubles crit, snake eyes whiff | FF6 Setzer (≈ Fixed Dice), Persona Lucky Punch, Dark Souls luck weapons | **Yes, directly** | Fortune can reroll the dice |
| E | Proc multiplier | LUK raises every "X% chance to..." talent and trigger | Binding of Isaac (Luck drives nearly every tear-effect proc, many reach 100%), Risk of Rain 2, FFT Brave = reaction rate, Pokemon Serene Grace | Through procs | Makes LUK the hub of trigger trees |
| F | Status infliction / resist | Ailment chance shifted by LUK difference | Etrian Odyssey, Persona / SMT, Ragnarok | Through DoTs | Needs statuses |
| G | Endure / miracle | Lethal hit: a LUK-scaled chance (capped, once per fight) to survive at 1 HP | FE *Miracle* (Luck% when a hit would kill), Pokemon Focus Band, Hades Lucky Tooth | No, but buys time | Rare, visible, dramatic |
| H | Variance shaping | LUK raises the damage floor; negative luck causes feeble hits | Terraria, Monster Hunter negative affinity (x0.75) | No | Every attacker. We have no damage variance today. |
| I | Pity / anti-streak | Pseudo-random distribution on procs and crits; LUK tightens it | Dota 2 PRD, gacha pity | No | Feel only |
| J | Run luck | Fewer drill injuries, bonus gains, a draft reroll, better dungeon loot | Diablo magic find, Stardew, Elden Ring discovery, Slay the Spire extra choices | No | Every RNG outside battle. Watch for "LUK is mandatory" (the magic-find trap). |

Lesson from Balatro and Isaac: luck only feels like something when the odds are printed and the moment it helped is shown. A "Lucky!" tag on the rolls that changed, and "Fortune 31%" on the stat sheet, matter as much as the formula.

## 5. Field research: whole stat systems

Patterns for making every stat viable:

| Pattern | Games | Meaning here | Watch for |
|---|---|---|---|
| **Per-skill scaling stat** | Pokemon (Body Press = DEF, Foul Play = target ATK, Electro Ball = SPD ratio, Gyro Ball = slower is stronger, Seismic Toss fixed, Final Gambit = own HP), Elden Ring weapon grades, Brotato weapon classes, Epic Seven and Summoners War (SPD-, HP- and DEF-scaling skills), LoL (Malphite armor, Cho'Gath HP), Genshin (Noelle DEF, Hu Tao HP, capped at 4x base ATK), Monster Rancher (POW or INT per technique) | Our skills already carry a `scales` stat. A generic tree per stat with one skill scaled by that stat (*Iron Press* DEF, *Smite* WIS, *Flurry* SPD, *Precision* DEX, *Gamble* LUK, *Body Slam* HP) | Mono-X needs the tree to show up in its draft. Milestone drafts already offer the stat's own generic tree. (Since issue #121 an Awakening offers the stat's own tree plus any stat's trees, rolled; see section 10.) |
| **Every stat has one offense and one defense line** | Ragnarok Online (each of 6 stats touches both), D&D (each ability is a save plus skills) | An audit for the stat table in `specs/16` section 3 | Too many small effects make stats blur. Keep one headline per stat. |
| **Damage floor on the basic attack** | Dota universal heroes (≈ 0.7 x every attribute), Ragnarok minor ATK from DEX and LUK | A small share of the other stats in the basic attack | A `max(all stats)` attack erases identity: every stat becomes STR. |
| **Conversion keystones, capped** | PoE Iron Will (STR bonus applies to spells), Hu Tao, Noelle, Brotato characters | Talents or tree nodes: "40% of DEF counts as STR", "WIS counts as INT for smites" | Cap it or tax it, or the converted stat is strictly better. |
| **Retaliation** | LoL Thornmail and Rammus, TFT Bramble, Backpack Battles spikes, Slay the Spire Body Slam, Pokemon Counter | DEF already has `counter_mitigated` (*Unbreakable Will*). A WIS version against magic is missing. | Two turtles never end the fight. |
| **Timeout / overtime win** | Monster Rancher (a timed-out fight goes to the monster with more life left), The Bazaar sandstorm, Backpack Battles fatigue | `MON_TIME_LIMIT_TICKS` already ends fights `TimedOut`, and the caller decides. HP% on timeout plus a visible overtime would give HP, DEF and WIS a win plan. | Timeouts are dull to watch; overtime should be short and loud. |
| **Threshold milestones** | Fire Emblem doubling at +4/5 SPD, Elden Ring soft caps, D&D feats | `stat_milestones.csv` exists. Milestones could grant the stat's signature effect at 500 and 750. | Dead zones just below a threshold |
| **Relative-stat effects** | Electro Ball / Gyro Ball, Summoners War Savannah (relative SPD) | Matchup texture for SPD and DEX | Mirror matches make them zero (already true for DEX evasion) |

Warnings from the field:
- **Brotato** removed Harvesting scaling from its Sickle, because it "almost inherently" ends up "either overly weak or overly strong". Read that as a caution against LUK as a raw damage multiplier; prefer LUK feeding procs and rolls.
- **Diablo 3** made the primary stat everything, and the off-stats lost all identity.

## 6. Candidate packages (agent recommendation, for discussion)

Each package is independent and is measured with `balance --policies focus,mono-<stat>...`.

**DEX: the precision stat (fixes "magic is undodgeable" without making magic missable).**
1. **One avoid roll for every hit, but magic can only be grazed.** A physical hit that rolls under the evade chance misses as now. A magical hit that rolls under it is a *graze*: it takes `MON_GRAZE_MULT` (proposed 0.5) of its damage. This is the reflex-save and spell-suppression idea, shown as "Graze" in the log. `umbral_veil` keeps its identity: it turns those grazes into full dodges.
2. **Rescale the evade curve.** Evasion should come from a ratio, not a difference, so DEX still matters at S rank. For example `evade = CAP x DEX_t / (DEX_t + DEX_a x k)`, or the difference formula with a larger `MON_EVADE_PER_DEX`.
3. **Precision on offense:** attacker DEX above target DEX adds damage, `x (1 + max(0, DEX_a - DEX_t) x MON_PRECISION_PER_DEX)`. Or the softer option: the basic attack becomes finesse, using `max(STR, INT, DEX x MON_FINESSE)` with a factor below 1. Pick one, not both.
4. Optional: a riposte trigger on evade (a tree node, not a base rule). `OnEvade` already exists.

**LUK: the stat that bends the RNG.**
1. **Fortune.** `fortune = LUK / (LUK + MON_FORTUNE_K)`, capped. On each of the unit's own crit and proc rolls, and on its evade rolls when defending, it has that chance to draw again and keep the better. Every LUK point then touches DEX, crits and all trigger trees. Every reroll is drawn whether or not it is used, so the RNG stream, hashes and golden diffs stay stable.
2. **Crit avoidance:** defender LUK subtracts from incoming crit chance. This also tames the S-rank opponents' 825 LUK.
3. A generic *Gamble* skill (LUK-scaled dice damage) in the LUK milestone tree, so mono-LUK has its own damage line.
4. Optional, later: an endure chance once per fight, or run luck (injury chance, draft rerolls). Run luck touches `specs/14` and the training economy, so it needs its own decision.

**All-in builds.**
1. One signature skill per stat in the milestone trees, scaled by that stat (section 5, first row). This is the cleanest fit for "every build kind of works" and keeps identity.
2. Timeout by HP%, with overtime, as the win plan for HP, DEF and WIS. It touches gauntlet, Dungeon and PvP rules, so it is kj's call.
3. A loose harness check, `mono-viable`: every mono-<stat> policy reaches some floor for at least one species. A report only, like the other checks, and only once kj sets the floor.

**Not recommended:**
- A separate magic-evade stat.
- A `max(all stats)` basic attack.
- DEX as cast or action speed (SPD owns tempo).
- Full binary dodge against magic at the current 50% cap (the most frustrating option in the field).

## 7. Questions for kj (answered 2026-10-31)

kj's answers, in order: (1) graze, "maybe 25%", tuning left to the agent; (2) DEX as a crit damage scaler, if sound; (3) LUK rerolls dodge as well, and defends against crits; (4) an all-in build should reach about D or C when the rest is played sensibly, and all-in DEX should at least win some fights; (5) no timeout-by-HP% rule: it would overtune HP and heal builds; (6) leave SPD as it is and make the other stats stronger. The questions as asked:


1. **Magic and DEX.** Which family (section 3.1)?
   - B: DEX dodges magic like physical.
   - C: magic can only be grazed or halved (recommended).
   - D: keep magic undodgeable and fix DEX elsewhere.
2. **DEX on offense.** Which one?
   - Precision surplus as a damage multiplier (recommended).
   - Finesse basic attack.
   - Riposte only.
   - None (DEX stays purely defensive).
3. **LUK headline.** Which one?
   - Fortune, advantage on rolls (recommended).
   - Crits that pierce or add damage.
   - Proc multiplier for trigger trees.
   - Gamble skill as the main identity.

   Several can stack; the question is which one LUK's tooltip leads with.
4. **"Kind of works."** What bar should an all-in build meet? For example: mean grade within 1 of `focus` for at least one species, or a median gauntlet past the first boss floor. And should it become a sanity check?
5. **Timeouts.** Should a timed-out fight go to the side with the higher HP%, with an overtime ramp? This gives HP, DEF and WIS a win plan, and touches gauntlet, Dungeon and PvP rules.
6. **SPD.** Mono-SPD beats `focus` for 2 species, because gauge fill is linear. Leave it, or put SPD on diminishing returns as part of this retune? Any DEX or LUK change will be measured against it.

Changing these formulas moves every fight. That counts as a large retune (`CLAUDE.md`), so targets come before code.

## 8. Sources

Web checks during this spike:
- PoE wiki: [Evade (entropy)](https://www.poewiki.net/wiki/Evade), [Spell suppression](https://www.poewiki.net/wiki/Spell_suppression), [Lucky](https://poewiki.net/wiki/Lucky), [Iron Will](https://www.poewiki.net/wiki/Iron_Will)
- [UFOpaedia: Long War 2 mechanics](https://ufopaedia.org/Mechanics_(LW2)) (graze band, crit promotion)
- Monster Rancher: [DX manual](https://www.koeitecmoamerica.com/manual/mrdx/mr2/en/2300.html), [LegendCup technique data](https://legendcup.com/faqmr3techs.php)
- [Binding of Isaac wiki: Luck](https://bindingofisaacrebirth.wiki.gg/wiki/luck)
- [Risk of Rain 2 wiki: Luck](https://riskofrain2.wiki.gg/wiki/Luck)
- [iRO wiki: classic attack formulas](https://irowiki.org/classic/Attack)
- Fire Emblem wiki: [Miracle](https://breezewiki.discard.no/fireemblem/wiki/Miracle), [Luck](https://breezewiki.discard.no/fireemblem/wiki/Luck)
- [Megaten wiki: Critical](https://megatenwiki.com/wiki/Critical)
- [FFT Bravery and Faith](https://www.rpgsite.net/guide/18606-final-fantasy-tactics-the-ivalice-chronicles-how-bravery-and-faith-works)
- Mewgenics luck: [roonby guide](https://roonby.com/2026/02/18/mewgenics-luck-guide-explained-what-luck-actually-does/), [wiki](https://mewgenics.wiki.gg/wiki/Event)
- Brotato wiki: [Stats](https://brotato.wiki.spellsandguns.com/Stats), [Patch 0.8.0.3](https://brotato.wiki.spellsandguns.com/Patch_0.8.0.3)
- [Summoners War: Shumar speed formula](https://summonerswarskyarena.info/monster/shumar-dark-hellhound-review/)
- [Epic Seven skills (Game8)](https://game8.co/games/Epic-Seven/archives/276553)
- TFT items: [Titan's Resolve](https://wiki.leagueoflegends.com/en-us/TFT:Titan%27s_Resolve)

Everything else is from the researchers' own knowledge, and formulas marked (≈) may be off in the details.

## 9. The retune (content 4.6)

Built from the answers in section 7. The rules are in `specs/16` sections 3 and 5.3:
- **Graze.** Every hit rolls against the target's evade chance. A physical hit under it misses; a magical hit under it is grazed and deals `MON_GRAZE_MULT` = 0.25 of its damage, with no crit. `umbral_veil` makes those grazes full evades.
- **Evade curve.** `MON_EVADE_PER_DEX` 0.0005 to 0.001, so 550 DEX against a B rank's 300 evades about 30% instead of 17.5%.
- **DEX crit damage.** A crit deals `MON_CRIT_DMG` + DEX x `MON_CRIT_DMG_PER_DEX` (0.001), so 550 DEX makes a crit x2.05 instead of x1.5.
- **Fortune.** `min(0.5, LUK / (LUK + 1000))`. It rerolls the unit's own failed evade (graze included) and failed crit, and a crit against it rolls again. The battle log marks those hits "lucky!" and grazes "(grazed)".

Graze took the 25% kj suggested: in the measurements, 0.25 and 0.5 differed by less than 0.1 grade for every cell. Grade means at 200 seeds, before and after (E = 0, D = 1, C = 2):

| Policy | slime | drake | sprite | mandrake | shade_hound | avg | rank fight win rate |
|---|---|---|---|---|---|---|---|
| `mono-dex` before | 1.78 | 1.14 | 1.91 | 1.25 | 2.99 | 1.81 | 15% |
| `mono-dex` after | 2.02 | 1.51 | 2.25 | 1.29 | 3.38 | 2.09 | 17% |
| `mono-luk` before / after | 2.56 / 2.50 | 1.26 / 1.25 | 2.37 / 2.38 | 1.75 / 1.72 | 1.74 / 1.90 | 1.94 / 1.95 | 16% / 16% |
| `mono-def` before / after | 1.97 / 1.93 | 1.14 / 1.06 | 2.02 / 2.03 | 1.51 / 1.43 | 1.77 / 1.84 | 1.68 / 1.66 | 14% / 14% |
| `focus` before / after | 4.67 / 4.18 | 2.41 / 2.29 | 3.00 / 2.91 | 2.31 / 2.12 | 2.99 / 3.38 | 3.08 / 2.97 | 26% / 25% |

What changed:
- **All-in DEX** moved from second-worst to level with all-in STR (2.04). Its median rank fights won rose to 2 for drake, and the drake mean rose from 1.1 to 1.5.
- **Every all-in build** now averages D or better on every species. All-in WIS and all-in DEF on drake sit right at D (1.00 and 1.06).
- **LUK** barely moves in the means, because rank opponents carry the highest LUK of any stat (825 at S), so their fortune cancels most of the player's. LUK now does what kj asked (rerolls dodges, defends crits); making the all-in build stronger is the follow-up below.
- **Low-DEX builds** lose a little: opponents' DEX now dodges more of their hits. Slime `focus` (all-in HP) went from 4.67 to 4.18.
- **Magic** grazes against high-DEX opponents, so all-in INT lost about 0.07.
- **Sanity checks:** all 12 pass in the default `balance` run.

Follow-up: all-in DEF is now the weakest build (1.66). LUK, WIS and INT are close behind (about 1.9). A pass to make those stronger is issue #117 (section 10).


## 10. Signature skills for the lagging all-in builds (content 4.7, issue #117)

kj's bar (section 7, answers 4 and 6): every all-in build should reach about D to C, and the weak stats get stronger rather than SPD weaker. After content 4.6 all-in DEF, WIS, INT and LUK still trailed all-in STR and DEX. The fix follows section 6 ("All-in builds", option 1): each lagging stat's own Awakening tree (the generic tree its 300 milestone drafts, `specs/15` section 4) holds a damage skill scaled by that stat, at index 1, right after the first growth node. A run then has it as soon as it puts 3 SP into the tree; at index 4 (where *Gamble* and *Arcane Blast* sat) the bots only reached it in the last cycles, after the native trees.

**Any-stat Awakenings (content 5.2, issue #121).** Awakenings now offer any stat's trees, rolled (`specs/15` section 4.1), so an all-in run finds its stat's tree (and its signature skill) only when the draft deals it, about 3 cards in 8 at 300. Measured 200 seeds, no books, the bots picking the card for the stat they drill: `mono-luk` mean rank 1.31 to 2.20 per species (was 1.68 to 2.49), `mono-def` 1.70 to 2.47 (1.82 to 2.87), `mono-wis` 1.20 to 2.63 (1.33 to 3.00); the default policies moved by at most 0.15. Every all-in cell stayed at D or better. kj then had the crossing stat's own tree always be the first card (2026-10-31, issue #121; `specs/15` section 4): every `mono-<stat>` cell and every default policy is back within 0.15 of its mean rank before any-stat Awakenings (e.g. `mono-luk` 1.67 to 2.54).

| Tree | Index 1 (was) | Skill |
|---|---|---|
| *Bulwark* (DEF) | +15 DEF | *Iron Press* `iron_press`: physical damage 120% DEF, cooldown 2 (new) |
| *Sage Mind* (WIS) | +15 WIS | *Smite* `smite`: magical damage 160% WIS, cooldown 2 (new) |
| *Fortune* (LUK) | +15 LUK, now at index 4 | *Gamble*: physical damage 250% LUK, cooldown 4 (moved from index 4) |
| *Spellcraft* (INT) | +15 INT, now at index 4 | *Arcane Blast*: magical damage 200% INT, cooldown 3 (moved from index 4) |

One AI fix came with it: a taunt is used only when the unit has a living ally to protect (`specs/16` section 5.2). *Bulwark*'s *Provoke* used to spend every fourth action of a lone DEF monster on a taunt that changes nothing in 1v1. On its own this fix moved all-in DEF from 1.66 to 1.78 (drake 1.06 to 1.41).

**Opponents' LUK (section 6 option 2) was measured and not built.** Lowering every rank opponent's LUK to at most 10% above its highest other stat (S 825 to 550), with the points spread over its other stats so the rank budget holds (`specs/14` section 5), lowered every policy by about 0.05 to 0.15 and did not help all-in LUK (1.95 to 1.94): what the opponents gain in the other stats outweighs what the player's fortune and crits gain. The rank sheets keep their LUK.

Grade means at 200 seeds, no books, content 4.6 before and 4.7 after (E = 0, D = 1, C = 2; rank fight win rate averaged over the five species):

| Policy | slime | drake | sprite | mandrake | shade_hound | avg | win rate |
|---|---|---|---|---|---|---|---|
| `mono-def` | 1.93 / 2.79 | 1.06 / 2.17 | 2.03 / 2.17 | 1.43 / 2.26 | 1.84 / 1.81 | 1.66 / 2.24 | 14% / 19% |
| `mono-wis` | 2.37 / 3.00 | 1.00 / 1.34 | 2.65 / 2.88 | 1.88 / 2.49 | 1.52 / 1.52 | 1.89 / 2.25 | 16% / 19% |
| `mono-luk` | 2.50 / 2.49 | 1.25 / 1.68 | 2.38 / 2.48 | 1.72 / 1.76 | 1.90 / 1.73 | 1.95 / 2.03 | 16% / 17% |
| `mono-int` | 2.10 / 2.13 | 1.16 / 1.42 | 2.91 / 2.92 | 1.75 / 1.77 | 1.34 / 1.34 | 1.85 / 1.92 | 15% / 16% |
| `mono-str`, `mono-dex` (reference) | unchanged | | | | | 2.04, 2.09 | |
| `focus` | 4.18 / 4.18 | 2.29 / 2.29 | 2.91 / 2.92 | 2.12 / 2.12 | 3.38 / 3.38 | 2.97 / 2.98 | |

- Every all-in build now averages about C (1.92 to 2.25, SPD and HP still above). The drake DEF and WIS cells, which sat right at D, are at 2.17 and 1.34.
- All-in DEF and WIS end slightly above all-in STR and DEX (2.24 and 2.25 against 2.04 and 2.09), mostly from slime and mandrake, the species that grow those stats best. kj (2026-10-31): keep them as they are, no trim.
- The default policies (`focus`, `balanced`, `hoarder`, `chaser`, `reckless`) and `mono-hp`, `mono-str`, `mono-dex` and `mono-spd` move by 0.01 at most: they rarely slot these trees early.
- The weakest cells are shade_hound's all-in INT, WIS, LUK and DEF (1.34 to 1.81): unless it trains HP, its 1,000 max HP dies to C-rank casters in a few hits, and those stats grow slowly for it (growth 0.6 to 1.1). That is a species question, not a stat one.
- **Sanity checks:** all 12 pass in the default `balance` run.
