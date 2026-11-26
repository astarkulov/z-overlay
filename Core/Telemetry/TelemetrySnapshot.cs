using System;
using System.Collections.Generic;

namespace ApexHUD.Core.Telemetry;

public enum CarProximityState
{
    Off = 0,
    Clear = 1,
    CarLeft = 2,
    CarRight = 3,
    CarLeftRight = 4,
    TwoCarsLeft = 5,
    TwoCarsRight = 6
}

public enum LapRelation
{
    SameLap = 0,
    LapAhead = 1,
    LapDown = -1
}

[Flags]
public enum SessionFlagMask : uint
{
    None = 0,
    Checkered = 0x00000001,
    White = 0x00000002,
    Green = 0x00000004,
    Yellow = 0x00000008,
    Red = 0x00000010,
    Blue = 0x00000020,
    Debris = 0x00000040,
    Crossed = 0x00000080,
    YellowWaving = 0x00000100,
    OneLapToGreen = 0x00000200,
    GreenHeld = 0x00000400,
    Caution = 0x00004000,
    CautionWaving = 0x00008000,
    Black = 0x00010000,
    Disqualify = 0x00020000,
    Repair = 0x00100000
}

public sealed class CornerSpeedRecord
{
    public string Label { get; set; } = "";
    public float PlayerMinMs { get; set; }
    public float PlayerBestMinMs { get; set; }
    public float SessionFastestMinMs { get; set; }
    public bool HasBestRef { get; set; }
    public bool HasFastestRef { get; set; }
    public bool IsLiveCorner { get; set; }
}

public sealed class InputTelemetry
{
    public float Throttle { get; set; }           // 0..1
    public float Brake { get; set; }              // 0..1
    public float Clutch { get; set; }             // 0..1 (1 = fully pressed)
    public bool AbsActive { get; set; }
    public float SteeringAngleRad { get; set; }   // radians (+ left, - right in iRacing)
    public float SteeringMaxRad { get; set; } = 3.92f;
    public int Gear { get; set; }                 // -1 R, 0 N, 1..8
    public float SpeedMs { get; set; }            // m/s
    public float Rpm { get; set; }
    public float ShiftFirstRpm { get; set; } = 6400f;
    public float ShiftOptimalRpm { get; set; } = 7800f;
    public float ShiftBlinkRpm { get; set; } = 8250f;
    public float ShiftPct { get; set; }           // 0..1

    // Racelab-style in-car electronics & live lap delta
    public float BrakeBiasPct { get; set; }       // e.g. 54.2 (%)
    public int TractionControl { get; set; } = -1;// -1 if unavailable
    public int AbsSetting { get; set; } = -1;     // -1 if unavailable
    public float LapDeltaToBestSec { get; set; }  // negative = faster (green), positive = slower (red)
    public bool HasLapDelta { get; set; }

    // Racelab-style last 4 corners: your min speed vs session fastest min speed at that track sector
    public List<CornerSpeedRecord> RecentCorners { get; set; } = new();
}

public sealed class FuelTelemetry
{
    public float FuelLevelLiters { get; set; }
    public float FuelMaxLiters { get; set; } = 100f;
    public float FuelPct { get; set; }
    public float LastLapBurn { get; set; }
    public float AvgLapBurn { get; set; }
    public float MaxLapBurn { get; set; }
    public float EstLapsOnFuel { get; set; }
    public float RaceLapsRemaining { get; set; }
    public float FuelToAddLiters { get; set; }
    public float FuelToAddMinLiters { get; set; }
    public float FuelToAddSafeLiters { get; set; }
    public float FuelSurplusLiters { get; set; }  // + surplus to end, - deficit
    public float EstRefuelSeconds { get; set; }
    public int PitStopsRemaining { get; set; }
    public float TargetBurnPerLap { get; set; }
    public bool IsLowFuel { get; set; }
    public bool IsPitWindowOpen { get; set; }
    public bool HasReliableBurn { get; set; }
}

