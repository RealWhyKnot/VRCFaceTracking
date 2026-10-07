using System.Globalization;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Params.Data.Mutation;

namespace VRCFaceTracking.Core.Tests;

public class MutationSettingsTests
{
    private sealed class RecordingSettings : ILocalSettingsService
    {
        public List<string> Saved { get; } = new();

        public Task<T> ReadSettingAsync<T>(string key, T? defaultValue = default, bool forceLocal = false) => Task.FromResult(defaultValue!);

        public Task SaveSettingAsync<T>(string key, T value, bool forceLocal = false)
        {
            Saved.Add(key);
            return Task.CompletedTask;
        }

        public Task Save(object target) => Task.CompletedTask;
        public Task Load(object target) => Task.CompletedTask;
        public Task FlushAsync() => Task.CompletedTask;
    }

    [Fact]
    public void ActiveSwitch_Flipped_SavesTheMutation()
    {
        var settings = new RecordingSettings();
        var mutation = new EyeAssists { LocalSettingsService = settings };

        mutation.ActiveSwitch = true;

        Assert.True(mutation.IsActive);
        Assert.Equal(new[] { "Eye Assists" }, settings.Saved);
    }

    [Fact]
    public void ActiveSwitch_SameValue_DoesNotSave()
    {
        var settings = new RecordingSettings();
        var mutation = new Correctors { LocalSettingsService = settings };

        mutation.ActiveSwitch = true;

        Assert.Empty(settings.Saved);
    }

    [Fact]
    public void ParameterAdjustmentRange_Changed_SavesTheMutation()
    {
        var settings = new RecordingSettings();
        var mutation = new ParameterAdjustment { LocalSettingsService = settings };
        mutation.CreateProperties();
        var jaw = mutation.Components.OfType<MutationRangeProperty>().Single(c => c.Name == "Jaw");

        jaw.Item2 = 0.8f;

        Assert.Equal((0f, 0.8f), mutation.jawOpen);
        Assert.Equal(new[] { "Parameter Adjustment" }, settings.Saved);
    }

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
