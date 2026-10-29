namespace VRCFaceTracking.Core.Contracts.Services;

public interface IDispatcherService
{
    void Run(Action action);
}
