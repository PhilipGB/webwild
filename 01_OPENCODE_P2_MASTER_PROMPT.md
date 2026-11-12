# Webwild v0.2.0 P2 — Modular Silk Bridges, Reliability & Release Polish

## Mission

Implement and deliver **Webwild v0.2.0 P2**, the final technical hardening and presentation milestone after the two-region Reedwater Bank implementation. Use the **finished original modular bridge assets included with this handoff**. This task is to implement a true playable feature end-to-end, not create mockups or a plan only.

**Repository:** `https://github.com/PhilipGB/webwild` (must remain PRIVATE)  
**Expected baseline:** `main` at `698be2c48c2bf41699fba1ab278a1f2fbfea08a7`  
**Previous release:** `v0.1.2`  
**Target release:** `v0.2.0`  
**Stack:** Godot 4.7.2 .NET; C# / .NET 10; scene-first persistent worlds.

Work autonomously through investigation, scoped implementation, tests, rendered checks, review, PR and release. Do not ask for routine confirmation. Respect existing uncommitted changes and any newer main commits; never overwrite them. If the baseline differs, inspect the changes and adapt, documenting any conflicts.

## 1. Investigate first

1. Read `AGENTS.md` if present, `README.md`, `ACCEPTANCE.md`, `docs/SCENE_AUTHORING.md`, `docs/EVIDENCE.md`, `docs/VERIFICATION.md` and this bundle's `docs/BRIDGE_ASSET_INTEGRATION.md` and `docs/BRIDGE_P2_ACCEPTANCE.md`.
2. Confirm repo visibility, branch, Git status, baseline, PR #12 merge and last green CI.
3. Review `scripts/visual/WebMeshFactory.cs`, `scripts/building/WebVisual.cs`, `src/Core/BridgeGeometry.cs`, `src/Core/ResourceLedger.cs`, `src/Core/GameSimulation.cs`, `src/Core/SaveData.cs`, `src/Core/SaveMigrator.cs`, `scripts/ui/GameUI.cs`, `scripts/ui/UiKit.cs`, the runtime probe and tests.
4. Establish current bridge geometry, colliders, rope visualization, runtime length/angle constraints and screenshot baseline.
5. Inspect supplied GLBs and Godot wrappers; ensure existing `art/materials/vertex_color_material.tres` is available.
6. Create a focused `feat/v0.2.0-p2-bridge-polish` branch from up-to-date main, once safe.

Write a short implementation plan, then execute it without pausing for approval.

## 2. P2 reliability: one-time Reedwater supply cache grant

**Confirmed flaw:** `GameSimulation.TryTravelTo()` calls `Ledger.TryGrant(6, 24, $"region_supply_{regionId}")` on Reedwater entry. `ResourceLedger` retains only the most recent 512 transaction IDs. If the one-time grant ID expires, subsequent visits can grant six food and 24 silk again.

Fix using **durable gameplay state**, not the bounded ledger-history window. `VisitedRegions` is a possible basis if it unambiguously represents a claimed first entry; otherwise add a dedicated persistent claimed-cache flag/set with explicit migration and validation. Ensure the cache is granted **once per new game across unlimited play, save/load and region travel**, and never silently re-granted after 513+ transactions. Preserve existing player progress and schema-1→2 migration. If save schema change is needed, migrate explicitly rather than silently reusing an old field. Do not modify the 512-key rolling history policy.

Regression tests MUST cover:

- Entry grants exactly +6 food / +24 silk once, across immediate back-and-forth travel.
- 600+ unrelated ledger transactions, including history eviction, followed by repeated travel.
- Save/export → JSON round trip → reload → repeat travel after history expiry.
- Existing schema-1 fixtures (`v0.1.0`, `v0.1.1`, `v0.1.2`) and schema-2 P1 saves.
- No duplicate supply on repeated travel, no regression to other exactly-once resources, no invalid/negative economy.
- Multi-region saves and worker state stay coherent.

Use a test that actually expires the key; an immediate double-visit check does not prove the fix.

## 3. Replace flat yellow silk ribbons with modular silk-and-bark bridges

Visual art direction: a tiny spider colony lashes **twigs/bark fragments** together using pale ivory/cyan spider silk, like the original Webwild concept art. Friendly, flat-shaded, handcrafted and clearly walkable. Do NOT create a human-scale steel/rope bridge or large wooden suspension bridge. Keep capture webs visually distinct.

