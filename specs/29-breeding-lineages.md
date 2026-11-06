# 29 - Breeding and Tree Lineages

> **Historical prototype.** [Spec 30](30-lineages-i-x.md) supersedes the eligibility, progression ownership, parent consumption, I–X milestones, schema and UI rules below. Current synthesis consumes two parents and carries monster-held progress; parents-retained/account-wide statements below are historical. This file preserves the original Fortune prototype and its measurements.

Status: **Fortune I–V prototype**, selected from spike #174 by kj (2026-11-03). The long-term direction is a slow grind through small tree improvements, with transformative ranks at V and X. This slice implements only I–V for the existing generic **Fortune** tree (`fortune`); “Fortuna” in the discussion names the same lineage. Existing ids are preserved. Other tree lineages and VI–X are not built.

Code: `src/IdleRng.Sim/Account/FortunaBreeding.cs`, `Training/LineageRun.cs`, the shared command dispatcher, save/parts code and the web Raise screen. Numbers below are **prototype placeholders**, not agreed balance targets.

## 1. Breeding loop

1. Raise a monster that has bought Fortune's skill node, *Gamble* (index 1).
2. Keep it and another trained monster in the Vault.
3. Choose two distinct parents from **Breed · Fortune** on the Raise screen.
4. Pay `LINEAGE_BREED_COST` Training Shards and immediately begin the offspring's normal training run.
5. Develop its inherited Fortune tree, then seal the run to advance the persistent lineage.

The primary parent supplies the offspring's species and the eligible Fortune tree. It must hold Fortune with at least two bought nodes, including when that tree was retained inside a fusion. Both parents need distinct nonnegative certified run ids. The supporting parent may be any other certified Vault monster. **Both parents stay in the Vault**; their lock state does not block breeding. Supporting-parent quality and compatibility do not add lineage points. The prototype does not consume books or add a timer, breeding currency, daily cap or random offspring genes. The initial 50-shard breeding cost is below a completed run’s minimum 60-shard payout, so completing a descendant can fund the next generation; points remain the main long-term gate.

All normal `StartRun` conditions apply: no active run, owned species, valid talent loadout and Vault at or below its capacity. The offspring uses the primary's species, the account's current talent loadout and the normal next run seed. Fortune starts with zero bought nodes in slot 2 (the third slot), using one of the existing empty slots. Its two native trees remain in slots 0 and 1. A rejected breeding command changes nothing.

Ordinary, non-bred training remains available. It neither inherits account lineage ranks nor earns lineage points.

## 2. Account unlock versus individual investment

`AccountState.Fortuna` holds the account's unlocked rank (initially I), current points toward the next unlock or retry, V failures, lifetime V attempt count and the last award/result for feedback.

An offspring captures the unlocked rank **at birth**, recorded in `RunState.Lineage.Rank` and the inherited `TreeRoll.LineageRank`. A later account upgrade changes only future offspring. A previously sealed Vault monster keeps its captured tree roll, bought nodes and rank; no retroactive stat or combat change.

The rank changes the inherited tree's potential, not its SP investment. Node count, order and SP costs stay the same. To use the V transformation, that monster must buy Fortune's capstone. Native trees, chase trees and Skill Book gates are unaffected.

## 3. Lineage points: node progression only

There is one focus tree per bred run: Fortune. Points come **only** from the deepest Fortune node progression reached in that run. No points come from completion alone, grade, fights, wins, deeds, parent compatibility or species variety.

The actual Fortune layout has *Gamble* at node 2, rather than node 5 in the illustrative discussion. Weights follow the actual node kind, preserving the agreed total and capstone weighting:

| Node (one-based) | Kind | Incremental points | Total through node |
|---|---|---:|---:|
| 1 | Growth | 1 | 1 |
| 2 | Skill (*Gamble*) | 2 | 3 |
| 3 | Growth | 1 | 4 |
| 4 | Flat stat | 1 | 5 |
| 5 | Flat stat | 1 | 6 |
| 6 | Growth | 1 | 7 |
| 7 | Flat stat | 1 | 8 |
| 8 | Capstone (*Jackpot*) | 12 | **20** |

