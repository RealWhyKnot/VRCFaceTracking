using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Helpers;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Tests;

public sealed class EyelidBlendTests
{
    private static UnifiedTrackingData Blend(float blend, float left, float right)
    {
        var data = new UnifiedTrackingData();
        data.Eye.Left.Openness = left;
        data.Eye.Right.Openness = right;
        data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight = left;
        data.Shapes[(int)UnifiedExpressions.EyeWideRight].Weight = right;
        new EyeAssists { EyeLidBlend = blend }.MutateData(ref data);
        return data;
    }

    [Fact]
    public void FullBlendGivesBothEyesTheAverage()
    {
        UnifiedTrackingData data = Blend(1f, 0.2f, 0.8f);

        Assert.Equal(0.5f, data.Eye.Left.Openness, 5);
        Assert.Equal(0.5f, data.Eye.Right.Openness, 5);
        Assert.Equal(0.5f, data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight, 5);
        Assert.Equal(0.5f, data.Shapes[(int)UnifiedExpressions.EyeWideRight].Weight, 5);
    }

    [Fact]
    public void HalfBlendMixesEachEyeWithTheOthersOriginalValue()
    {
        UnifiedTrackingData data = Blend(0.5f, 0.2f, 0.8f);

        Assert.Equal(0.35f, data.Eye.Left.Openness, 5);
        Assert.Equal(0.65f, data.Eye.Right.Openness, 5);
    }

    [Fact]
    public void NoBlendLeavesEyesAlone()
    {
        UnifiedTrackingData data = Blend(0f, 0.2f, 0.8f);

        Assert.Equal(0.2f, data.Eye.Left.Openness);
        Assert.Equal(0.8f, data.Eye.Right.Openness);
    }

    [Fact]
    public void FullBlendAveragesPupilsInMillimetres()
    {
        var data = new UnifiedTrackingData();
        data.Eye.Left.PupilDiameter_MM = 3f;
        data.Eye.Right.PupilDiameter_MM = 5f;

        new EyeAssists { EyeLidBlend = 1f }.MutateData(ref data);

        Assert.Equal(4f, data.Eye.Left.PupilDiameter_MM, 5);
        Assert.Equal(4f, data.Eye.Right.PupilDiameter_MM, 5);
    }

    [Fact]
    public void CorrectorsNoLongerTouchEyelids()
    {
        var data = new UnifiedTrackingData();
        data.Eye.Left.Openness = 0f;
        data.Eye.Right.Openness = 1f;

        new Correctors().MutateData(ref data);

        Assert.Equal(0f, data.Eye.Left.Openness);
        Assert.Equal(1f, data.Eye.Right.Openness);
    }

    [Fact]
    public async Task OldCorrectorsSaveWithEyelidBlendStillLoads()
    {
        const string Saved = "{\"mouthClosedFix\":false,\"lipSuckFix\":true,\"eyeLidBlend\":1.0,\"eyeLookSymmetrize\":false,\"Name\":\"Unified Correctors\",\"Step\":2,\"IsActive\":true,\"IsSaved\":false}";

        Correctors loaded = (await JsonText.ToObjectAsync<Correctors>(Saved, CancellationToken.None))!;

        Assert.False(loaded.MouthClosedFix);
        Assert.True(loaded.IsActive);
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
        public Task SaveSettingAsync<T>(string key, T value, bool forceLocal, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(object target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadAsync(object target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private const string OldCorrectors = "{\"mouthClosedFix\":true,\"lipSuckFix\":true,\"eyeLidBlend\":1.0,\"eyeLookSymmetrize\":true,\"Name\":\"Unified Correctors\",\"Step\":2,\"IsActive\":true,\"IsSaved\":false}";
    private const string EyeAssistsWithoutBlend = "{\"convergenceFix\":false,\"convergenceStrength\":0.0,\"closeAssist\":true,\"closeAssistStrength\":1.0,\"eyelidSync\":true,\"syncStrength\":1.0,\"syncToMostOpen\":false,\"preserveWinks\":false,\"forceEyesClosed\":false,\"Name\":\"Eye Assists\",\"Step\":2,\"IsActive\":true,\"IsSaved\":false}";

    [Fact]
    public async Task OldCorrectorsSaveMovesEyelidBlendIntoEyeAssists()
    {
        var settings = new SavedSettings(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EyeAssists.CorrectorsSettingName] = OldCorrectors,
            ["Eye Assists"] = EyeAssistsWithoutBlend,
        });
        EyeAssists eyeAssists = (await settings.ReadSettingAsync("Eye Assists", new EyeAssists(), forceLocal: true, CancellationToken.None))!;

        await eyeAssists.MoveEyelidBlendFromCorrectorsAsync(settings, CancellationToken.None);

        Assert.Equal(1f, eyeAssists.EyeLidBlend);
        Assert.True(eyeAssists.EyelidSync);
    }

    [Fact]
    public async Task OldCorrectorsSaveMovesEyelidBlendWhenEyeAssistsWasNeverSaved()
    {
        var settings = new SavedSettings(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EyeAssists.CorrectorsSettingName] = OldCorrectors,
        });
        var eyeAssists = new EyeAssists();

        await eyeAssists.MoveEyelidBlendFromCorrectorsAsync(settings, CancellationToken.None);

        Assert.Equal(1f, eyeAssists.EyeLidBlend);
    }

    [Fact]
    public async Task EyeAssistsSaveKeepsItsOwnEyelidBlend()
    {
        var settings = new SavedSettings(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EyeAssists.CorrectorsSettingName] = OldCorrectors,
            ["Eye Assists"] = EyeAssistsWithoutBlend.Replace("\"forceEyesClosed\":false", "\"forceEyesClosed\":false,\"eyeLidBlend\":0.0", StringComparison.Ordinal),
        });
        EyeAssists eyeAssists = (await settings.ReadSettingAsync("Eye Assists", new EyeAssists(), forceLocal: true, CancellationToken.None))!;

        await eyeAssists.MoveEyelidBlendFromCorrectorsAsync(settings, CancellationToken.None);

        Assert.Equal(0f, eyeAssists.EyeLidBlend);
    }

    [Fact]
    public async Task NoSavedEyelidBlendLeavesTheDefault()
    {
        var settings = new SavedSettings(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EyeAssists.CorrectorsSettingName] = "{\"mouthClosedFix\":true,\"Name\":\"Unified Correctors\"}",
        });
        var eyeAssists = new EyeAssists();

        await eyeAssists.MoveEyelidBlendFromCorrectorsAsync(settings, CancellationToken.None);

        Assert.Equal(0f, eyeAssists.EyeLidBlend);
    }
}
