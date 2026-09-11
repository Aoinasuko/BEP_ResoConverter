using System.Reflection;
using FrooxEngine;
using Renderite.Shared;

namespace nadena.dev.resonity.remote.puppeteer;

// Only used by the isolated headless verifier. Never serialized in an avatar.
internal sealed class ExpressionInputVerificationScope : IDisposable
{
    private readonly World world;
    private readonly List<Action> restore = new();
    internal ExpressionInputVerificationScope(World world)
    {
        this.world = world;
        var input = world.InputInterface;
        foreach (var name in new[] { "_vrActive", "_nextVRactive", "_supressHotswitching" })
        {
            var field = typeof(InputInterface).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
            var value = field.GetValue(input); restore.Add(() => field.SetValue(input, value));
        }
        var device = typeof(InputInterface).GetProperty("HeadOutputDevice")!;
        var originalDevice = device.GetValue(input); restore.Add(() => device.SetValue(input, originalDevice));
        device.SetValue(input, HeadOutputDevice.SteamVR);
        var userDevice = world.LocalUser.HeadDevice; restore.Add(() => SetHeadDevice(userDevice));
        var autoRespawn = world.BlockAutoRespawn; restore.Add(() => world.BlockAutoRespawn = autoRespawn);
        world.BlockAutoRespawn = true;
        var focus = world.Focus; restore.Add(() => world.Focus = focus);
        world.Focus = World.WorldFocus.Focused;
        SetMode(true);
    }

    internal void SetMode(bool vr, HeadOutputDevice device = HeadOutputDevice.SteamVR)
    {
        SetHeadDevice(device);
        foreach (var name in new[] { "_vrActive", "_nextVRactive" })
            typeof(InputInterface).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(world.InputInterface, vr);
        typeof(InputInterface).GetField("_supressHotswitching", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(world.InputInterface, true);
        world.LocalUser.VR_Active = vr;
    }

    private void SetHeadDevice(HeadOutputDevice device)
    {
        // User's initialization filter rejects normal HeadDevice writes after
        // joining. Bypass it only in this disposable isolated test scope.
        var field = (Sync<HeadOutputDevice>)typeof(User).GetField("headDevice", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world.LocalUser)!;
        var filter = field.LocalFilter;
        try { field.LocalFilter = null; field.Value = device; }
        finally { field.LocalFilter = filter; }
    }

    public void Dispose()
    {
        foreach (var action in restore.AsEnumerable().Reverse()) action();
    }
}
