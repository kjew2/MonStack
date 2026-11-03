#!/usr/bin/env node
// Browser checks of the monster-trainer page beyond the main run (web/wasm/test/run-trainer.js), at phone width 390x844:
// - issue #87: each main screen (pick, run, fight playback, tree draft, end, gauntlet wheel, grade, vendor, Vault) has no horizontal scroll
//   and no page/console error; a tree draft overwrites an occupied slot (specs/12 section 5.1: half its spent points come back).
// - issue #88: save, reload and resume. A run reloaded mid-way (also with a draft waiting) comes back with the same state hash and
//   is then finished; an account on the server whose run in progress no longer loads drops only that run, with a notice and its
//   books returned (specs/06 section 5, specs/27 section 7); a damaged copy on the device does not matter, since every load opens
//   the server's account (specs/27 section 5.3).
// - issue #89: an injury loses the rest of its cycle, its fight and the next cycle, says when the monster is back, and survives a reload.
// - issue #105: every talent at max rank shows its bonus on the home screen and in the run; Second Look rerolls a draft once.
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
    s.bars = await page.evaluate(() => [...document.querySelectorAll('.dtile .val')].map((e) => e.textContent).join('|'));
    const r = a && a.Run;   // save v10: the run in progress as its state (specs/27 section 3.5); a field at its starting value is left out
    s.saved = r ? { species: r.SpeciesId, cycle: r.Cycle || 0, drills: r.Drills || 0, rests: r.Rests || 0, log: r.Log !== undefined } : null;
    return s;
  };
  const reloadSame = async (what) => {
    const before = await snapshot();
    await t.reload();
    const after = await snapshot();
    check(before.saved && before.hash && before.saved.log === false, what + ': the run is saved as its state, with no command log (' + before.saved.drills + ' drills)');
    check(JSON.stringify(before) === JSON.stringify(after), what + ': reload restores the same run (state hash, HUD, stats, trees, saved state)' +
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
  await act('tab-ladder').click();
  await page.locator('[data-id=ladder]').waitFor();
  await t.layout('rank ladder');
  await act('tab-trees').click();
  await t.layout('trees tab');
  await act('tab-train').click();
  const r0 = (await t.readAccount()).Run;
  check(r0 && r0.SpeciesId === speciesId && r0.Books.length === 1, 'the run started with the ' + speciesId + ' and the book');
  await act('drill-str').click();
  await act('rest').click();
  await act('drill-hp').click();
  await reloadSame('mid-run reload');

  // ---- first rank fight: playback screen, Skip, result
  let played = await t.playUntil('fight');
  check(await act('fight').isEnabled(), 'a rank fight is due after ' + played.steps + ' steps');
  const clipped = await page.$$eval('#main [data-id=stage], #main [data-id=pad]', (cs) => cs.map((c) => ({ card: Math.round(c.getBoundingClientRect().height), rows: c.scrollHeight })));
  check(clipped.length === 2 && clipped.every((c) => c.card >= c.rows - 1), 'issue #113: the stage and the drill tiles are not squeezed while a fight is due: ' + JSON.stringify(clipped));
  await act('fight').click();
  await page.locator('[data-id=fight-intro]').waitFor();
  await t.layout('fight intro');
  await act('intro-skip').click();
  await page.locator('[data-id=fight-play]').waitFor();
  await page.waitForTimeout(600);
  await t.layout('fight playback');
  await act('speed-skip').click();
  await page.locator('[data-id=fight-title]').waitFor();
  await t.layout('fight result');
  await act('fight-continue').click();
  await reloadSame('reload after a rank fight');

  // ---- buy nodes in slot 1, then a book draft overwrites it: the tree changes and half its spent points come back
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
  const lit = await page.$$eval('[data-id=slot-0] .npath .nt', (ts) => ts.map((t) => (t.classList.contains('got') ? 'G' : t.classList.contains('next') ? 'N' : '.')).join(''));
  check(lit === 'G'.repeat(bought0) + (bought0 < 8 ? 'N' + '.'.repeat(7 - bought0) : ''), 'issue #115: slot 1\'s path lights the ' + bought0 + ' bought tile(s) and outlines the next: ' + lit);
  const slot0Before = (await text('[data-id=slot-0] b')).replace(/^Slot 1: /, '');
  const spBefore = await text('[data-id=sp2]');
  const bookBtn = page.locator('[data-act^="book-"]:not([data-act="book-toggle"])').first();
  check((await bookBtn.count()) === 1, 'the brought book can be used');
  await bookBtn.click();
  await page.locator('[data-id=offer-title]').waitFor();
  await t.layout('tree draft');
  const tiles = await page.locator('.tcard.sel .npath .nt').count();
  check(tiles >= 2 && (await page.locator('.tcard.sel .bchip').count()) >= 1, 'the drafted tree shows its node path (' + tiles + ' tiles) and its stat boosts as chips');
  await act('offer-nodes').click();
  check((await page.locator('.tcard.sel .node').count()) === tiles, '"All nodes" lists one row per path tile');
  await act('offer-nodes').click();
  check((await page.locator('.tcard.sel .node').count()) === 0, 'and hides them again');
  await reloadSame('reload with a draft waiting');
  check(/Skill book/.test(await text('[data-id=offer-title]')), 'the draft is still waiting after the reload');
  const take = act('take-0-0');
  check(/replaces /.test(await take.getAttribute('aria-label')), 'the draft offers to overwrite slot 1: ' + (await take.getAttribute('aria-label')));
  const drafted = await text('[data-id^="offer-"]:not([data-id=offer-title]) b');
  await take.click();
  check((await page.locator('[data-id=offer-title]').count()) === 0, 'taking the tree closes the draft');
  await act('tab-trees').click();
  const slot0After = await text('[data-id=slot-0] b');
  check(slot0After === 'Slot 1: ' + drafted && drafted !== slot0Before, 'slot 1 now holds ' + drafted + ' instead of ' + slot0Before);
  check(/^0\//.test(await text('[data-id=slot-0] .num')), 'the new tree starts with no nodes bought');
  const spAfter = await text('[data-id=sp2]');
  const refunded = parseInt(spAfter, 10) - parseInt(spBefore, 10);   // half of the spent SP, rounded down: 0 for one 1-SP node
  check(refunded >= (bought0 >= 2 ? 1 : 0), 'half the points spent in the overwritten tree come back: ' + spBefore + ' -> ' + spAfter);
  await act('tab-log').click();
  if (refunded > 0) check((await text('[data-id=feed]')).includes('+' + refunded + ' SP refunded'), 'the log shows the refund (+' + refunded + ' SP)');
  check((await text('[data-id=feed]')).includes('Took ' + drafted + ' into slot 1 (replaced ' + slot0Before + ')'), 'the log says which tree was replaced');
  await act('tab-train').click();
  await reloadSame('reload after the overwrite');

  // ---- finish the run, the end screen, the wheel, the grade screen and the Vault
  played = await t.playUntil('end');
  if (await act('fight-continue').count()) await act('fight-continue').click();
  check(await act('seal').isVisible(), 'the resumed run plays to its end (' + played.fights + ' more fights)');
  await t.layout('end screen');
  const finalRun = (await t.readAccount()).Run;
  await page.emulateMedia({ reducedMotion: 'reduce' });   // with reduced motion the wheel lands at once, no spin
  await act('seal').click();
  await page.locator('[data-id=climb-reel]').waitFor();
  await t.layout('gauntlet wheel');
  check((await act('climb-skip').count()) === 0 && /^(Floor \d+|All \d+ floors): \S/.test(await page.locator('[data-id=climb-title]').innerText()), 'with reduced motion the wheel lands at once');
  await page.emulateMedia({ reducedMotion: 'no-preference' });
  await act('climb-continue').click();
  await page.locator('[data-id=grade]').waitFor();
  await t.layout('grade screen');
  const sealed = await t.readAccount();
  const entry = sealed.Account.Vault[0];
  check(sealed.Run === undefined && sealed.Account.Vault.length === 1 && entry.Log === undefined && Number.isInteger(entry.Id) && entry.Id >= 0, 'the resumed run was sealed into the Vault as its result, named by its run (id ' + entry.Id + ')');
  check(JSON.stringify(entry.Stats) === JSON.stringify(finalRun.Stats) && entry.RunHash === undefined && entry.Seed === undefined,
    'the Vault entry keeps the final stats of the run resumed across the reloads, and no run hash or seed (save v10)');
  await act('to-vendor').click();                   // the grade screen leads to the post-run vendor (specs/18 section 4)
  await page.locator('[data-id=vendor-offer]').first().waitFor();
  await t.layout('vendor screen');
  check((await t.readAccount()).Account.Vendor != null, 'the open vendor is in the save while its screen is up');
  await act('vendor-done').click();
  await act('start').waitFor();
  check((await t.readAccount()).Account.Vendor == null, 'Done closes the vendor (kj, 2026-11-01: transient)');
  await act('hometab-vault').click();
  await t.layout('Vault (home after a run)');
  check((await page.locator('[data-id=vault-entry]').count()) === 1, 'the Vault lists the sealed monster');
  await act('hometab-dungeon').click();
  await t.layout('Dungeon tab');
  await act('tower').click();
  await act('tower-climb').waitFor();
  await t.layout('Dungeon screen');
  await act('tower-back').click();
  await act('hometab-raise').click();

  // ---- a save whose run in progress refers to something no longer in the game (specs/06 section 5): only that run is dropped
  await act('species-' + speciesId).click();
  await act('grant-book').click();
  await act('book-toggle').check();
  await act('start').click();
  await act('drill-str').waitFor();
  await act('drill-str').click();
  const live = await t.readAccount();
  check(live.Run && live.Run.Books.length === 1, 'a second run is in progress with a book');
  const stale = JSON.parse(JSON.stringify(live));
  stale.Run.SpeciesId = 'species_removed_from_content';   // as if a content update removed it
  await t.writeSaveText(H.encodeSave(stale));             // the server's copy (its local test server)
  await page.reload();
  await act('start').waitFor({ timeout: 90000 });
  const toasts = await page.locator('#toasts').innerText();
  check(/no longer in the game/.test(toasts) && /books returned/.test(toasts), 'the player is told the stale run was dropped: ' + toasts.replace(/\s+/g, ' ').slice(0, 90));
  const fixed = await t.readAccount();
  check(fixed.Run === undefined, 'the stale run is gone from the loaded account');
  check(fixed.Account.Shards === live.Account.Shards && fixed.Account.Vault.length === 1 && fixed.Account.Vault[0].Id === entry.Id,
    'shards and Vault are kept (' + fixed.Account.Shards + ' shards, ' + fixed.Account.Vault.length + ' monster)');
  const bookId = live.Run.Books[0];
  check((fixed.Account.Books[bookId] || 0) === (live.Account.Books[bookId] || 0) + 1, 'the run\'s book is back in the inventory (' + bookId + ')');
  check(JSON.stringify(fixed.Account.Talents) === JSON.stringify(live.Account.Talents), 'talents are kept');
  await t.layout('home after a stale save');
  // The next commit stores the account as this load made it (specs/27 section 7), so the notice does not come back.
  await act('start').click();
  await act('drill-str').waitFor();
  await t.reload();
  check(!/no longer in the game/.test(await page.locator('#toasts').innerText()), 'the notice does not repeat once the account was committed');
  check((await t.readAccount()).Run !== undefined, 'the account still starts a run');

  // ---- an injury (specs/14 section 4.1, issue #89): drilling without rest injures the monster; it loses the rest of the cycle
  // and its fight and sits out the next cycle, then the run goes on. While training (no fight card up), the stage, the drill pad
  // and Rest stay where they are, results and the injury banner included: buttons never move under a thumb.
  const hurt = await page.evaluate(() => {
    const q = (s) => document.querySelector(s);
    const click = (s) => { const e = q(s); if (!e || e.disabled) return false; e.click(); return true; };
    const at = (s) => Math.round(q(s).getBoundingClientRect().top + q('#main').scrollTop);
    const spots = new Set();
    const measure = () => { if (!q('[data-id=fight-card]') && q('[data-act=rest]')) spots.add([at('[data-id=stage]'), at('[data-id=pad]'), at('[data-act=rest]')].join('/')); };
    for (let i = 0; i < 200; i++) {
      measure();
      if (q('[data-id=injury-result]')) return { cycle: q('[data-id=cycle]').textContent, result: q('[data-id=injury-result]').textContent,
        text: (q('[data-id=injury]') || {}).textContent || '', spots: [...spots] };
      if (q('[data-id=fight-play]') || q('[data-id=fight-intro]')) { click('[data-act="speed-skip"]'); continue; }
      if (click('[data-act="fight-continue"]') || click('[data-act="discard"]') || click('[data-act="fight"]')) continue;
      if (!click('[data-act="drill-str"]')) return null;
    }
    return null;
  });
  check(!!hurt, 'drilling without rest injures the monster: ' + JSON.stringify(hurt));
  const hm = hurt && /Lost: the rest of cycle (\d+), its rank fight, cycle (\d+) \(no drills, fights or cycle SP\)\. Back in cycle (\d+) with (\d+) energy\./.exec(hurt.text);
  check(!!hm && +hm[2] === +hm[1] + 1 && +hm[3] === +hm[1] + 2 && /^Injured!/.test(hurt.text), 'a banner on the stage says what the injury cost and when the monster is back: ' + (hurt && hurt.text));
  check(!!hurt && hurt.spots.length === 1, 'drills, results and the injury banner never move the stage, the drill pad or Rest (stage/pad/rest tops: ' + (hurt && hurt.spots.join(', ')) + ')');
  check(!!hm && hurt.result === 'Injured! Back in cycle ' + hm[3] + '.', 'so does the drill result: ' + (hurt && hurt.result));
  check(!!hm && hurt.cycle === 'Cycle ' + hm[3] + '/12', 'the run goes on in the cycle after the lost one: ' + (hurt && hurt.cycle));
  check(!!hm && new RegExp('Energy ' + hm[4] + '/').test(await text('.bar.energy span')), 'with the energy it came back with');
  check(/Injured! Back in cycle/.test(await page.locator('#toasts').innerText()), 'a toast says when it is back');
  await t.layout('run screen after an injury');
  await reloadSame('reload after an injury');
  check(/Back from injury/.test(await text('[data-id=injury]')) && (await text('[data-id=injury]')).includes('Back in cycle ' + (hm && hm[3])),
    'after a reload the run screen still says the monster is back from injury');
  await act('drill-str').click();
  check((await page.locator('[data-id=injury]').count()) === 0, 'the injury banner leaves the stage at the first action back');
  check((await t.readAccount()).Run.Drills > 0, 'and it trains again');

  // ---- a damaged copy on the device: every load opens the server's account (specs/27 section 5.3), so it does not matter
  const good = await t.readAccount();
  await page.evaluate((k) => localStorage.setItem(k, 'IDLERNG-SAVE damaged'), H.SAVE_KEY);
  await t.reload();
  const back = await t.readAccount();
  check(back && back.Run && back.Run.Drills === good.Run.Drills && back.Account.Vault.length === good.Account.Vault.length,
    'a damaged device copy is replaced by the server\'s account, run in progress included (' + back.Run.Drills + ' drills)');
  await clickDom('[data-act="tab-log"]');               // the run's Log tab holds Abandon
  await act('abandon').click();
  await act('abandon-yes').click();
  await act('start').waitFor();

  // ---- talent catalog v2 (issue #105, specs/18 section 3): an account with every talent at max rank. The home screen sums the
  // bonuses, the run shows a fifth tree slot and the bigger rest, and a milestone draft can be rerolled once (Second Look)
  const talented = await t.readAccount();
  delete talented.Run;
  await act('hometab-talents').click();
  const talentIds = await page.evaluate(() => [...document.querySelectorAll('[data-id^="talent-"]')].map((e) => e.getAttribute('data-id').slice(7))
    .filter((id) => id !== 'now'));
  const maxRanks = await page.evaluate(() => [...document.querySelectorAll('[data-id^="talent-"] .tag.num')].map((e) => +e.textContent.split('/')[1]));
  check(talentIds.length === 19 && maxRanks.length === 19, 'the talent list shows all 19 talents (' + talentIds.join(' ') + ')');
  talented.Account.Talents = {};
  // legendary talents (issue #103) stay out: each changes a rule this test does not expect (LegendaryTalentTests and run-trainer.js cover them)
  talentIds.forEach((id, i) => { if (!['iron_constitution', 'gamblers_blood', 'glass_cannon', 'echo_training'].includes(id)) talented.Account.Talents[id] = maxRanks[i]; });
  await t.writeSaveText(H.encodeSave(talented));
  await t.reload();
  await act('hometab-talents').click();
  const now = await text('[data-id=talent-now]');
  check(['rests +30 energy', 'a free drill after each Super', '+1 tree slot', '1 injury prevented per run', '+2 AP in the last cycle',
    'milestones at x0.80', '1 Awakening reroll per run', 'shards x1.50', '+2 SP per lost fight'].every((x) => now.includes(x)), 'the home screen sums every talent bonus: ' + now);
  await act('hometab-raise').click();
  await act('start').click();
  await act('drill-hp').waitFor();
  check(/^Rest \(\+80 energy\)$/.test(await text('[data-act="rest"]')), 'the rest button shows Deep Rest\'s bigger rest: ' + await text('[data-act="rest"]'));
  const hpTicks = await page.$$eval('[data-id=stat-hp] .bar.gauge .tick', (ts) => ts.map((t) => t.dataset.at));
  check(hpTicks[0] === '320', 'Early Bloom moves the milestone tick to 320: ' + hpTicks.join(' '));
  await clickDom('[data-act="tab-trees"]');
  check((await page.locator('.slotcard').count()) === 5, 'Fifth Slot: the monster has five tree slots');
  await clickDom('[data-act="tab-train"]');
  const draft = await page.evaluate(() => {
    const q = (s) => document.querySelector(s);
    const click = (s) => { const e = q(s); if (!e || e.disabled) return false; e.click(); return true; };
    for (let i = 0; i < 200; i++) {
      if (q('[data-act="reroll"]')) return [...document.querySelectorAll('[data-id^="offer-"]')].map((e) => e.getAttribute('data-id')).filter((x) => x !== 'offer-title');
      if (q('[data-id=fight-play]') || q('[data-id=fight-intro]')) { click('[data-act="speed-skip"]'); continue; }
      if (click('[data-act="fight-continue"]') || click('[data-act="discard"]') || click('[data-act="fight"]')) continue;
      const energy = +((/Energy (\d+)/.exec((q('.bar.energy span') || {}).textContent || '') || [])[1]);
      if (!(energy >= 40) ? !click('[data-act="rest"]') : !click('[data-act="drill-hp"]')) return null;
    }
    return null;
  });
  check(!!draft && draft.length > 0, 'an Awakening offers Second Look\'s reroll: ' + JSON.stringify(draft));
  // Rolled Awakening trees (specs/15 section 4.1, issue #121): every card names its rolled stat, and a rolled node's tooltip
  // shows its rolled amount.
  const rolledChips = await page.locator('.tcard [data-id=rolled-stat]').count();
  check(!!draft && rolledChips === draft.length, 'each Awakening card shows its rolled stat: ' + rolledChips + ' of ' + (draft || []).length);
  const rolledTip = page.locator('.tcard [data-tip*="~"]').first();
  const rolledRef = await rolledTip.getAttribute('data-tip');
  await rolledTip.click();
  const rolledText = await page.locator('#tip').innerText().catch(() => '');
  const [rStat, rAmount] = (rolledRef || '').split('~').slice(-2);   // node:<id>~<stat>~<amount> (a capstone: ~cap~<stat>~<growth>)
  check(!!rStat && rolledText.includes('+' + rAmount) && rolledText.toUpperCase().includes(rStat.toUpperCase()), 'a rolled node\'s tooltip shows its roll (' + rolledRef + '): ' + rolledText.replace(/\s+/g, ' ').slice(0, 60));
  await page.keyboard.press('Escape');
  const toastsUp = await page.locator('#toasts .toast').allTextContents();
  check(toastsUp.length === 0, 'issue #115: no toast covers the Awakening (its milestone toast is skipped): ' + JSON.stringify(toastsUp));
  await t.layout('tree draft with a reroll');
  await clickDom('[data-act="reroll"]');
  const redrawn = await page.evaluate(() => [...document.querySelectorAll('[data-id^="offer-"]')].map((e) => e.getAttribute('data-id')).filter((x) => x !== 'offer-title'));
  // One of the crossing stat's own trees (HP: Vital Core or Lifeblood) is always the first card and a reroll keeps it (issue #121).
  check(!!draft && ['offer-vital_core', 'offer-lifeblood'].includes(draft[0]), 'the HP Awakening offers one of HP\'s own trees first: ' + JSON.stringify(draft));
  check(!!draft && redrawn[0] === draft[0] && redrawn.length > 1 && redrawn.slice(1).every((x) => draft.indexOf(x) < 0),
    'the reroll keeps HP\'s tree and draws other trees: ' + JSON.stringify(redrawn));
  check((await page.locator('[data-act="reroll"]').count()) === 0, 'and only once per run');
  await clickDom('[data-act="discard"]');
  const afterOffer = await page.locator('#toasts').innerText();
  check(!/Milestone|Awakening/.test(afterOffer), 'issue #115: the skipped milestone toast does not show once the offer closes either: ' + JSON.stringify(afterOffer));
  await reloadSame('reload after a rerolled draft is discarded');

  await t.finish();
})().catch((e) => { console.error(e); process.exit(1); });
