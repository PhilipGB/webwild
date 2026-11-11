# UI, input and accessibility specification

## Input model
| Key/mouse | Action |
|---|---|
| WASD / arrow keys | Move relative to camera |
| Space | Jump (or hop between nearby walkable platforms) |
| E | Interact with focused nest/trapped insect/repair target |
| Mouse | Camera aim / pointer on UI and anchor selection |
| Left click | Select anchors, choose UI action, confirm placement |
| Right click | Cancel build step / back one step |
| Mouse wheel | Zoom third-person camera |
| 1 | Capture web building tool |
| 2 | Silk bridge building tool |
| Tab | Colony overview panel; Tab again to close |
| Esc | Cancel active build, or pause/resume |

## Selection and focus
Use a visible pointer/cursor. When choosing a web, player navigation pauses or slows to make mouse anchor placement accessible; clicking a highlighted anchor should never rotate the camera. Nearby interactable has large selection proxy even if mesh is thin. When focus changes, show outline + action label. Cancellation must be consistent and idempotent.

## HUD
Top-left: **Food** icon + count and **Silk** icon + count (include text, not colour only). Top-right: current mission one-line goal and tick progress. Bottom-centre: two large build buttons + help; bottom-right small Tab colony icon. Context prompt near focused object. Error message fits 1–2 short lines and times out only after user had time to read.

## Build UI
Toolbar: Capture Web (cost), Silk Bridge (cost). First click locks anchor A, cursor hover previews anchor B and projected web. Green = valid, coral = invalid with plain reason ("Too far", "Already connected", "Need 6 silk"). Confirm/cancel explicit. A 7-year-old should be able to build the first web after the instruction and two clicks.

## Nursery and colony
Nursery panel: current number of spiders (e.g. 2/5), large Hatch button, exact food/silk costs, a small preview of new friendly spider. Disabled button explains why. Colony overview shows 1–5 named spider chips with simple icon/state (Collecting, Repairing, Resting). A collapsed mode exists for beginners.

## Pause and settings
Resume, Save, Load/Continue, New Game (confirm to avoid accidental loss), How to Play, Settings, Quit to Menu. Settings: master/music/SFX volumes, camera sensitivity, UI scale 100/125/150%, reduced motion, optional low threat mode, full screen/resolution. Support pause state correctly; input behind modal is disabled.

## Accessibility requirements
- Minimum 18–20 px body text at 1080p; headline 24+ px; target buttons ≥ 52 px high (effective click area). Avoid huge paragraphs and tightly packed small controls.
- Use friendly, high-contrast UI in green/cream/dark ink, with icons AND labels and shapes/text for warning severity (not red/green alone).
- Default gentle weather, no countdown in core mission that causes failure, no irreversible destructive decisions. Allow reduced motion + mute without loss of gameplay information.
- Keyboard-accessible focus for menus where feasible; Esc navigation consistent; no double-click/drag precision requirements in story flow.
- Save/success errors explicit; if save failed, "Couldn't save. Try again" not "Saved".

## Included mock-ups
`assets/ui/hud_mockup.svg`, `assets/ui/web_building_mockup.svg`, `assets/ui/nursery_mockup.svg`, `assets/ui/pause_mockup.svg` are visual references; implement using native Godot `Control` nodes, not screenshots overlaid on the world.
