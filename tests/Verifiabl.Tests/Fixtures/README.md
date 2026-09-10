# Cross-SDK fixtures

These committed files are generated compatibility contracts, not hand-authored test data:

- `node-png-*.rgba.deflate` and `node-png-meta.json` contain exact Node compositor rasters and metadata.
- `node-svg-*.svg` and `node-svg-meta.json` contain exact Node SVG output and metadata.
- `src/Verifiabl/Assets/frame-*.vfr1` contains the equivalent embedded frames used by the .NET PNG compositor.

Maintainers regenerate all parity fixtures and both SDKs' frame assets together. The metadata records
the generator, source SDK version, and exact relevant tool versions. This repository retains every
generated input it needs, so it remains independently buildable and testable.

`NodeSdkParityTests` consumes only files in this repository and requires exact metadata, SVG, and
RGBA parity. Any deliberate change to the compositor, badge frame, fixture policy, or encoded scan
URL must include a coordinated update to the complete generated artifact set. The Node SDK
independently checks that its committed frame matches a fresh render of its live SVG.

The default cases cover the v2 short-host writer, including its explicit byte/alphanumeric segment
split. The `v1-default` cases retain compatibility coverage for the explicit rollback writer.
