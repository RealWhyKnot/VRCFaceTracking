using System.Globalization;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Params.Data.Mutation;

namespace VRCFaceTracking.Core.Tests;

public sealed class MutationSettingsTests
{
    private sealed class RecordingSettings : ILocalSettingsService
    {
        public List<string> Saved { get; } = [];

        public Task<T?> ReadSettingAsync<T>(string key, T? defaultValue, bool forceLocal, CancellationToken cancellationToken) => Task.FromResult(defaultValue);

        public Task SaveSettingAsync<T>(string key, T value, bool forceLocal, CancellationToken cancellationToken)
        {
            Saved.Add(key);
            return Task.CompletedTask;
        }

        public Task SaveAsync(object target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadAsync(object target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public void ActiveSwitchFlippedSavesTheMutation()
    {
        var settings = new RecordingSettings();
        var mutation = new EyeAssists
        {
            LocalSettingsService = settings,
            ActiveSwitch = true,
        };

        Assert.True(mutation.IsActive);
        Assert.Equal(["Eye Assists"], settings.Saved, StringComparer.Ordinal);
    }

    [Fact]
    public void ActiveSwitchSameValueDoesNotSave()
    {
        var settings = new RecordingSettings();
        _ = new Correctors
        {
            LocalSettingsService = settings,
            ActiveSwitch = true,
        };

        Assert.Empty(settings.Saved);
    }

    [Fact]
    public void ParameterAdjustmentRangeChangedSavesTheMutation()
    {
        var settings = new RecordingSettings();
        var mutation = new ParameterAdjustment { LocalSettingsService = settings };
        mutation.CreateProperties();
        MutationRangeProperty jaw = mutation.Components.OfType<MutationRangeProperty>().Single(c => string.Equals(c.Name, "Jaw", StringComparison.Ordinal));

        jaw.Item2 = 0.8f;

        Assert.Equal((0f, 0.8f), mutation.JawOpen);
        Assert.Equal(["Parameter Adjustment"], settings.Saved, StringComparer.Ordinal);
    }

    [Fact]
    public void DescribeSettingsListsOptionsWithInvariantNumbers()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var mutation = new EyeAssists
            {
                IsActive = true,
                CloseAssist = true,
                CloseAssistStrength = 1f,
                ConvergenceStrength = 0.6f,
            };

            string described = mutation.DescribeSettings();

            Assert.StartsWith("active=True ", described, StringComparison.Ordinal);
            Assert.Contains("CloseAssist=True", described, StringComparison.Ordinal);
            Assert.Contains("CloseAssistStrength=1", described, StringComparison.Ordinal);
            Assert.Contains("ConvergenceStrength=0.6", described, StringComparison.Ordinal);
            Assert.DoesNotContain("NowSeconds", described, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void DescribeSettingsWritesRangesAsMinAndMax()
    {
        var mutation = new ParameterAdjustment
        {
            JawOpen = (0.1f, 0.9f),
        };

        string described = mutation.DescribeSettings();

        Assert.Contains("JawOpen=0.1..0.9", described, StringComparison.Ordinal);
        Assert.Contains("EyeBrowRaiser=0..1", described, StringComparison.Ordinal);
    }
}
