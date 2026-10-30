#!/usr/bin/env node
// Replays Fixtures/Parity/*.json (recorded from web/sim.js) against the real C# engine running in headless Chromium and writes
// web/wasm/test/parity-report.md. Not a pass/fail gate: C# is the source of truth and differs from sim.js on purpose.
// Three comparisons per fixture:
//   raw      C# vs the recorded JS result, exact
//   aligned  C# vs sim.js re-run in-process with the known C# tuning applied (aligned-js.js), exact
//   loose    aligned, ignoring fields that cannot match by construction (hero display names, profile id guid, ...)
// Usage: node web/wasm/test/parity-report.js [--direct] [fixtureName ...]
//   --direct   bypass web/engine.js and drive the IdleRng.Web.Engine exports through dotnet.create() (default: the engine.js facade)
'use strict';
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');
const { start } = require('./serve.js');
const aligned = require('./aligned-js.js');

const ROOT = path.resolve(__dirname, '..', '..', '..');
const WEB = path.join(ROOT, 'web');
const FIX = path.join(ROOT, 'Fixtures', 'Parity');
const args = process.argv.slice(2);
const DIRECT = args.includes('--direct');
const only = args.filter((a) => !a.startsWith('--'));

// ------------------------------------------------------------------ in-page runner
async function pageRunner([fx, direct]) {
  let eng, mode;
  const base = '/wasm/dist/';
  if (!direct && window.IdleEngine) {
    eng = await window.IdleEngine.create({ backend: 'wasm', content: window.IdleContent, wasmBase: base, nowMs: fx.nowMs,
      profileSeedHex: fx.seedHex, profileId: 'fixture' });
    if (eng.backend !== 'wasm') throw new Error('facade fell back to ' + eng.backend + ': ' + eng.fallbackReason);
    mode = 'facade';
    eng.emitHits = !!fx.emitHits;
    eng.__call = (kind, name, a) => eng[name].apply(eng, a);
    eng.__state = () => JSON.parse(JSON.stringify(eng.state));
    eng.__adv = (n) => eng.advance(n);
    eng.__drain = () => eng.drain();
  } else {
    if (!window.__dn) {
      const { dotnet } = await import(base + '_framework/dotnet.js');
      const rt = await dotnet.create();
      window.__dn = (await rt.getAssemblyExports('IdleRng.Web.dll')).IdleRng.Web.Engine;
      window.__pack = await (await fetch(base + 'content-pack.json')).text();
    }
    const H = window.__dn;
    H.Init(window.__pack, '', fx.seedHex, fx.nowMs, 'fixture');
    H.SetEmitHits(fx.emitHits ? 1 : 0);
    mode = 'direct';
    eng = {
      __call: (kind, name, a) => { let n = a.length; while (n > 0 && a[n - 1] === undefined) n--; const j = JSON.stringify(a.slice(0, n));
        const r = JSON.parse(kind === 'q' ? H.Query(name, j) : H.Cmd(name, j)); return r; },
      __state: () => JSON.parse(H.State()), __adv: (n) => H.Advance(n), __drain: () => JSON.parse(H.Drain()),
    };
  }
  const out = [];
  const t0 = performance.now();
  const norm = (v) => (v === undefined ? null : v);
  const QUERIES = new Set(['hp', 'bossHp', 'gold', 'statCost', 'statMult', 'gearTier', 'gearBasePct', 'gearEffPct', 'gearSlotMult', 'gearMult',
    'heroLevel', 'heroPct', 'heroMult', 'classMult', 'dps', 'hitDamage', 'hitsToKill', 'killTimeSeconds', 'isBossStage', 'bossClearable',
    'needDps', 'enhanceCost', 'enhanceSuccess', 'pityRate', 'freePullsRemaining', 'chestReadyAtMs', 'chestReady', 'bulkCost',
    'maxAffordable', 'pullPrice', 'bestParty', 'autoEquipWouldChange', 'nowMs', 'validate', 'consolidatedRates']);
  for (let i = 0; i < fx.steps.length; i++) {
    const s = fx.steps[i];
    try {
      switch (s.op) {
        case 'advance': eng.__adv(s.n); break;
        case 'sync': eng.__call('c', 'syncWallClock', [s.nowMs]); break;
        case 'state': out.push({ i: i, v: eng.__state() }); break;
        case 'drain': { const ev = eng.__drain(); if (s.check !== false) out.push({ i: i, v: ev }); break; }
        case 'query':
          if (!QUERIES.has(s.name)) throw new Error('unknown query ' + s.name);
          out.push({ i: i, v: norm(eng.__call('q', s.name, s.args)) }); break;
        case 'cmd': out.push({ i: i, v: norm(eng.__call('c', s.name, s.args)) }); break;
        default: throw new Error('unknown op ' + s.op);
      }
    } catch (e) { out.push({ i: i, err: String(e && e.message || e) }); break; }
  }
  return { mode: mode, ms: performance.now() - t0, out: out, version: eng.version ? eng.version() : null };
}

