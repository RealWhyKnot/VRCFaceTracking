using VRCFaceTracking.Core.Library;

namespace VRCFaceTracking.Core.Tests;

public sealed class ModuleRestartPolicyTests
{
    [Theory]
    [InlineData(ModuleProcessExitCodes.OK, false)]
    [InlineData(ModuleProcessExitCodes.InvalidArgs, false)]
    [InlineData(ModuleProcessExitCodes.ModuleLoadFailed, false)]
    [InlineData(ModuleProcessExitCodes.ParentExited, false)]
    [InlineData(ModuleProcessExitCodes.ExceptionCrash, true)]
    [InlineData(ModuleProcessExitCodes.NetworkConnectionTimedOut, true)]
    [InlineData(-1, true)]
    [InlineData(unchecked((int)0xE0434352), true)]
    [InlineData(unchecked((int)0xC0000005), true)]
    [InlineData(unchecked(0x40010004), true)]
    public void IsRestartableExitCodeClassifiesCodes(int code, bool expected) => Assert.Equal(expected, ModuleRestartPolicy.IsRestartableExitCode(code));

    [Fact]
    public void NextRestartDelayEscalatesThenExhausts()
    {
        var policy = new ModuleRestartPolicy();
        var now = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(TimeSpan.Zero, policy.NextRestartDelay("m.dll", now));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.NextRestartDelay("m.dll", now.AddSeconds(1)));
        Assert.Equal(TimeSpan.FromSeconds(15), policy.NextRestartDelay("m.dll", now.AddSeconds(10)));
        Assert.Null(policy.NextRestartDelay("m.dll", now.AddSeconds(20)));
    }

    [Fact]
    public void NextRestartDelayWindowExpiryRestoresBudget()
    {
        var policy = new ModuleRestartPolicy();
        var now = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < ModuleRestartPolicy.MaxAttempts; i++)
        {
            Assert.NotNull(policy.NextRestartDelay("m.dll", now.AddSeconds(i)));
        }
        Assert.Null(policy.NextRestartDelay("m.dll", now.AddSeconds(30)));

        Assert.Equal(TimeSpan.Zero, policy.NextRestartDelay("m.dll", now.AddSeconds(90)));
    }

    [Fact]
    public void NextRestartDelayTracksModulesIndependently()
    {
        var policy = new ModuleRestartPolicy();
        DateTime now = DateTime.UtcNow;

        Assert.Equal(TimeSpan.Zero, policy.NextRestartDelay("a.dll", now));
        Assert.Equal(TimeSpan.Zero, policy.NextRestartDelay("b.dll", now));
    }

    [Fact]
    public void ResetAllClearsHistory()
    {
        var policy = new ModuleRestartPolicy();
        DateTime now = DateTime.UtcNow;

        for (int i = 0; i < ModuleRestartPolicy.MaxAttempts; i++)
        {
            policy.NextRestartDelay("m.dll", now);
        }
        Assert.Null(policy.NextRestartDelay("m.dll", now));

        policy.ResetAll();
        Assert.Equal(TimeSpan.Zero, policy.NextRestartDelay("m.dll", now));
    }
}
