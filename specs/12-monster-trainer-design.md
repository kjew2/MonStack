# 12 - Monster Trainer: Consolidated Game Design

Status: **target design, partly built** (`specs/13` section 6 has each phase). **T9 change (kj, 2026-10-30, issue #73):** the species gacha is scrapped; species unlock from achievements, Skill Books are the main premium gate, and the talent tree is bought from a random post-run vendor. Sections 3, 5.6, 5.7, 9, 10 and 13 are updated for it. This is the game the project is pivoting to. `specs/13` is the plan for getting there from the current idle game. Until a system here is built, the older specs still describe the code.

Source: five design drafts kj wrote on 2026-10-30 ("Fast-Paced Monster Raising & Tower Battles", "Monster Skill Trees & Skill Book System", "Project Monster Trainer", "Rogue-Lite Systems Trainer", "Systems-First Monster Trainer"). This spec merges them: every idea in them is here once. Where they disagreed, section 13 lists the conflict and a proposed resolution. Anything marked **(proposed)** is an agent's reconciliation and needs kj's confirmation; everything else is stated in the drafts.

> **Numbers are placeholders.** The drafts give a few numbers (12 epochs, milestones at 300/500/700/900, 100 gauntlet floors). Treat all of them as placeholders until the systems are built (`CLAUDE.md`, "Current focus").

## 1. Pitch

A fast, systems-driven monster-raising game in the line of *Monster Rancher* and *Dragon Quest Monsters*, with rogue-lite meta-progression and a roster unlocked through achievements. It is a reaction to story-heavy raising sims (*Uma Musume*, *Star Savior*): no visual novel, no event text, no cutscenes. A training run is an 8-10 minute sprint of pure decisions, and the fun is finding broken builds, in the spirit of *The Exiled Heavy Knight Knows How to Game the System* ("legalized cheating").

## 2. Pillars

1. **Systems over story.** Zero mandatory narrative. Every choice in a run immediately changes stats, fatigue, skills or risk.
2. **Rapid-fire runs.** One run is 8-10 minutes, so a session holds several runs and testing a strange build is cheap.
3. **Legalized exploitation.** Skill trees combine into hidden chase trees and rule-breaking passives. Finding them is the point.
4. **Rogue-lite meta-progression.** Every run, finished or failed, earns account-wide currency for permanent training upgrades. There is **no breeding**.
5. **Fair, low-stress live service.** Asynchronous PvP rewards are cosmetic only. Combat stats cannot be bought.

## 3. Core loop

```
 Achievements ──> species unlocked (base stats, 2 native trees)
      │
      ▼
 Training run (12 epochs, 8-10 min) ──> stats, skill trees, chase traits
      │
      ▼
 End-of-run gauntlet ──> Legacy Title, monster certified into the Vault
      │
      ▼
 Towers and PvP with Vault teams (4v1 Dungeon, 4v4 Endless Tower, 4v4 async PvP)
      │
      ├── Training Shards ──> post-run vendor: talent ranks (cheaper, stronger future runs)
      ├── Achievements ─────> new species
      └── Skill Books ──────> book-gated trees in future runs (the premium gate)
```

1. **Unlock** a species: one to start, the rest from achievements.
2. **Train** it in a run: raise stats, manage energy and fatigue, build skill trees.
3. **Evaluate**: the gauntlet finds how far it gets and mints a Legacy Title. The monster joins the Vault.
4. **Challenge** towers and PvP with Vault teams.
5. **Progress**: after each run a vendor offers talent upgrades for shards; achievements unlock species; books open new trees.

## 4. Training run

### 4.1 Shape

- One monster per run, picked from the species the account owns.
- **12 cycles** (an "epoch" in the drafts; one draft said 12-15). 8-10 minutes in total.
- No events, dialogue or story scenes. Every screen is a mechanical choice.
- A run ends after the last cycle. An injury costs cycles but does not end it (section 4.3).

### 4.2 Cycle flow (kj, 2026-10-30)

Each cycle the monster has a few **action points** (AP). Every tap spends 1 AP on one of two actions, with instant feedback:

| Action | Effect |
|---|---|
| **Drill** (one stat) | Roll fail, success or super, which sets a gain range; a second roll picks the exact gain in that range. Costs energy. |
| **Rest** | Restores training energy. Nothing else. |

When the AP are spent, the run moves to a **rank fight**, in the spirit of *Monster Rancher*'s fight classes: almost always a single 1v1 fight against the opponent of the monster's current rank (E up to S). A win promotes the monster one rank. If a fight every cycle turns out too frequent, it moves to every other cycle (`FIGHT_EVERY`). This keeps the training loop snappy. Rules: `specs/14`.

The rank fight replaces the drafts' *Tactical Challenge*: it is the in-run fight that feeds playstyle conditions (section 5.5).

### 4.3 Stats, energy, fatigue, injury

- **Stats (kj, 2026-10-30):** eight trained stats: HP, STR, INT, WIS, DEX, DEF, SPD, LUK. The drafts named six; kj added HP and LUK. How each one works in battle is `specs/16` section 3.
- **Energy** is the one training meter (no separate fatigue). Drills spend it, rests restore it. Low energy makes a drill more likely to fail and, below a threshold, able to cause an **injury**: the monster loses the rest of the cycle and its rank fight and sits out the next cycle, then comes back rested (kj, 2026-10-31, issue #89: "skip a fight cycle", less punishing but it still bricks the build; rules `specs/14` section 4.1). Pushing on at low energy to cross a milestone before the run ends is the run's main risk-versus-reward decision. The talent tree can reduce drill cost.
- **Skill points** (kj, 2026-10-30): a fixed amount every cycle plus a bonus for each rank fight win, spent in trees. Saving them is a strategy (section 5.4). Rules: `specs/15`.

### 4.4 Attribute milestones and Awakenings

When a stat crosses **300 / 500 / 700 / 900**, the run pauses for an **Awakening** (kj, 2026-10-31: renamed from "tree draft"), a **3-card tree offer** themed on that stat (example at 700 WIS: *Mind Warp*, *Overclock*, *Aether Conduit*). The picked tree goes into a tree slot under the slot rule of section 5.1. Milestone drafts are one way to get a new tree, not the only one. The talent tree can add draft options.

### 4.5 End of run

- After epoch 12 the monster goes to the gauntlet (section 6) and then into the Vault.
- **Injuries (kj, 2026-10-31, issue #89):** an injury no longer ends the run early (kj's first rule, 2026-10-30). It costs the rest of the cycle, its fight and the next cycle; a run that lost many cycles is weak, so the gauntlet scores it low. Every run plays to cycle 12, is vaulted and pays Training Shards for all its cycles.

## 5. Skill trees

Monsters are skill vessels. **Tree shape (kj, 2026-10-30):** a tree is a line of nodes bought in order, long enough that about 75% of nodes are filler (a stat growth increase or a flat stat boost); the rest grant skills and effects, and the last node is the tree's chase trait. Rules: `specs/15`.

### 5.1 Tree slots (kj, 2026-10-30)

A monster has **4 tree slots**. A run starts with the species' 2 native trees in two slots (e.g. *Slime Fundamentals*, *Pyromancy*, *Pyre Core*, *Aether Striker*) and the other 2 slots empty.

Every newly acquired tree, from any source, is offered the same way: put it in an empty slot, **overwrite any occupied slot** (native trees included), or discard it. Sources include a consumed Skill Book (section 5.6), a generic tree from stat growth (section 4.4), random mid-run offers, and chase trees (section 5.3).

Example: the run starts with 2 native trees and 2 empty slots. The player uses a Skill Book and puts its tree in slot 3. Later a stat milestone offers a generic tree; it goes in slot 4. More generic trees arrive and are discarded in favour of the current four. Then a chase tree unlocks and overwrites the generic tree, or the weaker native tree.

**Overwriting (kj, 2026-10-31, replacing "points are lost" of 2026-10-30):** **half** the skill points spent in an overwritten tree come back (`TREE_REFUND`, `specs/15` section 4); kj: "it is punishing enough". An unlocked chase tree does not survive losing its prerequisites: if a tree its conditions used is overwritten, the chase tree locks again.

### 5.2 Chase traits

Every tree has a hidden capstone, its **chase trait**, that breaks a standard rule (kj, 2026-10-30). Maxing the tree reveals and unlocks it. Example: *Omniscience*: ignores resistances and refunds 50% energy. **A chase tree's own capstone must be significantly more impactful and unique** than a normal tree's chase trait: it is the run's jackpot, not a bigger number.

### 5.2.1 Higher-tier species and capstones (kj, 2026-10-31, #110)

Agreed direction from the higher-tier spike (`specs/22` section 6); not built yet:
- **The in-run chase tree is not guaranteed (kj, 2026-10-31):** each run rolls once whether it is attuned to its species' in-run chase tree. A new account is attuned in 20% of runs; the Chase Instinct talent raises it to 50% at max. The book-gated chase tree has no such roll (`specs/15` section 5.4).
- **Higher-tier species are later unlocks** (account achievements) and may be **stronger overall**, as in *Monster Rancher*. Mid-run evolution and breeding are much later on the roadmap.
- **Chase capstones may carry drawbacks** (keystones that give something up, in the *Exiled Heavy Knight* shape).
- **Some specific tree pairs may fuse** into one tree, and the UI must make each fusing pair clear. Fusion is not a general rule.
- **Reachability targets:** the in-run chase capstone in about the top 10% of runs; the book chase capstone in about 50% of runs that bring the species' own book (`specs/15` section 10).
- **Runs must diverge more** through skill expression or RNG. The skill point economy is the first place to look (spike #112, `specs/23`).
- **Skill point economy package, approved (kj, 2026-10-31, #112; not built yet):**
  - **Node hints:** drill results discount tree nodes.
  - **Challenge fights:** an optional harder rank fight that pays skill points.
  - **Capped SP interest.**
  - No respec. Overwriting refunds half the points spent in the tree (section 5.1; kj amended this later the same day, replacing "points stay lost").
  - Odds, discounts and interest are shown on the buttons.
  - At most two new sources of randomness per run (`specs/23` section 6).

Second round (kj, 2026-10-31, `specs/22` section 8.6); not built yet:
- **Digestion:** a deep node or capstone may need a role tally (e.g. damage mitigated for a tank tree) as well as SP. The tally counts from the start of the run, so the point dump (section 5.4) still works.
- **Rank-fight loot:** a rank-fight win may drop something from the opponent. Its use comes later (cosmetics, titles or unlock conditions); it does not add combat power yet.
- **No tribulation fights:** crossing a milestone drafts at once, with no extra fight.
- **Overwrite refund:** every overwrite refunds half the SP spent in the tree (section 5.1). kj dropped the neighbour-tree rule: there are no tree neighbours for now.

Third round (kj, 2026-10-31, creature spike `specs/24` section 6):
- **Creature tone:** a mix of cute, mythic, grimdark and hot; each creature takes the style that suits it.
- **Species follow their concepts:** slime, drake and sprite are named **Coremote**, **Bastion Drake** and **Mirage Moth** (ids unchanged) and drawn from hand-made pixel art.
- **Apex forms:** buying a chase tree's capstone changes the monster's look and name (`specs/15` section 5.3).
- **Two new species:** the **Mandrake** (nature) and the **Shade Hound** (dark), unlocked from achievements (`specs/24` section 5.1). Not built yet.
- **Fusing pairs:** *Pyromancy* + *Codex of Tides*, *Heavy Armor* + *Bulwark*, *Mind Warp* + *Nimble Feet*, *Fortune* + *Spellcraft* (`specs/24` section 5.2). How fusion works is not decided yet.

### 5.3 Chase trees

Rare trees unlocked mid-run by meeting conditions across trees and stats. **Each species has two chase trees of its own** (kj, 2026-10-30): one per unlock route below. Only that species can unlock them. Two unlock routes:

- **In-run:** stat thresholds plus nodes in other trees. Examples: maxing *Pyromancy* + *Geomancy* unlocks *Magma Overlord SP*; maxing *Heavy Armor* and reaching 500 DEF unlocks *Unbreakable Will*.
- **Book-gated:** a Skill Book tree plus nodes in other trees plus a playstyle condition (section 5.6). Each species has its own Skill Book for this tree; this is the chase tree that rewards planning a run around it.

Rules and placeholder content: `specs/15` section 5.

Rule-breaking synergy passive example: maxing *Heavy Armor* and *Unbreakable Will* gives "Counterattacks deal damage equal to 100% of mitigated defense, bypassing range and speed rules."

### 5.4 The stat-wall point dump

The designed signature moment:

1. A high tier tree (say the 700 WIS draft) is out of reach for the first ~8 epochs, so the player banks skill points while drilling stats.
2. Around epoch 9 the monster crosses 700 WIS and drafts the tree.
3. The player dumps the stockpile into it in one burst, maxes it and unlocks its chase trait.

The economy of points, milestones and epochs must make this reachable but not automatic.

### 5.5 Playstyle conditions

Some unlocks need in-run actions, e.g. "deal X fire damage" or "execute Y perfect dodges". These are counted during Tactical Challenges. Combat is automatic, so "perfect dodge" becomes something like "evade N attacks" (proposed, section 13 item 7).

### 5.6 Skill Books

- **Uber rare and uber strong (kj, 2026-10-31, #112):** a book is a rare event, and the book-gated chase tree it opens must be among the strongest things a run can get. Drop rates: `specs/21` section 5.
- Earned in the Dungeon (section 8.2), kept in the account inventory. **The main premium gate (kj, T9):** books are target-farmed as RNG drops from the Dungeon (T10). How books are sold, if at all, is open (#49, #77).
- Consuming a book at the start of (or during) a run opens its **Book Tree** for that run.
- A book-gated chase tree unlocks when all of these hold: points in named Book Tree nodes, points in named nodes of other trees, and a playstyle condition.

### 5.7 Genetic aptitude (dropped)

**Dropped at T9 (kj, 2026-10-30):** with no species gacha there are no duplicate pulls, so there are no Genetic Aptitude ranks. The drafts had duplicates raise the odds of rare drafts and chase trees in that species' runs.

## 6. End-of-run gauntlet and Legacy Titles

- The finished monster is auto-simulated against up to **100 escalating floors** to find where it fails.
- Presentation: a slowing wheel that lands on the floor and enemy that beat it, then a comic defeat animation (turned to ash, launched off screen).
- The monster gets a permanent **Legacy Title** from the result, e.g. "Floor 42: Professional Punching Bag", "Floor 83: Hydra Food", "Floor 95: God-Slayer Slime".
- The certified monster is stored in the Vault for towers and PvP.
- **MVP stand-in (kj, 2026-10-30):** the rank the monster reached in its rank fights (E-S) is the end-of-run grade, on a score screen with a stat breakdown. The simulated boss-tower gauntlet with the wheel spin comes after the MVP.

## 7. Roster

- The **Vault** holds every certified monster. Teams for 4v1 and 4v4 content come from it.
- **Vault cap (kj, 2026-10-30, #82):** a placeholder cap of 50. A finished run always seals, even over the cap; the player dismantles down to the cap (Training Shards by grade) before the next run. A lock protects favourites from dismantling. Capacity expansions wait for the monetization call (#49). Rules: `specs/18` section 7.
- **Onboarding:** a new account has 1 custom-trained slot plus 3 Mercenary starter units so 4-monster content is playable at once. Mercenaries are replaced as raised monsters retire into the Vault.

## 8. Combat

### 8.1 Rules

- Turn-based, speed-initiative auto-battle, kept clean and predictable so offline simulation works.
- 4v4 teams act on an **IF/THEN rulebook** (gambits), e.g. "If enemy HP < 30% -> priority: high-damage burst skill". The defender sets it for PvP defense.

### 8.2 Modes

| Mode | Format | Focus | Rewards |
|---|---|---|---|
| End-of-run Gauntlet | 1 monster vs 100 floors | How far one build goes | Legacy Title, shards |
| Dungeon (drafts: "Daily Challenge Tower", "Daily Tower Dive"; built as the Daily Tower, renamed by kj 2026-10-31) | 4v1 boss floors, a daily lineup; one free ticket a day, more ground from runs (`specs/21` section 7) | Single-target DPS, taunts, survivability, status mitigation, boss phases | Star-Gems on first clears and milestones; Skill Books (RNG per floor/boss, rarer books deeper) |
| Endless Tower | 4v4, escalating, with mutators | Team synergy, turn-order control, AoE burst, rulebooks | Shards, gems |
| Offline Ranked PvP | 4v4 async against stored teams | Rulebook tactics | Cosmetic only: titles, skins, recolors, auras, badges, borders |

### 8.3 Anti-trivialization without immunities

Bosses never get hard immunities ("immune to stun", "immune to magic"). Broken builds are checked by structure instead:

1. **Overload / Entropy gauge.** High-frequency loops, infinite energy and huge multipliers build entropy. At 100 the excess vents: self-recoil, a stun, or an enraged enemy, unless the build manages or converts entropy.
2. **Phase shifts and lateral objectives.** Bosses change the win condition mid-fight: single-target DPS check, then a multi-target or interrupt phase (target split, channel interrupts), then a survival clock. This punishes pure single-target glass cannons and rewards 4-monster synergy.
3. **Escalating environmental clock.** Arena corruption grows over turns, cutting healing and adding damage, which caps stall and immortality builds.

## 9. Meta-progression

The account-wide rogue-lite talents, bought with Training Shards. Named examples: -5% energy cost per action, +10% base stat floors, a better chance that a run is attuned to its in-run chase tree (Chase Instinct, replacing the Wide Scouting "more milestone draft slots" of 2026-10-31), access to more skill trees.

**Post-run vendor (kj, T9):** talents are not an always-on shop. After each run a vendor offers a random assortment of upgrades (5 offers), with rarities tied to the run's score. A rarity is the talent's own tier and each offer is one rank (kj, 2026-10-31, #102); unbought offers vanish when the next run starts. Rules: `specs/18` section 4.

**Species unlocks (kj, T9):** a new account owns one species; the others unlock from account achievements with varied conditions (rank reached, a stat reached, runs finished, gauntlet floors). Rules: `specs/18` section 8.

## 10. Currencies

### 10.1 No species gacha

The drafts had a gacha that unlocked species (with duplicates raising Genetic Aptitude). **kj scrapped it at T9 (2026-10-30):** "scrap the gacha for creatures. make them unlock through other random achievements. the skill book become the main premium gate since they are target farmed as rng drops from daily towers." `specs/01` is retired.

### 10.2 Currencies

| Currency | Earned from | Spent on |
|---|---|---|
| Training Shards (soft) | Completed and failed runs, dismantling, gauntlets, towers | Post-run vendor offers (talent ranks) |
| Star-Gems (premium) | First-time tower clears, daily milestones, achievement ladders | Open: there are no gacha pulls (#49, #77) |
| Skill Books (items, the premium gate) | Dungeon drops, target-farmed | Book Trees in runs |

## 11. Monetization and operations

- **Free to play plus a one-time ad-removal pass** ($4.99-$9.99).
- Free tier: opt-in ads for utilities only (reroll a starting run trait, double earned shards, quick stamina refill). No forced interstitials.
- The pass removes ad prompts and grants the ad utilities automatically.
- Cosmetics as above. No draft says gems are sold for money (section 13 item 10). Skill Books are the premium gate; how they are sold is open (#49, #77).
- **Server cost:** offline 4v4 teams are static JSON (~1 KB). Estimated under $30-$50/month for 10,000-50,000 active players.
- **Platform:** PC (Steam) and web with a clean, spreadsheet-transparent UI (section 13 item 11).

## 12. Scope

### 12.1 MVP: single-player demo

Estimated 150-200 hours (8-12 weeks part time with AI help).

- Training run engine (12 epochs).
- 3 species.
- 6 base trees + 2 chase synergy trees.
- Rank fights during the run; the rank reached is the grade (instead of towers and the gauntlet).
- Score screen with the rank and a stat breakdown.

- Added to the MVP (kj, 2026-10-31): T9 (planned as the species gacha; re-scoped by kj to species unlocks by achievement and the post-run vendor, `specs/13` T9) and the Dungeon (T10). The hour estimate above predates this and has not been revisited.

Not in the MVP: multiplayer and database, Endless Tower scaling, PvP.

### 12.2 1.0 launch

- Async 4v4 PvP sync.
- Endless Tower with mutators.
- Ad-removal integration. (The gacha was scrapped at T9.)
- Steam and web launch.

## 13. Conflicts between the drafts and open questions

Proposed resolutions, for kj to confirm or overrule. `specs/13` section 9 repeats the ones that block work.

1. **Run length.** 12 cycles as a content constant (`RUN_CYCLES`, placeholder).
2. **Tree slots.** Decided (kj, 2026-10-30): 4 slots, 2 native + 2 empty at the start, any new tree may fill an empty slot or overwrite any slot (section 5.1). Still proposed: a chase trait at the top of every tree (section 5.2).
3. **What trees give.** Decided (kj, 2026-10-30): about 75% filler (growth and flat stats), the rest skills and effects, chase traits last (section 5).
4. **Stats.** Decided (kj, 2026-10-30): HP, STR, INT, WIS, DEX, DEF, SPD, LUK. Proposed: milestones on all eight.
5. **Duplicates.** Moot: the species gacha was scrapped at T9 (kj, 2026-10-30), so there are no duplicates or aptitude (section 5.7).
6. **Daily tower naming.** "Daily Challenge Tower" and "Daily Tower Dive" are treated as one mode, built as the Daily Tower and renamed the **Dungeon** (kj, 2026-10-31). Endless Tower is 4v4 in one draft and grouped with 4v1 in others; proposed 4v4.
7. **Dodges in an auto-battle.** "Perfect dodges" implies action input. Proposed: combat stays automatic and the condition counts evasions.
8. **Where in-run fights come from.** Decided (kj, 2026-10-30): the rank fight after each cycle's AP (section 4.2).
9. **Failed runs.** Decided (kj, 2026-10-30): section 4.5.
10. **Gem sales.** The drafts describe a gacha with premium currency but only an ad-removal pass as revenue. Since T9 there is no gacha and Skill Books are the premium gate (kj). Proposed: no gem sales at launch; revenue is the pass and cosmetics. How books (or gems) are sold is kj's call (#49, #77).
11. **Platform.** Decided: web first (kj, 2026-10-30, with the client technology left to the agent). `specs/13` section 1 records the call.
12. **Stamina.** "Quick stamina refills" implies an account stamina gate on runs; no draft describes it. Proposed: no stamina gate in the MVP.
13. **Idle.** No draft has an idle or offline layer. Proposed: drop it (offline catch-up, idle chest, stage push).
14. **Roles and elements.** No draft mentions the current game's 5 roles and 5 elements. Proposed: keep the 5 elements as species/skill types; roles come from trees, not fixed per species.
15. **Overwritten trees.** Decided (kj, 2026-10-30): points lost; a chase tree whose prerequisite tree is overwritten locks again (section 5.1). Amended (kj, 2026-10-31): half the points come back (section 5.1).
16. **One energy meter.** The drafts have energy and fatigue; kj's cycle flow has only training energy. Proposed (agent, T2): one meter, energy; low energy raises the fail chance and allows injury (`specs/14` section 4). Easy to split later.