public sealed class RelativeEntry
{
    public int CarIdx { get; set; }
    public int ClassPosition { get; set; }
    public string CarNumber { get; set; } = "";
    public CarBrand Brand { get; set; } = CarBrand.Unknown;
    public string DriverName { get; set; } = "";
    public string ClassColorHex { get; set; } = "#F5A623";
    public string LicenseText { get; set; } = "A 3.5";
    public string LicenseColorHex { get; set; } = "#2563EB";
    public int IRating { get; set; }
    public float GapSeconds { get; set; }         // + ahead, - behind
    public float LastLapSeconds { get; set; }
    public float PaceDeltaVsPlayerSec { get; set; } // + slower than you, - faster than you
    public bool HasPaceDelta { get; set; }
    public bool IsPlayer { get; set; }
    public bool IsOnPitRoad { get; set; }
    public bool IsOutLap { get; set; }
    public bool IsOffTrack { get; set; }
    public LapRelation LapRelation { get; set; }
}

public sealed class StandingsEntry
{
    public int CarIdx { get; set; }
    public int ClassPosition { get; set; }
    public int OverallPosition { get; set; }
    public int PosDelta { get; set; }             // + gained positions, - lost positions
    public string CarNumber { get; set; } = "";
    public CarBrand Brand { get; set; } = CarBrand.Unknown;
    public string DriverName { get; set; } = "";
    public int ClassId { get; set; }
    public string ClassName { get; set; } = "GT3";
    public string ClassColorHex { get; set; } = "#F5A623";
    public string LicenseText { get; set; } = "A 3.5";
    public string LicenseColorHex { get; set; } = "#2563EB";
    public int IRating { get; set; }
    public int ProjectedIRDelta { get; set; }
    public float GapToLeaderSeconds { get; set; }
    public float IntervalSeconds { get; set; }    // gap to car directly ahead in class
    public int LapsDown { get; set; }
    public float LastLapSeconds { get; set; }
    public float BestLapSeconds { get; set; }
    public bool IsClassBestLap { get; set; }
    public string TireCode { get; set; } = "D";   // D=Dry, W=Wet, S=Soft, M=Med, H=Hard
    public string TireColorHex { get; set; } = "#94A3B8";
    public int StintLaps { get; set; }
    public bool IsPlayer { get; set; }
    public bool IsOnPitRoad { get; set; }
    public int PitStops { get; set; }
}

public sealed class StandingsClassGroup
{
    public int ClassId { get; set; }
    public string ClassName { get; set; } = "";
    public string ClassColorHex { get; set; } = "#F5A623";
    public int Sof { get; set; }
    public int TotalDrivers { get; set; }
    public List<StandingsEntry> Drivers { get; set; } = new();
}

public sealed class RadarBlip
{
    public float OffsetX { get; set; }   // meters, - left, + right
    public float OffsetY { get; set; }   // meters, + ahead, - behind
    public string ColorHex { get; set; } = "#F5A623";
}

public sealed class RadarTelemetry
{
    public CarProximityState Proximity { get; set; } = CarProximityState.Clear;
    public float NearestAheadMeters { get; set; } = 999f;
    public float NearestBehindMeters { get; set; } = 999f;
    public List<RadarBlip> Blips { get; set; } = new();
    public bool HasActiveThreat =>
        Proximity is not (CarProximityState.Off or CarProximityState.Clear) || Blips.Count > 0;
}

public sealed class SessionHeaderTelemetry
{
    public string TrackName { get; set; } = "Disconnected";
    public string SessionType { get; set; } = "WAIT";
    public bool IsRaceSession { get; set; } = true;
    public int CurrentLap { get; set; }
    public int TotalLaps { get; set; }
    public double TimeRemainingSeconds { get; set; }
    public int Incidents { get; set; }
    public int IncidentLimit { get; set; } = 17;
    public int OverallSof { get; set; }
    public float TrackTempC { get; set; }
    public float AirTempC { get; set; }
    public bool IsWetTrack { get; set; }
    public SessionFlagMask Flags { get; set; }
}

public sealed class TelemetrySnapshot
{
    public bool IsConnected { get; set; }
    public bool IsDemo { get; set; }
    public bool IsOnTrack { get; set; }
    public bool IsDriving { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public SessionHeaderTelemetry Session { get; set; } = new();
    public InputTelemetry Inputs { get; set; } = new();
    public FuelTelemetry Fuel { get; set; } = new();
    public List<RelativeEntry> Relative { get; set; } = new();
    public List<StandingsClassGroup> Standings { get; set; } = new();
    public RadarTelemetry Radar { get; set; } = new();
}
