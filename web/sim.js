// Idle RNG browser prototype: the pure game core.
//
// This file is the TypeScript-free mirror of the planned C# IdleRng.Sim assembly (specs/03). It follows
// the same rules so the eventual port is a translation, not a redesign:
//   - no DOM, no Date.now(), no Math.random(): wall time comes in through syncWallClock(), randomness
//     through named SplitMix64 streams (specs/03 section 5);
//   - no Math.pow/exp/log: geometric curves are tables built by repeated multiplication (specs/03 section 4);
//   - every balance number comes from the content object (web/content.js), never a literal here.
// The same file runs in the browser (window.IdleSim) and in Node (require) for tests and the bot.
(function (root) {
  'use strict';

  // ---------------------------------------------------------------- RNG (specs/03 section 5.2)
  const M64 = (1n << 64n) - 1n;
  const GAMMA = 0x9E3779B97F4A7C15n;
  const TWO53 = 9007199254740992;

  function mix64(z) {
    z &= M64;
    z = ((z ^ (z >> 30n)) * 0xBF58476D1CE4E5B9n) & M64;
    z = ((z ^ (z >> 27n)) * 0x94D049BB133111EBn) & M64;
    return (z ^ (z >> 31n)) & M64;
  }
  function derive(seed, k) { return mix64((seed + (BigInt(k) + 1n) * GAMMA) & M64); }
  function toUnit(x) { return Number(x >> 11n) / TWO53; }
  function fnv1a64(s) {
    let h = 0xCBF29CE484222325n;
    const bytes = typeof TextEncoder !== 'undefined' ? new TextEncoder().encode(s) : Buffer.from(s, 'utf8');
    for (const b of bytes) { h ^= BigInt(b); h = (h * 0x100000001B3n) & M64; }
    return h;
  }
  const STREAMS = ['combat', 'gear_drop', 'gear_rarity', 'enhance', 'gacha_hero'];
  function streamSeed(profileSeed, name) { return mix64((profileSeed + fnv1a64(name)) & M64); }

  // A stream is { seed: BigInt, counter: Number }; the counter lives in the save.
  function next(stream) { return toUnit(derive(stream.seed, stream.counter++)); }

  // ---------------------------------------------------------------- curves (specs/03 section 4)
  function GeoTable(a, g) { this.v = [a]; this.g = g; }
  GeoTable.prototype.at = function (k) {
    const v = this.v;
    while (v.length <= k) v.push(v[v.length - 1] * this.g);
    return v[k];
  };

  function sample(weights, u) {
    let acc = 0;
    for (let i = 0; i < weights.length; i++) { acc += weights[i]; if (u < acc) return i; }
    return weights.length - 1;
  }
  // Draw over indices [lo, hi] with weights rescaled to sum to 1.
  function sampleRange(weights, lo, hi, u) {
    let total = 0;
    for (let i = lo; i <= hi; i++) total += weights[i];
    let acc = 0;
    for (let i = lo; i <= hi; i++) { acc += weights[i] / total; if (u < acc) return i; }
    return hi;
  }

  // ---------------------------------------------------------------- pure helpers
  function pityRate(banner, n) {
    if (n >= banner.PITY_HARD) return 1;
    return Math.min(1, banner.BASE_LPLUS + banner.PITY_SOFT_STEP * Math.max(0, n - banner.PITY_SOFT_START));
  }

  // specs/01 section 3.3: exact consolidated rates, no simulation.
  function consolidatedRates(content) {
    const b = content.banner;
    let S = 1, E = 0;
    for (let n = 1; n <= b.PITY_HARD; n++) {
      const q = pityRate(b, n);
      E += n * S * q;
      S *= 1 - q;
    }
    const lplus = 1 / E;
    return { expectedPulls: E, lplus: lplus, m: lplus * b.M_SHARE_OF_LPLUS, l: lplus * (1 - b.M_SHARE_OF_LPLUS) };
  }

  function heroLevel(content, copies) {
    let lvl = 0;
    for (const c of content.heroes.LVL_CUM) if (copies >= c) lvl++;
    return lvl;
  }

  // Short big-number format (specs/03 section 4): 3 significant digits, K M B T then aa, ab, ...
  function formatShort(v) {
    if (!isFinite(v)) return String(v);
    const neg = v < 0; v = Math.abs(v);
    if (v < 1000) {
      const s = v < 10 && v !== Math.floor(v) ? v.toFixed(2).replace(/\.?0+$/, '') : String(Math.round(v));
      return (neg ? '-' : '') + s;
    }
    const suffixes = ['', 'K', 'M', 'B', 'T'];
    let e = 0;
    while (v >= 999.5) { v /= 1000; e++; }
    let suf;
    if (e < suffixes.length) suf = suffixes[e];
    else {
      const k = e - suffixes.length; // 0 -> aa
      suf = String.fromCharCode(97 + Math.floor(k / 26)) + String.fromCharCode(97 + (k % 26));
    }
    const digits = v >= 100 ? 0 : v >= 10 ? 1 : 2;
    return (neg ? '-' : '') + v.toFixed(digits) + suf;
  }

  // ---------------------------------------------------------------- profile creation
  const SCHEMA_VERSION = 1;

  function newState(content, opts) {
    const seed = BigInt.asUintN(64, BigInt(opts.profileSeed));
    const now = opts.nowMs;
    const streams = {};
    for (const name of STREAMS) streams[name] = { seed: streamSeed(seed, name), counter: 0 };
    return {
      schema: SCHEMA_VERSION,
      contentVersion: content.version,
      profileId: opts.profileId || 'local',
      createdUtcMs: now,
      tick: 0,
      rng: { profileSeed: seed, streams: streams },
      stage: { stage: 1, killsInStage: 0, boss: 'none', bossFailsOnStage: 0 },
      combat: { phase: 'spawning', spawnTimer: content.combat.SPAWN_DELAY_TICKS, enemyHp: 0, enemyMaxHp: 0,
                enemyIsBoss: false, attackTimer: 0, bossTimer: 0 },
      cur: { gold: 0, gems: 0, shards: 0, freePullsDaily: 0, freePullsBanked: 0 },
      statLevel: 0,
      gear: content.gear.SLOTS.map(function () { return null; }),
      heroes: { owned: {}, party: new Array(content.heroes.SLOTS).fill(null) },
      banner: { pity: 0, totalPulls: 0 },
      classAdvances: 0,
      daily: { dayKey: -1, progress: content.quests.map(function () { return 0; }),
               rewarded: content.quests.map(function () { return false; }) },
      prog: { highestFirstClearBoss: 0, chestTimerStartMs: now, tutorialGranted: false },
      clock: { lastSyncUtcMs: now, tickAtLastSync: 0 },
      life: { kills: 0, bossKills: 0, bossFails: 0, gearDrops: 0, enhAttempts: 0, enhSuccesses: 0,
              goldEarned: 0, offlineGold: 0, firstWallStage: 0, pulls: 0, lplus: 0 },
    };
  }

  // ---------------------------------------------------------------- the simulation
  function Sim(content, state) {
    this.c = content;
    this.state = state;
    this.events = [];
    this.emitHits = false;
    const cb = content.combat, ec = content.economy, g = content.gear;
    this.hpT = new GeoTable(cb.HP0, cb.HP_G);
    this.goldT = new GeoTable(ec.GOLD0, ec.GOLD_G);
    this.statT = new GeoTable(ec.STAT_COST0, ec.STAT_COST_G);
    this.gearT = new GeoTable(1, g.SLOT_G);
    this.classT = new GeoTable(1, content.class.MULT);
    this.heroById = {};
    const rar = content.rarities;
    content.heroes.pool.forEach(function (tierList) {
      tierList.forEach(function (h) { this.heroById[h.id] = { id: h.id, name: h.name, short: h.short || h.name, tier: rar.indexOf(h.tier) }; }, this);
    }, this);
    this._dirty = true;
  }

  Sim.newProfile = function (content, opts) {
    const sim = new Sim(content, newState(content, opts));
    const s = sim.state, ec = content.economy;
    // specs/04 section 8: one-time tutorial grants, then the first day's login and free pulls.
    if (!s.prog.tutorialGranted) {
      s.cur.gems += ec.TUTORIAL_GEMS;
      s.cur.freePullsBanked += ec.TUTORIAL_FREE_PULLS;
      s.prog.tutorialGranted = true;
    }
    sim._ensureDay(opts.nowMs);
    return sim;
  };

  const P = Sim.prototype;

  // Every random draw goes through here; tests replace it to script outcomes.
  P._draw = function (stream) { return next(stream); };

  P._emit = function (e) { e.tick = this.state.tick; this.events.push(e); };
  P.drain = function () { const e = this.events; this.events = []; return e; };
  P.nowMs = function () {
    const s = this.state;
    return s.clock.lastSyncUtcMs + (s.tick - s.clock.tickAtLastSync) * (1000 / this.c.combat.TICK_HZ);
  };

  // ---------- queries
  P.hp = function (stage) { return this.hpT.at(stage); };
  P.bossHp = function (stage) { return this.c.combat.BOSS_HP_MULT * this.hpT.at(stage); };
  P.gold = function (stage) { return this.goldT.at(stage); };
  P.statCost = function (n) { return this.statT.at(n); };
  P.statMult = function (n) { return 1 + this.c.economy.STAT_PER_LVL * (n === undefined ? this.state.statLevel : n); };
  P.gearTier = function (stage) { const w = this.c.gear.TIER_WIDTH; return Math.floor(stage / w) * w; };
  P.gearBasePct = function (piece) { return piece ? (this.gearT.at(piece.tier) - 1) * this.c.gear.RARITY_MULT[piece.rarity] : 0; };
  P.gearEffPct = function (piece) { return piece ? this.gearBasePct(piece) * (1 + this.c.gear.ENH_PER_LVL * piece.enh) : 0; };
  P.gearSlotMult = function (slot) { return 1 + this.gearEffPct(this.state.gear[slot]); };
  P.gearMult = function () {
    let m = 1;
    for (let i = 0; i < this.state.gear.length; i++) m *= this.gearSlotMult(i);
    return m;
  };
  P.heroLevel = function (copies) { return heroLevel(this.c, copies); };
  P.heroPct = function (id) {
    const copies = this.state.heroes.owned[id] || 0;
    if (!copies) return 0;
    const h = this.heroById[id], hc = this.c.heroes;
    return hc.BASE_PCT[h.tier] * (1 + hc.LVL_BONUS * (heroLevel(this.c, copies) - 1));
  };
  P.heroMult = function () {
    let sum = 0;
    for (const id of this.state.heroes.party) if (id) sum += this.heroPct(id);
    return 1 + sum / 100;
  };
  P.classMult = function (k) { return this.classT.at(k === undefined ? this.state.classAdvances : k); };
  P._recalc = function () {
    this._dps = this.c.combat.BASE_ATK * this.statMult() * this.gearMult() * this.heroMult() * this.classMult();
    this._hit = this._dps * this.c.combat.ATTACK_INTERVAL_TICKS / this.c.combat.TICK_HZ;
    this._dirty = false;
  };
  P.dps = function () { if (this._dirty) this._recalc(); return this._dps; };
  P.hitDamage = function () { if (this._dirty) this._recalc(); return this._hit; };
  P.hitsToKill = function (hp) { return Math.max(1, Math.ceil(hp / this.hitDamage())); };
  P.killTimeSeconds = function (stage) {
    const cb = this.c.combat;
    return (this.hitsToKill(this.hp(stage)) * cb.ATTACK_INTERVAL_TICKS + cb.SPAWN_DELAY_TICKS) / cb.TICK_HZ;
  };
  P.isBossStage = function () { return this.state.stage.stage % this.c.combat.BOSS_EVERY === 0; };
  P.bossClearable = function () {
    const cb = this.c.combat;
    return this.hitsToKill(this.bossHp(this.state.stage.stage)) * cb.ATTACK_INTERVAL_TICKS <= cb.BOSS_TIME_TICKS;
  };
  // DPS needed to kill the current stage's boss inside the timer.
  P.needDps = function () {
    const cb = this.c.combat;
    const hits = cb.BOSS_TIME_TICKS / cb.ATTACK_INTERVAL_TICKS;
    return (this.bossHp(this.state.stage.stage) / hits) * cb.TICK_HZ / cb.ATTACK_INTERVAL_TICKS;
  };
  P.enhanceCost = function () { return this.c.gear.ENH_COST_GOLD_MULT * this.gold(this.state.stage.stage); };
  P.enhanceSuccess = function (lvl) { const g = this.c.gear; return Math.max(g.ENH_SUCCESS_FLOOR, 1 - g.ENH_SUCCESS_STEP * lvl); };
  P.pityRate = function (n) { return pityRate(this.c.banner, n); };
  P.freePullsRemaining = function () { return this.state.cur.freePullsDaily + this.state.cur.freePullsBanked; };
  P.chestReadyAtMs = function () { return this.state.prog.chestTimerStartMs + this.c.economy.IDLE_CHEST_HOURS * 3600000; };
  P.chestReady = function () { return this.nowMs() >= this.chestReadyAtMs(); };
  P.bulkCost = function (n, m) { let t = 0; for (let i = 0; i < m; i++) t += this.statCost(n + i); return t; };
  P.maxAffordable = function (gold) {
    if (gold === undefined) gold = this.state.cur.gold;
    let n = this.state.statLevel, m = 0, acc = 0;
    while (m < 100000) {
      const c = this.statCost(n + m);
      if (acc + c > gold) break;
      acc += c; m++;
    }
    return m;
  };

  // ---------- tick loop (specs/03 section 7)
  P.tick = function () {
    const s = this.state, cbc = this.c.combat, cb = s.combat;
    s.tick += 1;
    this._ensureDay(this.nowMs());
    if (cb.phase === 'spawning') {
      cb.spawnTimer -= 1;
      if (cb.spawnTimer <= 0) this._spawn();
      return;
    }
    cb.attackTimer -= 1;
    if (cb.attackTimer === 0) {
      cb.attackTimer = cbc.ATTACK_INTERVAL_TICKS;
      const dmg = this.hitDamage();
      cb.enemyHp -= dmg;
      if (this.emitHits) this._emit({ type: 'hit', dmg: dmg });
      if (cb.enemyHp <= 0) { this._kill(); return; }
    }
    if (cb.enemyIsBoss) {
      cb.bossTimer -= 1;
      if (cb.bossTimer === 0) this._bossFail();
    }
  };
  P.advance = function (ticks) { for (let i = 0; i < ticks; i++) this.tick(); };

  P._spawn = function () {
    const s = this.state, cbc = this.c.combat, cb = s.combat;
    const isBoss = s.stage.boss === 'pending';
    cb.enemyIsBoss = isBoss;
    cb.enemyMaxHp = cb.enemyHp = isBoss ? this.bossHp(s.stage.stage) : this.hp(s.stage.stage);
    cb.attackTimer = cbc.ATTACK_INTERVAL_TICKS;
    cb.bossTimer = isBoss ? cbc.BOSS_TIME_TICKS : 0;
    cb.phase = 'fighting';
    if (isBoss) this._progress('bossAttempts', 1);
    this._emit({ type: 'spawn', boss: isBoss, stage: s.stage.stage });
  };

  P._kill = function () {
    const s = this.state, cbc = this.c.combat, st = s.stage;
    const stage = st.stage, isBoss = s.combat.enemyIsBoss;
    const gold = this.gold(stage) * (isBoss ? cbc.BOSS_GOLD_MULT : 1);
    s.cur.gold += gold; s.life.goldEarned += gold; s.life.kills += 1;
    if (isBoss) s.life.bossKills += 1;
    this._emit({ type: 'kill', stage: stage, boss: isBoss, gold: gold });
    this._progress('kills', 1);
    this._rollGearDrop(isBoss);

    if (isBoss) {
      this._emit({ type: 'bossCleared', stage: stage });
      this._firstClear(stage);
      this._advanceStage(true);
    } else if (st.boss === 'walled') {
      // farming a walled stage: no kill credit toward advance
    } else {
      st.killsInStage += 1;
      if (st.killsInStage === cbc.KILLS_PER_STAGE - 1 && stage % cbc.BOSS_EVERY === 0) st.boss = 'pending';
      else if (st.killsInStage >= cbc.KILLS_PER_STAGE) this._advanceStage(false);
    }
    s.combat.phase = 'spawning';
    s.combat.spawnTimer = cbc.SPAWN_DELAY_TICKS;
  };

  P._advanceStage = function (viaBoss) {
    const s = this.state, st = s.stage, from = st.stage, cls = this.c.class;
    if (from >= this.c.combat.STAGE_CAP) { st.boss = 'none'; this._emit({ type: 'stageCap' }); return; }
    st.stage = from + 1; st.killsInStage = 0; st.boss = 'none'; st.bossFailsOnStage = 0;
    this._emit({ type: 'stageAdvanced', from: from, to: st.stage, viaBoss: viaBoss });
    while (s.classAdvances < cls.STAGES.length && st.stage >= cls.STAGES[s.classAdvances]) {
      const before = this.dps();
      s.classAdvances += 1; this._dirty = true;
      this._emit({ type: 'classAdvanced', advances: s.classAdvances, stage: st.stage, dpsBefore: before, dpsAfter: this.dps() });
    }
  };

  P._bossFail = function () {
    const s = this.state, st = s.stage;
    st.boss = 'walled'; st.bossFailsOnStage += 1; s.life.bossFails += 1;
    if (!s.life.firstWallStage) s.life.firstWallStage = st.stage;
    this._emit({ type: 'bossFailed', stage: st.stage, attempt: st.bossFailsOnStage });
    s.combat.phase = 'spawning';
    s.combat.spawnTimer = this.c.combat.SPAWN_DELAY_TICKS;
  };

  P._firstClear = function (stage) {
    const p = this.state.prog;
    if (stage > p.highestFirstClearBoss) {
      p.highestFirstClearBoss = stage;
      this.state.cur.gems += this.c.economy.FIRST_CLEAR_GEMS;
      this._emit({ type: 'firstClear', stage: stage, gems: this.c.economy.FIRST_CLEAR_GEMS });
    }
  };

  // ---------- gear (specs/03 section 8.2)
  P._rollGearDrop = function (isBoss) {
    const s = this.state, g = this.c.gear, rs = s.rng.streams;
    const p = isBoss ? g.BOSS_DROP_P : g.DROP_P;
    if (this._draw(rs.gear_drop) >= p) return;
    const u1 = this._draw(rs.gear_rarity), u2 = this._draw(rs.gear_rarity);
    const rarity = isBoss ? sampleRange(g.RARITY_W, g.BOSS_MIN_RARITY, g.RARITY_W.length - 1, u1) : sample(g.RARITY_W, u1);
    const slot = Math.floor(u2 * g.SLOTS.length);
    const piece = { tier: this.gearTier(s.stage.stage), rarity: rarity, enh: 0 };
    s.life.gearDrops += 1;
    const current = s.gear[slot];
    const equipped = current === null || this.gearBasePct(piece) > this.gearBasePct(current);
    if (equipped) { s.gear[slot] = piece; this._dirty = true; }
    this._emit({ type: 'gearDropped', slot: slot, piece: piece, equipped: equipped, replaced: equipped ? current : null, current: equipped ? null : current });
  };

  // ---------- daily, quests, chest (specs/04 section 7)
  P._ensureDay = function (nowMs) {
    const d = this.state.daily;
    const key = Math.floor(nowMs / (this.c.economy.DAY_SECONDS * 1000));
    if (key <= d.dayKey) return;
    d.dayKey = key;
    d.progress = this.c.quests.map(function () { return 0; });
    d.rewarded = this.c.quests.map(function () { return false; });
    this.state.cur.freePullsDaily = this.c.economy.FREE_PULLS_PER_DAY;
    this._emit({ type: 'dayRolledOver', dayKey: key });
    this._progress('login', 1);
  };

  P._progress = function (kind, delta) {
    const d = this.state.daily, qs = this.c.quests;
    for (let i = 0; i < qs.length; i++) {
      if (qs[i].kind !== kind) continue;
      d.progress[i] += delta;
      if (!d.rewarded[i] && d.progress[i] >= qs[i].target) {
        d.rewarded[i] = true;
        this.state.cur.gems += qs[i].gems;
        this._emit({ type: 'questCompleted', id: qs[i].id, label: qs[i].label, gems: qs[i].gems });
      }
    }
  };

  P.claimChest = function () {
    const now = this.nowMs();
    this._ensureDay(now);
    if (now < this.chestReadyAtMs()) return { ok: false, reason: 'notReady' };
    this.state.cur.gems += this.c.economy.IDLE_CHEST_GEMS;
    this.state.prog.chestTimerStartMs = now;
    this._emit({ type: 'chestClaimed', gems: this.c.economy.IDLE_CHEST_GEMS });
    return { ok: true };
  };

  // ---------- wall clock and offline catch-up (specs/03 section 11)
  P.syncWallClock = function (nowMs) {
    const s = this.state, ec = this.c.economy;
    const simulatedMs = (s.tick - s.clock.tickAtLastSync) * (1000 / this.c.combat.TICK_HZ);
    const gapMs = nowMs - (s.clock.lastSyncUtcMs + simulatedMs);
    if (gapMs >= ec.OFFLINE_MIN_GAP_S * 1000) this._catchUp(gapMs);
    else if (gapMs < -1000) this._emit({ type: 'clockWentBackwards', byMs: -gapMs });
    s.clock.lastSyncUtcMs = nowMs;
    s.clock.tickAtLastSync = s.tick;
    this._ensureDay(nowMs);
  };

  P._catchUp = function (gapMs) {
    const s = this.state, ec = this.c.economy;
    const creditedS = Math.min(gapMs / 1000, ec.OFFLINE_CAP_H * 3600);
    const killT = this.killTimeSeconds(s.stage.stage);
    const gold = creditedS * ec.OFFLINE_RATE * this.gold(s.stage.stage) / killT;
    s.cur.gold += gold; s.life.offlineGold += gold;
    this._emit({ type: 'offlineGold', elapsedMs: gapMs, creditedS: creditedS, gold: gold });
  };

  // ---------- commands
  P.buyStatLevels = function (m) {
    if (!(m > 0)) return { ok: false, reason: 'invalid' };
    const s = this.state, cost = this.bulkCost(s.statLevel, m);
    if (s.cur.gold < cost) return { ok: false, reason: 'notEnoughGold' };
    s.cur.gold -= cost;
    const from = s.statLevel;
    s.statLevel += m; this._dirty = true;
    this._emit({ type: 'statLeveled', from: from, to: s.statLevel, cost: cost });
    return { ok: true, cost: cost };
  };
  P.buyStatLevelsMax = function () {
    const m = this.maxAffordable();
    if (m > 0) this.buyStatLevels(m);
    return m;
  };

  P.enhance = function (slot) {
    const s = this.state, g = this.c.gear, piece = s.gear[slot];
    if (!piece) return { ok: false, reason: 'emptySlot' };
    if (piece.enh >= g.ENH_MAX) return { ok: false, reason: 'maxed' };
    const cost = this.enhanceCost();
    if (s.cur.gold < cost) return { ok: false, reason: 'notEnoughGold' };
    s.cur.gold -= cost; s.life.enhAttempts += 1;
    const success = this._draw(s.rng.streams.enhance) < this.enhanceSuccess(piece.enh);
    if (success) { piece.enh += 1; s.life.enhSuccesses += 1; this._dirty = true; }
    this._emit({ type: 'enhanced', slot: slot, success: success, level: piece.enh, cost: cost });
    return { ok: true, success: success, level: piece.enh, cost: cost };
  };

  P.attemptBoss = function () {
    const s = this.state, st = s.stage;
    if (st.boss === 'pending') return { ok: false, reason: 'bossAlreadyPending' };
    if (!this.isBossStage() || st.boss !== 'walled') return { ok: false, reason: 'noBossToAttempt' };
    st.boss = 'pending';
    s.combat.phase = 'spawning';
    s.combat.spawnTimer = this.c.combat.SPAWN_DELAY_TICKS;
    return { ok: true };
  };

  // specs/04 section 7.5: who pays for a pull. No mixed payments.
  P.pullPrice = function (count) {
    const cur = this.state.cur, cost = this.c.economy.PULL_COST_GEMS;
    if (count === 1) {
      if (cur.freePullsDaily > 0) return { with: 'freeDaily' };
      if (cur.freePullsBanked > 0) return { with: 'freeBanked' };
      if (cur.gems >= cost) return { with: 'gems', gems: cost };
      return null;
    }
    if (cur.freePullsBanked >= 10) return { with: 'freeBanked' };
    if (cur.gems >= 10 * cost) return { with: 'gems', gems: 10 * cost };
    return null;
  };

  P.pull = function (count) {
    count = count === 10 ? 10 : 1;
    this._ensureDay(this.nowMs());
    const price = this.pullPrice(count);
    if (!price) return { ok: false, reason: 'cannotAfford' };
    const cur = this.state.cur;
    if (price.with === 'freeDaily') cur.freePullsDaily -= 1;
    else if (price.with === 'freeBanked') cur.freePullsBanked -= count;
    else cur.gems -= price.gems;
    const results = [];
    for (let i = 0; i < count; i++) {
      let force = false;
      if (count === 10 && i === 9) force = !results.some(function (r) { return r.tier >= 2; });
      results.push(this._pullOne(force));
    }
    this._progress('pulls', count);
    this._emit({ type: 'pulled', results: results, paidWith: price.with });
    return { ok: true, results: results, paidWith: price.with };
  };

  // specs/01 section 8. Exactly three draws per pull.
  P._pullOne = function (forceEplus) {
    const s = this.state, b = this.c.banner, rs = s.rng.streams.gacha_hero;
    s.banner.pity += 1; s.banner.totalPulls += 1; s.life.pulls += 1;
    const u1 = this._draw(rs), u2 = this._draw(rs), u3 = this._draw(rs);
    const pityAt = s.banner.pity;
    let tier;
    const isLplus = u1 < pityRate(b, s.banner.pity);
    if (isLplus) {
      tier = u2 < b.M_SHARE_OF_LPLUS ? 4 : 3;
      s.banner.pity = 0; s.life.lplus += 1;
    } else if (forceEplus) {
      tier = 2;
    } else {
      tier = sampleRange(b.BASE_RATES, 0, 2, u2);
    }
    const pool = this.c.heroes.pool[tier];
    const hero = pool[Math.floor(u3 * pool.length)];
    const owned = s.heroes.owned;
    const before = owned[hero.id] ? heroLevel(this.c, owned[hero.id]) : 0;
    owned[hero.id] = (owned[hero.id] || 0) + 1;
    const after = heroLevel(this.c, owned[hero.id]);
    let shards = 0;
    const cum = this.c.heroes.LVL_CUM;
    if (owned[hero.id] > cum[cum.length - 1]) { shards = b.SHARD_VALUE[tier]; s.cur.shards += shards; }
    if (after !== before && s.heroes.party.indexOf(hero.id) >= 0) this._dirty = true;
    return { id: hero.id, name: hero.name, tier: tier, isNew: owned[hero.id] === 1, levelBefore: before,
             levelAfter: after, shards: shards, pity: pityAt, forced: forceEplus && !isLplus };
  };

  P.equipHero = function (slot, id) {
    const h = this.state.heroes;
    if (!h.owned[id]) return { ok: false, reason: 'heroNotOwned' };
    const at = h.party.indexOf(id);
    if (at >= 0 && at !== slot) return { ok: false, reason: 'heroAlreadyEquipped' };
    h.party[slot] = id; this._dirty = true;
    return { ok: true };
  };
  P.unequipHero = function (slot) { this.state.heroes.party[slot] = null; this._dirty = true; return { ok: true }; };

  P.bestParty = function () {
    const self = this;
    const ids = Object.keys(this.state.heroes.owned).filter(function (id) { return self.state.heroes.owned[id] > 0; });
    ids.sort(function (a, b) { return self.heroPct(b) - self.heroPct(a) || (a < b ? -1 : a > b ? 1 : 0); });
    return ids.slice(0, this.c.heroes.SLOTS);
  };
  P.autoEquipWouldChange = function () {
    const party = this.state.heroes.party;
    const best = this.bestParty();
    let cur = 0, bestSum = 0;
    for (const id of party) if (id) cur += this.heroPct(id);
    for (const id of best) bestSum += this.heroPct(id);
    return bestSum > cur + 1e-9;
  };
  // Keeps heroes that stay in the best six in their slot, fills the rest. Returns slots changed.
  P.autoEquip = function () {
    const party = this.state.heroes.party, best = this.bestParty();
    const keep = new Set(best);
    const next = party.map(function (id) { return id && keep.has(id) ? id : null; });
    const placed = new Set(next.filter(Boolean));
    let bi = 0;
    for (let i = 0; i < next.length; i++) {
      if (next[i]) continue;
      while (bi < best.length && placed.has(best[bi])) bi++;
      if (bi < best.length) { next[i] = best[bi]; placed.add(best[bi]); bi++; }
    }
    let changed = 0;
    for (let i = 0; i < next.length; i++) if (next[i] !== party[i]) changed++;
    this.state.heroes.party = next; this._dirty = true;
    return changed;
  };

  // ---------- debug (always compiled; the UI shows it behind the cog)
  P.debugGrantGold = function (n) { this.state.cur.gold += n; };
  P.debugGrantGems = function (n) { this.state.cur.gems += n; };
  P.debugSetStage = function (stage) {
    const s = this.state, cls = this.c.class;
    s.stage = { stage: Math.max(1, Math.min(stage, this.c.combat.STAGE_CAP)), killsInStage: 0, boss: 'none', bossFailsOnStage: 0 };
    s.combat.phase = 'spawning'; s.combat.spawnTimer = this.c.combat.SPAWN_DELAY_TICKS;
    while (s.classAdvances < cls.STAGES.length && s.stage.stage >= cls.STAGES[s.classAdvances]) s.classAdvances++;
    this._dirty = true;
  };

  // specs/03 section 5.5: stream counters are a function of lifetime counters.
  P.validate = function () {
    const s = this.state, rs = s.rng.streams, errs = [];
    if (rs.gear_drop.counter !== s.life.kills) errs.push('gear_drop counter != kills');
    if (rs.gear_rarity.counter !== 2 * s.life.gearDrops) errs.push('gear_rarity counter != 2 * drops');
    if (rs.enhance.counter !== s.life.enhAttempts) errs.push('enhance counter != attempts');
    if (rs.gacha_hero.counter !== 3 * s.banner.totalPulls) errs.push('gacha counter != 3 * pulls');
    if (rs.combat.counter !== 0) errs.push('combat counter != 0');
    if (!(s.cur.gold >= 0) || !(s.cur.gems >= 0)) errs.push('negative currency');
    return errs;
  };

  // ---------------------------------------------------------------- save (specs/06, JSON for the prototype)
  function serialize(state) {
    return JSON.stringify(state, function (k, v) { return typeof v === 'bigint' ? { $u64: v.toString(16) } : v; });
  }
  // Forward-only migrations keyed by the version they upgrade from. Empty until schema 2 exists.
  const MIGRATIONS = {};
  function deserialize(content, json) {
    let st = JSON.parse(json, function (k, v) {
      return v && typeof v === 'object' && typeof v.$u64 === 'string' ? BigInt('0x' + v.$u64) : v;
    });
    if (!st || typeof st.schema !== 'number') throw new Error('Not an Idle RNG save');
    if (st.schema > SCHEMA_VERSION) throw new Error('Save is from a newer version (schema ' + st.schema + ')');
    while (st.schema < SCHEMA_VERSION) {
      const step = MIGRATIONS[st.schema];
      if (!step) throw new Error('No migration from schema ' + st.schema);
      st = step(st); st.schema += 1;
    }
    // Repair: fill anything a hand-edited save is missing from a fresh profile's defaults.
    const fresh = newState(content, { profileSeed: st.rng && st.rng.profileSeed || 0n, nowMs: st.createdUtcMs || 0 });
    (function fill(dst, src) {
      for (const k in src) {
        if (!(k in dst) || dst[k] === undefined) dst[k] = src[k];
        else if (src[k] && typeof src[k] === 'object' && !Array.isArray(src[k]) && typeof src[k] !== 'bigint') fill(dst[k], src[k]);
      }
    })(st, fresh);
    return new Sim(content, st);
  }

  const api = {
    Sim: Sim, serialize: serialize, deserialize: deserialize, SCHEMA_VERSION: SCHEMA_VERSION,
    rng: { mix64: mix64, derive: derive, toUnit: toUnit, fnv1a64: fnv1a64, streamSeed: streamSeed, STREAMS: STREAMS },
    pityRate: pityRate, consolidatedRates: consolidatedRates, heroLevel: heroLevel, formatShort: formatShort,
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.IdleSim = api;
})(typeof self !== 'undefined' ? self : this);
