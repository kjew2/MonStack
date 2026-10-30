# 12 - Monster Trainer: Consolidated Game Design

Status: **target design, not built.** This is the game the project is pivoting to. `specs/13` is the plan for getting there from the current idle game. Until a system here is built, the older specs still describe the code.

Source: five design drafts kj wrote on 2026-10-30 ("Fast-Paced Monster Raising & Tower Battles", "Monster Skill Trees & Skill Book System", "Project Monster Trainer", "Rogue-Lite Systems Trainer", "Systems-First Monster Trainer"). This spec merges them: every idea in them is here once. Where they disagreed, section 13 lists the conflict and a proposed resolution. Anything marked **(proposed)** is an agent's reconciliation and needs kj's confirmation; everything else is stated in the drafts.

> **Numbers are placeholders.** The drafts give a few numbers (12 epochs, milestones at 300/500/700/900, 100 gauntlet floors). Treat all of them as placeholders until the systems are built (`CLAUDE.md`, "Current focus").

## 1. Pitch

A fast, systems-driven monster-raising game in the line of *Monster Rancher* and *Dragon Quest Monsters*, with rogue-lite meta-progression and gacha collection. It is a reaction to story-heavy raising sims (*Uma Musume*, *Star Savior*): no visual novel, no event text, no cutscenes. A training run is an 8-10 minute sprint of pure decisions, and the fun is finding broken builds, in the spirit of *The Exiled Heavy Knight Knows How to Game the System* ("legalized cheating").

## 2. Pillars

1. **Systems over story.** Zero mandatory narrative. Every choice in a run immediately changes stats, fatigue, skills or risk.
2. **Rapid-fire runs.** One run is 8-10 minutes, so a session holds several runs and testing a strange build is cheap.
3. **Legalized exploitation.** Skill trees combine into hidden chase trees and rule-breaking passives. Finding them is the point.
4. **Rogue-lite meta-progression.** Every run, finished or failed, earns account-wide currency for permanent training upgrades. There is **no breeding**.
5. **Fair, low-stress live service.** Asynchronous PvP rewards are cosmetic only. Combat stats cannot be bought.

## 3. Core loop

```
 Gacha pull ──> species unlocked (base stats, 2 native trees, aptitudes)
      │
      ▼
 Training run (12 epochs, 8-10 min) ──> stats, skill trees, chase traits
      │
      ▼
 End-of-run gauntlet ──> Legacy Title, monster certified into the Vault
      │
      ▼
 Towers and PvP with Vault teams (4v1 Daily Tower, 4v4 Endless Tower, 4v4 async PvP)
      │
      ├── Training Shards ──> account talent tree (cheaper, stronger future runs)
      ├── Star-Gems ────────> gacha pulls (new species, aptitude ranks, cosmetics)
      └── Skill Books ──────> book-gated trees in future runs
```

1. **Summon** a species from the gacha.
2. **Train** it in a run: raise stats, manage energy and fatigue, build skill trees.
3. **Evaluate**: the gauntlet finds how far it gets and mints a Legacy Title. The monster joins the Vault.
4. **Challenge** towers and PvP with Vault teams.
5. **Progress**: shards buy permanent talents; gems buy pulls; books open new trees.

## 4. Training run

### 4.1 Shape

- One monster per run, picked from the species the account owns.
- **12 epochs** (one draft said 12-15; section 13 item 1). Each epoch is one macro decision. 8-10 minutes in total, so roughly 40 seconds of decisions per epoch including drafts.
- No events, dialogue or story scenes. Every screen is a mechanical choice.
- A run ends after epoch 12, or early if the player retires the monster (section 4.5).

### 4.2 Epoch actions

| Action | Effect |
|---|---|
| **Heavy Drill** | Large stat gain in the drilled stat(s); costs energy and adds fatigue. |
| **Maintenance / Rest** | Restores energy, removes fatigue, little or no stat gain. |
| **Tactical Challenge** | A fight during the run (proposed: an auto-battle against a scaled opponent). Earns skill points and progress toward playstyle conditions (section 5.5). |

The exact action list is open; the drafts name these three as examples.

### 4.3 Stats, energy, fatigue, injury

- **Stats:** STR, INT, WIS, DEX, DEF, SPD (the six the drafts name). No draft names HP; battle needs one (section 13 item 4).
- **Energy** pays for actions. The talent tree reduces energy cost.
- **Fatigue** builds with hard training. Training at high fatigue raises **injury risk** but gives the large gains needed to cross a milestone before epoch 12. This is the run's main risk-versus-reward decision.
- **Skill points** are earned during the run and spent in trees. Saving them is a strategy (section 5.4). How points are earned is open (proposed: per epoch, plus extra from Tactical Challenges).

### 4.4 Attribute milestones and drafts

When a stat crosses **300 / 500 / 700 / 900**, the run pauses for a **3-card tree draft** themed on that stat (example at 700 WIS: *Mind Warp*, *Overclock*, *Aether Conduit*). The picked tree takes the monster's draft slot (section 5.1). The talent tree can add draft slots or options.

