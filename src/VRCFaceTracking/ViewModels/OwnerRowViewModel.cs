using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Helpers;
using VRCFaceTracking.Strings;

namespace VRCFaceTracking.ViewModels;

internal sealed class OwnerRowViewModel : ObservableObject
{
    private static readonly PropertyChangedEventArgs OwnerArgs = new(nameof(Owner));
    private static readonly PropertyChangedEventArgs NoteArgs = new(nameof(Note));
    private static readonly PropertyChangedEventArgs HasNoteArgs = new(nameof(HasNote));
    private static readonly PropertyChangedEventArgs HasOwnerArgs = new(nameof(HasOwner));

    private string? _lastOwner;
    private string[] _lastClaimants = [];

    public OwnerRowViewModel(string label)
    {
        Label = label;
        Owner = Resources.HomeOwner_None;
    }

    public string Label { get; }

    public string Owner { get; private set; }

    public string Note { get; private set; } = string.Empty;

    public bool HasNote => Note.Length > 0;

    public bool HasOwner { get; private set; }

    public void Update(CapabilityOwnership ownership)
    {
        if (string.Equals(ownership.Owner, _lastOwner, StringComparison.Ordinal) && SameClaimants(ownership.Claimants))
        {
            return;
        }

        _lastOwner = ownership.Owner;
        _lastClaimants = [.. ownership.Claimants];

        bool hasOwner = !string.IsNullOrEmpty(ownership.Owner);
        string owner = hasOwner ? ownership.Owner! : Resources.HomeOwner_None;
        string note = BuildNote(ownership);

        if (hasOwner != HasOwner)
        {
            HasOwner = hasOwner;
            OnPropertyChanged(HasOwnerArgs);
        }

        if (!string.Equals(owner, Owner, StringComparison.Ordinal))
        {
            Owner = owner;
            OnPropertyChanged(OwnerArgs);
        }

        if (!string.Equals(note, Note, StringComparison.Ordinal))
        {
            Note = note;
            OnPropertyChanged(NoteArgs);
            OnPropertyChanged(HasNoteArgs);
        }
    }

    private static string BuildNote(CapabilityOwnership ownership)
    {
        if (!ownership.HasConflict)
        {
            return string.Empty;
        }

        List<string> others = [];
        foreach (string claimant in ownership.Claimants)
        {
            if (!string.Equals(claimant, ownership.Owner, StringComparison.Ordinal))
            {
                others.Add(claimant);
            }
        }

        return others.Count == 0
            ? string.Empty
            : TrackingText.Format(Resources.HomeOwner_AlsoFrom, string.Join(", ", others));
    }

    private bool SameClaimants(IReadOnlyList<string> claimants)
    {
        if (claimants.Count != _lastClaimants.Length)
        {
            return false;
        }

        for (int i = 0; i < _lastClaimants.Length; i++)
        {
            if (!string.Equals(_lastClaimants[i], claimants[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
