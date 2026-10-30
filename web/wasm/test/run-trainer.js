#!/usr/bin/env node
// End-to-end browser check of the monster-trainer page (web/run.html) on the C# (wasm) engine at 390x844: picks a species,
// plays a whole run through the real buttons (drill, rest, rank fights, tree drafts, buying nodes), seals it, checks the grade
// screen and Vault list, reloads mid-run and after the end to prove the replay save restores the same run.
// Fails (exit 1) on any page error, console error (Google Fonts aborts excluded) or failed assertion.
// Usage: node web/wasm/test/run-trainer.js [--shots dir]
'use strict';
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');
const { start } = require('./serve.js');

const WEB = path.resolve(__dirname, '..', '..');
const shotsDir = process.argv.includes('--shots') ? process.argv[process.argv.indexOf('--shots') + 1] : null;
const fails = [], errors = [];
const check = (ok, what) => { console.log((ok ? 'ok   ' : 'FAIL ') + what); if (!ok) fails.push(what); };

(async () => {
  if (!fs.existsSync(path.join(WEB, 'wasm', 'dist', '_framework', 'dotnet.js'))) { console.error('web/wasm/dist is missing: run web/wasm/build.sh first'); process.exit(2); }
  const srv = await start(WEB, 0);
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  await ctx.route(/fonts\.(googleapis|gstatic)\.com/, (r) => r.abort());
  const page = await ctx.newPage();
  page.on('pageerror', (e) => errors.push('pageerror: ' + e.message));
  page.on('console', (m) => {
    if (m.type() === 'error' && !/fonts\.g|ERR_FAILED|Failed to load resource/.test(m.text() + (m.location().url || ''))) errors.push('console.error: ' + m.text());
  });
  const shot = async (n) => { if (shotsDir) { fs.mkdirSync(shotsDir, { recursive: true }); await page.screenshot({ path: path.join(shotsDir, n + '.png') }); } };
  const act = (a) => page.locator('[data-act="' + a + '"]');
  const text = (sel) => page.locator(sel).first().innerText();
  const url = srv.url + 'run.html';

  // ---- home screen
  await page.goto(url);
  await act('start').waitFor({ timeout: 90000 });
  check((await page.locator('[data-act^="species-"]').count()) >= 3, 'home lists the species from the C# content');
  check(await act('start').isEnabled(), 'a species is pre-picked');
  await shot('01-home');

  // ---- start a run, first drill and rest
  await act('species-drake').click();
  await act('start').click();
  await act('drill-str').waitFor();
  check(/Cycle 1\/12/.test(await text('[data-id=cycle]')), 'HUD shows cycle 1/12');
  check(/Rank E/.test(await text('[data-id=rank]')), 'starts at rank E');
  check((await page.locator('[data-act^="drill-"]').count()) === 8, 'eight stats can be drilled');
  const odds0 = await text('[data-id=odds]');
  check(/fail \d+%/.test(odds0) && /injury \d+%/.test(odds0), 'drill odds shown: ' + odds0.slice(0, 60));
  await act('drill-str').click();
  check(/Drill|Fail|Success|Super/.test(await text('[data-id=result]')), 'a drill result appears');
  await act('rest').click();
  check(/Rest/.test(await text('[data-id=result]')), 'a rest result appears');
  await shot('02-train');

  // ---- mid-run reload restores the same run (replay save)
  const before = await page.evaluate(() => ({ cycle: document.querySelector('[data-id=cycle]').textContent, bars: [...document.querySelectorAll('.stat .bar span')].map((e) => e.textContent).join('|') }));
  const saved = await page.evaluate(() => JSON.parse(localStorage.getItem('idle-rng-run-v1')));
  check(saved && saved.species === 'drake' && saved.log.length === 2 && saved.log[0].k === 'Drill', 'save is species + seed + command log (' + (saved && saved.log.length) + ' commands)');
  await page.reload();
  await act('drill-str').waitFor({ timeout: 90000 });
  const after = await page.evaluate(() => ({ cycle: document.querySelector('[data-id=cycle]').textContent, bars: [...document.querySelectorAll('.stat .bar span')].map((e) => e.textContent).join('|') }));
  check(JSON.stringify(before) === JSON.stringify(after), 'reload restores cycle and stats exactly');

  // ---- trees tab
  await act('tab-trees').click();
  check((await page.locator('[data-id^="slot-"]').count()) === 2, 'two native trees slotted at the start');
  check(/SP/.test(await text('[data-id=sp2]')), 'skill points shown');
  await shot('03-trees');
  await act('tab-train').click();

  // ---- play the whole run through the buttons
  const played = await page.evaluate(() => {
    const q = (s) => document.querySelector(s);
    const click = (s) => { const e = q(s); if (!e || e.disabled) return false; e.click(); return true; };
    const stats = { fights: 0, drafts: 0, bought: 0, takes: 0, steps: 0 };
    for (let i = 0; i < 1500 && !q('[data-act="seal"]'); i++) {
      stats.steps++;
      if (click('[data-act="fight-continue"]')) continue;
      if (q('[data-id=offer-title]')) {
        stats.drafts++;
        if (click('[data-act^="take-0-2"]') || click('[data-act^="take-0-3"]') || click('[data-act^="take-0-1"]')) stats.takes++; else click('[data-act="discard"]');
        continue;
      }
      if (click('[data-act="fight"]')) { stats.fights++; continue; }
      click('[data-act="tab-trees"]');
      while (click('[data-act^="buy-"]')) stats.bought++;
      click('[data-act="tab-train"]');
      const m = /Energy (\d+)/.exec((q('.bar.energy span') || {}).textContent || '');
      const energy = m ? +m[1] : 100;
      if (energy < 45) click('[data-act="rest"]');
      else click('[data-act="drill-' + ['str', 'hp', 'def', 'spd', 'int', 'wis', 'dex', 'luk'][stats.steps % 8] + '"]');
    }
    return stats;
  });
  console.log('played: ' + JSON.stringify(played));
  check(played.fights >= 6, 'rank fights happened (' + played.fights + ')');
  check(played.bought >= 3, 'tree nodes were bought (' + played.bought + ')');
  if (await act('fight-continue').count()) await act('fight-continue').click();   // the last fight's result popup
  check(await act('seal').isVisible(), 'the run reaches its end and offers to seal');
  await shot('04-end');

  // ---- seal -> grade screen -> vault
  await act('seal').click();
  const grade = await text('[data-id=grade]');
  check(/^[A-Z]$/.test(grade), 'grade screen shows the rank reached: ' + grade);
  const vault = await page.evaluate(() => JSON.parse(localStorage.getItem('idle-rng-vault-v1')));
  check(vault && vault.length === 1 && vault[0].species === 'drake' && vault[0].log.length > 20, 'a Vault entry was stored with the run log');
  check(vault && vault[0].grade === grade.toLowerCase() && /^[0-9a-f]{64}$/.test(vault[0].hash), 'entry grade and run hash match');
  await shot('05-grade');

  await page.reload();
  await page.locator('[data-id=grade]').waitFor({ timeout: 90000 });
  check((await text('[data-id=grade]')) === grade, 'a reload after sealing shows the same grade');
  const vaultAfter = await page.evaluate(() => JSON.parse(localStorage.getItem('idle-rng-vault-v1')).length);
  check(vaultAfter === 1, 'reloading does not seal twice');

  await act('new-run').click();
  await act('start').waitFor();
  check((await page.locator('[data-id=vault-entry]').count()) === 1, 'the home screen lists the vaulted monster');
  await shot('06-vault');

  // ---- the book option and drafts
  await act('book-toggle').check();
  await act('start').click();
  await act('tab-trees').click();
  check((await page.locator('[data-act^="book-"]:not([data-act="book-toggle"])').count()) === 1, 'a test skill book can be used');
  await page.locator('[data-act^="book-"]:not([data-act="book-toggle"])').first().click();
  check(/Skill book/.test(await text('[data-id=offer-title]')), 'using the book opens a draft');
  await shot('07-draft');
  await act('take-0-2').click();                                   // into the first empty slot
  check((await page.locator('[data-id=offer-title]').count()) === 0, 'taking the tree closes the draft');
  check((await page.locator('[data-id^="slot-"]').count()) === 3, 'the book tree now fills slot 3');
  check((await page.locator('[data-act^="book-"]:not([data-act="book-toggle"])').count()) === 0, 'the used book is gone');

  // ---- the idle page still loads and links here
  await page.goto(srv.url + 'index.html');
  await page.locator('#trainerLink').waitFor();
  check(true, 'index.html has the Trainer link');

  check(errors.length === 0, 'no page/console errors' + (errors.length ? ': ' + errors.join(' | ') : ''));
  await browser.close();
  await srv.close();
  if (fails.length) { console.log('\n' + fails.length + ' FAILED'); process.exit(1); }
  console.log('\nall passed');
})().catch((e) => { console.error(e); process.exit(1); });