### 4.5 End of run

- After epoch 12 the monster goes to the gauntlet (section 6) and then into the Vault.
- Both completed and failed runs pay Training Shards. What "failed" means (injury, collapse, retiring early) and whether a failed monster still enters the Vault is open (section 13 item 9).

## 5. Skill trees

Monsters are skill vessels. Trees do not hand out flat percentage boosts; they change combat mechanics and rules. (One draft says base trees "provide essential stats"; section 13 item 3.)

### 5.1 Tree slots (proposed reconciliation, section 13 item 2)

Up to **4 trees** per monster per run:

| Slot | Source | When |
|---|---|---|
| Native A, Native B | The species (e.g. *Slime Fundamentals*, *Pyromancy*, *Pyre Core*, *Aether Striker*) | From the start |
| Draft | A milestone draft (section 4.4), a random mid-run offer, or a consumed Skill Book's tree (section 5.6) | Mid-run |
| Chase | A chase tree whose unlock conditions were met (section 5.3) | Mid-to-late run |

### 5.2 Chase traits

Every tree has a hidden capstone, its **chase trait**, that breaks a standard rule. Maxing the tree reveals and unlocks it. Example: *Omniscience*: ignores resistances and refunds 50% energy.

### 5.3 Chase trees

Rare trees unlocked mid-run by meeting conditions across trees and stats. Two unlock routes:

- **In-run:** stat thresholds plus nodes in other trees. Examples: maxing *Pyromancy* + *Geomancy* unlocks *Magma Overlord SP*; maxing *Heavy Armor* and reaching 500 DEF unlocks *Unbreakable Will*.
- **Book-gated:** a Skill Book tree plus nodes in other trees plus a playstyle condition (section 5.6).

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

- Earned in the Daily Tower (section 8.2), kept in the account inventory.
- Consuming a book at the start of (or during) a run opens its **Book Tree** for that run.
- A book-gated chase tree unlocks when all of these hold: points in named Book Tree nodes, points in named nodes of other trees, and a playstyle condition.

### 5.7 Genetic aptitude

Duplicate pulls raise a species' **Genetic Aptitude rank**, which raises the odds that rare drafts and chase trees appear in that species' future runs (example: +15% chance for Fire chase trees). Aptitude never adds raw stats. One draft also mentions "Hidden Aptitude Trees" from duplicates (section 13 item 5).

## 6. End-of-run gauntlet and Legacy Titles

- The finished monster is auto-simulated against up to **100 escalating floors** to find where it fails.
- Presentation: a slowing wheel that lands on the floor and enemy that beat it, then a comic defeat animation (turned to ash, launched off screen).
- The monster gets a permanent **Legacy Title** from the result, e.g. "Floor 42: Professional Punching Bag", "Floor 83: Hydra Food", "Floor 95: God-Slayer Slime".
- The certified monster is stored in the Vault for towers and PvP.
- **MVP stand-in:** a static 3-boss ladder and a score screen with a letter grade (S, A, B) and a stat breakdown.

## 7. Roster

- The **Vault** holds every certified monster. Teams for 4v1 and 4v4 content come from it.
- **Onboarding:** a new account has 1 custom-trained slot plus 3 Mercenary starter units so 4-monster content is playable at once. Mercenaries are replaced as raised monsters retire into the Vault.

## 8. Combat

### 8.1 Rules

- Turn-based, speed-initiative auto-battle, kept clean and predictable so offline simulation works.
- 4v4 teams act on an **IF/THEN rulebook** (gambits), e.g. "If enemy HP < 30% -> priority: high-damage burst skill". The defender sets it for PvP defense.

### 8.2 Modes

| Mode | Format | Focus | Rewards |
|---|---|---|---|
| End-of-run Gauntlet | 1 monster vs 100 floors | How far one build goes | Legacy Title, shards |
| Daily Tower ("Daily Challenge Tower", "Daily Tower Dive") | 4v1 boss floors, daily | Single-target DPS, taunts, survivability, status mitigation, boss phases | Star-Gems on first clears and milestones; Skill Books (RNG per floor/boss, rarer books deeper) |
| Endless Tower | 4v4, escalating, with mutators | Team synergy, turn-order control, AoE burst, rulebooks | Shards, gems |
| Offline Ranked PvP | 4v4 async against stored teams | Rulebook tactics | Cosmetic only: titles, skins, recolors, auras, badges, borders |

### 8.3 Anti-trivialization without immunities

Bosses never get hard immunities ("immune to stun", "immune to magic"). Broken builds are checked by structure instead:

1. **Overload / Entropy gauge.** High-frequency loops, infinite energy and huge multipliers build entropy. At 100 the excess vents: self-recoil, a stun, or an enraged enemy, unless the build manages or converts entropy.
2. **Phase shifts and lateral objectives.** Bosses change the win condition mid-fight: single-target DPS check, then a multi-target or interrupt phase (target split, channel interrupts), then a survival clock. This punishes pure single-target glass cannons and rewards 4-monster synergy.
3. **Escalating environmental clock.** Arena corruption grows over turns, cutting healing and adding damage, which caps stall and immortality builds.

