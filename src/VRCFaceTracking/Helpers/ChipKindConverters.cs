using Avalonia.Data.Converters;
using VRCFaceTracking.ViewModels;

namespace VRCFaceTracking.Helpers;

internal static class ChipKindConverters
{
    public static readonly IValueConverter IsSuccess = new FuncValueConverter<ChipKind, bool>(kind => kind == ChipKind.Success);

    public static readonly IValueConverter IsProgress = new FuncValueConverter<ChipKind, bool>(kind => kind == ChipKind.Progress);
}