The capstone is 60% of a completed tree's award. Points are awarded once by `FinishRun`, when the run seals. The run keeps `Lineage.Deepest`, a high-water mark: overwriting Fortune, replacing it, or absorbing it into a fusion cannot erase earned progression. Reacquiring and rebuying it never awards a node twice. Abandoning awards nothing. A second seal is rejected because the active run has already been cleared; server commit retries are idempotent under `specs/27`.

Points already earned on the account persist. Excess points carry through upgrades and retries rather than being discarded. Once V is unlocked, no further advancement is available in this slice; the last offspring's node award can still be shown.

## 4. Thresholds and protected breakthrough

II, III and IV unlock automatically and are guaranteed. V attempts automatically when its point threshold is reached. No separate attempt payment, destruction or downgrade occurs.

| Advancement | Main threshold | Fully maxed descendants, ignoring carried overflow |
|---|---:|---:|
| I → II | 40 | 2 |
| II → III | 80 | 4 |
| III → IV | 140 | 7 |
| IV → V | 180 | 9 |

A failure at V spends that attempt's required points but preserves completion of the main threshold. The next attempt needs only `ceil(LINEAGE_TO_5 × LINEAGE_RETRY_FRAC)` = **45** new points, less any carried overflow. Each failure adds 15 **percentage points** to the next success chance, capped at 100%:

| V attempt | Success chance |
|---|---:|
| 1 | 50% |
| 2 | 65% |
| 3 | 80% |
| 4 | 95% |
| 5 | 100% |

The fifth attempt guarantees V. Attempts are drawn from the account seed's named `lineage.fortune` stream using the persisted lifetime attempt count as counter. Reloading or replaying a rejected/duplicate command cannot choose a fresh result. A successful upgrade clears its failure state. Enough carried points may resolve subsequent eligible upgrades or attempts during the same seal.

These thresholds put the first attempt at **22 fully maxed descendants** and the hard guarantee at **31**, given 20 points each. Expected V points are about **472**, or approximately **24 fully maxed descendants** after accounting for whole-run awards. Partial-tree runs are slower: learning only *Gamble* earns 3 points, and stopping immediately before the capstone earns 8. There is no deed trial in addition to the node grind. VI–X thresholds, odds and effects remain future work.

## 5. Rank effects and Fortune V

Each inherited filler amount is its fixed content amount multiplied by:

```
1 + (inherited rank - 1) × LINEAGE_FILLER_PER_RANK
```

With the initial 0.025 per rank, II/III/IV/V improve Fortune's filler amounts by 2.5%/5%/7.5%/10%. This is an improvement to this tree's flat/growth nodes, not the monster's whole stat sheet. Inherited trees do not reroll Awakening amounts; their captured roll stores these amounts, with no random capstone-growth bonus. Existing Awakening Fortune offers still follow `specs/15` section 4.1.

**V — Fortune's Patience (`fortune_bank`).** Buying Fortune V's capstone adds a battle rule:

