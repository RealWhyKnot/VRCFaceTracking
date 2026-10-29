using System.Collections.ObjectModel;
using System.ComponentModel;
using VRCFaceTracking.Core.Contracts.Services;

namespace VRCFaceTracking.Core.Library;

public sealed class ModuleInitProgress : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly IDispatcherService _dispatcher;
    private readonly Dictionary<string, ModuleInitEntry> _byPath = [];

    public ModuleInitProgress(IDispatcherService dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public ObservableCollection<ModuleInitEntry> Modules { get; } = [];

    public bool IsInitializing
    {
        get;
        private set
        {
            if (field == value)
            {
                return;
            }
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInitializing)));
        }
    }

    public bool HasPending
    {
        get;
        private set
        {
            if (field == value)
            {
                return;
            }
            field = value;
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
        if (_byPath.TryGetValue(path, out ModuleInitEntry? entry))
        {
            entry.Stage = stage;
        }
    });

    public void Resolve(string path) => _dispatcher.Run(() =>
    {
        if (!_byPath.Remove(path, out ModuleInitEntry? entry))
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
        foreach (ModuleInitEntry entry in Modules)
        {
            if (entry.Stage != ModuleInitStage.TimedOut)
            {
                entry.Stage = ModuleInitStage.TimedOut;
            }
        }
        HasPending = false;
    });
}
