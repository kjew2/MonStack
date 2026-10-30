// Headless Chromium smoke test of the built page. Run: node web/build.js && node web/tools/engine-smoke.js
'use strict';
const path = require('path');
const assert = require('assert');
const { chromium } = require('playwright');
const url = 'file://' + path.join(__dirname, '..', 'dist', 'idle-rng.html');

async function run(query, check, mock) {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  const errors = [];
  page.on('pageerror', (e) => errors.push('pageerror: ' + e.message));
  page.on('console', (m) => { if (m.type() === 'error' && !/Failed to load resource|ERR_FAILED/.test(m.text())) errors.push('console: ' + m.text()); });
  if (mock) {                                    // inject the JSON-bridge mock as the wasm loader (browser-side copy of engine-mock.js)
    const src = require('fs').readFileSync(path.join(__dirname, 'engine-mock.js'), 'utf8');
    await page.addInitScript((code) => {
      window.__idleLoadWasm = async (o) => { const module = { exports: {} }; new Function('module', 'require', code)(module, () => window.IdleSim); return module.exports.mockLoader()(o); };
    }, src);
  }
  await page.route(/fonts\.(googleapis|gstatic)\.com/, (r) => r.abort());
  try {
    await page.goto(url + query);
    await page.waitForFunction(() => document.getElementById('stageEl').textContent.startsWith('Stage'), null, { timeout: 15000 });
    const earlyToast = await page.evaluate(() => document.getElementById('toasts').textContent);
    await page.waitForTimeout(5000);
    for (const t of ['Stats', 'Gear', 'Heroes', 'Summon', 'Class', 'Fight']) {
      await page.click('#tabs button:has-text("' + t + '")');
      await page.waitForTimeout(150);
    }
    await page.click('#tabs button:has-text("Summon")');
    await page.click('button:has-text("Summon x10")');
    await page.waitForTimeout(800);
    await page.keyboard.press('Escape');
    await page.click('#cogBtn');
    const engineText = await page.textContent('#engineChip');
    await page.keyboard.press('Escape');
    const toastText = earlyToast;
    const w = await page.evaluate(() => ({ sw: document.documentElement.scrollWidth, iw: window.innerWidth, stage: document.getElementById('stageEl').textContent }));
    assert.strictEqual(w.sw, w.iw, 'horizontal overflow ' + w.sw + ' vs ' + w.iw);
    await check({ engineText, toastText, errors, page });
    assert.deepStrictEqual(errors, [], 'errors: ' + errors.join(' | '));
    console.log('ok   ' + (query || '(default)') + '  ' + engineText + (toastText ? '  toast: ' + toastText : ''));
  } finally { await browser.close(); }
}

(async () => {
  await run('?engine=js', ({ engineText, toastText }) => {
    assert.ok(/^Engine: JS/.test(engineText), engineText);
    assert.ok(!/unavailable/.test(toastText));
  });
  await run('?engine=wasm', ({ engineText, toastText }) => {          // no bundle is served from file://, so it must fall back
    assert.ok(/^Engine: JS/.test(engineText), engineText);
    assert.ok(/C# engine unavailable, using JS engine/.test(toastText), 'notice missing: ' + toastText);
  });
  await run('', ({ engineText, toastText }) => { assert.ok(/^Engine: JS/.test(engineText), engineText); assert.ok(!/unavailable/.test(toastText)); });
  await run('?engine=wasm', async ({ engineText, toastText, page }) => {   // wasm facade driven by the mock bridge
    assert.ok(/^Engine: C# \(wasm\) · IdleRng.Sim MOCK/.test(engineText), engineText);
    assert.ok(!/unavailable/.test(toastText));
    const keys = await page.evaluate(() => Object.keys(localStorage).filter((k) => /save-v1$/.test(k)));
    assert.deepStrictEqual(keys, ['idle-rng-wasm-save-v1']);
  }, true);
  console.log('smoke tests passed');
})().catch((e) => { console.error('FAIL', e.message); process.exit(1); });
