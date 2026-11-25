using System;
using System.Collections.Generic;
using System.Linq;

namespace ApexHUD.Core.Telemetry;

/// <summary>
/// Tracks per-lap fuel burn accurately, rejecting pit refuel spikes and partial laps.
/// </summary>
public sealed class FuelTracker
{
    private readonly Queue<float> _recentLapBurns = new();
    private const int WindowSize = 5;

    private int _lastLap = -1;
    private float _lapStartFuel = -1f;
    private float _lastFuelReading = -1f;
    private bool _pittedOnCurrentLap;

    public float LastLapBurn { get; private set; }
    public float MaxLapBurn { get; private set; }
    public float AvgLapBurn => _recentLapBurns.Count > 0 ? _recentLapBurns.Average() : 0f;
    public bool HasReliableBurn => _recentLapBurns.Count > 0;

    public void Reset()
    {
        _recentLapBurns.Clear();
        _lastLap = -1;
        _lapStartFuel = -1f;
        _lastFuelReading = -1f;
        _pittedOnCurrentLap = false;
        LastLapBurn = 0f;
        MaxLapBurn = 0f;
    }

    public FuelTelemetry Update(
        float fuelLiters,
        float fuelMaxLiters,
        int currentLap,
        float lapDistPct,
        bool isOnTrack,
        bool isOnPitRoad,
        int sessionLapsRemain,
        double sessionTimeRemainSec,
        float estLapTimeSec,
        double safetyMarginLaps,
        string calcMode = "Avg")
    {
        if (isOnPitRoad)
            _pittedOnCurrentLap = true;

        // Detect refuel spike (reset lap baseline)
        if (_lastFuelReading > 0f && fuelLiters > _lastFuelReading + 0.5f)
        {
            _pittedOnCurrentLap = true;
            _lapStartFuel = fuelLiters;
        }
        _lastFuelReading = fuelLiters;

        if (isOnTrack && currentLap >= 0)
        {
            if (_lastLap == -1)
            {
                _lastLap = currentLap;
                _lapStartFuel = fuelLiters;
                _pittedOnCurrentLap = isOnPitRoad;
            }
            else if (currentLap > _lastLap)
            {
                if (!_pittedOnCurrentLap && _lapStartFuel > 0f)
                {
                    float burned = _lapStartFuel - fuelLiters;
                    if (burned > 0.15f && burned < 25f)
                    {
                        LastLapBurn = burned;
                        if (burned > MaxLapBurn) MaxLapBurn = burned;
                        _recentLapBurns.Enqueue(burned);
                        while (_recentLapBurns.Count > WindowSize)
                            _recentLapBurns.Dequeue();
                    }
                }

                _lastLap = currentLap;
                _lapStartFuel = fuelLiters;
                _pittedOnCurrentLap = isOnPitRoad;
            }
            else if (currentLap < _lastLap)
            {
                // New session started
                Reset();
                _lastLap = currentLap;
                _lapStartFuel = fuelLiters;
            }
        }

        // Choose burn rate based on Racelab strategy mode (Avg / Last / Max)
        float baseAvg = AvgLapBurn;
        float burn = calcMode switch
        {
            "Last" when LastLapBurn > 0.05f => LastLapBurn,
            "Max" when MaxLapBurn > 0.05f => MaxLapBurn,
            _ => baseAvg
        };
        if (burn <= 0.05f && _lapStartFuel > fuelLiters && lapDistPct > 0.15f)
        {
            float liveEst = (_lapStartFuel - fuelLiters) / lapDistPct;
            if (liveEst > 0.15f && liveEst < 25f)
                burn = liveEst;
        }

        float lapsRemaining = EstimateRaceLapsRemaining(sessionLapsRemain, sessionTimeRemainSec, estLapTimeSec, lapDistPct);
        float estLapsOnFuel = burn > 0.05f ? fuelLiters / burn : 0f;
        float neededMin = burn > 0.05f ? lapsRemaining * burn : 0f;
        float neededTarget = burn > 0.05f ? (lapsRemaining + (float)safetyMarginLaps) * burn : 0f;
        float neededSafe = burn > 0.05f ? (lapsRemaining + (float)safetyMarginLaps + 1.0f) * burn : 0f;

        float toAddMin = Math.Max(0f, neededMin - fuelLiters);
        float fuelToAdd = Math.Max(0f, neededTarget - fuelLiters);
        float toAddSafe = Math.Max(0f, neededSafe - fuelLiters);
        float surplus = fuelLiters - neededMin;
        float targetBurn = lapsRemaining > 0.25f ? fuelLiters / lapsRemaining : burn;
        int pitStopsRem = fuelToAdd <= 0.1f ? 0 : (int)Math.Ceiling(fuelToAdd / Math.Max(15f, fuelMaxLiters));
        float estRefuelSec = fuelToAdd > 0.1f ? fuelToAdd / 2.4f : 0f;

        return new FuelTelemetry
        {
            FuelLevelLiters = fuelLiters,
            FuelMaxLiters = Math.Max(1f, fuelMaxLiters),
            FuelPct = fuelMaxLiters > 0.1f ? Math.Clamp(fuelLiters / fuelMaxLiters, 0f, 1f) : 0f,
            LastLapBurn = LastLapBurn,
            AvgLapBurn = baseAvg > 0.05f ? baseAvg : burn,
            MaxLapBurn = MaxLapBurn,
            EstLapsOnFuel = estLapsOnFuel,
            RaceLapsRemaining = lapsRemaining,
            FuelToAddLiters = fuelToAdd,
            FuelToAddMinLiters = toAddMin,
            FuelToAddSafeLiters = toAddSafe,
            FuelSurplusLiters = surplus,
            EstRefuelSeconds = estRefuelSec,
            PitStopsRemaining = pitStopsRem,
            TargetBurnPerLap = targetBurn,
            HasReliableBurn = HasReliableBurn || burn > 0.05f,
            IsLowFuel = estLapsOnFuel > 0f && estLapsOnFuel < 2.2f,
            IsPitWindowOpen = fuelToAdd > 0.2f && estLapsOnFuel > 0f && estLapsOnFuel <= 3.5f
        };
    }

    private static float EstimateRaceLapsRemaining(int sessionLapsRemain, double sessionTimeRemainSec, float estLapTimeSec, float lapDistPct)
    {
        float lapFractionLeft = Math.Clamp(1f - lapDistPct, 0f, 1f);

        // Fixed-lap race (iRacing uses 32767 for unlimited laps in timed races)
        if (sessionLapsRemain > 0 && sessionLapsRemain < 30000)
            return Math.Max(0f, (sessionLapsRemain - 1) + lapFractionLeft);

        // Timed race
        if (sessionTimeRemainSec > 0 && estLapTimeSec > 10f)
        {
            double remainingAfterCurrent = Math.Max(0.0, sessionTimeRemainSec - lapFractionLeft * estLapTimeSec);
            int fullLapsAfterCurrent = (int)Math.Ceiling(remainingAfterCurrent / estLapTimeSec);
            return fullLapsAfterCurrent + lapFractionLeft;
        }

        return 0f;
    }
}
