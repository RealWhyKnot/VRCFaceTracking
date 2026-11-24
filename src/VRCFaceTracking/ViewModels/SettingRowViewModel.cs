using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Helpers;

namespace VRCFaceTracking.ViewModels;

internal abstract class SettingRowViewModel : ObservableObject
{
    private static readonly PropertyChangedEventArgs IsEnabledArgs = new(nameof(IsEnabled));
    private static readonly PropertyChangedEventArgs IsVisibleArgs = new(nameof(IsVisible));

    protected SettingRowViewModel(TrackingCardViewModel card, IMutationComponent component)
    {
        Card = card;
        Component = component;
        Metadata = component.Metadata;
        Unit = Metadata?.Owner;
        string type = Unit?.GetType().Name ?? "Unknown";
        string key = Metadata?.Key ?? component.Name;
        TextKey = $"Tracking_{type}_{key}";
        Label = TrackingText.Get(TextKey, component.Name);
        Tip = TrackingText.Find(TextKey + "_Tip");
        if (component is INotifyPropertyChanged source)
        {
            source.PropertyChanged += OnComponentChanged;
        }
    }

    public TrackingCardViewModel Card { get; }

    public IMutationComponent Component { get; }

    public MutationMetadata? Metadata { get; }

    public TrackingMutation? Unit { get; }

    public string TextKey { get; }

    public string Label { get; }

    public string? Tip { get; }

    public bool IsDeveloper => Metadata?.Developer == true;

    public bool IsAdvanced => Metadata?.Advanced == true;

    public string? DependsOn => Metadata?.DependsOn;

    public ToggleRowViewModel? Parent { get; set; }

    public bool IsVisible => !IsDeveloper || Card.Page.Developer.Enabled;

    public bool UnitOn => Unit?.IsCardOn(Card.Card) ?? true;

    public bool IsIndented => Parent != null && Parent.Section != null && ReferenceEquals(Parent.Section, Section);

    public SettingSectionViewModel? Section { get; set; }

    public virtual bool IsEnabled => Card.Page.MasterOn && Card.IsOn && UnitOn && (Parent == null || Parent.IsChecked);

    public virtual void Refresh()
    {
        OnPropertyChanged(IsEnabledArgs);
        OnPropertyChanged(IsVisibleArgs);
    }

    protected abstract void OnValueChanged();

    public void Detach()
    {
        if (Component is INotifyPropertyChanged source)
        {
            source.PropertyChanged -= OnComponentChanged;
        }
    }

    private void OnComponentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            OnValueChanged();
        }
        else
        {
            Dispatcher.UIThread.Post(OnValueChanged);
        }
    }
}
