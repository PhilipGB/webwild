# ADR 0001 — Never-Ending World (v0.3.0)

Status: Accepted · Applies to: v0.3.0 (P0–P9) · Supersedes the "two authored regions"
world model of v0.2.x where noted.

## Context

v0.2.2 is two hand-authored regions (Garden Clearing, Reedwater Bank) with fixed
scene sets, small navigation graphs, ±60-unit position validation, a 512 KB save
cap and region-id coupling in the simulation, worker logic and save schema. The
v0.3.0 milestone replaces this with an indefinitely expanding, procedurally
generated world while preserving every existing mission, mechanic, control and
save. This record fixes the architecture decisions each phase must follow.

## Decisions

### 1. World coordinates

- The logical world uses unbounded signed metres (`double`) for authoring and
  simulation, decomposed for persistence into a **signed 64-bit chunk coordinate
  pair plus local doubles** (`WorldPos`), with local X/Z always in `[0, 24)`.
  Height (`Y`) is stored absolutely because it is naturally bounded.
- Chunk size is **24 m** with a 13-point terrain grid (2 m spacing).
- Rendering and physics use a bounded **engine-space** derived as
  `logical − origin` (`WorldOrigin`). When the player travels beyond ~16k m in
  engine space, the origin is rebased onto a 64 m grid near the player. All
  engine-space values therefore stay inside comfortable float32 precision;
  logical values keep full double precision forever.
- Floor semantics are symmetric across zero: x = −0.1 is chunk X = −1, local 23.9.
  This is covered by round-trip and boundary tests, including negative chunks.
- Player, worker and marker saves store `WorldPos`, never a bare float triple
  that grows without bound.

### 2. Chunk identity and ownership

- `ChunkCoordinate(long X, long Z)` is the only chunk identity. Its canonical
  string form (`"<X>_<Z>"`) keys save files, the exploration record and maps.
- A chunk's **baseline content is owned by the generator** (seed + coordinates)
  and is never saved. A chunk's **player-made changes are owned by the save
  system** as a small versioned `ChunkChangeSave` record (built webs,
  modified food sources, markers). Nothing else about a chunk is durable state.
- Authored landmarks own their local frames; they do not own chunks. Procedural
  chunks around them are ordinary generated chunks.

### 3. Deterministic generation

- Every world seed is persisted in the manifest and controls all generation.
- Terrain sampling is a pure function of `(seed, absolute x, z)`, not of chunk
  indices, so neighbouring chunks always agree at shared boundaries and the
  same location regenerates identically after any unload/reload cycle, in any
  order, in any process (`TerrainSampler`, `DeterministicHash`).
- Prop, biome and water placement are seeded deterministically per feature
  (chunk + ordinal + kind), never from wall-clock or exploration order.
- Content identity is proven by `ChunkData.ContentHash()` compared across
  regeneration in tests; rendered comparisons are added from P1 onward.

### 4. Authored landmark integration

- `LandmarkCatalog` gives each authored environment a constant world origin.
  Garden Clearing sits at the world origin (legacy garden coordinates are world
  coordinates); Reedwater Bank sits at (96, 0, 0), east of the garden's
  east-hedge passage, across two to three chunks of connecting terrain.
- These origins are a **save contract**: changing them would move authored
  landmarks under every existing manifest save.
- Story logic keys on landmark/zone identifiers. The procedural world is not
  registered through the finite `RegionIds` catalogue; new procedural content
  must not require new region entries. Migration and mission sequencing keep
  the existing mission and objective IDs untouched.
- Travel between landmarks becomes ordinary walking (P2). The old travel
  dialog is replaced by physical connectivity; unlock gating is expressed
  through the environment and objective availability, never teleports.

### 5. Chunk loading and unloading

- A `WorldStreamer` maintains a bounded active set of chunks around the player
  (load radius ahead of movement, retention radius behind). Generation may run
  off-thread; all Godot scene-tree and physics-server changes happen on the
  main thread per engine threading rules.
- Terrain visuals, collision and decorations are node lifetimes; logical world
  data and persistence records outlive them. Unloading disposes Godot nodes and
  releases meshes; it never forgets construction state.
