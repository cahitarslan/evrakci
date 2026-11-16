using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Evrakci.App.Services;

/// <summary>Light, dark or following Windows. Document pages stay white in every theme.</summary>
public static class ThemeService
{
    public const string System = "Sistem", Light = "Açık", Dark = "Koyu";

    // Brand colours (Marka\Marka Rehberi.html): seal violet as the accent, solid "file paper" surfaces instead of
    // Mica, so the app looks the same on every Windows wallpaper and accent colour.
    static readonly Color Violet = Color.FromRgb(0x4B, 0x2E, 0x83);
    static readonly Color Lilac = Color.FromRgb(0xB9, 0xA7, 0xEC);
    static bool _hooked;

    public static string Normalize(string? mode) => mode is Light or Dark ? mode : System;

    public static void Apply(string? mode, Window window)
    {
        if (!_hooked)
        {
            // Windows switching light/dark (in "Sistem" mode) must not bring the Windows accent colour back.
            ApplicationThemeManager.Changed += (theme, _) => ApplyBrand(theme);
            _hooked = true;
        }
        mode = Normalize(mode);
        if (mode == System)
        {
            SystemThemeWatcher.Watch(window, WindowBackdropType.None, false);
            ApplicationThemeManager.ApplySystemTheme(false);
        }
        else
        {
            SystemThemeWatcher.UnWatch(window);
            ApplicationThemeManager.Apply(mode == Dark ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.None, false);
        }
        ApplyBrand(ApplicationThemeManager.GetAppTheme());
    }

    static void ApplyBrand(ApplicationTheme theme)
    {
        bool dark = theme == ApplicationTheme.Dark;
        ApplicationAccentColorManager.Apply(dark ? Lilac : Violet, theme);
        var res = Application.Current.Resources;
        void Set(string hex, params string[] keys)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            foreach (var key in keys)
                res[key] = key.EndsWith("Brush") || !key.Contains("Color") ? new SolidColorBrush(color) : color;
        }

        // Surfaces: the desk (window), the sheet (content area) and cards on it. All opaque.
        Set(dark ? "#16131D" : "#EEEEF2", "ApplicationBackgroundBrush", "ApplicationBackgroundColor");
        Set(dark ? "#1D1926" : "#FAFAFC", "LayerFillColorDefaultBrush", "SolidBackgroundFillColorBaseBrush");
        Set(dark ? "#252131" : "#FFFFFF", "CardBackgroundFillColorDefaultBrush", "CardBackground", "LayerFillColorAltBrush");
        Set(dark ? "#2B2638" : "#F7F6FA", "CardBackgroundPointerOver", "CardBackgroundFillColorSecondaryBrush");
        Set(dark ? "#363044" : "#DCDBE3", "CardStrokeColorDefaultBrush", "DividerStrokeColorDefaultBrush", "ControlStrokeColorDefaultBrush");
        Set(dark ? "#2A2537" : "#FFFFFF", "ControlFillColorDefaultBrush", "TextControlBackground");
        Set(dark ? "#312B40" : "#F4F3F8", "ControlFillColorSecondaryBrush", "TextControlBackgroundPointerOver");
        Set(dark ? "#2F2A3D" : "#EDEBF4", "SubtleFillColorSecondaryBrush");

        // Ink: body text, secondary text.
        Set(dark ? "#ECEAF2" : "#1A1A22", "TextFillColorPrimaryBrush", "TextControlForeground");
        Set(dark ? "#A8A4B8" : "#5E6270", "TextFillColorSecondaryBrush", "TextControlPlaceholderForeground");
        Set(dark ? "#B9A7EC" : "#4B2E83", "AccentTextFillColorPrimaryBrush", "AccentTextFillColorSecondaryBrush",
            "AccentFillColorDefaultBrush", "TextControlFocusedBorderBrush");

        // Filled buttons, checked tabs and step markers. Text on them is always white (above 6:1 in both themes).
        string fill = dark ? "#6A4FB3" : "#4B2E83", hover = dark ? "#775DC0" : "#5A3A99", pressed = dark ? "#5B42A0" : "#3B2468";
        Set(fill, "AccentButtonBackground", "ToggleButtonBackgroundChecked");
        Set(hover, "AccentButtonBackgroundPointerOver");
        Set(pressed, "AccentButtonBackgroundPressed", "AccentButtonBorderBrushPressed", "ToggleButtonBackgroundCheckedPressed",
            "ToggleButtonBorderBrushCheckedPressed");
        Set("#FFFFFF", "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed",
            "ToggleButtonForegroundChecked", "ToggleButtonForegroundCheckedPointerOver", "ToggleButtonForegroundCheckedPressed",
            "TextOnAccentFillColorPrimaryBrush", "TextOnAccentFillColorSecondaryBrush");
        // Step markers use AccentFillColorDefaultBrush with white text on top: on dark that must be the button fill, not lilac.
        if (dark) Set(fill, "AccentFillColorDefaultBrush");

        // WPF-UI paints window backgrounds while it loads the theme, before the colours above exist.
        foreach (Window w in Application.Current.Windows) UseBrandBackground(w);
    }

    /// <summary>Points a window's background at the brand's desk colour (also for dialogs opened later).</summary>
    public static void UseBrandBackground(Window w) => w.SetResourceReference(Window.BackgroundProperty, "ApplicationBackgroundBrush");
}
