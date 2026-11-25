using System;
using System.Collections.Generic;

namespace ApexHUD.Core.Telemetry;

/// <summary>
/// Tracks the player's last 4 actually driven corners (NEWEST FIRST: index 0 = live / most recent),
/// auto-numbers discovered corners (T1, T2, ...) in track order around the lap,
/// and benchmarks each corner against:
///   - White:  player's current / last corner minimum speed
///   - Green:  player's personal best (highest) minimum speed in this corner
///   - Purple: minimum speed of the EXACT driver who holds the fastest lap in the player's class
/// </summary>
public sealed class CornerSpeedTracker
{
    private const int TrackBuckets = 100; // 1% track-length resolution
    private const int MaxCars = 64;
    private const int MaxHistory = 4;

    private readonly float[] _prevCarPct = new float[MaxCars];
    private readonly float[] _prevCarBestLap = new float[MaxCars];
    private readonly float[,] _carCurLapSectorMs = new float[MaxCars, TrackBuckets];
    private readonly float[,] _carBestLapSectorMs = new float[MaxCars, TrackBuckets];
    private readonly float[] _playerSectorBestMs = new float[TrackBuckets];

    private readonly List<float> _knownApexPcts = new();
    private readonly List<(float ApexPct, CornerSpeedRecord Record)> _history = new(MaxHistory);

    private double _lastUpdateSec = -1;
    private int _fastestCarIdx = -1;
    private float _fastestClassLapSec = float.MaxValue;

    private bool _inCorner;
    private float _entrySpeedMs;
    private float _curCornerMinMs = float.MaxValue;
    private float _curCornerApexPct;
    private float _recentMaxMs;

    public CornerSpeedTracker()
    {
        Reset();
    }

    public void Reset()
    {
        Array.Fill(_prevCarPct, -1f);
        Array.Fill(_prevCarBestLap, float.MaxValue);
        Array.Clear(_carCurLapSectorMs);
        Array.Clear(_carBestLapSectorMs);
        Array.Fill(_playerSectorBestMs, 0f);
        _knownApexPcts.Clear();
        _history.Clear();
        _lastUpdateSec = -1;
        _fastestCarIdx = -1;
        _fastestClassLapSec = float.MaxValue;
        _inCorner = false;
        _entrySpeedMs = 0f;
        _curCornerMinMs = float.MaxValue;
        _recentMaxMs = 0f;
    }

