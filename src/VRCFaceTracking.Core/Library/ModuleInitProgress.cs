using System.Collections.ObjectModel;
using System.ComponentModel;
using VRCFaceTracking.Core.Contracts.Services;

namespace VRCFaceTracking.Core.Library;

public enum ModuleInitStage
{
    Handshake,
    Capabilities,
    Initializing,
    TimedOut,
}

public class ModuleInitEntry : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

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

public class ModuleInitProgress : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    private readonly IDispatcherService _dispatcher;
    private readonly Dictionary<string, ModuleInitEntry> _byPath = new();
    private bool _isInitializing;
    private bool _hasPending;

    public ModuleInitProgress(IDispatcherService dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public ObservableCollection<ModuleInitEntry> Modules { get; } = new();

    public bool IsInitializing
    {
        get => _isInitializing;
        private set
        {
            if (_isInitializing == value)
            {
                return;
            }
            _isInitializing = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInitializing)));
        }
    }

    public bool HasPending
    {
        get => _hasPending;
        private set
        {
            if (_hasPending == value)
            {
                return;
            }
            _hasPending = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPending)));
        }
    }

    public void Begin() => _dispatcher.Run(() =>
    {
        _byPath.Clear();
        Modules.Clear();
        IsInitializing = true;
        HasPending = true;
    });

    public void Add(string path, string name) => _dispatcher.Run(() =>
    {
        if (!IsInitializing || _byPath.ContainsKey(path))
        {
            return;
        }
        var entry = new ModuleInitEntry(name, ModuleInitStage.Handshake);
        _byPath[path] = entry;
        Modules.Add(entry);
    });

    public void Advance(string path, ModuleInitStage stage) => _dispatcher.Run(() =>
    {
        if (_byPath.TryGetValue(path, out var entry))
        {
            entry.Stage = stage;
        }
    });

    public void Resolve(string path) => _dispatcher.Run(() =>
    {
        if (!_byPath.Remove(path, out var entry))
        {
            return;
        }
        Modules.Remove(entry);
        if (Modules.Count == 0)
        {
            IsInitializing = false;
            HasPending = false;
        }
        else
        {
            HasPending = Modules.Any(m => m.Stage != ModuleInitStage.TimedOut);
        }
    });

    public void Finish() => _dispatcher.Run(() =>
    {
        _byPath.Clear();
        Modules.Clear();
        IsInitializing = false;
        HasPending = false;
    });

    public void TimeOutPending() => _dispatcher.Run(() =>
    {
        foreach (var entry in Modules)
        {
            if (entry.Stage != ModuleInitStage.TimedOut)
            {
                entry.Stage = ModuleInitStage.TimedOut;
            }
        }
        HasPending = false;
    });
}
