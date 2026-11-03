# 15 - Skill Trees

Status: **live (T3 of `specs/13`)**, in the training run engine and the web client (T6: the tree screen, Awakenings, books and node tooltips in `web/run.js`). Code: `src/IdleRng.Sim/Training/TrainingRun.cs` (tree commands), `src/IdleRng.Sim/Combat` (rule modifiers). Tests: `src/IdleRng.Sim.Tests/SkillTreeTests.cs`. Golden: `golden/training-run-vectors.txt`.

The design is `specs/12` section 5; the decisions are kj's (2026-10-30) unless marked **(agent)**.

> **Numbers and content are placeholders.** Every tree, node, skill, cost and condition here is placeholder content until balance resumes (`CLAUDE.md`).

## 1. Summary

- A monster has `TREE_SLOTS` (4) slots, plus the Fifth Slot talent's (`specs/18` section 3). A run starts with its species' two **native** trees in slots 0 and 1 and the other slots empty. The Second Look talent rerolls an Awakening (`specs/18` section 3).
- **Skill points** (SP): `SP_PER_CYCLE` at the start of every cycle (cycle 1 included) plus `SP_PER_WIN` for each rank fight won.
- SP buy a tree's **nodes in order**. About 75% of nodes are **filler** (a growth bonus or a flat stat boost); the rest grant skills, triggered effects or rule modifiers. The **last node is the tree's chase trait**.
- Each species owns **two chase trees** of its own: one unlocked in-run, one gated behind a skill book tree (section 5.1).
- New trees arrive as **offers**: an Awakening (a milestone draft of **rolled** trees for any stat, section 4.1), a skill book, or an unlocked chase tree. Any offered tree may go into an empty slot or **overwrite any slot**, native trees included; or the offer is discarded.

## 2. Trees and nodes

