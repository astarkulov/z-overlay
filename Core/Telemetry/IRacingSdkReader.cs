using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using ApexHUD.Core.Native;

namespace ApexHUD.Core.Telemetry;

/// <summary>
/// Direct native binary reader for iRacing Shared Memory (Local\IRSDKMemMapFileName).
/// Zero NuGet dependencies, 60 Hz frame-synced reads via IRSDKDataValidEvent.
/// </summary>
public sealed class IRacingSdkReader : IDisposable
{
    private const string MemMapName = "Local\\IRSDKMemMapFileName";
    private const string DataValidEventName = "Local\\IRSDKDataValidEvent";
    private const int MaxCars = 64;

    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _view;
    private IntPtr _dataEvent = IntPtr.Zero;

    private readonly Dictionary<string, VarHeader> _vars = new(StringComparer.Ordinal);
    private readonly FuelTracker _fuelTracker = new();
    private readonly CornerSpeedTracker _cornerTracker = new();
    private readonly int[] _pitStops = new int[MaxCars];
    private readonly int[] _lastPitLap = new int[MaxCars];
    private readonly int[] _startClassPos = new int[MaxCars];
    private readonly bool[] _prevOnPitRoad = new bool[MaxCars];

    private int _lastSessionInfoUpdate = -1;
    private int _lastSessionNum = -1;
    private YamlSessionMeta _yamlMeta = new();

    private readonly float[] _carLapDistPct = new float[MaxCars];
    private readonly int[] _carLap = new int[MaxCars];
    private readonly int[] _carLapCompleted = new int[MaxCars];
    private readonly int[] _carPosition = new int[MaxCars];
    private readonly int[] _carClassPosition = new int[MaxCars];
    private readonly float[] _carEstTime = new float[MaxCars];
    private readonly float[] _carF2Time = new float[MaxCars];
    private readonly float[] _carLastLap = new float[MaxCars];
    private readonly float[] _carBestLap = new float[MaxCars];
    private readonly bool[] _carOnPitRoad = new bool[MaxCars];
    private readonly int[] _carTrackSurface = new int[MaxCars];
    private readonly int[] _carTireCompound = new int[MaxCars];

    private byte[] _frameBuffer = Array.Empty<byte>();
    private bool _connected;

    private readonly struct VarHeader
    {
        public readonly int Type;
        public readonly int Offset;
        public readonly int Count;

        public VarHeader(int type, int offset, int count)
        {
            Type = type;
            Offset = offset;
            Count = count;
        }
    }

    public bool IsConnected => _connected;

    public bool TryConnect()
    {
        if (_connected) return true;

        try
        {
            _mmf = MemoryMappedFile.OpenExisting(MemMapName, MemoryMappedFileRights.Read);
            _view = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            _dataEvent = Win32.OpenEvent(Win32.SYNCHRONIZE, false, DataValidEventName);

            int status = _view.ReadInt32(4);
            if ((status & 1) == 0)
            {
                Disconnect();
                return false;
            }

            LoadVarHeaders();
            _lastSessionInfoUpdate = -1;
            _connected = true;
            return true;
        }
        catch
        {
            Disconnect();
            return false;
        }
    }

    public void Disconnect()
    {
        _connected = false;
        _vars.Clear();
        _lastSessionInfoUpdate = -1;
        _lastSessionNum = -1;
        _fuelTracker.Reset();
        _cornerTracker.Reset();
        Array.Clear(_pitStops);
        Array.Clear(_lastPitLap);
        Array.Clear(_startClassPos);
        Array.Clear(_prevOnPitRoad);

        if (_dataEvent != IntPtr.Zero)
        {
            Win32.CloseHandle(_dataEvent);
            _dataEvent = IntPtr.Zero;
        }
        _view?.Dispose();
        _view = null;
        _mmf?.Dispose();
        _mmf = null;
    }

    public bool WaitForData(int timeoutMs = 32)
    {
        if (_dataEvent == IntPtr.Zero) return false;
        return Win32.WaitForSingleObject(_dataEvent, (uint)timeoutMs) == Win32.WAIT_OBJECT_0;
    }

