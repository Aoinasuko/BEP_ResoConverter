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

Settings are JSON: `asAvatar`, `lockSaving` (default true), `useStandardSize`, `standardHeight` (meters, default 1.8), optional `expressions`, optional `eyeLook`, and optional `verifyAfterExport`. Verification reimports the finished package in the isolated engine, loads all exported mesh assets and writes `.inspection.json` with component counts, selected blink targets, the measured model height, and expression checks when configured. Source renderers are tracked explicitly so helper objects do not affect bounds.

PhysBone parameters use an approximation because the native solvers differ. Pull maps to chain elasticity, spring to damping, stiffness and immobility to chain stiffness and inertia, and gravity to a world-space gravity vector. Radius curves are mapped per bone. Native DynamicBoneChain currently offers no per-bone elasticity/damping/stiffness fields, so the original scalar values and sampled curves are retained in a native ValueField reference record. Capsule colliders use a bounded chain of overlapping native spheres. Angle, Hinge and Polar limits are applied to the visible skeleton as described below. Unsupported settings and animator logic are reported by the Unity frontend.

## PhysBone angular limits in v0.3.2

The protobuf schema carries `DynamicBone.limit_type` and `multi_child_type`, plus sampled `max_angle_x`, `max_angle_z` and `limit_rotation` on each node. The editor reads these settings through reflection and retains compatibility with projects without the VRC SDK. Angular curves use VRC's simulated-segment ratio, excluding the final tip from the denominator. Rotation curves multiply the XYZ Euler components independently; their quaternion uses Euler XYZ order rather than Unity's default `Quaternion.Euler` order. Original source settings remain in the conversion report.

`ignoreOtherPhysBones` excludes another PhysBone's resolved root and its subtree, including when that other component is disabled. A missing root reference resolves to the component Transform. Branch policies are preserved: First uses the first eligible child direction, Average uses the mean child direction, and Ignore leaves the branch junction undriven while retaining the required ancestor topology for descendant segments. Explicit endpoint offsets are exported, and native `SimulateTerminalBones` is disabled so the engine does not invent further segments. A single terminal node with a zero endpoint receives no simulated swing.

The native converter creates a private simulation skeleton and standard ProtoFlux controls that apply Angle, Hinge or Polar bounds to the visible bones. Resonite users need no additional MOD or custom runtime component. Collision and grab calculations still use the simulation skeleton before the visible angular clamp, so their positions can differ from the visible bones near a limit. This limitation is reported once by the frontend and retained in native metadata. Nonuniform or negative scales also make world-space angular bounds approximate. This implementation does not reproduce the complete VRChat physics solver.

A limited PhysBone rooted at the export root itself is rejected with an explicit error; its root must be a child bone beneath the selected avatar or model root. The `physBoneLimits: 1` payload capability prevents the frontend from silently exporting limited PhysBones with an older backend that would ignore the added protobuf fields. Existing native packages must be exported again from their Unity source to receive these changes.

Unity regression checks cover the four limit kinds, protobuf transport, per-segment curves, the installed SDK's limit-axis calculation, and nested PhysBone boundaries. The optional-package project passed all 74 EditMode tests; the project without those packages passed 54 tests with 20 optional integration cases skipped. Native verification results are maintained separately in [VERIFICATION.md](../VERIFICATION.md).

Source VRC visemes override the native avatar builder's automatic detection. If source visemes are unavailable, standard blendshape-name detection remains available. The selected blink shape is authoritative; an empty selection removes inferred eyelid drives. Jaw-bone lip sync uses a native voice volume meter and quaternion gradient.

## Avatar expressions

The Unity frontend offers independent options for hand-sign expressions and named menu expressions, both disabled by default. They apply only to avatar output; model output does not receive expression controls. These options define new native controls rather than importing existing VRChat or Modular Avatar expression menus or Animator state machines.

