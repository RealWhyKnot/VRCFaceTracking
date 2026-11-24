using System.ComponentModel.DataAnnotations;
using System.Reflection;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Helpers;
using VRCFaceTracking.Core.Models;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Tests;

public sealed partial class MutationSettingsFixesTests
{
    private static float Shape(UnifiedTrackingData data, UnifiedExpressions shape) => data.Shapes[(int)shape].Weight;

    private static void SetShape(UnifiedTrackingData data, UnifiedExpressions shape, float weight) => data.Shapes[(int)shape].Weight = weight;

    [Theory]
    [InlineData(nameof(ParameterAdjustment.HeadRotationYaw))]
    [InlineData(nameof(ParameterAdjustment.HeadRotationPitch))]
    [InlineData(nameof(ParameterAdjustment.HeadRotationRoll))]
    [InlineData(nameof(ParameterAdjustment.HeadPositionX))]
    [InlineData(nameof(ParameterAdjustment.HeadPositionY))]
    [InlineData(nameof(ParameterAdjustment.HeadPositionZ))]
    public void HeadRangesAllowMinusOneToOne(string property)
    {
        PropertyInfo info = typeof(ParameterAdjustment).GetProperty(property)!;
        MutationPropertyAttribute attribute = info.GetCustomAttribute<MutationPropertyAttribute>()!;

        Assert.Equal(-1f, attribute.Min);
        Assert.Equal(1f, attribute.Max);
        Assert.Equal((-1f, 1f), ((float, float))info.GetValue(new ParameterAdjustment())!);
    }

    [Fact]
    public void HeadRangeComponentsExposeMinusOneToOneBounds()
    {
        var mutation = new ParameterAdjustment();
        mutation.CreateProperties();

        MutationRangeProperty yaw = Assert.IsType<MutationRangeProperty>(mutation.Components.Single(c => string.Equals(c.Name, "Head Rotation (Side-to-Side)", StringComparison.Ordinal)));

        Assert.Equal(-1f, yaw.Min);
        Assert.Equal(1f, yaw.Max);
        Assert.Equal(-1f, yaw.Item1);
    }

    [Theory]
    [InlineData(0.5f, 0.5f, 0.5f)]
    [InlineData(0.5f, 0.7f, 0.2f)]
    [InlineData(0.5f, 0.2f, 0.9f)]
    [InlineData(1f, 0f, 0f)]
    [InlineData(float.NaN, 0f, 1f)]
    [InlineData(0.5f, float.NaN, float.NaN)]
    [InlineData(0.5f, float.PositiveInfinity, float.NegativeInfinity)]
    public void RescaleNeverEmitsNaNOrInfinity(float value, float floor, float ceiling)
    {
        float result = ParameterAdjustment.Rescale(value, floor, ceiling);

        Assert.True(float.IsFinite(result));
        Assert.InRange(result, 0f, 1f);
    }

    [Fact]
    public void RescaleKeepsAMinimumGapWhenCeilingTouchesFloor()
    {
        Assert.Equal(1f, ParameterAdjustment.Rescale(0.6f, 0.5f, 0.5f));
        Assert.Equal(0f, ParameterAdjustment.Rescale(0.4f, 0.5f, 0.5f));
    }

    [Fact]
    public void RescaleClampsToTheOutputRange()
    {
        Assert.Equal(1f, ParameterAdjustment.Rescale(0.9f, 0f, 0.5f));
        Assert.Equal(-1f, ParameterAdjustment.Rescale(-0.9f, 0f, 0.5f, -1f, 1f));
        Assert.Equal(0.5f, ParameterAdjustment.Rescale(0.25f, 0f, 0.5f));
    }

    [Fact]
    public void CrossedRangesProduceFiniteOutput()
    {
        var adjustment = new ParameterAdjustment { JawOpen = (0.8f, 0.2f), HeadRotationYaw = (0.5f, 0.5f) };
        var data = new UnifiedTrackingData();
        SetShape(data, UnifiedExpressions.JawOpen, 0.5f);
        data.Head.HeadYaw = 0.5f;

        adjustment.MutateData(ref data);

        Assert.True(float.IsFinite(Shape(data, UnifiedExpressions.JawOpen)));
        Assert.True(float.IsFinite(data.Head.HeadYaw));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(65535, true)]
    [InlineData(0, false)]
    [InlineData(65536, false)]
    public void OscPortsAcceptTheFullPortRange(int port, bool valid)
    {
        PropertyInfo[] ports = [.. typeof(OscTarget).GetProperties().Where(p => p.GetCustomAttribute<RangeAttribute>() is not null)];

        Assert.NotEmpty(ports);
        foreach (PropertyInfo property in ports)
        {
            RangeAttribute range = property.GetCustomAttribute<RangeAttribute>()!;
            Assert.Equal(1, range.Minimum);
            Assert.Equal(65535, range.Maximum);
            Assert.Equal(valid, range.IsValid(port));
        }
    }

