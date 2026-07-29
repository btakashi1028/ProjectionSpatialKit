# Snapshot notes

All notable changes to Projection Spatial Kit are documented here.
The identifiers below describe development snapshots, not a compatibility promise.

## [0.2.0] - 2026-07-29

Repositioned around what the kit is actually good at: **judging a venue by calculation**
rather than previewing it. The Editor's preview limits (one Game View resolution at a time,
Editor ≠ build) do not apply to analysis, so this is where the kit becomes useful in practice.

### Added
- **Preflight Check** (`Projection Spatial Kit ▸ Preflight Check`, also on the Scene overlay):
  verifies projection placement and screen configuration in Edit mode — no Play, no rendering —
  and reports pass / warn / fail with measured numbers and a concrete fix. Exports to Markdown
  so the verdict can be taken on site or handed over.
  - *Placement*: image corners landing on the intended surface, landed size vs target width and
    the move needed, **whether the required throw ratio is within the projector model's range**
    (and how far to move if not), lens shift beyond the model's limit, incidence angle, focus
    distance error, and how much of the image an obstacle blocks.
  - *Screens*: devices pointed at undefined channels, channels with no device, portrait/landscape
    mismatch, panel-vs-channel aspect mismatch, Observer/content display collision, content scene
    Build Settings registration, and Game View resolution vs channel resolution.
  - Throw-ratio model matching is **withheld at steep incidence**, where the spec value stops
    describing reality — otherwise an aiming problem would be reported as "buy another projector".
- `ProjectedImageFootprint` + `VirtualProjectorLight.TryTraceImage` / `GetImageRayDirection`:
  public geometry for where the image actually lands (distance, size, incidence, spill).
  The rendered picture, the Scene gizmo and this trace now share one quad, so they cannot
  disagree — the gizmo previously ignored lens shift and keystone.

## [0.1.0] - 2026-07-28

Initial extracted snapshot.

### Added
- Venue simulation: load an unmodified content scene additively and project it without touching the content.
- `VirtualProjectorLight`: light-based projector (URP spot light + cookie) driven by throw ratio, zoom, lens shift, keystone and focus; the image lands wherever the frustum hits.
- `MonitorSurface`: self-emissive LCD/OLED panel, optional touch, `Match Content Aspect` for portrait/landscape content.
- Detector-driven touch: URG rigs sense their projection, touch monitors sense themselves; projector-only setups are display-only.
- `OutputRouter` / `ContentScreenCaptureSource`: one capture channel per content display, arbitrary resolution (portrait supported).
- `TouchInjectionHub`: injects observer clicks / URG points / scripted demo as New Input System Touchscreen + Mouse, with per-display `displayIndex` and channel-resolution coordinates.
- Scene-view overlay: setup diagnostics, venue creation, device ↔ display mapping, per-channel resolution, projection aiming, monitor touch, play controls.
- `Run Project Setup`: diagnose/repair URP, Input System, venue layer, cookie shader inclusion, and a venue renderer (self-healing against missing/duplicate entries); the host's default renderer is never changed automatically.
- Content scene auto-registered in Build Settings when a venue/sample scene is created, so the additive load is reliable on a cold Editor open and in builds; a setup-diagnostic check + Fix flags a missing registration, and the loader warns when it has to fall back.
- Editor tests for the projection math, URG conversion, room mesh, and injection coordinates.
- Sample: Touch Ripple content + a venue scene that projects it.

### Known limitations
- Multiple content displays with MIXED aspect (e.g. portrait 1080×1920 + landscape 1920×1080) preview poorly in the Editor because the Game View shows one resolution at a time. Match each display's Game View resolution to its channel, or verify in a build; builds and single-display setups are unaffected.

### Requirements
- Unity 6000.3, URP 17.x, Input System package. OpenCV calibration (`Runtime/Vision`) is optional and gated behind the `PROJECTION_SPATIAL_KIT_OPENCV` define.
