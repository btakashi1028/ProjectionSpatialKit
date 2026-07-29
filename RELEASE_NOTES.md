# Projection Spatial Kit — experimental source snapshot (2026-07-29)

This is an exploratory Unity source snapshot accompanying the Zenn article about changing
direction from venue preview fidelity to calculable Preflight checks.

It is **not a UPM package** and does not replace validation with real projectors,
multi-display hardware, or URG sensors.

## Included

- Preview an unmodified Unity content scene in a separate venue scene
- Route content displays to virtual projectors and monitor surfaces
- Inject observer and ideal URG input through the New Input System
- Run Preflight checks for projection placement and display configuration
- Export the Preflight result as Markdown
- Touch Ripple sample content and venue

## Confirmed environment

- Unity 6000.3.x (developed with 6000.3.11f1)
- URP 17.x
- New Input System
- Development-project compilation: 0 errors, 0 warnings
- ProjectionSpatialKit EditMode tests: 38 passed, 0 failed

## Install

1. Download and extract `ProjectionSpatialKit-experimental-snapshot-2026-07-29.zip`.
2. Copy the extracted `ProjectionSpatialKit` folder, including `.meta` files, to
   `Assets/ProjectionSpatialKit` in the target Unity project.
3. Wait for compilation to finish.
4. Run `Projection Spatial Kit ▸ Run Project Setup`.
5. Open `Samples/Scenes/910_SampleVenue.unity` to try the included sample.

Do not overwrite an existing `Assets/ProjectionSpatialKit` folder. Use version control or
make a backup before importing.

## Status

Provided as-is under the MIT License. Ongoing maintenance, compatibility with future Unity
versions, and prompt Issue/PR support are not promised.