// ------------------------------------------------------------------ diff
const IGNORE_KEYS = new Set(['name', 'profileId', 'contentVersion']);       // loose mode only
function diff(js, cs, p, loose) {
  p = p || '$';
  if (js === cs) return null;
  const tj = js === null ? 'null' : Array.isArray(js) ? 'array' : typeof js, tc = cs === null ? 'null' : Array.isArray(cs) ? 'array' : typeof cs;
  if (tj !== tc || tj !== 'object' && tj !== 'array') return { path: p, js: js, cs: cs };
  if (tj === 'array') {
    if (js.length !== cs.length) return { path: p + '.length', js: js.length, cs: cs.length };
    for (let i = 0; i < js.length; i++) { const d = diff(js[i], cs[i], p + '[' + i + ']', loose); if (d) return d; }
    return null;
  }
  const kj = Object.keys(js).sort(), kc = Object.keys(cs).sort();
  const fj = loose ? kj.filter((k) => !IGNORE_KEYS.has(k)) : kj, fc = loose ? kc.filter((k) => !IGNORE_KEYS.has(k)) : kc;
  for (const k of fj) if (!(k in cs)) return { path: p + '.' + k, js: js[k], cs: '<missing>' };
  for (const k of fc) if (!(k in js)) return { path: p + '.' + k, js: '<missing>', cs: cs[k] };
  for (const k of fj) { const d = diff(js[k], cs[k], p + '.' + k, loose); if (d) return d; }
  return null;
}
// Every differing leaf (loose rules), capped.
function diffAll(js, cs, p, out) {
  out = out || []; p = p || '$';
  if (out.length > 60 || js === cs) return out;
  const tj = js === null ? 'null' : Array.isArray(js) ? 'array' : typeof js, tc = cs === null ? 'null' : Array.isArray(cs) ? 'array' : typeof cs;
  if (tj !== tc || (tj !== 'object' && tj !== 'array')) { out.push({ path: p, js: js, cs: cs }); return out; }
  if (tj === 'array') {
    if (js.length !== cs.length) { out.push({ path: p + '.length', js: js.length, cs: cs.length }); return out; }
    for (let i = 0; i < js.length; i++) diffAll(js[i], cs[i], p + '[' + i + ']', out);
    return out;
  }
  const keys = new Set(Object.keys(js).concat(Object.keys(cs)));
  for (const k of keys) {
    if (IGNORE_KEYS.has(k)) continue;
    if (!(k in js)) out.push({ path: p + '.' + k, js: '<missing>', cs: cs[k] });
    else if (!(k in cs)) out.push({ path: p + '.' + k, js: js[k], cs: '<missing>' });
    else diffAll(js[k], cs[k], p + '.' + k, out);
  }
  return out;
}
// Known, understood divergences (see the notes section). Returns a label or null.
function explain(fx, step, stepIdx, d) {
  if (/\.combat\.enemyHp$/.test(d.path) && d.cs === 0) return 'enemyHp: C# zeroes it after a kill / debugSetStage, sim.js leaves the overkill (or the old enemy)';
  if (/(^|\.)label$/.test(d.path)) return 'quest label text differs (strings.json vs content.js)';
  if (step.op === 'query' && step.name === 'classMult' && step.args.length && step.args[0] > 4) return 'classMult(k > 4): C# clamps to the 4 defined advances, sim.js keeps multiplying';
  if (d.cs === 2147483647 || d.cs === 2147483647.5) return 'hitsToKill saturates at int.MaxValue (absurd argument)';
  if (fx.name === 'class-advance' && stepIdx >= 28) return 'debugSetStage below a class stage: C# recomputes classAdvances downward, sim.js never lowers it';
  return null;
}
const show = (v) => { const s = v === undefined ? 'undefined' : typeof v === 'string' ? JSON.stringify(v) : JSON.stringify(v); return s.length > 80 ? s.slice(0, 77) + '...' : s; };

