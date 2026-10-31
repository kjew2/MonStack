# 22 - Spike: skill trees and capstones for higher-tier creatures

Status: **spike report (agent, 2026-10-31; issue #110); kj answered section 6 the same day.** Only the answers in section 6 are agreed design; the rest is research. kj asked for research on skill trees and capstones for higher-tier creatures, then widened it: any interesting mechanic, interaction or synergy from RPG-like games, anime, manga or LitRPG that could inspire a tree or capstone. The starting point is *The Exiled Heavy Knight Knows How to Game the System*, which inspired the game. Section 6 records kj's answers, which are copied into `specs/12` section 5.2.1 and `specs/15` section 10. **Section 8 is an addendum (agent, 2026-10-31)**: kj asked to widen the research beyond anime, to manhwa and webtoons, donghua and Chinese web novels, and light novels, *Lord of the Mysteries* in particular. kj answered section 8.6 the same day; those answers are copied into `specs/12` section 5.2.1 and `specs/15` sections 4 and 10. The rest of section 8 is research.

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

## 8. Addendum: manhwa, donghua, web novels and light novels (agent, 2026-10-31)

kj asked to widen section 3 beyond anime and Western games, to manhwa and webtoons, donghua and Chinese web novels, and light novels, which often have more detailed power systems. *Lord of the Mysteries* came first. The same caveats as section 3 apply. Sources are fan wikis, fan guides and reviews found by web search (the fan wikis could not be fetched from the session, so some details come from search summaries). Rules the sources did not confirm are marked **(unconfirmed)**. The sketches use today's vocabulary (trees, nodes, tallies, rule modifiers, rank fights) with placeholder numbers.

### 8.1 Does *Lord of the Mysteries* apply?

**Partly, and the useful part is the most unusual one.** The setting (22 pathways, Sequences 9 to 0, gods, Uniqueness) is cosmology, and MonStack does not need that. But four of its rules map onto systems MonStack already has ([Acting Method](https://lordofthemysteries.fandom.com/wiki/Acting_Method), [Sequence](https://lordofthemysteries.fandom.com/wiki/Sequence), [Above the Sequence](https://lordofthemysteries.fandom.com/wiki/Above_the_Sequence), [Moegirl: pathways](https://zh.moegirl.org.cn/en/%E9%9D%9E%E5%87%A1%E9%80%94%E5%BE%84)):

| LotM rule | What it does there | MonStack idea |
|---|---|---|
| **Acting method** | A potion is digested by living out its name (a Seer acts as a seer). Only a digested potion lets you advance safely. | **Digestion.** A tree's deep nodes (or its capstone) need a run tally that fits the tree's role, not only SP. A tank tree digests through damage mitigated, *Pyromancy* through fire damage, a speed tree through evades. Tallies exist already (`specs/15` section 6); today they gate only chase trees. |
| **Loss of control** | Advancing before the potion is digested risks madness. | **Overreach.** Buying a node before it is digested is allowed. Each overreached node gives a chance per rank fight that the monster goes berserk for 2 turns (random target, no skills). It is a drawback the player chooses, like the agreed keystone capstones (section 6.4), and it reuses the Frenzy shape from section 3.1. |
| **Named Sequence ladder** | Each step on a pathway has a title (Seer, Clown, Magician, Faceless...). | **Titled skill nodes.** A tree's skill nodes carry names that read as a rank ladder, and the deepest one reached goes into the Legacy Title. This is presentation only, and cheap. |
| **Neighbouring pathways** | From Sequence 4, a Beyonder can switch to a neighbouring pathway in the same group; any other switch risks madness and stops advancement. | **Lateral swap.** Each tree lists 1 or 2 neighbours. Overwriting a tree with one of its neighbours keeps half the SP spent (today overwriting loses all of it, `specs/12` section 5.1). The UI draws the links. It fits kj's "fusion only for specific, clearly shown pairs" (section 6.5), with a lighter rule. |

A fifth rule is for later: when a Beyonder dies, their **characteristic** stays behind, and related characteristics are drawn to each other (convergence). In MonStack, dismantling a Vault monster could leave its deepest tree as a one-use book. That belongs with inheritance (option F), which kj put far out (section 6.1).

**Tension with the point dump.** Digestion spends tallies over time, while the stat-wall point dump (`specs/12` section 5.4) spends SP in one burst. The two fit if digestion **banks like SP**: the tally is counted from the run's start, not from when the tree was slotted. A player who drilled and fought "in role" before the 700 draft can still dump every point at once. That also makes runs diverge through skill, which is what spike #112 (`specs/23`) is after: two runs with the same SP can reach different depths.

### 8.2 Catalogue

**Loot from the fallen: rank fights drop power, and the player chooses it.**
- *Soul Land* spirit rings ([fables.gg](https://fables.gg/worlds/soul-land-fpclts81ic9f/lore/soul-rings), [Anibase](https://anibase.net/en/anime/l3aRN/Soul-Land)): rings come from slain spirit beasts and are coloured by the beast's age (white 10 years, yellow 100, purple 1,000, black 10,000, red 100,000). Each soul master has an absorption limit; going over it is "limit surpassing", possible but dangerous. **Rank rings:** each rank-fight win offers the opponent's ring, coloured by the opponent's rank (E white up to S red). A monster has a few ring slots, each a small passive from that opponent. Taking a ring above the limit (a WIS- or HP-based number) is an injury roll for a bigger ring. Spirit bones drop rarely; the source says resentment or a willing death raises the odds. **Bone drop:** a decisive win (no damage taken, or a KO in 3 turns) has a chance to drop a permanent bonus.
- *Reincarnated as a Sword* ([Wikipedia](https://en.wikipedia.org/wiki/Reincarnated_as_a_Sword), [ANN](https://animenewsnetwork.com/review/reincarnated-as-a-sword/ebook-1/.147368)): breaking a beaten monster's magic stone absorbs one of its skills, and repeated skills level up into Superior versions. **Absorb:** a win lets the monster copy one opponent skill at level 1. Copying the same skill again levels it, and level 5 upgrades it. This differs from *Devour* (section 3.2), which takes stats.
- *Battle Through the Heavens* ([fandom](https://battle-through-the-heavens.fandom.com/wiki/Yan_Clan), [chapter 1048](https://www.wuxiaworld.com/novel/battle-through-the-heavens/btth-chapter-1048)): 23 ranked Heavenly Flames, and a flame that devours the others becomes rank 1. **Ranked uniques:** a short, numbered list of elemental cores that only rare chase conditions drop; each extra core a monster takes in merges and stacks. It is a collection the player can see (pattern 7, uniqueness).

**Breakthroughs: the milestone becomes an event.**
- Xianxia cultivation in general ([BladeRPG](https://www.bladerpg.com/en/spirit-roots/), [Substack overview](https://jabberwocko.substack.com/p/xianxia-the-path-to-immortality)): realms advance by passing a qi threshold, major steps bring a **tribulation**, and rushing causes **qi deviation**. Spirit-root quality sets how fast someone cultivates and where they hit a bottleneck. **Tribulation fight:** crossing 700 or 900 in a stat (`specs/12` section 4.4) does not draft at once; it starts a special fight against a "heavenly" foe that scales with that stat. A win drafts 4 cards instead of 3, or gives the drafted tree its first node free. A loss drafts as usual and costs energy. The player can hold at 699 until ready, a planning choice the player can see. Spirit roots are today's species growth rates; MonStack already has them.
- *DanMachi* Falna ([fandom](https://danmachi.fandom.com/wiki/Falna), [guide](https://myanimethoughts.com/danmachi-levelling-ranks-detail)): stats grow from normal excelia, but a **level up** needs a basic stat at rank D **and** a "great feat", and every level up offers a development ability. **Upset promotion:** a rank-fight win over a clearly stronger opponent (by a power-rating gap) also offers a 3-card pick of small passives. It rewards fighting above your weight rather than grinding.

**Earned titles: deeds grant skills outside the tree slots.**
- *So I'm a Spider, So What?* ([fandom: Taboo](https://kumodesu.fandom.com/wiki/Taboo)): titles such as *Kin Eater*, *Merciless* or *Assassin* are "enhancement codes obtained by satisfying conditions", each granting up to two skills. *Taboo* is a hidden skill that grows from forbidden acts and shows only at level 10. **Deed titles:** tally conditions (10 crits, 3 wins below 25% HP, 0 Rests in 4 cycles) grant a named title with one small skill, outside the 4 tree slots, kept for the run. **Hidden counter:** one secret tally per species shows only when it pays out. It works like a chase trait without a tree.
- *Overlord* job classes ([Overlord wiki](https://overlordmaruyama.fandom.com/wiki/Skeleton_Mage)): each class caps at 15 levels, so level 100 needs at least 7 classes, and racial classes chain (Skeleton Mage, then Elder Lich, then Overlord). **Racial tax:** this is a balance lever for stronger higher-tier species (section 6.2). Part of a high-tier species' SP budget goes into a fixed racial tree that cannot be overwritten. The species is stronger, but has fewer free points to build with.

**Patrons and outside goals.**
- *Omniscient Reader's Viewpoint* ([epicdope](https://www.epicdope.com/whom-does-dokja-choose-as-his-sponsor-in-orv/), [SV quest summary](https://directforums.sufficientvelocity.com/threads/ways-of-perishing-on-the-apocalypse-orv-quest.95800/all/reader)): constellations sponsor incarnations with coins, and "probability" makes unlikely deeds cost more. Hidden scenarios appear only when conditions are met in sequence. **Patron:** at run start, pick 1 of 3 patrons. Every 3 cycles the patron sets a side goal (win without resting, cross 500 INT by cycle 6) that pays SP or AP. **Probability bonus:** a win the odds said you should lose pays extra SP, so underdog species earn more. Both add the run-to-run variance kj asked for (section 6.3).

**Upkeep: power that has to be fed.**
- *Reverend Insanity* ([Wikipedia](https://en.wikipedia.org/wiki/Reverend_Insanity)): power is a collection of Gu worms. Each Gu must be fed or it dies, and Gu are combined by recipes into stronger ones **(unconfirmed: the feeding and recipe rules come from general knowledge of the novel, not a source found in the spike)**. Aperture grade, the innate talent, caps how fast essence comes back. **Upkeep node:** a very strong node that costs 1 energy (or 1 SP) per cycle and goes dormant for a cycle when unpaid. It turns every Rest into an ongoing decision. There is a risk of tedium in an 8-10 minute run, so use it for 1 or 2 nodes at most.

**Teams: a role that comes from the build, not the species.**
- *Tower of God* positions ([fandom](https://towerofgod.fandom.com/wiki/Position), [animehunch](https://animehunch.com/positions-in-tower-of-god-anime-explained/)): Fisherman (melee offence), Spear Bearer (ranged), Light Bearer (information and command), Scout (forward recon), Wave Controller (shinsu support, rare). People change position: Lero-Ro was a top Fisherman and became a better Light Bearer. **Positions:** a Vault monster's trees give it a position, and a 4v4 team that covers distinct positions gets a small team bonus. The Light Bearer reveals the boss's next phase (`specs/12` section 8.3) to the team's gambits. This belongs to the Endless Tower and PvP rulebooks (T11), not to the run.

**Account knowledge that compounds.**
- *The Rising of the Shield Hero* ([fandom: Legendary Jewel](https://shield-hero.fandom.com/wiki/Legendary_Jewel), [Bubbleblabber](https://www.bubbleblabber.com/english-dub-review-the-rising-of-the-shield-hero-four-heroes-council)): the heroes' power-up methods work for everyone once they are shared, and knowing one makes the others stronger. Naofumi unlocks a new shield just by touching it. **Tree mastery:** maxing a tree in any run marks it "known" on the account. A known tree's first node is free in later runs, for every species. Seeing a tree in a draft adds it to a codex. This is meta-progression (`specs/18`), and it rewards trying new trees.

**Rewinds.**
- *Re:Zero*'s Return by Death and the "regressor" manhwa (ORV's *Regressor* stigma, *Second Life Ranker*): failure resets to a checkpoint with knowledge kept. **Regressor:** a rare capstone that, once per run, replays a cycle that ended in an injury or a lost rank fight. The run's RNG must stay deterministic: the replay draws fresh rolls from the same seeded stream. This needs checking against the run streams (`specs/14`) and the bot goldens.

**Already covered in section 3:** *Solo Leveling*'s Shadow Extraction and *Tensura*'s Predator and skill fusion. *Perfect World*'s supreme bone and *The Beginning After the End*'s core stages and Beast Will ([TBATE wiki](https://tbate.fandom.com/wiki/Magic_and_Objects), [Sportskeeda](https://www.sportskeeda.com/anime/what-beast-will-the-beginning-after-end-everything-know-sylvia-s-gift-arthur)) are innate talent and inheritance again. The one new idea: a bound beast's will works in phases (a passive boost first, a transformation later). A Vault parent could lend its capstone at half strength, unlocking fully on a condition. That waits for inheritance (section 6.1).

### 8.3 New patterns (adding to section 3.3)

8. **Digestion** (LotM acting method, DanMachi great feats): power gated by playing the role, not by points. It is what most separates these sources from Western games and anime.
9. **Loot from the fallen** (*Soul Land*, *Reincarnated as a Sword*, BTTH): each win offers a choice from the opponent. Runs diverge by who you fought and what you took.
10. **Breakthrough events** (xianxia tribulations): a threshold is a moment with stakes, not a number crossing.
11. **Outside goals** (ORV patrons, hidden scenarios): side goals that change from run to run push each run a different way.
12. **Deed titles** (*So I'm a Spider*): small rewards for how you played, outside the tree budget.
13. **Compounding knowledge** (*Shield Hero*): the account learns, so later runs start smarter.

### 8.4 Pitfalls (adding to section 3.4)

- **Digestion against the point dump:** solved only if digestion banks from run start (section 8.1). Otherwise it kills the signature moment.
- **Loot needs content:** rank rings and Absorb need every rank opponent to carry a distinct, readable ring or skill. That is content work per opponent and per rank, and it makes balance depend on the opponent roster.
- **More fights, longer runs:** a tribulation adds fights to a 12-cycle, 8-10 minute run. Cap it at 2 per run (700 and 900 only).
- **Chores:** patron goals and upkeep can feel like homework. Keep goals optional and few, and make the reward something the player can see.
- **Rewinds and determinism:** a replayed cycle must not let a player re-roll until lucky. One per run, and fresh rolls from the same stream, so the bots and goldens stay deterministic.

### 8.5 Recommendation (agent)

1. **Digestion plus overreach for chase and keystone capstones** (8.1). It is the best fit of the batch. It reuses tallies, gives the agreed drawback keystones (section 6.4) a mechanism, and makes runs diverge through skill. Prototype it on the drake book chase tree next to the section 5 item 5 prototype, and measure it with the `balance` capstone report (#111).
2. **Fold the rest into the point-economy package** (`specs/23` section 5), not as new systems:
   - a **tribulation** is a Challenge fight (`specs/23` section 3.6) that the 700 and 900 milestones force;
   - an **upset promotion** is a reward for winning a Challenge fight;
   - **rank rings** are a candidate Challenge reward next to SP and a book roll (`specs/23` question 2), and look the strongest of the three, because each fight's result changes the build;
   - **patrons** are a variant of the cycle-0 boon (`specs/23` section 3.6).
3. **Lateral swap** (8.1) is one answer to `specs/23` question 4 (overwrite softening): cheap, readable, and a partner to the agreed fusion pairs.
4. **Hold:** racial tax (until higher tiers exist and dominate), positions (T11 rulebooks), tree mastery (meta-progression), characteristics, Beast Will and Regressor (inheritance and determinism work).

### 8.6 Questions for kj (answered 2026-10-31)

1. **Digestion:** may a deep node or capstone need a role tally as well as SP, with the tally banked from run start so the point dump still works? **Yes** (kj).
2. **Rank-fight loot:** should rank-fight wins offer something from the opponent (a ring, a copied skill)? **Yes, set it up** (kj). What the loot is for comes later: cosmetics, titles or unlock conditions. **(agent)** So the first version records drops and does not add combat power; a power use (rank rings, Absorb) would need kj again.
3. **Tribulation fights** at 700 and 900: worth the extra run length? **No** (kj). Milestones keep drafting at once.
4. **Lateral swap:** should overwriting a tree with a listed neighbour keep half its SP? **Every overwrite refunds half; no neighbour rule for now** (kj: "half refund is fine... it is punishing enough"). Built in content 3.15 (`TREE_REFUND`, `specs/15` section 4). This amends kj's `specs/23` answer 4.
