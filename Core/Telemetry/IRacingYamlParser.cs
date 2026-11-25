using System;
using System.Collections.Generic;
using System.Globalization;

namespace ApexHUD.Core.Telemetry;

public sealed class YamlDriverMeta
{
    public int CarIdx { get; set; } = -1;
    public string UserName { get; set; } = "";
    public string AbbrevName { get; set; } = "";
    public string CarNumber { get; set; } = "";
    public string CarScreenName { get; set; } = "";
    public string CarPath { get; set; } = "";
    public CarBrand Brand { get; set; } = CarBrand.Unknown;
    public int CarIsPaceCar { get; set; }
    public int IsSpectator { get; set; }
    public int CarClassId { get; set; }
    public string CarClassShortName { get; set; } = "CLASS";
    public string CarClassColorHex { get; set; } = "#F5A623";
    public float CarClassEstLapTime { get; set; } = 100f;
    public int IRating { get; set; } = 1350;
    public string LicString { get; set; } = "R 2.50";
    public string LicColorHex { get; set; } = "#EF4444";
}

public sealed class YamlSessionMeta
{
    public string TrackDisplayName { get; set; } = "iRacing";
    public float TrackLengthMeters { get; set; } = 5000f;
    public int IncidentLimit { get; set; } = 17;
    public int DriverCarIdx { get; set; } = -1;
    public float DriverCarFuelMaxLtr { get; set; } = 100f;
    public float DriverCarSLFirstRpm { get; set; } = 6500f;
    public float DriverCarSLShiftRpm { get; set; } = 7800f;
    public float DriverCarSLBlinkRpm { get; set; } = 8200f;
    public float DriverCarEstLapTime { get; set; } = 100f;
    public Dictionary<int, string> SessionTypesByNum { get; } = new();
    public Dictionary<int, YamlDriverMeta> DriversByIdx { get; } = new();
}

/// <summary>
/// Fast, zero-dependency parser tailored for iRacing SessionInfo YAML.
/// </summary>
public static class IRacingYamlParser
{
    public static YamlSessionMeta Parse(string yaml)
    {
        var meta = new YamlSessionMeta();
        if (string.IsNullOrWhiteSpace(yaml)) return meta;

        string section = "";
        bool inDriversList = false;
        bool inSessionsList = false;
        YamlDriverMeta? currentDriver = null;
        int currentSessionNum = -1;

        var lines = yaml.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string raw = lines[i].TrimEnd('\r');
            if (raw.Length == 0 || raw[0] == '#' || raw == "---" || raw == "...") continue;

            int indent = 0;
            while (indent < raw.Length && raw[indent] == ' ') indent++;
            string trimmed = raw.Substring(indent);

            if (indent == 0 && trimmed.EndsWith(':'))
            {
                FlushDriver(meta, ref currentDriver);
                section = trimmed.TrimEnd(':');
                inDriversList = false;
                inSessionsList = false;
                continue;
            }

            if (section == "WeekendInfo")
            {
                if (TrySplitKv(trimmed, out string k, out string v))
                {
                    switch (k)
                    {
                        case "TrackDisplayName":
                            meta.TrackDisplayName = Unquote(v);
                            break;
                        case "TrackLength":
                            meta.TrackLengthMeters = ParseTrackLengthMeters(v);
                            break;
                        case "WeekendOptions":
                            break;
                        case "IncidentLimit":
                            if (int.TryParse(v, out int lim) && lim > 0) meta.IncidentLimit = lim;
                            break;
                    }
                }
            }
            else if (section == "SessionInfo")
            {
                if (trimmed == "Sessions:")
                {
                    inSessionsList = true;
                    continue;
                }
                if (!inSessionsList) continue;

                bool isItemStart = trimmed.StartsWith("- ");
                string kvLine = isItemStart ? trimmed.Substring(2).Trim() : trimmed;
                if (TrySplitKv(kvLine, out string k, out string v))
                {
                    if (k == "SessionNum" && int.TryParse(v, out int sNum))
                        currentSessionNum = sNum;
                    else if (k == "SessionType" && currentSessionNum >= 0)
                        meta.SessionTypesByNum[currentSessionNum] = Unquote(v).ToUpperInvariant();
                }
            }
            else if (section == "DriverInfo")
            {
                if (trimmed == "Drivers:")
                {
                    inDriversList = true;
                    continue;
                }

                if (!inDriversList)
                {
                    if (TrySplitKv(trimmed, out string k, out string v))
                    {
                        switch (k)
                        {
                            case "DriverCarIdx":
                                if (int.TryParse(v, out int idx)) meta.DriverCarIdx = idx;
                                break;
                            case "DriverCarFuelMaxLtr":
                                if (TryParseFloat(v, out float maxF) && maxF > 1f) meta.DriverCarFuelMaxLtr = maxF;
                                break;
                            case "DriverCarSLFirstRPM":
                                if (TryParseFloat(v, out float sl1) && sl1 > 500f) meta.DriverCarSLFirstRpm = sl1;
                                break;
                            case "DriverCarSLShiftRPM":
                                if (TryParseFloat(v, out float sl2) && sl2 > 500f) meta.DriverCarSLShiftRpm = sl2;
                                break;
                            case "DriverCarSLBlinkRPM":
                                if (TryParseFloat(v, out float sl3) && sl3 > 500f) meta.DriverCarSLBlinkRpm = sl3;
                                break;
                            case "DriverCarEstLapTime":
                                if (TryParseFloat(v, out float lapT) && lapT > 10f) meta.DriverCarEstLapTime = lapT;
                                break;
                        }
                    }
                    continue;
                }

                bool isNewDriver = trimmed.StartsWith("- ");
                string line = isNewDriver ? trimmed.Substring(2).Trim() : trimmed;
                if (isNewDriver)
                {
                    FlushDriver(meta, ref currentDriver);
                    currentDriver = new YamlDriverMeta();
                }

                if (currentDriver == null) continue;
                if (TrySplitKv(line, out string dk, out string dv))
                {
                    switch (dk)
                    {
                        case "CarIdx":
                            if (int.TryParse(dv, out int cIdx)) currentDriver.CarIdx = cIdx;
                            break;
                        case "UserName":
                            currentDriver.UserName = Unquote(dv);
                            break;
                        case "AbbrevName":
                            currentDriver.AbbrevName = Unquote(dv);
                            break;
                        case "CarNumber":
                            currentDriver.CarNumber = Unquote(dv);
                            break;
                        case "CarScreenName":
                        case "CarScreenNameShort":
                            if (string.IsNullOrEmpty(currentDriver.CarScreenName))
                                currentDriver.CarScreenName = Unquote(dv);
                            break;
                        case "CarPath":
                            currentDriver.CarPath = Unquote(dv);
                            break;
                        case "CarIsPaceCar":
                            if (int.TryParse(dv, out int pc)) currentDriver.CarIsPaceCar = pc;
                            break;
                        case "IsSpectator":
                            if (int.TryParse(dv, out int sp)) currentDriver.IsSpectator = sp;
                            break;
                        case "CarClassID":
                            if (int.TryParse(dv, out int cid)) currentDriver.CarClassId = cid;
                            break;
                        case "CarClassShortName":
                            string sn = Unquote(dv);
                            if (!string.IsNullOrWhiteSpace(sn)) currentDriver.CarClassShortName = sn;
                            break;
                        case "CarClassColor":
                            currentDriver.CarClassColorHex = ParseHexColor(dv, "#F5A623");
                            break;
                        case "CarClassEstLapTime":
                            if (TryParseFloat(dv, out float est) && est > 10f) currentDriver.CarClassEstLapTime = est;
                            break;
                        case "IRating":
                            if (int.TryParse(dv, out int ir) && ir > 0) currentDriver.IRating = ir;
                            break;
                        case "LicString":
                            currentDriver.LicString = FormatLicense(Unquote(dv));
                            break;
                        case "LicColor":
                            currentDriver.LicColorHex = ParseHexColor(dv, "#3B82F6");
                            break;
                    }
                }
            }
        }