    public TelemetrySnapshot? ReadSnapshot(double fuelMarginLaps, string fuelCalcMode, int relativeEachSide, int maxStandingsRows)
    {
        if (!_connected || _view == null) return null;

        try
        {
            int status = _view.ReadInt32(4);
            if ((status & 1) == 0)
            {
                Disconnect();
                return null;
            }

            // Refresh SessionInfo YAML when version increments
            int sessionInfoUpdate = _view.ReadInt32(12);
            if (sessionInfoUpdate != _lastSessionInfoUpdate)
            {
                int yamlLen = _view.ReadInt32(16);
                int yamlOffset = _view.ReadInt32(20);
                if (yamlLen > 0 && yamlLen < 4_000_000 && yamlOffset > 0)
                {
                    byte[] yamlBytes = new byte[yamlLen];
                    _view.ReadArray(yamlOffset, yamlBytes, 0, yamlLen);
                    int nul = Array.IndexOf(yamlBytes, (byte)0);
                    string yaml = Encoding.Latin1.GetString(yamlBytes, 0, nul >= 0 ? nul : yamlLen);
                    _yamlMeta = IRacingYamlParser.Parse(yaml);
                    _lastSessionInfoUpdate = sessionInfoUpdate;
                }
            }

            // Pick latest of up to 4 telemetry buffers
            int numBuf = Math.Clamp(_view.ReadInt32(32), 1, 4);
            int bufLen = _view.ReadInt32(36);
            if (bufLen <= 0 || bufLen > 1_000_000) return null;

            int latestTick = int.MinValue;
            int latestOffset = -1;
            for (int i = 0; i < numBuf; i++)
            {
                int entryPos = 48 + i * 16;
                int tick = _view.ReadInt32(entryPos);
                int offset = _view.ReadInt32(entryPos + 4);
                if (tick > latestTick && offset > 0)
                {
                    latestTick = tick;
                    latestOffset = offset;
                }
            }

            if (latestOffset <= 0) return null;
            if (_frameBuffer.Length != bufLen)
                _frameBuffer = new byte[bufLen];

            _view.ReadArray(latestOffset, _frameBuffer, 0, bufLen);

            return BuildSnapshot(fuelMarginLaps, fuelCalcMode, relativeEachSide, maxStandingsRows);
        }
        catch
        {
            Disconnect();
            return null;
        }
    }

    private void LoadVarHeaders()
    {
        if (_view == null) return;
        _vars.Clear();

        int numVars = _view.ReadInt32(24);
        int varHeaderOffset = _view.ReadInt32(28);
        byte[] nameBuf = new byte[32];

        for (int i = 0; i < numVars; i++)
        {
            int pos = varHeaderOffset + i * 144;
            int type = _view.ReadInt32(pos);
            int offset = _view.ReadInt32(pos + 4);
            int count = _view.ReadInt32(pos + 8);
            _view.ReadArray(pos + 16, nameBuf, 0, 32);
            int nul = Array.IndexOf(nameBuf, (byte)0);
            string name = Encoding.ASCII.GetString(nameBuf, 0, nul >= 0 ? nul : 32);
            if (!string.IsNullOrEmpty(name))
                _vars[name] = new VarHeader(type, offset, count);
        }
    }

