namespace VRCFaceTracking.Core.Contracts.Services;

public interface IMainService
{
    Action<string, float> ParameterUpdate
    {
        get; set;
    }

    Task TeardownAsync(CancellationToken cancellationToken);
    Task InitializeAsync(CancellationToken cancellationToken);
}
