using System.ComponentModel;
using System.Globalization;
using VRCFaceTracking.Core.Params.Data.Mutation;

namespace VRCFaceTracking.ViewModels;

internal sealed class TextRowViewModel : SettingRowViewModel
{
    private static readonly PropertyChangedEventArgs TextArgs = new(nameof(Text));

    private readonly MutationProperty _property;

    public TextRowViewModel(TrackingCardViewModel card, MutationProperty property)
        : base(card, property)
    {
        _property = property;
    }

    public string Text
    {
        get => _property.StringValue;
        set
        {
            if (_property.Value is int)
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number > 0)
                {
                    _property.Value = number;
                }
                else
                {
                    OnPropertyChanged(TextArgs);
                }

                return;
            }

            _property.StringValue = value ?? string.Empty;
        }
    }

    protected override void OnValueChanged() => OnPropertyChanged(TextArgs);
}
