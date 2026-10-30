#!/usr/bin/env node
// Writes Content/content.json from web/content.js (the single source of balance numbers).
'use strict';
const fs = require('fs');
const path = require('path');

const OUT = path.join(__dirname, '..', '..', 'Content', 'content.json');

function render() {
  const content = require('../content.js');
  return JSON.stringify(content, null, 2) + '\n';
}

module.exports = { render, OUT };

if (require.main === module) {
  fs.mkdirSync(path.dirname(OUT), { recursive: true });
  const text = render();
  fs.writeFileSync(OUT, text);
  console.log('wrote ' + path.relative(process.cwd(), OUT) + ' (' + text.length + ' bytes)');
}
