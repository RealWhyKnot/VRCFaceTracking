using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Osc;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.DataTypes;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Services;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Tests;

[Collection("UnifiedTrackingState")]
public sealed class NativeFaceTrackingTests
{
    private static readonly string[] VrchatKeyOrder =
    [
        "EyeLookOutRight", "EyeLookInRight", "EyeLookUpRight", "EyeLookDownRight",
        "EyeLookOutLeft", "EyeLookInLeft", "EyeLookUpLeft", "EyeLookDownLeft",
        "EyeClosedRight", "EyeClosedLeft", "EyeDilationRight", "EyeDilationLeft", "EyeConstrictRight", "EyeConstrictLeft",
        "EyeSquintRight", "EyeSquintLeft", "EyeWideRight", "EyeWideLeft",
        "BrowPinchRight", "BrowPinchLeft", "BrowLowererRight", "BrowLowererLeft",
        "BrowInnerUpRight", "BrowInnerUpLeft", "BrowOuterUpRight", "BrowOuterUpLeft",
        "NasalDilationRight", "NasalDilationLeft", "NasalConstrictRight", "NasalConstrictLeft",
        "CheekSquintRight", "CheekSquintLeft", "CheekPuffRight", "CheekPuffLeft", "CheekSuckRight", "CheekSuckLeft",
        "JawOpen", "JawRight", "JawLeft", "JawForward", "JawBackward", "JawClench", "JawMandibleRaise",
        "MouthClosed",
        "LipSuckUpperRight", "LipSuckUpperLeft", "LipSuckLowerRight", "LipSuckLowerLeft", "LipSuckCornerRight", "LipSuckCornerLeft",
        "LipFunnelUpperRight", "LipFunnelUpperLeft", "LipFunnelLowerRight", "LipFunnelLowerLeft",
        "LipPuckerUpperRight", "LipPuckerUpperLeft", "LipPuckerLowerRight", "LipPuckerLowerLeft",
        "MouthUpperUpRight", "MouthUpperUpLeft", "MouthUpperDeepenRight", "MouthUpperDeepenLeft",
        "NoseSneerRight", "NoseSneerLeft", "MouthLowerDownRight", "MouthLowerDownLeft",
        "MouthUpperRight", "MouthUpperLeft", "MouthLowerRight", "MouthLowerLeft",
        "MouthCornerPullRight", "MouthCornerPullLeft", "MouthCornerSlantRight", "MouthCornerSlantLeft",
        "MouthFrownRight", "MouthFrownLeft", "MouthStretchRight", "MouthStretchLeft",
        "MouthDimpleRight", "MouthDimpleLeft", "MouthRaiserUpper", "MouthRaiserLower",
        "MouthPressRight", "MouthPressLeft", "MouthTightenerRight", "MouthTightenerLeft",
        "TongueOut", "TongueUp", "TongueDown", "TongueRight", "TongueLeft", "TongueRoll", "TongueBendDown", "TongueCurlUp",
        "TongueSquish", "TongueFlat", "TongueTwistRight", "TongueTwistLeft",
        "SoftPalateClose", "ThroatSwallow", "NeckFlexRight", "NeckFlexLeft",
    ];

    private sealed record Avatar(string Name, string Id, IReadOnlyList<IParameterDefinition> Parameters, bool FullFaceTracking) : IAvatarInfo;

    private sealed class FakeOscTarget : IOscTarget
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsConnected
        {
            get; set;
        }
        public int InPort
        {
            get; set;
        }
        public string? DestinationAddress { get; set; } = "127.0.0.1";

