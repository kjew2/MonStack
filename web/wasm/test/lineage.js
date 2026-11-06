#!/usr/bin/env node
// Consumptive, monster-held I–X synthesis (specs/30): real UI selection, two inheritances,
// personal protected breakthroughs, save/reload, secondary choices and phone layout.
'use strict';
const H = require('./harness.js');

(async () => {
  const t = await H.open({ shotsDir: H.shotsArg() });
  const { page, check, act, text } = t;
  const account = async () => (await t.readAccount()).Account;
  const selectedParent = (key) => page.locator('[data-id=breed-' + key + '-card]').getAttribute('data-parent-index');
  const parent = {
    SpeciesId: 'slime', Grade: 'e', End: 'Completed', Cycles: 12,
    Stats: { Hp: 200, Str: 100, Int: 100, Wis: 100, Dex: 100, Def: 100, Spd: 100, Luk: 100 }, Trees: [],
  };
  const monster = (id, tree, progress, extra) => Object.assign(JSON.parse(JSON.stringify(parent)), {
    Id: id, Trees: [{ TreeId: tree, Bought: tree === 'fortune' ? 2 : 5 }], Lineages: { [tree]: progress || { Rank: 1 } },
  }, extra || {});
  async function install(entries, extra) {
    const save = await t.readAccount();
    save.Run = null;
    save.SchemaVersion = 15; save.Account.SchemaVersion = 15;
    delete save.Account.Fortuna;
    save.Account.Lineages = {};
    save.Account.Vault = entries;
    save.Account.RunsStarted = Math.max(...entries.map((e) => e.Id)) + 1;
    save.Account.Shards = 10000;
    save.Account.Rulebooks = {};
    if (extra) Object.assign(save.Account, extra);
    await t.writeSaveText(H.encodeSave(save));
    await t.reload();
  }
  async function openBreeding(tree = 'fortune') {
    await act('open-lineages').click();
    await act('lineage-picker-toggle').click();
    check(await page.locator('[data-id=lineage-search]').evaluate((e) => e === document.activeElement), 'opening the tree picker focuses search');
    await act('lineage-' + tree).click();
    check(await act('lineage-picker-toggle').evaluate((e) => e === document.activeElement), 'choosing a focus tree returns keyboard focus');
  }
  async function chooseParent(key, index) {
    await act('breed-' + key).click();
    await page.locator('[data-id=parent-picker]').waitFor();
    await act('pick' + key + '-' + index).click();
    check(await page.locator('[data-id=parent-picker]').count() === 0, 'choosing a ' + key + ' card closes the roster');
  }
  async function synthesize(primary = 0, support = 1) {
    await chooseParent('primary', String(primary));
    check(await act('breed-primary').evaluate((e) => e === document.activeElement), 'choosing a card returns focus to its parent slot');
    await chooseParent('support', String(support));
    check(await act('breed-review').isEnabled(), 'two parents with distinct tree contributions enable review');
    const before = JSON.stringify(await t.readAccount());
    await act('breed-review').click();
    check(await act('breed').isEnabled(), 'the offspring review enables synthesis');
    check(/consum|sacrific|leave your Vault/i.test(await text('[data-id=breed-offspring-preview]') + await text('.workshop-cost')),
      'review makes the parent sacrifice explicit');
    check(JSON.stringify(await t.readAccount()) === before, 'review does not consume parents or spend shards');
    await act('breed-back').click();
    check(await selectedParent('primary') === String(primary) && await selectedParent('support') === String(support), 'review back keeps both selected parents');
    await act('breed-review').click();
    const visible = await page.locator('[data-id=breed-offspring-preview]').evaluate((e) => {
      document.getElementById('main').scrollTop = 0;
      const r = e.getBoundingClientRect(), b = document.querySelector('[data-act=breed]').getBoundingClientRect();
      return r.top >= 0 && r.bottom <= b.top;
    });
    check(visible, 'phone review shows both inherited trees above its confirmation without scrolling');
    await t.layout('offspring synthesis review');
    await act('breed').click();
    await act('drill-str').waitFor();
  }
  async function leaveGrade() {
    if (await act('to-vendor').count()) { await act('to-vendor').click(); await act('vendor-done').click(); }
    else await act('new-run').click();
    await act('start').waitFor();
  }
  async function playToSeal() { return page.evaluate(async () => {
    const q = (s) => document.querySelector(s);
    const click = (s) => { const e = q(s); if (!e || e.disabled) return false; e.click(); return true; };
    for (let i = 0; i < 1500; i++) {
      if (q('#sync-wait')) { await new Promise((r) => setTimeout(r, 20)); continue; }
      if (q('[data-id=fight-play]') || q('[data-id=fight-intro]')) { click('[data-act="speed-skip"]'); continue; }
      if (click('[data-act="fight-continue"]')) continue;
      if (q('[data-id=offer-title]')) { click('[data-act="discard"]'); continue; }
      click('[data-act="tab-trees"]');
      while (click('[data-act="buy-2"]')) { /* only the primary focus earns lineage points */ }
      click('[data-act="tab-train"]');
      if (q('[data-act="seal"]')) return true;
      if (click('[data-act="fight"]')) continue;
      click('[data-act="rest"]');
    }
    return false;
  }); }

  await page.goto(t.url);
  await act('start').waitFor({ timeout: 90000 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  const support = monster(1, 'brute_force', { Rank: 2, Points: 3 });
  support.Trees.push({ TreeId: 'vital_core', Bought: 5 });
  support.Lineages.vital_core = { Rank: 3, Points: 2 };
  await install([
    monster(0, 'fortune', { Rank: 4, Points: 44, Failures: 4, Attempts: 4 }), support,
    monster(2, 'fortune', { Rank: 2, Points: 7 }, { Locked: true }),
  ], { Rulebooks: { 0: '', 1: '', 2: '' } });
  const unrelatedFortune = JSON.stringify((await account()).Vault.find((e) => e.Id === 2).Lineages.fortune);
  await openBreeding();
  check(await page.locator('[data-id=breeding-workshop]').isVisible(), 'Breed opens the parent-focused synthesis workshop');
  check(await page.locator('[data-id=lineage-search]').count() === 0, 'the tree library stays inside its picker');
  for (const id of ['breed-primary-card', 'breed-support-card', 'breed-offspring-preview']) check(await page.locator('[data-id=' + id + ']').isVisible(), 'the workshop presents ' + id);
  check(await act('breed-support').isDisabled(), 'partner browsing requires a primary parent first');
  check(await page.locator('[data-id=breed-primary-card] select, [data-id=breed-support-card] select').count() === 0, 'parents use cards instead of dropdowns');
  const foreground = await page.locator('[data-id=breed-offspring-preview] h2').evaluate((e) => getComputedStyle(e).color);
  const channels = (value) => (value.match(/[\d.]+/g) || []).slice(0, 3).map(Number);
  check(channels(foreground).every((n) => n >= 180), 'dark workshop headings have a readable foreground');
  await act('account').click();
  const colors = await page.locator('#account-sheet').evaluate((e) => ({ background: getComputedStyle(e).backgroundColor, color: getComputedStyle(e).color }));
  check(channels(colors.background).every((n) => n < 80) && channels(colors.color).every((n) => n >= 180), 'account overlays retain workshop contrast');
  await t.layout('synthesis account overlay');
  await act('account-close').click();
  await act('hometab-raise').click();
  check(!(await page.locator('#app').getAttribute('class') || '').split(/\s+/).includes('workshop') && await page.locator('#app').evaluate((e) => parseFloat(getComputedStyle(e).maxWidth)) <= 560, 'leaving synthesis restores the normal theme and width');
  await openBreeding();
  await t.layout('synthesis workshop phone');
  await page.setViewportSize({ width: 1280, height: 900 });
  const cards = await page.locator('[data-id=breed-primary-card], [data-id=breed-support-card]').evaluateAll((entries) => entries.map((e) => { const r = e.getBoundingClientRect(); return { top: r.top, left: r.left, right: r.right }; }));
  check(cards.length === 2 && Math.abs(cards[0].top - cards[1].top) < 5 && cards[1].left >= cards[0].right, 'desktop places the two parent cards side by side');
  await t.shot('synthesis-desktop');
  await page.setViewportSize(H.PHONE);
  const beforeBrowsing = JSON.stringify(await t.readAccount());
  await act('breed-primary').click();
  check(await act('parent-picker-close').evaluate((e) => e === document.activeElement), 'roster opening focuses a control without summoning the phone keyboard');
  await act('parent-filter-all').click();
  check(await act('pickprimary-0').isEnabled() && await act('pickprimary-1').isDisabled(), 'primary eligibility follows the selected inherited tree');
  check(/skill|carry|inherit/i.test(await text('[data-id=parent-option-1]')), 'an unavailable monster explains its tree requirement');
  check(await act('pickprimary-2').isDisabled() && /lock/i.test(await text('[data-id=parent-option-2]')), 'locked monsters cannot be sacrificed');
  await page.locator('[data-id=parent-search]').fill('not-a-monster');
  check(await page.locator('[data-id^=parent-option-]').count() === 0, 'roster search has an empty result');
  await page.locator('[data-id=parent-search]').fill('Coremote');
  check(await page.locator('[data-id^=parent-option-]').count() === 3, 'roster search finds species names');
  check(/Fortune IV/.test(await text('[data-id=parent-option-0]')), 'parent cards show their own carried lineage rank');
  await act('parent-picker-close').focus();
  await page.keyboard.press('Shift+Tab');
  check(await page.locator('[data-id=parent-picker]').evaluate((e) => e.contains(document.activeElement)), 'focus wraps inside the roster');
  await page.keyboard.press('Tab');
  check(await act('parent-picker-close').evaluate((e) => e === document.activeElement), 'forward Tab returns to the first control');
  await t.layout('synthesis primary roster');
  await page.keyboard.press('Escape');
  check(await page.locator('[data-id=parent-picker]').count() === 0 && await selectedParent('primary') === '', 'Escape cancels without choosing a parent');
  check(JSON.stringify(await t.readAccount()) === beforeBrowsing, 'browsing and cancelling leave the account unchanged');
  await chooseParent('primary', '0');
  check(/Fortune IV/.test(await text('[data-id=lineage-detail]')) && /100%/.test(await text('[data-id=lineage-odds]')), 'detail uses the selected monster’s protected IV progress');
  await act('breed-support').click();
  check(/Coremote/.test(await text('[data-id=parent-selected-primary]')) && /Coremote/.test(await text('[data-id=parent-offspring-preview]')), 'partner browsing pins the primary and previews offspring');
  await act('parent-filter-all').click();
  check(await act('picksupport-0').isDisabled() && await act('picksupport-1').isEnabled() && await act('picksupport-2').isDisabled(), 'partner eligibility excludes the primary and locked monsters');
  await t.layout('synthesis partner roster');
  await act('parent-picker-close').click();
  check(await selectedParent('support') === '', 'closing partner browsing makes no selection');
  await chooseParent('support', '1');
  check(await act('support-tree-brute_force').count() === 1 && await act('support-tree-vital_core').count() === 1, 'a partner with two eligible trees offers an explicit inheritance choice');
  await act('support-tree-vital_core').click();
  check(/Vital Core III/i.test(await text('[data-id=inherited-support-tree]')), 'choosing the supporting tree updates its carried rank preview');
  await act('support-tree-brute_force').click();
  check(/Brute Force II/.test(await text('[data-id=inherited-support-tree]')), 'the partner contributes a distinct ranked tree');
  await act('lineage-picker-toggle').click();
  await act('lineage-fortune').click();
  check(await selectedParent('primary') === '0' && await selectedParent('support') === '1', 'reselecting the same focus preserves the pairing');
  await act('lineage-picker-toggle').click();
  check(await page.locator('[data-id=lineage-library] button').count() === 40, 'the library contains all 40 generic trees');
  await page.locator('[data-id=lineage-search]').fill('brute');
  check(await page.locator('[data-id=lineage-library] button').count() === 1, 'tree search narrows the catalogue');
  await page.locator('[data-id=lineage-search]').fill('');
  await act('lineage-brute_force').click();
  check(await selectedParent('primary') === '' && await selectedParent('support') === '', 'changing focus clears the pairing');
  await act('lineage-picker-toggle').click(); await act('lineage-fortune').click();
  check(await page.locator('.lineage-track li').count() === 10 && await page.locator('.lineage-milestone').count() === 2, 'the selected tree has I–X progression and V/X powers');
  await synthesize();
  const born = await t.readAccount();
  check(born.Run.Lineage.Rank === 4 && born.Run.Slots[2].TreeId === 'fortune' && !(born.Run.Slots[2].Bought || 0), 'the offspring captures focus Fortune IV with zero nodes');
  check(born.Run.Slots[3].TreeId === 'brute_force' && born.Run.Slots[3].Roll.LineageRank === 2 && !(born.Run.Slots[3].Bought || 0), 'the supporting parent supplies Brute Force II in slot 4 with zero nodes');
  check(born.Run.Lineage.Progress.fortune.Points === 44 && born.Run.Lineage.Progress.brute_force.Points === 3, 'both personal progress records transfer without adding ranks');
  check(born.Account.Vault.length === 1 && born.Account.Vault[0].Id === 2 && born.Account.Shards === 9950, 'confirmed synthesis consumes exactly both parents and pays 50 shards');
  check(Object.keys(born.Account.Rulebooks || {}).join(',') === '2', 'consumed parents’ rulebooks are removed while unrelated rulebooks remain');
  check(Object.keys(born.Account.Lineages || {}).length === 0, 'new personal inheritance creates no account-wide progress');
  await act('tab-trees').click(); await act('buy-2').click();
  check(/1 \/ 20 points/.test(await text('[data-id=run-lineage]')), 'the first focus node records one point for sealing');
  await act('tab-train').click(); await t.reload();
  const resumed = (await t.readAccount()).Run;
  check(resumed.Lineage.Rank === 4 && resumed.Lineage.Deepest === 1 && resumed.Lineage.Progress.fortune.Points === 44 && resumed.Slots[3].Roll.LineageRank === 2, 'reload preserves both inheritances, birth rank and high-water mark');
  check(await playToSeal(), 'the descendant finishes a complete training run');
  check((await t.readAccount()).Run.Slots[2].Bought === 8, 'the focus reaches its capstone');
  await act('seal').click();
  const sealed = await account(), child = sealed.Vault.find((e) => e.Id === 3), progressed = child.Lineages.fortune;
  check(progressed.Rank === 5 && progressed.LastAward === 20 && progressed.Points === 19 && progressed.Attempts === 5 && !(progressed.Failures || 0), 'the child’s protected V breakthrough retains its 19-point overflow');
  check(child.Lineage.Rank === 4 && child.Trees.find((x) => x.TreeId === 'fortune').Roll.LineageRank === 4, 'the sealed fighter retains its trained birth rank IV');
  check(child.Lineages.brute_force.Rank === 2 && child.Lineages.brute_force.Points === 3, 'the supporting inheritance receives no focus award');
  check(JSON.stringify(sealed.Vault.find((e) => e.Id === 2).Lineages.fortune) === unrelatedFortune, 'another monster’s Fortune progress is untouched');
  await act('climb-continue').click(); await page.locator('[data-id=grade]').waitFor();
  check(/\+20 Fortune lineage points/.test(await text('[data-id=grade-lineage]')) && /Fortune V.*unlocked/.test(await text('[data-id=grade-lineage-result]')), 'grade feedback announces this monster’s personal V breakthrough');
  await t.layout('personal V breakthrough grade'); await leaveGrade();
  const next = await t.readAccount();
  next.Account.Vault.push(monster(4, 'brute_force', { Rank: 1 })); next.Account.RunsStarted = 5;
  await t.writeSaveText(H.encodeSave(next)); await t.reload(); await openBreeding(); await chooseParent('primary', '1');
  check(/Fortune V/.test(await text('[data-id=lineage-detail]')), 'the child’s inherited V survives a reload');
  await synthesize(1, 2); await act('tab-trees').click();
  check(/Fortune V/.test(await text('[data-id=slot-2]')) && /Capstone:.*bank/i.test(await text('[data-id=slot-2]')), 'the next generation equips V and presents its capstone effect');
  await act('tab-log').click(); await act('abandon').click();
  check(/parents will not return/i.test(await text('#main')), 'abandon warns that sacrificed parents will not return');
  await act('abandon-yes').click();
  check((await account()).Vault.length === 1 && (await account()).Vault[0].Id === 2, 'abandoning a synthesized run never refunds its parents');

  // A separate bloodline reaches X; another monster’s Fortune ledger remains isolated.
  await install([
    monster(10, 'brute_force', { Rank: 9, Points: 349, Failures: 5, Attempts: 5 }),
    monster(11, 'fortune', { Rank: 1 }),
    monster(12, 'fortune', { Rank: 5, Points: 19 }, { Locked: true }),
  ]);
  const kept = JSON.stringify((await account()).Vault[2].Lineages.fortune);
  await openBreeding('brute_force'); await chooseParent('primary', '0');
  check(/Brute Force IX/.test(await text('[data-id=lineage-detail]')) && /100%/.test(await text('[data-id=lineage-odds]')), 'the selected Brute Force bloodline exposes its protected X attempt');
  await synthesize(); check(await playToSeal(), 'the Brute Force descendant completes training'); await act('seal').click();
  const xAccount = await account(), xChild = xAccount.Vault.find((e) => e.Id === 13);
  check(xChild.Lineages.brute_force.Rank === 10 && xChild.Lineages.brute_force.Points === 19 && xChild.Lineages.brute_force.LastAward === 20, 'personal X progression retains overflow');
  check(JSON.stringify(xAccount.Vault.find((e) => e.Id === 12).Lineages.fortune) === kept, 'the X breakthrough does not change another monster’s Fortune');
  await act('climb-continue').click(); await page.locator('[data-id=grade]').waitFor();
  check(/Brute Force X/.test(await text('[data-id=grade-lineage-result]')), 'the grade names the personal X breakthrough');
  await leaveGrade(); await t.reload(); await openBreeding('brute_force'); await chooseParent('primary', '1');
  check(/Rank X complete/.test(await text('[data-id=lineage-progress]')), 'the completed personal X ledger survives reload');

  // Both primary and secondary training powers must acknowledge, save and reload their choices.
  for (const secondary of [false, true]) {
    await install(secondary ? [monster(20, 'fortune', { Rank: 1 }), monster(21, 'heirloom', { Rank: 5 })]
      : [monster(20, 'heirloom', { Rank: 5 }), monster(21, 'brute_force', { Rank: 1 })]);
    await openBreeding(secondary ? 'fortune' : 'heirloom'); await synthesize();
    const training = await t.readAccount(); training.Run.Sp = 100;
    await t.writeSaveText(H.encodeSave(training)); await t.reload(); await act('tab-trees').click();
    const slot = secondary ? 3 : 2;
    for (let i = 0; i < 8; i++) await act('buy-' + slot).click();
    const action = 'lineage-choice-' + (secondary ? 'heirloom:' : '') + 'nominate_hp';
    await act(action).click();
    const chosen = (await t.readAccount()).Run;
    check((secondary ? chosen.SecondaryLineageTraining : chosen.LineageTraining).Stat === 'hp', (secondary ? 'secondary' : 'focus') + ' Heirloom choice returns success and persists');
    check(!(chosen.Lineage.Progress.heirloom.Points || 0), 'training choices grant no lineage points');
    await t.reload(); await act('tab-trees').click();
    const loaded = (await t.readAccount()).Run;
    check((secondary ? loaded.SecondaryLineageTraining : loaded.LineageTraining).Stat === 'hp' && await act(action).count() === 0, 'the selected ' + (secondary ? 'secondary' : 'focus') + ' power survives reload and cannot repeat');
    check(/Health/i.test(await text('[data-id=lineage-power-summary]')), 'the chosen power appears in the combined player-facing summary');
    await t.layout(secondary ? 'secondary training power' : 'focus training power');
  }
  // Opening an old account assigns earned progress to its strongest eligible carrier,
  // while the workshop shows an unmatched reserve without changing the old battle build.
  const legacy = await t.readAccount();
  legacy.Run = null; legacy.SchemaVersion = 14; legacy.Account.SchemaVersion = 14;
  legacy.Account.RunsStarted = 63; legacy.Account.Shards = 10000; legacy.Account.Rulebooks = {};
  legacy.Account.Fortuna = { Rank: 4, Points: 44, Failures: 4, Attempts: 4 };
  legacy.Account.Lineages = { fortune: { ...legacy.Account.Fortuna }, vital_core: { Rank: 3, Points: 2 } };
  legacy.Account.Vault = [monster(60, 'fortune'), monster(61, 'fortune'), monster(62, 'brute_force')];
  for (const entry of legacy.Account.Vault) delete entry.Lineages;
  legacy.Account.Vault[1].Stats.Hp = 400;
  await t.writeSaveText(H.encodeSave(legacy)); await t.reload();
  const migrated = await account();
  check(migrated.Vault[1].Lineages.fortune.Rank === 4 && migrated.Vault[1].Lineages.fortune.Points === 44
    && !((migrated.Vault[0].Lineages || {}).fortune), 'old progress migrates to the strongest eligible carrier only');
  check(!(migrated.Vault[1].Trees[0].Roll || {}).LineageRank, 'migration leaves the carrier’s trained battle tree unchanged');
  check(!((migrated.Lineages || {}).fortune) && migrated.Lineages.vital_core.Rank === 3, 'matched legacy progress is assigned once and unmatched trees stay reserved');
  await openBreeding(); await chooseParent('primary', '1');
  check(/Fortune IV/.test(await text('[data-id=lineage-detail]')) && /1 unmatched legacy lineage/.test(await text('[data-id=lineage-reserve-summary]')),
    'the workshop presents the migrated personal rank and unmatched reserve');
  await t.layout('migrated synthesis workshop');

  const crowded = Array.from({ length: 50 }, (_, id) => monster(id, 'fortune', { Rank: 1 }));
  crowded.forEach((e) => { e.Trees.push({ TreeId: 'brute_force', Bought: 5 }); e.Lineages.brute_force = { Rank: 1 }; });
  await install(crowded); await openBreeding(); await act('breed-primary').click();
  check(await page.locator('[data-id^=parent-option-]').count() === 50, 'a full Vault exposes all 50 compatible cards');
  check(await page.locator('[data-id=parent-roster]').evaluate((e) => e.scrollHeight > e.clientHeight && e.clientHeight > 0), 'the full Vault scrolls inside the bounded roster');
  await t.layout('full synthesis Vault roster'); await act('pickprimary-0').click();
  check(await selectedParent('primary') === '0', 'the oldest monster remains selectable');
  await chooseParent('support', '49'); await chooseParent('primary', '49');
  check(await selectedParent('support') === '' && await act('breed-review').isDisabled(), 'a conflicting partner clears when the primary changes');
  await t.finish();
})().catch((e) => { console.error(e); process.exit(1); });
