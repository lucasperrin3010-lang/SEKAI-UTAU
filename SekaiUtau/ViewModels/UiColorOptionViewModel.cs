using System;
using Avalonia.Media;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;

namespace SekaiUtau.App.ViewModels;

/// <summary>
/// One interface colour, editable either with the colour picker or by typing a hex code
/// such as <c>#8CC63F</c>. Both stay in sync, and every change is persisted immediately.
/// </summary>
public partial class UiColorOptionViewModel : ViewModelBase {
    public string Name { get; }

    private readonly Func<Color> themeColor;
    private readonly Action<string> onChanged;
    private bool syncing;

    [Reactive] public partial Color Color { get; set; }
    [Reactive] public partial string Hex { get; set; }

    public UiColorOptionViewModel(string name, Color color, Func<Color> themeColor, Action<string> onChanged) {
        Name = name;
        this.themeColor = themeColor;
        this.onChanged = onChanged;
        Color = color;
        Hex = ToHex(color);

        this.WhenAnyValue(vm => vm.Color)
            .Skip(1)
            .Subscribe(c => {
                if (syncing) {
                    return;
                }
                syncing = true;
                Hex = ToHex(c);
                syncing = false;
                onChanged(ToHex(c));
            });

        this.WhenAnyValue(vm => vm.Hex)
            .Skip(1)
            .Subscribe(text => {
                if (syncing || !Color.TryParse((text ?? string.Empty).Trim(), out var parsed)) {
                    return;
                }
                syncing = true;
                Color = parsed;
                syncing = false;
                onChanged(ToHex(parsed));
            });
    }

    /// <summary>Back to the active theme's colour, without touching the saved preferences.</summary>
    public void Reset() => SetColorSilently(themeColor());

    public void SetColorSilently(Color color) {
        syncing = true;
        Color = color;
        Hex = ToHex(color);
        syncing = false;
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
