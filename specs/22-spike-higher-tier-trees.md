# 22 - Spike: skill trees and capstones for higher-tier creatures

Status: **spike report (agent, 2026-10-31; issue #110); kj answered section 6 the same day.** Only the answers in section 6 are agreed design; the rest is research. kj asked for research on skill trees and capstones for higher-tier creatures, then widened it: any interesting mechanic, interaction or synergy from RPG-like games, anime, manga or LitRPG that could inspire a tree or capstone. The starting point is *The Exiled Heavy Knight Knows How to Game the System*, which inspired the game. Section 6 records kj's answers, which are copied into `specs/12` section 5.8 and `specs/15` section 10.

## 1. Questions

1. How far does today's tree economy get a run toward a capstone, and is there room above it for a higher tier?
2. How much does a capstone move a monster's strength today?
3. Which mechanics elsewhere make a build feel "broken" in a fun way, and which would work in an auto-battler that the player watches?
4. What could make a higher-tier creature different from a base one, and not just bigger?

## 2. Measurements of today's trees

**Method.** A throwaway harness (not merged; it reuses `RunBot`'s policies, `TrainingRun.Apply`, `MonsterFighter` and `GauntletSim.Run` on content 3.13). It ran 200 seeds (1-200) for each species x run policy, both with no books and with the species' own skill book. The gauntlet is the 1v1 end-of-run climb (`specs/19`); floor 101 means cleared. The Daily Tower was not measured. Bots are simple: a planning human does better, so read these numbers as "what the current rules give for free", not as a ceiling.

### 2.1 Skill points and trees

| Policy | SP earned per run | Trees maxed at end (mean) |
|---|---|---|
| balanced | 26-30 | 0-2 |
| focus | 24-36 | 1.0-2.6 |
| reckless | 18-20 | 0-1 |
| hoarder | 28-35 | 1.7-2.7 |

A placeholder tree costs 11 SP, so a run can max **two or three trees at most**. A chase capstone needs its prerequisites (a maxed tree, or a book tree to node 6 plus a native tree to node 5: 11 to 13 SP) **plus** 11 SP for the chase tree itself. With the cost of the trees a run needs anyway, that is the whole budget of a good run.

### 2.2 How often a capstone is reached

Share of runs that end with a chase tree's capstone bought:

| Species | No books | Own book |
|---|---|---|
| slime | hoarder 69%, focus 4%, others 0% | hoarder 69%, focus 4%, others 0% |
| drake | 0% | 0% (chase tree slotted in 100% of balanced/focus runs, never maxed) |
| sprite | 0% | 0% |

- **When:** a capstone bought is bought in cycle 12 (mean 11.7-12.0), after the last rank fight. It plays only in the gauntlet and the Vault, never in the run that earned it.
- **What blocks it** (share of runs where the condition held at any point, own book):
  - Drake's *Unbreakable Will* needs DEF >= 500, which no run reached.
  - Sprite's *Starlit Ascension* needs SPD >= 500, which no run reached.
  - Sprite's *Arcane Overflow* needs a *Spellcraft* node; Spellcraft was there in 0-4% of runs.
  - The bots drill their best-growth stat (drake STR, sprite INT), not the stat their chase tree asks for. Part of that is the bots, part is the content: a chase condition fights the species' growth.
- **Books crowd each other out:** a run that brings every book fills its two empty slots with other species' book trees. A drake run's own chase tree then never unlocks (100% slotted with its own book only, 0% with every book). Books are not species-locked (`specs/15` section 5.1), so a player can make the same mistake.

### 2.3 What a capstone is worth

- Slime runs that bought their capstone climb about **5 floors higher** with it than the same monster with the rule stripped: 52 to 57 (hoarder), 57 to 60 (focus), about 10%.
- A run that **missed** its in-run capstone, handed only the rule (no tree nodes), climbs +4 to +8 floors (slime), +1.4 to +2.8 (drake), +0.6 to +3.2 (sprite).
- So today's capstones are **noticeable but not jackpots**. `specs/12` section 5.2 asks that a chase capstone be "the run's jackpot, not a bigger number".

### 2.4 Stat ceilings (the milestones a tier could gate on)

Share of runs whose top stat reaches each milestone (all three species are close):

| Policy | 500 | 700 | 900 |
|---|---|---|---|
| focus | 100% | 94-95% | 11-14% |
| hoarder | 100% | 74-80% | 1-2% |
| reckless | 78-85% | 4-5% | 0% |
| balanced | 0% | 0% | 0% |

700 in one stat is routine for a focused run. **900 is rare (about 1 run in 8 under focus)**, which makes it a natural gate for a jackpot.

### 2.5 What this means for higher tiers

- More or longer trees for a higher tier do nothing unless the SP economy changes too. Today the extra nodes would never be bought.
- The jackpot moment comes after the last fight, so the player never sees it in the run. The point dump (`specs/12` section 5.4) has no payoff on screen.
- Any higher-tier design should come with a reachability check in `balance` (capstone rate per species x policy). Today nothing reports it.

## 3. Research digest

Sources were fan wikis, guides and news pages, read through web search during the spike. Some mechanics were not confirmed and are marked so. The MonStack ideas are sketches in today's vocabulary (triggers, rule modifiers, tallies), with placeholder numbers.

### 3.1 The Exiled Heavy Knight (the original inspiration)

*Tsuihō sareta Tensei Jūkishi wa Game Chishiki de Musō suru*, a light novel by Nekoko. The anime premiered 2026-07-03 ([LiveChart](https://www.livechart.me/anime/12823), [ANN](https://www.animenewsnetwork.com/encyclopedia/anime.php?id=30434)).
- Elymas draws the "useless" Heavy Knight class and is exiled. He remembers the game this world copies, in which Heavy Knight is secretly the strongest class.
- His build is two trees: his native tank tree *Oath of the Heavy Armor*, and an illegal skill book, *Smoldering Fang of Madness* ([fandom](https://isekai.fandom.com/wiki/The_Heavy_Knight)).
- The combo ([TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/Literature/TheExiledHeavyKnightKnowsHowToGameTheSystem)):
  - *Life Shield* turns 20% of max HP into a shield, so he sits "below 50% HP" without being in danger.
  - That turns on the Fang skills, which only work below 50% HP.
  - *Relentless Vigor* moves DEF into ATK.
  - A Heavy Knight faith skill cancels the Frenzy the Fang tree inflicts.

This is MonStack's tree system almost one to one: a native tree plus a skill book tree, with the build living in how they combine. Four ideas carry over:

| Idea | MonStack shape |
|---|---|
| **Threshold enabler** | Capstone or skill: at fight start, turn 25% of current HP into a shield. Every "below 50% HP" trigger is then live while the monster is safe. Pairs with `tidal_overflow`. |
| **Stat conversion** | Rule: below 50% HP, DEF stops mitigating and is added to STR. A DEF-drilled run becomes a late-fight glass cannon. |
| **Drawback canceller** | A node in tree A that removes the drawback of tree B's capstone. It rewards owning both. |
| **Underdog species** | A low-rated species whose native trees look weak alone but combo absurdly with one book. It makes discovery a layer of the game. |

### 3.2 Mechanics catalogue

Grouped by what they do. Each row: the source and a MonStack idea.

**Conversion: one stat does another's job.**
- PoE *Iron Reflexes* (evasion becomes armour, [poewiki](https://www.poewiki.net/wiki/Iron_Reflexes)): evasion is 0, but DEX counts toward DEF.
- PoE *Mind over Matter* / *Eldritch Battery* ([poewiki](https://www.poewiki.net/wiki/Keystone)): a pool of INT x k takes 30% of hits first, and cooldowns tick faster while it holds.
- PoE *Avatar of Fire*: all damage becomes the tree's element at +40%.
- PoE *Elemental Equilibrium*: each hit lowers the target's resistance to the elements not just used.
- PoE *Blood Magic*: skills have no cooldown but cost 6% max HP.
- Bofuri's all-VIT Maple ([Looper](https://www.looper.com/756049/maples-machine-god-form-in-bofuri-explained)): a chase tree for DEF >= 900 with every other stat <= 150. Its capstone adds DEF to every hit.

**Restriction pays power.**
- Hunter x Hunter vows ([fandom](https://manga.fandom.com/wiki/Kurapika)) and Jujutsu Kaisen binding vows: an optional **vow node** before the capstone adds a restriction ("no Rest for 3 cycles", "INT attacks only") and doubles the capstone's numbers.
- Shangri-La Frontier's curse mark: no DEF gains from drills, but lower-ranked foes start at -20% stats.
- DOS2 *Lone Wolf*: x1.4 stats in 1v1, x0.8 in 4v4. That gives a Vault-identity choice that does not break PvP.

**Risk and low HP.**
- Monster Hunter *Resentment* ([fextralife](https://monsterhunterrise.wiki.fextralife.com/Resentment)): half of damage taken becomes "red HP" that adds to STR and decays when the monster acts.
- Monster Hunter *Heroics*, Fire Emblem *Vantage + Wrath*: below 50% HP, counters land before the enemy's hit.
- Darkest Dungeon *Death's Door* ([wiki](https://darkestdungeon.wiki.gg/wiki/Death%27s_Door_(Darkest_Dungeon_II))): at 0 HP, survive with a 70% chance per later hit, -10% per hit survived. Each hit is a visible coin flip.
- Monster Hunter *Latent Power*: after the 8th action, all stats +30%. It also guards against stalemates.

**Time and turns (the ATB gauge).**
- Bravely Default *Brave/Default* ([Wikipedia](https://en.wikipedia.org/wiki/Bravely_Default)): bank up to 3 turns while shielded or at full HP, then spend them in a burst. Show pips on the gauge.
- Persona *One More* ([megatenwiki](https://megatenwiki.com/wiki/One_More)), Fire Emblem *Galeforce*: a crit or super-effective hit refunds 40% of the gauge, once per action.
- Octopath *Break* ([GamerGuides](https://www.gamerguides.com/octopath-traveler/guide/introduction/game-mechanics/battle-system)): hits on a weakness strip guard pips. At 0 the foe skips an action and takes +50% damage.
- Slay the Spire *Echo Form* ([wiki](https://slay-the-spire.fandom.com/wiki/Echo_Form)): the first skill after each cooldown reset casts twice at 100%. An upgraded `echo_cast`.
- FF12 gambits: one pre-fight rule the player sets ("heal below 40%"). This is agency in an auto-battler, and it previews T11.

**Stacking and scaling.**
- Slay the Spire *Demon Form*: +6% STR per action, uncapped. It wins long fights and loses to burst.
- *Barricade*: shields never decay.
- Elden Ring bleed ([fextralife](https://eldenring.wiki.fextralife.com/Bleed)): hits fill a gauge at a rate set by DEX, and a full gauge deals 15% of the target's max HP. It answers high-HP tower bosses.
- Balatro *Blueprint*: **Mimic Core** copies the capstone of the tree in the slot to its left, so slot order becomes strategy.

**Absorption and snowball.**
- Berserk of Gluttony ([ANN](https://www.animenewsnetwork.com/review/berserk-of-gluttony/gn-1/.169626)), Tensura's Predator: **Devour**. Each rank-fight win permanently adds 5% of the opponent's best stat to the same stat; the cost is -1 energy every cycle.
- Solo Leveling *Shadow Extraction* ([fandom](https://solo-leveling.fandom.com/wiki/Shadow_Extraction)): **Arise**. The first enemy beaten in a tower comes back as a 30%-stat ally. It does nothing in 1v1 and is strong in 4v1 and 4v4.

**Combos between two things (the best fit for chase trees).**
- Vampire Survivors evolutions ([wiki](https://vampire-survivors.fandom.com/wiki/Evolution)), Hades Duo boons ([Prima](https://primagames.com/tips/hades-what-are-duo-boons-how-get-duo-boons)), Tensura skill fusion: two maxed trees **fuse** into one tree with a duo capstone and free a slot. Example: lifesteal + fire = "burn heals you".
- Hades *Privileged Status*: +40% damage against a target with 2 or more debuffs.
- Slay the Spire *Corruption + Dead Branch*: two halves across trees. One makes skills fire twice then lock; the other grants a random skill on each lock.

**Death and revival.**
- Inscryption *Unkillable*: **Phoenix Egg**, revive once at 40% HP with a full gauge.
- Super Auto Pets faint triggers: **Last Rite**, on death give allies 30% of your stats (4v4 only).

**Training and growth (the run layer, not combat).**
- Balatro interest ([guide](https://games.gg/balatro/guides/balatro-economy-guide/)): **SP interest**, +1 SP per 5 banked at cycle end, capped. It pays for the point dump directly; a capstone could raise the cap.
- Umamusume skill hints ([GameWith](https://gamewith.net/uma-musume/69504)): a Super drill discounts the matching tree's next node by 1 SP.
- Umamusume failure rate: a capstone where a Super refunds its energy, or where drills under 30% energy cannot injure but cannot Super.
- Monster Rancher errantry techniques ([legendcup](https://legendcup.com/faqmr2techs.php)): a chase condition as a chance rolled every cycle (sum of the named stats / threshold) rather than one hard gate.
- Monster Rancher lifespan: **Burn Bright**, drills +50% but each one removes a cycle at the end of the run.
- Slay the Spire *Ectoplasm* / *Snecko Eye*: **Ascetic** gives +1 AP per cycle but wins give no SP; **Chaos Drill** gives random drill costs but one extra drill per cycle.
- Hades keepsakes ([maxroll](https://maxroll.gg/hades-2/guides/hades-2-keepsake-guide)): filler nodes that level up with use, e.g. +growth every 3 drills of their stat.
- FF9 abilities learned through gear: a book tree's capstone becomes permanent after N wins, so overwriting the tree no longer erases it.
- Power Pro Super Abilities ([Wikipedia](https://en.wikipedia.org/wiki/Power_Pro_Kun_Pocket)): one "super" capstone per monster that raises a stat past its cap (`STAT_MAX`).
- Digimon World evolution ([guide](https://almarsguides.com/retro/walkthroughs/ps1/games/digimonworld/misc/raisingdigimon/)): 3 of 4 conditions (a stat, injuries <= N, a win streak, Rests). An injured run evolves into a "dark" form instead.
- Dragon Quest Monsters synthesis ([wiki](https://dragon-quest.org/wiki/Monster_synthesis)), Umamusume sparks ([GameTora](https://gametora.com/umamusume/inheritance-guide)): two Vault parents pass one tree or "spark" to a higher-tier child.

### 3.3 Patterns that make a build feel broken in a fun way

1. **Hidden or absurd unlock conditions** (Sword Art Online's Dual Blades, Bofuri, Shangri-La, Dungeon Crawler Carl). Chase trees already do this. Lean into extreme-stat and odd-action conditions.
2. **Restriction pays power** (vows, curse marks). The player chooses the drawback, so the power reads as earned.
3. **Conversion** (Relentless Vigor, Iron Reflexes). A "wrong" training plan becomes the right one.
4. **Threshold enablers** (Life Shield). A cheap node whose job is to switch on another tree's condition. Builds happen across slots.
5. **Fusion and evolution** (Tensura, Vampire Survivors, Digimon). Maxing trees becomes a new tier, not a number.
6. **Self-cancelling drawbacks** (the faith skill cancelling Frenzy). A risky capstone is paired with a planned fix.
7. **Uniqueness** (Dual Blades, Overlord World Items). A capstone only one Vault monster can hold.

### 3.4 Pitfalls

- **Loops.** A counter can answer a counter, an echo can echo itself, and lifesteal can feed an overflow shield that feeds a counter. Rules to adopt (Risk of Rain proc coefficients, [wiki](https://riskofrain2.wiki.gg/wiki/Proc_Coefficient)): a triggered effect never fires its own trigger type; cap the recursion depth; cap extra turns per round; apply conversions once, before multipliers (Grim Dawn).
- **Readability.** Proc soup is noise when nobody controls the fight. Keep capstone triggers rare and big, show each as a named banner, and count capstone procs in the battle log.
- **One-shot openers.** A free first turn plus Galeforce plus echo can kill a foe before it acts. Cap them, or make the opening action unable to kill.
- **Stalemates.** Barricade-plus-regen tanks never die. Keep a fight-length enrage (Latent Power style) as an engine fallback.
- **Mode asymmetry.** Last Rite, Arise and Lone Wolf swing hugely between 1v1, 4v1 and 4v4. `balance` must report each mode.
- **Strictly bigger tiers.** If a higher tier is just bigger numbers, base species become obsolete, and the Tower and PvP collapse onto the top tier.
- **Pure-luck gating** (Balatro's roughly 0.3% legendary jokers). It feels bad, and it contradicts the game's no-gacha stance. Gate on conditions instead.
- **Dead branches** (Monster Train paths nobody took, Diablo IV's legendary nodes weaker than the filler around them). Every capstone must beat the filler it replaces.

## 4. Options for higher-tier creatures

These options can be combined. Effort assumes today's engine: a new chase capstone is a coded rule modifier (`specs/16` section 6.2), and every new rule needs code, a golden fighter and a balance pass.

| # | Option | What makes the tier different | Cost | Main risk |
|---|---|---|---|---|
| A | **Late bloomer** (Pokémon pseudo-legendaries, DQM ranks) | Higher stat growth but lower early growth; power shows from cycle 9 and in the Vault | Content only | Still "bigger"; weak early fights in a 12-cycle run |
| B | **Fusion trees** (Tensura, Vampire Survivors, Hades duos) | Maxing two named trees fuses them into one tree with a duo capstone and frees a slot; only higher tiers fuse | New offer source and slot rule; one rule per fusion | Combinations to balance multiply |
| C | **Keystone capstones** (PoE, Heavy Knight, vows) | A higher tier's chase capstone is much stronger but always trades something away (conversion, a restriction, a drawback another tree can cancel) | Rule modifiers only | Harder to explain; the AI must play around the drawback |
| D | **A different SP economy** (Balatro interest, Uma hints) | The tier earns SP interest on banked points, or node discounts from Super drills; the point dump is how it is meant to be played | Run rules and a constant or two | Uncapped interest makes "never spend" dominant |
| E | **Mid-run evolution** (Digimon, Pokémon) | A species evolves around cycle 6 into one of 2-3 higher forms, by conditions (stats, injuries, wins, a tally); each form adds a 5th slot or a third chase tree | New run event, species links, UI | Overlaps achievements and chase trees as "unlock" systems |
| F | **Inheritance** (DQM synthesis, Uma sparks) | Higher tiers are bred from two Vault monsters and inherit a tree or sparks | Account and Vault rules, a new screen | Meta snowball; Vault monsters become fodder |
| G | **Team cost** (DQM Joker 3 sizes) | A higher tier takes 2 of the 4 team slots in towers and PvP | Team rules | Does nothing for the run itself |

## 5. Recommendation (agent)

1. **First, make today's capstones reachable and visible.** That is a prerequisite for any tier, and balance work, not new design:
   - add capstone rate and capstone cycle to `balance`;
   - let the bots drill what their chase conditions ask for, and bring only their own book;
   - bring the cost of capstones within reach, through cheaper chase trees, SP interest (D) or node hints (D), so a capstone is usually bought by cycle 9-10 and plays in the run's last fights.

   Tuning these numbers is a large retune, so the targets need kj's agreement (`CLAUDE.md`).
2. **Build higher tiers on C + B, with E as the way they arrive:**
   - a higher-tier species is reached by evolving a base species mid-run (E), or unlocked from achievements as today;
   - its chase capstones are keystones with a drawback (C), in the Heavy Knight shape;
   - its two native trees can fuse into a duo tree (B).
   This makes the tier deeper rather than bigger. It reuses the chase-condition machinery for evolution and fusion. And it echoes the anime that inspired the game.
3. **Hold back A, F and G** until the Tower and PvP show whether higher tiers dominate. G is the cheapest fix if they do.
4. **Adopt the loop and readability rules in section 3.4** before more trigger-heavy capstones are added. Add them to `specs/16` once kj agrees.
5. **First prototype:** one Heavy Knight-style pair, a "Life Shield" threshold enabler plus a "Relentless Vigor" DEF-to-STR conversion keystone, as a drake book chase tree. Measure it with the harness in section 2 before building any tier system.

## 6. Questions for kj (answered 2026-10-31)

1. **What is a higher tier?** **A later unlock** (kj): a higher-tier species is unlocked from account achievements like the others. Mid-run evolution (E) and breeding (F) are much further out on the roadmap.
2. **May higher tiers be stronger overall?** **Yes, like Monster Rancher** (kj). Option A is on the table; G stays the fallback if they dominate the Tower or PvP.
3. **Capstone reachability target.** kj: tune from the top down.
   - The **in-run chase capstone** is hard: about the **top 10% of runs**.
   - The **book chase capstone** is more consistent, since the player already needed luck to get the book: about **50% of runs that bring the species' own book**.
   - kj deferred the exact numbers to the agent. **(agent)** These are measured with the `balance` harness under the `focus` and `hoarder` policies, once the bots play toward their chase conditions (#111).
   - kj also asked for **features that make runs diverge**, through skill expression or RNG: runs are too deterministic today. That is spike #112 (`specs/23`).
4. **Drawback capstones?** **Yes** (kj): keystones that give something up suit chase trees.
5. **Fusion?** **Only for some specific pairs** (kj), and the association must be clear to the player: the UI shows which trees fuse.
6. **SP economy?** **Yes**, as a starting point (kj). SP interest and node hints lead into spike #112, which researches point economies with more variance and dynamism.

## 7. Follow-up

- Gap found during the spike: the bots never reach the drake and sprite chase capstones, and "every book" runs crowd out the species' own book. Filed as issue #111.
- The measurement harness was throwaway. If kj wants these numbers tracked, add capstone rate, capstone cycle and chase-condition hit rates to `balance` (`specs/09` section 7).