**Supplied finished assets:**

- `art/models/bridge/bark_plank_a.glb`, `bark_plank_b.glb`, `bark_plank_c.glb`
- `silk_cinch_knot.glb`, `silk_double_hitch.glb`
- `wooden_eyelet.glb`, `forked_rail_fixture.glb`
- `silk_wrapped_support.glb`, `silk_cross_lashing.glb`
- `braided_silk_segment.glb`, `woven_silk_panel.glb`

Godot wrappers are at `scenes/building/bridge/parts/*.tscn`, plus `BridgeAssetGallery.tscn`. `examples/bridge_4m_reference_only.glb` is a static sample **for art reference only**. Do not load the example as a runtime bridge. The GLBs have original `COLOR_0` vertex colours and use the existing `res://art/materials/vertex_color_material.tres`. Validate import node names; retain/adjust editor-visible material overrides and ensure exported Linux builds display colours correctly.

### Required hybrid renderer

Implement a focused reusable bridge visual assembler (e.g. `BridgeVisualAssembler`), used by actual constructed and preview bridges. Its inputs should include two anchor positions, authored bank approach points when present, bridge state/ID and a visual mode. It should:

1. Compute a deterministic sagging centreline for arbitrary 3D anchor positions. Reuse the **same sampling/sag contract as existing bridge collision**, or centralise sampling so visuals/collision cannot diverge.
2. Place alternating plank A/B/C models at fairly even arc-length intervals along the centreline, oriented to the local tangent, with their **local +X along travel and +Z across bridge**. Use tiny deterministic offsets (stable from ID/index), not unstable random per reload. Models are approximately 0.19–0.24 m long in X and 0.66–0.73 m across Z.
3. Add optional woven silk panel segments underneath or in gaps. Avoid fully solid bright yellow ribbons/opaque rectangles.
4. Generate curved, ivory/silver-blue silk side ropes and thinner vertical/horizontal lashings that follow the span and sag, including reversed anchor selection and predominantly Z-axis bridges.
5. Instance support posts/eyelets/knots at suitable points (not at absurd density). Reuse the existing authored environmental anchor posts; do not replace mandatory gameplay anchors.
6. Keep rail/rope visibility readable against both mint grass and cyan pond. Use moderate contrast and small highlights; avoid neon/glowing yellow across the entire bridge.
7. Reflect damage on presentation via carefully constrained tint/fraying/strand fading WITHOUT changing physics/repair semantics. The broken/disabled state must be clear, but not require new game rules.
8. Preserve build-preview validity signalling (green/coral or a comparably accessible cue), and avoid mixing the preview state with actual constructed collision.
9. Avoid excessive node counts and per-frame mesh rebuilds. Prefer shared `PackedScene` / `Mesh` resources, batched `MultiMesh` where worthwhile, or a bounded number of instances for short spans. Rebuild on construction/state changes, not every tick.
10. Handle short/long spans, near-zero spans, diagonal, elevated and reversed-order anchors with no NaNs or missing geometry. The pure core still enforces bridge-placement rules.

**Critical:** Preserve the existing `WebVisual.BuildBridgeCollision()` walkable decks, 7-segment sagging walkway and authored-bank 1.2 m ramps, or replace them only with geometrically and physically equivalent collision proven in runtime tests. The assets themselves are **visual-only and intentionally contain no independent collisions**. Do not add dozens of duplicate colliders via prop scenes. Keep worker graph logic, silk costs, save schema and capture-web visuals unchanged unless an issue is directly demonstrated.

Inspect the supplied contact sheet and `docs/BRIDGE_ASSET_CATALOGUE.md` before implementing. All original planks, fixtures and knots should be genuinely used where appropriate; avoid discarding the pack and regenerating equivalent cubes in C#.

## 4. Usability: reduce toast / HUD obstruction

Supplied P1 screenshots show large centre-screen notifications hiding the player, anchor selection area or bridge. Improve the existing notification layout without redesigning the entire UI.

- Place brief toasts in a dedicated non-modal notification lane that avoids the player/camera centre, tutorial instructions, mission card, interaction controls and bottom action toolbar.
- Limit toast width/height, duration and stacking; use concise wrapping and accessible text contrast.
- Keep long actions visible via objective/hint UI rather than long-lived blocking toasts.
- Check at 1280×720 for UI scales **100%, 125% and 150%**. No overlapping clickable controls or action buttons.
- Do not block keyboard/mouse input through notifications.
- Maintain accessibility, reduced motion and existing pause/help/colony/navigation behaviour.
- Ensure bridge construction and captured insect prompts remain understandable to ages 7–12.

