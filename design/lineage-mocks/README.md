# Lineage UI concepts

Two self-contained, interactive visual explorations for MonStack’s breeding lineage flow. Open either HTML file in a browser; all artwork, scripts, and styles are embedded. No game save is read or changed.

- **Library:** selected lineage detail, I–X progression, V/X previews, then guided parent selection, offspring review, training contribution, and breakthrough preview.
- **Workshop (selected by kj, 2026-11-05):** the same decisions arranged around two parent cards, their prospective offspring, and a persistent generation milestone rail. This supplied the production Synthesis Workshop’s visual direction: parent pairing by default, a searchable lineage library inside the focus-tree picker, and explicit offspring review.

Production adopts the Workshop’s dark violet and gold palette within the Breed screen; other game screens retain their existing theme. These are concept mockups, not screenshots of the production UI. Example data focuses on Fortune IV → V; the other library entries are illustrative samples rather than the full production catalogue. Breakthrough success is scripted, and training explicitly jumps to a later qualifying seal. Production resolves eligible protected breakthroughs automatically when a run seals; the mock's breakthrough button does not add a separate player-controlled roll. The current production contract is [spec 30](../../specs/30-lineages-i-x.md#8-synthesis-workshop-ui).

Both show historical sample data: 140/180 account lineage points, a 50-shard cost, retained parents, 12-point capstones, 20-point complete trees, 50% initial V breakthrough odds with pity, and the locked X Shared Fortune milestone. Production now consumes both parents and transfers monster-held rank/progress for two selected trees; the mockups do not simulate those rules. Node-only focus growth and automatic protected milestones remain in production.

`*-overview.png` files show the desktop layout; `*-mobile.png` show the 390px layout. Browser verification covers the complete action flow, all six stages at 390px without document overflow, and JavaScript errors.
