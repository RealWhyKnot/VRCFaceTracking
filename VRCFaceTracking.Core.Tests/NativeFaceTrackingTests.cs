using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.OSC;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Services;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Tests;

[Collection("UnifiedTrackingState")]
public class NativeFaceTrackingTests
{
    private static readonly string[] VrchatKeyOrder =
    {
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
    };

    private sealed record Avatar(string Name, string Id, IParameterDefinition[] Parameters, bool FullFaceTracking) : IAvatarInfo;

    private sealed class FakeOscTarget : IOscTarget
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int _outPort;

        public bool IsConnected
        {
            get; set;
        }
        public int InPort
        {
            get; set;
        }
        public string DestinationAddress { get; set; } = "127.0.0.1";

        public int OutPort
        {
            get => _outPort;
            set
            {
                _outPort = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OutPort)));
            }
        }
    }

    private sealed class NullSettings : ILocalSettingsService
    {
        public Task<T> ReadSettingAsync<T>(string key, T? defaultValue = default, bool forceLocal = false) => Task.FromResult(defaultValue!);
        public Task SaveSettingAsync<T>(string key, T value, bool forceLocal = false) => Task.CompletedTask;
        public Task Save(object target) => Task.CompletedTask;
        public Task Load(object target) => Task.CompletedTask;
        public Task FlushAsync() => Task.CompletedTask;
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
        var bytes = new byte[NativeFaceBlob.Length];
        NativeFaceBlob.Write(data, bytes);
        return bytes;
    }

    private static int Key(UnifiedExpressions shape) => NativeFaceBlob.ShapeOffset + (int)shape;

    private static float Tan(float degrees) => MathF.Tan(degrees * MathF.PI / 180f);

    [Fact]
    public void Blob_HasOneByteForEveryVrchatKey()
    {
        Assert.Equal(102, NativeFaceBlob.Length);
        Assert.Equal(NativeFaceBlob.Length, VrchatKeyOrder.Length);
    }

    [Fact]
    public void Blob_ShapesFollowVrchatKeyOrder()
    {
        for (var i = NativeFaceBlob.ShapeOffset; i < NativeFaceBlob.Length; i++)
        {
            Assert.Equal(VrchatKeyOrder[i], ((UnifiedExpressions)(i - NativeFaceBlob.ShapeOffset)).ToString());
        }

        Assert.Equal(36, Key(UnifiedExpressions.JawOpen));
    }

    [Fact]
    public void Blob_NeutralFaceIsAllZero()
    {
        Assert.All(Write(OpenEyes()), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Blob_SplitsGazeIntoLookKeysForEachEye()
    {
        var data = OpenEyes();
        data.Eye.Right.Gaze = new Vector2(Tan(45f), Tan(9f));
        data.Eye.Left.Gaze = new Vector2(Tan(45f), Tan(-45f));

        var bytes = Write(data);

        Assert.Equal(new byte[] { 255, 0, 51, 0, 0, 255, 0, 255 }, bytes[..8]);
    }

    [Fact]
    public void Blob_ClampsGazePastFortyFiveDegrees()
    {
        var data = OpenEyes();
        data.Eye.Right.Gaze = new Vector2(Tan(-70f), Tan(-70f));
        data.Eye.Left.Gaze = new Vector2(Tan(-70f), Tan(70f));

        var bytes = Write(data);

        Assert.Equal(new byte[] { 0, 255, 0, 255, 255, 0, 255, 0 }, bytes[..8]);
    }

    [Fact]
    public void Blob_KeepsEachEyelidSeparate()
    {
        var data = OpenEyes();
        data.Eye.Right.Openness = 0.25f;

        var bytes = Write(data);

        Assert.Equal(191, bytes[8]);
        Assert.Equal(0, bytes[9]);
    }

    [Theory]
    [InlineData(8f, 255, 0)]
    [InlineData(6.5f, 128, 0)]
    [InlineData(2f, 0, 255)]
    public void Blob_SplitsPupilAroundTheMiddleOfItsRange(float rightDiameter, byte dilation, byte constrict)
    {
        var data = OpenEyes();
        data.Eye._minDilation = 2f;
        data.Eye._maxDilation = 8f;
        data.Eye.Left.PupilDiameter_MM = 5f;
        data.Eye.Right.PupilDiameter_MM = rightDiameter;

        var bytes = Write(data);

        Assert.Equal(new byte[] { dilation, 0, constrict, 0 }, bytes[10..14]);
    }

    [Theory]
    [InlineData(0f, 10f, 5f)]
    [InlineData(999f, 0f, 5f)]
    [InlineData(5f, 5f, 5f)]
    [InlineData(5f, float.PositiveInfinity, 5f)]
    public void Blob_SendsNoPupilAtTheMiddleOrWithoutARange(float min, float max, float diameter)
    {
        var data = OpenEyes();
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
    public void Blob_RoundsAndClampsShapeWeights(float weight, byte expected)
    {
        var data = OpenEyes();
        data.Shapes[(int)UnifiedExpressions.JawOpen].Weight = weight;
        data.Shapes[(int)UnifiedExpressions.NeckFlexLeft].Weight = weight;

        var bytes = Write(data);

        Assert.Equal(expected, bytes[Key(UnifiedExpressions.JawOpen)]);
        Assert.Equal(expected, bytes[NativeFaceBlob.Length - 1]);
    }

    [Fact]
    public void Blob_RejectsAShortBuffer()
    {
        Assert.Throws<ArgumentException>(() => NativeFaceBlob.Write(OpenEyes(), new byte[NativeFaceBlob.Length - 1]));
    }

    [Theory]
    [InlineData("avtr_a", "avtr_a", null, false, true)]
    [InlineData("avtr_a", "avtr_a", false, false, true)]
    [InlineData("avtr_a", "avtr_a", true, true, true)]
    [InlineData("avtr_a", "avtr_a", true, false, false)]
    [InlineData("avtr_a", "avtr_a", false, true, false)]
    [InlineData("avtr_b", "avtr_a", null, false, false)]
    [InlineData(null, "avtr_a", null, false, false)]
    public void IsAlreadyLoaded_ReloadsWhenVrchatReportsADifferentFaceTrackingFlag(
        string? newId, string loadedId, bool? reported, bool loaded, bool expected)
    {
        Assert.Equal(expected, OscQueryService.IsAlreadyLoaded(newId!, loadedId, reported, loaded));
    }

    [Fact]
    public async Task Sender_SendsTheBlobOnlyWhileTheAvatarUsesVrchatFaceTracking()
    {
        ParameterSenderService.AllParametersRelevantStatic = false;
        var previousMutator = UnifiedTracking.Mutator;

        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var target = new FakeOscTarget();
        var send = new OscSendService(NullLogger<OscSendService>.Instance, target);
        target.OutPort = ((IPEndPoint)listener.LocalEndPoint!).Port;
        var mutator = new UnifiedTrackingMutator(NullLogger<UnifiedTrackingMutator>.Instance, new NullSettings(), new InlineDispatcher());
        var sender = new ParameterSenderService(send, mutator, NullLogger<ParameterSenderService>.Instance, new TimerResolutionHold(NullLogger.Instance));
        var face = new BinaryFaceDataSender(NullLogger<BinaryFaceDataSender>.Instance);

        try
        {
            lock (UnifiedTracking.DataLock)
            {
                UnifiedTracking.Data.Eye.Left.Openness = 1f;
                UnifiedTracking.Data.Eye.Right.Openness = 0f;
                UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.JawOpen].Weight = 0.2f;
            }

            await face.StartAsync(CancellationToken.None);
            await sender.StartAsync(CancellationToken.None);

            face.SetAvatar(new Avatar("Native", "avtr_native", Array.Empty<IParameterDefinition>(), true));
            Assert.True(face.AvatarUsesNativeFaceTracking);
            UnifiedTracking.MarkDataUpdated();
            var blob = ReceiveBlob(listener, NativeFaceBlob.Address, TimeSpan.FromSeconds(3));

            Assert.NotNull(blob);
            Assert.Equal(NativeFaceBlob.Length, blob.Length);
            Assert.Equal(51, blob[Key(UnifiedExpressions.JawOpen)]);
            Assert.Equal(255, blob[8]);
            Assert.Equal(0, blob[9]);

            face.SetAvatar(new Avatar("Legacy", "avtr_legacy", Array.Empty<IParameterDefinition>(), false));
            Assert.False(face.AvatarUsesNativeFaceTracking);
            await Task.Delay(100);
            Drain(listener);
            UnifiedTracking.MarkDataUpdated();

            Assert.Null(ReceiveBlob(listener, NativeFaceBlob.Address, TimeSpan.FromMilliseconds(500)));
        }
        finally
        {
            await sender.StopAsync(CancellationToken.None);
            sender.Dispose();
            await face.StopAsync(CancellationToken.None);
            lock (UnifiedTracking.DataLock)
            {
                UnifiedTracking.Data.Eye.Left.Openness = 0f;
                UnifiedTracking.Data.Eye.Right.Openness = 0f;
                UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.JawOpen].Weight = 0f;
            }
            UnifiedTracking.Mutator = previousMutator;
            ParameterSenderService.Clear();
        }
    }

    private static byte[]? ReceiveBlob(Socket listener, string address, TimeSpan timeout)
    {
        var buffer = new byte[65536];
        var pattern = Encoding.ASCII.GetBytes(address + "\0");
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

            var start = buffer.AsSpan(0, length).IndexOf(pattern);
            if (start < 0)
            {
                continue;
            }

            var index = start;
            var message = OscCodec.TryParse(buffer, length, ref index);
            if (message?.Values is [{ Type: OscValueType.Blob } value])
            {
                return value.BlobValue;
            }
        }

        return null;
    }

    private static void Drain(Socket listener)
    {
        var buffer = new byte[65536];
        while (listener.Available > 0)
        {
            listener.Receive(buffer);
        }
    }
}
