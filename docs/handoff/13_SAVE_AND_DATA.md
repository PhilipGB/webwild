# Persistence and JSON data contract

## Save state
Recommended `user://saves/slot1.json` JSON, versioned. Minimum: schemaVersion, saveId, created/updated UTC timestamps, deterministic seed/clock, player transform (bounded and validated), food/silk, active webs (stable IDs, anchor IDs, type, durability), spider roster/task state (safe resumable intent only), nursery and objective progress, weather state/timing, settings. Store counts and completed event IDs to prevent duplicate rewards.

## Critical validation
Reject unknown type IDs, negative resources, NaN/Infinity coordinates, duplicate web IDs, missing anchors, invalid durability, spider count >5, impossible mission combinations and extreme file sizes. Never trust external save JSON. For version mismatch, provide a limited explicit migration or clear non-destructive "Cannot load this save" notice; don't quietly reset progress.

## Atomic writes
Write new bytes to sibling temp path, flush/close, validate, rotate healthy prior main to `.bak` and replace main via file API with appropriate platform semantics; preserve backup if writing fails. Do not blindly delete healthy data. If write fails, show exact user-safe error message and leave game playable. Test permissions/read-only and corrupted file cases.

## Recovery sequence
On Continue: try main and validate; if invalid, try backup and validate; if backup loads, inform player the last saved version was recovered and restore to main on a subsequent safe save. If both invalid, offer New Game without overwriting corrupt artifacts until explicit confirmation. No false "Saved" UI.

## Save timing
Initial New Game after scene initialises, autosave on mission completion/hatch/build/wind repair and at safe intervals (e.g. 2–3 minutes), manual Save from pause; throttle rapid changes. Pausing saves exact simulation state; no offline simulation while quit. No network dependency.

## Definitions
Author mission and tuning inputs as JSON in `data/`, load with validation, and keep gameplay balance numeric values in one place. The attached JSON files are examples to adapt, not a claim the game already uses these files.
