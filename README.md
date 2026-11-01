# MonStack - monster trainer

A fast, systems-driven monster-raising game

Checks: `./check.sh` runs every check in the repo (`--fast` for the inner loop, `--no-dotnet` without the .NET SDK). CI (`.github/workflows/check.yml`) runs the full `./check.sh` on every pull request and push to `main`. Agent rules live in `CLAUDE.md`.

Tools (Python 3.7+, no dependencies):

- `tools/content_build.py` - builds `Content/` from `content_src/` and validates it (`--check` in `check.sh`).

Server spike: `server/ugs/` holds the UGS Cloud Code module that resolves battles with `IdleRng.Sim` (issue #17, `specs/08` section 10.1). It needs credentials and the .NET 10 SDK, so it is not part of `check.sh`; see `server/ugs/README.md`.

## Running the web client

The web client is the real C# game core (`src/IdleRng.Sim`, the code the bots use), compiled to WebAssembly by `src/IdleRng.Web` and played through the browser UI in `web/`. Everything runs on your own computer; no Claude session is needed.

### One-time setup

**1. Check what you already have:**

```
./play.sh --check
```

It lists each dependency as `ok` or `MISSING`, with the install command for your OS next to anything missing. Run it again after installing until it says "All set".

**2. Install anything missing.** You need four things; the fifth is optional:

| Dependency | Why | macOS | Windows (PowerShell) | Ubuntu / Debian |
|---|---|---|---|---|
| Python 3 | serves the page | `brew install python` | `winget install Python.Python.3.12` | `sudo apt install python3` |
| Node.js 18+ | packs the game content | `brew install node` | `winget install OpenJS.NodeJS.LTS` | `sudo apt install nodejs` |
| .NET 8 SDK | builds the C# engine | [installer](https://dotnet.microsoft.com/download/dotnet/8.0) | `winget install Microsoft.DotNet.SDK.8` | `sudo apt install dotnet-sdk-8.0` |
| WebAssembly tools | compile C# for the browser | `sudo dotnet workload install wasm-tools` | `dotnet workload install wasm-tools` (as administrator) | `sudo dotnet workload install wasm-tools` |
| qrencode (optional) | QR code for the phone link | `brew install qrencode` | not needed | `sudo apt install qrencode` |

Install them top to bottom: the WebAssembly tools need the .NET SDK first. On macOS, `brew` is [Homebrew](https://brew.sh); the official installers from python.org and nodejs.org work too. Ubuntu's `nodejs` package must be version 18 or newer (`node --version`); older releases need the installer from nodejs.org. On Windows, run `./play.sh` from Git Bash (comes with [Git for Windows](https://git-scm.com/download/win)) or WSL; open a new terminal after installing so it sees the new tools.

### Play

From the repo root:

```
./play.sh
```

It checks the prerequisites (and tells you the command to fix anything missing), builds the C# engine only when `src/` or `Content/` changed since the last build (the first build takes 1-3 minutes), starts a local server, prints the link for this computer and for your phone, and opens your browser. Stop it with Ctrl+C. 

Options: `--rebuild` forces a build, `--port 8080` picks another port (it moves to the next free one if the port is taken), `--no-open` skips opening a browser, `--local` refuses connections from other devices. If `qrencode` is installed it also prints a QR code for the phone link.

By hand, the same thing is `web/wasm/build.sh` then `cd web && python3 -m http.server 8000`.

It must be served over HTTP: browsers refuse to load WebAssembly from a `file://` page, so opening `index.html` directly shows an error telling you to start the server. On Windows, run `build.sh` from Git Bash or WSL.

If the page says "The C# engine did not load", it names the cause: the bundle is not built (run `./play.sh --rebuild`), the page was opened as a file, or loading failed (rerun the build and read its output; the usual cause is the missing `wasm-tools` workload).

### On a phone

**GitHub Pages (the public web build).** https://kjew2.github.io/MonStack/ serves `main` after each green check run (`.github/workflows/pages.yml`). It is the copy where Google sign-in works (`specs/08` section 4); the save there is separate from the artifact's.

**Claude artifact link (no computer needed).** Open https://claude.ai/artifact/LpdHtJ53ujKkXPizqPkkkg in the phone's browser while signed in to claude.ai. It opens the monster trainer. It is private to the owner and runs the same C# engine bundle as `./play.sh`. It is a copy: it changes only when Claude republishes it (`web/wasm/publish-artifact.sh` builds and prints the Artifact publish call: `web/index.html`, root = repo root, and the file map from `web/wasm/artifact-files.py`). The page header shows the commit and date it was built from (`wasm/dist/build-info.json`), so you can tell whether it is current; "dev" means no build stamp.

**From your computer, on the same Wi-Fi (always matches your checkout):**

1. Run `./play.sh` on the computer. It prints an "On your phone" link such as `http://192.168.1.20:8000`.
2. Open that link on the phone, on the same Wi-Fi. If it does not load, allow Python through the computer's firewall.
3. Optional: Add to Home Screen from the browser's share menu for a full-screen icon.

Progress is saved in the browser, separately for each address, so the artifact link and the Wi-Fi address each keep their own save. Fonts load from Google Fonts; offline the page falls back to system fonts and still plays.

### Tests for the web build

- `node web/wasm/test/run-trainer.js`: plays a whole monster-trainer run on `index.html` through the real buttons (drill, rest, fights, drafts, buying), seals it, checks the grade and Vault, reloads mid-run and after the end, and locks, dismantles and loads an over-cap v1 save.
- `node web/wasm/test/flows.js`: checks each main screen at 390x844 for horizontal scroll and console errors, overwrites a tree slot from a draft, reloads mid-run (also with a draft waiting) and resumes to the end with the same run hash, and loads a stale and a damaged save (notice, kept-aside backup, working account).
- Game rules are tested in C# (`src/IdleRng.Sim.Tests`); `./check.sh` runs everything, including the web build and the browser tests in full mode.
