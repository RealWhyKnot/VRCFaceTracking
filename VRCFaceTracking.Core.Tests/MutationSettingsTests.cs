using System.Globalization;
using VRCFaceTracking.Core.Params.Data.Mutation;

namespace VRCFaceTracking.Core.Tests;

public class MutationSettingsTests
{
    [Fact]
    public void DescribeSettings_ListsOptionsWithInvariantNumbers()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var mutation = new EyeAssists
            {
                IsActive = true,
                closeAssist = true,
                closeAssistStrength = 1f,
                convergenceStrength = 0.6f,
            };

            var described = mutation.DescribeSettings();

            Assert.StartsWith("active=True ", described);
            Assert.Contains("closeAssist=True", described);
            Assert.Contains("closeAssistStrength=1", described);
            Assert.Contains("convergenceStrength=0.6", described);
            Assert.DoesNotContain("NowSeconds", described);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void DescribeSettings_WritesRangesAsMinAndMax()
    {
        var mutation = new ParameterAdjustment();
        mutation.jawOpen = (0.1f, 0.9f);

        var described = mutation.DescribeSettings();

        Assert.Contains("jawOpen=0.1..0.9", described);
        Assert.Contains("eyeBrowRaiser=0..1", described);
    }
}
