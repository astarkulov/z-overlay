using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ApexHUD.Core.Settings;

public sealed class WidgetLayout
{
    public bool Enabled { get; set; } = true;
    public bool HideWhenNotDriving { get; set; } = false;
    public double Left { get; set; }
    public double Top { get; set; }
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 1.0;
}

public sealed class AppSettings
{
    // Core mode
    public bool DemoMode { get; set; } = true;
    public bool Locked { get; set; } = false;
    public bool AutoHideOutOfSim { get; set; } = false;
    public bool ShowOnlyWhenIRacingFocused { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = true;

    // Appearance & Typography
    public string UiSkin { get; set; } = "IRacingNative"; // IRacingNative | ApexCarbon
    public double GlobalScale { get; set; } = DefaultGlobalScale();
    public double HudFontScale { get; set; } = 1.05;      // 0.80 .. 1.35 internal font size multiplier
    public double BackgroundOpacity { get; set; } = 0.82;
    public string ThemePreset { get; set; } = "IRacingNative";
    public string UiFontFamily { get; set; } = "Segoe UI Variable Text";
    public string MonoFontFamily { get; set; } = "Cascadia Mono";

    // Units & Fuel Strategy
    public bool UseImperialUnits { get; set; } = false;
    public double FuelSafetyMarginLaps { get; set; } = 0.5;
    public string FuelCalcMode { get; set; } = "Avg"; // Avg | Last | Max

    // Relative toggles
    public bool ShowCarBrandIcons { get; set; } = true;
    public int RelativeCarsEachSide { get; set; } = 4;
    public bool ShowRelativeHeader { get; set; } = true;
    public bool ShowRelativeCarNumber { get; set; } = true;
    public bool ShowRelativeIRating { get; set; } = true;
    public bool ShowRelativeLicense { get; set; } = true;
    public bool ShowRelativePaceDelta { get; set; } = true;

    // Standings toggles
    public int StandingsMaxRows { get; set; } = 10;
    public bool ShowStandingsHeader { get; set; } = true;
    public bool ShowStandingsCarNumber { get; set; } = true;
    public bool ShowStandingsPosDelta { get; set; } = true;
    public bool ShowStandingsTireCompound { get; set; } = true;
    public bool ShowStandingsIRating { get; set; } = true;
    public bool ShowStandingsDeltaIR { get; set; } = true;
    public bool ShowStandingsInterval { get; set; } = false;
    public bool ShowStandingsLastLap { get; set; } = true;
    public bool ShowStandingsPits { get; set; } = true;

    // Inputs — 100% modular (every block can be toggled off)
    public bool ShowInputsShiftBar { get; set; } = true;
    public bool ShowInputsGraph { get; set; } = true;
    public double InputsGraphSeconds { get; set; } = 3.5; // 1.0 .. 12.0 seconds
    public double InputsGraphWidth { get; set; } = 240.0; // 140 .. 460 px
    public bool ShowInputsPedalBars { get; set; } = true;
    public bool ShowInputsGearCluster { get; set; } = true;
    public bool ShowInputsSteering { get; set; } = true;
    public bool ShowInputsClutch { get; set; } = true;
    public bool ShowInputsElectronics { get; set; } = true;
    public bool ShowInputsDeltaBar { get; set; } = true;
    public bool ShowInputsCornerSpeed { get; set; } = true; // Racelab 4-corner min speed vs fastest

    public bool RadarAutoHideWhenClear { get; set; } = true;

    // Allowed monitors by DeviceName (empty = Primary screen only)
    public List<string> AllowedMonitorDevices { get; set; } = new();

    public Dictionary<string, WidgetLayout> Widgets { get; set; } = CreateDefaultWidgets();

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ApexHUD", "settings.json");

    private static double DefaultGlobalScale()
    {
        double screenH = System.Windows.SystemParameters.PrimaryScreenHeight;
        if (screenH >= 2000) return 1.35;
        if (screenH >= 1350) return 1.15;
        return 1.0;
    }

    public static Dictionary<string, WidgetLayout> CreateDefaultWidgets()
    {
        double screenW = System.Windows.SystemParameters.PrimaryScreenWidth;
        double screenH = System.Windows.SystemParameters.PrimaryScreenHeight;
        double gs = DefaultGlobalScale();

        return new Dictionary<string, WidgetLayout>
        {
            ["Relative"] = new()
            {
                Enabled = true,
                Left = Math.Max(24, screenW - 440 * gs - 28),
                Top = Math.Max(24, screenH - 290 * gs - 36),
                Scale = 1.0,
                Opacity = 1.0
            },
            ["Standings"] = new()
            {
                Enabled = true,
                Left = 24,
                Top = 28,
                Scale = 1.0,
                Opacity = 1.0
            },
            ["Inputs"] = new()
            {
                Enabled = true,
                HideWhenNotDriving = true,
                Left = Math.Max(24, (screenW - 480 * gs) / 2.0),
                Top = Math.Max(24, screenH - 180 * gs - 36),
                Scale = 1.0,
                Opacity = 1.0
            },
            ["Fuel"] = new()
            {
                Enabled = true,
                Left = 24,
                Top = Math.Max(24, screenH - 220 * gs - 36),
                Scale = 1.0,
                Opacity = 1.0
            },
            ["Radar"] = new()
            {
                Enabled = true,
                Left = Math.Max(24, (screenW - 185 * gs) / 2.0),
                Top = Math.Max(24, (screenH - 185 * gs) / 2.0 + 30),
                Scale = 1.0,
                Opacity = 1.0
            }
        };
    }

    public WidgetLayout GetWidget(string key)
    {
        if (!Widgets.TryGetValue(key, out var layout))
        {
            var defaults = CreateDefaultWidgets();
            layout = defaults.TryGetValue(key, out var def) ? def : new WidgetLayout { Left = 60, Top = 60 };
            Widgets[key] = layout;
        }
        return layout;
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    if (string.IsNullOrWhiteSpace(loaded.UiSkin)) loaded.UiSkin = "IRacingNative";
                    if (loaded.GlobalScale < 0.6) loaded.GlobalScale = DefaultGlobalScale();
                    if (loaded.HudFontScale < 0.75 || loaded.HudFontScale > 1.4) loaded.HudFontScale = 1.05;
                    if (loaded.BackgroundOpacity <= 0.0 && !json.Contains("BackgroundOpacity"))
                        loaded.BackgroundOpacity = 0.62;
                    if (!json.Contains("ShowOnlyWhenIRacingFocused")) loaded.ShowOnlyWhenIRacingFocused = true;
                    if (!json.Contains("AutoCheckUpdates")) loaded.AutoCheckUpdates = true;
                    if (!json.Contains("ShowCarBrandIcons")) loaded.ShowCarBrandIcons = true;
                    if (!json.Contains("ShowRelativeHeader")) loaded.ShowRelativeHeader = true;
                    if (!json.Contains("ShowRelativeCarNumber")) loaded.ShowRelativeCarNumber = true;
                    if (!json.Contains("ShowRelativePaceDelta")) loaded.ShowRelativePaceDelta = true;
                    if (!json.Contains("ShowStandingsHeader")) loaded.ShowStandingsHeader = true;
                    if (!json.Contains("ShowStandingsCarNumber")) loaded.ShowStandingsCarNumber = true;
                    if (!json.Contains("ShowStandingsIRating")) loaded.ShowStandingsIRating = true;
                    if (!json.Contains("ShowStandingsPosDelta")) loaded.ShowStandingsPosDelta = true;
                    if (!json.Contains("ShowStandingsTireCompound")) loaded.ShowStandingsTireCompound = true;
                    if (!json.Contains("ShowInputsGraph")) loaded.ShowInputsGraph = true;
                    if (!json.Contains("ShowInputsPedalBars")) loaded.ShowInputsPedalBars = true;
                    if (!json.Contains("ShowInputsGearCluster")) loaded.ShowInputsGearCluster = true;
                    if (!json.Contains("ShowInputsElectronics")) loaded.ShowInputsElectronics = true;
                    if (!json.Contains("ShowInputsDeltaBar")) loaded.ShowInputsDeltaBar = true;
                    if (!json.Contains("ShowInputsCornerSpeed")) loaded.ShowInputsCornerSpeed = true;
                    if (string.IsNullOrWhiteSpace(loaded.UiFontFamily)) loaded.UiFontFamily = "Segoe UI Variable Text";
                    if (string.IsNullOrWhiteSpace(loaded.MonoFontFamily)) loaded.MonoFontFamily = "Cascadia Mono";
                    if (string.IsNullOrWhiteSpace(loaded.FuelCalcMode)) loaded.FuelCalcMode = "Avg";
                    if (loaded.StandingsMaxRows < 4) loaded.StandingsMaxRows = 10;
                    if (loaded.RelativeCarsEachSide < 1) loaded.RelativeCarsEachSide = 4;
                    if (loaded.InputsGraphSeconds < 1.0 || loaded.InputsGraphSeconds > 12.0) loaded.InputsGraphSeconds = 3.5;
                    if (loaded.InputsGraphWidth < 140.0 || loaded.InputsGraphWidth > 460.0) loaded.InputsGraphWidth = 240.0;

                    var defaults = CreateDefaultWidgets();
                    foreach (var kv in defaults)
                    {
                        if (!loaded.Widgets.ContainsKey(kv.Key))
                            loaded.Widgets[kv.Key] = kv.Value;
                    }
                    if (!json.Contains("HideWhenNotDriving") && loaded.Widgets.TryGetValue("Inputs", out var inpLayout))
                        inpLayout.HideWhenNotDriving = true;
                    return loaded;
                }
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }
}