        public int OutPort
        {
            get;
            set
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OutPort)));
            }
        }
    }

    private sealed class NullSettings : ILocalSettingsService
    {
        public Task<T?> ReadSettingAsync<T>(string key, T? defaultValue, bool forceLocal, CancellationToken cancellationToken) => Task.FromResult(defaultValue);
        public Task SaveSettingAsync<T>(string key, T value, bool forceLocal, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(object target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadAsync(object target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InlineDispatcher : IDispatcherService
    {
        public void Run(Action action) => action();
    }

    private static UnifiedTrackingData OpenEyes()
    {
        var data = new UnifiedTrackingData();
        data.Eye.Left.Openness = 1f;
        data.Eye.Right.Openness = 1f;
        return data;
    }

    private static byte[] Write(UnifiedTrackingData data)
    {
        byte[] bytes = new byte[NativeFaceBlob.Length];
        NativeFaceBlob.Write(data, bytes);
        return bytes;
    }

    private static int Key(UnifiedExpressions shape) => NativeFaceBlob.ShapeOffset + (int)shape;

    private static float Tan(float degrees) => MathF.Tan(degrees * MathF.PI / 180f);

    [Fact]
    public void BlobHasOneByteForEveryVrchatKey()
    {
        Assert.Equal(102, NativeFaceBlob.Length);
        Assert.Equal(NativeFaceBlob.Length, VrchatKeyOrder.Length);
    }

    [Fact]
    public void BlobShapesFollowVrchatKeyOrder()
    {
        for (int i = NativeFaceBlob.ShapeOffset; i < NativeFaceBlob.Length; i++)
        {
            Assert.Equal(VrchatKeyOrder[i], ((UnifiedExpressions)(i - NativeFaceBlob.ShapeOffset)).ToString());
        }

        Assert.Equal(36, Key(UnifiedExpressions.JawOpen));
    }

    [Fact]
    public void BlobNeutralFaceIsAllZero() => Assert.All(Write(OpenEyes()), b => Assert.Equal(0, b));

    [Fact]
    public void BlobSplitsGazeIntoLookKeysForEachEye()
    {
        UnifiedTrackingData data = OpenEyes();
        data.Eye.Right.Gaze = new Vector2(Tan(45f), Tan(9f));
        data.Eye.Left.Gaze = new Vector2(Tan(45f), Tan(-45f));

        byte[] bytes = Write(data);

        Assert.Equal(new byte[] { 255, 0, 51, 0, 0, 255, 0, 255 }, bytes[..8]);
    }

    [Fact]
    public void BlobClampsGazePastFortyFiveDegrees()
    {
        UnifiedTrackingData data = OpenEyes();
        data.Eye.Right.Gaze = new Vector2(Tan(-70f), Tan(-70f));
        data.Eye.Left.Gaze = new Vector2(Tan(-70f), Tan(70f));

        byte[] bytes = Write(data);

        Assert.Equal(new byte[] { 0, 255, 0, 255, 255, 0, 255, 0 }, bytes[..8]);
    }

    [Fact]
    public void BlobKeepsEachEyelidSeparate()
    {
        UnifiedTrackingData data = OpenEyes();
        data.Eye.Right.Openness = 0.25f;

        byte[] bytes = Write(data);

        Assert.Equal(191, bytes[8]);
        Assert.Equal(0, bytes[9]);
    }

    [Theory]
    [InlineData(8f, 255, 0)]
    [InlineData(6.5f, 128, 0)]
    [InlineData(2f, 0, 255)]
    public void BlobSplitsPupilAroundTheMiddleOfItsRange(float rightDiameter, byte dilation, byte constrict)
    {
        UnifiedTrackingData data = OpenEyes();
        data.Eye._minDilation = 2f;
        data.Eye._maxDilation = 8f;
        data.Eye.Left.PupilDiameter_MM = 5f;
        data.Eye.Right.PupilDiameter_MM = rightDiameter;

        byte[] bytes = Write(data);

        Assert.Equal(new byte[] { dilation, 0, constrict, 0 }, bytes[10..14]);
    }

    [Theory]
    [InlineData(0f, 10f, 5f)]
    [InlineData(999f, 0f, 5f)]
    [InlineData(5f, 5f, 5f)]
    [InlineData(5f, float.PositiveInfinity, 5f)]
    public void BlobSendsNoPupilAtTheMiddleOrWithoutARange(float min, float max, float diameter)
    {
        UnifiedTrackingData data = OpenEyes();
        data.Eye._minDilation = min;
        data.Eye._maxDilation = max;
        data.Eye.Left.PupilDiameter_MM = diameter;
        data.Eye.Right.PupilDiameter_MM = diameter;

        Assert.All(Write(data)[10..14], b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData(0.2f, 51)]
    [InlineData(1f, 255)]
    [InlineData(1.5f, 255)]
    [InlineData(-0.5f, 0)]
    [InlineData(float.NaN, 0)]
    public void BlobRoundsAndClampsShapeWeights(float weight, byte expected)
    {
        UnifiedTrackingData data = OpenEyes();
        data.Shapes[(int)UnifiedExpressions.JawOpen].Weight = weight;
        data.Shapes[(int)UnifiedExpressions.NeckFlexLeft].Weight = weight;

        byte[] bytes = Write(data);

        Assert.Equal(expected, bytes[Key(UnifiedExpressions.JawOpen)]);
        Assert.Equal(expected, bytes[NativeFaceBlob.Length - 1]);
    }

    [Fact]
    public void BlobRejectsAShortBuffer() => Assert.Throws<ArgumentException>(() => NativeFaceBlob.Write(OpenEyes(), new byte[NativeFaceBlob.Length - 1]));

    [Theory]
    [InlineData("avtr_a", "avtr_a", null, false, true)]
    [InlineData("avtr_a", "avtr_a", false, false, true)]
    [InlineData("avtr_a", "avtr_a", true, true, true)]
    [InlineData("avtr_a", "avtr_a", true, false, false)]
    [InlineData("avtr_a", "avtr_a", false, true, false)]
    [InlineData("avtr_b", "avtr_a", null, false, false)]
    [InlineData(null, "avtr_a", null, false, false)]
    public void IsAlreadyLoadedReloadsWhenVrchatReportsADifferentFaceTrackingFlag(
        string? newId, string loadedId, bool? reported, bool loaded, bool expected) => Assert.Equal(expected, OscQueryService.IsAlreadyLoaded(newId, loadedId, reported, loaded));

    private sealed record ParamDef(string Address, string Name, Type Type) : IParameterDefinition;

    private static readonly IParameterDefinition[] HybridAvatar =
    [
        new ParamDef("/avatar/parameters/EyeTrackingActive", "EyeTrackingActive", typeof(bool)),
        new ParamDef("/avatar/parameters/LipTrackingActive", "LipTrackingActive", typeof(bool)),
        new ParamDef("/avatar/parameters/ExpressionTrackingActive", "ExpressionTrackingActive", typeof(bool)),
        new ParamDef("/avatar/parameters/FT/v2/Head/Yaw", "FT/v2/Head/Yaw", typeof(float)),
        new ParamDef("/avatar/parameters/FT/v2/JawOpen", "FT/v2/JawOpen", typeof(float)),
        new ParamDef("/avatar/parameters/FT/v2/MouthClosed2", "FT/v2/MouthClosed2", typeof(bool)),
        new ParamDef("/avatar/parameters/FT/v2/EyeLidLeft", "FT/v2/EyeLidLeft", typeof(float)),
        new ParamDef("/avatar/parameters/FaceTrackingLimits", "FaceTrackingLimits", typeof(bool)),
    ];

    private static string[] SentNames(IEnumerable<Parameter> relevant) =>
        [.. relevant.SelectMany(parameter => parameter.GetParamNames()).Select(name => name.paramName)];

    private static int TrackingEyeCount(IEnumerable<Parameter> relevant) =>
        relevant.Count(parameter => parameter.GetType().IsGenericType && parameter.GetType().GetGenericTypeDefinition() == typeof(NativeParameter<>));

    [Fact]
    public void FullFaceTrackingAvatarKeepsStatusAndHeadParamsAndLeavesExpressionsToVrchat()
    {
        ParameterSenderService.AllParametersRelevantStatic = false;
        var leftToVrchat = new List<string>();

        try
        {
            List<Parameter> sent = AvatarParameters.Reset(HybridAvatar, fullFaceTracking: true, leftToVrchat);
            string[] names = SentNames(sent);

            Assert.Contains("EyeTrackingActive", names, StringComparer.Ordinal);
            Assert.Contains("LipTrackingActive", names, StringComparer.Ordinal);
            Assert.Contains("ExpressionTrackingActive", names, StringComparer.Ordinal);
            Assert.Contains("v2/Head/Yaw", names, StringComparer.Ordinal);
            Assert.DoesNotContain("v2/JawOpen", names, StringComparer.Ordinal);
            Assert.DoesNotContain("v2/MouthClosed2", names, StringComparer.Ordinal);
            Assert.DoesNotContain("v2/EyeLidLeft", names, StringComparer.Ordinal);
            Assert.Equal(2, TrackingEyeCount(sent));
            Assert.Equal(["FT/v2/JawOpen", "FT/v2/MouthClosed2", "FT/v2/EyeLidLeft"], leftToVrchat, StringComparer.Ordinal);
        }
        finally
        {
            AvatarParameters.Reset([], fullFaceTracking: false, []);
        }
    }

    [Fact]
    public void SwitchingFromFullFaceTrackingToALegacyAvatarSendsEveryListedParamAgain()
    {
        ParameterSenderService.AllParametersRelevantStatic = false;
        var leftToVrchat = new List<string>();

        try
        {
            AvatarParameters.Reset(HybridAvatar, fullFaceTracking: true, []);
            List<Parameter> sent = AvatarParameters.Reset(HybridAvatar, fullFaceTracking: false, leftToVrchat);
            string[] names = SentNames(sent);

            Assert.Contains("EyeTrackingActive", names, StringComparer.Ordinal);
            Assert.Contains("v2/JawOpen", names, StringComparer.Ordinal);
            Assert.Contains("v2/MouthClosed2", names, StringComparer.Ordinal);
            Assert.Contains("v2/EyeLidLeft", names, StringComparer.Ordinal);
            Assert.Equal(1, TrackingEyeCount(sent));
            Assert.Empty(leftToVrchat);
        }
        finally
        {
            AvatarParameters.Reset([], fullFaceTracking: false, []);
        }
    }

    [Fact]
    public void AllParametersRelevantStillSendsEverythingToAFullFaceTrackingAvatar()
    {
        ParameterSenderService.AllParametersRelevantStatic = true;
        var leftToVrchat = new List<string>();

        try
        {
            string[] names = SentNames(AvatarParameters.Reset(HybridAvatar, fullFaceTracking: true, leftToVrchat));

            Assert.Contains("v2/JawOpen", names, StringComparer.Ordinal);
            Assert.Contains("EyeTrackingActive", names, StringComparer.Ordinal);
            Assert.Empty(leftToVrchat);
        }
        finally
        {
            ParameterSenderService.AllParametersRelevantStatic = false;
            AvatarParameters.Reset([], fullFaceTracking: false, []);
        }
    }

    [Fact]
    public async Task SenderSendsTheBlobOnlyToAFaceTrackingAvatarWhileAModuleIsTracking()
    {
        ParameterSenderService.AllParametersRelevantStatic = false;
        UnifiedTrackingMutator previousMutator = TrackingOutput.Mutator;
        bool tracking = true;

        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var target = new FakeOscTarget();
        var send = new OscSendService(NullLogger<OscSendService>.Instance, target);
        target.OutPort = ((IPEndPoint)listener.LocalEndPoint!).Port;
        var mutator = new UnifiedTrackingMutator(NullLogger<UnifiedTrackingMutator>.Instance, new NullSettings(), new InlineDispatcher());
        var sender = new ParameterSenderService(send, mutator, NullLogger<ParameterSenderService>.Instance, new TimerResolutionHold(NullLogger.Instance));
        var face = new BinaryFaceDataSender(NullLogger<BinaryFaceDataSender>.Instance) { TrackingActive = () => Volatile.Read(ref tracking) };

        try
        {
            SetFace(leftOpenness: 1f, rightOpenness: 0f, jawOpen: 0.2f);

            await face.StartAsync(CancellationToken.None);
            await sender.StartAsync(CancellationToken.None);

            face.SetAvatar(new Avatar("Native", "avtr_native", [], FullFaceTracking: true));
            Assert.True(face.AvatarUsesNativeFaceTracking);
            UnifiedTracking.MarkDataUpdated();
            AssertFaceBlob(ReceiveBlob(listener, NativeFaceBlob.Address, TimeSpan.FromSeconds(3)));

            Volatile.Write(ref tracking, false);
            await AssertNoBlobAsync(listener, CancellationToken.None);

            Volatile.Write(ref tracking, true);
            face.SetAvatar(new Avatar("Legacy", "avtr_legacy", [], FullFaceTracking: false));
            Assert.False(face.AvatarUsesNativeFaceTracking);
            await AssertNoBlobAsync(listener, CancellationToken.None);
        }
        finally
        {
            await sender.StopAsync(CancellationToken.None);
            sender.Dispose();
            await face.StopAsync(CancellationToken.None);
            SetFace(leftOpenness: 0f, rightOpenness: 0f, jawOpen: 0f);
            TrackingOutput.Mutator = previousMutator;
            ParameterSenderService.Clear();
        }
    }

    private static void SetFace(float leftOpenness, float rightOpenness, float jawOpen)
    {
        using (UnifiedTracking.LockData())
        {
            UnifiedTracking.Data.Eye.Left.Openness = leftOpenness;
            UnifiedTracking.Data.Eye.Right.Openness = rightOpenness;
            UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.JawOpen].Weight = jawOpen;
        }
    }

    private static void AssertFaceBlob(byte[]? blob)
    {
        Assert.NotNull(blob);
        Assert.Equal(NativeFaceBlob.Length, blob.Length);
        Assert.Equal(51, blob[Key(UnifiedExpressions.JawOpen)]);
        Assert.Equal(255, blob[8]);
        Assert.Equal(0, blob[9]);
    }

    private static async Task AssertNoBlobAsync(Socket listener, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        Drain(listener);
        UnifiedTracking.MarkDataUpdated();

        Assert.Null(ReceiveBlob(listener, NativeFaceBlob.Address, TimeSpan.FromMilliseconds(500)));
    }

    private static byte[]? ReceiveBlob(Socket listener, string address, TimeSpan timeout)
    {
        byte[] buffer = new byte[65536];
        byte[] pattern = Encoding.ASCII.GetBytes(address + "\0");
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            listener.ReceiveTimeout = Math.Max(1, (int)(timeout - watch.Elapsed).TotalMilliseconds);
            int length;
            try
            {
                length = listener.Receive(buffer);
            }
            catch (SocketException)
            {
                return null;
            }

            int start = buffer.AsSpan(0, length).IndexOf(pattern);
            if (start < 0)
            {
                continue;
            }

            int index = start;
            if (OscCodec.TryParse(buffer, length, ref index, out OscMessage? message) && message._values is [{ Type: OscValueType.Blob } value])
            {
                return value.BlobValue;
            }
        }

        return null;
    }

    private static void Drain(Socket listener)
    {
        byte[] buffer = new byte[65536];
        while (listener.Available > 0)
        {
            listener.Receive(buffer);
        }
    }
}