function expectIndexMap(fx) {
  const m = [];
  fx.steps.forEach((s, i) => { if (s.op === 'state' || s.op === 'query' || s.op === 'cmd' || (s.op === 'drain' && s.check !== false)) m.push(i); });
  return m;
}
function describeStep(s) { return s.op + (s.name ? ' ' + s.name : '') + (s.args && s.args.length ? '(' + show(s.args).slice(0, 40) + ')' : s.n ? ' ' + s.n : ''); }

// First divergence of `got` against `exp` (arrays aligned to expectIndexMap). Returns {matches, total, first}.
function compare(fx, exp, got, loose) {
  const map = expectIndexMap(fx);
  let matches = 0, first = null;
  for (let k = 0; k < exp.length; k++) {
    const g = got[k];
    if (!g) { if (!first) first = { k: k, step: map[k], op: describeStep(fx.steps[map[k]]), path: '(not run)', js: '', cs: '' }; continue; }
    const d = diff(exp[k], g.v, '$', loose);
    if (!d) matches++;
    else if (!first) first = { k: k, step: map[k], op: describeStep(fx.steps[map[k]]), path: d.path, js: d.js, cs: d.cs };
  }
  return { matches: matches, total: exp.length, first: first };
}

// Per top-level category of the first difference of each non-matching entry (for the "what diverges" histogram).
function histogram(fx, exp, got, loose) {
  const map = expectIndexMap(fx), h = {};
  for (let k = 0; k < exp.length; k++) {
    const g = got[k]; if (!g) continue;
    const d = diff(exp[k], g.v, '$', loose);
    if (!d) continue;
    const st = fx.steps[map[k]];
    const key = st.op + (st.name ? ' ' + st.name : '') + ' ' + d.path.replace(/\[\d+\]/g, '[]').replace(/^\$\.?/, '').split('.').slice(0, 2).join('.');
    if (!h[key]) h[key] = { n: 0, ex: '#' + map[k] + ' ' + describeStep(st) + ' ' + d.path + ' JS ' + show(d.js) + ' vs C# ' + show(d.cs) };
    h[key].n++;
  }
  return h;
}

