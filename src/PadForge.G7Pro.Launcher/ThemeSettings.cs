using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PadForge.G7Pro.Launcher;

internal enum ThemePreference
{
    System,
    Light,
    Dark,
}

internal static class ThemeSettings
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PadForgeG7ProLauncher",
        "settings.json");

    public static ThemePreference Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return ThemePreference.System;
            SettingsFile? settings = ReadSettings();
            return Enum.TryParse(settings?.Theme, ignoreCase: true, out ThemePreference preference)
                ? preference
                : ThemePreference.System;
        }
        catch
        {
            return ThemePreference.System;
        }
    }

    public static void Save(ThemePreference preference)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        SettingsFile current = ReadSettings() ?? new SettingsFile(null, null);
        string json = JsonSerializer.Serialize(current with { Theme = preference.ToString() }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    public static string? LoadPadForgePath()
    {
        try
        {
            string? path = ReadSettings()?.PadForgePath;
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    public static void SavePadForgePath(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        SettingsFile current = ReadSettings() ?? new SettingsFile(null, null);
        string json = JsonSerializer.Serialize(current with { PadForgePath = Path.GetFullPath(path) }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    public static bool IsDark(ThemePreference preference) => preference switch
    {
        ThemePreference.Dark => true,
        ThemePreference.Light => false,
        _ => WindowsUsesDarkMode(),
    };

    public static void ApplyDarkTitleBar(Form form, bool dark)
    {
        if (!form.IsHandleCreated) return;
        int enabled = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int));
    }

    private static bool WindowsUsesDarkMode()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private static SettingsFile? ReadSettings() => File.Exists(SettingsPath)
        ? JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(SettingsPath))
        : null;

    private sealed record SettingsFile(string? Theme, string? PadForgePath);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}
