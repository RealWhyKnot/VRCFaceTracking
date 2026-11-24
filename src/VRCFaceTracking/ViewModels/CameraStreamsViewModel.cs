using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCFaceTracking.Strings;

namespace VRCFaceTracking.ViewModels;

internal sealed partial class CameraStreamsViewModel : ObservableObject
{
    private static readonly PropertyChangedEventArgs HasCamerasArgs = new(nameof(HasCameras));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BlurButtonText))]
    private bool _isBlurred = true;

    [ObservableProperty] private bool _showGuides;

    [ObservableProperty] private bool _isFrozen;

    partial void OnIsBlurredChanged(bool value)
    {
        Eye.IsBlurred = value;
        Lip.IsBlurred = value;
    }

    partial void OnShowGuidesChanged(bool value)
    {
        Eye.ShowGuides = value;
        Lip.ShowGuides = value;
    }

    public CameraStreamsViewModel(TimeProvider? time = null)
    {
        TimeProvider provider = time ?? TimeProvider.System;
        Eye = new CameraStreamViewModel(Resources.CameraStreamsEye, provider);
        Lip = new CameraStreamViewModel(Resources.CameraStreamsLip, provider);
    }

    public CameraStreamViewModel Eye { get; }

    public CameraStreamViewModel Lip { get; }

    public bool HasCameras { get; private set; }

    public string BlurButtonText => IsBlurred ? Resources.Camera_Show : Resources.Camera_Blur;

    public void Refresh(Core.Types.Image? eye, Core.Types.Image? lip)
    {
        bool hasEye = Eye.Update(eye, IsFrozen);
        bool hasLip = Lip.Update(lip, IsFrozen);
        bool has = hasEye || hasLip;
        if (has != HasCameras)
        {
            HasCameras = has;
            OnPropertyChanged(HasCamerasArgs);
        }
    }

    public void Reset()
    {
        IsBlurred = true;
        IsFrozen = false;
        Eye.Clear();
        Lip.Clear();
        if (HasCameras)
        {
            HasCameras = false;
            OnPropertyChanged(HasCamerasArgs);
        }
    }

    [RelayCommand]
    private void ToggleBlur() => IsBlurred = !IsBlurred;
}