    [Fact]
    public async Task FilterSettingsRoundTrip()
    {
        var filter = new Filter { Smoothing = 0.8f, MinCutoff = 0.7f, Beta = 0.2f, DerivativeCutoff = 0.3f };

        Filter loaded = (await JsonText.ToObjectAsync<Filter>(await JsonText.StringifyAsync(filter, CancellationToken.None), CancellationToken.None))!;

        Assert.Equal(0.8f, loaded.Smoothing);
        Assert.Equal(0.7f, loaded.MinCutoff);
        Assert.Equal(0.2f, loaded.Beta);
        Assert.Equal(0.3f, loaded.DerivativeCutoff);
    }

    [Fact]
    public async Task OldFilterSaveLoadsWithCurrentBehavior()
    {
        const string Saved = "{\"Name\":\"Data Filter\",\"Step\":2,\"IsActive\":true,\"IsSaved\":true}";

        Filter loaded = (await JsonText.ToObjectAsync<Filter>(Saved, CancellationToken.None))!;

        Assert.True(loaded.IsActive);
        Assert.Equal(1f, loaded.MinCutoff);
        Assert.Equal(0.5f, loaded.Beta);
        Assert.Equal(0.1f, loaded.DerivativeCutoff);
        Assert.Equal(0.5f, loaded.Smoothing);
    }

    [Fact]
    public void DefaultSmoothingMatchesTheOldFixedFilter()
    {
        var filter = new Filter();

        Assert.Equal(0.5f, filter.Smoothing);
        Assert.Equal(1f, filter.MinCutoff, 4);
        Assert.Equal(0.5f, filter.Beta, 4);
        Assert.Equal(0.1f, filter.DerivativeCutoff, 4);
    }

    [Fact]
    public void SmoothingMapsMonotonicallyToResponsiveness()
    {
        var filter = new Filter();
        float previousCutoff = float.MaxValue;
        float previousBeta = float.MaxValue;
        for (float smoothing = 0f; smoothing <= 1f; smoothing += 0.1f)
        {
            filter.Smoothing = smoothing;
            Assert.True(filter.MinCutoff < previousCutoff);
            Assert.True(filter.Beta < previousBeta);
            Assert.InRange(filter.MinCutoff, 0.5f, 2f);
            Assert.InRange(filter.Beta, 0.25f, 1f);
            previousCutoff = filter.MinCutoff;
            previousBeta = filter.Beta;
        }
    }

    [Theory]
    [InlineData(-3f, 0f)]
    [InlineData(7f, 1f)]
    [InlineData(float.NaN, 0.5f)]
    public void SmoothingIsClamped(float input, float expected)
    {
        var filter = new Filter { Smoothing = input };

        Assert.Equal(expected, filter.Smoothing);
    }

    [Fact]
    public void HeavierSmoothingFollowsAStepSlower()
    {
        static float Settle(float smoothing)
        {
            var filter = new Filter { Smoothing = smoothing };
            var data = new UnifiedTrackingData();
            for (int i = 0; i < 3; i++)
            {
                SetShape(data, UnifiedExpressions.JawOpen, 1f);
                filter.MutateData(ref data);
            }

            return Shape(data, UnifiedExpressions.JawOpen);
        }

        Assert.True(Settle(0f) > Settle(1f));
    }

    [Fact]
    public void FiltersKeepIndependentSettings()
    {
        var smooth = new Filter { Smoothing = 1f };
        var snappy = new Filter { Smoothing = 0f };

        Assert.NotEqual(smooth.MinCutoff, snappy.MinCutoff);
    }

    [Fact]
    public async Task CalibrationSettingsRoundTrip()
    {
        var calibration = new Calibration { WindowSize = 128, StepDelta = 0.3f };

        Calibration loaded = (await JsonText.ToObjectAsync<Calibration>(await JsonText.StringifyAsync(calibration, CancellationToken.None), CancellationToken.None))!;

        Assert.Equal(128, loaded.WindowSize);
        Assert.Equal(0.3f, loaded.StepDelta);
    }

