#!/usr/bin/env node
// Browser checks of the gambit rulebook editor (web/rulebook.js, specs/28 section 4.5) at phone width 390x844: the Vault detail's
// Rulebook button; rule cards built through the pickers (condition, value, action, target), reordered, deleted and capped at the
// slot count; Save stores the canonical text in the save and survives a reload; Clear all removes it; a monster with no id cannot
// hold one; Escape closes the picker, then the sheet; the Test preview plays a floor back with the acting rule's card lit and
// pays nothing. Fails (exit 1) on any page error, console error or failed assertion.
// Usage: node web/wasm/test/rulebook.js [--shots dir]   (screenshots are debug output, never compared)
'use strict';
const H = require('./harness.js');

(async () => {
  const t = await H.open({ shotsDir: H.shotsArg() });
  const { page, check, act } = t;
  const sheetOpen = () => page.evaluate(() => RulebookView.isOpen());
  const ruleCount = () => page.locator('[data-act^="rb-rule-"]').count();
  const rulebooks = async () => (await t.readAccount()).Account.Rulebooks || {};

  // ---- one run to its end, sealed, back home
  await page.goto(t.url);
  await act('start').waitFor({ timeout: 90000 });
  await page.locator('[data-act^="species-"]:not([disabled])').first().click();
  await act('start').click();
  await act('drill-str').waitFor();
  await t.playUntil('end');
  if (await act('fight-continue').count()) await act('fight-continue').click();
  await page.emulateMedia({ reducedMotion: 'reduce' });   // the gauntlet wheel lands at once
  await act('seal').click();
  await page.locator('[data-id=climb-reel]').waitFor();
  await act('climb-continue').click();
  await page.locator('[data-id=grade]').waitFor();
  await act('to-vendor').click();
  await page.locator('[data-id=vendor-offer]').first().waitFor();
  await act('vendor-done').click();
  await act('start').waitFor();
  await page.emulateMedia({ reducedMotion: 'no-preference' });

  // ---- a Vault of four copies of the sealed monster, ids 0..3 (save edit)
  const acct = await t.readAccount();
  const base = acct.Account.Vault[0];
  acct.Account.Vault = [0, 1, 2, 3].map((id) => Object.assign(JSON.parse(JSON.stringify(base)), { Id: id }));
  await t.writeSaveText(H.encodeSave(acct));
  await t.reload();
  const shards0 = (await t.readAccount()).Account.Shards || 0;

  // ---- the Vault detail and its Rulebook button
  await act('hometab-vault').click();
  await page.locator('[data-id=vault-entry]').first().waitFor();
  await act('detail-1').click();
  await t.layout('Vault detail');
  check(await act('detail-rulebook').isVisible() && (await act('detail-rulebook').innerText()) === 'Rulebook: none', 'the Vault detail shows "Rulebook: none"');
  await act('detail-rulebook').click();
  await act('rb-sheet').waitFor();
  check(await sheetOpen(), 'the rulebook editor opens');
  const meta = await page.evaluate(() => { const c = RulebookView.lastCtx; return { slots: c.C.gambits.slots, skills: c.run.account.vault[1].skills }; });
  check(meta.slots >= 3, 'the content carries the slot count (' + meta.slots + ')');
  check((await ruleCount()) === 0 && (await act('rb-test').count()) === 0, 'no rules yet, and no Test button without a floor');
  check(await page.locator('[data-act=rb-sheet]').getAttribute('role') === 'dialog' && await page.locator('[data-act=rb-sheet]').getAttribute('aria-modal') === 'true', 'the sheet is a modal dialog');

  // ---- rule 1 through the pickers: enemy_hp_below 40% > a skill (or basic) @ lowest_hp
  await act('rb-add').click();
  check((await ruleCount()) === 1 && (await act('rb-cond-0').innerText()) === 'Always' && (await act('rb-value-0').count()) === 0, 'a new rule is "always > basic", with no value chip');
  check((await act('rb-cond-0').getAttribute('aria-label')) === 'Condition: Always', 'chips carry an aria-label');
  await act('rb-cond-0').click();
  await page.locator('[data-id=rb-picker]').waitFor();
  await t.layout('rulebook condition picker');
  await act('rb-pick-enemy_hp_below').click();
  check((await page.locator('[data-id=rb-picker]').count()) === 0 && (await act('rb-value-0').innerText()) === '50%', 'picking a condition that takes a fraction adds a value chip (50%)');
  await act('rb-value-0').click();
  await act('rb-pick-0.4').click();
  check((await act('rb-value-0').innerText()) === '40%', 'the value picker sets 40%');
  const skill = meta.skills[0] || 'basic';
  await act('rb-action-0').click();
  check((await page.locator('[data-id=rb-picker] [data-act="rb-pick-basic"]').count()) === 1 &&
    (await page.locator('[data-id=rb-picker] .rbinfo[data-tip]').count()) === meta.skills.length, 'the action picker lists the basic attack and each skill with a tooltip (' + meta.skills.length + ' skills)');
  if (meta.skills.length) {
    await page.locator('[data-id=rb-picker] .rbinfo[data-tip]').first().click();
    check(await page.locator('#tip').isVisible(), 'a skill\'s info button opens its tooltip');
    await page.keyboard.press('Escape');
    check(!(await page.locator('#tip').isVisible()) && (await page.locator('[data-id=rb-picker]').count()) === 1, 'Escape closes the tooltip first, the picker stays');
  }
  await act('rb-pick-' + skill).click();
  await act('rb-target-0').click();
  await act('rb-pick-lowest_hp').click();
  const rule1 = 'enemy_hp_below:0.4>' + skill + '@lowest_hp';

  // ---- rules 2 and 3, reorder, delete, the cap
  await act('rb-add').click();                       // rule 2: always > basic
  await act('rb-add').click();                       // rule 3: first_actions 2 > basic @ highest_hp
  await act('rb-cond-2').click();
  await act('rb-pick-first_actions').click();
  check((await act('rb-value-2').innerText()) === '1', 'a count condition starts at 1');
  await act('rb-value-2').click();
  await act('rb-pick-2').click();
  await act('rb-target-2').click();
  await act('rb-pick-highest_hp').click();
  check(await act('rb-up-0').isDisabled() && await act('rb-down-2').isDisabled() && !(await act('rb-up-2').isDisabled()), 'up is disabled on the first card, down on the last');
  await act('rb-up-2').click();                      // rule 3 above rule 2
  await act('rb-up-1').click();                      // and above rule 1
  check((await act('rb-cond-0').innerText()) === 'First actions' && (await act('rb-value-1').innerText()) === '40%', 'up moves a card up');
  await act('rb-down-0').click();                    // back below rule 1
  check((await act('rb-cond-1').innerText()) === 'First actions', 'down moves a card down');
  await t.layout('rulebook editor');
  while ((await ruleCount()) < meta.slots) await act('rb-add').click();
  check(await act('rb-add').isDisabled(), 'Add rule is disabled at ' + meta.slots + ' rules');
  while ((await ruleCount()) > 3) await act('rb-del-' + ((await ruleCount()) - 1)).click();
  await act('rb-del-2').click();                     // drop the always > basic rule
  check((await ruleCount()) === 2 && !(await act('rb-add').isDisabled()), 'delete removes a card and Add is back');

  // ---- save, label, reload
  await act('rb-save').click();
  const want = rule1 + '|first_actions:2>basic@highest_hp';
  const saved = await rulebooks();
  check(saved[String(1)] === want && Object.keys(saved).length === 1, 'Save stores the canonical text under the monster\'s id (' + JSON.stringify(saved) + ')');
  check(await act('rb-save').isDisabled(), 'Save is disabled with nothing new to save');
  await act('rb-close').click();
  check(!(await sheetOpen()) && (await act('detail-rulebook').innerText()) === 'Rulebook (2 rules)', 'closing the sheet shows the Vault detail with "Rulebook (2 rules)"');
  await t.reload();
  await act('hometab-vault').click();
  await act('detail-1').click();
  check((await act('detail-rulebook').innerText()) === 'Rulebook (2 rules)', 'a reload keeps the rulebook');
  await act('detail-rulebook').click();
  check((await ruleCount()) === 2 && (await act('rb-value-0').innerText()) === '40%' && (await act('rb-target-1').innerText()) !== '', 'the editor reads the saved rulebook back into cards');

  // ---- unsaved changes ask before closing; Escape closes the picker, then the sheet
  await act('rb-add').click();
  await act('rb-close').click();
  check(await page.locator('[data-id=rb-confirm]').isVisible() && await sheetOpen(), 'Close with unsaved changes asks first');
  await act('rb-keep').click();
  await act('rb-del-2').click();
  check((await page.locator('[data-id=rb-confirm]').count()) === 0, 'Keep editing hides the confirm row');
  await act('rb-cond-0').click();
  await page.keyboard.press('Escape');
  check((await page.locator('[data-id=rb-picker]').count()) === 0 && await sheetOpen(), 'Escape closes the picker first');
  await page.keyboard.press('Escape');
  check(!(await sheetOpen()), 'a second Escape closes the sheet');
  check(await act('detail-close').isVisible(), 'the Vault detail sheet is still there under it');

  // ---- clear all
  await act('detail-rulebook').click();
  await act('rb-clear').click();
  check((await ruleCount()) === 0 && (await page.locator('[data-id=rb-empty]').count()) === 1, 'Clear all removes every card');
  await act('rb-save').click();
  const cleared = await rulebooks();
  check(!cleared['1'] && Object.keys(cleared).length === 0, 'Clear all + Save removes the rulebook from the save');
  await act('rb-close').click();
  check((await act('detail-rulebook').innerText()) === 'Rulebook: none', 'the button reads "Rulebook: none" again');

  // ---- a saved rule naming a skill the monster does not have is a warning card (save edit: the engine took it when it had it)
  const withBad = await t.readAccount();
  withBad.Account.Rulebooks = { 2: 'always>no_such_skill@lowest_hp|always>basic' };
  await t.writeSaveText(H.encodeSave(withBad));
  await t.reload();
  await act('hometab-vault').click();
  await act('detail-2').click();
  await act('detail-rulebook').click();
  check(await page.locator('[data-act=rb-rule-0].warn').count() === 1 && await page.locator('[data-act=rb-rule-1].warn').count() === 0,
    'a rule naming a skill the monster lacks shows as a warning card');
  await act('rb-del-0').click();
  await act('rb-save').click();
  check((await rulebooks())['2'] === 'always>basic', 'deleting the bad rule and saving keeps the good one');
  await act('rb-close').click();

  // ---- Test: a preview of an Endless floor with the draft, played back with the acting rule lit, paying nothing
  await page.evaluate(() => RulebookView.open(RulebookView.lastCtx, 1, { season: 2966, team: [0, 1, 2, 3], floor: 1 }));
  await act('rb-sheet').waitFor();
  await act('rb-add').click();                       // a draft always > basic, not saved
  const before = await t.readAccount();
  await act('rb-test').click();
  await act('rb-play').waitFor();
  check((await act('rb-error').count()) === 0, 'the preview runs' + ((await act('rb-error').count()) ? ': ' + (await act('rb-error').first().innerText()) : ''));
  const lit = await page.waitForFunction(() => !!document.querySelector('[data-act=rb-rule-0].lit'), null, { timeout: 30000, polling: 50 }).then(() => true, () => false);
  check(lit, 'the acting rule\'s card lights up during playback');
  await t.layout('rulebook test playback');
  await act('rb-skip').click();
  check(await act('rb-result').isVisible() && /^(Won|Lost on) floor 1/.test(await act('rb-result').innerText()), 'the result shows: ' + (await act('rb-result').innerText()));
  check((await page.locator('.rbcard.lit').count()) === 0, 'no card stays lit after the playback');
  const after = await t.readAccount();
  check((after.Account.Shards || 0) === (before.Account.Shards || 0) && (after.Account.Shards || 0) === shards0 && JSON.stringify(after.Account.Endless || null) === JSON.stringify(before.Account.Endless || null) &&
    !(after.Account.Rulebooks || {})['1'], 'the test pays and saves nothing (shards ' + (after.Account.Shards || 0) + ', no rulebook stored)');
  await act('rb-test').click();                      // a second test, then close mid-playback: its timer stops
  await act('rb-close').click();
  if (await act('rb-discard').count()) await act('rb-discard').click();
  check(!(await sheetOpen()), 'the sheet closes during a playback');
  await page.waitForTimeout(600);

  // ---- a monster with no id (lean JSON: an Id of -1 is left out) cannot hold a rulebook
  const noId = await t.readAccount();
  delete noId.Account.Vault[3].Id;
  await t.writeSaveText(H.encodeSave(noId));
  await t.reload();
  await act('hometab-vault').click();
  await act('detail-3').click();
  await act('detail-rulebook').click();
  check((await page.locator('[data-id=rb-noid]').innerText()).includes('cannot hold a rulebook') && (await act('rb-add').count()) === 0 && (await act('rb-save').count()) === 0,
    'a monster with no id shows the no-id message and no editing');
  await t.layout('rulebook no id');
  await page.keyboard.press('Escape');
  check(!(await sheetOpen()), 'Escape closes it');

  await t.finish();
})().catch((e) => { console.error(e); process.exit(1); });
