using System.Collections.ObjectModel;
using VRCFaceTracking.Core.Library;

namespace VRCFaceTracking.Core.Contracts.Services;

public interface ILibManager
{
    public ObservableCollection<ModuleMetadataInternal> LoadedModulesMetadata
    {
        get; set;
    }
    public ModuleInitProgress InitProgress
    {
        get;
    }
    public void Initialize();
    void TeardownAllAndReset();
    void SetImageStreamEnabled(bool enabled);
}