# Risks and scope boundaries

| Risk | Typical failure | Mitigation |
|---|---|---|
| Free-form climbing ambition | Camera and gravity bugs consume entire project | Pre-authored assisted climb surfaces, good collisions, otherwise grounded locomotion |
| Tangled graph/physical worlds | Workers teleport or never reach web | Small hand-authored graph with explicit edge IDs; runtime verification |
| Web visual vs capture mismatch | Fly catches without intersecting shown strands | Share plane basis/extent between geometry and trigger; runtime tests |
| Fiction vs actual biology | Claims all spiders live in worker castes | Explicitly fictional cooperative spiders; avoid inaccurate educational claims |
| Resource softlock | Player runs out of silk before successful trap | Tutorial refund and limited nursery silk rescue, tested negative cases |
| C# export problems | Standard Godot binary/editor loaded accidentally | Pin 4.7.2 .NET executable + matching templates, preflight early |
| Scope creep | Five-region RPG built instead of finished loop | Strict vertical slice, P0–P8 gates, non-goals list |
| Code-only success | Everything compiles but HUD is unusable | Manual screenshot inspection and end-to-end gameplay acceptance |
| Fake tests / fabricated evidence | Claims pass without running | Record commands, stdout, exit code, counts and real screenshots |
| Save loss | Corrupt/partial files or stale references | Versioned validation, atomic backups and recovery tests |
| Licensing | Reused online textures or music lack rights | Original GLB, generated concept references, attribution checklist |

## Deferred backlog (not first playable)
Wildlife predator encounters; five complete regions; multiplayer; complex genetics; realistic orb web physics; rich day/night seasons; Web Sense specialist system; shop/customisation; mobile/web ports; elaborate worker stat trees; procedural open world; competitive ranking; cloud saves.

## Quality trade-off rule
When schedule or agent context becomes constrained, cut deferred breadth, never essential gameplay integration or data-loss safety. Finish the first playable loop, polish and verification before optional extras.
