using System.Collections.ObjectModel;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Contracts.Services;

public interface ILibManager
{
    ObservableCollection<ModuleMetadataInternal> LoadedModulesMetadata
    {
        get;
    }
    ModuleInitProgress InitProgress
    {
        get;
    }
    TrackingOwnershipSnapshot GetOwnershipSnapshot();
    void Initialize();
    void TeardownAllAndReset();
    CameraStreamRegistry CameraStreams
    {
        get;
    }
    void SetImageStreamEnabled(bool enabled);
    bool SendCameraControl(Guid moduleId, CameraControlCommand command);
}
