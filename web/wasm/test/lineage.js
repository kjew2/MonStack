#!/usr/bin/env node
// Generic I–X lineages (specs/30): Workshop UI breeding, focus-node progress, sealing and reload,
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
    await act('lineage-picker-toggle').click();
    check(await page.locator('[data-id=lineage-search]').evaluate((e) => e === document.activeElement),
      'opening the focus-tree picker puts keyboard focus in search');
    await act('lineage-' + tree).click();
    check(await act('lineage-picker-toggle').evaluate((e) => e === document.activeElement),
      'choosing a focus tree returns keyboard focus to the picker control');
  }
  const selectedParent = (key) => page.locator('[data-id=breed-' + key + '-card]').getAttribute('data-parent-index');
  async function chooseParent(key, index) {
    await act('breed-' + key).click();
    await page.locator('[data-id=parent-picker]').waitFor();
    await act('pick' + key + '-' + index).click();
    check(await page.locator('[data-id=parent-picker]').count() === 0, 'choosing a ' + key + ' card closes the roster');
  }
  async function breed() {
    await t.layout('breeding parents');
    await chooseParent('primary', '0');
    check(await act('breed-primary').evaluate((e) => e === document.activeElement), 'choosing a card returns keyboard focus to its parent slot');
    check(await act('breed-review').isDisabled(), 'review needs two different parents');
    await chooseParent('support', '1');
    check(await act('breed-review').isEnabled(), 'two eligible parents enable offspring review');
    await act('breed-review').click();
    check(await act('breed').isEnabled(), 'the offspring review enables paid breeding');
    await act('breed-back').click();
    check(await selectedParent('primary') === '0' && await selectedParent('support') === '1',
      'returning from review keeps the selected parents for adjustment');
    await act('breed-review').click();
    const reviewVisible = await page.locator('[data-id=breed-offspring-preview]').evaluate((e) => {
      document.getElementById('main').scrollTop = 0;
      const preview = e.getBoundingClientRect();
      const action = document.querySelector('[data-act=breed]').getBoundingClientRect();
      return preview.top >= 0 && preview.bottom <= action.top;
    });
    check(reviewVisible, 'the phone review shows the offspring preview above its confirmation action without scrolling');
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
  check(await page.locator('[data-id=breeding-workshop]').isVisible(), 'Breed opens the parent-focused Breeding Workshop');
  for (const card of ['breed-primary-card', 'breed-support-card', 'breed-offspring-preview']) {
    check(await page.locator('[data-id=' + card + ']').isVisible(), 'the workshop presents ' + card + ' before breeding');
  }
  check(await page.locator('[data-id=lineage-search]').count() === 0, 'the tree library stays inside the focus-tree picker');
  const foreground = await page.locator('[data-id=breed-offspring-preview] h2').evaluate((e) => getComputedStyle(e).color);
  const textChannels = (foreground.match(/[\d.]+/g) || []).slice(0, 3).map(Number);
  check(textChannels.length === 3 && textChannels.every((n) => n >= 180),
    'Workshop headings use a readable light foreground on their dark cards: ' + foreground);
  await act('account').click();
  const accountColors = await page.locator('#account-sheet').evaluate((e) => {
    const s = getComputedStyle(e); return { background: s.backgroundColor, color: s.color };
  });
  const channels = (value) => (value.match(/[\d.]+/g) || []).slice(0, 3).map(Number);
  check(channels(accountColors.background).every((n) => n < 80) && channels(accountColors.color).every((n) => n >= 180),
    'the account overlay keeps a dark surface and readable foreground within Workshop');
  await t.layout('workshop account overlay');
  await act('account-close').click();
  await act('hometab-raise').click();
  check(!(await page.locator('#app').getAttribute('class') || '').split(/\s+/).includes('workshop') && await page.locator('#app').evaluate((e) => parseFloat(getComputedStyle(e).maxWidth)) <= 560,
    'returning to Raise restores the standard game theme and column');
  await openBreeding();
  await t.layout('breeding workshop phone');
  await page.setViewportSize({ width: 1280, height: 900 });
  const desktopCards = await page.locator('[data-id=breed-primary-card], [data-id=breed-support-card]').evaluateAll((cards) => cards.map((card) => {
    const r = card.getBoundingClientRect(); return { top: r.top, left: r.left, right: r.right };
  }));
  check(desktopCards.length === 2 && Math.abs(desktopCards[0].top - desktopCards[1].top) < 5 && desktopCards[1].left >= desktopCards[0].right,
    'desktop places the primary and supporting parent cards side by side');
  await t.shot('breeding-workshop-desktop');
  await page.setViewportSize(H.PHONE);
  check(await act('breed-support').isDisabled(), 'partner browsing starts after choosing a primary parent');
  check(await page.locator('[data-id=breed-primary-card] select, [data-id=breed-support-card] select').count() === 0,
    'parent selection uses monster cards instead of dropdowns');
  const beforeBrowsing = JSON.stringify(await t.readAccount());
  await act('breed-primary').click();
  check(await act('parent-picker-close').evaluate((e) => e === document.activeElement), 'opening the roster focuses a dialog control without summoning a phone keyboard');
  await act('parent-filter-all').click();
  check(await act('pickprimary-0').isEnabled() && await act('pickprimary-1').isDisabled(), 'the roster shows eligible monsters and disables parents without the focus skill');
  check(/skill/i.test(await text('[data-id=parent-option-1]')), 'an unavailable monster explains its eligibility requirement');
  await page.locator('[data-id=parent-search]').fill('not-a-monster');
  check(await page.locator('[data-id^=parent-option-]').count() === 0, 'roster search supports an empty result');
  await page.locator('[data-id=parent-search]').fill('Coremote');
  check(await page.locator('[data-id^=parent-option-]').count() === 2, 'roster search finds species names');
  check(!/Fortune IV/.test(await text('[data-id=parent-option-0]')), 'parent cards show actual inherited ranks rather than account-wide rank');
  await act('parent-picker-close').focus();
  await page.keyboard.press('Shift+Tab');
  check(await page.locator('[data-id=parent-picker]').evaluate((e) => e.contains(document.activeElement)), 'keyboard focus wraps inside the roster');
  await page.keyboard.press('Tab');
  check(await act('parent-picker-close').evaluate((e) => e === document.activeElement), 'forward Tab returns to the first roster control');
  await t.layout('primary monster card roster');
  await page.keyboard.press('Escape');
  check(await page.locator('[data-id=parent-picker]').count() === 0 && await selectedParent('primary') === '', 'Escape cancels parent browsing without choosing a monster');
  check(JSON.stringify(await t.readAccount()) === beforeBrowsing, 'browsing and cancelling make no changes to the saved account');
  await chooseParent('primary', '0');
  await act('breed-support').click();
  check(/Coremote/.test(await text('[data-id=parent-selected-primary]')) && /Coremote/.test(await text('[data-id=parent-offspring-preview]')), 'partner browsing pins the primary parent and previews its offspring');
  await act('parent-filter-all').click();
  check(await act('picksupport-0').isDisabled() && await act('picksupport-1').isEnabled(), 'the first parent cannot be its own partner');
  await t.layout('partner monster card roster');
  await act('parent-picker-close').click();
  check(await selectedParent('support') === '', 'closing partner browsing leaves the pairing unchanged');
  await chooseParent('support', '1');
  check(await act('breed-review').isEnabled(), 'the workshop can prepare an eligible pairing');
  check(/Coremote/.test(await text('[data-id=breed-offspring-preview]')) && /Fortune IV/.test(await text('[data-id=breed-offspring-preview]')),
    'the live offspring preview shows the primary species and frozen inheritance rank');
  await act('lineage-picker-toggle').click();
  await act('lineage-fortune').click();
  check(await selectedParent('primary') === '0' && await selectedParent('support') === '1',
    'choosing the current focus tree retains the prepared pairing');
  await act('lineage-picker-toggle').click();
  check(await page.locator('[data-id=lineage-library] button').count() === 40, 'the focus-tree picker contains all 40 generic trees');
  await page.locator('[data-id=lineage-search]').fill('brute');
  check(await page.locator('[data-id=lineage-library] button').count() === 1, 'search narrows the library by tree name');
  await page.locator('[data-id=lineage-search]').fill('');
  await act('lineage-brute_force').click();
  check(await selectedParent('primary') === '' && await selectedParent('support') === '',
    'switching focus trees clears the previous pairing');
  check(await act('breed-review').isDisabled() && await page.locator('[data-id=lineage-no-parent]').isVisible(),
    'an unlearned focus tree explains parent eligibility and cannot advance to review');
  await act('lineage-picker-toggle').click();
  await act('lineage-fortune').click();
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
  // A full Vault must stay a browsable roster rather than pushing its controls off-screen.
  const crowded = await t.readAccount();
  crowded.Run = null;
  crowded.Account.RunsStarted = 50;
  crowded.Account.Vault = Array.from({ length: 50 }, (_, id) => Object.assign(JSON.parse(JSON.stringify(parent)), {
    Id: id, Trees: [{ TreeId: 'fortune', Bought: 2 }],
  }));
  await t.writeSaveText(H.encodeSave(crowded));
  await t.reload();
  await openBreeding();
  await act('breed-primary').click();
  check(await page.locator('[data-id^=parent-option-]').count() === 50, 'a full Vault exposes every compatible monster card');
  const scrolls = await page.locator('[data-id=parent-roster]').evaluate((e) => e.scrollHeight > e.clientHeight && e.clientHeight > 0);
  check(scrolls, 'the full Vault scrolls inside its bounded roster');
  await t.layout('full Vault parent roster');
  await act('pickprimary-0').click();
  check(await selectedParent('primary') === '0', 'the oldest monster remains selectable at the end of a full roster');
  await chooseParent('support', '49');
  await chooseParent('primary', '49');
  check(await selectedParent('support') === '' && await act('breed-review').isDisabled(), 'changing primary clears a conflicting partner and requires a new pairing');
  await t.finish();
})().catch((e) => { console.error(e); process.exit(1); });
