using System.Globalization;
using Avalonia.Data.Converters;
using VRCFaceTracking.Core.Library;

namespace VRCFaceTracking.Helpers;

public class ModuleInitStageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ModuleInitStage.Handshake => Strings.Resources.ModuleInitStageHandshake,
        ModuleInitStage.Capabilities => Strings.Resources.ModuleInitStageCapabilities,
        ModuleInitStage.Initializing => Strings.Resources.ModuleInitStageInitializing,
        ModuleInitStage.TimedOut => Strings.Resources.ModuleInitStageTimedOut,
        _ => string.Empty,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
