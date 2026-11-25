using System;
using System.Diagnostics;
using System.Threading;
using ApexHUD.Core.Settings;

namespace ApexHUD.Core.Telemetry;

/// <summary>
/// 60 Hz telemetry loop handling seamless switching between Live iRacing SDK and Demo Mode.
/// </summary>
public sealed class TelemetryService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly IRacingSdkReader _sdk = new();
    private readonly DemoTelemetryProvider _demo = new();

    private Thread? _worker;
    private volatile bool _running;

    public event Action<TelemetrySnapshot>? SnapshotReady;

    public TelemetryService(AppSettings settings)
    {
        _settings = settings;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _worker = new Thread(Loop)
        {
            IsBackground = true,
            Name = "ApexHUD-Telemetry-60Hz",
            Priority = ThreadPriority.AboveNormal
        };
        _worker.Start();
    }

    private void Loop()
    {
        var sw = Stopwatch.StartNew();
        double lastTime = 0.0;
        long lastConnectTryMs = -10_000;

        while (_running)
        {
            double now = sw.Elapsed.TotalSeconds;
            double dt = Math.Clamp(now - lastTime, 0.004, 0.1);
            lastTime = now;

            if (_settings.DemoMode)
            {
                if (_sdk.IsConnected) _sdk.Disconnect();
                var snap = _demo.Step(dt, _settings.FuelSafetyMarginLaps, _settings.FuelCalcMode);
                SnapshotReady?.Invoke(snap);
                Thread.Sleep(16);
                continue;
            }

            // Live iRacing mode
            if (!_sdk.IsConnected)
            {
                if (sw.ElapsedMilliseconds - lastConnectTryMs > 1000)
                {
                    lastConnectTryMs = sw.ElapsedMilliseconds;
                    _sdk.TryConnect();
                }

                if (!_sdk.IsConnected)
                {
                    SnapshotReady?.Invoke(new TelemetrySnapshot
                    {
                        IsConnected = false,
                        IsDemo = false,
                        IsOnTrack = false
                    });
                    Thread.Sleep(250);
                    continue;
                }
            }

            // Sync with iRacing's 60Hz data-valid event (fallback timeout 32ms)
            _sdk.WaitForData(32);

            var liveSnap = _sdk.ReadSnapshot(
                _settings.FuelSafetyMarginLaps,
                _settings.FuelCalcMode,
                _settings.RelativeCarsEachSide,
                _settings.StandingsMaxRows);

            if (liveSnap != null)
            {
                SnapshotReady?.Invoke(liveSnap);
            }
            else
            {
                SnapshotReady?.Invoke(new TelemetrySnapshot
                {
                    IsConnected = false,
                    IsDemo = false,
                    IsOnTrack = false
                });
                Thread.Sleep(200);
            }
        }
    }

    public void Dispose()
    {
        _running = false;
        _worker?.Join(500);
        _sdk.Dispose();
    }
}
