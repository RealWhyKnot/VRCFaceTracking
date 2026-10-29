using VRCFaceTracking.Core.Library;

namespace VRCFaceTracking.Core.Tests;

public sealed class ModuleProcessExitCodesTests
{
    [Theory]
    [InlineData(ModuleProcessExitCodes.OK, "clean exit")]
    [InlineData(ModuleProcessExitCodes.ExceptionCrash, "unhandled exception")]
    [InlineData(ModuleProcessExitCodes.ModuleLoadFailed, "module assembly failed to load")]
    [InlineData(ModuleProcessExitCodes.ParentExited, "host process exited")]
    [InlineData(-1, "killed (exit code -1)")]
    [InlineData(1, "killed (exit code 1)")]
    [InlineData(42, "exit code 42")]
    public void DescribeKnownCodes(int code, string expected) => Assert.Equal(expected, ModuleProcessExitCodes.Describe(code));

    [Fact]
    public void DescribeNativeCodes()
    {
        Assert.Equal("access violation (0xC0000005)", ModuleProcessExitCodes.Describe(unchecked((int)0xC0000005)));
        Assert.Equal("unhandled .NET exception (0xE0434352)", ModuleProcessExitCodes.Describe(unchecked((int)0xE0434352)));
        Assert.Equal("native exit code 0xC0000409", ModuleProcessExitCodes.Describe(unchecked((int)0xC0000409)));
    }
}
