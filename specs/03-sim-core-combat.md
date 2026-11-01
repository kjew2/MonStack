# 03 - Sim Core

Scope: the `IdleRng.Sim` assembly. Pure C#, no `UnityEngine` reference, no IO, clock or JSON. It holds the monster battle (`specs/16`), the training run (`specs/14`, `specs/15`), the account (`specs/18`), the gauntlet (`specs/19`), the Dungeon (`specs/21`), the content database (`specs/05`) and the RNG. The web bridge (`src/IdleRng.Web`), the bots (`specs/09`) and the save layer (`IdleRng.Runtime`, `specs/06`) all drive this one assembly.

This spec was the idle game's tick-loop spec until the pivot (`specs/13` T7 removed that game). It now keeps what every part of the sim still shares: the design rules, the numeric model and the RNG.

## 1. Design rules

1. Everything observable is a pure function of `(ContentDb, inputs)` plus the sequence of commands applied. State is a plain mutable graph (`RunState`, `AccountState`, `FightState`) that clones and serialises as it is.
2. Replay is exact. A run replays from `(species, seed, books, modifiers, command log)` to the same `StateHash`; a fight from `(rules, fighters, seed)`; a gauntlet from `(fighter, seed)`.
3. Deterministic across machines: byte-identical output for the same inputs on x64, ARM64, IL2CPP and WebAssembly. No clock reads, no `Math.Pow`/`Exp`/`Log` (section 2), no reflection, no unordered iteration over hashed collections where the order matters.
4. Content is data. Every number lives in a table or constant of `specs/05`; code never holds a balance literal.
5. Canonical hashes (`TrainingRun.StateHash`, `BattleSim.StateHash`, `GauntletSim.Result.Hash`) are SHA-256 over fixed binary encodings built with `CanonicalWriter` (`src/IdleRng.Sim/Replay/Canonical.cs`): little-endian, doubles as raw IEEE-754 bits, strings as int32 UTF-8 length plus bytes, sorted maps. Changing what is hashed bumps that hash's version constant.

## 2. Numeric model

Magnitudes (stats, damage, HP) are `double`; counts (shards, levels, ticks, RNG counters) are `int`, `long` or `ulong`. `float` and `decimal` are not used.

