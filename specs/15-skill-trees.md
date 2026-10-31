# 15 - Skill Trees

Status: **live (T3 of `specs/13`)**, in the training run engine and the web client (T6: the tree screen, drafts, books and node tooltips in `web/run.js`). Code: `src/IdleRng.Sim/Training/TrainingRun.cs` (tree commands), `src/IdleRng.Sim/Combat` (rule modifiers). Tests: `src/IdleRng.Sim.Tests/SkillTreeTests.cs`. Golden: `golden/training-run-vectors.txt`.

The design is `specs/12` section 5; the decisions are kj's (2026-10-30) unless marked **(agent)**.

> **Numbers and content are placeholders.** Every tree, node, skill, cost and condition here is placeholder content until balance resumes (`CLAUDE.md`).

## 1. Summary

- A monster has `TREE_SLOTS` (4) slots. A run starts with its species' two **native** trees in slots 0 and 1 and two empty slots.
- **Skill points** (SP): `SP_PER_CYCLE` at the start of every cycle (cycle 1 included) plus `SP_PER_WIN` for each rank fight won.
- SP buy a tree's **nodes in order**. About 75% of nodes are **filler** (a growth bonus or a flat stat boost); the rest grant skills, triggered effects or rule modifiers. The **last node is the tree's chase trait**.
- Each species owns **two chase trees** of its own: one unlocked in-run, one gated behind a skill book tree (section 5.1).
- New trees arrive as **offers**: a milestone draft, a skill book, or an unlocked chase tree. Any offered tree may go into an empty slot or **overwrite any slot**, native trees included; or the offer is discarded.

## 2. Trees and nodes

Content table `trees`: `id`, `name_key`, `kind` (`native`, `generic`, `chase`, `book`), and for generic trees `offer_stat` (a trained stat, or empty for any stat) and `offer_threshold`.

Content table `tree_nodes`: `id`, `tree_id`, `index` (0..N-1 contiguous per tree), `cost` (SP, `>= 1`), `kind` and its arguments:

| Kind | Arguments | Effect |
|---|---|---|
| `growth` | `stat`, `amount` | Adds `amount` to the drill growth multiplier of `stat` (filler) |
| `stat` | `stat`, `amount` | Adds `amount` flat points to `stat`, capped at `STAT_MAX` (filler) |
| `skill` | `skill_id` | Adds a monster skill to the fight kit (a passive applies at battle start) |
| `trigger` | `skill_id`, `trigger` | Fires the skill at a battle trigger (`specs/16` section 6.1) |
| `rule` | `rule_id` | Adds a coded rule modifier (`specs/16` section 6.2) |

