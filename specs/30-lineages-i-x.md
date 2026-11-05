# 30 - Generic Tree Lineages I–X

Status: **implemented I–X extension for all 40 currently generic trees** (#174, 2026-11-04). This is the canonical current lineage contract and supersedes the Fortune-only rules and UI in [29](29-breeding-lineages.md). Spec 29 remains the prototype history and its measurements remain prototype measurements. Other tree families are not generally breedable. Values are tuning placeholders, not final balance targets.

Code: `Account/TreeLineages.cs`, `Training/LineageRun.cs`, `Training/Rules/LineageTraining.cs`, combat lineage rules, `AccountCommands`, save/parts, `IdleRng.Web/RunHost.cs`, and `web/run.js`. The catalogue below records the concrete implementation, including adaptations of the planning concepts.

## 1. One focus, a long horizon

Choose one generic focus tree and two distinct certified Vault parents. The primary must have bought that tree through its first skill node (the actual content index; not universally node 2), and supplies the offspring species. Support supplies a different certified run id. Parents remain, including locked parents. Spend 50 Training Shards and start an ordinary-length training run immediately: no timer, daily breeding cap, new currency, compatibility points, or destructive failure.

Normal StartRun checks apply, including species ownership, loadout, active-run exclusion, and Vault capacity. The focus starts at zero purchased nodes in existing slot 2 (the third slot), beside the species' two native trees. Its rank is captured from the account at birth, not copied from the parent's birth rank. Existing Vault monsters and older offspring keep their captured ranks. Ordinary non-bred runs neither inherit lineage ranks nor award lineage points. One run has only one lineage focus.

Only `TreeKind.Generic` is eligible: all **40** current generic trees, not all content trees. Native lineages, book lineages, chase lineages and fusion lineages remain future scope. Species-native identity stays species-bound; books still need their book gate; chase offers still need their species, prerequisite trees and unlock/re-lock conditions; fusion still needs its normal pair. A parent may retain an eligible generic contribution inside a fusion, and bought generic contributions survive absorption under the existing fusion rules. Breeding never directly grants a native, book, chase or fusion tree, bypasses its gates, or adds slots.

## 2. Node-only points

Sealing awards the focus tree's deepest purchased progression once. Wins, grades, deeds, compatibility, species variety and completion by themselves award no lineage points. Abandon awards none. Run-local `Lineage.Deepest` preserves a high-water mark through overwrite, fusion and reacquisition; rebuying nodes cannot count twice. Server retry/idempotency rules prevent duplicate sealing.

Every complete eligible tree awards **20 points**, with **12 at the capstone** and **8 before it**. Pre-capstone nodes use skill weight 2 and other-node weight 1, normalized to the eight-point budget using integer largest remainders, with earlier nodes breaking ties. All current generic nodes have positive awards. This adapts the Fortune-only weight schedule to actual tree layouts without making a longer or skill-heavy tree intrinsically faster. The first Fortune skill remains node 2; several advanced trees put the skill at node 5.

Points and overflow persist. At rank X the ledger is complete; no further upgrade or point award is needed. Account ledgers are independent per tree, with no cross-tree point spending.

## 3. Rank costs and protected milestones

| Unlock | Points since preceding unlock | Resolution |
|---|---:|---|
| II | 40 | Guaranteed |
| III | 80 | Guaranteed |
| IV | 140 | Guaranteed |
| V | 180 | Protected breakthrough |
| VI | 320 | Guaranteed |
| VII | 480 | Guaranteed |
| VIII | 700 | Guaranteed |
| IX | 1,000 | Guaranteed |
| X | 1,400 | Protected breakthrough |

Thresholds are incremental, not lifetime totals. Eligible unlocks and attempts resolve automatically when the run seals; there is no separate attempt currency or player-controlled roll. V odds are 50%, 65%, 80%, 95%, then guaranteed on attempt 5. X odds are 35%, 50%, 65%, 80%, 95%, then guaranteed on attempt 6. Misses do not destroy parents, offspring or ranks. After the first milestone payment a retry costs 25% of that milestone's threshold, rounded up: V 45, X 350. Overflow can fund subsequent eligible resolutions in the same seal. Success resets milestone failures.

Each tree uses `lineage.<treeId>` derived from the account seed and its persisted lifetime attempt counter. No save reload or rejected command grants a new roll. With 20-point full trees, the first X attempt costs 4,340 total points (217 full descendants); the hard V-plus-X guarantee costs 6,270 (314 full descendants after rounding), before partial-run variation. These are arithmetic pacing bounds, not a human playtime claim or new balance measurement.

## 4. Rank effects

Inherited filler uses fixed content amounts, not Awakening rerolls. II–V adds 2.5 percentage points per rank above I; VI–X adds 1 point each. The bonuses are I 0%, II 2.5%, III 5%, IV 7.5%, V 10%, VI 11%, VII 12%, VIII 13%, IX 14%, X 15%. This scales that tree's filler, not the whole stat sheet. SP prices, node order and purchased-node requirements stay unchanged.

V adds a first transformation and X adds a second. Both require the inherited focus capstone to be bought and active; reaching an account rank alone does not activate them. Bounded stores, reset windows, opportunity costs and ordinary combat/action caps remain part of each mechanic. Effects do not create unbounded free actions, permanent inherited random skills, automatic chase mastery or repeat revives. Team effects identify their recipient explicitly; solo fallbacks are listed where implemented. Some team-specific effects intentionally have no extra solo benefit.

## 5. Implemented milestone catalogue

Names and descriptions below match the implementation metadata. Common parameters resolve to gain **10%**, cap **30%**, window **2 own actions/ticks as described**, training gain **10%**, training cap **30%**, and reserve charge requirement **3**. Tree-specific bounds such as Fortune's 40% bank remain distinct. This is the concrete adaptation of the approved direction, rather than a promise of unimplemented mechanics.

### Combat foundations (18)

| Tree | V | X |
|---|---|---|
| **Vital Core** (`vital_core`) | **Reserve Heart** — Overhealing stores up to 30% max HP. After each action, restore up to 10% max HP from the reserve. | **Heartlink** — Reserve recovery instead reaches the most injured living ally when they need it more; works on yourself when alone. |
| **Brute Force** (`brute_force`) | **Heavy Rhythm** — Landed basic attacks build 10% Heavy Rhythm, up to 30%. At full rhythm the next basic gains that damage bonus and resets the cycle. | **Demolition** — The empowered basic also removes shielding up to 10% of its empowered raw damage and adds the removed amount as damage. |
| **Spellcraft** (`spellcraft`) | **Arcane Residue** — A damaging skill stores 10% charge for the next magical damaging skill, up to 30%. | **Spell Weaving** — A magical skill different from your previous damaging skill adds another 10% charge, up to 30%. |
| **Sage Mind** (`sage_mind`) | **Measured Ward** — When your shields or wards absorb a hit, store 10% charge, up to 30%, for your next WIS-scaled hit. | **Sanctuary** — Allied shield or ward absorption also charges your next WIS-scaled hit. |
| **Nimble Feet** (`nimble_feet`) | **Read the Strike** — Landed enemy hits add 10 percentage points to your evade chance, up to 30%, respecting the evade cap. An evasion clears the bonus after your next action. | **Borrowed Opening** — An evasion grants the most injured ally 10% damage reduction for two of your actions. You receive the opening when alone. |
| **Bulwark** (`bulwark`) | **Pressure Plate** — When your shields or wards absorb a hit, store 10% charge, up to 30%, for your next DEF-scaled hit. | **Bastion** — Allied shield or ward absorption also charges your next DEF-scaled hit. |
| **Overclock** (`overclock`) | **Controlled Overdrive** — Each action builds 10% gauge-fill speed, up to 30%. After acting at full speed, reset and recover for two actions. | **Gear Shift** — Reaching recovery transfers your stored momentum into up to 30% action gauge for the most injured ally, or yourself when alone. Gauge cannot exceed one ready turn. |
| **Fortune** (`fortune`) | **Fortune Bank** — A failed critical roll stores 10% fortune reroll chance, up to 40%. A successful critical clears the bank. | **Shared Fortune** — After an action with a full bank, spend it to grant the most injured eligible ally 10 percentage points of crit chance on their next attack. Keep the personal bank when alone, when allies are crit-capped, or while a gift is pending. |
| **Battle Instinct** (`battle_instinct`) | **Read the Opponent** — A landed noncritical hit marks its target. Your next attack against that target gains 10% damage; critical hits clear the read. | **Adaptive Read** — Switching targets retains 10% of your stored read for the new attack. |
| **Survivor** (`survivor`) | **Recovery Reserve** — Store 10% of HP damage taken as recovery, capped at 30% max HP. After each action, recover up to 10% max HP from the reserve. | **Hardened Recovery** — Repeated hits from the same source deal 10% less damage for two of your actions after taking HP damage. |
| **Lifeblood** (`lifeblood`) | **Living Power** — Healing received stores damage charge equal to the fraction of max HP restored, capped at 30%. Your next HP-scaled hit consumes it. | **Vital Return** — Your empowered HP-scaled hit heals you for damage dealt times its charge, capped at 10% max HP. |
| **Berserker** (`berserker`) | **Controlled Fury** — HP damage taken builds an equal fraction of bonus damage, capped at 30%. Healing reduces Fury by the fraction of max HP restored. | **Bloodburst** — Attacking at full Fury gains another 10% damage and spends the Fury. You take 10% more damage during a two-action recovery window. |
| **Runeweaver** (`runeweaver`) | **Inscribed Rune** — Landed basics build 10% rune charge, up to 30%. Your next magical skill consumes the charge as bonus damage. | **Piercing Rune** — That magical skill also reduces its target mitigation by the stored rune percentage. |
| **Hermit** (`hermit`) | **Quiet Recovery** — Actions build 10% recovery strength, up to 30%; each action restores max HP times this strength times 10%. Normal healing resets the accumulated strength. | **Solitary Insight** — Your next WIS-scaled hit can consume recovery strength as bonus damage. |
| **Duelist** (`duelist`) | **Patient Focus** — Landed hits build 10% damage focus, up to 30%. Switching targets retains only 10% of current focus. | **Decisive Focus** — At full focus, gain an additional 10% damage and consume the focus. Switching targets instead retains 90% of focus. |
| **Stonewall** (`stonewall`) | **Set Stance** — Physical HP damage taken builds 10% physical damage reduction, up to 30%. The stance expires after two of your actions without another physical hit. | **Release the Wall** — Your next DEF-scaled hit may consume the stance as bonus damage, trading protection for offense. |
| **Quickstep** (`quickstep`) | **Basic Momentum** — Landed basics build 10% gauge-fill speed, up to 30%. Using a skill resets momentum. | **Flowing Step** — Your next damaging skill first spends momentum to gain the same fraction of action gauge, capped at one ready turn. |
| **Lucky Star** (`lucky_star`) | **Windfall** — Critical hits store 10% Windfall, up to 30%. At full reserve, a later hit spends it as bonus damage. | **Guiding Star** — Healing or shielding skills may spend stored Windfall to grant the most injured ally additional healing or shield equal to their max HP times Windfall times 10%. Full-reserve damage remains available; works on yourself when alone. |

### Advanced combat roles (13)

| Tree | V | X |
|---|---|---|
| **Iron Reflexes** (`iron_reflexes`) | **Pressure Guard** — Physical hits lose 10% of raw damage and store converted DEX pressure, capped at 30% of max HP. Evasion and graze stay disabled. | **Tempered Answer** — The next physical strike spends the stored pressure as flat damage; magical strikes retain it. No additional action is granted. |
| **Resentment** (`resentment`) | **Red Memory** — Save 10% of normal red-pool decay into a reserve, capped at 30% of max HP. | **Revenge Release** — The next non-triggered hit spends the reserve as damage and sacrifices 10% of the ongoing STR pool. |
| **Death’s Door** (`deaths_door`) | **Borrowed Recovery** — Only the first survived lethal blow opens 2 own actions of healing 10% of missing HP. Survival odds are unchanged. | **Final Answer** — A survived lethal blow prepares one hit with 30% bonus damage, consumed on attack. Preparation ends above 30% max HP; recovery never refreshes. |
| **One More** (`one_more`) | **Carried Momentum** — Bank 10% of otherwise wasted gauge refund, capped at 30% of a gauge. Release into own ordinary gauge on a later action. | **Shared Opening** — Release the bank into the first living non-risen ally instead, capped at a full gauge. Use self when alone. Existing action limits remain. |
| **Latent Power** (`latent_power`) | **Stirring Power** — 2 actions before full activation, gain a short 10% damage anticipation window. Original activation threshold remains. | **Awakening Surge** — Full activation advances all current skill cooldowns by 2 once per fight. The normal full awakening still applies. |
| **Lone Wolf** (`lone_wolf`) | **Personal Challenge** — While alone, actions build 10% damage against a designated first living enemy, capped at 30%. Switching opponent resets progress. | **Duel Within The Crowd** — The challenge also builds with allies, at 10% of solo effectiveness. Original team stat penalty remains. |
| **Arise** (`arise`) | **Protective Shade** — A newly raised ally grants its summoner a barrier equal to 10% of its max HP and records ownership. Original raise limits remain. | **Shade Communion** — On its next action the owner sacrifices its living shade once, healing 10% of shade max HP and gaining 30% damage for 2 ticks. No extra action or revival. |
| **Last Rite** (`last_rite`) | **Living Legacy** — Own actions build a visible shield legacy by 10%, capped at 30%. On death each living ally receives max HP × reserve × original rite share as shield, alongside the original stat transfer. | **Early Bequest** — At full reserve, once per fight grant the first living non-risen ally max HP × reserve × 10% as shield, spending 10% of reserve. Remaining legacy can still transfer on death. |
| **Hemorrhage** (`hemorrhage`) | **Clot Memory** — A bleed burst retains at least 10% and at most 30% of the bleed threshold as overflow, including natural excess. The original burst remains. | **Spreading Rupture** — Also seed the same bounded overflow on one other living enemy without causing an immediate burst. Alone, add 10% of that overflow back to the original target. |
| **Privileged Status** (`privileged_status`) | **Read The Weakness** — DEF/WIS debuffs reduce mitigation by 10%; SPD/DEX debuffs refund a 1% gauge fraction; other stat debuffs heal 1% of missing HP and build a capped reserve. Original debuff-threshold damage stays. | **Exploit And Release** — At 30% reserve, empower one hit by the reserve, spend it, and remove one negative target effect, giving up sustained debuff pressure. |
| **Equilibrium** (`equilibrium`) | **Resonant Sequence** — 2 successive element changes discharge a 10% damage bonus; repeated elements reset progress. Original target marks still apply. | **Resonant Relay** — Discharge grants the first non-risen ally of another element 30% damage for 2 ticks; without such an ally empower the current hit by 30%. |
| **Corruption** (`corruption`) | **Forbidden Residue** — Each recorded skill lock adds 10% damage to remaining damaging skills, capped at 30%. Original duplicate cast and lock remain. | **Reclaimed Spell** — Once after 2 recorded locks, restore the oldest locked skill with 2 recovery cooldown. Spend residue; all subsequent damaging skills operate at 90% power and still lock normally. |
| **Dead Branch** (`dead_branch`) | **Guided Growth** — When drawing, first favor a skill role not currently present among unlocked skills. The original random draw picks within that role; an exhausted pool grants nothing. | **Signature Branch** — The first drawn skill becomes a temporary signature that resists one subsequent lock. It then follows normal cooldown and lock rules. No skill is inherited permanently. |

### Training and commitment (9)

| Tree | V | X |
|---|---|---|
| **Heirloom** (`heirloom`) | **Chosen Keepsake** — Nominate a bought keepsake stat. Each drill of it builds 10% extra growth, up to 30%. | **Living Inheritance** — Once per run, redirect 10% of the built keepsake growth to another stat. The original keeps the remainder. |
| **Mastery** (`mastery`) | **Master’s Commitment** — Commit to preserving one mastered ordinary capstone, or pursuing a bought ordinary capstone with one extra mastery step per rank victory. Chase, fusion and Mastery itself are excluded. | **Integrated Discipline** — While preserving your chosen mastered capstone, your current kit gains 10% growth to every trained stat. Pursuit remains available instead. |
| **Limit Break** (`limit_break`) | **Overflow Reserve** — Drilling a stat already at its normal cap stores 10% of its drill gain as a rank-fight DEF bonus, bounded at 30% of the normal stat cap. The training cap remains unchanged. | **Manifestation** — Once per run, commit the stored overflow to rank-fight STR or DEF. Stored overflow never feeds itself and cannot exceed its bound. |
| **Iron Vow** (`iron_vow`) | **Training Rhythm** — Consecutive drills of the same stat build 10% bonus drill gain each, up to 30%. Switching stats restarts the rhythm; rank fights clear it. Rest remains forbidden. | **Vow Recovery** — Spend 3 rhythm to recover 30% maximum energy without using Rest. Rebuilding rhythm still costs drills. |
| **Cursed Mark** (`cursed_mark`) | **Focused Curse** — Commit to a non-DEF specialization. Rank opponents lose another 10% of that stat after the ordinary curse. DEF training stays disabled. | **Shared Burden** — While alive in a team, reduce damage to another ally by 10% when the attacker has more DEF than you. Multiple carriers do not stack; no extra solo benefit. |
| **Devourer** (`devourer`) | **Directed Absorption** — Choose which trained stat receives future devoured growth. Rank wins remain required and cycle energy upkeep stays unchanged. | **Adaptive Feeding** — Choose to divert 30% of future devoured growth into a temporary chosen-stat bonus for the next rank fight, bounded at 30% of the normal stat cap. The remainder stays permanent. |
| **Second Breath** (`second_breath`) | **Refund Reserve** — Each refunded Super drill adds one reserve charge, up to 3. Low-energy drills still cannot Super. | **Steady Breath** — Spend 3 reserve charges to stabilize the next drill against injury. Failure, Super restrictions and the ordinary drill costs remain unchanged. |
| **Ascetic** (`ascetic`) | **Patient Practice** — Bank one action point within the current cycle, then release it when wanted. Unreleased actions expire at the cycle boundary; no extra actions are created. | **Chosen Sacrifice** — Once per run, exchange one action point for the normal victory SP amount. The exchange requires another action left; rank victories still grant no SP. |
| **Hungry Core** (`hungry_core`) | **Feeding Regimen** — Commit next cycle to modest feeding with 30% less upkeep and fed bonus, or heavy feeding with 30% more upkeep and fed bonus. Starvation still disables the core. | **Specialized Core** — Alternatively keep normal upkeep and move 30% of every other fed stat bonus into one chosen stat for the next cycle’s rank fight. Feeding choices never retune an already paid meal. |

## 6. Training choices and authority

The nine training lineages use explicit host-driven choices after the relevant capstone is active. The host supplies only legal choices, their descriptions and argument arrays. `lineageChoice(choice, statOrCapstone?)` validates rank, phase, bought capstone, charge and one-use limits in the sim; rejected choices mutate nothing and draw no RNG. The accepted command is logged and replayable. Its run-local state is cloned, saved and included in canonical hashes; it never grants account lineage points.

- Heirloom nominates a bought keepsake stat; X redirects once.
- Mastery commits once to preserve one already-mastered ordinary capstone or pursue a bought ordinary capstone. Chase, fusion and Mastery itself are excluded; X enhances the preserve route.
- Limit Break accumulates bounded excess, with X committing once to offense or protection; the normal training cap stays unchanged.
- Iron Vow's X spends three rhythm on recovery while Rest remains forbidden.
- Cursed Mark commits to a non-DEF specialization; DEF training remains disabled. X is team mitigation with no added solo effect.
- Devourer selects a future absorption stat or X adaptation; winning and energy upkeep still apply.
- Second Breath's X spends three refund charges to prevent the next drill injury; failure and Super restrictions remain.
- Ascetic banks/releases an existing cycle AP; X exchanges one AP for normal victory SP once, requiring another AP left. Rank wins still grant no SP.
- Hungry Core commits feeding before the next cycle; choices cannot retune an already paid meal. Starvation still disables it.

## 7. Save and server contract

Schema **14** stores `AccountState.Lineages`, a sorted dictionary keyed by tree id. Ledgers may be materialized lazily; an absent eligible tree reads as rank I with zero points and attempts. `Fortuna` remains a compatibility alias of `Lineages["fortune"]`, never an independent progression track. v13 migration copies the exact Fortune ledger into the dictionary, preserving points, failures, lifetime attempts and last feedback. New trees start at I. Old unbred runs and Vault monsters stay unbred; captured lineage snapshots remain unchanged.

`breed(primaryPosition, supportingPosition, talentsOff?, treeId?)` defaults omitted tree id to `fortune` for older callers. Reads collection, vault/entries and progress; writes training and wallet. `lineageChoice` reads/writes training. `finishRun` writes the focus ledger in progress atomically with the sealed result. Client and server share parsing and validation, and the ordinary checkpoint retry protocol protects attempt counters and awards. Local saves and server parts share migration and canonical hash behavior; no new external service or deployment model is required.

## 8. Breeding Workshop UI

The home **Breed** tab opens the **Breeding Workshop**, the user-selected Concept 2 direction (2026-11-05). The default workspace centers the primary and supporting parent cards with a prospective offspring preview. Parents sit side by side on desktop and stack on phones. Cost, eligibility, retained parents and the primary parent's species contribution remain explicit before committing. The dark violet and gold Workshop theme and wider desktop canvas apply only to Breed; other game screens keep their existing theme and width.

The focus-tree picker contains the searchable/filterable library of all 40 generic lineages. Changing focus must clear incompatible parent selections and refresh the offspring preview. The selected lineage's current rank, node points, next threshold/odds/pity, I–X progress and V/X effects stay alongside pairing rather than requiring a separate library-detail stage. An explicit offspring review precedes **Breed & train**; entering review does not spend shards or start training.

Training shows high-water node contribution and host-supplied milestone choices. Seal feedback reports automatic unlocks or the next protected attempt. The presentation change does not alter breeding eligibility, inherited ranks, point rewards or breakthrough rules.

The original dark visual explorations remain available, with embedded art and no external dependencies. Workshop supplies the production flow direction; the screenshots are concept captures rather than production captures:

- [Library concept](../design/lineage-mocks/lineage-library-concept.html), [desktop preview](../design/lineage-mocks/lineage-library-overview.png), [390px preview](../design/lineage-mocks/lineage-library-mobile.png).
- [Breeding Workshop concept (selected)](../design/lineage-mocks/lineage-workshop-concept.html), [desktop preview](../design/lineage-mocks/lineage-workshop-overview.png), [390px preview](../design/lineage-mocks/lineage-workshop-mobile.png).

The concepts use scripted breakthrough buttons to expose the result screen. Production resolves breakthrough automatically at sealing as specified above.

## 9. Validation and limits

Required coverage: all 40 eligible trees and both milestone ranks; positive normalized point awards; capstone gating and birth isolation; overwrite/fusion/reacquisition; threshold carry, retries and deterministic attempts; v13 migration and save/parts/server round trips; exact command replay and state hashes; combat bounds and team/solo recipients; all nine training choice families and original restrictions; browser workshop, focus-tree search, parent changes, offspring review and in-run choice flows at phone and desktop widths. Full repository validation is recorded with the implementation change; this document does not claim a new balance study. The two standalone concepts have separately passed their six-stage desktop/390px flow and JavaScript/overflow checks.
