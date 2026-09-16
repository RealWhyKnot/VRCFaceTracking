using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;

namespace VRCFaceTracking.UiTests;

public class ThemeResourceTests
{
    private static readonly string[] Keys =
    [
        "SystemFillColorCautionBrush",
        "SystemFillColorCriticalBrush",
        "SystemFillColorAttentionBrush",
        "TextFillColorTertiaryBrush",
        "ControlFillColorDefaultBrush",
        "ControlStrokeColorDefaultBrush",
        "ControlFillColorSecondaryBrush"
    ];

    public static TheoryData<string> SemanticBrushKeys
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var key in Keys)
            {
                data.Add(key);
            }

            return data;
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(SemanticBrushKeys))]
    public void SemanticBrush_ResolvesInBothThemeVariants(string key)
    {
        var app = Application.Current;
        Assert.NotNull(app);

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Assert.True(
                app.TryFindResource(key, variant, out var value),
                $"'{key}' did not resolve for the {variant} theme variant.");
            Assert.IsAssignableFrom<IBrush>(value);
        }
    }

    [AvaloniaFact]
    public void SemanticBrushes_DifferBetweenThemeVariants()
    {
        var app = Application.Current;
        Assert.NotNull(app);

        var differing = 0;
        foreach (var key in Keys)
        {
            app.TryFindResource(key, ThemeVariant.Light, out var light);
            app.TryFindResource(key, ThemeVariant.Dark, out var dark);
            if (Describe(light) != Describe(dark))
            {
                differing++;
            }
        }

        Assert.True(differing > 0, "No semantic brush changed between the light and dark variants.");
    }

    private static string Describe(object? brush) =>
        brush is ISolidColorBrush solid ? solid.Color.ToString() : brush?.ToString() ?? "null";
}
