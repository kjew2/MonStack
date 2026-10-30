#!/usr/bin/env node
// Replays every Fixtures/Parity/*.json on the JS core (loading Content/content.json) and asserts each result
// equals `expect` exactly. Also checks format-vectors.json. Exit 1 on any mismatch.
'use strict';
const fs = require('fs');
const path = require('path');
const lib = require('./parity-lib.js');

const ROOT = path.join(__dirname, '..', '..');
const DIR = process.argv[2] || path.join(ROOT, 'Fixtures', 'Parity');
const content = JSON.parse(fs.readFileSync(path.join(ROOT, 'Content', 'content.json'), 'utf8'));
let bad = 0;

for (const f of fs.readdirSync(DIR).filter((n) => n.endsWith('.json')).sort()) {
  const j = JSON.parse(fs.readFileSync(path.join(DIR, f), 'utf8'));
  if (f === 'format-vectors.json') {
    for (const [v, s] of j) if (lib.simApi.formatShort(v) !== s) { console.error(f + ': formatShort(' + v + ') = ' + lib.simApi.formatShort(v) + ' != ' + s); bad++; }
    console.log('ok   ' + f + ' (' + j.length + ' vectors)');
    continue;
  }
  const sim = lib.create(j, content);
  let k = 0, fails = 0;
  j.steps.forEach((step, i) => {
    const r = lib.exec(sim, content, step);
    if (!r.has) return;
    if (k >= j.expect.length) { console.error(f + ': step ' + i + ' has no expect entry'); fails++; return; }
    const d = lib.diff(r.value, j.expect[k], '$');
    if (d && fails < 5) console.error(f + ': step ' + i + ' (' + JSON.stringify(step).slice(0, 80) + ') expect[' + k + '] ' + d);
    if (d) fails++;
    k++;
  });
  if (k !== j.expect.length) { console.error(f + ': ' + j.expect.length + ' expects for ' + k + ' result steps'); fails++; }
  bad += fails;
  console.log((fails ? 'FAIL ' : 'ok   ') + f + ' (' + j.steps.length + ' steps, ' + k + ' expects)');
}
process.exit(bad ? 1 : 0);
