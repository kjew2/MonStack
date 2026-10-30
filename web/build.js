// Bundles index.html + scripts into one self-contained file.
//   node web/build.js                -> web/dist/idle-rng.html (open directly, email, host anywhere; JS engine only)
//   node web/build.js --with-wasm    -> same, and also copies web/wasm/dist (if built) to web/dist/wasm/dist
//   node web/build.js --fragment OUT -> page body without <html>/<head> wrappers (for claude.ai artifacts; JS engine only)
// The single-file bundle cannot contain the .NET runtime. Wasm mode needs the page served over HTTP(S) (not file://) with
// web/dist/wasm/dist next to it; without that, the page falls back to the JS engine (silently by default, with a notice when
// ?engine=wasm was requested explicitly).
'use strict';
const fs = require('fs');
const path = require('path');
const dir = __dirname;
const args = process.argv.slice(2);
const withWasm = args.includes('--with-wasm');
const fragAt = args.indexOf('--fragment');
const frag = fragAt >= 0;
const html = fs.readFileSync(path.join(dir, 'index.html'), 'utf8');
const scripts = ['content.js', 'sim.js', 'engine.js', 'sprites.js', 'app.js']
  .map((f) => '<script>\n' + fs.readFileSync(path.join(dir, f), 'utf8').replace(/<\/script/gi, '<\\/script') + '\n</script>')
  .join('\n');
const between = (s, a, b) => s.slice(s.indexOf(a) + a.length, s.indexOf(b));
const head = between(html, '<!--BUILD:HEAD-->', '<!--/BUILD:HEAD-->');
const body = between(html, '<body>', '<!--BUILD:SCRIPTS-->');
const out = frag
  ? head.trim() + '\n' + body.trim() + '\n' + scripts + '\n'
  : html.replace(/<!--BUILD:SCRIPTS-->[\s\S]*<!--\/BUILD:SCRIPTS-->/, () => scripts);
const target = frag ? args[fragAt + 1] : path.join(dir, 'dist', 'idle-rng.html');
fs.mkdirSync(path.dirname(target), { recursive: true });
fs.writeFileSync(target, out);
console.log('wrote ' + target + ' (' + Math.round(out.length / 1024) + ' KB)');
if (withWasm) {
  const src = path.join(dir, 'wasm', 'dist');
  if (frag) console.log('--with-wasm ignored for --fragment (artifact pages only get the JS engine)');
  else if (!fs.existsSync(src)) console.log('--with-wasm: ' + src + ' not found (run web/wasm/build.sh); the page will use the JS engine');
  else {
    const dst = path.join(path.dirname(target), 'wasm', 'dist');
    fs.rmSync(dst, { recursive: true, force: true });
    fs.cpSync(src, dst, { recursive: true });
    console.log('copied ' + src + ' -> ' + dst + ' (serve ' + path.dirname(target) + ' over HTTP; file:// cannot load wasm)');
  }
}
