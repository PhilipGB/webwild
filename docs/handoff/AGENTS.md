# Repository agent instructions — Webwild

You are building Webwild first playable as defined by the versioned handoff documents. Read all `docs/handoff/` materials if available and follow `01_AGENT_MASTER_PROMPT.md` as the source operating contract.

- C#/.NET only for game scripts; Godot 4.7.2 .NET and `net10.0`; no quiet shift to GDScript.
- Complete vertical slice before extra features: one clearing, spider locomotion, 2 web types, insects, food/silk, 5 spider cap, nursery, workers, wind, 3 missions, save/load, polish.
- Pure rules independent of Godot; deterministic unit tests, runtime scenes tested headlessly AND visual/manual checks.
- No fabricated PASS counts or screenshots. No arbitrary online services, licensed assets without attribution, or public repos.
- Correctness and save integrity before cosmetic extras; stable main and milestone branch/PR workflow.
- Every milestone must end with executable evidence, updated docs and clear known limitations.
