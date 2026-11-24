using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VRCFaceTracking.ViewModels;

internal sealed class SettingSectionViewModel : ObservableObject
{
    private static readonly PropertyChangedEventArgs IsExpandedArgs = new(nameof(IsExpanded));
    private static readonly PropertyChangedEventArgs IsVisibleArgs = new(nameof(IsVisible));
    private static readonly PropertyChangedEventArgs ShowRowsArgs = new(nameof(ShowRows));

    private bool _isExpanded;

    public SettingSectionViewModel(string key, string? title, bool isCollapsible)
    {
        Key = key;
        Title = title ?? string.Empty;
        ShowTitle = title != null && !isCollapsible;
        IsCollapsible = isCollapsible && title != null;
        _isExpanded = !IsCollapsible;
    }

    public string Key { get; }

    public string Title { get; }

    public bool ShowTitle { get; }

    public bool IsCollapsible { get; }

    public List<SettingRowViewModel> Rows { get; } = [];

    public bool IsVisible => Rows.Exists(row => row.IsVisible);

    public bool ShowRows => IsVisible && IsExpanded;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged(IsExpandedArgs);
            OnPropertyChanged(ShowRowsArgs);
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(IsVisibleArgs);
        OnPropertyChanged(ShowRowsArgs);
    }
}
