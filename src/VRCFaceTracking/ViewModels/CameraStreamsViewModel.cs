using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Types;
using VRCFaceTracking.Strings;

namespace VRCFaceTracking.ViewModels;

internal sealed partial class CameraStreamsViewModel : ObservableObject
{
    private static readonly PropertyChangedEventArgs HasCamerasArgs = new(nameof(HasCameras));

    private readonly Func<Guid, CameraControlCommand, bool> _send;
    private readonly TimeProvider _time;
    private readonly CameraStreamViewModel.Decoding? _decoding;
    private readonly Dictionary<string, long> _expired = [];
    private int _version;
    private bool _hasVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BlurButtonText))]
    private bool _isBlurred = true;

    [ObservableProperty] private bool _showGuides;

    [ObservableProperty] private bool _isFrozen;

    public CameraStreamsViewModel(Func<Guid, CameraControlCommand, bool>? send = null, TimeProvider? time = null, CameraStreamViewModel.Decoding? decoding = null)
    {
        _send = send ?? ((_, _) => false);
        _time = time ?? TimeProvider.System;
        _decoding = decoding;
    }

    public ObservableCollection<CameraStreamViewModel> Streams { get; } = [];

    public bool HasCameras => Streams.Count > 0;

    public string BlurButtonText => IsBlurred ? Resources.Camera_Show : Resources.Camera_Blur;

    public void Refresh(CameraStreamRegistry registry)
    {
        int version = registry.Version;
        if (_hasVersion && version == _version)
        {
            Tick();
            return;
        }

        IReadOnlyList<CameraStreamSnapshot> snapshots = registry.Snapshot();
        Apply(snapshots);
        _version = version;
        _hasVersion = true;
    }

    public void Refresh(IReadOnlyList<CameraStreamSnapshot> snapshots)
    {
        _hasVersion = false;
        Apply(snapshots);
    }

    public void Reset()
    {
        IsBlurred = true;
        IsFrozen = false;
        foreach (CameraStreamViewModel stream in Streams)
        {
            stream.Clear();
        }

        Streams.Clear();
        _expired.Clear();
        _hasVersion = false;
        OnPropertyChanged(HasCamerasArgs);
    }

    partial void OnIsBlurredChanged(bool value)
    {
        foreach (CameraStreamViewModel stream in Streams)
        {
            stream.IsBlurred = value;
        }
    }

    partial void OnShowGuidesChanged(bool value)
    {
        foreach (CameraStreamViewModel stream in Streams)
        {
            stream.ShowGuides = value;
        }
    }

    private void Apply(IReadOnlyList<CameraStreamSnapshot> snapshots)
    {
        bool had = HasCameras;
        HashSet<string> live = [];
        foreach (CameraStreamSnapshot snapshot in snapshots.Take(CameraStreamRegistry.MaxStreams))
        {
            string key = CameraStreamViewModel.KeyOf(snapshot.ModuleId, snapshot.StreamId);
            live.Add(key);
            if (_expired.TryGetValue(key, out long dead) && snapshot.Sequence <= dead)
            {
                continue;
            }

            CameraStreamViewModel? stream = Streams.FirstOrDefault(s => s.Matches(snapshot));
            if (stream == null)
            {
                if (Streams.Count >= CameraStreamRegistry.MaxStreams)
                {
                    continue;
                }

                stream = new CameraStreamViewModel(snapshot, _send, _time, _decoding) { IsBlurred = IsBlurred, ShowGuides = ShowGuides };
                Streams.Add(stream);
            }

            if (!stream.Update(snapshot, IsFrozen))
            {
                _expired[key] = Math.Max(snapshot.Sequence, stream.LastSequence);
                Streams.Remove(stream);
            }
        }

        RemoveMissing(live);
        if (had != HasCameras)
        {
            OnPropertyChanged(HasCamerasArgs);
        }
    }

    private void Tick()
    {
        bool had = HasCameras;
        for (int i = Streams.Count - 1; i >= 0; i--)
        {
            CameraStreamViewModel stream = Streams[i];
            if (!stream.Tick(IsFrozen))
            {
                _expired[CameraStreamViewModel.KeyOf(stream.ModuleId, stream.StreamId)] = stream.LastSequence;
                Streams.RemoveAt(i);
            }
        }

        if (had != HasCameras)
        {
            OnPropertyChanged(HasCamerasArgs);
        }
    }

    private void RemoveMissing(HashSet<string> live)
    {
        for (int i = Streams.Count - 1; i >= 0; i--)
        {
            CameraStreamViewModel stream = Streams[i];
            if (!live.Contains(CameraStreamViewModel.KeyOf(stream.ModuleId, stream.StreamId)))
            {
                stream.Clear();
                Streams.RemoveAt(i);
            }
        }

        foreach (string key in _expired.Keys.Where(k => !live.Contains(k)).ToList())
        {
            _expired.Remove(key);
        }
    }

    [RelayCommand]
    private void ToggleBlur() => IsBlurred = !IsBlurred;
}