Each configured AnimationClip contributes only sampled `SkinnedMeshRenderer` blendshape values at the specified time in seconds (default `0`, within `0` to `clip.length`). The frontend reports unsupported Transform, material, and active-state bindings, and rejects clips with no target blendshapes or unresolved target references. It retains binding targets on the temporary clone before Modular Avatar preprocessing so surviving objects can be identified after hierarchy changes. The initial expression uses the post-preprocessing scene values captured before expression sampling, not a snapshot of running native blink or viseme drivers. Unity blendshape percentages are converted to native weights once during serialization.

Hand-sign rows match a pair of left and right conditions. Each condition is one of the eight VRChat-style signs (shown in the UI as `Idle`, `Fist`, `Open`, `Point`, `Victory`, `RockNRoll`, `HandGun`, `ThumbsUp`) or `Any`. Both conditions must match; the first matching row wins. The finger-pose classifier approximates these signs from live finger data. The v0.3.0 controller path described below adds an input-based classifier for SteamVR Oculus/Meta Touch. Neither path guarantees identical VRChat behavior for every device or custom binding.

A menu is generated only when at least one valid named entry exists. It includes the initial-state entry automatically and adds a return-to-hand-signs entry when both expression features are enabled. Menu selections, including the initial state, override hand-sign selection until explicitly released. Returning to hand signs evaluates the current input. Expressions override only blendshapes explicitly keyed by the selected clip. Existing blink/viseme drives are redirected to live base fields so unkeyed shapes and released overrides continue to receive their automatic values.

Native Continuous Relay nodes explicitly propagate changing finger positions and rotations. The v0.2.0 verification package passed 81 expression checks, including 64 left/right pose combinations. Three input values were passed through the native blink ValueDriver, its live base field, and the original mesh weight. An isolated wearer with the native AvatarManager was used to exercise actual equip/dequip, wearer finger-source lookup, menu registration and removal, and independent selections on two avatar copies. These historical checks used test inputs and do not establish v0.3.0 controller-input or generated-hand-pose behavior. VR hardware gesture recognition and interactive menu use remain unverified.

The user-facing labels and setup procedure are documented in the [Japanese manual](../Assets/BEPFairyTech/ResoConverter/README-ja.md). See [verification notes](../VERIFICATION.md) for the implemented checks and any unverified interactive behavior.

## Controller hand poses in v0.3.0

As of v0.3.3, the controller and tracked-pose classifiers are only used in VR. Desktop hand expressions use the dedicated keyboard path described below, including when `UseControllerHandPoses` is disabled. The option is now shown as `VRのコントローラー操作で手の形も切り替える`.

