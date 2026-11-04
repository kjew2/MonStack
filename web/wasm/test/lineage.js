#!/usr/bin/env node
// Generic I–X lineages (specs/29): real UI breeding, focus-node progress, sealing and reload,
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
  async function openBreeding(tree = 'fortune') {
    await act('open-lineages').click();
    await act('lineage-' + tree).click();
  }
  async function breed() {
    await act('breed-parents').click();
    await t.layout('breeding parents');
    await page.locator('[data-id=breed-primary]').selectOption('0');
    check(await page.locator('[data-id=breed-support] option[value="0"]').count() === 0 && await act('breed-review').isDisabled(), 'the primary parent is unavailable as support and review needs two different parents');
    await page.locator('[data-id=breed-support]').selectOption('1');
    check(await act('breed-review').isEnabled(), 'two eligible parents enable offspring review');
    await act('breed-review').click();
    check(await act('breed').isEnabled(), 'the offspring review enables paid breeding');
    await t.layout('offspring review');
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
  save.Account.Lineages = { fortune: { ...save.Account.Fortuna }, brute_force: { Rank: 2, Points: 3 } };
  await t.writeSaveText(H.encodeSave(save));
  await t.reload();
  await openBreeding();
  check(await page.locator('[data-id=lineage-library] button').count() === 40, 'the lineage library contains all 40 generic trees');
  await page.locator('[data-id=lineage-search]').fill('brute');
  check(await page.locator('[data-id=lineage-library] button').count() === 1, 'search narrows the library by tree name');
  await page.locator('[data-id=lineage-search]').fill('');
  check(await page.locator('.lineage-track li').count() === 10 && (await page.locator('.lineage-track li').allTextContents()).join(',') === 'I,II,III,IV,V,VI,VII,VIII,IX,X', 'the selected lineage has a complete I–X rank track');
  check(await page.locator('.lineage-milestone').count() === 2 && /RANK V/.test(await text('.lineage-milestones')) && /RANK X/.test(await text('.lineage-milestones')), 'both transformative capstone milestones have dedicated cards');
  check(/Fortune IV/.test(await text('[data-id=lineage-detail]')) && /100%/.test(await text('[data-id=lineage-odds]')),
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
  async function playToSeal() { return page.evaluate(async () => {
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
  }); }
  const played = await playToSeal();
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
  check(/Fortune V/.test(await text('[data-id=lineage-detail]')), 'the V breakthrough survives a server reload');
  await breed();
  await act('tab-trees').click();
  check(/Fortune V/.test(await text('[data-id=slot-2]')) && /Capstone:.*bank/i.test(await text('[data-id=slot-2]')),
    'new descendants inherit V and display its transformative capstone effect');
  await t.layout('Fortune V inherited tree');
  // A different tree reaches X without modifying Fortune's independent account ledger.
  const xSave = await t.readAccount();
  const fortuneLedger = JSON.stringify(xSave.Account.Lineages.fortune);
  xSave.Run = null;
  xSave.Account.Vault[0].Trees.push({ TreeId: 'brute_force', Bought: 5 });
  xSave.Account.Lineages.brute_force = { Rank: 9, Points: 349, Failures: 5, Attempts: 5 };
  await t.writeSaveText(H.encodeSave(xSave));
  await t.reload();
  await openBreeding('brute_force');
  check(/Brute Force IX/.test(await text('[data-id=lineage-detail]')) && /100%/.test(await text('[data-id=lineage-odds]')), 'a second generic lineage exposes its protected guaranteed X attempt');
  await breed();
  check((await t.readAccount()).Run.Lineage.TreeId === 'brute_force', 'the selected generic tree becomes the newborn focus');
  check(await playToSeal(), 'the second generic descendant completes training');
  await act('seal').click();
  const xAccount = await account();
  check(xAccount.Lineages.brute_force.Rank === 10 && xAccount.Lineages.brute_force.Points === 19 && xAccount.Lineages.brute_force.LastAward === 20,
    'the protected X breakthrough awards capstone-weighted points and retains overflow');
  check(JSON.stringify(xAccount.Lineages.fortune) === fortuneLedger, 'progress in another lineage leaves the Fortune ledger untouched');
  await act('climb-continue').click();
  await page.locator('[data-id=grade]').waitFor();
  check(/\+20 Brute Force lineage points/.test(await text('[data-id=grade-lineage]')) && /Brute Force X/.test(await text('[data-id=grade-lineage-result]')), 'the reward recap reports the active tree and its X breakthrough');
  await t.layout('generic X breakthrough grade');
  if (await act('to-vendor').count()) { await act('to-vendor').click(); await act('vendor-done').click(); }
  else await act('new-run').click();
  await t.reload();
  await openBreeding('brute_force');
  check(/Rank X complete/.test(await text('[data-id=lineage-progress]')), 'the independent X ledger persists across reload');
  await t.layout('completed generic lineage');
  // Isolate the browser command/save contract with an established training fixture.
  const trainingSave = await t.readAccount();
  trainingSave.Run = null;
  trainingSave.Account.Vault[0].Trees.push({ TreeId: 'heirloom', Bought: 5 });
  trainingSave.Account.Lineages.heirloom = { Rank: 5 };
  await t.writeSaveText(H.encodeSave(trainingSave));
  await t.reload();
  await openBreeding('heirloom');
  await breed();
  const trained = await t.readAccount();
  trained.Run.Sp = 100;
  await t.writeSaveText(H.encodeSave(trained));
  await t.reload();
  await act('tab-trees').click();
  for (let i = 0; i < 8; i++) await act('buy-2').click();
  await act('lineage-choice-nominate_hp').click();
  const chosen = await t.readAccount();
  check(chosen.Run.LineageTraining && chosen.Run.LineageTraining.Stat === 'hp',
    'an inherited training power returns success and persists the selected stat');
  check(!(chosen.Account.Lineages.heirloom.Points || 0), 'choosing a power grants no lineage points');
  await t.reload();
  await act('tab-trees').click();
  check((await t.readAccount()).Run.LineageTraining.Stat === 'hp',
    'the milestone command is saved through the account checkpoint and survives reload');
  check(await act('lineage-choice-nominate_hp').count() === 0, 'the selected power cannot be nominated twice');
  const powerSummary = await text('[data-id=lineage-power-summary]');
  check(/Health/i.test(powerSummary), 'the selected power is summarized in player-facing terms: ' + powerSummary);
  await t.layout('inherited training power selected');
  await t.finish();
})().catch((e) => { console.error(e); process.exit(1); });