    public List<CornerSpeedRecord> Update(
        double sessionTimeSec,
        int playerIdx,
        int playerClassId,
        float playerSpeedMs,
        float playerBrake,
        float playerThrottle,
        float playerSteerRad,
        float playerLapDistPct,
        bool playerOnTrack,
        float trackLengthMeters,
        float[] carLapDistPct,
        float[] carBestLap,
        int[] carTrackSurface,
        bool[] carOnPitRoad,
        Dictionary<int, YamlDriverMeta> drivers,
        Dictionary<int, YamlSessionResult>? yamlResults = null)
    {
        double dt = _lastUpdateSec > 0 ? sessionTimeSec - _lastUpdateSec : 0.016;
        _lastUpdateSec = sessionTimeSec;

        // 1) Track EVERY car in the player's class so we always have the exact fastest-lap holder's corner speeds
        if (dt is > 0.005 and < 0.25 && trackLengthMeters > 400f)
        {
            foreach (var kv in drivers)
            {
                var meta = kv.Value;
                int idx = meta.CarIdx;
                if (idx < 0 || idx >= MaxCars || meta.CarIsPaceCar == 1 || meta.IsSpectator == 1 || meta.CarClassId != playerClassId)
                    continue;

                // Resolve best lap from live telemetry OR YAML session results (works even when car is in pits)
                float bl = carBestLap[idx];
                if (bl <= 10f && yamlResults != null && yamlResults.TryGetValue(idx, out var yr) && yr.FastestTime > 10f)
                    bl = yr.FastestTime;

                // If this car just beat its own best lap, snapshot its current-lap sector speeds as its best-lap trace!
                if (bl > 10f && bl < _prevCarBestLap[idx] - 0.005f)
                {
                    _prevCarBestLap[idx] = bl;
                    for (int b = 0; b < TrackBuckets; b++)
                    {
                        float cur = _carCurLapSectorMs[idx, b];
                        if (cur > 5f) _carBestLapSectorMs[idx, b] = cur;
                    }
                }

                // Track the exact holder of the class fastest lap
                if (bl > 10f && bl < _fastestClassLapSec - 0.001f)
                {
                    _fastestClassLapSec = bl;
                    _fastestCarIdx = idx;
                }

                // Sample live speed per 1% track bucket while on track
                if (carTrackSurface[idx] == 3 && !carOnPitRoad[idx])
                {
                    float curPct = carLapDistPct[idx];
                    float prevPct = _prevCarPct[idx];
                    if (curPct >= 0f && prevPct >= 0f)
                    {
                        float dPct = curPct - prevPct;
                        if (dPct < -0.5f)
                        {
                            // Crossed Start/Finish -> start fresh current-lap trace (keep best-lap trace intact!)
                            dPct += 1f;
                            for (int b = 0; b < TrackBuckets; b++)
                                _carCurLapSectorMs[idx, b] = 0f;
                        }

                        if (dPct is > 0f and < 0.05f)
                        {
                            float estSpeedMs = idx == playerIdx && playerOnTrack
                                ? playerSpeedMs
                                : (float)(dPct * trackLengthMeters / dt);
                            if (estSpeedMs is > 6f and < 115f)
                            {
                                int bucket = Math.Clamp((int)(curPct * TrackBuckets) % TrackBuckets, 0, TrackBuckets - 1);
                                float existing = _carCurLapSectorMs[idx, bucket];
                                _carCurLapSectorMs[idx, bucket] = existing <= 0.1f
                                    ? estSpeedMs
                                    : Math.Min(existing, estSpeedMs);
                            }
                        }
                    }
                    _prevCarPct[idx] = curPct;
                }
                else
                {
                    _prevCarPct[idx] = -1f;
                }
            }
        }

        // 2) Track real corners driven by the player (requires genuine >= 12 km/h drop from entry)
        if (playerOnTrack && playerSpeedMs > 5.5f)
        {
            if (playerSpeedMs > _recentMaxMs) _recentMaxMs = playerSpeedMs;
            else _recentMaxMs = Math.Max(playerSpeedMs, _recentMaxMs - 0.04f);

            float steerDegAbs = Math.Abs(playerSteerRad * (180f / (float)Math.PI));
            bool entering = playerBrake > 0.10f || (steerDegAbs > 26f && playerSpeedMs < _recentMaxMs - 3.5f);

            if (!_inCorner)
            {
                if (entering)
                {
                    _inCorner = true;
                    _entrySpeedMs = Math.Max(_recentMaxMs, playerSpeedMs);
                    _curCornerMinMs = playerSpeedMs;
                    _curCornerApexPct = playerLapDistPct;
                }
            }
            else
            {
                if (playerSpeedMs < _curCornerMinMs)
                {
                    _curCornerMinMs = playerSpeedMs;
                    _curCornerApexPct = playerLapDistPct;
                }

                bool exiting = playerBrake < 0.03f && playerThrottle > 0.30f && playerSpeedMs > _curCornerMinMs + 1.8f;
                if (exiting)
                {
                    if (_entrySpeedMs - _curCornerMinMs >= 3.5f)
                    {
                        string turnLabel = RegisterAndGetTurnLabel(_curCornerApexPct);

                        float prevPb = LookupPlayerBestMs(_curCornerApexPct);
                        float newPb = Math.Max(prevPb, _curCornerMinMs);
                        RecordPlayerBestMs(_curCornerApexPct, newPb);

                        float refFast = LookupFastestDriverCornerMs(_curCornerApexPct, playerIdx);

                        _history.Insert(0, (_curCornerApexPct, new CornerSpeedRecord
                        {
                            Label = turnLabel,
                            PlayerMinMs = _curCornerMinMs,
                            PlayerBestMinMs = newPb,
                            SessionFastestMinMs = refFast,
                            HasBestRef = true,
                            HasFastestRef = refFast > 5f,
                            IsLiveCorner = false
                        }));
                        while (_history.Count > MaxHistory)
                            _history.RemoveAt(_history.Count - 1);

                        RefreshHistoryLabelsAndRefs(playerIdx);
                    }

                    _inCorner = false;
                    _recentMaxMs = playerSpeedMs;
                }
            }
        }
        else
        {
            _inCorner = false;
        }

        // Return NEWEST FIRST (slot 0 = live corner or corner you just exited, slots 1..3 = older)
        var result = new List<CornerSpeedRecord>(MaxHistory);
        if (_inCorner && _curCornerMinMs < 150f && (_entrySpeedMs - _curCornerMinMs >= 2.5f))
        {
            string liveLabel = LookupNearestTurnLabel(_curCornerApexPct);
            float pb = LookupPlayerBestMs(_curCornerApexPct);
            float refFast = LookupFastestDriverCornerMs(_curCornerApexPct, playerIdx);
            result.Add(new CornerSpeedRecord
            {
                Label = liveLabel,
                PlayerMinMs = _curCornerMinMs,
                PlayerBestMinMs = pb > 5f ? pb : _curCornerMinMs,
                SessionFastestMinMs = refFast,
                HasBestRef = pb > 5f,
                HasFastestRef = refFast > 5f,
                IsLiveCorner = true
            });
            for (int i = 0; i < _history.Count && result.Count < MaxHistory; i++)
                result.Add(_history[i].Record);
        }
        else
        {
            for (int i = 0; i < _history.Count; i++)
                result.Add(_history[i].Record);
        }

        return result;
    }

