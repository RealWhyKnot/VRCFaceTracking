using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace VRCFaceTracking.Tests;

public sealed class ThemeResourceTests
{
    private static readonly string[] Keys =
    [
        "SystemFillColorCautionBrush",
        "SystemFillColorCriticalBrush",
        "SystemFillColorAttentionBrush",
        "TextFillColorTertiaryBrush",
        "ControlFillColorDefaultBrush",
        "ControlStrokeColorDefaultBrush",
        "ControlFillColorSecondaryBrush",
    ];

    public static TheoryData<string> SemanticBrushKeys
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string key in Keys)
            {
                data.Add(key);
            }

            return data;
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(SemanticBrushKeys))]
    public void SemanticBrushResolvesInBothThemeVariants(string key)
    {
        Application? app = Application.Current;
        Assert.NotNull(app);

        foreach (ThemeVariant? variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Assert.True(
                app.TryFindResource(key, variant, out object? value),
                $"'{key}' did not resolve for the {variant} theme variant.");
            Assert.IsAssignableFrom<IBrush>(value);
        }
    }

    [AvaloniaFact]
    public void SemanticBrushesDifferBetweenThemeVariants()
    {
        Application? app = Application.Current;
        Assert.NotNull(app);

        int differing = 0;
        foreach (string key in Keys)
        {
            app.TryFindResource(key, ThemeVariant.Light, out object? light);
            app.TryFindResource(key, ThemeVariant.Dark, out object? dark);
            if (!string.Equals(Describe(light), Describe(dark), StringComparison.Ordinal))
            {
                differing++;
            }
        }

        Assert.True(differing > 0, "No semantic brush changed between the light and dark variants.");
    }

    private static string Describe(object? brush) =>
        brush is ISolidColorBrush solid ? solid.Color.ToString() : brush?.ToString() ?? "null";

    [AvaloniaTheory]
    [InlineData("Shared")]
    [InlineData("Motion")]
    public void StyleSheetParses(string name)
    {
        object loaded = AvaloniaXamlLoader.Load(new Uri($"avares://VRCFaceTracking/Styles/{name}.axaml"));
        Assert.IsAssignableFrom<Styles>(loaded);
    }
}
