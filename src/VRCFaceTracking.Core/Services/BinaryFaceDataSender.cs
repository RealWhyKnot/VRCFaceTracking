using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Osc;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;

namespace VRCFaceTracking.Core.Services;

public sealed partial class BinaryFaceDataSender : IHostedService
{
    private readonly ILogger<BinaryFaceDataSender> _logger;
    private readonly byte[] _blob = new byte[NativeFaceBlob.Length];
    private readonly OscMessage _message;
    private volatile bool _avatarUsesNativeFaceTracking;

    public BinaryFaceDataSender(ILogger<BinaryFaceDataSender> logger)
    {
        _logger = logger;
        _message = OscMessage.CreateBlob(NativeFaceBlob.Address, _blob);
    }

    public bool AvatarUsesNativeFaceTracking => _avatarUsesNativeFaceTracking;

    internal Func<bool> TrackingActive
    {
        get; init;
    } = () =>
        UnifiedLibManager.EyeStatus == ModuleState.Active || UnifiedLibManager.ExpressionStatus == ModuleState.Active;

    public void SetAvatar(IAvatarInfo avatar)
    {
        bool usesNativeFaceTracking = avatar is { FullFaceTracking: true };
        if (usesNativeFaceTracking == _avatarUsesNativeFaceTracking)
        {
            return;
        }

        _avatarUsesNativeFaceTracking = usesNativeFaceTracking;
        if (usesNativeFaceTracking)
        {
            LogAvatarUsesVRChatFaceTracking(_logger, avatar.Id, NativeFaceBlob.Address);
        }
        else
        {
            LogAvatarDoesnUseVRChatFace(_logger, avatar?.Id, NativeFaceBlob.Address);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        TrackingOutput.DataUpdated += OnDataUpdated;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        TrackingOutput.DataUpdated -= OnDataUpdated;
        return Task.CompletedTask;
    }

    internal void OnDataUpdated(object? sender, TrackingDataEventArgs e)
    {
        if ((!_avatarUsesNativeFaceTracking && !ParameterSenderService.AllParametersRelevantStatic) || !TrackingActive())
        {
            return;
        }

        NativeFaceBlob.Write(e.Data, _blob);
        ParameterSenderService.Enqueue(_message);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Avatar {AvatarId} uses VRChat face tracking. Sending face data to {Address}")]
    private static partial void LogAvatarUsesVRChatFaceTracking(ILogger logger, string avatarId, string address);

    [LoggerMessage(Level = LogLevel.Information, Message = "Avatar {AvatarId} doesn't use VRChat face tracking. Stopped sending to {Address}")]
    private static partial void LogAvatarDoesnUseVRChatFace(ILogger logger, string? avatarId, string address);
}
