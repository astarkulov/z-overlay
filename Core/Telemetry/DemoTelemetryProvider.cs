using System;
using System.Collections.Generic;
using System.Linq;

namespace ApexHUD.Core.Telemetry;

/// <summary>
/// Realistic 60 Hz multiclass GT3 + LMP2 Spa-Francorchamps simulator so the user can
/// tune layout, scale, and visuals immediately even without iRacing open.
/// </summary>
public sealed class DemoTelemetryProvider
{
    private double _simTime = 0.0;
    private float _fuel = 36.8f;

    private sealed record DemoCar(
        int Idx,
        string Number,
        CarBrand Brand,
        string Name,
        int ClassId,
        string ClassName,
        string ClassColor,
        string Lic,
        string LicColor,
        int IRating,
        float BaseGap,
        float BaseLap,
        bool IsPlayer,
        int PitStops,
        LapRelation Rel);

    private readonly List<DemoCar> _cars = new()
    {
        // LMP2 Class
        new(1, "07", CarBrand.Dallara,     "Max Verstappen",     1, "LMP2", "#38BDF8", "A 4.9", "#2563EB", 8940, 24.5f, 126.412f, false, 1, LapRelation.LapAhead),
        new(2, "24", CarBrand.Dallara,     "Kevin Estre",        1, "LMP2", "#38BDF8", "A 4.6", "#2563EB", 7210, 21.8f, 126.890f, false, 1, LapRelation.LapAhead),
        new(3, "88", CarBrand.Dallara,     "Laurens Vanthoor",   1, "LMP2", "#38BDF8", "A 3.9", "#2563EB", 6480, 18.2f, 127.145f, false, 1, LapRelation.LapAhead),

        // GT3 Class (Player's Class)
        new(4, "12", CarBrand.McLaren,     "Scott Baldwin",      2, "GT3",  "#F5A623", "A 4.8", "#2563EB", 5420,  4.8f, 136.814f, false, 1, LapRelation.SameLap),
        new(5, "91", CarBrand.Mercedes,    "Daniel Morad",       2, "GT3",  "#F5A623", "A 4.2", "#2563EB", 4890,  2.1f, 137.041f, false, 1, LapRelation.SameLap),
        new(6, "33", CarBrand.Ferrari,     "Axel Lindholm",      2, "GT3",  "#F5A623", "A 3.6", "#2563EB", 4120,  0.65f,137.188f, false, 1, LapRelation.SameLap),
        new(7, "17", CarBrand.Porsche,     "Aktan Astarkulov",   2, "GT3",  "#F5A623", "A 3.8", "#2563EB", 3850,  0.0f, 137.224f, true,  1, LapRelation.SameLap),
        new(8, "46", CarBrand.Bmw,         "Raffaele Marciello", 2, "GT3",  "#F5A623", "B 3.4", "#16A34A", 4260, -0.42f,137.302f, false, 1, LapRelation.SameLap),
        new(9, "77", CarBrand.Lamborghini, "Thomas Preining",    2, "GT3",  "#F5A623", "A 2.9", "#2563EB", 3490, -1.95f,137.650f, false, 1, LapRelation.SameLap),
        new(10,"04", CarBrand.Audi,        "Jules Gounon",       2, "GT3",  "#F5A623", "B 4.1", "#16A34A", 3120, -3.70f,137.982f, false, 1, LapRelation.SameLap),
        new(11,"55", CarBrand.AstonMartin, "Nicki Thiim",        2, "GT3",  "#F5A623", "B 2.7", "#16A34A", 2840, -6.10f,138.410f, false, 0, LapRelation.SameLap),
        new(12,"63", CarBrand.Chevrolet,   "Maro Engel",         2, "GT3",  "#F5A623", "C 3.2", "#EAB308", 2410,-11.40f,139.115f, false, 1, LapRelation.LapDown)
    };

