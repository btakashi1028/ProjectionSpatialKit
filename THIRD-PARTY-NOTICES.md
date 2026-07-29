# Third-party notices

Projection Spatial Kit bundles or optionally depends on the following third-party components.

## JetBrains Mono (bundled)

- Path: `Fonts/JetBrains_Mono/`
- License: SIL Open Font License 1.1 — see `Fonts/JetBrains_Mono/OFL.txt`
- Used for the Scene-view info plates. The OFL text is kept alongside the font as required.

## OpenCvSharp (optional, NOT bundled)

- Assembly: `Runtime/Vision/` (`ProjectionSpatialKit.Vision.asmdef`)
- The camera/projector calibration code in `Runtime/Vision` references `OpenCvSharp.dll`, but the
  assembly is compiled **only** when the scripting define `PROJECTION_SPATIAL_KIT_OPENCV` is set
  (`defineConstraints`). No OpenCvSharp binary is shipped inside this kit.
- To use the Vision features, add OpenCvSharp (and its native `OpenCvSharpExtern`) to your project
  yourself and define `PROJECTION_SPATIAL_KIT_OPENCV`. Comply with OpenCvSharp's and OpenCV's own
  licenses. Without the define the rest of the kit builds and runs normally.

## Unity packages (dependencies, not bundled)

- Universal Render Pipeline (`com.unity.render-pipelines.universal`)
- Input System (`com.unity.inputsystem`)

These are resolved by the Unity Package Manager in the host project and are not redistributed here.
