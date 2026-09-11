# Native backend

This directory contains the native package converter, adapted from the MIT-licensed [Modular Avatar Resonite backend](https://github.com/bdunderscore/modular-avatar-resonite). It does not load Unity, Modular Avatar or lilToon. The Unity editor exports scene data through the protobuf schema; a separate headless Resonite engine constructs and packages native components and assets.

The vendored baseline is upstream commit `6370bef022a7ca29511dedaef3f5d29892c6269e` (0.0.48). The BEP changes are the settings/CLI integration, dependency-free editor schema transport, current Resonite API compatibility fixes, facial controls, direct material mappings, physics conversion, permission selection, bounds calculation, and package verification.

The backend resolves proprietary engine assemblies from the user's installed Resonite directory. Those assemblies are not redistributed. The shipped Windows x64 payload includes the .NET runtime and needs no separate SDK/runtime installation. Unity editor dependencies are generated in `Editor/Plugins` and the executable payload is stored in `Editor/BackendPayload.bytes` so Unity packages include it.

Build with .NET SDK 10 and an up-to-date Resonite installation:

```powershell
.\Backend\build.ps1 -ResonitePath 'PATH_TO_RESONITE_INSTALLATION'
```

The build script compiles the standalone Unity protobuf runtime under `UnityProtobuf`, regenerates the schema, publishes the backend, checks that proprietary engine assemblies were not included, and rebuilds `BackendPayload.bytes`.

Native CLI:

```text
Launcher.exe --input scene.pb --output avatar.resonitepackage --settings settings.json --resonite-install-path RESONITE_DIRECTORY --temp-directory ISOLATED_WORK_DIRECTORY
```

Settings are JSON: `asAvatar`, `lockSaving` (default true), `useStandardSize`, `standardHeight` (meters, default 1.8), optional `expressions`, and optional `verifyAfterExport`. Verification reimports the finished package in the isolated engine, loads all exported mesh assets and writes `.inspection.json` with component counts, selected blink targets, the measured model height, and expression checks when configured. Source renderers are tracked explicitly so helper objects do not affect bounds.

PhysBone parameters use an approximation because the native solvers differ. Pull maps to chain elasticity, spring to damping, stiffness and immobility to chain stiffness and inertia, and gravity to a world-space gravity vector. Radius curves are mapped per bone. Native DynamicBoneChain currently offers no per-bone elasticity/damping/stiffness fields, so the original scalar values and sampled curves are retained in a native ValueField reference record. Capsule colliders use a bounded chain of overlapping native spheres. Unsupported limits and animator logic are reported by the Unity frontend.

Source VRC visemes override the native avatar builder's automatic detection. If source visemes are unavailable, standard blendshape-name detection remains available. The selected blink shape is authoritative; an empty selection removes inferred eyelid drives. Jaw-bone lip sync uses a native voice volume meter and quaternion gradient.

## Avatar expressions in v0.2.0

The Unity frontend offers independent options for hand-sign expressions and named menu expressions, both disabled by default. They apply only to avatar output; model output does not receive expression controls. These options define new native controls rather than importing existing VRChat or Modular Avatar expression menus or Animator state machines.

Each configured AnimationClip contributes only sampled `SkinnedMeshRenderer` blendshape values at the specified time in seconds (default `0`, within `0` to `clip.length`). The frontend reports unsupported Transform, material, and active-state bindings, and rejects clips with no target blendshapes or unresolved target references. It retains binding targets on the temporary clone before Modular Avatar preprocessing so surviving objects can be identified after hierarchy changes. The initial expression uses the post-preprocessing scene values captured before expression sampling, not a snapshot of running native blink or viseme drivers. Unity blendshape percentages are converted to native weights once during serialization.

Hand-sign rows match a pair of left and right conditions. Each condition is one of the eight VRChat-style signs (shown in the UI as `Idle`, `Fist`, `Open`, `Point`, `Victory`, `RockNRoll`, `HandGun`, `ThumbsUp`) or `Any`. Both conditions must match; the first matching row wins. Recognition approximates these signs from live finger poses rather than equating them to Resonite's internal `FingerPosePreset` enum values. Device input and finger positions can affect the thresholds and resulting classifications; identical VRChat behavior is not guaranteed.

A menu is generated only when at least one valid named entry exists. It includes the initial-state entry automatically and adds a return-to-hand-signs entry when both expression features are enabled. Menu selections, including the initial state, override hand-sign selection until explicitly released. Returning to hand signs evaluates the current input. Expressions override only blendshapes explicitly keyed by the selected clip. Existing blink/viseme drives are redirected to live base fields so unkeyed shapes and released overrides continue to receive their automatic values.

Native Continuous Relay nodes explicitly propagate changing finger positions and rotations. The final package roundtrip passed 81 expression checks, including 64 left/right pose combinations. Three input values were passed through the native blink ValueDriver, its live base field, and the original mesh weight. An isolated wearer with the native AvatarManager was used to exercise actual equip/dequip, wearer finger-source lookup, menu registration and removal, and independent selections on two avatar copies. These are engine-level checks with test inputs; VR hardware gesture recognition and interactive menu use remain unverified.

The user-facing labels and setup procedure are documented in the [Japanese manual](../Assets/BEPFairyTech/ResoConverter/README-ja.md). See [verification notes](../VERIFICATION.md) for the implemented checks and any unverified interactive behavior.

## Save policy

The save policy removes protections generated by AvatarCreator using the same permission check and removal flag as its native inspector, then attaches one root protection only when requested. It never imports an arbitrary user Resonite object to change its permissions. Current local assembly API changes are checked during build, and failures stop export instead of producing a success result.

See `THIRD-PARTY-NOTICES.md` and `Native/THIRD-PARTY-LICENSE.md` for licensing.
