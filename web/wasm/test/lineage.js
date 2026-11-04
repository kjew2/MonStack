#!/usr/bin/env node
// Fortuna I–V prototype (specs/29): real UI breeding, focus-node progress, sealing and reload,
// a protected V breakthrough, inherited V capstone presentation, and phone layout.
'use strict';
const H = require('./harness.js');

(async () => {
  const t = await H.open({ shotsDir: H.shotsArg() });
  const { page, check, act, text } = t;
  const account = async () => (await t.readAccount()).Account;
  async function homeAfterSeal() {
    await act('climb-continue').click();
    await page.locator('[data-id=grade]').waitFor();
    check(/\+20 Fortune lineage points/.test(await text('[data-id=grade-lineage]')),
      'the grade screen immediately reports the descendant’s 20 lineage points');
    check(/Fortune V.*unlocked/.test(await text('[data-id=grade-lineage-result]')),
      'the grade screen announces the transformative Fortune V breakthrough');
    await t.layout('lineage breakthrough grade screen');
    if (await act('to-vendor').count()) { await act('to-vendor').click(); await act('vendor-done').click(); }
    else await act('new-run').click();
    await act('start').waitFor();
  }
  async function openBreeding() {
    await page.locator('[data-id=lineage-panel] summary').click();
    await page.locator('[data-id=breed-primary]').waitFor();
  }
  async function breed() {
    await page.locator('[data-id=breed-primary]').selectOption('0');
    await page.locator('[data-id=breed-support]').selectOption('0');
    check(await act('breed').isDisabled(), 'the same monster cannot fill both parent slots');
    await page.locator('[data-id=breed-support]').selectOption('1');
    check(await act('breed').isEnabled(), 'two eligible parents enable breeding');
    await act('breed').click();
    await act('drill-str').waitFor();
  }

  await page.goto(t.url);
  await act('start').waitFor({ timeout: 90000 });
  await page.emulateMedia({ reducedMotion: 'reduce' });

  // Two certified-parent fixtures; the tested offspring itself is bred, trained and sealed through the real UI.
  const save = await t.readAccount();
  const parent = {
    SpeciesId: 'slime', Grade: 'e', End: 'Completed', Cycles: 12,
    Stats: { Hp: 200, Str: 100, Int: 100, Wis: 100, Dex: 100, Def: 100, Spd: 100, Luk: 100 },
    Trees: [],
  };
  save.Account.Vault = [0, 1].map((id) => Object.assign(JSON.parse(JSON.stringify(parent)), { Id: id }));
  save.Account.Vault[0].Trees.push({ TreeId: 'fortune', Bought: 2 });
  save.Account.RunsStarted = 2;
  save.Account.Shards = 10000;
  save.Account.Fortuna = { Rank: 4, Points: 44, Failures: 4, Attempts: 4 };
  await t.writeSaveText(H.encodeSave(save));
  await t.reload();
  await openBreeding();
  check(/Fortune IV/.test(await text('[data-id=lineage-panel] summary')) && /100%/.test(await text('[data-id=lineage-panel]')),
    'the lineage panel shows rank IV and its guaranteed protected breakthrough');
  await t.layout('breeding parent selection');
  await breed();
  const born = await t.readAccount();
  check(born.Run.Lineage.Rank === 4 && born.Run.Slots[2].TreeId === 'fortune' && !(born.Run.Slots[2].Bought || 0),
    'the newborn captures Fortune IV in slot 3 with no nodes purchased');
  check(born.Account.Vault.length === 2 && born.Account.Shards < 10000 && born.Account.Fortuna.Points === 44,
    'breeding keeps both parents, spends shards and awards no lineage points');
  await act('tab-trees').click();
  check(/Fortune IV/.test(await text('[data-id=slot-2]')), 'the inherited rank appears on the tree card');
  await act('buy-2').click();
  check(/1 \/ 20 points/.test(await text('[data-id=run-lineage]')), 'the first node records one point for sealing');
  check((await account()).Fortuna.Points === 44, 'buying a node does not prematurely award account progress');
  await act('tab-train').click();
  await t.reload();
  const resumed = (await t.readAccount()).Run;
  check(resumed.Lineage.Rank === 4 && resumed.Lineage.Deepest === 1 && resumed.Slots[2].Bought === 1,
    'reload preserves inherited rank, bought nodes and the lineage high-water mark');

  // Spend only on the inherited focus tree, rest through training, discard offers and skip automatic combat.
  const played = await page.evaluate(async () => {
    const q = (s) => document.querySelector(s);
    const click = (s) => { const e = q(s); if (!e || e.disabled) return false; e.click(); return true; };
    for (let i = 0; i < 1500; i++) {
      if (q('#sync-wait')) { await new Promise((r) => setTimeout(r, 20)); continue; }
      if (q('[data-id=fight-play]') || q('[data-id=fight-intro]')) { click('[data-act="speed-skip"]'); continue; }
      if (click('[data-act="fight-continue"]')) continue;
      if (q('[data-id=offer-title]')) { click('[data-act="discard"]'); continue; }
      click('[data-act="tab-trees"]');
      while (click('[data-act="buy-2"]')) { /* focus tree only */ }
      click('[data-act="tab-train"]');
      if (q('[data-act="seal"]')) return true;
      if (click('[data-act="fight"]')) continue;
      click('[data-act="rest"]');
    }
    return false;
  });
  check(played && await act('seal').isVisible(), 'the bred descendant finishes a complete training run');
  const finishedRun = (await t.readAccount()).Run;
  check(finishedRun.Lineage.Deepest === 8 && finishedRun.Slots[2].Bought === 8, 'the focus tree reaches its capstone');
  await act('seal').click();
  const sealed = await account();
  check(sealed.Fortuna.Rank === 5 && sealed.Fortuna.LastAward === 20 && sealed.Fortuna.Points === 19,
    'sealing awards 20 node points, guarantees V, and keeps the 19-point overflow');
  check(sealed.Fortuna.Attempts === 5 && !(sealed.Fortuna.Failures || 0), 'the fifth attempt succeeds and resets pity');
  check(sealed.Vault.length === 3 && sealed.Vault[2].Lineage.Rank === 4,
    'the sealed descendant keeps its birth rank IV after the account unlocks V');
  await homeAfterSeal();
  await t.reload();
  await openBreeding();
  check(/Fortune V/.test(await text('[data-id=lineage-panel] summary')), 'the V breakthrough survives a server reload');
  await breed();
  await act('tab-trees').click();
  check(/Fortune V/.test(await text('[data-id=slot-2]')) && /Capstone:.*bank/i.test(await text('[data-id=slot-2]')),
    'new descendants inherit V and display its transformative capstone effect');
  await t.layout('Fortune V inherited tree');
  await t.finish();
})().catch((e) => { console.error(e); process.exit(1); });
