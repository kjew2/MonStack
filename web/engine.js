// Loader for the C# core compiled to WebAssembly (src/IdleRng.Web, contract in web/wasm/BRIDGE.md).
// IdleEngine.loadWasm(opts) -> Promise<{ host, contentPack }>. The monster-trainer facade (web/run-engine.js) builds on it.
// No game rule lives here.
(function (root) {
  'use strict';

  const WASM_BASE = 'wasm/dist/';

  function withTimeout(p, ms, what) {
    let t;
    return Promise.race([p, new Promise((_, rej) => { t = setTimeout(() => rej(new Error(what + ' timed out')), ms); })]).finally(() => clearTimeout(t));
  }

  // Loads the .NET runtime and the content pack. Rejects with a message the UI shows as-is.
  async function loadWasm(o) {
    const base = o.wasmBase || WASM_BASE;
    if (root.location && root.location.protocol === 'file:') {
      throw new Error('This page was opened as a file. Browsers only load the C# engine over HTTP: run `cd web && python3 -m http.server 8000` and open http://localhost:8000');
    }
    let head = null;
    try { head = await fetch(base + '_framework/dotnet.js', { method: 'HEAD' }); } catch (e) { /* network error */ }
    if (!head || !head.ok) throw new Error('The C# engine is not built. Run `web/wasm/build.sh`, then reload.');
    const abs = (p) => new URL(p, document.baseURI).href;
    const run = async () => {
      const { dotnet } = await import(abs(base + '_framework/dotnet.js'));
      const runtime = await dotnet.withDiagnosticTracing(false).create();
      const exports = await runtime.getAssemblyExports('IdleRng.Web.dll');
      // Revalidate like dotnet.js does for its own files: a cached pack from the last deploy with this deploy's engine
      // fails to load (E040 "required constant missing" when a constant was added).
      const r = await fetch(base + 'content-pack.json', { cache: 'no-cache' });
      if (!r.ok) throw new Error('content-pack.json missing from ' + base + '. Rerun `web/wasm/build.sh`.');
      return { host: exports.IdleRng.Web.Engine, contentPack: await r.text() };
    };
    return withTimeout(run(), o.timeoutMs || 30000, 'Loading the C# engine');
  }

  root.IdleEngine = { loadWasm: loadWasm };
})(typeof self !== 'undefined' ? self : this);