Content table `trees`: `id`, `name_key`, `kind` (`native`, `generic`, `chase`, `book`), for generic trees `offer_stat` (the tree's own stat: most of its rolled nodes and its capstone growth go to it, section 4.1; empty = an any-stat tree, whose own stat is rolled) and `offer_threshold` (the lowest milestone that drafts it), for chase and fusion trees `apex_name_key`, the name of the apex form the tree gives (section 5.3), and for fusion trees `fuse_a` and `fuse_b`, the two trees that fuse into it (section 5.6). `kind` may also be `fusion`.

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
- Placeholder shape **(agent)**: 8 nodes per tree: growth, stat, growth, stat, skill (cost 2), growth, stat, chase trait (cost 3); filler nodes cost 1, so a tree costs 11 SP. Trees can be any length; kj suggested longer ones with about 75% filler. Chase trees are priced for the reachability targets of section 10 (content `4.10`, issue #111): *Arcane Overflow*, *Inferno Sovereign* and *Mandragora Scream* cost 9 (skill 1, capstone 2), *Night Feast* 10 (capstone 2), *Umbral Veil* 13 (node 5 costs 2, capstone 4); the others 11.

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

- **Awakening** (the milestone tree offer; "draft" before kj's rename, 2026-10-31, who found the word bland; the code keeps `Draft` and the `draft` stream). When a stat crosses a `stat_milestones` threshold (by a drill or a flat stat node), the run offers up to `DRAFT_SIZE` distinct generic trees whose `offer_threshold` is at most the threshold, excluding trees already slotted or offered: **first one of the crossing stat's own trees** (a tree whose `offer_stat` is that stat, drawn among them when it has several, as every stat does since content 5.4; kj, 2026-10-31, issue #121, so an all-in build keeps its stat's tree and signature skill), then the other cards **for any stat** (kj, issue #121: a drake that hits 400 STR can be offered a LUK tree that makes its crit build work). When the stat's own tree is slotted or already offered, every card is for any stat. Each part is picked by partial shuffle on the run's `draft` stream, one draw per pick (the own tree first, a shuffle of one), then each tree is **rolled** (section 4.1). No eligible tree, no offer. Each stat and threshold pair drafts once per run. Until issue #121 the offer held only trees whose `offer_stat` was the crossing stat or empty, not rolled; a legacy run still drafts that way (section 4.1).
### 4.1 Rolled Awakening trees (kj, 2026-10-31, issue #121)

Every tree an Awakening offers (and a Second Look reroll redraws) is a **premade** tree whose **numbers are rolled** when it is offered (kj, 2026-10-31: premade trees, with rolls within them; content 5.2 also rolled each node's stat, content 5.3 keeps them as designed), so two *Fortune* trees have the same nodes but different amounts, and a good roll is worth taking. kj's aim: a tree is the way to grow its stat, so a lucky Awakening can open a build the species could not support (drake's LUK is a slog; a good LUK tree lets it train LUK for a crit build).

- **Kept from content:** the tree, its node count, order, costs and node stats, and its skill, trigger and rule nodes (so *Fortune* still has *Gamble* at index 1, fusion pairs still name the tree, and chase conditions still count its nodes).
- **Own stat:** the tree's `offer_stat`; for an any-stat tree, the stat of its first filler node (**(agent)**: *Battle Instinct* STR, *Survivor* HP).
- **Filler nodes** (growth and flat stat) keep their kind and stat and roll their amount, one draw each: uniform in the own range of its kind when the node is on the tree's own stat, else the other range: growth `ROLL_GROWTH_OWN_*` or `ROLL_GROWTH_OFF_*` (rounded to 0.01), flat `ROLL_FLAT_OWN_*` or `ROLL_FLAT_OFF_*` (whole points). The content amount is not used. `ROLL_OWN_CHANCE` is unused since content 5.3 (kept so its id is not removed, `E090`).
- **Capstone growth:** the tree also rolls `ROLL_CAP_GROWTH_*` (0.01), added to its own stat's drill growth while its capstone is bought (on top of the capstone's skill; kj chose "big": own-stat growth nodes give 2 to 3.5 times the 0.1 of the fixed trees, and the capstone more).
- **Order of draws** on the `draft` stream: the picks (section 4: the own tree, then the other cards), then each picked tree in offer order: its filler nodes in index order, then its capstone growth.
- **State:** `TreeOffer.Rolls` (one `TreeRoll` per offered tree), carried into `TreeSlot.Roll` by `TakeTree` (and kept by a fusion's absorbed parent), and into `VaultTree.Roll` by `Vault.Seal` for display. `TrainingRun.Nodes` gives a slotted tree's nodes with its rolls applied; `SpendPoint`, `Growth` and `MonsterFighter` use it. The run hash adds a "rolls" block only when a run has any rolls, so a legacy run keeps its hash.
- **Legacy (agent):** a run saved before rolled trees (the run in progress and every Vault entry; the save migration to schema v8 sets `RunModifiers.LegacyDrafts`, `specs/06` section 5) drafts as it was played: the crossing stat's own trees, not rolled. **(agent)** It drafts only from the ten generic trees content had then (*Vital Core* to *Survivor*; `TrainingRun.LegacyGenericTrees`), so the trees added in content 5.4 never enter its drafts. Its log replays unchanged.
- **UI:** an offer card and a slot show the rolled nodes, a chip for the tree's own stat, and the capstone's growth in its text and the stat chips; a rolled node carries its own tooltip (the bridge's `rolled` object, `web/wasm/BRIDGE.md`).

- **Skill book.** `UseBook` offers the book's tree. Books come from the Dungeon (post-MVP); until then a run is started with a list of books (`TrainingRun.Start(..., books)`), the debug grant `specs/13` T3 planned.
- **Chase tree.** Section 5.
- Offers queue oldest first.
- **Overwriting** (kj, 2026-10-31; it was "points are lost" until then): `TREE_REFUND` (0.5, half) of the SP spent in the overwritten tree comes back, rounded down **(agent)**. **(agent)** Its effects go with it: its flat stat boosts are subtracted again (never below 0), its growth bonus stops applying, its skills leave the kit. Gains already made from its growth bonus stay. Milestones already recorded stay recorded.
- **(agent)** A chase tree that locks again (section 5.2) leaves the same way, so it is refunded too. Each tree is rounded down on its own. `TakeTree` reports the trees removed and the SP refunded.

**Offer screen (web, 2026-10-31, kj: the old full node list per tree was too clunky).** An offer is a card pick (drawn in the live UI skin, `specs/26` section 4.1): a banner (Awakening with the stat and threshold, Skill book, or Chase unlocked) and one compact card per offered tree. A card shows the node path as a strip of tiles in buy order (a stat node as its stat, ▲ for growth and + for flat; a skill, trigger or rule node as its initials; the chase trait as a star; each tile opens its node tooltip), the filler nodes summed per stat as chips (the bridge's `boosts`), and the skill, trigger, rule and chase trait nodes one line each. The first card starts picked; tapping another picks it. The picked card shows a tile per slot (empty slots highlighted, an occupied one its tree and nodes bought) and the full node list behind "All nodes". Tapping a slot takes the tree. A slot whose tree a chase tree needs is flagged (section 5.2). Skip discards the offer. The Trees tab draws each slotted tree with the same node path, its bought tiles lit and the rest dimmed, the next one to buy a plain grey tile with a dashed outline (kj: it must not look bought), so the slot view and the offer read the same (issue #115). No toast covers an offer: toasts raised while one is on screen wait until it closes, and a milestone that opens one is not toasted (its banner names the stat and threshold).

## 5. Chase trees

### 5.1 Two per species (kj, 2026-10-30, issue #78)

Every species owns exactly two chase trees, named in `species`:

- `chase_tree_run`, the **in-run** chase tree: unlocks from the species' own native trees plus stat thresholds and a playstyle tally (the normal chase rules). Its conditions name no book tree.
- `chase_tree_book`, the **book-gated** chase tree, the one that rewards planning a run: its conditions also need nodes in a species-specific skill book tree (`specs/12` section 5.3), plus nodes in other trees and a playstyle tally. Each species has its own placeholder book.

Only the owner's runs unlock a chase tree: another species meeting its conditions is never offered it. Every chase tree belongs to exactly one species. **(agent)** Books are not species-locked: any species may use any book and slot its tree as an ordinary tree; only the owner unlocks the chase tree that needs it.

### 5.4 Attunement (removed, kj, 2026-10-31)

kj: the in-run chase tree must not be something the player "just gets every run". A first limiter, an **attunement roll** at run start (`CHASE_BASE_CHANCE` 0.2 plus the Chase Instinct talent, 0.5 at max), was removed the same day: an unattuned run showed every condition met and still never unlocked the tree, which read as a bug, and kj called it a bad implementation of that limit. A better limiter is open in issue #119.

- Every new run can unlock its in-run chase tree once its conditions hold, like the book-gated one. `RunModifiers.ChaseChance` is 1 for every new run, and no talent or constant changes it.
- **Legacy (agent):** a run saved while attunement existed (an active run, or a Vault entry) carries its `ChaseChance` and replays with it: `RunState.ChaseAttuned = ChaseChance >= 1 or u < ChaseChance`, `u` the first draw of the run's `chase` stream. Otherwise its command log would hit a chase offer it never had and stop replaying. `TrainingRun.ChaseOpen` is false only for such an unattuned legacy run's in-run tree, and the Trees tab says so on that tree's card. Chase trees of other species are never open.

### 5.2 Conditions

Content table `chase_conditions`: `id`, `tree_id` (a chase tree), `type`, `ref`, `amount`. All of a chase tree's conditions must hold (AND); every chase tree has at least one (`E065`).

| Type | `ref` | Holds when |
|---|---|---|
| `tree_maxed` | another tree | that tree is slotted with every node bought |
| `node_owned` | a node of another tree | its tree is slotted with that node bought |
| `stat_at_least` | trained stat | the stat is at least `amount` |
| `tally_at_least` | `damage_dealt`, `evades`, `crits`, `kills`, `wins`, or `damage_<element>` | the run tally (section 6) is at least `amount` |

**Source hints (kj).** The Trees tab lists the run species' two chase trees, tagged in-run or book-gated, and shows under each where every tree its conditions name comes from (the bridge's `source` text per tree: a species' native tree, the milestone threshold from which an Awakening can roll a generic tree, or the book that holds a book tree), so a player can tell how to obtain what a condition asks for.

After every command the run reconciles chase trees, in this order:

1. A slotted chase tree whose conditions no longer hold **locks again** and leaves its slot (kj: an unlocked chase tree does not survive losing its prerequisites). **(agent)** It leaves like an overwritten tree (section 4). Repeat until nothing changes, since one removal can break another chase tree.
2. A pending chase offer whose conditions no longer hold is withdrawn.
3. A chase tree of the run's species whose conditions hold, that is neither slotted nor already offered since it last unlocked, is offered. A discarded chase offer is not repeated while the tree stays unlocked; once it locks again it can be offered again.

Taking a chase tree into the slot of one of its own prerequisites makes it lock at once. The offer screen flags that slot ("Locks it!"), and a slot holding a prerequisite of a slotted chase tree ("Chase needs it"); it still allows the take.

A book-gated chase tree's conditions include `node_owned` (or `tree_maxed`) on a book tree (*Arcane Overflow* needs an Echo Song node, a Mind Warp node and 2 crits). Since content `4.10` (issue #111) every book-gated chase tree needs only the **first node** of its book tree (it was node 5), so bringing the book and starting its tree opens the chase.

### 5.3 Apex forms (kj, 2026-10-31, `specs/24` section 6)

A monster that owns a chase tree's capstone takes that tree's **apex form**: the tree's apex name (`apex_name_key`) replaces the species name wherever the monster is shown (the run, the Vault, gauntlet and tower playback), and the web client draws the apex form's generated art (`web/art/`, `specs/26` section 8), falling back to the species' pixel art with the apex palette and overlay (`web/sprites.js`). It is cosmetic: no fight reads it.

- The apex form is the **first slotted chase tree, in slot order, with every node bought** (`ApexForm.TreeOf`). **(agent)** Slot order, rather than the last capstone bought, because a Vault entry keeps its trees but not the order they were maxed.
- A run loses the form when the chase tree leaves its slot (overwritten, or locked again because a prerequisite was overwritten). A Vault entry keeps the form it was sealed with.
- The web client plays an evolution once when the run's monster takes a new form, apex or fused look (kj, 2026-10-31; `playEvolve` in `web/run.js`, presentation only): the old form glows and shakes, white silhouettes of the old and new forms swap faster and faster, a white flash, then the new form in colour with sparkles and "Bastion Drake became the Aegis Wyrm!". A tap skips to the end, the next closes it; reduced motion shows the end at once. It does not play on a reload of a run already in the form. With the debug controls on, each chase card on the Trees tab has a "Test: preview evolution" button that plays its apex form's evolution and changes nothing.

| Chase tree | Apex form |
|---|---|
| *Living Ocean* | Solaris Tide |
| *Abyssal Bloom* | Abyssal Siphonophore |
| *Unbreakable Will* | Aegis Wyrm |
| *Inferno Sovereign* | Surtr Drake |
| *Starlit Ascension* | Comet Moth |
| *Arcane Overflow* | Gemini Moth |
| *World Root* | Worldroot Colossus |
| *Mandragora Scream* | Mandragora Queen |
| *Umbral Veil* | Grave Shuck |
| *Night Feast* | Eclipse Hound |

### 5.5 Power ladder (kj, 2026-10-31)

Where each kind of capstone lands, weakest and commonest first. **The book-gated chase tree is the rarest and the strongest** (kj): it needs an uber rare Skill Book (`specs/21` section 5).

| Tier | How it is reached | Target: gauntlet floors the whole tree adds |
|---|---|---|
| Ordinary tree capstone | max any native or generic tree | (no target) |
| Fusion capstone | max both trees of a fusing pair (section 5.6) | +3 to +5 |
| In-run chase tree | its conditions (section 5.1) | +5 to +8 |
| Book chase tree | the species' own Skill Book plus its conditions | +10 to +15 |

- **Measured** by the `balance` report's chase power section (`specs/09` section 7): each run's monster with its chase trees taken out, then with one chase tree bought in full on top, up the gauntlet on the same seed. The targets hold for each species averaged over the `focus`, `hoarder`, `balanced` and `chaser` policies; single cells may stray. The `capstone-ladder` sanity check WARNs when a species' book chase tree does not beat its in-run one under `focus`.
- **Retune at content 4.4 (agent, to kj's targets):** in-run trees add 5.6-7.7 floors and book trees 10.8-12.0. Survival dominates the gauntlet (a focus-trained monster dies in one or two hits), so three book capstones gained a second part to reach the band: *Arcane Overflow*'s mirror twin takes the first hits (`MON_ECHO_DECOYS`), *Mandragora Scream* works on every action and withers (`MON_WAIL_WITHER`), *Night Feast* starts fed and feeds damage, not only STR (`MON_FEAST_START`, `MON_FEAST_DMG`); *Abyssal Bloom*'s overheal becomes shield (`MON_DRAIN_SHIELD_CAP`). Rules: `specs/16` section 6.2.
- **Fusion at content 4.5:** a fused tree adds 3.4-5.2 floors on top of both parents maxed, averaged over the pairs each species can hold.

### 5.6 Fusion (kj, 2026-10-31)

Only four pairs fuse (kj chose them from `specs/24` section 5.2), and the UI names each pair: a fusable tree's card says which tree it fuses with and into what.

| Fusion tree (`kind` `fusion`) | Parents (`fuse_a`, `fuse_b`) | Fused look | Duo capstone |
|---|---|---|---|
| *Geyser Heart* | *Pyromancy* + *Riptide* | Geyser Wyrm | *Steam Burst*: when hit, fire damage to all enemies |
| *Living Fortress* | *Heavy Armor* + *Bulwark* | Fortress Drake | *Siege Return*: when hit, strike back off DEF |
| *Hall of Mirrors* | *Mind Warp* + *Nimble Feet* | Mirrorwing | *Shatter*: when hit, the mirror strikes back off INT |
| *Arcana Deck* | *Fortune* + *Spellcraft* | Tarot Mimic | *The Star*: when hit, heal 1% of max HP |

- **Offer.** When both parents sit in slots with every node bought, the run offers the fusion tree (offer source `Fusion`), once; a pending offer is withdrawn if a parent stops being ready, and a discarded one comes back only after that (`RunState.FusionOffered`).
- **Taking it.** `TakeTree` must name one of the two parents' slots. The fusion tree goes there with no node bought and **absorbs** both parents (`TreeSlot.Absorbed`); the other parent's slot comes free. Nothing is refunded or lost: the parents' bought nodes still give their skills to the fighter (absorbed parents first, then the fusion tree's nodes), their flat stats stay, and they still count for chase conditions (`TreeMaxed`, `NodeOwned`). An absorbed tree is never drafted again.
- **Leaving.** Overwriting a fusion tree (or losing it any other way) takes its parents with it: `TREE_REFUND` of the SP spent in each of the three comes back, each rounded down, and all their flat stats come off.
- **Fused look.** A fusion tree's `apex_name_key` names the monster as soon as the trees fuse (section 5.3); a chase tree's apex form, once its capstone is bought, wins over it.
- **Vault.** `Vault.Seal` lists absorbed parents as trees with `FusedInto` set, before the fusion tree.
- **Hash.** A run's fusion state (fusions offered, absorbed parents) is hashed only when it has any, so every other run keeps its hash.
- **Bots** always take a fusion, into the first parent's slot.
- Content rules: a fusion tree names two different parents that exist and are native, generic or book trees, each parent fuses into at most one tree, and only fusion trees name parents (`E023`, `E043`). Chase and fusion trees, and only they, have an `apex_name_key`.

## 6. Run tally

The run adds up the monster's battle tallies (`specs/16` section 6.3) across its rank fights: damage dealt, evades, crits, kills and damage per element. `wins` is the rank fights won. These feed `tally_at_least` conditions, the drafts' "deal X fire damage" style playstyle conditions (`specs/12` section 5.5).

## 7. Placeholder content (content `2.5`, per-species chase trees in `3.3`)

| Kind | Trees |
|---|---|
| Native | slime: *Slime Fundamentals*, *Gel Armor*; drake: *Pyromancy*, *Heavy Armor*; sprite: *Aether Striker*, *Mind Warp*; mandrake: *Rootbound*, *Mandrake Wail*; shade_hound: *Umbral Step*, *Shadow Harvest* (content `4.2`) |
| Generic | all at 400, the single milestone (content 6.0, kj 2026-11-01; until then the stat trees were at 300 and the any-stat trees at 500): two per stat (content 5.4 added the second: HP *Vital Core*, *Lifeblood*; STR *Brute Force*, *Berserker*; INT *Spellcraft*, *Runeweaver*; WIS *Sage Mind*, *Hermit*; DEX *Nimble Feet*, *Duelist*; DEF *Bulwark*, *Stonewall*; SPD *Overclock*, *Quickstep*; LUK *Fortune*, *Lucky Star*), two for any stat (*Battle Instinct*, *Survivor*). Any milestone may offer any of them (its own stat's tree first), with rolled amounts (section 4.1); the content amounts are only a legacy run's. Four stat trees put a damage skill scaled by their own stat at index 1, so an all-in build gets it soon after its Awakening (content 4.7, `specs/25` section 10): *Bulwark* *Iron Press* (DEF), *Sage Mind* *Smite* (WIS, magical), *Fortune* *Gamble* (LUK), *Spellcraft* *Arcane Blast* (INT) | Every content 5.4 tree has the same shape: own-stat growth, a damage skill scaled by its own stat at index 1, a second stat for the other filler nodes, and a new capstone (*Bloodwell*, *Berserk*, *Arcane Spark*, *Inner Peace*, *Precision*, *Granite Skin*, *Momentum*, *Windfall*); the second stat differs from the stat's first tree. Idea-pool trees (epic #132) follow the placeholder shape of section 2 with every filler node on the tree's own stat, a skill at index 4 and a `rule` capstone at index 7 (`specs/16` section 6.2). Lane A (#134, conversion): *Iron Reflexes* (DEX, *Iron Hide*, `iron_reflexes`), *Elemental Avatar* (INT, *Arcane Blast*, `elemental_avatar`), *Fortress Fist* (DEF, *Iron Press*, `fortress_fist`), *Blood Magic* (HP, *Body Slam*, `blood_magic`).
| Book | *Riptide* (book `book_tides`, slime), *Ember Heart* (`book_embers`, drake), *Echo Song* (`book_echoes`, sprite), *Sporebloom* (`book_roots`, mandrake), *Bloodhunt* (`book_shadows`, shade_hound) |

Chase trees (node numbers are 0-based `index`; the UI says "node index+1"):

| Species | In-run | Book-gated |
|---|---|---|
| slime | *Living Ocean*: Gel Armor maxed, HP >= 500, 2 wins; `tidal_overflow` | *Abyssal Bloom*: Riptide node 0, Slime Fundamentals node 4, 2000 water damage; `abyssal_drain` |
| drake | *Unbreakable Will*: Heavy Armor maxed, DEF >= 500, 2 wins; `counter_mitigated` | *Inferno Sovereign*: Ember Heart node 0, Pyromancy node 4, 2000 fire damage; `fire_pierce` |
| sprite | *Starlit Ascension*: Aether Striker maxed, SPD >= 500, 2 wins; `double_turn_start` | *Arcane Overflow*: Echo Song node 0, Mind Warp node 4, 2 crits; `echo_cast` |
| mandrake | *World Root*: Rootbound maxed, HP >= 500, 2 wins; `root_rebirth` | *Mandragora Scream*: Sporebloom node 0, Mandrake Wail node 4, 2000 nature damage; `wail_stagger` |
| shade_hound | *Umbral Veil*: Umbral Step maxed, DEX >= 500, 2 wins; `umbral_veil` | *Night Feast*: Bloodhunt node 0, Shadow Harvest node 4, 7000 dark damage; `night_feast` |

The two original chase trees were shared by every species until issue #78 assigned them: *Unbreakable Will* to drake, *Arcane Overflow* to sprite. Every tree and threshold is a placeholder. Content `4.10` (issue #111) retuned the book-gated conditions and the chase tree costs toward the reachability targets of section 10; *Arcane Overflow* named the generic *Spellcraft* until then, which a sprite run slotted in under 6% of runs.

Generated once by a script; the CSVs in `content_src/` are the source of truth and are edited by hand from now on.

## 8. Bots

Run policies resolve offers (take into an empty slot, from an Awakening of rolled trees the first tree whose own stat is the stat the policy would drill now, else the first (issue #121); a chase tree overwrites the least-invested slot that is neither one of its prerequisites nor a prerequisite of a chase tree already slotted; otherwise discard), use their books at once, and buy the first affordable next node in slot order. **(agent, issue #111)** Once a chase tree is slotted and not maxed, its next node comes first: bought when affordable, else the SP are banked for it until the run ends. Before content `4.10` the four non-planning policies bought it in slot order, so a chase tree (in the last slot) got only what the trees before it left, and a chase offer could overwrite another slotted chase tree's prerequisite and lock it again. `hoarder` banks SP until cycle 9, then dumps them (`specs/12` section 5.4). `golden/training-run-vectors.txt` adds a run with every book per species.

`chaser` (issue #111) plans toward one of its species' own chase capstones, read from `chase_conditions` (nothing hard-coded). Its **target** is a slotted own chase tree whose capstone is not bought yet; otherwise, among the own chase trees whose prerequisite trees can still be had (slotted, pending, a book tree whose book the run still holds, or a generic tree that a milestone not yet crossed would draft), the one with the fewest stat goals (unmet stat conditions, plus the offer stat of a generic prerequisite still below the milestone that drafts it), then the fewest SP still needed, then content order. It uses only its species' own book (a book whose tree is a prerequisite of one of its chase trees) and leaves other books unused. A draft holding a tree the target needs gives that tree; a needed tree, the own book tree or a chase offer takes an empty slot or overwrites the least-invested slot the target does not need. It buys the target's nodes first (prerequisites in condition order, then the chase tree) and other nodes only while the SP left plus the cycle SP still to come cover what the target needs. It drills the target's unmet stat conditions (and the offer stat of a generic prerequisite up to the milestone that drafts it: its own stat's Awakening always offers one of that stat's trees, issue #121; since content 5.4 each stat has two, so a named one comes about half the time, and no chase condition names a generic tree today) before its best-growth pick. The other four policies do not plan for chase trees, so what they measure stays comparable.

## 9. Content rules

New in content `2.5` (species chase trees in `3.3`), checked by `tools/content_build.py` (`validate_trees`) and `ContentDb.BuildTrees`.

| Constant | Value | Range |
|---|---|---|
| `SP_PER_CYCLE` | 2 | `>= 0` |
| `SP_PER_WIN` | 2 | `>= 0` |
| `TREE_SLOTS` | 4 | `>= 2` |
| `DRAFT_SIZE` | 3 | `> 0` |
| `TREE_REFUND` | 0.5 | `[0, 1]`: share of the SP spent in a tree that comes back when it leaves its slot (section 4) |
| `ROLL_OWN_CHANCE` | 0.7 | `[0, 1]`: unused since content 5.3 (it was the share of a rolled tree's filler nodes moved onto its own stat) |
| `ROLL_GROWTH_OWN_MIN`, `_MAX` | 0.2, 0.35 | `0 <= MIN <= MAX`: a rolled growth node on the own stat |
| `ROLL_GROWTH_OFF_MIN`, `_MAX` | 0.05, 0.15 | same: a rolled growth node on another stat |
| `ROLL_FLAT_OWN_MIN`, `_MAX` | 15, 30 | same: a rolled flat node on the own stat (whole points) |
| `ROLL_FLAT_OFF_MIN`, `_MAX` | 10, 20 | same: a rolled flat node on another stat |
| `ROLL_CAP_GROWTH_MIN`, `_MAX` | 0.3, 0.6 | same: the growth a rolled tree's capstone adds to its own stat |
| `MON_ECHO_POWER` | 0.5 | `[0, 1]` (`specs/16` section 7) |
| `MON_TIDE_REGEN`, `MON_TIDE_SHIELD_CAP`, `MON_DRAIN_FRAC`, `MON_FIRE_PIERCE` | 0.05, 0.3, 0.3, 0.5 | `[0, 1]` (`specs/16` section 7) |
| `MON_DOUBLE_TURNS` | 1 | `>= 1` (`specs/16` section 7) |

| Table | Rules |
|---|---|
| `monster_skills` | effect stat for buff/debuff/passive, trained `scales` for damage/heal/shield (`E023`); power, cooldown, duration `>= 0` (`E043`); element id or empty (`E023`) |
| `trees` | generic trees need `offer_threshold > 0` and a trained or empty `offer_stat`; other kinds have neither (`E043`, `E023`). Chase and fusion trees, and only they, have an `apex_name_key`; fusion trees, and only they, name `fuse_a` and `fuse_b` (section 5.6; `E023`, `E043`) |
| `tree_nodes` | known tree (`E023`); index `0..N-1` (`E060`); at least 2 nodes (`E044`); cost `>= 1`; growth/stat need a trained stat and `amount > 0`; skill/trigger need a monster skill; trigger nodes need a trigger and cannot fire a passive; rule nodes need a known rule id, and a `fire_pierce` node needs the `fire` element (`E023`); last node skill/trigger/rule (`E064`) |
| `species` | `native_tree_1`, `native_tree_2`: two different native trees (`E023`, `E043`); `chase_tree_run`, `chase_tree_book`: two different chase trees (`E023`, `E043`); the in-run one names no book tree in its conditions, the book-gated one names at least one (`E043`); no chase tree belongs to two species and every chase tree belongs to one (`E043`) |
| `chase_conditions` | a chase tree (`E023`); refs by type (`E023`); `amount >= 0` for stat and tally (`E043`); every chase tree has a condition (`E065`) |
| `skill_books` | `tree_id` is a book tree (`E023`) |

## 10. Open

- Genetic aptitude (`specs/12` section 5.7) was dropped with the species gacha at T9 (kj, 2026-10-30): nothing raises a species' Awakening or chase-tree odds.
- Real tree content: names, skills and chase capstones beyond placeholders.
- **Capstone reachability targets (kj, 2026-10-31, #110):**
  - the in-run chase capstone is bought in about the top 10% of runs;
  - the book chase capstone in about 50% of runs that bring the species' own book.
  - **(agent)** Measured with `balance` (`specs/09` section 7), 200 seeds, as the mean of the `focus` and `hoarder` cells: the in-run tree in the main pass (no books), the book-gated tree with `--books own`. `chaser` (which plans for it) is reported for reference.
  - **Retune, content `4.10` (#111):** book-gated chase trees need only their book tree's first node, *Arcane Overflow* a Mind Warp node instead of Spellcraft, some chase trees were repriced (section 3), and every policy buys a slotted chase tree first (section 8). Measured (in-run / book, focus+hoarder mean): slime 5% / 55%, drake 0% / 44%, sprite 0% / 44%, mandrake 13.5% / 44%, shade_hound 16% / 54% (before: 0-7% / 0%). `chaser` reaches 74-100% in-run and 87-100% book. Book capstones land in cycle 11-12 under `focus` and `hoarder`, in cycle 7.5-8 under `chaser` for drake, sprite and mandrake.
  - **Off-growth conditions kept (kj, 2026-10-31, #118):** drake's DEF >= 500 and sprite's SPD >= 500 in-run conditions ask for a stat their growth does not favour, so the non-planning bots never meet them (0% at any SP price); only a run that plans for them does. They stay as a reward for planning, and these two species' in-run capstones are judged by the `chaser` cells instead of `focus` and `hoarder`. Prices are unchanged (`chaser`: drake 74%, sprite 100%).
- **Book chase capstones must be uber strong** (kj, 2026-10-31, #112): books are uber rare (`specs/21` section 5), so a run that gets its book should get a jackpot. The 50% target is for runs that have the book.
- **Drawback capstones** and **fusion of specific tree pairs** are allowed (kj, 2026-10-31; `specs/12` section 5.2.1).
- **Digestion and rank-fight loot** are agreed, tribulation fights are not; overwriting refunds half (built, content 3.15) and there are no tree neighbours (kj, 2026-10-31; `specs/12` section 5.2.1, `specs/22` section 8.6). Open **(agent)**: the content shape for digestion tallies on nodes, and what rank-fight loot drops.
