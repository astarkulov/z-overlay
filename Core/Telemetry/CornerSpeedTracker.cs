using System;
using System.Collections.Generic;

namespace ApexHUD.Core.Telemetry;

/// <summary>
/// Tracks the player's last 4 actually driven corners (ordered NEWEST FIRST: index 0 = most recent / live)
/// and benchmarks each corner against the fastest driver's minimum speed in the same track sector.
/// </summary>
public sealed class CornerSpeedTracker
{
    private const int TrackBuckets = 100; // 1% track-length resolution
    private const int MaxHistory = 4;

    private readonly float[] _prevCarPct = new float[64];
    private readonly float[] _playerSectorBestMs = new float[TrackBuckets];
    private readonly float[] _fastestSectorMinMs = new float[TrackBuckets];
    private readonly List<CornerSpeedRecord> _history = new(MaxHistory);

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
        Array.Fill(_playerSectorBestMs, 0f);
        Array.Fill(_fastestSectorMinMs, 0f);
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
                            _fastestSectorMinMs[bucket] = existing <= 0.1f
                                ? estSpeedMs
                                : existing * 0.7f + estSpeedMs * 0.3f;
                        }
                    }
                }
                _prevCarPct[refIdx] = curPct;
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
                    // Only record genuine corners (at least ~12.5 km/h drop from entry speed)
                    if (_entrySpeedMs - _curCornerMinMs >= 3.5f)
                    {
                        float prevPb = LookupPlayerBestMs(_curCornerApexPct);
                        float newPb = Math.Max(prevPb, _curCornerMinMs);
                        RecordPlayerBestMs(_curCornerApexPct, newPb);

                        float refFast = LookupSectorFastestMinMs(_curCornerApexPct);
                        if (refFast > 5f && newPb > refFast) refFast = newPb;

                        _history.Insert(0, new CornerSpeedRecord
                        {
                            Label = "",
                            PlayerMinMs = _curCornerMinMs,
                            PlayerBestMinMs = newPb,
                            SessionFastestMinMs = refFast > 5f ? refFast : newPb,
                            HasBestRef = true,
                            HasFastestRef = refFast > 5f,
                            IsLiveCorner = false
                        });
                        while (_history.Count > MaxHistory)
                            _history.RemoveAt(_history.Count - 1);
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
            float pb = LookupPlayerBestMs(_curCornerApexPct);
            float refFast = LookupSectorFastestMinMs(_curCornerApexPct);
            result.Add(new CornerSpeedRecord
            {
                Label = "LIVE",
                PlayerMinMs = _curCornerMinMs,
                PlayerBestMinMs = pb > 5f ? pb : _curCornerMinMs,
                SessionFastestMinMs = refFast > 5f ? refFast : (pb > 5f ? pb : _curCornerMinMs),
                HasBestRef = pb > 5f,
                HasFastestRef = refFast > 5f,
                IsLiveCorner = true
            });
            for (int i = 0; i < _history.Count && result.Count < MaxHistory; i++)
                result.Add(_history[i]);
        }
        else
        {
            result.AddRange(_history);
        }

        return result;
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
