#!/usr/bin/env node
// Browser checks of the monster-trainer page beyond the main run (web/wasm/test/run-trainer.js), at phone width 390x844:
// - issue #87: each main screen (pick, run, fight playback, tree draft, end, gauntlet wheel, grade, vendor, Vault) has no horizontal scroll
//   and no page/console error; a tree draft overwrites an occupied slot (specs/12 section 5.1: its spent points are lost).
// - issue #88: save, reload and resume. A run reloaded mid-way (also with a draft waiting) comes back with the same state hash and
//   is then finished; a save whose run in progress no longer replays drops only that run, with a notice, its books returned and
//   the original kept aside (specs/06 section 5); a damaged save is kept aside next to it and the player gets a fresh, working
//   account. Issue #93: kept-aside saves are capped, the oldest (or a legacy single backup) is never evicted.
// - issue #89: an injury loses the rest of its cycle, its fight and the next cycle, says when the monster is back, and survives a reload.
// Fails (exit 1) on any page error, console error (Google Fonts aborts excluded) or failed assertion.
// Usage: node web/wasm/test/flows.js [--shots dir]   (screenshots are debug output, never compared)
'use strict';
const H = require('./harness.js');

(async () => {
  const t = await H.open({ shotsDir: H.shotsArg() });
  const { page, check, act, text } = t;
  const clickDom = (sel) => page.evaluate((s) => document.querySelector(s).click(), sel);   // works under an open modal

  // Everything a reload must bring back: the stored recipe, the HUD, the slotted trees and the engine's state hash (Log tab).
  const snapshot = async () => {
    const a = await t.readAccount();
    await clickDom('[data-act="tab-log"]');
    const s = await page.evaluate(() => {
      const q = (x) => (document.querySelector(x) || {}).textContent || '';
      return {
        cycle: q('[data-id=cycle]'), rank: q('[data-id=rank]'), sp: q('[data-id=sp]'), energy: q('.bar.energy span'),
        hash: (/state ([0-9a-f]{12})/.exec(document.querySelector('#main').textContent) || [])[1] || null,
        offer: q('[data-id=offer-title]'),
      };
    });
    await clickDom('[data-act="tab-trees"]');
    s.slots = await page.evaluate(() => [...document.querySelectorAll('.slotcard')].map((e) => e.querySelector('b').textContent + ' ' + ((e.querySelector('.num') || {}).textContent || '')).join(' | '));
    await clickDom('[data-act="tab-train"]');
    s.bars = await page.evaluate(() => [...document.querySelectorAll('.stat .bar span')].map((e) => e.textContent).join('|'));
    const r = a && a.ActiveRun;
    s.recipe = r ? { species: r.SpeciesId, seed: r.Seed, log: r.Log.length, hash: r.Hash } : null;
    return s;
  };
  const reloadSame = async (what) => {
    const before = await snapshot();
    await t.reload();
    const after = await snapshot();
    check(before.recipe && before.hash && before.recipe.hash.startsWith(before.hash), what + ': the saved run hash is the engine\'s state hash (' + before.hash + ', ' + (before.recipe && before.recipe.log) + ' commands)');
    check(JSON.stringify(before) === JSON.stringify(after), what + ': reload restores the same run (state hash, HUD, stats, trees, recipe)' +
      (JSON.stringify(before) === JSON.stringify(after) ? '' : '\n  before ' + JSON.stringify(before) + '\n  after  ' + JSON.stringify(after)));
    return after;
  };

  // ---- pick screen
  await page.goto(t.url + '?debug=1');               // ?debug shows the debug-only "Test: add a book" button (specs/21 section 7)
  await act('start').waitFor({ timeout: 90000 });
  await t.layout('pick screen');
  const species = page.locator('[data-act^="species-"]:not([disabled])').first();
  const speciesId = (await species.getAttribute('data-act')).slice('species-'.length);
  await species.click();
  await act('grant-book').click();                 // a test skill book, brought into the run for the overwrite and the stale save
  await act('book-toggle').check();

  // ---- run screen; a reload right after the start and after a few commands
  await act('start').click();
  await act('drill-str').waitFor();
  await t.layout('run screen');
  const r0 = (await t.readAccount()).ActiveRun;
  check(r0 && r0.SpeciesId === speciesId && r0.Books.length === 1, 'the run started with the ' + speciesId + ' and the book');
  await act('drill-str').click();
  await act('rest').click();
  await act('drill-hp').click();
  await reloadSame('mid-run reload');

  // ---- first rank fight: playback screen, Skip, result
  let played = await t.playUntil('fight');
  check(await act('fight').isEnabled(), 'a rank fight is due after ' + played.steps + ' steps');
  await act('fight').click();
  await page.locator('[data-id=fight-play]').waitFor();
  await page.waitForTimeout(600);
  await t.layout('fight playback');
  await act('speed-skip').click();
  await page.locator('[data-id=fight-title]').waitFor();
  await t.layout('fight result');
  await act('fight-continue').click();
  await reloadSame('reload after a rank fight');

  // ---- buy nodes in slot 1, then a book draft overwrites it: the tree changes and its spent points are lost
  const slot0Bought = async () => { await act('tab-trees').click(); return +(/^(\d+)\//.exec(await text('[data-id=slot-0] .num')) || [])[1]; };
  let bought0 = 0;
  for (let i = 0; i < 4 && (bought0 = await slot0Bought()) < 1; i++) {   // a few more cycles, buying every node it can afford
    await act('tab-train').click();
    played = await t.playUntil('fight');
    await act('fight').click();                                            // Skip is remembered: the result shows at once
    await act('fight-continue').click();
  }
  while ((await page.locator('[data-id=offer-title]').count())) await act('discard').click();
  await act('tab-trees').click();
  check(bought0 >= 1, 'slot 1 has ' + bought0 + ' node(s) bought before it is overwritten');
  const slot0Before = (await text('[data-id=slot-0] b')).replace(/^Slot 1: /, '');
  const spBefore = await text('[data-id=sp2]');
  const bookBtn = page.locator('[data-act^="book-"]:not([data-act="book-toggle"])').first();
  check((await bookBtn.count()) === 1, 'the brought book can be used');
  await bookBtn.click();
  await page.locator('[data-id=offer-title]').waitFor();
  await t.layout('tree draft');
  await reloadSame('reload with a draft waiting');
  check(/Skill book/.test(await text('[data-id=offer-title]')), 'the draft is still waiting after the reload');
  const take = act('take-0-0');
  check(/replaces /.test(await take.innerText()), 'the draft offers to overwrite slot 1: ' + (await take.innerText()));
  const drafted = await text('[data-id^="offer-"]:not([data-id=offer-title]) b');
  await take.click();
  check((await page.locator('[data-id=offer-title]').count()) === 0, 'taking the tree closes the draft');
  await act('tab-trees').click();
  const slot0After = await text('[data-id=slot-0] b');
  check(slot0After === 'Slot 1: ' + drafted && drafted !== slot0Before, 'slot 1 now holds ' + drafted + ' instead of ' + slot0Before);
  check(/^0\//.test(await text('[data-id=slot-0] .num')), 'the new tree starts with no nodes bought');
  check((await text('[data-id=sp2]')) === spBefore, 'the points spent in the overwritten tree are lost, not refunded (' + spBefore + ')');
  await act('tab-log').click();
  check((await text('[data-id=feed]')).includes('Took ' + drafted + ' into slot 1 (replaced ' + slot0Before + ')'), 'the log says which tree was replaced');
  await act('tab-train').click();
  await reloadSame('reload after the overwrite');

  // ---- finish the run, the end screen, the wheel, the grade screen and the Vault
  played = await t.playUntil('end');
  if (await act('fight-continue').count()) await act('fight-continue').click();
  check(await act('seal').isVisible(), 'the resumed run plays to its end (' + played.fights + ' more fights)');
  await t.layout('end screen');
  const finalHash = (await t.readAccount()).ActiveRun.Hash;
  await act('seal').click();
  await page.locator('[data-id=climb-reel]').waitFor();
  await page.waitForTimeout(500);
  await t.layout('gauntlet wheel');
  await act('climb-skip').click();
  await act('climb-continue').click();
  await page.locator('[data-id=grade]').waitFor();
  await t.layout('grade screen');
  const sealed = await t.readAccount();
  const entry = sealed.Account.Vault[0];
  check(sealed.ActiveRun === null && sealed.Account.Vault.length === 1 && entry.Log.length > 20, 'the resumed run was sealed into the Vault');
  check(entry.RunHash === finalHash, 'the Vault entry\'s run hash is the hash of the run resumed across the reloads (' + finalHash.slice(0, 12) + ')');
  check((await text('#main')).includes('Run hash ' + finalHash.slice(0, 16)), 'and the grade screen shows it');
  await act('to-vendor').click();                   // the grade screen leads to the post-run vendor (specs/18 section 4)
  await page.locator('[data-id=vendor-offer]').first().waitFor();
  await t.layout('vendor screen');
  await act('vendor-done').click();
  await act('start').waitFor();
  await t.layout('Vault (home after a run)');
  check((await page.locator('[data-id=vault-entry]').count()) === 1, 'the Vault lists the sealed monster');

  // ---- a save whose run in progress no longer replays (specs/06 section 5): only that run is dropped
  await act('species-' + speciesId).click();
  await act('grant-book').click();
  await act('book-toggle').check();
  await act('start').click();
  await act('drill-str').waitFor();
  await act('drill-str').click();
  const live = await t.readAccount();
  check(live.ActiveRun && live.ActiveRun.Books.length === 1, 'a second run is in progress with a book');
  const stale = JSON.parse(JSON.stringify(live));
  stale.ActiveRun.Hash = (stale.ActiveRun.Hash[0] === '0' ? '1' : '0') + stale.ActiveRun.Hash.slice(1);   // as if the rules changed
  const staleText = H.encodeSave(stale);
  await t.writeSaveText(staleText);
  await page.reload();
  await act('start').waitFor({ timeout: 90000 });
  const toasts = await page.locator('#toasts').innerText();
  check(/older rules/.test(toasts) && /books returned/.test(toasts), 'the player is told the stale run was dropped: ' + toasts.replace(/\s+/g, ' ').slice(0, 90));
  const bad1 = await t.readBadTexts();
  check(bad1.length === 1 && bad1[0] === staleText, 'the original save is kept aside under a backup key (' + bad1.length + ' kept)');
  const fixed = await t.readAccount();
  check(fixed.ActiveRun === null, 'the stale run is gone from the saved account');
  check(fixed.Account.Shards === live.Account.Shards && fixed.Account.Vault.length === 1 && fixed.Account.Vault[0].RunHash === entry.RunHash,
    'shards and Vault are kept (' + fixed.Account.Shards + ' shards, ' + fixed.Account.Vault.length + ' monster)');
  const bookId = live.ActiveRun.Books[0];
  check((fixed.Account.Books[bookId] || 0) === (live.Account.Books[bookId] || 0) + 1, 'the run\'s book is back in the inventory (' + bookId + ')');
  check(JSON.stringify(fixed.Account.Talents) === JSON.stringify(live.Account.Talents), 'talents are kept');
  await t.layout('home after a stale save');
  await t.reload();
  check(!/older rules/.test(await page.locator('#toasts').innerText()), 'the notice does not repeat on the next load');
  await act('start').click();
  await act('drill-str').waitFor();
  check((await t.readAccount()).ActiveRun !== null, 'the account still starts a run');

  // ---- an injury (specs/14 section 4.1, issue #89): drilling without rest injures the monster; it loses the rest of the cycle
  // and its fight and sits out the next cycle, then the run goes on
  const hurt = await page.evaluate(() => {
    const q = (s) => document.querySelector(s);
    const click = (s) => { const e = q(s); if (!e || e.disabled) return false; e.click(); return true; };
    for (let i = 0; i < 200; i++) {
      if (q('[data-id=injury-result]')) return { cycle: q('[data-id=cycle]').textContent, result: q('[data-id=injury-result]').textContent,
        text: (q('[data-id=injury]') || {}).textContent || '' };
      if (q('[data-id=fight-play]')) { click('[data-act="speed-skip"]'); continue; }
      if (click('[data-act="fight-continue"]') || click('[data-act="discard"]') || click('[data-act="fight"]')) continue;
      if (!click('[data-act="drill-str"]')) return null;
    }
    return null;
  });
  check(!!hurt, 'drilling without rest injures the monster: ' + JSON.stringify(hurt));
  const hm = hurt && /Lost: the rest of cycle (\d+), its rank fight, cycle (\d+) \(no drills, fights or cycle SP\)\. Back in cycle (\d+) with (\d+) energy\./.exec(hurt.text);
  check(!!hm && +hm[2] === +hm[1] + 1 && +hm[3] === +hm[1] + 2 && /^Injured!/.test(hurt.text), 'a card at the top says what the injury cost and when the monster is back: ' + (hurt && hurt.text));
  check(!!hm && hurt.result === 'Injured! Back in cycle ' + hm[3] + '.', 'so does the drill result: ' + (hurt && hurt.result));
  check(!!hm && hurt.cycle === 'Cycle ' + hm[3] + '/12', 'the run goes on in the cycle after the lost one: ' + (hurt && hurt.cycle));
  check(!!hm && new RegExp('Energy ' + hm[4] + '/').test(await text('.bar.energy span')), 'with the energy it came back with');
  check(/Injured! Back in cycle/.test(await page.locator('#toasts').innerText()), 'a toast says when it is back');
  await t.layout('run screen after an injury');
  await reloadSame('reload after an injury');
  check(/Back from injury/.test(await text('[data-id=injury]')) && (await text('[data-id=injury]')).includes('Back in cycle ' + (hm && hm[3])),
    'after a reload the run screen still says the monster is back from injury');
  await act('drill-str').click();
  check((await t.readAccount()).ActiveRun.Log.length > 0, 'and it trains again');

  // ---- a damaged save: kept aside, and a fresh account that works
  const damaged = (await t.readSaveText()).slice(0, -20);             // cut short: the length and CRC no longer match
  await t.writeSaveText(damaged);
  await page.reload();
  await act('start').waitFor({ timeout: 90000 });
  const toasts2 = await page.locator('#toasts').innerText();
  check(/could not be loaded and was kept aside/.test(toasts2), 'the player is told the save was kept aside: ' + toasts2.replace(/\s+/g, ' ').slice(0, 90));
  const bad2 = await t.readBadTexts();
  check(bad2.length === 2 && bad2[0] === staleText && bad2[1] === damaged, 'the damaged save is kept aside, unchanged, next to the stale save kept before (issue #93)');
  const fresh = await t.readAccount();
  check(fresh && fresh.Kind === 'monster_account' && fresh.ActiveRun === null && fresh.Account.Vault.length === 0 && fresh.Account.Shards === 0, 'a fresh account replaces it');
  check((await page.locator('[data-id=vault-entry]').count()) === 0 && (await text('[data-id=shards]')).startsWith('0 '), 'the home screen shows the fresh account');
  await t.layout('home after a damaged save');
  await act('start').click();
  await act('drill-str').waitFor();
  await act('drill-str').click();
  await t.reload();
  check((await page.locator('[data-act="drill-str"]').count()) === 1 && (await t.readAccount()).ActiveRun.Log.length === 1, 'the fresh account saves and resumes a run');
  const bad3 = await t.readBadTexts();
  check(bad3.length === 2 && bad3[0] === staleText && bad3[1] === damaged, 'and both kept-aside saves are still there');

  // ---- the backup cap (issue #93): a legacy single backup counts as the oldest and is never evicted; a new backup past the cap
  // evicts the second oldest
  const legacy = 'legacy backup written before issue #93';
  await page.evaluate(([k, v]) => localStorage.setItem(k, v), [H.BAD_KEY, legacy]);
  const keep = await page.evaluate(() => RunEngine.BAD_KEEP);
  const extra = [];
  for (let i = 0; i < 2; i++) {
    const d = (await t.readSaveText()).slice(0, -(21 + i));           // damaged differently each time
    extra.push(d);
    await t.writeSaveText(d);
    await t.reload();
  }
  const bad4 = await t.readBadTexts();
  check(keep === 3 && bad4.length === keep && bad4[0] === legacy && bad4[1] === extra[0] && bad4[2] === extra[1],
    'past the cap of ' + keep + ' the legacy backup and the newest saves are kept (' + bad4.length + ' kept)');
  check((await page.evaluate((k) => localStorage.getItem(k), H.BAD_KEY)) === legacy, 'the legacy backup key is untouched');

  await t.finish();
})().catch((e) => { console.error(e); process.exit(1); });
