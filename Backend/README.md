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

## Avatar expressions

The Unity frontend offers independent options for hand-sign expressions and named menu expressions, both disabled by default. They apply only to avatar output; model output does not receive expression controls. These options define new native controls rather than importing existing VRChat or Modular Avatar expression menus or Animator state machines.

Each configured AnimationClip contributes only sampled `SkinnedMeshRenderer` blendshape values at the specified time in seconds (default `0`, within `0` to `clip.length`). The frontend reports unsupported Transform, material, and active-state bindings, and rejects clips with no target blendshapes or unresolved target references. It retains binding targets on the temporary clone before Modular Avatar preprocessing so surviving objects can be identified after hierarchy changes. The initial expression uses the post-preprocessing scene values captured before expression sampling, not a snapshot of running native blink or viseme drivers. Unity blendshape percentages are converted to native weights once during serialization.

Hand-sign rows match a pair of left and right conditions. Each condition is one of the eight VRChat-style signs (shown in the UI as `Idle`, `Fist`, `Open`, `Point`, `Victory`, `RockNRoll`, `HandGun`, `ThumbsUp`) or `Any`. Both conditions must match; the first matching row wins. The finger-pose classifier approximates these signs from live finger data. The v0.3.0 controller path described below adds an input-based classifier for SteamVR Oculus/Meta Touch. Neither path guarantees identical VRChat behavior for every device or custom binding.

A menu is generated only when at least one valid named entry exists. It includes the initial-state entry automatically and adds a return-to-hand-signs entry when both expression features are enabled. Menu selections, including the initial state, override hand-sign selection until explicitly released. Returning to hand signs evaluates the current input. Expressions override only blendshapes explicitly keyed by the selected clip. Existing blink/viseme drives are redirected to live base fields so unkeyed shapes and released overrides continue to receive their automatic values.

Native Continuous Relay nodes explicitly propagate changing finger positions and rotations. The v0.2.0 verification package passed 81 expression checks, including 64 left/right pose combinations. Three input values were passed through the native blink ValueDriver, its live base field, and the original mesh weight. An isolated wearer with the native AvatarManager was used to exercise actual equip/dequip, wearer finger-source lookup, menu registration and removal, and independent selections on two avatar copies. These historical checks used test inputs and do not establish v0.3.0 controller-input or generated-hand-pose behavior. VR hardware gesture recognition and interactive menu use remain unverified.

The user-facing labels and setup procedure are documented in the [Japanese manual](../Assets/BEPFairyTech/ResoConverter/README-ja.md). See [verification notes](../VERIFICATION.md) for the implemented checks and any unverified interactive behavior.

## Controller hand poses in v0.3.0

The new option `UseControllerHandPoses`, shown as `コントローラー操作で手の形も切り替える`, defaults to true beneath the opt-in hand-expression setting. For SteamVR Oculus/Meta Touch, it distinguishes trigger contact from trigger pull and combines these with grip and thumb contact to select the eight gesture states. Thumb input uses joystick and face-button touch, with button presses and joystick clicks as fallbacks. ThumbRest touch is excluded, matching VRChat's SteamVR default Touch bindings. The result is a VRChat-like control scheme; it does not import the user's VRChat bindings. The seven named pose cases follow the [official Touch chart](https://docs.vrchat.com/docs/touch); other combinations use Idle.

When the controller path is active for the local wearer, it also supplies standard native poses for the visible fingers. The original hand-pose source takes precedence while that hand holds a Grabbable or has a tool. Menu expression priority affects facial blendshapes only, so fixing a face does not freeze the controller-driven hand pose. Disabling the new option retains the original hand control and selects expressions through the finger-pose classifier.

Vive and other controllers use the finger-pose classifier and their original hand control. Vive trackpad-to-eight-gesture mappings are not implemented in this version; no legacy or current VRChat Vive binding compatibility is claimed.

The v0.3.0 tracking gate addresses a v0.2.0 failure with valid `FingerPoseStreamManager` input whose finger positions can be zero. A zero position alone is insufficient evidence of missing tracking; the gate must consider the source's tracking state. This is separate from the Continuous Relay work recorded for v0.2.0.

Native roundtrip verification passed 88 expression checks, 25 raw Touch input cases, and 180 actual finger-bone rotation checks. Inputs were injected into native TouchControllerProxy ValueStreams, so these checks do not test a physical SteamVR device or another machine over the network. Controller-disabled and menu-only configurations also passed, and item output retained its ray/grab behavior without expression controls. Existing avatars require re-export from Unity to receive the new behavior. AnimationClip conversion remains limited to static blendshape samples.

## Save policy

The save policy removes protections generated by AvatarCreator using the same permission check and removal flag as its native inspector, then attaches one root protection only when requested. It never imports an arbitrary user Resonite object to change its permissions. Current local assembly API changes are checked during build, and failures stop export instead of producing a success result.

See `THIRD-PARTY-NOTICES.md` and `Native/THIRD-PARTY-LICENSE.md` for licensing.