    public TelemetrySnapshot Step(double dtSec, double fuelMarginLaps, string fuelCalcMode = "Avg")
    {
        _simTime += dtSec;
        double phase = (_simTime % 14.0) / 14.0; // 14s repeating cornering cycle

        // Realistic corner telemetry cycle: Straight -> Heavy Braking + ABS -> Trail Brake + Apex -> Exit WOT
        float throttle, brake, clutch = 0f, steerRad, speedKmh;
        bool abs = false;
        int gear;

        if (phase < 0.30)
        {
            // Full throttle straight + upshift
            float t = (float)(phase / 0.30);
            throttle = 1.0f;
            brake = 0f;
            steerRad = (float)(Math.Sin(_simTime * 1.4) * 0.06);
            speedKmh = 212f + t * 56f;
            gear = speedKmh > 246f ? 6 : 5;
        }
        else if (phase < 0.48)
        {
            // Heavy braking zone into chicane with ABS pulse
            float t = (float)((phase - 0.30) / 0.18);
            throttle = 0f;
            brake = t < 0.25f ? Math.Clamp(t * 4f * 0.92f, 0f, 0.92f) : Math.Clamp(0.92f - (t - 0.25f) * 0.72f, 0.2f, 0.92f);
            abs = t > 0.12f && t < 0.55f && ((int)(_simTime * 18) % 2 == 0);
            steerRad = (float)(t * 0.35);
            speedKmh = 268f - t * 174f;
            gear = speedKmh > 200 ? 5 : speedKmh > 155 ? 4 : speedKmh > 118 ? 3 : 2;
        }
        else if (phase < 0.68)
        {
            // Trail-braking into apex and rolling back on throttle
            float t = (float)((phase - 0.48) / 0.20);
            brake = Math.Clamp(0.28f * (1f - t * 1.5f), 0f, 0.28f);
            throttle = Math.Clamp((t - 0.35f) * 1.55f, 0f, 0.95f);
            steerRad = (float)(Math.Sin(t * Math.PI) * 1.38);
            speedKmh = 94f + Math.Max(0f, t - 0.4f) * 58f;
            gear = 2;
        }
        else
        {
            // Corner exit acceleration through gears 2->3->4->5
            float t = (float)((phase - 0.68) / 0.32);
            throttle = Math.Clamp(0.85f + t * 0.2f, 0f, 1f);
            brake = 0f;
            steerRad = (float)((1f - t) * -0.45 * Math.Sin(t * Math.PI));
            speedKmh = 128f + t * 86f;
            gear = speedKmh < 142 ? 2 : speedKmh < 174 ? 3 : speedKmh < 204 ? 4 : 5;
        }

        // Compute realistic RPM in gear
        float gearFloor = gear switch { 2 => 80f, 3 => 130f, 4 => 165f, 5 => 198f, _ => 238f };
        float gearCeiling = gear switch { 2 => 145f, 3 => 178f, 4 => 210f, 5 => 252f, _ => 282f };
        float gearFrac = Math.Clamp((speedKmh - gearFloor) / (gearCeiling - gearFloor), 0.15f, 0.99f);
        float rpm = 4800f + gearFrac * 3550f;
        float shiftPct = Math.Clamp((rpm - 6400f) / (8250f - 6400f), 0f, 1f);

        // Fuel consumption ~2.85 L / lap
        _fuel -= (float)(dtSec * 0.022);
        if (_fuel < 8.0f) _fuel = 38.4f;

        float lastBurn = 2.81f;
        float avgBurn = 2.84f;
        float maxBurn = 2.96f;
        float activeBurn = fuelCalcMode switch { "Last" => lastBurn, "Max" => maxBurn, _ => avgBurn };
        float lapsLeftInRace = 16.4f;
        float estLapsFuel = _fuel / activeBurn;
        float neededMin = lapsLeftInRace * activeBurn;
        float toAddMin = Math.Max(0f, neededMin - _fuel);
        float toAdd = Math.Max(0f, (lapsLeftInRace + (float)fuelMarginLaps) * activeBurn - _fuel);
        float toAddSafe = Math.Max(0f, (lapsLeftInRace + (float)fuelMarginLaps + 1.0f) * activeBurn - _fuel);

        // Live oscillating gaps for Relative
        var relative = new List<RelativeEntry>();
        var gt3Cars = _cars.Where(c => c.ClassId == 2).ToList();
        var lmp2Lead = _cars[2];

        var relPool = new List<RelativeEntry>
        {
            MakeRel(lmp2Lead, 3, 2.85f + (float)Math.Sin(_simTime * 0.25) * 0.35f, false)
        };

        int gt3Pos = 1;
        foreach (var c in gt3Cars)
        {
            float wobble = c.IsPlayer ? 0f : (float)Math.Sin(_simTime * 0.45 + c.Idx) * 0.18f;
            float gap = c.BaseGap + wobble;
            relPool.Add(MakeRel(c, gt3Pos++, gap, c.Idx == 11));
        }

        var ahead = relPool.Where(r => !r.IsPlayer && r.GapSeconds > 0).OrderBy(r => r.GapSeconds).Take(4).Reverse();
        var player = relPool.First(r => r.IsPlayer);
        var behind = relPool.Where(r => !r.IsPlayer && r.GapSeconds < 0).OrderByDescending(r => r.GapSeconds).Take(4);
        relative.AddRange(ahead);
        relative.Add(player);
        relative.AddRange(behind);

        // Standings grouped by class (GT3 first since player is GT3, then LMP2)
        var standings = new List<StandingsClassGroup>
        {
            BuildDemoGroup(2, "GT3", "#F5A623", gt3Cars),
            BuildDemoGroup(1, "LMP2", "#38BDF8", _cars.Where(c => c.ClassId == 1).ToList())
        };

        // Radar: periodically bring a rival alongside on left/right so user can see radar live!
        double radarCycle = _simTime % 10.0;
        var radar = new RadarTelemetry();
        if (radarCycle is > 2.2 and < 5.6)
        {
            float y = (float)Math.Sin((radarCycle - 2.2) / 3.4 * Math.PI) * 5.0f - 2.2f;
            radar.Proximity = CarProximityState.CarRight;
            radar.NearestAheadMeters = Math.Max(0.4f, y);
            radar.Blips.Add(new RadarBlip { OffsetX = 2.4f, OffsetY = y, ColorHex = "#F5A623" });
        }
        else if (radarCycle is > 6.4 and < 8.6)
        {
            float y = (float)((radarCycle - 6.4) / 2.2 * 10.0 - 5.0);
            radar.Proximity = CarProximityState.CarLeft;
            radar.NearestBehindMeters = Math.Abs(y);
            radar.Blips.Add(new RadarBlip { OffsetX = -2.5f, OffsetY = y, ColorHex = "#38BDF8" });
        }
        else
        {
            radar.Proximity = CarProximityState.Clear;
        }

        int overallSof = IRatingEstimator.CalculateSof(_cars.Select(c => c.IRating).ToList());

        return new TelemetrySnapshot
        {
            IsConnected = true,
            IsDemo = true,
            IsOnTrack = true,
            TimestampUtc = DateTime.UtcNow,
            Session = new SessionHeaderTelemetry
            {
                TrackName = "Circuit de Spa-Francorchamps",
                SessionType = "RACE",
                CurrentLap = 11,
                TotalLaps = 27,
                TimeRemainingSeconds = Math.Max(0, 2245.0 - (_simTime % 600.0)),
                Incidents = 2,
                IncidentLimit = 17,
                OverallSof = overallSof,
                TrackTempC = 31.4f,
                AirTempC = 22.8f,
                Flags = SessionFlagMask.Green
            },
            Inputs = new InputTelemetry
            {
                Throttle = throttle,
                Brake = brake,
                Clutch = clutch,
                AbsActive = abs,
                SteeringAngleRad = steerRad,
                SteeringMaxRad = 3.92f,
                Gear = gear,
                SpeedMs = speedKmh / 3.6f,
                Rpm = rpm,
                ShiftFirstRpm = 6400f,
                ShiftOptimalRpm = 7800f,
                ShiftBlinkRpm = 8250f,
                ShiftPct = shiftPct,
                BrakeBiasPct = 54.4f,
                TractionControl = 4,
                AbsSetting = 3,
                LapDeltaToBestSec = (float)(Math.Sin(_simTime * 0.45) * 0.34 - 0.08),
                HasLapDelta = true,
                RecentCorners = new List<CornerSpeedRecord>
                {
                    new() { Label = "T1",  PlayerMinMs = 68f / 3.6f, SessionFastestMinMs = 71f / 3.6f, HasFastestRef = true },
                    new() { Label = "T7",  PlayerMinMs = 96f / 3.6f, SessionFastestMinMs = 94f / 3.6f, HasFastestRef = true },
                    new() { Label = "T10", PlayerMinMs = 86f / 3.6f, SessionFastestMinMs = 89f / 3.6f, HasFastestRef = true },
                    new()
                    {
                        Label = phase is >= 0.30 and < 0.65 ? "NOW" : "T18",
                        PlayerMinMs = (phase is >= 0.30 and < 0.65 ? Math.Min(speedKmh, 140f) : 94f) / 3.6f,
                        SessionFastestMinMs = 96f / 3.6f,
                        HasFastestRef = true,
                        IsLiveCorner = phase is >= 0.30 and < 0.65
                    }
                }
            },
            Fuel = new FuelTelemetry
            {
                FuelLevelLiters = _fuel,
                FuelMaxLiters = 100f,
                FuelPct = _fuel / 100f,
                LastLapBurn = lastBurn,
                AvgLapBurn = avgBurn,
                MaxLapBurn = maxBurn,
                EstLapsOnFuel = estLapsFuel,
                RaceLapsRemaining = lapsLeftInRace,
                FuelToAddLiters = toAdd,
                FuelToAddMinLiters = toAddMin,
                FuelToAddSafeLiters = toAddSafe,
                FuelSurplusLiters = _fuel - neededMin,
                EstRefuelSeconds = toAdd / 2.4f,
                PitStopsRemaining = toAdd > 0.1f ? 1 : 0,
                TargetBurnPerLap = _fuel / lapsLeftInRace,
                HasReliableBurn = true,
                IsLowFuel = estLapsFuel < 3.0f,
                IsPitWindowOpen = toAdd > 0.5f && estLapsFuel < 14f
            },
            Relative = relative,
            Standings = standings,
            Radar = radar
        };
    }