        FlushDriver(meta, ref currentDriver);
        return meta;
    }

    private static void FlushDriver(YamlSessionMeta meta, ref YamlDriverMeta? driver)
    {
        if (driver != null && driver.CarIdx >= 0)
        {
            if (string.IsNullOrWhiteSpace(driver.AbbrevName))
                driver.AbbrevName = AbbreviateName(driver.UserName);
            driver.Brand = CarBrandResolver.Resolve(driver.CarScreenName, driver.CarPath);
            meta.DriversByIdx[driver.CarIdx] = driver;
        }
        driver = null;
    }

    private static bool TrySplitKv(string line, out string key, out string val)
    {
        int idx = line.IndexOf(':');
        if (idx <= 0)
        {
            key = "";
            val = "";
            return false;
        }
        key = line.Substring(0, idx).Trim();
        val = line.Substring(idx + 1).Trim();
        return true;
    }

    private static string Unquote(string s)
    {
        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            return s.Substring(1, s.Length - 2);
        return s;
    }

    private static bool TryParseFloat(string s, out float result)
    {
        string token = s.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static float ParseTrackLengthMeters(string s)
    {
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return 5000f;
        if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
            return 5000f;
        if (parts.Length > 1 && parts[1].StartsWith("mi", StringComparison.OrdinalIgnoreCase))
            return val * 1609.344f;
        return val * 1000f;
    }

    private static string ParseHexColor(string raw, string fallback)
    {
        string clean = Unquote(raw).Trim();
        if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            clean = clean.Substring(2);
        if (clean.StartsWith("#"))
            clean = clean.Substring(1);
        if (clean.Length == 6)
        {
            // Avoid invisible black/dark default class colors (0x000000 / 0xffffff)
            if (clean.Equals("000000", StringComparison.OrdinalIgnoreCase)) return fallback;
            return "#" + clean.ToUpperInvariant();
        }
        return fallback;
    }

    private static string FormatLicense(string lic)
    {
        // e.g. "A 4.99" -> "A 4.9"
        var parts = lic.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float sr))
            return $"{parts[0]} {sr:0.0}";
        return string.IsNullOrWhiteSpace(lic) ? "R 2.5" : lic;
    }

    public static string AbbreviateName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "Driver";
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0];
        return $"{parts[0][0]}. {parts[^1]}";
    }
}
