using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.OSC;
using VRCFaceTracking.Core.Params.Data;

namespace VRCFaceTracking.Core.Services;

public class ParameterSenderService : BackgroundService
{
    // We probably don't need a queue since we use osc message bundles, but for now, we're keeping it as
    // we might want to allow a way for the user to specify bundle or single message sends in the future
    private static readonly List<OscMessage> SendQueue = new();

    private readonly OscSendService _sendService;
    private readonly ILogger<ParameterSenderService> _logger;
    private readonly UnifiedTrackingMutator _mutator; // We don't use this but we do want DI to run its constructor

    public static bool AllParametersRelevantStatic
    {
        get; set;
    }
    public bool AllParametersRelevant
    {
        get => AllParametersRelevantStatic;
        set
        {
            if (AllParametersRelevantStatic == value) return;
            AllParametersRelevantStatic = value;
            SendQueue.Clear();
            foreach (var parameter in UnifiedTracking.AllParameters)
            {
                parameter.ResetParam(Array.Empty<IParameterDefinition>());
            }
        }
    }

    public ParameterSenderService(OscSendService sendService, UnifiedTrackingMutator mutator, ILogger<ParameterSenderService> logger)
    {
        _sendService = sendService;
        _logger = logger;
        _mutator = mutator;
    }

    public static void Enqueue(OscMessage message) => SendQueue.Add(message);
    public static void Clear() => SendQueue.Clear();

    protected async override Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(10, cancellationToken);

                UnifiedTracking.UpdateData();

                if (SendQueue.Count <= 0)
                {
                    continue;
                }

                await _sendService.Send(SendQueue, cancellationToken);

                SendQueue.Clear();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to send {Count} queued OSC messages", SendQueue.Count);
            }
        }
    }
}