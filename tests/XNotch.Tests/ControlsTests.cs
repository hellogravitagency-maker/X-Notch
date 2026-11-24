using System.Linq;
using NAudio.CoreAudioApi;
using XNotch.Services;
using Xunit;

namespace XNotch.Tests;

public class ControlsTests
{
    [Fact]
    public async System.Threading.Tasks.Task Controls_ReadState_WithoutChangingAnything()
    {
        using var c = new SystemControlsService();
        await c.InitializeAsync(); // radios / focus are optional hardware: must not throw

        _ = c.MicMuted;
        foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
        {
            var devices = c.Devices(flow);
            Assert.True(devices.Count(d => d.IsDefault) <= 1);

            // Re-selecting the current default is a no-op that proves the IPolicyConfig vtable is right.
            if (devices.FirstOrDefault(d => d.IsDefault) is { } def)
            {
                SystemControlsService.SetDefaultDevice(def.Id);
                Assert.Equal(def.Id, c.Devices(flow).First(d => d.IsDefault).Id);
            }
        }
    }
}
