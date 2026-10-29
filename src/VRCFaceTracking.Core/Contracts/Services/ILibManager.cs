using System.Collections.ObjectModel;
using VRCFaceTracking.Core.Library;

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
    void Initialize();
    void TeardownAllAndReset();
    void SetImageStreamEnabled(bool enabled);
}
