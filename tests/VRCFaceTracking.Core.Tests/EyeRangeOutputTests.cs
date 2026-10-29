using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.OSC;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.DataTypes;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Services;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Tests;

[Collection("UnifiedTrackingState")]
public class EyeRangeOutputTests
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

    private static readonly IParameterDefinition[] EyeParamAvatar =
    {
        new ParamDef(RawLeftX, "FT/v2/EyeLeftX", typeof(float)),
        new ParamDef(RawLeftY, "FT/v2/EyeLeftY", typeof(float)),
        new ParamDef(LinearRightX, "FT/v2/EyeRightLinearX", typeof(float)),
        new ParamDef(LinearRightY, "FT/v2/EyeRightLinearY", typeof(float)),
    };

    private static Parameter Find(string name) =>
        UnifiedExpressionsParameters.UnifiedCombinedShapes.Single(p => p.GetParamNames().Any(n => n.Item1 == name));

    private static Dictionary<string, float[]> Receive(Socket listener, params string[] addresses)
    {
        var found = new Dictionary<string, float[]>();
        var buffer = new byte[65536];
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
            foreach (var address in addresses)
            {
                var start = buffer.AsSpan(0, length).IndexOf(Encoding.ASCII.GetBytes(address + "\0"));
                if (start < 0)
                {
                    continue;
                }
                var index = start;
                var message = OscCodec.TryParse(buffer, length, ref index);
                if (message != null)
                {
                    found[address] = message.Values.Select(v => v.FloatValue).ToArray();
                }
            }
        }
        return found;
    }

    [Fact]
    public async Task RangeLimit_ReachesAvatarParamsAndNativeEyeOutputOnTheWire()
    {
        ParameterSenderService.AllParametersRelevantStatic = false;
        var previousMutator = UnifiedTracking.Mutator;
        var eyeLeft = Find("v2/EyeLeftX");
        var eyeRightLinear = Find("v2/EyeRightLinearX");
        var native = UnifiedExpressionsParameters.UnifiedCombinedShapes.OfType<NativeParameter<Vector4>>().Single();

        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.ReceiveTimeout = 1000;
        var target = new FakeOscTarget();
        var send = new OscSendService(NullLogger<OscSendService>.Instance, target);
        target.OutPort = ((IPEndPoint)listener.LocalEndPoint!).Port;
        var mutator = new UnifiedTrackingMutator(NullLogger<UnifiedTrackingMutator>.Instance, new NullSettings(), new InlineDispatcher());
        mutator._mutations.Add(new EyeAssists
        {
            Logger = NullLogger.Instance,
            IsActive = true,
            limitEyeRange = true,
            maxHorizontal = 0.3f,
            maxVertical = 0.2f,
        });
        mutator.Enabled = true;
        var sender = new ParameterSenderService(send, mutator, NullLogger<ParameterSenderService>.Instance, new TimerResolutionHold(NullLogger.Instance));

        try
        {
            lock (UnifiedTracking.DataLock)
            {
                UnifiedTracking.Data.Eye.Left.Gaze = new Vector2(1f, 1f);
                UnifiedTracking.Data.Eye.Right.Gaze = new Vector2(-1f, -1f);
            }
            Assert.Equal(2, eyeLeft.ResetParam(EyeParamAvatar).Length);
            Assert.Equal(2, eyeRightLinear.ResetParam(EyeParamAvatar).Length);
            Assert.Single(native.ResetParam(Array.Empty<IParameterDefinition>()));

            await sender.StartAsync(CancellationToken.None);
            UnifiedTracking.MarkDataUpdated();
            var sent = Receive(listener, RawLeftX, RawLeftY, LinearRightX, LinearRightY, NativeAddress);

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
        finally
        {
            await sender.StopAsync(CancellationToken.None);
            sender.Dispose();
            eyeLeft.ResetParam(Array.Empty<IParameterDefinition>());
            eyeRightLinear.ResetParam(Array.Empty<IParameterDefinition>());
            native.ResetParam(EyeParamAvatar);
            lock (UnifiedTracking.DataLock)
            {
                UnifiedTracking.Data.Eye.Left.Gaze = Vector2.zero;
                UnifiedTracking.Data.Eye.Right.Gaze = Vector2.zero;
            }
            UnifiedTracking.Mutator = previousMutator;
            ParameterSenderService.Clear();
        }
    }
}
