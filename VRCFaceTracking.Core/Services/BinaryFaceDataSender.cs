using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.OSC;
using VRCFaceTracking.Core.Params.Data;

namespace VRCFaceTracking.Core.Services;

public sealed class BinaryFaceDataSender : IHostedService
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
        var usesNativeFaceTracking = avatar is { FullFaceTracking: true };
        if (usesNativeFaceTracking == _avatarUsesNativeFaceTracking)
        {
            return;
        }

        _avatarUsesNativeFaceTracking = usesNativeFaceTracking;
        if (usesNativeFaceTracking)
        {
            _logger.LogInformation("Avatar {AvatarId} uses VRChat face tracking. Sending face data to {Address}", avatar.Id, NativeFaceBlob.Address);
        }
        else
        {
            _logger.LogInformation("Avatar {AvatarId} doesn't use VRChat face tracking. Stopped sending to {Address}", avatar?.Id, NativeFaceBlob.Address);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        UnifiedTracking.OnUnifiedDataUpdated += OnDataUpdated;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        UnifiedTracking.OnUnifiedDataUpdated -= OnDataUpdated;
        return Task.CompletedTask;
    }

    internal void OnDataUpdated(UnifiedTrackingData data)
    {
        if (!_avatarUsesNativeFaceTracking && !ParameterSenderService.AllParametersRelevantStatic || !TrackingActive())
        {
            return;
        }

        NativeFaceBlob.Write(data, _blob);
        ParameterSenderService.Enqueue(_message);
    }
}
