# Instructions for agents

## Keep the specs current

The specs in `specs/` are the source of truth for this project. Start with `specs/00-overview.md`, which holds the canonical constants and cross-spec contracts.

When something is discussed and agreed with the user that diverges from what a spec says, update the spec in the same change:

- Edit the affected spec(s) so they describe the agreed design, not the original plan. Fix every spec the change touches, including constants and contracts in `specs/00-overview.md`.
- Update anything that mirrors the spec in the same change: test vectors, `golden/` files, `src/IdleRng.Bots/Acceptance/acceptance-targets.json`, `tools/` models, and the `web/` prototype where relevant.
- If the README's spec table no longer matches, update it too.
- Do not leave code and specs disagreeing. If you notice an existing mismatch with no agreed decision behind it, ask the user which one is right instead of guessing.
