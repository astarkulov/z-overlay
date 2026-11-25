using System;
using System.Collections.Generic;

namespace ApexHUD.Core.Telemetry;

/// <summary>
/// Racelab-style 4-corner apex minimum speed tracker:
/// Tracks player's last 4 corners AND benchmarks each corner against the fastest driver's
/// minimum speed in the same track sector (LapDistPct bucket) during the session.
/// </summary>
public sealed class CornerSpeedTracker
{
    private const int TrackBuckets = 100; // 1% track-length resolution
    private const int MaxHistory = 4;

    private readonly float[] _prevCarPct = new float[64];
    private readonly float[] _fastestSectorMinMs = new float[TrackBuckets];
    private readonly List<CornerSpeedRecord> _history = new(MaxHistory);

    private double _lastUpdateSec = -1;
    private int _fastestCarIdx = -1;
    private float _fastestClassLapSec = float.MaxValue;

    private bool _inCorner;
    private float _curCornerMinMs = float.MaxValue;
    private float _curCornerApexPct;
    private float _recentMaxMs;
    private int _cornerCounter;

    public CornerSpeedTracker()
    {
        Reset();
    }

    public void Reset()
    {
        Array.Fill(_prevCarPct, -1f);
        Array.Fill(_fastestSectorMinMs, 0f);
        _history.Clear();
        _lastUpdateSec = -1;
        _fastestCarIdx = -1;
        _fastestClassLapSec = float.MaxValue;
        _inCorner = false;
        _curCornerMinMs = float.MaxValue;
        _recentMaxMs = 0f;
        _cornerCounter = 0;
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
        Dictionary<int, YamlDriverMeta> drivers)
    {
        double dt = _lastUpdateSec > 0 ? sessionTimeSec - _lastUpdateSec : 0.016;
        _lastUpdateSec = sessionTimeSec;

        // 1) Identify fastest driver in player's class and sample their speed per track sector
        if (dt is > 0.005 and < 0.25 && trackLengthMeters > 400f)
        {
            foreach (var kv in drivers)
            {
                var meta = kv.Value;
                int idx = meta.CarIdx;
                if (idx < 0 || idx >= 64 || meta.CarIsPaceCar == 1 || meta.CarClassId != playerClassId) continue;

                float bl = carBestLap[idx];
                if (bl > 10f && bl < _fastestClassLapSec)
                {
                    _fastestClassLapSec = bl;
                    if (_fastestCarIdx != idx)
                    {
                        _fastestCarIdx = idx;
                        Array.Fill(_fastestSectorMinMs, 0f);
                    }
                }
            }

            int refIdx = _fastestCarIdx >= 0 ? _fastestCarIdx : playerIdx;
            if (refIdx >= 0 && refIdx < 64 && carTrackSurface[refIdx] == 3 && !carOnPitRoad[refIdx])
            {
                float curPct = carLapDistPct[refIdx];
                float prevPct = _prevCarPct[refIdx];
                if (curPct >= 0f && prevPct >= 0f)
                {
                    float dPct = curPct - prevPct;
                    if (dPct < -0.5f) dPct += 1f;
                    if (dPct is > 0f and < 0.05f)
                    {
                        float estSpeedMs = (float)(dPct * trackLengthMeters / dt);
                        if (estSpeedMs is > 8f and < 115f)
                        {
                            int bucket = Math.Clamp((int)(curPct * TrackBuckets) % TrackBuckets, 0, TrackBuckets - 1);
                            float existing = _fastestSectorMinMs[bucket];
                            // Keep the highest-pace minimum speed seen cleanly at that sector
                            if (existing <= 0.1f)
                                _fastestSectorMinMs[bucket] = estSpeedMs;
                            else
                                _fastestSectorMinMs[bucket] = existing * 0.7f + estSpeedMs * 0.3f;
                        }
                    }
                }
                _prevCarPct[refIdx] = curPct;
            }
        }

        // 2) Track player's corners (last 4 corners)
        if (playerOnTrack && playerSpeedMs > 5.5f)
        {
            if (playerSpeedMs > _recentMaxMs) _recentMaxMs = playerSpeedMs;
            else _recentMaxMs = Math.Max(playerSpeedMs, _recentMaxMs - 0.06f);

            float steerDegAbs = Math.Abs(playerSteerRad * (180f / (float)Math.PI));
            bool entering = playerBrake > 0.08f || (steerDegAbs > 24f && playerSpeedMs < _recentMaxMs - 2.5f);

            if (!_inCorner)
            {
                if (entering)
                {
                    _inCorner = true;
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

                bool exiting = playerBrake < 0.03f && playerThrottle > 0.25f && playerSpeedMs > _curCornerMinMs + 1.2f;
                if (exiting)
                {
                    _cornerCounter++;
                    float refMin = LookupSectorFastestMinMs(_curCornerApexPct);
                    _history.Add(new CornerSpeedRecord
                    {
                        Label = $"T{((_cornerCounter - 1) % 16) + 1}",
                        PlayerMinMs = _curCornerMinMs,
                        SessionFastestMinMs = refMin > 5f ? refMin : _curCornerMinMs,
                        HasFastestRef = refMin > 5f,
                        IsLiveCorner = false
                    });
                    while (_history.Count > MaxHistory)
                        _history.RemoveAt(0);

                    _inCorner = false;
                    _recentMaxMs = playerSpeedMs;
                }
            }
        }
        else
        {
            _inCorner = false;
        }

        // Return up to 4 records (if currently mid-corner, include live corner as the rightmost 4th slot)
        var result = new List<CornerSpeedRecord>(MaxHistory);
        if (_inCorner && _curCornerMinMs < 150f)
        {
            int takePrev = Math.Min(_history.Count, MaxHistory - 1);
            for (int i = _history.Count - takePrev; i < _history.Count; i++)
                result.Add(_history[i]);

            float refMin = LookupSectorFastestMinMs(_curCornerApexPct);
            result.Add(new CornerSpeedRecord
            {
                Label = "NOW",
                PlayerMinMs = _curCornerMinMs,
                SessionFastestMinMs = refMin > 5f ? refMin : _curCornerMinMs,
                HasFastestRef = refMin > 5f,
                IsLiveCorner = true
            });
        }
        else
        {
            result.AddRange(_history);
        }

        return result;
    }

    private float LookupSectorFastestMinMs(float apexPct)
    {
        if (apexPct < 0f) return 0f;
        int center = Math.Clamp((int)(apexPct * TrackBuckets) % TrackBuckets, 0, TrackBuckets - 1);
        float minVal = float.MaxValue;
        for (int offset = -2; offset <= 2; offset++)
        {
            int b = (center + offset + TrackBuckets) % TrackBuckets;
            float v = _fastestSectorMinMs[b];
            if (v > 5f && v < minVal)
                minVal = v;
        }
        return minVal < float.MaxValue ? minVal : 0f;
    }
}