    private static RelativeEntry MakeRel(DemoCar c, int classPos, float gap, bool inPit)
    {
        float playerLap = 137.224f;
        return new()
        {
            CarIdx = c.Idx,
            ClassPosition = classPos,
            CarNumber = c.Number,
            Brand = c.Brand,
            DriverName = c.Name,
            ClassColorHex = c.ClassColor,
            LicenseText = c.Lic,
            LicenseColorHex = c.LicColor,
            IRating = c.IRating,
            GapSeconds = gap,
            LastLapSeconds = c.BaseLap,
            PaceDeltaVsPlayerSec = c.IsPlayer ? 0f : c.BaseLap - playerLap,
            HasPaceDelta = !c.IsPlayer,
            IsPlayer = c.IsPlayer,
            IsOnPitRoad = inPit,
            IsOutLap = c.Idx == 10,
            LapRelation = c.Rel
        };
    }

    private StandingsClassGroup BuildDemoGroup(int classId, string name, string color, List<DemoCar> cars)
    {
        var irs = cars.Select(c => c.IRating).ToList();
        int sof = IRatingEstimator.CalculateSof(irs);
        int[] deltas = IRatingEstimator.EstimateDeltas(irs);
        int[] demoPosDeltas = { 1, -1, 0, 2, -2, 3, -1, 0, 2 };

        var entries = new List<StandingsEntry>();
        float leadBase = cars[0].BaseGap;
        float prevGap = 0f;
        for (int i = 0; i < cars.Count; i++)
        {
            var c = cars[i];
            float gap = i == 0 ? 0f : Math.Abs(leadBase - c.BaseGap) + (float)Math.Sin(_simTime * 0.3 + i) * 0.08f;
            float interval = i == 0 ? 0f : Math.Max(0.05f, gap - prevGap);
            prevGap = gap;

            entries.Add(new StandingsEntry
            {
                CarIdx = c.Idx,
                ClassPosition = i + 1,
                OverallPosition = i + 1,
                PosDelta = demoPosDeltas[i % demoPosDeltas.Length],
                CarNumber = c.Number,
                Brand = c.Brand,
                DriverName = c.Name,
                ClassId = classId,
                ClassName = name,
                ClassColorHex = color,
                LicenseText = c.Lic,
                LicenseColorHex = c.LicColor,
                IRating = c.IRating,
                ProjectedIRDelta = deltas[i],
                GapToLeaderSeconds = gap,
                IntervalSeconds = interval,
                LapsDown = c.Rel == LapRelation.LapDown ? 1 : 0,
                LastLapSeconds = c.BaseLap + 0.14f,
                BestLapSeconds = c.BaseLap,
                IsClassBestLap = i == 0,
                TireCode = c.Idx % 4 == 0 ? "M" : "S",
                TireColorHex = c.Idx % 4 == 0 ? "#EAB308" : "#EF4444",
                StintLaps = 6 + (i % 5),
                IsPlayer = c.IsPlayer,
                IsOnPitRoad = c.Idx == 11,
                PitStops = c.PitStops
            });
        }

        return new StandingsClassGroup
        {
            ClassId = classId,
            ClassName = name,
            ClassColorHex = color,
            Sof = sof,
            TotalDrivers = cars.Count,
            Drivers = entries
        };
    }
}
