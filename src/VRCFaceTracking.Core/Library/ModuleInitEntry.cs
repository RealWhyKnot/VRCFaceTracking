using System.ComponentModel;

namespace VRCFaceTracking.Core.Library;

public sealed class ModuleInitEntry : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public ModuleInitEntry(string name, ModuleInitStage stage)
    {
        Name = name;
        _stage = stage;
    }

    public string Name
    {
        get;
    }

    private ModuleInitStage _stage;

    public ModuleInitStage Stage
    {
        get => _stage;
        set
        {
            if (_stage == value)
            {
                return;
            }
            _stage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Stage)));
        }
    }
}