- Begin each fight with zero bank.
- On an eligible attacking critical roll that ultimately fails (after the target's Fortune denial), add `MON_FORTUNE_BANK_STEP` = 0.1 to the bank, capped at `MON_FORTUNE_BANK_CAP` = 0.4.
- Add the current bank to that unit's Fortune reroll chance for subsequent attacking crit rolls, capped at 1. The critical chance itself does not increase.
- A successful random attacking critical roll resets the bank to zero. This check precedes later forced-crit hooks such as Anvil Rhythm: a forced crit after a failed random roll does not reset the bank.
- Evaded hits and grazes have no attacking crit roll and do not change the bank. The rule does not improve defensive evades or deny enemy crits. It adds no extra roll beyond the normal Fortune reroll sequence.
- Record `rule:fortune_bank` with the bank after the roll; bank state is cloned and hashed only for carriers. The rule does not trigger an extra hit or an extra action.

This works in 1v1, Dungeon 4v1 and team 4v4 without ally selection. Each carrier owns its own bounded bank. An acquired V tree absorbed into Fortune + Spellcraft fusion retains its already-bought capstone contribution under the normal absorbed-tree rules. Losing the tree entirely removes its combat benefits, while node progression already earned for lineage remains.

The long-term X direction is a second transformative shift, possibly sharing Fortune with an ally while retaining a solo use. It is not implemented or balanced here.

## 6. Persistence, authority and presentation

Save schema **v13** adds `Fortuna` on the account and optional lineage birth/progression snapshots on bred runs and their Vault entries. `TreeRoll.LineageRank` tags inherited ranks. Older accounts migrate to rank I with zero points/attempts; old runs and monsters remain unbred. Runs with lineage and their inherited rolls include the new state in conditional canonical hash sections, so unrelated old runs retain their hash encoding.

`breed(primaryVaultPosition, supportingVaultPosition, talentsOff?)` is an account command through the shared dispatcher. It reads `collection`, `vault`, the entries and `progress`, and writes `training` and `wallet`. The ledger belongs to `progress`, written by sealing. Server verification re-applies the same breeding, node purchases and seal command; attempt state is committed atomically with the run result under the normal checkpoint protocol.

The Raise panel shows current rank/progress, next attempt's odds, retry state, remaining attempts until guarantee, parent selectors, cost and the last result. Tree views show the inherited rank and V capstone effect; the run's Trees tab shows earned lineage points. Numeric rules come from content/the bridge. No breeding-specific deployment or external service is added.

## 7. Constants

All are `content_src/constants.csv` rows and placeholders:

| Constant | Initial value |
|---|---:|
| `LINEAGE_BREED_COST` | 50 |
| `LINEAGE_TO_2`, `LINEAGE_TO_3`, `LINEAGE_TO_4`, `LINEAGE_TO_5` | 40, 80, 140, 180 |
| `LINEAGE_RETRY_FRAC` | 0.25 |
| `LINEAGE_V_CHANCE`, `LINEAGE_V_PITY` | 50, 15 |
| `LINEAGE_NODE_POINTS`, `LINEAGE_SKILL_POINTS`, `LINEAGE_CAPSTONE_POINTS` | 1, 2, 12 |
| `LINEAGE_FILLER_PER_RANK` | 0.025 |
| `MON_FORTUNE_BANK_STEP`, `MON_FORTUNE_BANK_CAP` | 0.1, 0.4 |

## 8. Validation scope

Verify node-only awards, partial trees, capstone weighting, overwrite/reacquisition, rank-at-birth isolation, frozen Vault fighters, no reward on abandon, once-only sealing, carry/refill arithmetic, bounded V attempts and save/parts/server round trips. Exercise Fortune V in 1v1, 4v1 and 4v4 and inspect time-to-first-use, actual proc counts and fight lengths. The full repository checks include the phone browser breeding flow. Broader progression pacing and VI–X remain tuning/research work; no new tight balance gate is introduced.

## 9. Prototype measurements

A seeded 10,000-account sweep of the actual lineage award/upgrade rules (each descendant reaches the indicated node depth) gives:

| Deepest node | Points/run | Mean descendants to V | Median | 95th percentile | Maximum |
|---|---:|---:|---:|---:|---:|
| 2 (*Gamble*) | 3 | 157.803 | 162 | 177 | 207 |
| 4 | 5 | 94.482 | 97 | 106 | 124 |
| 7 | 8 | 59.284 | 61 | 67 | 78 |
| 8 (capstone) | 20 | **23.944** | **25** | **27** | **31** |

The minimum for maxed descendants was 22. Mean V attempts were 1.720, maximum five. This demonstrates the intended capstone incentive and bounded unlucky tail; it is a simulation of repeated node-depth outcomes, not a measured human playtime study.

In controlled equal-stat combat samples of 200 seeds per rank/mode, I and IV share the same fight kit; V reduced mean ticks from 31.70 to 30.57 in 1v1, 64.61 to 62.55 in 4v1, and 33.03 to 32.28 in 4v4. All samples had zero timeouts. V's first bank event occurred around tick 4, with mean bank events per fight 8.62/73.25/34.79 in those modes. The controlled gauntlet sample showed **no clear floor gain** (mean 51.87 for I/IV versus 51.79 for V). These samples establish that the bounded mechanic activates during short fights; they do not establish a final power target or account-wide balance.
