namespace VRCFaceTracking.Contracts.Services;

internal interface IActivationService
{
    Task ActivateAsync(CancellationToken cancellationToken);
}
