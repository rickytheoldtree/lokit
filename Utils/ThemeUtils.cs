using System.Windows;
using System.Windows.Interop;
using LocalizationTranslationWPF.Data;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using static LocalizationTranslationWPF.Data.Config;

namespace LocalizationTranslationWPF.Utils;

public static class ThemeUtils
{
    public static void Apply(AppThemeMode mode)
    {
        var theme = mode switch
        {
            AppThemeMode.Dark => ApplicationTheme.Dark,
            AppThemeMode.Light => ApplicationTheme.Light,
            _ => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark
                ? ApplicationTheme.Dark
                : ApplicationTheme.Light,
        };
        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, true);
    }

    public static void Attach(Window window, AppThemeMode mode)
    {
        SystemThemeWatcher.UnWatch(window);
        Apply(mode);
        if (mode == AppThemeMode.System)
            SystemThemeWatcher.Watch(window, WindowBackdropType.Mica, true);
    }

    public static void Detach(Window window)
    {
        if (!window.IsLoaded || new WindowInteropHelper(window).Handle == nint.Zero) return;
        SystemThemeWatcher.UnWatch(window);
    }
}