**No transcendental functions.** `Math.Pow` and friends are not bit-exact across libm implementations. Geometric curves are built by repeated multiplication, which uses only IEEE 754 basic operations (the gauntlet's floor levels, `GauntletSim.Levels`, do this).

## 5. RNG design

### 5.1 Requirements

1. Random access: result `k` of a stream is a pure function `Derive(seed, k)`. The commitment in `specs/01` section 6 depends on this: a restored save restores an older counter and re-receives the same results.
2. Named, independent streams (section 5.5). A fight, a training run and an account each derive their own, so adding one consumer never changes another's results. Consuming draws on one stream never changes another.
3. 53-bit uniform doubles, so `(int)(u * n)` is unbiased for any pool size we have.
4. Trivially reimplementable in Python (tools), Go or TypeScript (server), with published test vectors.

### 5.2 Algorithm: SplitMix64 as a stateless derive

```csharp
namespace IdleRng.Sim.Rng
{
    public static class SplitMix64
    {
        public const ulong Gamma = 0x9E3779B97F4A7C15UL;

        public static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// Output k (0-based) of the reference SplitMix64 generator seeded with `seed`.
        public static ulong Derive(ulong seed, ulong k) => Mix(unchecked(seed + (k + 1) * Gamma));

        /// Uniform in [0, 1) with 53 bits of precision.
        public static double ToUnit(ulong x) => (x >> 11) * (1.0 / (1UL << 53));
    }

    public static class Fnv1a64
    {
        public static ulong Hash(string s)   // FNV-1a over the UTF-8 bytes
        {
            ulong h = 0xCBF29CE484222325UL;
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes(s)) { h ^= b; h *= 0x100000001B3UL; }
            return h;
        }
    }

    public static class StreamNames
    {
        /// Per-stream seed. The seed comes from the caller (a run seed, a fight seed, the account seed).
        public static ulong StreamSeed(ulong profileSeed, string name) => SplitMix64.Mix(unchecked(profileSeed + Fnv1a64.Hash(name)));
    }

    /// Persisted as part of GameState.Rng. Seed and Counter are the entire state.
    public sealed class RngStream
    {
        public string Name { get; init; }
        public ulong Seed { get; init; }
        public ulong Counter { get; set; }

        public double NextDouble() => SplitMix64.ToUnit(SplitMix64.Derive(Seed, Counter++));
        public int NextIndex(int n) => (int)(NextDouble() * n);              // 0..n-1; same arithmetic as specs/01 section 8
        public double PeekDouble(ulong counter) => SplitMix64.ToUnit(SplitMix64.Derive(Seed, counter));   // audit and tests only
    }
}
```

Why SplitMix64 and not PCG32 or `System.Random`:

- SplitMix64's output is already a hash of `seed + (k+1) * gamma`; "stateless derive" is its natural form, not a bolt-on. PCG32 is a sequential LCG; random access needs an O(log k) jump-ahead and its 32-bit output needs two draws per 53-bit double. Both are solvable but add code and audit surface for no gain.
- `Derive(seed, k)` is exactly the k-th output of the reference generator (Java `SplittableRandom`, Rust `splitmix64`, every C reference). A server team can audit a pull with any off-the-shelf implementation.
- `System.Random` changed algorithm in .NET 6, is not available with the same semantics inside Unity's runtime and has no random access.
- Quality is adequate for a game: SplitMix64 passes BigCrush. It is not a CSPRNG; a server-held seed's secrecy (for a paid random draw, should one return: `specs/01`, retired) would come from the server never sending it, not from the mixer.

The `+ 1` in `Derive` matters: `Mix(0) == 0`, so without the offset a zero seed would start with a zero draw. With the offset the first output for seed 0 is the standard reference value below.

### 5.3 Test vectors

Produced by the Python reference in section 5.4. The first line is the published SplitMix64 reference value (`new SplittableRandom(0).nextLong()` in Java is `-2152535657050944081`, which is this number as a signed 64-bit integer).

```
Derive(0x0000000000000000, 0)    = 0xE220A8397B1DCDAF   ToUnit = 0.88331080821364261
Derive(0x0000000000000000, 1)    = 0x6E789E6AA1B965F4   ToUnit = 0.43152799704850997
Derive(0x0000000000000000, 2)    = 0x06C45D188009454F   ToUnit = 0.026433771592597743
Derive(0x0000000000000000, 1000) = 0x2CFA2F23425329E1   ToUnit = 0.17569250689332405
Derive(0x0000000000000007, 0)    = 0x63CBE1E459320DD7   ToUnit = 0.38982974839127149
Derive(0x0000000000000007, 1)    = 0x044C3CD7F43C661C   ToUnit = 0.016788294528156111
Derive(0x0000000000000007, 2)    = 0xE6984080BAB12A02   ToUnit = 0.90076068060688341
Derive(0x0000000000000007, 1000) = 0xCF1B8D545D1615CA   ToUnit = 0.80901416120203629
Derive(0x123456789ABCDEF0, 0)    = 0x161922C645CE50E8   ToUnit = 0.08632104244844041
Derive(0x123456789ABCDEF0, 1)    = 0xAD760CAFA1697B60   ToUnit = 0.67758254325727829

Fnv1a64("combat")      = 0xCA0BF97CCE717B21   StreamSeed(7, "combat")      = 0xD638BA460341F349
Fnv1a64("enhance")     = 0xDC465426329C1EE9   StreamSeed(7, "enhance")     = 0x84C074F86395F937
```

### 5.4 Python reference (for `tools/` and server audits)

```python
M = (1 << 64) - 1
GAMMA = 0x9E3779B97F4A7C15
def mix64(z):
    z &= M
    z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & M
    z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & M
    return (z ^ (z >> 31)) & M
def derive(seed, k):  return mix64((seed + (k + 1) * GAMMA) & M)
def to_unit(x):       return (x >> 11) * (1.0 / (1 << 53))
def fnv1a64(s):
    h = 0xCBF29CE484222325
    for b in s.encode(): h ^= b; h = (h * 0x100000001B3) & M
    return h
def stream_seed(profile_seed, name): return mix64((profile_seed + fnv1a64(name)) & M)
```

### 5.5 Streams in use

| Stream | Owner | Draws |
|---|---|---|
| `training` | a training run | drill tier, drill gain, injury (`specs/14` section 4) |
| `draft` | a training run | one per tree picked for a milestone draft (`specs/15` section 4) |
| `fight` | a training run | derives the seed of each rank fight |
| `battle_sim` | one fight | evade, crit and target draws (`specs/16` section 5.3) |
| `gauntlet` | a gauntlet | derives the seed of each floor's fight |
| `tower` | `TOWER_SEED` and the UTC day | the day's seed and its lineup draws (`specs/21` section 4) |
| `tower_fight`, `drop` | the day's seed | derive each floor's fight seed and Skill Book roll (`specs/21` sections 2 and 7) |
| `run` | the account | derives each run's seed (`specs/18` section 2) |
| `vendor` | the account | the post-run vendor's offers: per offer a rarity draw, then a talent draw; seeded per run index (`specs/18` section 4) |

Every stream is `(seed, counter)`; the counter lives in the state it belongs to. Callers derive a child seed with `SplitMix64.Derive(StreamNames.StreamSeed(seed, name), index)`.

## 6. Unit tests

`src/IdleRng.Sim.Tests`: `RngTests` (the vectors above), `BattleSimTests`, `TrainingRunTests`, `SkillTreeTests`, `AccountTests`, `VaultTests`, `GauntletTests`, `TowerBattleTests`, `TowerTests`, `ContentDbTests`. Golden files for the battle, the runs, the gauntlet and the tower are in `specs/09`.