- The last node must be `skill`, `trigger` or `rule` (`E064`): it is the chase trait.
- **Chase trees' capstones must be significantly more impactful and unique** than a normal tree's chase trait (kj). Each chase tree ends in a coded rule modifier of its own, and no two chase trees share one (kj, 2026-10-31, issue #92): *Unbreakable Will* `counter_mitigated`, *Arcane Overflow* `echo_cast`, *Living Ocean* `tidal_overflow`, *Abyssal Bloom* `abyssal_drain`, *Inferno Sovereign* `fire_pierce`, *Starlit Ascension* `double_turn_start` (`specs/16` section 6.2). Their numbers are placeholders and can be fleshed out later.
- Placeholder shape **(agent)**: 8 nodes per tree: growth, stat, growth, stat, skill (cost 2), growth, stat, chase trait (cost 3); filler nodes cost 1, so a tree costs 11 SP. Trees can be any length; kj suggested longer ones with about 75% filler.

Content table `monster_skills`: the skills nodes grant, in the battle vocabulary (`specs/16` section 5.1): `id`, `name_key`, `kind`, `target`, `stat`, `scales`, `power`, `cooldown`, `duration`, `element`. The description is the string `mskill.<id>.desc` by convention (there is no `desc_key` column; dropped in content `3.2`), a template over the row (`{power:pct}`, `{cooldown}`, ...; `specs/16` section 9), so its numbers cannot drift from the data.

The monster's fight kit (`TrainingRun.MonsterFighter`): the bought `skill`, `trigger` and `rule` nodes of its slotted trees, in slot order then node order. That order is the AI's skill priority.

Drill growth (`TrainingRun.Growth`): species growth plus the bought `growth` nodes of the slotted trees.

## 3. Commands

Tree commands work in every phase, including after the run has finished, so the final build can still be shaped. `Drill`, `Rest` and `Fight` wait until no offer is pending.

| Command | Rule |
|---|---|
| `SpendPoint(slot)` | Buys the next node of that slot's tree. Rejects an empty slot, a maxed tree, or too few SP. |
| `TakeTree(choice, slot)` | Puts tree `choice` of the oldest offer into `slot`, overwriting what is there. |
| `DiscardOffer()` | Drops the oldest offer. |
| `UseBook(book)` | Consumes a book brought into the run and offers its book tree. Rejected if that tree is already slotted or offered. |

All are logged and replayed like the training commands (`specs/14` section 7).

## 4. Offers and overwriting

- **Milestone draft.** When a stat crosses a `stat_milestones` threshold (by a drill or a flat stat node), the run offers up to `DRAFT_SIZE` distinct generic trees whose `offer_stat` is that stat or empty and whose `offer_threshold` is at most the threshold, excluding trees already slotted or offered. They are picked by partial shuffle on the run's `draft` stream, one draw per pick. No eligible tree, no offer. Each stat and threshold pair drafts once per run.
- **Skill book.** `UseBook` offers the book's tree. Books come from the Dungeon (post-MVP); until then a run is started with a list of books (`TrainingRun.Start(..., books)`), the debug grant `specs/13` T3 planned.
- **Chase tree.** Section 5.
- Offers queue oldest first.
- **Overwriting** (kj): the overwritten tree's points are lost. **(agent)** Its effects go with it: its flat stat boosts are subtracted again (never below 0), its growth bonus stops applying, its skills leave the kit. Gains already made from its growth bonus stay. Milestones already recorded stay recorded.
- **Lateral swap** (kj, 2026-10-31, `specs/12` section 5.2.1; not built yet): if the new tree is one of the old tree's listed neighbours, half the SP spent in the old tree comes back (**(agent)** rounded down). The rest of the overwrite works as above.

## 5. Chase trees

### 5.1 Two per species (kj, 2026-10-30, issue #78)

Every species owns exactly two chase trees, named in `species`:

- `chase_tree_run`, the **in-run** chase tree: unlocks from the species' own native trees plus stat thresholds and a playstyle tally (the normal chase rules). Its conditions name no book tree.
- `chase_tree_book`, the **book-gated** chase tree, the one that rewards planning a run: its conditions also need nodes in a species-specific skill book tree (`specs/12` section 5.3), plus nodes in other trees and a playstyle tally. Each species has its own placeholder book.

Only the owner's runs unlock a chase tree: another species meeting its conditions is never offered it. Every chase tree belongs to exactly one species. **(agent)** Books are not species-locked: any species may use any book and slot its tree as an ordinary tree; only the owner unlocks the chase tree that needs it.

### 5.2 Conditions

Content table `chase_conditions`: `id`, `tree_id` (a chase tree), `type`, `ref`, `amount`. All of a chase tree's conditions must hold (AND); every chase tree has at least one (`E065`).

| Type | `ref` | Holds when |
|---|---|---|
| `tree_maxed` | another tree | that tree is slotted with every node bought |
| `node_owned` | a node of another tree | its tree is slotted with that node bought |
| `stat_at_least` | trained stat | the stat is at least `amount` |
| `tally_at_least` | `damage_dealt`, `evades`, `crits`, `kills`, `wins`, or `damage_<element>` | the run tally (section 6) is at least `amount` |

**Source hints (kj).** The Trees tab lists the run species' two chase trees, tagged in-run or book-gated, and shows under each where every tree its conditions name comes from (the bridge's `source` text per tree: a species' native tree, the stat threshold that drafts a generic tree, or the book that holds a book tree), so a player can tell how to obtain what a condition asks for.

After every command the run reconciles chase trees, in this order:

1. A slotted chase tree whose conditions no longer hold **locks again** and leaves its slot (kj: an unlocked chase tree does not survive losing its prerequisites). **(agent)** It leaves like an overwritten tree (section 4). Repeat until nothing changes, since one removal can break another chase tree.
2. A pending chase offer whose conditions no longer hold is withdrawn.
3. A chase tree of the run's species whose conditions hold, that is neither slotted nor already offered since it last unlocked, is offered. A discarded chase offer is not repeated while the tree stays unlocked; once it locks again it can be offered again.

Taking a chase tree into the slot of one of its own prerequisites makes it lock at once. The UI should warn before that.

A book-gated chase tree's conditions include `node_owned` (or `tree_maxed`) on a book tree (*Arcane Overflow* needs a Tome of Echoes node, a Spellcraft node and 3 crits).

## 6. Run tally

The run adds up the monster's battle tallies (`specs/16` section 6.3) across its rank fights: damage dealt, evades, crits, kills and damage per element. `wins` is the rank fights won. These feed `tally_at_least` conditions, the drafts' "deal X fire damage" style playstyle conditions (`specs/12` section 5.5).

## 7. Placeholder content (content `2.5`, per-species chase trees in `3.3`)

| Kind | Trees |
|---|---|
| Native | slime: *Slime Fundamentals*, *Gel Armor*; drake: *Pyromancy*, *Heavy Armor*; sprite: *Aether Striker*, *Mind Warp* |
| Generic | one per stat at 300 (*Vital Core*, *Brute Force*, *Spellcraft*, *Sage Mind*, *Nimble Feet*, *Bulwark*, *Overclock*, *Fortune*), two for any stat at 500 (*Battle Instinct*, *Survivor*) |
| Book | *Codex of Tides* (book `book_tides`, slime), *Ember Codex* (`book_embers`, drake), *Tome of Echoes* (`book_echoes`, sprite) |

Chase trees (node numbers are 0-based `index`; the UI says "node index+1"):

| Species | In-run | Book-gated |
|---|---|---|
| slime | *Living Ocean*: Gel Armor maxed, HP >= 500, 2 wins; `tidal_overflow` | *Abyssal Bloom*: Codex of Tides node 5, Slime Fundamentals node 4, 2000 water damage; `abyssal_drain` |
| drake | *Unbreakable Will*: Heavy Armor maxed, DEF >= 500, 2 wins; `counter_mitigated` | *Inferno Sovereign*: Ember Codex node 5, Pyromancy node 4, 2000 fire damage; `fire_pierce` |
| sprite | *Starlit Ascension*: Aether Striker maxed, SPD >= 500, 2 wins; `double_turn_start` | *Arcane Overflow*: Tome of Echoes node 5, Spellcraft node 4, 3 crits; `echo_cast` |

The two original chase trees were shared by every species until issue #78 assigned them: *Unbreakable Will* to drake, *Arcane Overflow* to sprite. Every tree and threshold is a placeholder.

Generated once by a script; the CSVs in `content_src/` are the source of truth and are edited by hand from now on.

## 8. Bots

Run policies resolve offers (take into an empty slot; a chase tree overwrites the least-invested slot that is not one of its prerequisites; otherwise discard), use their books at once, and buy the first affordable next node in slot order. `hoarder` banks SP until cycle 9, then dumps them (`specs/12` section 5.4). `golden/training-run-vectors.txt` adds a run with every book per species.

`chaser` (issue #111) plans toward one of its species' own chase capstones, read from `chase_conditions` (nothing hard-coded). Its **target** is a slotted own chase tree whose capstone is not bought yet; otherwise, among the own chase trees whose prerequisite trees can still be had (slotted, pending, a book tree whose book the run still holds, or a generic tree that a milestone not yet crossed would draft), the one with the fewest stat goals (unmet stat conditions, plus the offer stat of a generic prerequisite still below the milestone that drafts it), then the fewest SP still needed, then content order. It uses only its species' own book (a book whose tree is a prerequisite of one of its chase trees) and leaves other books unused. A draft holding a tree the target needs gives that tree; a needed tree, the own book tree or a chase offer takes an empty slot or overwrites the least-invested slot the target does not need. It buys the target's nodes first (prerequisites in condition order, then the chase tree) and other nodes only while the SP left plus the cycle SP still to come cover what the target needs. It drills the target's unmet stat conditions (and the offer stat of a generic prerequisite up to the milestone that drafts it) before its best-growth pick. The other four policies do not plan for chase trees, so what they measure stays comparable.

## 9. Content rules

New in content `2.5` (species chase trees in `3.3`), checked by `tools/content_build.py` (`validate_trees`) and `ContentDb.BuildTrees`.

| Constant | Value | Range |
|---|---|---|
| `SP_PER_CYCLE` | 2 | `>= 0` |
| `SP_PER_WIN` | 2 | `>= 0` |
| `TREE_SLOTS` | 4 | `>= 2` |
| `DRAFT_SIZE` | 3 | `> 0` |
| `MON_ECHO_POWER` | 0.5 | `[0, 1]` (`specs/16` section 7) |
| `MON_TIDE_REGEN`, `MON_TIDE_SHIELD_CAP`, `MON_DRAIN_FRAC`, `MON_FIRE_PIERCE` | 0.05, 0.3, 0.3, 0.5 | `[0, 1]` (`specs/16` section 7) |
| `MON_DOUBLE_TURNS` | 1 | `>= 1` (`specs/16` section 7) |

| Table | Rules |
|---|---|
| `monster_skills` | effect stat for buff/debuff/passive, trained `scales` for damage/heal/shield (`E023`); power, cooldown, duration `>= 0` (`E043`); element id or empty (`E023`) |
| `trees` | generic trees need `offer_threshold > 0` and a trained or empty `offer_stat`; other kinds have neither (`E043`, `E023`) |
| `tree_nodes` | known tree (`E023`); index `0..N-1` (`E060`); at least 2 nodes (`E044`); cost `>= 1`; growth/stat need a trained stat and `amount > 0`; skill/trigger need a monster skill; trigger nodes need a trigger and cannot fire a passive; rule nodes need a known rule id, and a `fire_pierce` node needs the `fire` element (`E023`); last node skill/trigger/rule (`E064`) |
| `species` | `native_tree_1`, `native_tree_2`: two different native trees (`E023`, `E043`); `chase_tree_run`, `chase_tree_book`: two different chase trees (`E023`, `E043`); the in-run one names no book tree in its conditions, the book-gated one names at least one (`E043`); no chase tree belongs to two species and every chase tree belongs to one (`E043`) |
| `chase_conditions` | a chase tree (`E023`); refs by type (`E023`); `amount >= 0` for stat and tally (`E043`); every chase tree has a condition (`E065`) |
| `skill_books` | `tree_id` is a book tree (`E023`) |

## 10. Open

- Genetic aptitude (`specs/12` section 5.7) was dropped with the species gacha at T9 (kj, 2026-10-30): nothing raises a species' draft or chase-tree odds.
- Real tree content: names, skills and chase capstones beyond placeholders.
- **Capstone reachability targets (kj, 2026-10-31, #110):**
  - the in-run chase capstone is bought in about the top 10% of runs;
  - the book chase capstone in about 50% of runs that bring the species' own book.
  - **(agent)** Measured under the `focus` and `hoarder` policies. Today it is 0-69% for in-run and 0% for book (`specs/22` section 2), so content and the SP economy need retuning. The bots must first play toward their chase conditions (#111).
- **Book chase capstones must be uber strong** (kj, 2026-10-31, #112): books are uber rare (`specs/21` section 5), so a run that gets its book should get a jackpot. The 50% target is for runs that have the book.
- **Drawback capstones** and **fusion of specific tree pairs** are allowed (kj, 2026-10-31; `specs/12` section 5.2.1).
- **Digestion, rank-fight loot and lateral swap** are agreed, tribulation fights are not (kj, 2026-10-31; `specs/12` section 5.2.1, `specs/22` section 8.6). Open **(agent)**: the content shape for neighbours (a list per tree) and for digestion tallies on nodes, and what rank-fight loot drops.
