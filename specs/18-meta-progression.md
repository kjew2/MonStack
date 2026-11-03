# 18 - Account and Meta-Progression

Status: **live (T5 of `specs/13`; species unlocks by achievement and the post-run vendor since T9, issue #73)**. Code: `src/IdleRng.Sim/Account` (account rules), `src/IdleRng.Sim/Training/RunState.cs` (`RunModifiers`), `src/IdleRng.Runtime/Save/AccountSave.cs` (save). Tests: `src/IdleRng.Sim.Tests/AccountTests.cs`, `src/IdleRng.Runtime.Tests/AccountSaveTests.cs`. The web client wires it in through its bridge (T6).

The design is `specs/12` sections 7, 9 and 10.

> **Numbers and content are placeholders.** Payouts, talents, ranks, costs, achievement thresholds, vendor rarities, odds and prices are placeholders until balance resumes (`CLAUDE.md`).

**T9 re-scope (kj, 2026-10-30, issue #73):** "scrap the gacha for creatures. make them unlock through other random achievements. the skill book become the main premium gate since they are target farmed as rng drops from daily towers. we also update the meta progression upgrades to be a random vendor assortment of buyable after a given run, say 5 offered, with rarities tied to score of your run." So: no species gacha, no duplicates and no Genetic Aptitude; species unlock from account achievements (section 8); the talent shop is replaced by the post-run vendor (section 4). How skill books are sold stays open (#49, #77); the Dungeon that drops them is T10 (#74).

## 1. Account

`AccountState` holds, between runs:

| Field | Meaning |
|---|---|
| `Seed`, `RunsStarted` | Run seeds derive from these (section 2) |
| `Shards` | Training Shards, the soft currency (`specs/12` section 10.2) |
| `OwnedSpecies` | Species the player can train, in content order: the starter (the first species in `species.csv`, slime) plus every species an achievement unlocked (section 8) |
| `Books` | Skill books by id and count |
| `Talents` | Talent ranks by id (bought from the post-run vendor, section 4) |
| `Achievements`, `Records` | Achievement ids earned, in earn order, and the lifetime records they are checked against (section 8) |
| `Vendor` | The post-run vendor's offers from the last sealed run, or null (section 4) |
| `Vault` | Finished monsters (`VaultEntry`, `specs/13` T4), oldest first; each carries the player's `Locked` flag. At most `VAULT_CAP` before a new run (section 7) |
| `Tower` | Dungeon progress: tickets held, ticket pieces and the last free-ticket day; the day of the last attempt, attempts that day, most floors cleared that day and ever (`specs/21` section 7) |
| `Active` | The run in progress, or null |

There is no premium currency yet. Skill Books are the premium gate (kj, T9); whether and how they, or Star-Gems, are sold is the monetization call (#49, #77).

## 2. Commands (`AccountOps`)

| Command | Rule |
|---|---|
| `New(seed)` | Owns only the starter species (`ContentDb.StarterSpecies`, section 8); no shards, books, talents, achievements or Vault entries |
| `StartRun(species, books, talentsOff)` | No run in progress; the Vault is not over `VAULT_CAP` (section 7); the species is owned; the books are in the inventory and leave it. The run seed is `Derive(StreamSeed(seed, "run"), RunsStarted)`; `RunsStarted` goes up by one. The run starts with the bonuses of the talents its loadout leaves on (sections 3 and 4; `talentsOff` null = the account's last loadout, `DefaultLoadout`), and that loadout is saved on the account. The vendor's unbought offers vanish (section 4) |
| `SetLoadout(talentsOff)` | Saves the loadout the next run starts with (the species pick's toggles; section 4). Free, at any time; a run in progress keeps its bonuses |
| `FinishRun(sealedUtcMs)` | The active run is over (after its last cycle). Pays shards (section 5), returns unused books to the inventory **(agent)**, seals the monster into the Vault (`Vault.Seal`), records it and checks achievements, which may unlock species (section 8), adds the grade's Dungeon ticket pieces (`fight_ranks.ticket_pieces`, 4 make a ticket; `specs/21` section 7), rolls the vendor's offers (section 4) and clears the active run. It seals even at or over `VAULT_CAP`: no monster is lost (section 7). The result lists the achievements earned, the species unlocked and the ticket pieces and tickets earned |
| `CloseVendor()` | The player left the vendor screen: the vendor and its unbought offers are gone (section 4). Nothing to close is fine |
| `AbandonRun()` | Ends the active run in any phase with no payout and no Vault entry; unused books return to the inventory (a misclick costs nothing). `RunsStarted` stays counted, so the next run gets a new seed and abandoning cannot reroll a run. (Agreed between the two building sessions, 2026-10-30; the web client's Abandon button calls it.) |
| `BuyOffer(index)` | A vendor offer not yet bought, whose talent is not maxed; enough shards. Costs the offer's price, adds one rank (section 4). Applies from the next run: a run keeps the bonuses it started with. There is no other way to buy talents |
| `RerollVendor()` | A vendor with an unbought offer, a talent not maxed, and enough shards for `RerollPrice`. Replaces every unbought offer with a fresh roll; bought offers stay (section 4) |
| `Dismantle(index)` | Removes Vault entry `index` (oldest first) for good and pays its grade's refund (section 7). Refused for a locked entry. Allowed during a run |
| `SetLocked(index, locked)` | Locks or unlocks Vault entry `index`. Account state only: not part of the sealed monster or its `RunHash` |
| `GrantBooks(book, count)` | Adds (or removes) books. The Dungeon adds its drops with `RunTower` (`specs/21` section 7); tests and debug use it now |
| `UnlockSpecies(species)` | Adds a species to `OwnedSpecies` in content order. Achievements call it; tests use it |

Rejected commands throw and change nothing. The sim has no clock: the caller passes the seal time.

## 3. Run modifiers

`TrainingRun.Start(content, species, seed, books, mods)` takes a `RunModifiers` (null = none). It is stored on `RunState.Mods`, hashed into `StateHash` (version 5 replaced `DraftSizeBonus` with `ChaseChance`, now legacy only; the talent catalog v2 fields below are hashed only when a run uses one, under run hash version 7, and the legendary fields only when a run uses one, under version 8 (issue #103), so a run without them keeps its version 5, 6 or 7 hash; `specs/14` section 7), and must be passed to `Replay` again; the account save does that (section 6). `Start` refuses values outside the ranges below. Every default changes nothing, so runs and Vault entries from before replay as they were.

| Field | Talent effect | Effect | Default | Range |
|---|---|---|---|---|
| `DrillCostMult` | `drill_cost` | Multiplies `DRILL_ENERGY_COST` (`TrainingRun.DrillCost(c, run)` gives the next drill's cost, for the UI) | 1 | `>= 0` |
| `StatFloorMult` | `stat_floor` | Multiplies the species' base stats at the start (floored, capped at `STAT_MAX`) | 1 | `>= 0` |
| `ChaseChance` | (none since 5.0) | Legacy: the attunement odds a run saved before attunement was removed replays with (`specs/15` section 5.4); every new run has 1 | 1 | `[0, 1]` |
| `StartSp` | `start_sp` | Extra skill points at the start | 0 | `>= 0` |
| `RestEnergyBonus` | `rest_energy` | Added to `REST_ENERGY` (`TrainingRun.RestEnergy`) | 0 | `>= 0` |
| `FailChanceDelta` | `fail_chance` | Added to the drill fail chance, which never drops below 0 (`RunFailChance`) | 0 | `<= 0` |
| `SuperChanceBonus` | `super_chance` | Added to `DRILL_SUPER_P`; the super chance never exceeds `1 - fail` (`RunSuperChance`) | 0 | `>= 0` |
| `ShardMult` | `shard_mult` | Multiplies the run's shard payout, rounded down (section 5) | 1 | `>= 0` |
| `MomentumDrills` | `momentum` | A Super drill makes the next this-many drills cost no energy (`RunState.FreeDrills`; another Super resets it to this, it does not stack). The Super drill itself costs as usual | 0 | `>= 0` |
| `LossSp` | `loss_sp` | Skill points a lost rank fight pays | 0 | `>= 0` |
| `SpecialistBonus` | `specialist` | Drill growth on the highest trained stat (current value, ties all count) `x (1 + b)`, on every other stat `x (1 - b / 2)` (`TrainingRun.Growth`, so the UI's growth shows it) | 0 | `[0, 2]` |
| `TreeSlotBonus` | `tree_slots` | Added to `TREE_SLOTS`; the extra slots start empty | 0 | `>= 0` |
| `InjuryShields` | `injury_shield` | Injuries prevented per run: a drill that rolls an injury does not injure; the monster drops to 0 energy instead (`RunState.ShieldsUsed`, `DrillResult.Shielded`). The gain stays, the AP is spent as usual | 0 | `>= 0` |
| `LastCycleAp` | `last_cycle_ap` | Extra action points in the last cycle (`TrainingRun.CycleAp`) | 0 | `>= 0` |
| `MilestoneMult` | `milestone_mult` | A stat crosses milestone threshold `t` at `round(t x m)` (half away from zero; `TrainingRun.MilestoneAt`). The hit and its Awakening still name `t`, so the tree pool is the same | 1 | `> 0` |
| `DraftRerolls` | `draft_rerolls` | Awakenings the run may reroll (`RerollDraft`, below; `RunState.RerollsUsed`) | 0 | `>= 0` |
| `InjuryImmune`, `RestEnergyMult` | `iron_constitution` | Drills never injure (the injury roll is still drawn, so the stream stays aligned); a rest restores `(REST_ENERGY + RestEnergyBonus) x RestEnergyMult` (`per_rank`) | false, 1 | -, `>= 0` |
| `NeverFail` | `gamblers_blood` | The drill fail chance is 0 (`RunFailChance`); the drawback multiplies `DrillCostMult` by `per_rank` (after Frugal Drills is summed) | false | - |
| `GlassCannonBonus` | `glass_cannon` | Drill growth on every trained stat but HP `x (1 + b)`, on HP 0 (before Specialist; `TrainingRun.Growth`) | 0 | `>= 0` |
| `EchoFrac` | `echo_training` | After each drill, one draw on the training stream picks one of the other trained stats uniformly; it gains `floor(gain x f)` (capped at `STAT_MAX`, may cross a milestone; `DrillResult.EchoStat`, `EchoGain`) | 0 | `[0, 1]` |

**`RerollDraft` (Second Look; a run command, logged like the others):** allowed while rerolls are left and the oldest pending offer is an Awakening (a milestone tree offer). It keeps the crossing stat's own tree (whichever of its trees was dealt) when the offer holds one (its first card; kj, 2026-10-31, issue #121) and redraws the other cards with the Awakening's partial shuffle on the `draft` stream, from the generic trees at or below its threshold **for any stat**, that are not slotted and not in a pending offer, so none of the trees it replaces. Up to `DRAFT_SIZE` trees in all, each rolled afresh, the kept one too (`specs/15` section 4.1). A legacy run's reroll redraws every card from any stat and rolls none. Refused when no new tree can be drawn; `CanRerollDraft` tells the UI whether it would be accepted.

## 4. Talents and the post-run vendor

Content table `talents`: `id`, `name_key`, `desc_key`, `rarity` (a `vendor_rarities` id: the talent's tier), `effect` (one of the talent effects of section 3), `per_rank`, `max_rank`, `cost_base`, `cost_step`. The normal cost of rank `r + 1` is `cost_base + cost_step x r` shards (`TalentCost`). A talent at rank `r` adds `per_rank x r` to its modifier field (`drill_cost`, `fail_chance` and `milestone_mult` are negative). Several talents with one effect add up (`AccountOps.Modifiers`); the sum is clamped to what `Start` accepts: `DrillCostMult >= 0`, `SpecialistBonus <= 2`, `MilestoneMult >= 0.1` (`MinMilestoneMult`, **agent**).

**The post-run vendor** (kj, T9) replaces the always-on talent shop. Code: `AccountOps.RollVendor`, `OfferOpen`, `OfferPrice`, `BuyOffer`. The talents overhaul is tracked in #100.

- When a run is sealed (`FinishRun`), the vendor rolls `VENDOR_OFFERS` offers. **A rarity is the talent's own tier (kj, 2026-10-31, issue #102).** Per offer: draw a tier by weight among the tiers that still have a talent not maxed, then a talent of that tier uniformly among its talents not maxed. The same talent may be offered more than once. No offers once every talent is maxed.
- **Rarity odds follow the run's score**, its grade (the rank reached, E = index 0) **(agent: the grade is the score; the gauntlet floor does not count yet)**. Content table `vendor_rarities`: `id`, `sort_order`, `name_key`, `weight_base`, `weight_per_rank`, `legendary_slot` (bool: the tier's talents take a legendary slot, below). A tier's weight at grade index `g` is `max(0, weight_base + weight_per_rank x g)`.
- **An exhausted tier is skipped (agent):** a tier whose talents are all maxed has no weight, so the other tiers share its odds. **A tier that weighs 0 at the grade is never sold (issue #103: no legendary below A):** when every tier with a talent left weighs 0 there, the vendor has no offers (this replaced "the commonest of them is used", which could have sold a legendary at grade E; `OpenTiers`, **agent**), and a reroll is refused. Two draws per offer, always.
- **One rank per purchase (kj, 2026-10-31, issue #102):** an offer adds exactly one rank of its talent at its normal cost (`TalentCost` at the current rank), whatever its rarity. The price is worked out when bought, so buying one offer reprices another offer of the same talent; an offer whose talent reached max rank cannot be bought.
- Each offer sells once. **The vendor is open only on the post-run screens (kj, 2026-11-01):** from sealing a run until the player leaves the vendor screen (Done, or New run when there is nothing to sell), which closes it (`CloseVendor`); its unbought offers are gone, and there is no vendor between runs. **It is transient (kj, 2026-11-01):** it lives only on the post-run screens of the page that sealed the run; loading the account closes a vendor left open (`web/run-engine.js` calls `closeVendor` after a load), so a reload, a crash or another device never brings it back. Starting a run also clears it, which only matters for a save from before this rule. An abandoned run rolls no vendor. (Before 2026-11-01 the offers stayed on the home screen until the next run started.)
- **Reroll (kj, 2026-10-31, issue #104; the Brotato model of the spike #101):** `RerollVendor` costs `REROLL_BASE + REROLL_STEP x n` shards (`RerollPrice`), `n` = rerolls already made this visit (`Vendor.Rerolls`), and replaces every unbought offer with a fresh roll at the same grade odds, by the same rules as the roll at seal time (skipping maxed talents); bought offers stay where they are. The count starts at 0 with each sealed run's vendor, so the price resets. Refused with no unbought offer or when nothing is left to sell at the grade (every talent maxed, or only tiers that weigh 0 there; nothing would change, `CanRerollVendor`). There is no hold or lock of an offer (it may come later as a talent).
- **Deterministic:** the vendor's draws come from the `vendor` stream, seeded `Derive(StreamSeed(account seed, "vendor"), run index)` where the run index is the sealed run's (`RunsStarted - 1`). Reroll `n >= 1` draws a full `VENDOR_OFFERS` roll seeded `Derive(that seed, n)` (`VendorSeed(seed, run, n)`), and unbought offer `i` takes that roll's offer `i` **(agent)**. The offers are stored on the account (`Vendor`), so the save holds them as rolled. An offer stores the tier it was drawn from; an offer rolled before #102 may hold an older rarity, and the web view shows the talent's own tier.

| Constant | Value | Range |
|---|---|---|
| `VENDOR_OFFERS` | 5 (kj: "say 5") | `> 0` |
| `REROLL_BASE` | 10 (placeholder) | `>= 0` |
| `REROLL_STEP` | 10 (placeholder) | `>= 0` |

Placeholder tiers: `vendor_rarities.csv`. Odds at grade E and S follow from the weights (while every tier has a talent left):

| Rarity | Weight `base + per_rank x g` | E | S |
|---|---|---|---|
| Common | 80 - 10g | 80% | 30% |
| Rare | 18 + 4g | 18% | 38% |
| Epic | 2 + 6g | 2% | 32% |
| Legendary | -14 + 4g (0 at E to B, 2 at A, 6 at S) | 0% | about 6% (2% at A) |

**Legendary tier (kj, 2026-10-31, issue #103):** above Epic, offered only by the vendor after a run graded A or S (weight 0 at E to B); the A and S odds are agent placeholders. Its `legendary_slot` is true, so at most `LEGENDARY_SLOTS` (1) legendary talent is on per run (the loadout, below). With the S odds and five offers, about a quarter of S vendors show at least one legendary.

Rules (`E023`, `E043`, `E044`, `E047`, both `tools/content_build.py` and `ContentDb`): at least one rarity; `sort_order` exactly `0..N-1`; at every grade index the weights do not all come to 0; every talent's `rarity` is a `vendor_rarities` id (`E023`).

Placeholder talents: the drafts' four examples (`specs/12` section 9) and the talent catalog v2 (kj, 2026-10-31: "build them all"; issue #105, from the spike #101). Numbers are placeholders.

| Talent | Rarity | Effect per rank | Max | Normal cost |
|---|---|---|---|---|
| Frugal Drills | Common | drills cost 5% less energy | 5 | 20, 40, 60, ... |
| Head Start | Common | +1 starting skill point | 3 | 25, 50, 75 |
| Deep Rest | Common | rests restore +10 energy | 3 | 20, 40, 60 |
| Steady Hands | Common | drill fail chance -2 points | 3 | 20, 40, 60 |
| Scavenger | Common | +10% shard payout | 5 | 15, 30, 45, ... |
| Strong Stock | Rare | base stats +10% | 3 | 30, 60, 90 |
| Super Set | Rare | drill super chance +3 points | 3 | 30, 60, 90 |
| Momentum | Rare | after a Super drill, the next drill is free | 1 | 60 |
| Grudge Match | Rare | +1 SP per lost rank fight | 2 | 40, 80 |
| Specialist | Rare | highest stat grows +15%, the others -7.5% (a trade-off) | 2 | 40, 80 |
| Fifth Slot | Epic | +1 tree slot | 1 | 120 |
| Second Wind | Epic | the first injury of a run is prevented (energy drops to 0) | 1 | 80 |
| Overtime | Epic | +1 action point in the last cycle | 2 | 60, 120 |
| Early Bloom | Epic | milestone thresholds -10% | 2 | 70, 140 |
| Second Look | Epic | reroll one Awakening per run | 1 | 70 |

**Talent loadout (kj, 2026-10-31, issue #106).** On the species pick the player can switch any owned talent off for the next run (free; on by default). Only the talents left on go into the run's `RunModifiers`, which the run and its Vault entry store, so replays hold. At most `LEGENDARY_SLOTS` talents of a `legendary_slot` tier can be on in one run (`CheckLoadout` refuses more). The loadout is saved on the account (`TalentsOff`, the ids switched off, **agent**) and the next species pick starts from it: a talent bought since is on, and when owned legendary-slot talents are more than the slots, the first in content order keep them (`DefaultLoadout`, **agent**). The web client's toggles call `SetLoadout`; switching a legendary on when the slots are full switches the others off. More slots may come from achievements later (not built).

| Constant | Value | Range |
|---|---|---|
| `LEGENDARY_SLOTS` | 1 (kj) | `>= 0` |

**Legendary talents (kj, 2026-10-31, issue #103; catalogue from the spike #101, agent placeholders).** Each changes a rule rather than a number: one rank, most with a drawback, one number (`per_rank`) each.

| Talent | Rule | Drawback | `per_rank` | Cost |
|---|---|---|---|---|
| Iron Constitution | drills never injure | rests restore half | 0.5 (rest multiplier) | 150 |
| Gambler's Blood | drills never Fail | drills cost twice the energy | 2 (cost multiplier) | 150 |
| Glass Cannon | drill gains +30% on every stat but HP | HP drills gain nothing | 0.3 | 150 |
| Echo Training | each drill adds 25% of its gain to a random other stat | none | 0.25 | 150 |

Not built from the spike list: Thirteenth Cycle (it would make `RUN_CYCLES` per run), Underdog, Chase Instinct (an Epic talent of that name existed until content 5.0), Last Stand (a gauntlet rule). The bot `balance` verb plays runs without talents unless `--talents a,b` gives it some (each at max rank, at most one legendary; `specs/09` section 7). Measured at 120 seeds per cell (content 4.11), each legendary alone keeps every sanity check at PASS, and the trade-offs show: Iron Constitution lifts the reckless policy (no injuries) and lowers the resting ones, Glass Cannon lowers slime (HP-led) and lifts the others, Gambler's Blood is small, Echo Training is a plain gain, and keeps no drawback (kj, 2026-10-31). The full focus Dungeon team with Gambler's Blood or Echo Training on every monster clears the tower on 91% and 93% of days (82% without talents; the loose check WARNs at 100%). The other talents are still untested by the bots, and so are the vendor talents of the spike (Haggler: cheaper rerolls, Extra Shelf: +1 offer), which are not built.

A flat list for now; a branching talent tree is a later content change. Rules (`E043`, both `tools/content_build.py` and `ContentDb`): `max_rank >= 1`, costs `>= 0`; `drill_cost` per rank `<= 0` and the multiplier stays `>= 0` at max rank; `stat_floor`, `rest_energy` and `shard_mult` per rank `>= 0`; `fail_chance` per rank `<= 0`; `super_chance` per rank `>= 0` and at most 1 at max rank; `specialist` per rank `>= 0` and at most 2 at max rank; `milestone_mult` per rank `<= 0` and the multiplier stays `> 0` at max rank; `start_sp`, `momentum`, `loss_sp`, `tree_slots`, `injury_shield`, `last_cycle_ap` and `draft_rerolls` per rank a whole number `>= 0`; the legendary effects `iron_constitution`, `gamblers_blood`, `glass_cannon` and `echo_training` need `max_rank` 1, with `iron_constitution` and `echo_training` per rank in `[0, 1]` and the other two `>= 0`.

**Wide Scouting removed (kj, 2026-10-31).** The old Epic talent (+1 tree per Awakening) was never worth buying while the chase trees were the goal, so it was replaced by Chase Instinct (content 4.0, a MAJOR bump for the removed id: a saved rank in `wide_scouting` is ignored and not refunded). The `DRAFT_SIZE` constant stays at 3 and no talent changes it.

**Chase Instinct removed (kj, 2026-10-31).** Attunement was removed (`specs/15` section 5.4, issue #119), and its talent with it (content 5.0, a MAJOR bump for the removed id): a saved rank in `chase_instinct` is ignored and not refunded, like Wide Scouting.

## 5. Shard payout

`Payout = floor((SHARDS_PER_CYCLE x cycles played + SHARDS_PER_RANK x rank index reached) x ShardMult)` (E = 0; `ShardMult` is the run's Scavenger multiplier, 1 without it, section 3). Cycles sat out after an injury count as played (`specs/14` section 4.1), so every finished run pays for `RUN_CYCLES` cycles; old Vault entries that ended `Injured` were paid for the cycles they reached.

| Constant | Value | Range |
|---|---|---|
| `SHARDS_PER_CYCLE` | 5 | `>= 0` |
| `SHARDS_PER_RANK` | 10 | `>= 0` |

## 6. Save

A new save lineage (kj: clean break, `specs/13` section 1), in its own store slot (`AccountSave.SlotName` = `account`), using the file shape of `specs/06` (header with CRC, JSON body) and its two-slot rotation:

```
{ "Kind": "monster_account", "SchemaVersion": 11,
  "Account": { ...AccountState without Active... },
  "Run": { ...RunState without its Log... } }      (absent when no run is in progress)
```

- The run in progress is stored as its state, not its command log (save v10, kj 2026-11-01, `specs/27` section 3.5): it reloads exactly (a test saves and reloads every few commands through every bot policy and species and gets the same run) and carries on under later rule changes. `RunState.BooksUsed` records the books used, which the sealed entry lists with the unused ones. A state that refers to an id gone from content is dropped, its books return to the inventory and the rest of the account loads with a notice (`specs/06` section 5). Saves before v10 stored a replay recipe (`ActiveRun`), replayed once on load.
- `Load` falls back to the backup slot when the primary is corrupt or invalid and quarantines the bad slot. No slot: a new account is the caller's job.
- Vault entries are stored as their result (schema v9 and v10, kj 2026-11-01, `specs/27` section 3.5): `Id` (the index of the run that made it, `RunsStarted` when it started), species, books brought, grade, rank, end, cycles, fights, final stats, trees (bought nodes, rolls, fusions), the gauntlet result (floor, killer, Legacy Title), seal time and `Locked`. No replay log, `Mods`, `Ladder`, seed, run hash or gauntlet hash: `VaultEntry.Fighter` is built from the stored stats and trees (`TrainingRun.BuildFighter`, the same code the run uses), so a sealed monster keeps the stats it was sealed with under later rule changes.
- `Ladder` (content 4.3, `specs/14` section 5) is the run's rolled rank ladder, in the recipe of the run in progress, so a replay fights the same opponents even if the pools change later. A save from before the pools has none: it loads as an empty ladder, a legacy run that fights the flat `fight_ranks` opponents and keeps its version 5 run hash, so the run in progress goes on. The field is additive with that default, so the schema version does not change **(agent)**.
- Schema versions (`specs/06` section 5): v1 was the first; v2 (issue #82) adds `Locked` to Vault entries; v3 (issue #73) adds `Achievements`, `Records` and `Vendor`; v4 (`specs/21`) adds `Tower`, empty for older saves; v5 (`specs/21` section 7) adds the Dungeon tickets to it, none for older saves; v6 (issue #104) adds `Vendor.Rerolls`, 0 for older saves; v7 (issue #106) adds `TalentsOff`, empty (every talent on) for older saves; v8 (issue #121) marks saved runs as legacy drafts; v9 (`specs/27`) gives Vault entries `Id` and drops their replay recipe; v10 keeps only their result and saves the run in progress as its state; v11 (`specs/28` section 5) adds `Endless` (Endless Tower progress) and `Rulebooks`, empty for older saves. Saves migrate forward on load: v1 entries are unlocked; a v2 account **keeps every species it owned** (accounts from before T9 owned them all) and its talents, starts with no vendor, and gets its records rebuilt from the Vault (each entry one finished run; dismantled monsters are not counted, so the records are a lower bound), after which achievements already met are earned.
- On every load the account's achievements are checked again, so an achievement added to content later is granted when the records already meet it (section 8).

## 7. Vault cap and dismantling

kj's decision (2026-10-30, issue #82): **a cap and dismantling only.** Code: `AccountOps.VaultOver`, `Dismantle`, `DismantleRefund`, `SetLocked`.

- The Vault holds at most `VAULT_CAP` monsters before a new run. A finished run **always seals**, even at or over the cap, so no monster is lost.
- `StartRun` is refused while the Vault holds more than `VAULT_CAP` (`VaultOver > 0`): the player dismantles down to the cap first. Starting at exactly the cap is allowed, so a run can end one over it **(agent: "down to the cap" read as `count <= VAULT_CAP`)**.
- **Dismantling** removes an entry for good and pays Training Shards by its grade: the `dismantle_shards` column of `fight_ranks` (`specs/14` section 8). A grade no longer in content pays 0.
- **Lock**: a locked entry cannot be dismantled. The web client asks for confirmation before dismantling.
- Capacity expansions wait for the monetization call (#49); none are built.

| Constant | Value | Range |
|---|---|---|
| `VAULT_CAP` | 50 | `> 0` |

Placeholder refunds (`fight_ranks.dismantle_shards`, `>= 0`, `E043`): E 5, D 10, C 15, B 25, A 40, S 60.

## 8. Achievements and species unlocks

kj (T9): species unlock "through other random achievements" instead of a gacha. Code: `AccountOps.Record`, `RebuildRecords`, `AchievementProgress`, `AchievementHolds`, `CheckAchievements`, `UnlockSpecies`.

- A new account owns one species, the **starter**: the first species in `species.csv` (slime) **(agent)**. Every other species is unlocked by an achievement.
- Achievements are account-level. They are checked when a run is sealed (after it is recorded) and when an account loads. An earned achievement stays earned; the species it names is added to `OwnedSpecies`.
- `Records` (sealed runs only; abandoned runs do not count): `RunsFinished`, `BestRank` (grade index, -1 before the first), `BestFloorsCleared` (gauntlet floors cleared: `GauntletFloor - 1`), `BestStats` (best final value per stat).
- Content table `achievements`: `id`, `name_key`, `kind`, `ref`, `amount`, `unlocks_species` (nullable). Kinds:

| Kind | `ref` | `amount` | Holds when |
|---|---|---|---|
| `runs_finished` | empty | whole number > 0 | `RunsFinished >= amount` |
| `rank_reached` | a `fight_ranks` id | empty | a sealed run reached that rank (progress counts ranks from 1, E = 1) |
| `stat_reached` | a stat id, or empty for any stat | > 0 | a sealed monster finished with that stat (any stat) `>= amount` |
| `gauntlet_floors` | empty | whole number > 0 | a sealed monster cleared `>= amount` gauntlet floors |
| `endless_floor` | empty | whole number > 0 | one Endless Tower climb cleared `>= amount` floors (`Endless.ClearedEver`, `specs/28` section 6); also checked after each climb (content 6.2) |

Placeholder achievements (thresholds checked against the bots: balanced runs reach B, focused runs pass 500 in one stat):

| Achievement | Condition | Unlocks |
|---|---|---|
| Contender | Reach rank C in a run | drake (Bastion Drake) |
| Prodigy | Finish a run with any stat at 500 or more | sprite (Mirage Moth) |
| Veteran | Finish 5 runs | mandrake (Mandrake; kj, 2026-10-31, `specs/24` section 6) |
| Climber | Clear 10 gauntlet floors | shade_hound (Shade Hound; kj, 2026-10-31) |
| Deep Spring | Finish a run with INT at 650 or more | fountain_axolotl (Fountain Axolotl; #150, placeholder) |
| Living Candle | Finish a run with HP at 700 or more (`stat_reached`, ref `hp`) | candlemane (Candlemane, #141) |
| Kindled | Reach rank B in a run | pyre_stag (Pyre Stag, #142) |
| Castle Walls | Finish a run with DEF at 650 or more (`stat_reached`, ref `def`) | keepshell_crab (Keepshell Crab, #152, placeholder) |
| Quickdraw | Finish a run with SPD at 650 or more (`stat_reached`, ref `spd`) | iaido_mantis (Iaido Mantis, #159, placeholder) |
| Stolen Fortune | Finish a run with LUK at 650 or more (`stat_reached`, ref `luk`) | cuckling (Cuckling, #143) |
| Still Waters | Finish a run with WIS at 600 or more (`stat_reached`, ref `wis`) | gearbell_jelly (Gearbell Jelly, #158, placeholder) |
| Steadfast | Clear 30 Endless Tower floors in one climb (`endless_floor`) | aegis_ram (Aegis Ram, #147, placeholder; the issue's `runs_finished` 10 fell back to its named alternative because Lodge Beaver's lane took `runs_finished`) |
| Spire Breaker | Clear 20 Endless Tower floors in one climb | none **(agent: a goal, not an unlock, until a species is meant for it)** |

Rules (both `tools/content_build.py` and `ContentDb`): `ref` and `amount` as in the table (`E023` for a bad reference, `E043` otherwise); `unlocks_species` is a species id (`E023`) and not the starter (`E043`); every species but the starter is unlocked by at least one achievement (`E044`), so none is unobtainable.

The web client shows locked species on the species pick with their unlock condition and progress (folded under one "N locked species" line on the Raise tab, kj 2026-10-31: less clutter), an achievement list on the home screen (its Goals tab), and a notice on the grade screen (and a toast) for each achievement a run earned.

## 9. Vault browsing (web client)

kj's request (issue #81): large Vaults stay navigable. UI only (`web/vault.js`); every value comes from the account view (`web/wasm/BRIDGE.md`), and nothing here is a game rule.

- **Sort**: newest first (the default), oldest first, grade (best first), gauntlet floor (highest first; a cleared tower on top, an entry with no gauntlet last), species (A to Z, best grade first within one), stat total, or one chosen stat (highest first). Ties fall back to newest first. "Newest" is the Vault's seal order (its index), so an entry with no recorded seal time still sorts.
- **Filter**: species, element (the species' element), Legacy Title (any, has one, has none, or one specific title text among those in the Vault), lock (locked or unlocked only). **Search** keeps the entries whose text holds every typed word: species, element, grade and rank name, Legacy Title and its line, the enemy that beat it, end reason and tree names. Vault entries have no names of their own, so there is no name to search **(agent)**.
- The list says how many entries it shows when a filter hides some and offers Clear filters.
- Each entry shows the monster's **sprite** (issue #125): a small one on its list row (and on the Dungeon team picker's rows), a larger one in the detail and compare headers. It is the species' sprite in its apex form when the entry owns one (`apex` in the account view, `specs/15` section 5.3; the generated art, else the pixel art, `specs/26` section 8).
- **Detail** sheet for one entry: sprite, grade, rank, species and element, end and cycles, floor and Legacy Title, fights won, refund, seal time, all stats and each slotted tree with its bought nodes checked (nodes are bought in order, `specs/15`, so the first `bought` nodes are the bought ones), plus a lock toggle and a shortcut to compare it.
- **Compare** two entries side by side (tap Compare on one, then on another): rank, floor, every stat and the stat total for A and B with B minus A, and each one's trees. Swap flips A and B.
- **Lock and dismantle address the real Vault index** (`vaultIndex`, oldest first), never the position in the sorted or filtered list. An open confirm, detail or compare pick is dropped when its entry is no longer at that index (a dismantle shifts later entries).
- The sort and filters are remembered per viewer in `localStorage['idle-rng-vault-view']` (a convenience; a blocked store falls back to the defaults). The search text is not remembered, so a reload never hides monsters behind a forgotten search **(agent)**.

## 10. Open

- Whether shards buy anything besides vendor offers; what Star-Gems are for now that there is no species gacha, and how skill books are sold (#49, #77).
- Rewards for achievements that unlock no species (Veteran, Climber), and more achievements as species are added.
- Whether the run's score for the vendor should include the gauntlet floor, not only the grade.
- A branching talent tree instead of a list.
- Vault capacity expansions (after the monetization call, #49).
