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
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.DataTypes;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Services;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Tests;

[Collection("UnifiedTrackingState")]
public sealed class EyeRangeOutputTests
{
    private const string NativeAddress = "/tracking/eye/LeftRightPitchYaw";
    private const string RawLeftX = "/avatar/parameters/FT/v2/EyeLeftX";
    private const string RawLeftY = "/avatar/parameters/FT/v2/EyeLeftY";
    private const string LinearRightX = "/avatar/parameters/FT/v2/EyeRightLinearX";
    private const string LinearRightY = "/avatar/parameters/FT/v2/EyeRightLinearY";

    private sealed record ParamDef(string Address, string Name, Type Type) : IParameterDefinition;

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

    private static readonly IParameterDefinition[] EyeParamAvatar =
    [
        new ParamDef(RawLeftX, "FT/v2/EyeLeftX", typeof(float)),
        new ParamDef(RawLeftY, "FT/v2/EyeLeftY", typeof(float)),
        new ParamDef(LinearRightX, "FT/v2/EyeRightLinearX", typeof(float)),
        new ParamDef(LinearRightY, "FT/v2/EyeRightLinearY", typeof(float)),
    ];

    private static Parameter Find(string name) =>
        UnifiedExpressionsParameters.UnifiedCombinedShapes.Single(p => p.GetParamNames().Any(n => string.Equals(n.paramName, name, StringComparison.Ordinal)));

    private static Dictionary<string, float[]> Receive(Socket listener, params string[] addresses)
    {
        var found = new Dictionary<string, float[]>(StringComparer.Ordinal);
        byte[] buffer = new byte[65536];
        var watch = Stopwatch.StartNew();
        while (found.Count < addresses.Length && watch.ElapsedMilliseconds < 3000)
        {
            int length;
            try
            {
                length = listener.Receive(buffer);
            }
            catch (SocketException)
            {
                break;
            }
            foreach (string address in addresses)
            {
                int start = buffer.AsSpan(0, length).IndexOf(Encoding.ASCII.GetBytes(address + "\0"));
                if (start < 0)
                {
                    continue;
                }
                int index = start;
                if (OscCodec.TryParse(buffer, length, ref index, out OscMessage? message))
                {
                    found[address] = [.. message._values.Select(v => v.FloatValue)];
                }
            }
        }
        return found;
    }

    [Fact]
    public async Task RangeLimitReachesAvatarParamsAndNativeEyeOutputOnTheWire()
    {
        ParameterSenderService.AllParametersRelevantStatic = false;
        UnifiedTrackingMutator previousMutator = TrackingOutput.Mutator;
        Parameter eyeLeft = Find("v2/EyeLeftX");
        Parameter eyeRightLinear = Find("v2/EyeRightLinearX");
        NativeParameter<Vector4> native = UnifiedExpressionsParameters.UnifiedCombinedShapes.OfType<NativeParameter<Vector4>>().Single();

        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.ReceiveTimeout = 1000;
        ParameterSenderService sender = CreateRangeLimitedSender(((IPEndPoint)listener.LocalEndPoint!).Port);

        try
        {
            SetGaze(new Vector2(1f, 1f), new Vector2(-1f, -1f));
            Assert.Equal(2, eyeLeft.ResetParam(EyeParamAvatar).Length);
            Assert.Equal(2, eyeRightLinear.ResetParam(EyeParamAvatar).Length);
            Assert.Single(native.ResetParam([]));

            await sender.StartAsync(CancellationToken.None);
            UnifiedTracking.MarkDataUpdated();
            AssertRangeLimited(Receive(listener, RawLeftX, RawLeftY, LinearRightX, LinearRightY, NativeAddress));
        }
        finally
        {
            await sender.StopAsync(CancellationToken.None);
            sender.Dispose();
            eyeLeft.ResetParam([]);
            eyeRightLinear.ResetParam([]);
            native.ResetParam(EyeParamAvatar);
            SetGaze(Vector2.zero, Vector2.zero);
            TrackingOutput.Mutator = previousMutator;
            ParameterSenderService.Clear();
        }
    }

    private static ParameterSenderService CreateRangeLimitedSender(int port)
    {
        var target = new FakeOscTarget();
        var send = new OscSendService(NullLogger<OscSendService>.Instance, target);
        target.OutPort = port;
        var mutator = new UnifiedTrackingMutator(NullLogger<UnifiedTrackingMutator>.Instance, new NullSettings(), new InlineDispatcher());
        mutator.Mutations.Add(new EyeAssists
        {
            Logger = NullLogger.Instance,
            IsActive = true,
            LimitEyeRange = true,
            MaxHorizontal = 0.3f,
            MaxVertical = 0.2f,
        });
        mutator.Enabled = true;
        return new ParameterSenderService(send, mutator, NullLogger<ParameterSenderService>.Instance, new TimerResolutionHold(NullLogger.Instance));
    }

    private static void SetGaze(Vector2 left, Vector2 right)
    {
        using (UnifiedTracking.LockData())
        {
            UnifiedTracking.Data.Eye.Left.Gaze = left;
            UnifiedTracking.Data.Eye.Right.Gaze = right;
        }
    }

    private static void AssertRangeLimited(Dictionary<string, float[]> sent)
    {
        Assert.Equal(0.240079f, Assert.Single(sent[RawLeftX]), 4);
        Assert.Equal(0.158384f, Assert.Single(sent[RawLeftY]), 4);
        Assert.Equal(-0.3f, Assert.Single(sent[LinearRightX]), 4);
        Assert.Equal(0.2f, Assert.Single(sent[LinearRightY]), 4);
        Assert.Equal(4, sent[NativeAddress].Length);
        Assert.Equal(-9f, sent[NativeAddress][0], 3);
        Assert.Equal(13.5f, sent[NativeAddress][1], 3);
        Assert.Equal(9f, sent[NativeAddress][2], 3);
        Assert.Equal(-13.5f, sent[NativeAddress][3], 3);
    }
}
