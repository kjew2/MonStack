# 15 - Skill Trees

Status: **live in the training run engine (T3 of `specs/13`)**, not yet in the web client (T6). Code: `src/IdleRng.Sim/Training/TrainingRun.cs` (tree commands), `src/IdleRng.Sim/Combat` (rule modifiers). Tests: `src/IdleRng.Sim.Tests/SkillTreeTests.cs`. Golden: `golden/training-run-vectors.txt`.

The design is `specs/12` section 5; the decisions are kj's (2026-10-30) unless marked **(agent)**.

> **Numbers and content are placeholders.** Every tree, node, skill, cost and condition here is placeholder content until balance resumes (`CLAUDE.md`).

## 1. Summary

- A monster has `TREE_SLOTS` (4) slots. A run starts with its species' two **native** trees in slots 0 and 1 and two empty slots.
- **Skill points** (SP): `SP_PER_CYCLE` at the start of every cycle (cycle 1 included) plus `SP_PER_WIN` for each rank fight won.
- SP buy a tree's **nodes in order**. About 75% of nodes are **filler** (a growth bonus or a flat stat boost); the rest grant skills, triggered effects or rule modifiers. The **last node is the tree's chase trait**.
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
- **Chase trees' capstones must be significantly more impactful and unique** than a normal tree's chase trait (kj). In the placeholder content they are coded rule modifiers: *Unbreakable Will* ends in `counter_mitigated`, *Arcane Overflow* in `echo_cast`.
- Placeholder shape **(agent)**: 8 nodes per tree: growth, stat, growth, stat, skill (cost 2), growth, stat, chase trait (cost 3); filler nodes cost 1, so a tree costs 11 SP. Trees can be any length; kj suggested longer ones with about 75% filler.

Content table `monster_skills`: the skills nodes grant, in the battle vocabulary (`specs/16` section 5.1): `id`, `name_key`, `desc_key`, `kind`, `target`, `stat`, `scales`, `power`, `cooldown`, `duration`, `element`.

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
- **Skill book.** `UseBook` offers the book's tree. Books come from the Daily Tower (post-MVP); until then a run is started with a list of books (`TrainingRun.Start(..., books)`), the debug grant `specs/13` T3 planned.
- **Chase tree.** Section 5.
- Offers queue oldest first.
- **Overwriting** (kj): the overwritten tree's points are lost. **(agent)** Its effects go with it: its flat stat boosts are subtracted again (never below 0), its growth bonus stops applying, its skills leave the kit. Gains already made from its growth bonus stay. Milestones already recorded stay recorded.

## 5. Chase trees

Content table `chase_conditions`: `id`, `tree_id` (a chase tree), `type`, `ref`, `amount`. All of a chase tree's conditions must hold (AND); every chase tree has at least one (`E065`).

| Type | `ref` | Holds when |
|---|---|---|
| `tree_maxed` | another tree | that tree is slotted with every node bought |
| `node_owned` | a node of another tree | its tree is slotted with that node bought |
| `stat_at_least` | trained stat | the stat is at least `amount` |
| `tally_at_least` | `damage_dealt`, `evades`, `crits`, `kills`, `wins`, or `damage_<element>` | the run tally (section 6) is at least `amount` |

After every command the run reconciles chase trees, in this order:

1. A slotted chase tree whose conditions no longer hold **locks again** and leaves its slot (kj: an unlocked chase tree does not survive losing its prerequisites). **(agent)** It leaves like an overwritten tree (section 4). Repeat until nothing changes, since one removal can break another chase tree.
2. A pending chase offer whose conditions no longer hold is withdrawn.
3. A chase tree whose conditions hold, that is neither slotted nor already offered since it last unlocked, is offered. A discarded chase offer is not repeated while the tree stays unlocked; once it locks again it can be offered again.

Taking a chase tree into the slot of one of its own prerequisites makes it lock at once. The UI should warn before that.

Book-gated chase trees are chase trees whose conditions include `node_owned` on a book tree's nodes (*Arcane Overflow* needs a Tome of Echoes node, a Spellcraft node and 3 crits).

## 6. Run tally

The run adds up the monster's battle tallies (`specs/16` section 6.3) across its rank fights: damage dealt, evades, crits, kills and damage per element. `wins` is the rank fights won. These feed `tally_at_least` conditions, the drafts' "deal X fire damage" style playstyle conditions (`specs/12` section 5.5).

## 7. Placeholder content (content `2.5`)

| Kind | Trees |
|---|---|
| Native | slime: *Slime Fundamentals*, *Gel Armor*; drake: *Pyromancy*, *Heavy Armor*; sprite: *Aether Striker*, *Mind Warp* |
| Generic | one per stat at 300 (*Vital Core*, *Brute Force*, *Spellcraft*, *Sage Mind*, *Nimble Feet*, *Bulwark*, *Overclock*, *Fortune*), two for any stat at 500 (*Battle Instinct*, *Survivor*) |
| Book | *Tome of Echoes* (book `book_echoes`) |
| Chase | *Unbreakable Will*: Heavy Armor maxed, DEF >= 500, 2 wins; capstone `counter_mitigated`. *Arcane Overflow*: Tome of Echoes node 5, Spellcraft node 4, 3 crits; capstone `echo_cast` |

Generated once by a script; the CSVs in `content_src/` are the source of truth and are edited by hand from now on.

## 8. Bots

Run policies resolve offers (take into an empty slot; a chase tree overwrites the least-invested slot that is not one of its prerequisites; otherwise discard), use their books at once, and buy the first affordable next node in slot order. `hoarder` banks SP until cycle 9, then dumps them (`specs/12` section 5.4). `golden/training-run-vectors.txt` adds a run with every book per species.

## 9. Content rules

New in content `2.5`, checked by `tools/content_build.py` (`validate_trees`) and `ContentDb.BuildTrees`.

| Constant | Value | Range |
|---|---|---|
| `SP_PER_CYCLE` | 2 | `>= 0` |
| `SP_PER_WIN` | 2 | `>= 0` |
| `TREE_SLOTS` | 4 | `>= 2` |
| `DRAFT_SIZE` | 3 | `> 0` |
| `MON_ECHO_POWER` | 0.5 | `[0, 1]` (`specs/16` section 7) |

| Table | Rules |
|---|---|
| `monster_skills` | effect stat for buff/debuff/passive, trained `scales` for damage/heal/shield (`E023`); power, cooldown, duration `>= 0` (`E043`); element id or empty (`E023`) |
| `trees` | generic trees need `offer_threshold > 0` and a trained or empty `offer_stat`; other kinds have neither (`E043`, `E023`) |
| `tree_nodes` | known tree (`E023`); index `0..N-1` (`E060`); at least 2 nodes (`E044`); cost `>= 1`; growth/stat need a trained stat and `amount > 0`; skill/trigger need a monster skill; trigger nodes need a trigger and cannot fire a passive; rule nodes need a known rule id; last node skill/trigger/rule (`E064`) |
| `species` | `native_tree_1`, `native_tree_2`: two different native trees (`E023`, `E043`) |
| `chase_conditions` | a chase tree (`E023`); refs by type (`E023`); `amount >= 0` for stat and tally (`E043`); every chase tree has a condition (`E065`) |
| `skill_books` | `tree_id` is a book tree (`E023`) |

## 10. Open

- Genetic aptitude raising the odds of rare drafts and chase trees (`specs/12` section 5.7) arrives with the gacha (post-MVP).
- Real tree content: names, skills and chase capstones beyond placeholders.