    private TelemetrySnapshot BuildSnapshot(double fuelMarginLaps, string fuelCalcMode, int relativeEachSide, int maxStandingsRows)
    {
        int sessionNum = ReadInt("SessionNum");
        if (sessionNum != _lastSessionNum)
        {
            _lastSessionNum = sessionNum;
            _fuelTracker.Reset();
            _cornerTracker.Reset();
            Array.Clear(_pitStops);
            Array.Clear(_lastPitLap);
            Array.Clear(_startClassPos);
            Array.Clear(_prevOnPitRoad);
        }

        int playerIdx = ReadInt("PlayerCarIdx", _yamlMeta.DriverCarIdx);
        bool isOnTrack = ReadBool("IsOnTrack");
        bool onPitRoad = ReadBool("OnPitRoad");
        int currentLap = ReadInt("Lap");
        float lapDistPct = ReadFloat("LapDistPct");

        ReadFloatArray("CarIdxLapDistPct", _carLapDistPct);
        ReadIntArray("CarIdxLap", _carLap);
        ReadIntArray("CarIdxLapCompleted", _carLapCompleted);
        ReadIntArray("CarIdxPosition", _carPosition);
        ReadIntArray("CarIdxClassPosition", _carClassPosition);
        ReadFloatArray("CarIdxEstTime", _carEstTime);
        ReadFloatArray("CarIdxF2Time", _carF2Time);
        ReadFloatArray("CarIdxLastLapTime", _carLastLap);
        ReadFloatArray("CarIdxBestLapTime", _carBestLap);
        ReadBoolArray("CarIdxOnPitRoad", _carOnPitRoad);
        ReadIntArray("CarIdxTrackSurface", _carTrackSurface);
        ReadIntArray("CarIdxTireCompound", _carTireCompound);

        // Track pit stop entries, stint start lap, and starting class position baseline
        for (int i = 0; i < MaxCars; i++)
        {
            if (_startClassPos[i] == 0 && _carClassPosition[i] > 0)
                _startClassPos[i] = _carClassPosition[i];

            if (_carOnPitRoad[i] && !_prevOnPitRoad[i] && _carLap[i] > 0)
            {
                _pitStops[i]++;
                _lastPitLap[i] = _carLap[i];
            }
            _prevOnPitRoad[i] = _carOnPitRoad[i];
        }

        // Inputs + in-car electronics (BB / TC / ABS) + Live Lap Delta
        float rawClutch = ReadFloat("Clutch", 1f);
        float bb = ReadFloat("dcBrakeBias", -1f);
        float tc = ReadFloat("dcTractionControl", -1f);
        float absSet = ReadFloat("dcABS", -1f);
        bool deltaOk = ReadBool("LapDeltaToBestLap_OK");
        float lapDelta = deltaOk ? ReadFloat("LapDeltaToBestLap") : 0f;
        double sessionTime = ReadDouble("SessionTime");
        float playerSpeedMs = Math.Max(0f, ReadFloat("Speed"));
        float playerThrottle = Math.Clamp(ReadFloat("Throttle"), 0f, 1f);
        float playerBrake = Math.Clamp(ReadFloat("Brake"), 0f, 1f);
        float playerSteerRad = ReadFloat("SteeringWheelAngle");
        int playerClassId = _yamlMeta.DriversByIdx.TryGetValue(playerIdx, out var pMeta) ? pMeta.CarClassId : 0;

        var recentCorners = _cornerTracker.Update(
            sessionTime,
            playerIdx,
            playerClassId,
            playerSpeedMs,
            playerBrake,
            playerThrottle,
            playerSteerRad,
            lapDistPct,
            isOnTrack && !onPitRoad,
            _yamlMeta.TrackLengthMeters,
            _carLapDistPct,
            _carBestLap,
            _carTrackSurface,
            _carOnPitRoad,
            _yamlMeta.DriversByIdx);

        var inputs = new InputTelemetry
        {
            Throttle = playerThrottle,
            Brake = playerBrake,
            Clutch = Math.Clamp(1f - rawClutch, 0f, 1f),
            AbsActive = ReadBool("BrakeABSactive"),
            SteeringAngleRad = playerSteerRad,
            SteeringMaxRad = Math.Max(1.0f, ReadFloat("SteeringWheelAngleMax", 3.92f)),
            Gear = ReadInt("Gear"),
            SpeedMs = playerSpeedMs,
            Rpm = Math.Max(0f, ReadFloat("RPM")),
            ShiftFirstRpm = _yamlMeta.DriverCarSLFirstRpm,
            ShiftOptimalRpm = _yamlMeta.DriverCarSLShiftRpm,
            ShiftBlinkRpm = _yamlMeta.DriverCarSLBlinkRpm,
            ShiftPct = Math.Clamp(ReadFloat("ShiftIndicatorPct"), 0f, 1f),
            BrakeBiasPct = bb > 10f ? bb : 0f,
            TractionControl = tc >= 0f ? (int)Math.Round(tc) : -1,
            AbsSetting = absSet >= 0f ? (int)Math.Round(absSet) : -1,
            LapDeltaToBestSec = lapDelta,
            HasLapDelta = deltaOk,
            RecentCorners = recentCorners
        };

        // Fuel
        int lapsRemainEx = ReadInt("SessionLapsRemainEx", 32767);
        double timeRemain = ReadDouble("SessionTimeRemain");
        float fuelLiters = ReadFloat("FuelLevel");
        var fuel = _fuelTracker.Update(
            fuelLiters,
            _yamlMeta.DriverCarFuelMaxLtr,
            currentLap,
            lapDistPct,
            isOnTrack,
            onPitRoad,
            lapsRemainEx,
            timeRemain,
            _yamlMeta.DriverCarEstLapTime,
            fuelMarginLaps,
            fuelCalcMode);

        // Standings grouped by class + SOF + projected +/- iR + intervals + purple best lap + tires
        var standings = BuildStandings(playerIdx, maxStandingsRows, out int overallSof);

        // Relative around player + pace delta vs player
        var relative = BuildRelative(playerIdx, relativeEachSide);

        // Radar
        var radar = BuildRadar(playerIdx);

        string sessionType = _yamlMeta.SessionTypesByNum.TryGetValue(sessionNum, out var st) ? st : "SESSION";
        int totalLaps = ReadInt("SessionLapsTotal");
        if (totalLaps >= 30000) totalLaps = 0;
        int wetness = ReadInt("TrackWetness");

        return new TelemetrySnapshot
        {
            IsConnected = true,
            IsDemo = false,
            IsOnTrack = isOnTrack,
            TimestampUtc = DateTime.UtcNow,
            Session = new SessionHeaderTelemetry
            {
                TrackName = _yamlMeta.TrackDisplayName,
                SessionType = sessionType,
                CurrentLap = Math.Max(0, currentLap),
                TotalLaps = totalLaps,
                TimeRemainingSeconds = Math.Max(0, timeRemain),
                Incidents = ReadInt("PlayerCarMyIncidentCount"),
                IncidentLimit = _yamlMeta.IncidentLimit,
                OverallSof = overallSof,
                TrackTempC = ReadFloat("TrackTempCrew", 28f),
                AirTempC = ReadFloat("AirTemp", 22f),
                IsWetTrack = wetness >= 2,
                Flags = (SessionFlagMask)ReadUInt("SessionFlags")
            },
            Inputs = inputs,
            Fuel = fuel,
            Relative = relative,
            Standings = standings,
            Radar = radar
        };
    }