Focus on this UX issue and any directly caused visual overlap. Avoid unrelated UI rewrites.

## 5. Automated and visual acceptance

Retain all pre-existing tests. P1 baseline reports **93 .NET tests, 47 scene checks, 168 runtime checks**, all green; treat these as minimums, not goals to reduce.

Add appropriate pure tests and Godot runtime assertions that validate:

- Plank spacing, deterministic transforms and sag, orientation for west→east, east→west, north→south and diagonals.
- No opaque ribbon or yellow solid box mesh is still rendered as the bridge surface.
- The same collision semantics and bridge ramp endpoints remain valid; no water/ravine crossing without a bridge.
- **Real player traversal** both directions on Garden and Reedwater bridges; worker use of existing navigation edges; remove/damage/repair behaviour.
- The visual assembly survives region transition, save/reload and exported Linux startup.
- Vertex-colour materials are present in editor and exported builds.
- Notification placement and visibility at each UI scale, including simultaneous mission cards/hints.
- Extended economy test after >512 transactions; legacy/current save compatibility.
- Real environment screens are checked for accidental floating planks, rope clipping, opaque stripes, material loss and frame-rate regressions.

Required runs:

```bash
 dotnet build -warnaserror
 dotnet test
 godot --headless --path . --editor --quit
 godot --headless --path . -- --scene-validate
 godot --headless --path . -- --runtime-probe
```

Also run rendered Xvfb runtime probe, Linux exported headless & rendered probes, packaged Linux re-download/checksum/extract/run, and Windows x64 export. Reproduce and inspect actual screenshots at 1280×720 and UI 150%, with clear before/after bridge views. Do not invent results. Where exact commands vary, follow `docs/VERIFICATION.md` and `docs/EXPORTS.md`.

The current bridge rendering is C# runtime content; static garden scenery and both region scenes remain authored `.tscn` and must not regress.

## 6. Independent diff review and release workflow

1. Run full diff review against main after implementing changes; fix visual defects, gameplay edge cases and save regressions.
2. Update relevant docs (`README.md`, `CHANGELOG.md`, `ACCEPTANCE.md`, `PROGRESS.md`, `docs/EVIDENCE.md`, scene authoring and exports where needed).
3. Commit in logical units; push a PR against `main`. Require exact PR-head CI green; fix any failures, rerun after changes.
4. Merge only when verified, check final merged `main` CI and clean worktree.
5. Build release archives for Linux/Windows with SHA256SUMS, README and explicit verification limitations. Test the actual published Linux archive after re-download, not only local dev export.
6. Create annotated `v0.2.0` tag and publish a GitHub release on the verified main commit **only if all executable technical release gates have passed and no known P1/P2 issue remains**. If release criteria cannot be met, leave the tag/release unpublished, provide the blocker and preserve merge evidence instead.
7. Repository must remain **PRIVATE**; verify via `gh repo view` before and after release.

**Cannot claim** Windows runtime tested without a Windows host, desktop full-screen tested without a real compositor, or supervised child playtested without actual participants. If unavailable, report each as **NOT VERIFIED** prominently and ensure release notes reflect residual risk. Do not substitute synthetic probe for a human child test. A human playtest is strongly recommended before broad distribution; do not fabricate completion.

Do not add third region, missions, new gameplay currencies, combat, multiplayer or procedural region geometry. This milestone is a small release-polish pass, not a redesign.

## 7. Final report

Include PR URL, branch/merge/final SHAs, release URL/tag, full model integration list, 3 major changes (durable cache, bridge visuals, notifications), test counts, scene/runtime probe counts, exact-head CI URLs, screenshots, export SHA256, release archive re-verification, repo visibility and Git clean status. Explicitly list unverified Windows/fullscreen/child tests and residual limitations.

**Completion criterion:** Reedwater cache is durably one-time beyond ledger retention; both regions have attractive, clearly silk-lashed modular bridges, with unchanged player/worker collision; the UI does not obscure gameplay; regression tests and exported Linux acceptance pass; v0.2.0 release is published when its gates are met. Begin with investigation and implement end-to-end.