## 9. Meta-progression

The account-wide rogue-lite talent tree, bought with Training Shards. Named examples: -5% energy cost per action, +10% base stat floors, more milestone draft slots, access to more skill trees.

## 10. Gacha and currencies

### 10.1 Gacha

- Pulls unlock **species** (base stats, 2 native trees, aptitudes) and cosmetic variants.
- **Duplicates** raise Genetic Aptitude ranks and give alternate skins or utility shards, never raw stat inflation.

### 10.2 Currencies

| Currency | Earned from | Spent on |
|---|---|---|
| Training Shards (soft) | Completed and failed runs, gauntlets, towers | Talent tree |
| Star-Gems (premium) | First-time tower clears, daily milestones, achievement ladders | Gacha pulls |
| Skill Books (items) | Daily Tower drops | Book Trees in runs |

## 11. Monetization and operations

- **Free to play plus a one-time ad-removal pass** ($4.99-$9.99).
- Free tier: opt-in ads for utilities only (reroll a starting run trait, double earned shards, quick stamina refill). No forced interstitials.
- The pass removes ad prompts and grants the ad utilities automatically.
- Gacha pulls and cosmetics as above. No draft says gems are sold for money (section 13 item 10).
- **Server cost:** offline 4v4 teams are static JSON (~1 KB). Estimated under $30-$50/month for 10,000-50,000 active players.
- **Platform:** PC (Steam) and web with a clean, spreadsheet-transparent UI (section 13 item 11).

## 12. Scope

### 12.1 MVP: single-player demo

Estimated 150-200 hours (8-12 weeks part time with AI help).

- Training run engine (12 epochs).
- 3 species.
- 6 base trees + 2 chase synergy trees.
- Static 3-boss ladder instead of towers.
- Score screen with letter grade (S, A, B) and stat breakdown.

Not in the MVP: multiplayer and database, gacha UI, Endless Tower scaling, PvP.

### 12.2 1.0 launch

- Async 4v4 PvP sync.
- Daily Tower and Endless Tower with mutators.
- Ad-removal integration and gacha.
- Steam and web launch.

## 13. Conflicts between the drafts and open questions

Proposed resolutions, for kj to confirm or overrule. `specs/13` section 9 repeats the ones that block work.

1. **Run length.** 12 epochs (four drafts) vs 12-15 (one). Proposed: 12, as a content constant.
2. **Tree slots.** The drafts give four different layouts: 2 native + chase trees; 2 base + 2 chase (one in-run, one book-gated); 2 native + 1 flex/draft + 1 secret chase; 2 native + 1 flex + 1 generic milestone. Proposed: section 5.1 (2 native + 1 draft + 1 chase, with two routes into the chase slot, and the Book Tree competing for the draft slot), plus a chase trait at the top of every tree (section 5.2).
3. **What trees give.** "Trees do not provide passive percentage boosts" vs "base trees provide essential stats". Proposed: native trees may give stats and core skills; draft trees, chase trees and chase traits change rules.
4. **HP.** The six named stats have no HP. Proposed: add HP as a seventh trained stat (*Monster Rancher*'s LIF), milestones on all seven.
5. **Duplicates.** "Raise chase-tree odds" (four drafts) vs "Hidden Aptitude Trees" (one). Proposed: odds only, no extra tree.
6. **Daily tower naming.** "Daily Challenge Tower" and "Daily Tower Dive" are treated as one mode. Endless Tower is 4v4 in one draft and grouped with 4v1 in others; proposed 4v4.
7. **Dodges in an auto-battle.** "Perfect dodges" implies action input. Proposed: combat stays automatic and the condition counts evasions.
8. **Where in-run fights come from.** Playstyle conditions need fights during the run. Proposed: Tactical Challenge is that fight.
9. **Failed runs.** What fails a run, and does the monster still enter the Vault? Proposed: an injury at high fatigue ends the run early; the monster is still certified and vaulted (the gauntlet scores it low), and the run still pays shards.
10. **Gem sales.** The drafts describe a gacha with premium currency but only an ad-removal pass as revenue. Proposed: no gem sales at launch; revenue is the pass and cosmetics. Monetization is kj's call.
11. **Platform.** The drafts say Steam and web; the current repo targets iOS (issues #6, #10, #11). Open.
12. **Stamina.** "Quick stamina refills" implies an account stamina gate on runs; no draft describes it. Proposed: no stamina gate in the MVP.
13. **Idle.** No draft has an idle or offline layer. Proposed: drop it (offline catch-up, idle chest, stage push).
14. **Roles and elements.** No draft mentions the current game's 5 roles and 5 elements. Proposed: keep the 5 elements as species/skill types; roles come from trees, not fixed per species.
