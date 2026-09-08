using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace KsitalTelemetryHub.UI.Desktop;

public static class ThemeManager
{
    public static void ApplyAutoTheme()
    {
        bool isDark = IsWindowsInDarkMode();
        var res = Application.Current.Resources;

        if (isDark)
        {
            res["BgMain"] = new SolidColorBrush(Color.FromRgb(30, 30, 46));
            res["BgCard"] = new SolidColorBrush(Color.FromRgb(36, 39, 58));
            res["BgHeader"] = new SolidColorBrush(Color.FromRgb(43, 43, 61));
            res["BgInner"] = new SolidColorBrush(Color.FromRgb(24, 24, 37));
            res["BorderColor"] = new SolidColorBrush(Color.FromRgb(49, 50, 68));
            res["TextMain"] = new SolidColorBrush(Color.FromRgb(236, 239, 244));
            res["TextMuted"] = new SolidColorBrush(Color.FromRgb(166, 173, 200));
        }
        else
        {
            res["BgMain"] = new SolidColorBrush(Color.FromRgb(245, 246, 250));
            res["BgCard"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            res["BgHeader"] = new SolidColorBrush(Color.FromRgb(230, 233, 240));
            res["BgInner"] = new SolidColorBrush(Color.FromRgb(238, 240, 245));
            res["BorderColor"] = new SolidColorBrush(Color.FromRgb(210, 215, 225));
            res["TextMain"] = new SolidColorBrush(Color.FromRgb(33, 37, 41));
            res["TextMuted"] = new SolidColorBrush(Color.FromRgb(108, 117, 125));
        }
    }

    private static bool IsWindowsInDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            return val is int i && i == 0;
        }
        catch
        {
            return true;
        }
    }
}