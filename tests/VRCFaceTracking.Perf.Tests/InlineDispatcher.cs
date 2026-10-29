using VRCFaceTracking.Core.Contracts.Services;

namespace VRCFaceTracking.Perf.Tests;

internal sealed class InlineDispatcher : IDispatcherService
{
    public void Run(Action action) => action();
}
