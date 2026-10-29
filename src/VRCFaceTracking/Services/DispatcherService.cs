using Avalonia.Threading;
using VRCFaceTracking.Core.Contracts.Services;

namespace VRCFaceTracking.Services;

internal sealed class DispatcherService : IDispatcherService
{
    public void Run(Action action) => Dispatcher.UIThread.Post(action.Invoke);
}