- Required invariants (tested in P1+): no gaps or height seams at boundaries,
  terrain ready before the player reaches an edge, no duplicate props, no
  collision discontinuities, no unbounded node accumulation, identical
  regeneration after unload.

### 6. Save storage

- The manifest (`world.json`, world schema **4**) is small: seed, save identity,
  tick, player `WorldPos`, active landmark, landmark visits, exploration record
  and the embedded colony payload (`SaveGameData` at its established schema-3
  semantics, so the existing migration chain and fixtures stay untouched).
- Per-chunk player-made changes live in separate files
  (`chunks/chunk_<X>_<Z>.json`), each small, versioned and independently
  replaceable. Regeneratable terrain is never written, so save size grows only
  with real player impact.
- All writes are atomic (temp file → validate → backup rotate → move), with
  leftover temp files cleaned on load; corrupt records are ignored, not fatal,
  because baseline content regenerates. The rotating `.bak` and the durable
  `.previous` New-Garden archive behaviour are preserved.
- Budgets: manifest ≤ 512 KB (as before); each chunk record ≤ 64 KB. Validation
  bounds are explicit, with fail-closed rejection, never silent trimming of
  gameplay-relevant data.

### 7. Worker navigation

- The tiny explicit `WorldGraph` remains authoritative **inside authored
  landmarks**. For procedural terrain a hierarchical scheme is added (P7):
  - **Local layer**: continuous movement/steering over nearby terrain and
    obstacles, including bridge decks and climbable surfaces.
  - **World layer**: chunk-to-chunk corridors and key waypoints (bridges,
    landmarks) searched coarsely, never over every surface triangle.
- Workers record the actual outbound route they used; returning prefers that
  route in reverse, then re-searches, then waits safely with a visible blocked
  state. Nothing teleports a worker or grants resources from an unreachable
  destination.

### 8. Simulation outside the visible world

- Logical worker simulation (task, route progress, arrival, collection,
  delivery) is decoupled from rendered Godot actors (P8). Distant workers make
  measurable progress with cheap logical updates and reappear at consistent
  positions when their chunks load again.
- Off-screen simulation must respect the same rules as on-screen: valid routes
  only, exactly-once resource delivery, blocked-route waiting, no physics or
  animation cost for unrendered workers, and no requirement to keep travelled
  chunks resident.
- Off-thread generation and world-layer navigation must not mutate
  `GameSimulation` concurrently; simulation ticks remain single-threaded on the
  main thread.

### 9. Resource regeneration

- Procedural food sources have stable world identities, per-source availability
  state and deterministic regeneration timers that advance with the
  simulation clock, not with visibility. A chunk may unload and the state
  still resolves correctly on return: availability is recomputed from the
  persisted state and the tick, never reset by walking away.
- Only *differing-from-baseline* food state is persisted. Untouched procedural
  objects never create save records.
- Collection idempotency keys follow the existing ledger pattern
  (`food:<source>:<generation>`), preventing duplicate credit across collectors,
  save/load, unload/reload and regeneration boundaries.

### 10. Migration from existing saves

- Legacy schema 1–3 files are wrapped into a schema-4 manifest by
  `WorldSaveMigrator` with exactly one semantic change: the player position
  becomes a world position through the constant landmark origins. The embedded
  payload is byte-compatible with the old model.
- v0.1.x → v0.2.x migrations continue to run first (unchanged `SaveMigrator`),
  then the manifest wrapper applies. All committed legacy fixtures must
  migrate, load and validate at every phase.
- Old saves remain loadable by the old model during P0–P1; the write path
  switches to manifests in P2 after both readers are proven.

## Consequences

- `GameSimulation` keeps its current role as colony simulation; world-scale
  services (generator, streamer, manifest store) are separate engine-free
  types in `Webwild.Core`, so unit tests cover the world without Godot.
- The per-region position catalogues, ±60 validation and region-coupled worker
  logic are superseded incrementally; each phase must keep the six missions and
  existing regression suites green before merging.
- New features that would tempt speculative systems (currencies, hostile
  colonies, destructible voxels) remain out of scope for v0.3.0 by design.
