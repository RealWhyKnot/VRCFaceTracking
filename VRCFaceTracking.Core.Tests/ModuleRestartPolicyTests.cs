using VRCFaceTracking.Core.Library;

namespace VRCFaceTracking.Core.Tests;

public class ModuleRestartPolicyTests
{
    [Theory]
    [InlineData(ModuleProcessExitCodes.OK, false)]
    [InlineData(ModuleProcessExitCodes.INVALID_ARGS, false)]
    [InlineData(ModuleProcessExitCodes.MODULE_LOAD_FAILED, false)]
    [InlineData(ModuleProcessExitCodes.PARENT_EXITED, false)]
    [InlineData(ModuleProcessExitCodes.EXCEPTION_CRASH, true)]
    [InlineData(ModuleProcessExitCodes.NETWORK_CONNECTION_TIMED_OUT, true)]
    [InlineData(-1, true)]
    [InlineData(unchecked((int)0xE0434352), true)]
    [InlineData(unchecked((int)0xC0000005), true)]
    [InlineData(unchecked((int)0x40010004), true)]
    public void IsRestartableExitCode_ClassifiesCodes(int code, bool expected)
    {
        Assert.Equal(expected, ModuleRestartPolicy.IsRestartableExitCode(code));
    }

    [Fact]
    public void NextRestartDelay_EscalatesThenExhausts()
    {
        var policy = new ModuleRestartPolicy();
        var now = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(TimeSpan.Zero, policy.NextRestartDelay("m.dll", now));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.NextRestartDelay("m.dll", now.AddSeconds(1)));
        Assert.Equal(TimeSpan.FromSeconds(15), policy.NextRestartDelay("m.dll", now.AddSeconds(10)));
        Assert.Null(policy.NextRestartDelay("m.dll", now.AddSeconds(20)));
    }

    [Fact]
    public void NextRestartDelay_WindowExpiryRestoresBudget()
    {
        var policy = new ModuleRestartPolicy();
        var now = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < ModuleRestartPolicy.MaxAttempts; i++)
        {
            Assert.NotNull(policy.NextRestartDelay("m.dll", now.AddSeconds(i)));
        }
        Assert.Null(policy.NextRestartDelay("m.dll", now.AddSeconds(30)));

        Assert.Equal(TimeSpan.Zero, policy.NextRestartDelay("m.dll", now.AddSeconds(90)));
    }

    [Fact]
    public void NextRestartDelay_TracksModulesIndependently()
    {
        var policy = new ModuleRestartPolicy();
        var now = DateTime.UtcNow;

        Assert.Equal(TimeSpan.Zero, policy.NextRestartDelay("a.dll", now));
        Assert.Equal(TimeSpan.Zero, policy.NextRestartDelay("b.dll", now));
    }

    [Fact]
    public void ResetAll_ClearsHistory()
    {
        var policy = new ModuleRestartPolicy();
        var now = DateTime.UtcNow;

        for (var i = 0; i < ModuleRestartPolicy.MaxAttempts; i++)
        {
            policy.NextRestartDelay("m.dll", now);
        }
        Assert.Null(policy.NextRestartDelay("m.dll", now));

        policy.ResetAll();
        Assert.Equal(TimeSpan.Zero, policy.NextRestartDelay("m.dll", now));
    }
}
