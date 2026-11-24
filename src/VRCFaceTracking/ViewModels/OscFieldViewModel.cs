using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VRCFaceTracking.ViewModels;

internal sealed class OscFieldViewModel : ObservableObject
{
    private static readonly PropertyChangedEventArgs TextArgs = new(nameof(Text));
    private static readonly PropertyChangedEventArgs ErrorArgs = new(nameof(Error));
    private static readonly PropertyChangedEventArgs HasErrorArgs = new(nameof(HasError));

    private readonly Func<string> _read;
    private readonly Func<string, string?> _apply;
    private string _text;

    public OscFieldViewModel(Func<string> read, Func<string, string?> apply)
    {
        _read = read;
        _apply = apply;
        _text = read();
    }

    public string Text
    {
        get => _text;
        set
        {
            string next = value ?? string.Empty;
            if (string.Equals(next, _text, StringComparison.Ordinal) && !HasError)
            {
                return;
            }

            _text = next;
            OnPropertyChanged(TextArgs);
            SetError(_apply(next));
        }
    }

    public string? Error { get; private set; }

    public bool HasError => Error != null;

    public void Sync()
    {
        if (HasError)
        {
            return;
        }

        string current = _read();
        if (!string.Equals(current, _text, StringComparison.Ordinal))
        {
            _text = current;
            OnPropertyChanged(TextArgs);
        }
    }

    public void Reload()
    {
        _text = _read();
        OnPropertyChanged(TextArgs);
        SetError(error: null);
    }

    private void SetError(string? error)
    {
        bool hadError = HasError;
        if (string.Equals(error, Error, StringComparison.Ordinal))
        {
            return;
        }

        Error = error;
        OnPropertyChanged(ErrorArgs);
        if (hadError != HasError)
        {
            OnPropertyChanged(HasErrorArgs);
        }
    }
}