    private List<StandingsClassGroup> BuildStandings(int playerIdx, int maxRows, out int overallSof)
    {
        var activeDrivers = new List<StandingsEntry>();
        var allIRatings = new List<int>();

        foreach (var kv in _yamlMeta.DriversByIdx)
        {
            var meta = kv.Value;
            int idx = meta.CarIdx;
            if (idx < 0 || idx >= MaxCars || meta.CarIsPaceCar == 1 || meta.IsSpectator == 1)
                continue;

            allIRatings.Add(meta.IRating);

            int classPos = _carClassPosition[idx];
            int overallPos = _carPosition[idx];
            var (tireCode, tireColor) = MapTireCompound(_carTireCompound[idx]);
            int stintLaps = Math.Max(0, _carLap[idx] - _lastPitLap[idx]);
            int posDelta = (_startClassPos[idx] > 0 && classPos > 0) ? (_startClassPos[idx] - classPos) : 0;

            activeDrivers.Add(new StandingsEntry
            {
                CarIdx = idx,
                ClassPosition = classPos > 0 ? classPos : 99,
                OverallPosition = overallPos > 0 ? overallPos : 99,
                PosDelta = posDelta,
                CarNumber = meta.CarNumber,
                Brand = meta.Brand,
                DriverName = !string.IsNullOrWhiteSpace(meta.UserName) ? meta.UserName : meta.AbbrevName,
                ClassId = meta.CarClassId,
                ClassName = meta.CarClassShortName,
                ClassColorHex = meta.CarClassColorHex,
                LicenseText = meta.LicString,
                LicenseColorHex = meta.LicColorHex,
                IRating = meta.IRating,
                GapToLeaderSeconds = Math.Max(0f, _carF2Time[idx]),
                LastLapSeconds = _carLastLap[idx] > 0f ? _carLastLap[idx] : 0f,
                BestLapSeconds = _carBestLap[idx] > 0f ? _carBestLap[idx] : 0f,
                TireCode = tireCode,
                TireColorHex = tireColor,
                StintLaps = stintLaps,
                IsPlayer = idx == playerIdx,
                IsOnPitRoad = _carOnPitRoad[idx],
                PitStops = _pitStops[idx]
            });
        }

        overallSof = IRatingEstimator.CalculateSof(allIRatings);

        var groups = new List<StandingsClassGroup>();
        int playerClassId = _yamlMeta.DriversByIdx.TryGetValue(playerIdx, out var pm) ? pm.CarClassId : -1;

        var grouped = activeDrivers
            .GroupBy(d => d.ClassId)
            .OrderByDescending(g => g.Key == playerClassId) // Player's class first
            .ThenBy(g => g.Min(x => x.OverallPosition));

        foreach (var g in grouped)
        {
            var ordered = g
                .OrderBy(d => d.ClassPosition)
                .ThenBy(d => d.OverallPosition)
                .ToList();

            // Re-index if session hasn't assigned positions yet (practice/grid)
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].ClassPosition == 99)
                    ordered[i].ClassPosition = i + 1;
            }

            var irList = ordered.Select(x => x.IRating).ToList();
            int classSof = IRatingEstimator.CalculateSof(irList);
            int[] deltas = IRatingEstimator.EstimateDeltas(irList);

            int leaderCompleted = ordered.Count > 0 ? _carLapCompleted[ordered[0].CarIdx] : 0;
            float leaderF2 = ordered.Count > 0 ? ordered[0].GapToLeaderSeconds : 0f;

            // Find fastest lap in class (purple)
            float bestClassLap = float.MaxValue;
            int bestClassIdx = -1;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].BestLapSeconds > 10f && ordered[i].BestLapSeconds < bestClassLap)
                {
                    bestClassLap = ordered[i].BestLapSeconds;
                    bestClassIdx = i;
                }
            }
            if (bestClassIdx >= 0) ordered[bestClassIdx].IsClassBestLap = true;

            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].ProjectedIRDelta = deltas[i];
                if (i == 0)
                {
                    ordered[i].GapToLeaderSeconds = 0f;
                    ordered[i].IntervalSeconds = 0f;
                    ordered[i].LapsDown = 0;
                }
                else
                {
                    int lapDiff = leaderCompleted - _carLapCompleted[ordered[i].CarIdx];
                    ordered[i].LapsDown = Math.Max(0, lapDiff);
                    ordered[i].GapToLeaderSeconds = Math.Max(0f, ordered[i].GapToLeaderSeconds - leaderF2);
                    ordered[i].IntervalSeconds = Math.Max(0f, ordered[i].GapToLeaderSeconds - ordered[i - 1].GapToLeaderSeconds);
                }
            }

            var displayed = SelectStandingsSlice(ordered, maxRows);
            var first = ordered[0];
            groups.Add(new StandingsClassGroup
            {
                ClassId = g.Key,
                ClassName = first.ClassName,
                ClassColorHex = first.ClassColorHex,
                Sof = classSof,
                TotalDrivers = ordered.Count,
                Drivers = displayed
            });
        }

        return groups;
    }

    private static List<StandingsEntry> SelectStandingsSlice(List<StandingsEntry> ordered, int maxRows)
    {
        if (ordered.Count <= maxRows) return ordered;

        int playerIndex = ordered.FindIndex(d => d.IsPlayer);
        if (playerIndex < 0 || playerIndex < maxRows)
            return ordered.Take(maxRows).ToList();

        // Show top 3 + slice centered around player
        var result = new List<StandingsEntry>(maxRows);
        int topCount = Math.Min(3, maxRows - 3);
        for (int i = 0; i < topCount; i++)
            result.Add(ordered[i]);

        int remaining = maxRows - topCount;
        int start = Math.Clamp(playerIndex - remaining / 2, topCount, ordered.Count - remaining);
        for (int i = start; i < start + remaining && i < ordered.Count; i++)
            result.Add(ordered[i]);

        return result;
    }

    private List<RelativeEntry> BuildRelative(int playerIdx, int eachSide)
    {
        var result = new List<RelativeEntry>();
        if (playerIdx < 0 || playerIdx >= MaxCars) return result;

        float playerPct = _carLapDistPct[playerIdx];
        if (playerPct < 0f) return result;

        float estLap = Math.Max(20f, _yamlMeta.DriverCarEstLapTime);
        float playerEst = _carEstTime[playerIdx];
        float playerProgress = _carLap[playerIdx] + playerPct;
        float playerLastLap = _carLastLap[playerIdx];

        var candidates = new List<RelativeEntry>();

        foreach (var kv in _yamlMeta.DriversByIdx)
        {
            var meta = kv.Value;
            int idx = meta.CarIdx;
            if (idx < 0 || idx >= MaxCars || meta.CarIsPaceCar == 1 || meta.IsSpectator == 1)
                continue;

            float pct = _carLapDistPct[idx];
            if (pct < 0f || _carTrackSurface[idx] == -1) continue;

            float gapSec;
            if (idx == playerIdx)
            {
                gapSec = 0f;
            }
            else if (playerEst > 0f && _carEstTime[idx] > 0f)
            {
                gapSec = _carEstTime[idx] - playerEst;
                while (gapSec > estLap * 0.5f) gapSec -= estLap;
                while (gapSec < -estLap * 0.5f) gapSec += estLap;
            }
            else
            {
                float dPct = pct - playerPct;
                if (dPct > 0.5f) dPct -= 1f;
                if (dPct < -0.5f) dPct += 1f;
                gapSec = dPct * estLap;
            }

            float diffLaps = (_carLap[idx] + pct) - playerProgress;
            LapRelation lapRel = LapRelation.SameLap;
            if (idx != playerIdx)
            {
                if (diffLaps > 0.5f) lapRel = LapRelation.LapAhead;
                else if (diffLaps < -0.5f) lapRel = LapRelation.LapDown;
            }

            float carLast = _carLastLap[idx] > 10f ? _carLastLap[idx] : 0f;
            bool hasPace = idx != playerIdx && carLast > 10f && playerLastLap > 10f;
            float paceDelta = hasPace ? (carLast - playerLastLap) : 0f; // + car is slower than you, - car is faster
            bool isOutLap = !_carOnPitRoad[idx] && _lastPitLap[idx] > 0 && _carLap[idx] == _lastPitLap[idx] && pct < 0.85f;

            candidates.Add(new RelativeEntry
            {
                CarIdx = idx,
                ClassPosition = _carClassPosition[idx] > 0 ? _carClassPosition[idx] : _carPosition[idx],
                CarNumber = meta.CarNumber,
                Brand = meta.Brand,
                DriverName = !string.IsNullOrWhiteSpace(meta.UserName) ? meta.UserName : meta.AbbrevName,
                ClassColorHex = meta.CarClassColorHex,
                LicenseText = meta.LicString,
                LicenseColorHex = meta.LicColorHex,
                IRating = meta.IRating,
                GapSeconds = gapSec,
                LastLapSeconds = carLast,
                PaceDeltaVsPlayerSec = paceDelta,
                HasPaceDelta = hasPace,
                IsPlayer = idx == playerIdx,
                IsOnPitRoad = _carOnPitRoad[idx],
                IsOutLap = isOutLap,
                IsOffTrack = _carTrackSurface[idx] == 0,
                LapRelation = lapRel
            });
        }

        var ahead = candidates
            .Where(c => !c.IsPlayer && c.GapSeconds >= 0f)
            .OrderBy(c => c.GapSeconds)
            .Take(eachSide)
            .Reverse()
            .ToList();

        var player = candidates.FirstOrDefault(c => c.IsPlayer);

        var behind = candidates
            .Where(c => !c.IsPlayer && c.GapSeconds < 0f)
            .OrderByDescending(c => c.GapSeconds)
            .Take(eachSide)
            .ToList();

        result.AddRange(ahead);
        if (player != null) result.Add(player);
        result.AddRange(behind);
        return result;
    }

    private RadarTelemetry BuildRadar(int playerIdx)
    {
        var radar = new RadarTelemetry
        {
            Proximity = (CarProximityState)ReadInt("CarLeftRight", 1)
        };

        if (playerIdx < 0 || playerIdx >= MaxCars) return radar;
        float playerPct = _carLapDistPct[playerIdx];
        if (playerPct < 0f) return radar;

        float trackLen = Math.Max(800f, _yamlMeta.TrackLengthMeters);
        float nearestAhead = 999f;
        float nearestBehind = 999f;

        foreach (var kv in _yamlMeta.DriversByIdx)
        {
            var meta = kv.Value;
            int idx = meta.CarIdx;
            if (idx == playerIdx || idx < 0 || idx >= MaxCars || meta.CarIsPaceCar == 1) continue;
            if (_carTrackSurface[idx] <= 0 || _carOnPitRoad[idx]) continue;

            float pct = _carLapDistPct[idx];
            if (pct < 0f) continue;

            float dPct = pct - playerPct;
            if (dPct > 0.5f) dPct -= 1f;
            if (dPct < -0.5f) dPct += 1f;

            float distMeters = dPct * trackLen;
            if (distMeters >= 0f && distMeters < nearestAhead) nearestAhead = distMeters;
            if (distMeters < 0f && Math.Abs(distMeters) < nearestBehind) nearestBehind = Math.Abs(distMeters);

            if (Math.Abs(distMeters) <= 18f)
            {
                float lat = 0f;
                if (Math.Abs(distMeters) <= 6.5f)
                {
                    if (radar.Proximity is CarProximityState.CarLeft or CarProximityState.TwoCarsLeft) lat = -2.6f;
                    else if (radar.Proximity is CarProximityState.CarRight or CarProximityState.TwoCarsRight) lat = 2.6f;
                    else if (radar.Proximity == CarProximityState.CarLeftRight) lat = (idx % 2 == 0) ? -2.6f : 2.6f;
                }
                radar.Blips.Add(new RadarBlip
                {
                    OffsetX = lat,
                    OffsetY = distMeters,
                    ColorHex = meta.CarClassColorHex
                });
            }
        }

        radar.NearestAheadMeters = nearestAhead;
        radar.NearestBehindMeters = nearestBehind;
        return radar;
    }

    private float ReadFloat(string name, float fallback = 0f)
    {
        if (!_vars.TryGetValue(name, out var h) || h.Offset + 4 > _frameBuffer.Length) return fallback;
        return h.Type switch
        {
            4 => BitConverter.ToSingle(_frameBuffer, h.Offset),
            5 when h.Offset + 8 <= _frameBuffer.Length => (float)BitConverter.ToDouble(_frameBuffer, h.Offset),
            2 => BitConverter.ToInt32(_frameBuffer, h.Offset),
            _ => fallback
        };
    }

    private double ReadDouble(string name, double fallback = 0.0)
    {
        if (!_vars.TryGetValue(name, out var h)) return fallback;
        if (h.Type == 5 && h.Offset + 8 <= _frameBuffer.Length)
            return BitConverter.ToDouble(_frameBuffer, h.Offset);
        if (h.Type == 4 && h.Offset + 4 <= _frameBuffer.Length)
            return BitConverter.ToSingle(_frameBuffer, h.Offset);
        return fallback;
    }

    private int ReadInt(string name, int fallback = 0)
    {
        if (!_vars.TryGetValue(name, out var h) || h.Offset + 4 > _frameBuffer.Length) return fallback;
        return BitConverter.ToInt32(_frameBuffer, h.Offset);
    }

    private uint ReadUInt(string name, uint fallback = 0)
    {
        if (!_vars.TryGetValue(name, out var h) || h.Offset + 4 > _frameBuffer.Length) return fallback;
        return BitConverter.ToUInt32(_frameBuffer, h.Offset);
    }

    private bool ReadBool(string name, bool fallback = false)
    {
        if (!_vars.TryGetValue(name, out var h) || h.Offset >= _frameBuffer.Length) return fallback;
        return _frameBuffer[h.Offset] != 0;
    }

    private void ReadFloatArray(string name, float[] dest)
    {
        Array.Fill(dest, -1f);
        if (!_vars.TryGetValue(name, out var h)) return;
        int n = Math.Min(dest.Length, h.Count);
        for (int i = 0; i < n; i++)
        {
            int off = h.Offset + i * 4;
            if (off + 4 <= _frameBuffer.Length)
                dest[i] = BitConverter.ToSingle(_frameBuffer, off);
        }
    }

    private void ReadIntArray(string name, int[] dest)
    {
        Array.Clear(dest);
        if (!_vars.TryGetValue(name, out var h)) return;
        int n = Math.Min(dest.Length, h.Count);
        for (int i = 0; i < n; i++)
        {
            int off = h.Offset + i * 4;
            if (off + 4 <= _frameBuffer.Length)
                dest[i] = BitConverter.ToInt32(_frameBuffer, off);
        }
    }

    private void ReadBoolArray(string name, bool[] dest)
    {
        Array.Clear(dest);
        if (!_vars.TryGetValue(name, out var h)) return;
        int n = Math.Min(dest.Length, h.Count);
        for (int i = 0; i < n; i++)
        {
            int off = h.Offset + i;
            if (off < _frameBuffer.Length)
                dest[i] = _frameBuffer[off] != 0;
        }
    }

    private static (string Code, string ColorHex) MapTireCompound(int compound) => compound switch
    {
        0 => ("D", "#CBD5E1"), // Dry / Primary
        1 => ("W", "#38BDF8"), // Wet
        2 => ("S", "#EF4444"), // Soft
        3 => ("M", "#EAB308"), // Medium
        4 => ("H", "#F8FAFC"), // Hard
        _ => ("D", "#94A3B8")
    };

    public void Dispose() => Disconnect();
}