(async () => {
  const files = fs.readdirSync(FIX).filter((n) => n.endsWith('.json') && n !== 'format-vectors.json').sort()
    .filter((n) => !only.length || only.includes(n.replace('.json', '')));
  const srv = await start(WEB, 0);
  const browser = await chromium.launch();
  const page = await browser.newPage();
  const pageErrors = [];
  page.on('pageerror', (e) => pageErrors.push(e.message));
  page.on('console', (m) => { if (m.type() === 'error') pageErrors.push('console: ' + m.text()); });
  await page.goto(srv.url + 'wasm/test/blank.html');
  let haveFacade = false;
  if (!DIRECT) {
    try {
      await page.addScriptTag({ url: '/content.js' });
      await page.addScriptTag({ url: '/engine.js' });
      haveFacade = await page.evaluate(() => !!(window.IdleEngine && window.IdleContent));
    } catch (e) { haveFacade = false; }
  }
  const useDirect = DIRECT || !haveFacade;
  console.log('mode: ' + (useDirect ? 'direct (dotnet exports)' : 'engine.js facade'));

  aligned.patch();
  const alignedContent = JSON.parse(fs.readFileSync(path.join(ROOT, 'Content', 'content.json'), 'utf8'));
  aligned.alignContent(alignedContent);

  const rows = [], details = [];
  let sumExp = 0, sumRaw = 0, sumAl = 0, sumLoose = 0, version = null, mode = null;
  for (const f of files) {
    const fx = JSON.parse(fs.readFileSync(path.join(FIX, f), 'utf8'));
    const t0 = Date.now();
    const res = await page.evaluate(pageRunner, [fx, useDirect]).catch((e) => ({ fatal: String(e.message || e), out: [] }));
    if (res.fatal) { console.log(f + ': FATAL ' + res.fatal); rows.push({ name: fx.name, fatal: res.fatal }); continue; }
    version = res.version || version; mode = res.mode;
    const errStep = res.out.find((o) => o.err);
    const got = res.out.filter((o) => !o.err);
    // aligned JS expect
    const sim = aligned.lib.create(fx, alignedContent);
    const al = [];
    for (const st of fx.steps) { const r = aligned.lib.exec(sim, alignedContent, st); if (r.has) al.push(r.value); }
    const raw = compare(fx, fx.expect, got, false), alg = compare(fx, al, got, false), loose = compare(fx, al, got, true);
    sumExp += raw.total; sumRaw += raw.matches; sumAl += alg.matches; sumLoose += loose.matches;
    const map = expectIndexMap(fx), causes = {}, unexplained = [];
    let nUnex = 0;
    for (let k = 0; k < al.length; k++) {
      if (!got[k]) { nUnex++; continue; }
      const ds = diffAll(al[k], got[k].v);
      if (!ds.length) continue;
      let bad = null;
      for (const d of ds) { const why = explain(fx, fx.steps[map[k]], map[k], d); if (why) causes[why] = (causes[why] || 0) + 1; else if (!bad) bad = d; }
      if (bad) { nUnex++; if (unexplained.length < 5) unexplained.push('#' + map[k] + ' ' + describeStep(fx.steps[map[k]]) + ' ' + bad.path + ' JS ' + show(bad.js) + ' vs C# ' + show(bad.cs)); }
    }
    rows.push({ name: fx.name, steps: fx.steps.length, raw: raw, alg: alg, loose: loose, ms: Date.now() - t0, err: errStep && errStep.err,
      hist: histogram(fx, al, got, true), causes: causes, nUnex: nUnex, unexplained: unexplained });
    console.log(fx.name.padEnd(18) + ' raw ' + raw.matches + '/' + raw.total + '  aligned ' + alg.matches + '  loose ' + loose.matches + (errStep ? '  ERROR ' + errStep.err : '') + '  (' + (Date.now() - t0) + ' ms)');
  }
  await browser.close(); await srv.close();

  const fmtFirst = (c) => c.first ? '#' + c.first.step + ' `' + c.first.op + '` `' + c.first.path + '` JS `' + show(c.first.js) + '` vs C# `' + show(c.first.cs) + '`' : 'none';
  const md = [];
  md.push('# Parity report: C# engine (wasm) vs web/sim.js fixtures');
  md.push('');
  md.push('Generated by `node web/wasm/test/parity-report.js` on ' + new Date().toISOString().slice(0, 10) + ' (mode: ' + mode + '; ' + version + ').');
  md.push('This is a diff report, not a gate: the C# core is the source of truth and differs from sim.js on purpose (see below).');
  md.push('');
  md.push('- raw: C# result vs the recorded sim.js result, exact (numbers bit-equal, every field).');
  md.push('- aligned: C# vs sim.js re-run in-process with the known C# tuning patched in memory (`aligned-js.js`: BASE_ATK 20, intro boss rule HP x4 below stage 60, gear auto-sell, whole-second clock, auto-equip-best, curve evaluation order, BaseLplus = 0.025 + 0.005).');
  md.push('- loose: aligned, ignoring `name` (hero display names differ), `profileId` (C# uses a Guid) and `contentVersion`.');
  md.push('');
  md.push('| fixture | expect entries | raw exact | aligned exact | loose exact | first divergence, raw | first divergence, loose |');
  md.push('|---|---:|---:|---:|---:|---|---|');
  for (const r of rows) {
    if (r.fatal) { md.push('| ' + r.name + ' | | | | | FATAL ' + r.fatal.replace(/\|/g, '/') + ' | |'); continue; }
    md.push('| ' + r.name + ' | ' + r.raw.total + ' | ' + r.raw.matches + ' | ' + r.alg.matches + ' | ' + r.loose.matches + ' | ' + fmtFirst(r.raw).replace(/\|/g, '/') + ' | ' + fmtFirst(r.loose).replace(/\|/g, '/') + ' |');
  }
  md.push('');
  md.push('**Overall:** raw ' + sumRaw + '/' + sumExp + ' (' + (100 * sumRaw / sumExp).toFixed(1) + '%), aligned ' + sumAl + '/' + sumExp + ' (' + (100 * sumAl / sumExp).toFixed(1) + '%), loose ' + sumLoose + '/' + sumExp + ' (' + (100 * sumLoose / sumExp).toFixed(1) + '%).');
  md.push('');
  md.push('## Unexplained divergences (after the aligned reference and the known causes below)');
  md.push('');
  md.push('| fixture | expect entries with an unexplained difference | examples |');
  md.push('|---|---:|---|');
  let totalUnex = 0;
  for (const r of rows) { if (r.fatal) continue; totalUnex += r.nUnex; md.push('| ' + r.name + ' | ' + r.nUnex + ' | ' + (r.unexplained.join('<br>') || '-').replace(/\|/g, '/') + ' |'); }
  md.push('');
  md.push('Total unexplained entries: **' + totalUnex + '** of ' + sumExp + '.');
  md.push('');
  md.push('## Explained differences still present after alignment (leaf counts)');
  md.push('');
  const causeTotals = {};
  for (const r of rows) if (!r.fatal) for (const [k, n] of Object.entries(r.causes)) causeTotals[k] = (causeTotals[k] || 0) + n;
  for (const [k, n] of Object.entries(causeTotals).sort((a, b) => b[1] - a[1])) md.push('- ' + n + ' x ' + k);
  md.push('');
  md.push('## Remaining (loose) divergences by step kind and field');
  md.push('Count of expect entries whose first differing field is of that kind (top 12 per fixture). These are the unexplained-or-uninvestigated ones; see the notes below.');
  md.push('');
  for (const r of rows) {
    if (r.fatal) continue;
    const top = Object.entries(r.hist).sort((a, b) => b[1].n - a[1].n).slice(0, 12);
    md.push('### ' + r.name + (r.err ? ' (stopped by error: ' + r.err + ')' : ''));
    if (!top.length) { md.push('none'); md.push(''); continue; }
    for (const [k, v] of top) md.push('- ' + v.n + ' x `' + k + '`, e.g. ' + v.ex.replace(/\|/g, '/'));
    md.push('');
  }
  if (pageErrors.length) { md.push('## Page errors'); pageErrors.slice(0, 10).forEach((e) => md.push('- ' + e)); md.push(''); }
  const notes = path.join(__dirname, 'parity-notes.md');
  if (fs.existsSync(notes)) md.push(fs.readFileSync(notes, 'utf8'));
  fs.writeFileSync(path.join(__dirname, 'parity-report.md'), md.join('\n') + '\n');
  console.log('wrote web/wasm/test/parity-report.md');
})().catch((e) => { console.error(e); process.exit(1); });
