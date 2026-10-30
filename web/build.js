// Bundles index.html + scripts into one self-contained file.
//   node web/build.js                -> web/dist/idle-rng.html (open directly, email, host anywhere)
//   node web/build.js --fragment OUT -> page body without <html>/<head> wrappers (for claude.ai artifacts)
'use strict';
const fs = require('fs');
const path = require('path');
const dir = __dirname;
const html = fs.readFileSync(path.join(dir, 'index.html'), 'utf8');
const scripts = ['content.js', 'sim.js', 'app.js']
  .map((f) => '<script>\n' + fs.readFileSync(path.join(dir, f), 'utf8').replace(/<\/script/gi, '<\\/script') + '\n</script>')
  .join('\n');
const between = (s, a, b) => s.slice(s.indexOf(a) + a.length, s.indexOf(b));
const head = between(html, '<!--BUILD:HEAD-->', '<!--/BUILD:HEAD-->');
const body = between(html, '<body>', '<!--BUILD:SCRIPTS-->');
const frag = process.argv[2] === '--fragment';
const out = frag
  ? head.trim() + '\n' + body.trim() + '\n' + scripts + '\n'
  : html.replace(/<!--BUILD:SCRIPTS-->[\s\S]*<!--\/BUILD:SCRIPTS-->/, () => scripts);
const target = frag ? process.argv[3] : path.join(dir, 'dist', 'idle-rng.html');
fs.mkdirSync(path.dirname(target), { recursive: true });
fs.writeFileSync(target, out);
console.log('wrote ' + target + ' (' + Math.round(out.length / 1024) + ' KB)');
