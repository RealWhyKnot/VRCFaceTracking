using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Logging;

namespace VRCFaceTracking.Core.Tests;

public sealed class LogLevelGateTests
{
    private static readonly bool[] RaisedThenLowered = [true, false];

    [Fact]
    public void DefaultsToWarning()
    {
        var gate = new LogLevelGate();
        Assert.False(gate.Verbose);
        Assert.False(gate.IsEnabled(LogLevel.Information));
        Assert.True(gate.IsEnabled(LogLevel.Warning));
        Assert.False(gate.IsEnabled(LogLevel.None));
    }

    [Fact]
    public void VerboseLetsDebugThroughAndRaisesOnce()
    {
        var gate = new LogLevelGate();
        var raised = new List<bool>();
        gate.VerboseChanged += (_, _) => raised.Add(gate.Verbose);
        gate.Set(verbose: true);
        gate.Set(verbose: true);
        gate.Set(verbose: false);
        Assert.Equal(RaisedThenLowered, raised);
        Assert.False(gate.IsEnabled(LogLevel.Debug));
        gate.Set(verbose: true);
        Assert.True(gate.IsEnabled(LogLevel.Debug));
        Assert.False(gate.IsEnabled(LogLevel.Trace));
    }
}