    [Fact]
    public async Task OldCalibrationSaveKeepsDefaultSettings()
    {
        const string Saved = "{\"calibrationBlend\":0.5,\"continuousCalibration\":true,\"calData\":{\"Shapes\":[{\"name\":\"EyeSquintRight\",\"progress\":0.25,\"max\":0.8}]},\"Name\":\"Calibration\",\"Step\":0,\"IsActive\":true,\"IsSaved\":true}";

        Calibration loaded = (await JsonText.ToObjectAsync<Calibration>(Saved, CancellationToken.None))!;

        Assert.Equal(64, loaded.WindowSize);
        Assert.Equal(0.15f, loaded.StepDelta);
        Assert.Equal(0.25f, loaded.CalData.Shapes[0].Progress);
    }

    [Fact]
    public void CalibrationWindowSizeResizesRuntimeBuffers()
    {
        var calibration = new Calibration { IsActive = true };
        var data = new UnifiedTrackingData();
        for (int i = 0; i < 20; i++)
        {
            SetShape(data, UnifiedExpressions.JawOpen, 0.1f + (i * 0.04f));
            calibration.MutateData(ref data);
        }

        CalibrationParameter jaw = calibration.CalData.Shapes[(int)UnifiedExpressions.JawOpen];
        float before = jaw.Progress;
        Assert.Equal(20 / 64f, before, 4);

        float last = 0.1f + (19 * 0.04f);
        calibration.WindowSize = 32;
        SetShape(data, UnifiedExpressions.JawOpen, last);
        calibration.MutateData(ref data);

        Assert.Equal(20 / 32f, jaw.Progress, 4);

        calibration.WindowSize = 8;
        SetShape(data, UnifiedExpressions.JawOpen, last);
        calibration.MutateData(ref data);

        Assert.Equal(1f, jaw.Progress, 4);
    }

    [Fact]
    public void CalibrationWindowSizeIsClamped()
    {
        var calibration = new Calibration { WindowSize = 1 };
        Assert.Equal(Calibration.MinWindowSize, calibration.WindowSize);
        calibration.WindowSize = 1_000_000;
        Assert.Equal(Calibration.MaxWindowSize, calibration.WindowSize);
    }

    [Fact]
    public void SmallerWindowFinishesLearningSooner()
    {
        static float Fill(int window)
        {
            var calibration = new Calibration { IsActive = true, WindowSize = window };
            var data = new UnifiedTrackingData();
            for (int i = 0; i < 16; i++)
            {
                SetShape(data, UnifiedExpressions.JawOpen, 0.1f + (i * 0.05f));
                calibration.MutateData(ref data);
            }

            return calibration.CalData.Shapes[(int)UnifiedExpressions.JawOpen].Progress;
        }

        Assert.True(Fill(16) > Fill(128));
    }

    [Fact]
    public void LoadedProgressSurvivesTheNextSample()
    {
        var calibration = new Calibration { IsActive = true };
        calibration.CalData.Shapes[(int)UnifiedExpressions.JawOpen].Progress = 0.5f;
        var data = new UnifiedTrackingData();
        SetShape(data, UnifiedExpressions.JawOpen, 0.9f);

        calibration.MutateData(ref data);

        Assert.True(calibration.CalData.Shapes[(int)UnifiedExpressions.JawOpen].Progress >= 0.5f);
    }

    private sealed class SavedSettings : ILocalSettingsService
    {
        private readonly Dictionary<string, string> _saved;

        public SavedSettings(Dictionary<string, string> saved)
        {
            _saved = saved;
        }

        public async Task<T?> ReadSettingAsync<T>(string key, T? defaultValue, bool forceLocal, CancellationToken cancellationToken) =>
            _saved.TryGetValue(key, out string? json) ? await JsonText.ToObjectAsync<T>(json, cancellationToken) : defaultValue;

        public void Remove(string key) => _saved.Remove(key);

        public async Task SaveSettingAsync<T>(string key, T value, bool forceLocal, CancellationToken cancellationToken) =>
            _saved[key] = await JsonText.StringifyAsync(value, cancellationToken);

        public Task SaveAsync(object target, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task LoadAsync(object target, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