    private string RegisterAndGetTurnLabel(float apexPct)
    {
        if (apexPct < 0f) return "T?";
        apexPct = ((apexPct % 1f) + 1f) % 1f;

        // Match existing discovered corner within +-2.6% lap distance
        for (int i = 0; i < _knownApexPcts.Count; i++)
        {
            float dist = CircularDist(apexPct, _knownApexPcts[i]);
            if (dist <= 0.026f)
            {
                // Smoothly refine stored apex center
                _knownApexPcts[i] = _knownApexPcts[i] * 0.7f + apexPct * 0.3f;
                return $"T{i + 1}";
            }
        }

        // Insert in ascending track order so T1..Tn always match real turn sequence from Start/Finish
        int insertIdx = 0;
        while (insertIdx < _knownApexPcts.Count && _knownApexPcts[insertIdx] < apexPct)
            insertIdx++;
        _knownApexPcts.Insert(insertIdx, apexPct);
        return $"T{insertIdx + 1}";
    }

    private string LookupNearestTurnLabel(float apexPct)
    {
        if (apexPct < 0f) return "T?";
        apexPct = ((apexPct % 1f) + 1f) % 1f;

        for (int i = 0; i < _knownApexPcts.Count; i++)
        {
            if (CircularDist(apexPct, _knownApexPcts[i]) <= 0.032f)
                return $"T{i + 1}";
        }

        // Estimate turn index in sorted order for a brand-new corner on Lap 1
        int rank = 0;
        while (rank < _knownApexPcts.Count && _knownApexPcts[rank] < apexPct)
            rank++;
        return $"T{rank + 1}";
    }

    private void RefreshHistoryLabelsAndRefs(int playerIdx)
    {
        for (int i = 0; i < _history.Count; i++)
        {
            var (pct, rec) = _history[i];
            rec.Label = LookupNearestTurnLabel(pct);
            float fast = LookupFastestDriverCornerMs(pct, playerIdx);
            if (fast > 5f)
            {
                rec.SessionFastestMinMs = fast;
                rec.HasFastestRef = true;
            }
        }
    }

    private static float CircularDist(float a, float b)
    {
        float d = Math.Abs(a - b);
        return d > 0.5f ? 1f - d : d;
    }

    private void RecordPlayerBestMs(float apexPct, float bestMs)
    {
        if (apexPct < 0f) return;
        int b = Math.Clamp((int)(apexPct * TrackBuckets) % TrackBuckets, 0, TrackBuckets - 1);
        if (bestMs > _playerSectorBestMs[b])
            _playerSectorBestMs[b] = bestMs;
    }

    private float LookupPlayerBestMs(float apexPct)
    {
        if (apexPct < 0f) return 0f;
        int center = Math.Clamp((int)(apexPct * TrackBuckets) % TrackBuckets, 0, TrackBuckets - 1);
        float maxVal = 0f;
        for (int offset = -2; offset <= 2; offset++)
        {
            int b = (center + offset + TrackBuckets) % TrackBuckets;
            float v = _playerSectorBestMs[b];
            if (v > maxVal) maxVal = v;
        }
        return maxVal;
    }

    /// <summary>
    /// Looks up the minimum speed in this corner of the EXACT driver who holds the class fastest lap.
    /// Prefers their recorded fastest-lap trace, falling back to their live on-track sector trace.
    /// </summary>
    private float LookupFastestDriverCornerMs(float apexPct, int playerIdx)
    {
        if (apexPct < 0f) return 0f;
        int targetIdx = _fastestCarIdx >= 0 ? _fastestCarIdx : playerIdx;
        if (targetIdx < 0 || targetIdx >= MaxCars) return 0f;

        int center = Math.Clamp((int)(apexPct * TrackBuckets) % TrackBuckets, 0, TrackBuckets - 1);

        // 1) Prefer the fastest-lap holder's snapshotted fastest-lap speed in this corner
        float bestLapMin = float.MaxValue;
        for (int offset = -2; offset <= 2; offset++)
        {
            int b = (center + offset + TrackBuckets) % TrackBuckets;
            float v = _carBestLapSectorMs[targetIdx, b];
            if (v > 5f && v < bestLapMin) bestLapMin = v;
        }
        if (bestLapMin < float.MaxValue) return bestLapMin;

        // 2) Fallback: fastest-lap holder's current-lap observed speed in this corner
        float curLapMin = float.MaxValue;
        for (int offset = -2; offset <= 2; offset++)
        {
            int b = (center + offset + TrackBuckets) % TrackBuckets;
            float v = _carCurLapSectorMs[targetIdx, b];
            if (v > 5f && v < curLapMin) curLapMin = v;
        }
        return curLapMin < float.MaxValue ? curLapMin : 0f;
    }
}
