// Shared helpers for the headless-browser checks of the monster-trainer page (web/wasm/test/*.js). Each check opens its own
// server, browser and phone-sized page through open(), records assertions with check() and ends with finish().
'use strict';
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const { chromium } = require('playwright');
const { start } = require('./serve.js');

const WEB = path.resolve(__dirname, '..', '..');
const PHONE = { width: 390, height: 844 };
const SAVE_KEY = 'idle-rng-account-v1';            // web/run-engine.js SAVE_KEY
const BAD_KEY = 'idle-rng-account-v1-unloadable';  // web/run-engine.js BAD_KEY
const STATS = ['str', 'hp', 'def', 'spd', 'int', 'wis', 'dex', 'luk'];

// AccountSave text (src/IdleRng.Runtime/Save/SaveSerializer.cs): a header line with the body's CRC-32 and length, then the JSON.
function encodeSave(obj) {
  const body = Buffer.from(JSON.stringify(obj), 'utf8');
  return 'IDLERNG-SAVE fmt=json crc32=' + (zlib.crc32(body) >>> 0).toString(16).padStart(8, '0') + ' len=' + body.length + '\n' + body.toString('utf8');
}
const decodeSave = (text) => (text ? JSON.parse(text.slice(text.indexOf('\n') + 1)) : null);

async function open(opts) {
  opts = opts || {};
  if (!fs.existsSync(path.join(WEB, 'wasm', 'dist', '_framework', 'dotnet.js'))) { console.error('web/wasm/dist is missing: run web/wasm/build.sh first'); process.exit(2); }
  const fails = [], errors = [];
  const check = (ok, what) => { console.log((ok ? 'ok   ' : 'FAIL ') + what); if (!ok) fails.push(what); return ok; };
  const srv = await start(WEB, 0);
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: PHONE, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  await ctx.route(/fonts\.(googleapis|gstatic)\.com/, (r) => r.abort());
  const page = await ctx.newPage();
  page.on('pageerror', (e) => errors.push('pageerror: ' + e.message));
  page.on('console', (m) => {
    if (m.type() === 'error' && !/fonts\.g|ERR_FAILED|Failed to load resource/.test(m.text() + (m.location().url || ''))) errors.push('console.error: ' + m.text());
  });

  const h = {
    page, check, errors, url: srv.url + 'index.html',
    act: (a) => page.locator('[data-act="' + a + '"]'),
    text: (sel) => page.locator(sel).first().innerText(),
    // Screenshots are debug output only (never compared): written when the check runs with --shots <dir>.
    shot: async (n) => {
      if (!opts.shotsDir) return;
      fs.mkdirSync(opts.shotsDir, { recursive: true });
      await page.screenshot({ path: path.join(opts.shotsDir, n + '.png') });
    },
    readSaveText: () => page.evaluate((k) => localStorage.getItem(k), SAVE_KEY),
    readBadText: () => page.evaluate((k) => localStorage.getItem(k), BAD_KEY),
    readAccount: async () => decodeSave(await h.readSaveText()),
    writeSaveText: (t) => page.evaluate(([k, v]) => localStorage.setItem(k, v), [SAVE_KEY, t]),
    // Reload and wait for the engine: the run screen (a drill button) or the home screen (the start button).
    reload: async () => { await page.reload(); await page.locator('[data-act="start"], [data-act^="drill-"]').first().waitFor({ timeout: 90000 }); },
    // The phone-width layout check (issue #87): neither the page nor its scrolling panes (#main, an open #modal: overflow-y:auto
    // makes them scroll sideways too) are wider than the screen, and nothing has logged an error so far.
    layout: async (screen) => {
      const wide = await page.evaluate((vw) => {
        const out = [];
        const doc = Math.max(document.documentElement.scrollWidth, document.body.scrollWidth);
        if (doc > vw) out.push('page ' + doc + 'px');
        ['main', 'modal'].forEach((id) => {
          const e = document.getElementById(id);
          if (e && !e.hidden && e.scrollWidth > e.clientWidth) out.push('#' + id + ' ' + e.scrollWidth + '/' + e.clientWidth + 'px');
        });
        return out;
      }, PHONE.width);
      check(wide.length === 0, screen + ': no horizontal scroll at ' + PHONE.width + 'px' + (wide.length ? ' (' + wide.join(', ') + ')' : ''));
      check(errors.length === 0, screen + ': no page/console errors' + (errors.length ? ': ' + errors.join(' | ') : ''));
      await h.shot(screen.replace(/\W+/g, '-'));
    },
    // Taps through the run (drill, rest, fights skipped, drafts taken into an empty slot or discarded, nodes bought) until
    // stop: 'fight' when a rank fight is due, 'end' at the seal button. Returns counts of what it did.
    playUntil: (stop) => page.evaluate(([stop, stats]) => {
      const q = (s) => document.querySelector(s);
      const click = (s) => { const e = q(s); if (!e || e.disabled) return false; e.click(); return true; };
      const st = { fights: 0, drafts: 0, bought: 0, steps: 0 };
      for (let i = 0; i < 1500 && !q('[data-act="seal"]'); i++) {
        st.steps++;
        if (q('[data-id=fight-play]')) { click('[data-act="speed-skip"]'); continue; }
        if (click('[data-act="fight-continue"]')) continue;
        if (q('[data-id=offer-title]')) {
          st.drafts++;
          if (!(click('[data-act^="take-0-3"]') || click('[data-act^="take-0-2"]'))) click('[data-act="discard"]');
          continue;
        }
        const f = q('[data-act="fight"]');
        if (f && !f.disabled && stop === 'fight') return st;
        if (click('[data-act="fight"]')) { st.fights++; continue; }
        click('[data-act="tab-trees"]');
        while (click('[data-act^="buy-"]')) st.bought++;
        click('[data-act="tab-train"]');
        const m = /Energy (\d+)/.exec((q('.bar.energy span') || {}).textContent || '');
        if ((m ? +m[1] : 100) < 45) click('[data-act="rest"]');
        else click('[data-act="drill-' + stats[st.steps % stats.length] + '"]');
      }
      return st;
    }, [stop, STATS]),
    finish: async () => {
      check(errors.length === 0, 'no page/console errors' + (errors.length ? ': ' + errors.join(' | ') : ''));
      await browser.close();
      await srv.close();
      if (fails.length) { console.log('\n' + fails.length + ' FAILED'); process.exit(1); }
      console.log('\nall passed');
    },
  };
  return h;
}

const shotsArg = () => (process.argv.includes('--shots') ? process.argv[process.argv.indexOf('--shots') + 1] : null);

module.exports = { open, encodeSave, decodeSave, shotsArg, SAVE_KEY, BAD_KEY, PHONE };