The new option `UseControllerHandPoses`, shown as `コントローラー操作で手の形も切り替える`, defaults to true beneath the opt-in hand-expression setting. For SteamVR Oculus/Meta Touch, it distinguishes trigger contact from trigger pull and combines these with grip and thumb contact to select the eight gesture states. Thumb input uses joystick and face-button touch, with button presses and joystick clicks as fallbacks. ThumbRest touch is excluded, matching VRChat's SteamVR default Touch bindings. The result is a VRChat-like control scheme; it does not import the user's VRChat bindings. The seven named pose cases follow the [official Touch chart](https://docs.vrchat.com/docs/touch); other combinations use Idle.

When the controller path is active for the local wearer, it also supplies standard native poses for the visible fingers. The original hand-pose source takes precedence while that hand holds a Grabbable or has a tool. Menu expression priority affects facial blendshapes only, so fixing a face does not freeze the controller-driven hand pose. Disabling the new option retains the original hand control and selects expressions through the finger-pose classifier.

Vive and other controllers use the finger-pose classifier and their original hand control. Vive trackpad-to-eight-gesture mappings are not implemented in this version; no legacy or current VRChat Vive binding compatibility is claimed.

The v0.3.0 tracking gate addresses a v0.2.0 failure with valid `FingerPoseStreamManager` input whose finger positions can be zero. A zero position alone is insufficient evidence of missing tracking; the gate must consider the source's tracking state. This is separate from the Continuous Relay work recorded for v0.2.0.

Native roundtrip verification passed 88 expression checks, 25 raw Touch input cases, and 180 actual finger-bone rotation checks. Inputs were injected into native TouchControllerProxy ValueStreams, so these checks do not test a physical SteamVR device or another machine over the network. Controller-disabled and menu-only configurations also passed, and item output retained its ray/grab behavior without expression controls. Existing avatars require re-export from Unity to receive the new behavior. AnimationClip conversion remains limited to static blendshape samples.

## Desktop hand poses in v0.3.3

Desktop input uses Left Shift + Alpha1–Alpha8 for the left hand and Right Shift + Alpha1–Alpha8 for the right hand. Each chord is active only while both keys are held. The eight states are Idle, Fist, Open, Point, Victory, RockNRoll, HandGun and ThumbsUp. An unoperated hand is treated as Idle for rule matching, but no hand rule, including Any/Any, is selected unless at least one chord is active. Normal desktop locomotion and pose-stream changes cannot select expressions. Multiple number keys choose the lowest number; both Shift keys apply that same state to both hands.

The keyboard path is generated whenever hand-expression rows are exported. It supplies poses only for operated hands, preserves the original pose while holding an item/tool, and retains menu expression priority. Released input returns expression targets to their live baseline unless the menu has fixed an expression. The `desktopHandGestures: 1` payload flag prevents a frontend from exporting this behavior with an older backend. Existing packages must be re-exported from Unity.

Standard `KeyHeld` nodes suppress keyboard reads during text focus, Userspace focus and unfocused worlds. An `Update` node assigned to the wearer, with `SkipIfNull` enabled, writes the result into synchronized fields. Observers read those fields instead of evaluating their own keyboard for the avatar. The final selection also checks the wearer's known output device and current `VR_Active` state, so desktop pose streams cannot fall through to the VR classifier. Headset users can switch into desktop without changing their initially reported output device.

## Save policy

The save policy removes protections generated by AvatarCreator using the same permission check and removal flag as its native inspector, then attaches one root protection only when requested. It never imports an arbitrary user Resonite object to change its permissions. Current local assembly API changes are checked during build, and failures stop export instead of producing a success result.

See `THIRD-PARTY-NOTICES.md` and `Native/THIRD-PARTY-LICENSE.md` for licensing.

## Eye look and blink conflicts in v0.3.1

`eyeLook` has an explicit `configured` flag to distinguish an absent VRC descriptor from a disabled VRC Eye Look. The frontend sends per-eye object IDs and authored local quaternions for straight/up/down/left/right without referencing SDK assemblies. When configured, the backend replaces the avatar builder's generic 10-degree eye pivots with standard ProtoFlux quaternion interpolation. Head-relative gaze is sampled continuously; the eye origins retain their original head hierarchy. Each direction reaches its authored endpoint at a 30-degree input gaze angle and clamps outside that response range. This preserves the authored poses, not VRChat's complete attention/eye-tracking behavior.

A separate eyelid expression can deform the same vertices as automatic blinking even when the blendshape names differ. Conversion identifies shared position deltas on the same mesh and gates the selected blink while a conflicting expression is active. Menu priority and return to current automatic values are retained. A neutral zero key on a zero-baseline blink channel preserves automatic blinking; nonzero explicit blink values still override it.

The frontend defaults `ToonShadowStrength` to 0.5 and bakes a lighter toon shadow ramp. This applies to lilToon and VRChat Mobile Toon Standard, which is now mapped to XiexeToonMaterial instead of PBR. Ordinary PBR lighting and world lights are unchanged. The feature marker is `facialExpressions: 3` and `avatarEyeLook: 1` so an older backend cannot silently ignore the face fixes.
