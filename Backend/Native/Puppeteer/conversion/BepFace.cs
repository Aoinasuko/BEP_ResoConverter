using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using p = nadena.dev.ndmf.proto;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

public partial class RootConverter
{
    private async Task SetupBepBlink(p.AvatarDescriptor spec)
    {
        // The picker is authoritative, including the explicit "do not use" option.
        // Release only automatically inferred eyelid drives; eye gaze remains intact.
        foreach (var automatic in _root.GetComponentsInChildren<EyeLinearDriver>())
            foreach (var eye in automatic.Eyes)
                eye.OpenCloseTarget.Target = null;
        var eyelids = spec.EyelookConfig?.Blendshape;
        if (eyelids == null || string.IsNullOrWhiteSpace(eyelids.Blink)) return;
        var mesh = Object<SkinnedMeshRenderer>(eyelids.EyelidMesh);
        if (mesh == null) return;
        await _context.WaitForAssetLoad(mesh.Mesh.Target);
        var shapes = mesh.Mesh.Asset?.Data?.BlendShapes;
        if (shapes == null) return;
        var index = shapes.ToList().FindIndex(shape => shape.Name == eyelids.Blink);
        if (index < 0 || index >= mesh.BlendShapeWeights.Count) return;

        var manager = _root.GetComponentInChildren<EyeManager>();
        if (manager == null)
        {
            var host = _root.FindChild("Head Proxy") ?? _root;
            manager = host.AttachComponent<EyeManager>();
            manager.EyeReference.Target = host;
            host.AttachComponent<AvatarUserReferenceAssigner>().References.Add(manager.SimulatingUser);
        }
        var driver = mesh.Slot.AddSlot("BEP Selected Blink").AttachComponent<ValueDriver<float>>();
        driver.ValueSource.Target = manager.CombinedEyeClose;
        driver.DriveTarget.ForceLink(mesh.BlendShapeWeights.GetElement(index));
    }

    private void SetupBepJaw(p.AvatarDescriptor spec)
    {
        var jaw = Object<Slot>(spec.JawBone);
        if (jaw == null || spec.JawClosedRotation == null || spec.JawOpenRotation == null) return;
        var host = (_root.FindChild("Head Proxy") ?? _root).AddSlot("BEP Voice Jaw");
        var volume = host.AttachComponent<VolumeMeter>();
        volume.Smoothing.Value = 0.1f;
        host.AttachComponent<AvatarVoiceSourceAssigner>().TargetReference.Target = volume.Source;
        var rotation = host.AttachComponent<ValueGradientDriver<floatQ>>();
        rotation.Interpolate.Value = true;
        rotation.AddPoint(0, spec.JawClosedRotation.Quat());
        rotation.AddPoint(1, spec.JawOpenRotation.Quat());
        rotation.Target.ForceLink(jaw.Rotation_Field);
        var gain = host.AttachComponent<LinearMapper1D>();
        gain.Source.Target = volume.Volume;
        gain.SourceMin.Value = 0;
        gain.SourceMax.Value = 0.15f;
        gain.TargetMin.Value = 0;
        gain.TargetMax.Value = 1;
        gain.Clamp.Value = true;
        gain.Target.ForceLink(rotation.Progress);
    }
}
