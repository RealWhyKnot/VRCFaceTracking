using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Helpers;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Tests;

public class EyelidBlendTests
{
    private static UnifiedTrackingData Blend(float blend, float left, float right)
    {
        var data = new UnifiedTrackingData();
        data.Eye.Left.Openness = left;
        data.Eye.Right.Openness = right;
        data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight = left;
        data.Shapes[(int)UnifiedExpressions.EyeWideRight].Weight = right;
        new EyeAssists { eyeLidBlend = blend }.MutateData(ref data);
        return data;
    }

    [Fact]
    public void FullBlend_GivesBothEyesTheAverage()
    {
        var data = Blend(1f, 0.2f, 0.8f);

        Assert.Equal(0.5f, data.Eye.Left.Openness, 5);
        Assert.Equal(0.5f, data.Eye.Right.Openness, 5);
        Assert.Equal(0.5f, data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight, 5);
        Assert.Equal(0.5f, data.Shapes[(int)UnifiedExpressions.EyeWideRight].Weight, 5);
    }

    [Fact]
    public void HalfBlend_MixesEachEyeWithTheOthersOriginalValue()
    {
        var data = Blend(0.5f, 0.2f, 0.8f);

        Assert.Equal(0.35f, data.Eye.Left.Openness, 5);
        Assert.Equal(0.65f, data.Eye.Right.Openness, 5);
    }

    [Fact]
    public void NoBlend_LeavesEyesAlone()
    {
        var data = Blend(0f, 0.2f, 0.8f);

        Assert.Equal(0.2f, data.Eye.Left.Openness);
        Assert.Equal(0.8f, data.Eye.Right.Openness);
    }

    [Fact]
    public void FullBlend_AveragesPupilsInMillimetres()
    {
        var data = new UnifiedTrackingData();
        data.Eye.Left.PupilDiameter_MM = 3f;
        data.Eye.Right.PupilDiameter_MM = 5f;

        new EyeAssists { eyeLidBlend = 1f }.MutateData(ref data);

        Assert.Equal(4f, data.Eye.Left.PupilDiameter_MM, 5);
        Assert.Equal(4f, data.Eye.Right.PupilDiameter_MM, 5);
    }

    [Fact]
    public void Correctors_NoLongerTouchEyelids()
    {
        var data = new UnifiedTrackingData();
        data.Eye.Left.Openness = 0f;
        data.Eye.Right.Openness = 1f;

        new Correctors().MutateData(ref data);

        Assert.Equal(0f, data.Eye.Left.Openness);
        Assert.Equal(1f, data.Eye.Right.Openness);
    }

    [Fact]
    public async Task OldCorrectorsSave_WithEyelidBlend_StillLoads()
    {
        const string saved = "{\"mouthClosedFix\":false,\"lipSuckFix\":true,\"eyeLidBlend\":1.0,\"eyeLookSymmetrize\":false,\"Name\":\"Unified Correctors\",\"Step\":2,\"IsActive\":true,\"IsSaved\":false}";

        var loaded = await Json.ToObjectAsync<Correctors>(saved);

        Assert.False(loaded.mouthClosedFix);
        Assert.True(loaded.IsActive);
    }

    private sealed class SavedSettings : ILocalSettingsService
    {
        private readonly Dictionary<string, string> _saved;

        public SavedSettings(Dictionary<string, string> saved)
        {
            _saved = saved;
        }

        public async Task<T> ReadSettingAsync<T>(string key, T? defaultValue = default, bool forceLocal = false) =>
            _saved.TryGetValue(key, out var json) ? await Json.ToObjectAsync<T>(json) : defaultValue!;
        public Task SaveSettingAsync<T>(string key, T value, bool forceLocal = false) => Task.CompletedTask;
        public Task Save(object target) => Task.CompletedTask;
        public Task Load(object target) => Task.CompletedTask;
        public Task FlushAsync() => Task.CompletedTask;
    }

    private const string OldCorrectors = "{\"mouthClosedFix\":true,\"lipSuckFix\":true,\"eyeLidBlend\":1.0,\"eyeLookSymmetrize\":true,\"Name\":\"Unified Correctors\",\"Step\":2,\"IsActive\":true,\"IsSaved\":false}";
    private const string EyeAssistsWithoutBlend = "{\"convergenceFix\":false,\"convergenceStrength\":0.0,\"closeAssist\":true,\"closeAssistStrength\":1.0,\"eyelidSync\":true,\"syncStrength\":1.0,\"syncToMostOpen\":false,\"preserveWinks\":false,\"forceEyesClosed\":false,\"Name\":\"Eye Assists\",\"Step\":2,\"IsActive\":true,\"IsSaved\":false}";

    [Fact]
    public async Task OldCorrectorsSave_MovesEyelidBlendIntoEyeAssists()
    {
        var settings = new SavedSettings(new Dictionary<string, string>
        {
            [EyeAssists.CorrectorsSettingName] = OldCorrectors,
            ["Eye Assists"] = EyeAssistsWithoutBlend,
        });
        var eyeAssists = await settings.ReadSettingAsync("Eye Assists", new EyeAssists(), true);

        await eyeAssists.MoveEyelidBlendFromCorrectorsAsync(settings);

        Assert.Equal(1f, eyeAssists.eyeLidBlend);
        Assert.True(eyeAssists.eyelidSync);
    }

    [Fact]
    public async Task OldCorrectorsSave_MovesEyelidBlendWhenEyeAssistsWasNeverSaved()
    {
        var settings = new SavedSettings(new Dictionary<string, string>
        {
            [EyeAssists.CorrectorsSettingName] = OldCorrectors,
        });
        var eyeAssists = new EyeAssists();

        await eyeAssists.MoveEyelidBlendFromCorrectorsAsync(settings);

        Assert.Equal(1f, eyeAssists.eyeLidBlend);
    }

    [Fact]
    public async Task EyeAssistsSave_KeepsItsOwnEyelidBlend()
    {
        var settings = new SavedSettings(new Dictionary<string, string>
        {
            [EyeAssists.CorrectorsSettingName] = OldCorrectors,
            ["Eye Assists"] = EyeAssistsWithoutBlend.Replace("\"forceEyesClosed\":false", "\"forceEyesClosed\":false,\"eyeLidBlend\":0.0"),
        });
        var eyeAssists = await settings.ReadSettingAsync("Eye Assists", new EyeAssists(), true);

        await eyeAssists.MoveEyelidBlendFromCorrectorsAsync(settings);

        Assert.Equal(0f, eyeAssists.eyeLidBlend);
    }

    [Fact]
    public async Task NoSavedEyelidBlend_LeavesTheDefault()
    {
        var settings = new SavedSettings(new Dictionary<string, string>
        {
            [EyeAssists.CorrectorsSettingName] = "{\"mouthClosedFix\":true,\"Name\":\"Unified Correctors\"}",
        });
        var eyeAssists = new EyeAssists();

        await eyeAssists.MoveEyelidBlendFromCorrectorsAsync(settings);

        Assert.Equal(0f, eyeAssists.eyeLidBlend);
    }
}
