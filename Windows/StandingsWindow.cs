using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ApexHUD.Controls;
using ApexHUD.Core.Settings;
using ApexHUD.Core.Telemetry;

namespace ApexHUD.Windows;

/// <summary>
/// Dynamic auto-width Multiclass Standings overlay with reusable zero-GC row pool
/// and 60fps GPU-composited animations (overtake slide, position gain/loss flash, purple fastest-lap shimmer).
/// </summary>
public sealed class StandingsWindow : OverlayWindowBase
{
    private readonly Border _outerBorder;
    private readonly StackPanel _rootStack;
    private readonly Border _headerHost;
    private readonly Grid _headerGrid;
    private readonly Grid _legendGrid;
    private readonly Border _legendDivider;
    private readonly TextBlock _nativeTitleText;
    private readonly TextBlock _trackText;
    private readonly TextBlock _sessionTypeText;
    private readonly TextBlock _lapsText;
    private readonly TextBlock _timerText;
    private readonly StackPanel _bodyPanel;
    private readonly TextBlock _emptyText;

    private FontFamily _uiFont = new("Segoe UI Variable Text, Segoe UI");
    private FontFamily _monoFont = new("Cascadia Mono, Consolas");

    // Reusable pooled visuals & animation state
    private readonly Dictionary<int, DriverRowVisual> _rowCache = new();
    private readonly Dictionary<int, ClassStripVisual> _classCache = new();
    private readonly List<int> _lastLayoutKeys = new();
    private readonly Dictionary<int, int> _prevRowSlot = new();
    private readonly Dictionary<int, int> _prevClassId = new();
    private readonly Dictionary<int, int> _prevClassPos = new();
    private readonly Dictionary<int, (int CarIdx, float BestLap)> _prevClassFastest = new();
    private bool _hasAnimatedBaseline;
    private string _lastSessionType = "";

    private static readonly SolidColorBrush BgPlayerCarbon = FormatHelpers.GetFrozenBrush("#4D2563EB");
    private static readonly SolidColorBrush BgPlayerNative = FormatHelpers.GetFrozenBrush("#22F2B830");
    private static readonly SolidColorBrush BgRowAltCarbon = FormatHelpers.GetFrozenBrush("#12FFFFFF");
    private static readonly SolidColorBrush NativeRowDivider = FormatHelpers.GetFrozenBrush("#FF2C303A");
    private static readonly SolidColorBrush Hairline = FormatHelpers.GetFrozenBrush("#22FFFFFF");
    private static readonly SolidColorBrush TextWhite = FormatHelpers.GetFrozenBrush("#F3F4F6");
    private static readonly SolidColorBrush TextSoft = FormatHelpers.GetFrozenBrush("#DCE2EC");
    private static readonly SolidColorBrush TextMuted = FormatHelpers.GetFrozenBrush("#9CA3AF");
    private static readonly SolidColorBrush NativeMuted = FormatHelpers.GetFrozenBrush("#8E95A3");
    private static readonly SolidColorBrush IRacingGold = FormatHelpers.GetFrozenBrush("#F2B830");
    private static readonly SolidColorBrush IRacingGoldBright = FormatHelpers.GetFrozenBrush("#F5C84C");
    private static readonly SolidColorBrush AccentSky = FormatHelpers.GetFrozenBrush("#7DD3FC");
    private static readonly SolidColorBrush GainGreen = FormatHelpers.GetFrozenBrush("#4ADE80");
    private static readonly SolidColorBrush LossRed = FormatHelpers.GetFrozenBrush("#FF5A6E");
    private static readonly SolidColorBrush PitAmber = FormatHelpers.GetFrozenBrush("#FCD34D");
    private static readonly SolidColorBrush PitBadgeBg = FormatHelpers.GetFrozenBrush("#FFF2B830");
    private static readonly SolidColorBrush PitBadgeText = FormatHelpers.GetFrozenBrush("#FF111318");
    private static readonly SolidColorBrush StintPittedBg = FormatHelpers.GetFrozenBrush("#28FFFFFF");
    private static readonly SolidColorBrush StintCleanText = FormatHelpers.GetFrozenBrush("#FFD6DFEC");
    private static readonly SolidColorBrush PurpleBest = FormatHelpers.GetFrozenBrush("#E879F9");
    private static readonly SolidColorBrush CarNumBgCarbon = FormatHelpers.GetFrozenBrush("#24FFFFFF");

    // Animation flash brushes
    private static readonly SolidColorBrush FlashGainBg = FormatHelpers.GetFrozenBrush("#5222C55E");
    private static readonly SolidColorBrush FlashGainEdge = FormatHelpers.GetFrozenBrush("#FF4ADE80");
    private static readonly SolidColorBrush FlashLossBg = FormatHelpers.GetFrozenBrush("#48EF4444");
    private static readonly SolidColorBrush FlashLossEdge = FormatHelpers.GetFrozenBrush("#FFFF5A6E");
    private static readonly SolidColorBrush FlashPurpleBg = FormatHelpers.GetFrozenBrush("#66D946EF");
    private static readonly SolidColorBrush FlashPurpleEdge = FormatHelpers.GetFrozenBrush("#FFE879F9");

    private static readonly CubicEase SlideEase = CreateEase<CubicEase>(EasingMode.EaseOut);
    private static readonly QuadraticEase FlashEase = CreateEase<QuadraticEase>(EasingMode.EaseOut);

    private static T CreateEase<T>(EasingMode mode) where T : EasingFunctionBase, new()
    {
        var e = new T { EasingMode = mode };
        e.Freeze();
        return e;
    }

    private int _renderTick;
    private bool _lastIsRaceSession = true;

    private sealed class ClassStripVisual
    {
        public Border Container = null!;
        public Border ColorBar = null!;
        public TextBlock NameText = null!;
        public TextBlock SofText = null!;
        public TextBlock CountText = null!;
    }

    private sealed class DriverRowVisual
    {
        public Border Container = null!;
        public TranslateTransform SlideTransform = null!;
        public Grid RowGrid = null!;
        public Border FlashOverlay = null!;
        public Border FlashEdge = null!;

        public Grid NodeGrid = null!;
        public Border NodeLine = null!;
        public Ellipse NodeDot = null!;
        public Border CarbonBar = null!;

        public TextBlock PosText = null!;
        public TextBlock PosDeltaText = null!;
        public Border CarNumBox = null!;
        public TextBlock CarNumText = null!;
        public CarBrandIcon BrandIcon = null!;
        public TextBlock NameText = null!;
        public Border TirePill = null!;
        public TextBlock TireText = null!;
        public TextBlock IRText = null!;
        public TextBlock IRDeltaText = null!;
        public TextBlock GapText = null!;
        public StackPanel LapPanel = null!;
        public HudMetricIcon FastestBolt = null!;
        public TextBlock LapText = null!;
        public Border PitBadge = null!;
        public StackPanel PitPanel = null!;
        public HudMetricIcon PitWrench = null!;
        public TextBlock PitText = null!;
    }

    public StandingsWindow(AppSettings settings) : base("Standings", settings)
    {
        _outerBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            SnapsToDevicePixels = true,
            ClipToBounds = true
        };

        _rootStack = new StackPanel { Effect = GetActiveShadow() };
        Grid.SetIsSharedSizeScope(_rootStack, true);

        _headerGrid = new Grid { Margin = new Thickness(3, 1, 3, 1) };
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftTrack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 6, 0)
        };
        _nativeTitleText = new TextBlock
        {
            Text = "STANDINGS",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = IRacingGold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        };
        leftTrack.Children.Add(_nativeTitleText);
        leftTrack.Children.Add(new HudMetricIcon(HudIconKind.Flag, "#7DD3FC", 11.5) { Margin = new Thickness(0, 0, 3, 0) });
        _trackText = new TextBlock
        {
            Text = "Standings",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = TextWhite,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 185,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        leftTrack.Children.Add(_trackText);
        Grid.SetColumn(leftTrack, 0);
        _headerGrid.Children.Add(leftTrack);

        var rightMeta = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 1, 0)
        };
        _sessionTypeText = new TextBlock
        {
            Text = "RACE",
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Foreground = AccentSky,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        rightMeta.Children.Add(_sessionTypeText);

        rightMeta.Children.Add(new HudMetricIcon(HudIconKind.LapLoop, "#CBD5E1", 11.5) { Margin = new Thickness(0, 0, 2, 0) });
        _lapsText = new TextBlock
        {
            Text = "0/0",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = TextSoft,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        rightMeta.Children.Add(_lapsText);

        rightMeta.Children.Add(new HudMetricIcon(HudIconKind.Stopwatch, "#CBD5E1", 11.5) { Margin = new Thickness(0, 0, 2, 0) });
        _timerText = new TextBlock
        {
            Text = "00:00",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = TextSoft,
            VerticalAlignment = VerticalAlignment.Center
        };
        rightMeta.Children.Add(_timerText);

        Grid.SetColumn(rightMeta, 1);
        _headerGrid.Children.Add(rightMeta);

        _headerHost = new Border { Child = _headerGrid };
        _rootStack.Children.Add(_headerHost);

        _legendGrid = new Grid { Height = 15 };
        _rootStack.Children.Add(_legendGrid);
        _legendDivider = new Border { Height = 1, Background = Hairline };
        _rootStack.Children.Add(_legendDivider);

        _bodyPanel = new StackPanel { Margin = new Thickness(0) };
        _rootStack.Children.Add(_bodyPanel);

        _emptyText = new TextBlock
        {
            Text = "Waiting for iRacing session...",
            FontSize = 11.5,
            Foreground = TextMuted,
            Margin = new Thickness(8, 6, 8, 6)
        };

        _outerBorder.Child = _rootStack;
        AttachOverlayRoot(_outerBorder, "Standings");
    }

    protected override void OnAppearanceUpdated()
    {
        if (_outerBorder == null) return;
        bool native = IsIRacingNative;
        _uiFont = GetUiFont();
        _monoFont = GetMonoFont();

        _outerBorder.Background = MakeBackdropBrush(1.0);
        _outerBorder.BorderBrush = MakeBorderBrush();
        _outerBorder.CornerRadius = GetCardRadius();
        _rootStack.Effect = GetActiveShadow();

        _headerGrid.Background = native ? Brushes.Transparent : MakeBackdropBrush(1.18, 0x0F, 0x13, 0x1C);
        _legendGrid.Background = native ? Brushes.Transparent : MakeBackdropBrush(0.78, 0x0A, 0x0D, 0x13);
        _legendDivider.Opacity = native ? 0.35 : 1.0;
        _headerHost.Visibility = Settings.ShowStandingsHeader ? Visibility.Visible : Visibility.Collapsed;

        _nativeTitleText.Visibility = native ? Visibility.Visible : Visibility.Collapsed;
        _nativeTitleText.FontFamily = _uiFont;
        _nativeTitleText.FontSize = FontSz(12);
        _trackText.FontFamily = _uiFont;
        _trackText.FontSize = FontSz(12);
        _trackText.Foreground = native ? NativeMuted : TextWhite;
        _sessionTypeText.FontFamily = _uiFont;
        _sessionTypeText.FontSize = FontSz(11.5);
        _sessionTypeText.Foreground = native ? IRacingGold : AccentSky;
        _lapsText.FontFamily = _monoFont;
        _lapsText.FontSize = FontSz(12);
        _timerText.FontFamily = _monoFont;
        _timerText.FontSize = FontSz(12);
        _emptyText.FontFamily = _uiFont;
        _emptyText.FontSize = FontSz(11.5);

        // Clear caches so skin/font/column changes apply cleanly on next frame
        _rowCache.Clear();
        _classCache.Clear();
        _lastLayoutKeys.Clear();
        _bodyPanel.Children.Clear();
        ResetAnimationBaseline();

        RebuildLegend();
        _renderTick = 0;
    }

    private void ResetAnimationBaseline()
    {
        _prevRowSlot.Clear();
        _prevClassId.Clear();
        _prevClassPos.Clear();
        _prevClassFastest.Clear();
        _hasAnimatedBaseline = false;
    }

    private void RebuildLegend()
    {
        _legendGrid.Children.Clear();
        _legendGrid.ColumnDefinitions.Clear();
        ConfigureRowColumns(_legendGrid);

        AddColHeader(_legendGrid, 1, "P", HorizontalAlignment.Right);
        if (Settings.ShowStandingsPosDelta && _lastIsRaceSession) AddColHeader(_legendGrid, 2, "±", HorizontalAlignment.Center);
        if (Settings.ShowStandingsCarNumber) AddColHeader(_legendGrid, 3, "#", HorizontalAlignment.Center);
        AddColHeader(_legendGrid, 5, "DRIVER", HorizontalAlignment.Left);
        if (Settings.ShowStandingsTireCompound) AddIconColHeader(_legendGrid, 6, HudIconKind.TireWheel, "", HorizontalAlignment.Center);
        if (Settings.ShowStandingsIRating) AddIconColHeader(_legendGrid, 7, HudIconKind.RatingBar, "iR", HorizontalAlignment.Right);
        if (Settings.ShowStandingsDeltaIR && _lastIsRaceSession) AddColHeader(_legendGrid, 8, "±iR", HorizontalAlignment.Right);
        AddIconColHeader(_legendGrid, 9, HudIconKind.Stopwatch, Settings.ShowStandingsInterval ? "INT" : "GAP", HorizontalAlignment.Right);
        if (Settings.ShowStandingsLastLap) AddColHeader(_legendGrid, 10, _lastIsRaceSession ? "LAST" : "BEST", HorizontalAlignment.Right);
        if (Settings.ShowStandingsPits) AddIconColHeader(_legendGrid, 11, HudIconKind.Wrench, "PIT", HorizontalAlignment.Center);
    }

    private static void ConfigureRowColumns(Grid g)
    {
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdClassNode" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdPos" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdDelta" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdNum" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdBrand" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdName" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdTire" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdIR" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdIRDelta" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdGap" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdLast" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdPit" });
    }

    private void AddColHeader(Grid g, int col, string text, HorizontalAlignment align)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontFamily = _uiFont,
            FontSize = FontSz(9),
            FontWeight = FontWeights.Bold,
            Foreground = TextMuted,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0)
        };
        Grid.SetColumn(tb, col);
        g.Children.Add(tb);
    }

    private void AddIconColHeader(Grid g, int col, HudIconKind icon, string text, HorizontalAlignment align)
    {
        var sp = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0)
        };
        sp.Children.Add(new HudMetricIcon(icon, "#CBD5E1", 9) { Margin = new Thickness(0, 0, string.IsNullOrEmpty(text) ? 0 : 1.5, 0) });
        if (!string.IsNullOrEmpty(text))
        {
            sp.Children.Add(new TextBlock
            {
                Text = text,
                FontFamily = _uiFont,
                FontSize = FontSz(9),
                FontWeight = FontWeights.Bold,
                Foreground = TextMuted,
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        Grid.SetColumn(sp, col);
        g.Children.Add(sp);
    }

    public void UpdateData(TelemetrySnapshot snap)
    {
        _renderTick++;
        if (_renderTick % 4 != 1 && _bodyPanel.Children.Count > 0) return;

        if (!string.Equals(snap.Session.SessionType, _lastSessionType, StringComparison.OrdinalIgnoreCase))
        {
            _lastSessionType = snap.Session.SessionType;
            ResetAnimationBaseline();
        }

        if (snap.Session.IsRaceSession != _lastIsRaceSession)
        {
            _lastIsRaceSession = snap.Session.IsRaceSession;
            RebuildLegend();
            ResetAnimationBaseline();
        }

        _trackText.Text = snap.Session.TrackName;
        _sessionTypeText.Text = snap.Session.SessionType;
        _lapsText.Text = snap.Session.TotalLaps > 0
            ? $"{snap.Session.CurrentLap}/{snap.Session.TotalLaps}"
            : $"{snap.Session.CurrentLap}";
        _timerText.Text = FormatHelpers.FormatTimeRemaining(snap.Session.TimeRemainingSeconds);

        if (snap.Standings.Count == 0)
        {
            _emptyText.Text = snap.IsConnected ? "Waiting for grid..." : "Waiting for iRacing session...";
            if (_bodyPanel.Children.Count != 1 || _bodyPanel.Children[0] != _emptyText)
            {
                _bodyPanel.Children.Clear();
                _bodyPanel.Children.Add(_emptyText);
                _lastLayoutKeys.Clear();
            }
            ResetAnimationBaseline();
            return;
        }

        bool native = IsIRacingNative;
        double rowH = Math.Round((native ? 20 : 19) * Math.Max(1.0, Settings.HudFontScale * 0.92));

        var desiredKeys = new List<int>(24);
        var desiredElements = new List<UIElement>(24);
        var newRowSlots = new Dictionary<int, int>(24);
        var newClassIds = new Dictionary<int, int>(24);
        var newClassPos = new Dictionary<int, int>(24);
        var newClassFastest = new Dictionary<int, (int CarIdx, float BestLap)>(4);

        // Pending GPU animation triggers applied right after layout order updates
        var slideTriggers = new List<(DriverRowVisual Row, double FromY, bool IsGain)>();
        var flashTriggers = new List<(DriverRowVisual Row, Brush Bg, Brush Edge, int DurationMs, double PeakOpacity)>();

        int shownClasses = Math.Min(2, snap.Standings.Count);
        int visualSlot = 0;

        for (int c = 0; c < shownClasses; c++)
        {
            var group = snap.Standings[c];
            int classKey = -10000 - group.ClassId;
            var strip = GetOrCreateClassStrip(group.ClassId);
            UpdateClassStrip(strip, group);

            desiredKeys.Add(classKey);
            desiredElements.Add(strip.Container);
            visualSlot++;

            int maxForClass = c == 0
                ? Math.Min(Settings.StandingsMaxRows, group.Drivers.Count)
                : Math.Min(3, group.Drivers.Count);

            for (int i = 0; i < maxForClass; i++)
            {
                var d = group.Drivers[i];
                bool isVeryLast = (c == shownClasses - 1) && (i == maxForClass - 1);

                var rv = GetOrCreateDriverRow(d.CarIdx, rowH);
                UpdateDriverRow(rv, d, i % 2 == 1, snap.Session.IsRaceSession, isVeryLast, rowH);

                desiredKeys.Add(d.CarIdx);
                desiredElements.Add(rv.Container);

                newRowSlots[d.CarIdx] = visualSlot;
                newClassIds[d.CarIdx] = group.ClassId;
                newClassPos[d.CarIdx] = d.ClassPosition;

                if (d.IsClassBestLap && d.BestLapSeconds > 10f)
                    newClassFastest[group.ClassId] = (d.CarIdx, d.BestLapSeconds);

                if (_hasAnimatedBaseline)
                {
                    bool triggeredPurple = false;
                    if (d.IsClassBestLap && d.BestLapSeconds > 10f)
                    {
                        bool isNewBest = !_prevClassFastest.TryGetValue(group.ClassId, out var prevFast)
                            || prevFast.CarIdx != d.CarIdx
                            || d.BestLapSeconds < prevFast.BestLap - 0.005f;
                        if (isNewBest)
                        {
                            flashTriggers.Add((rv, FlashPurpleBg, FlashPurpleEdge, 1450, 1.0));
                            triggeredPurple = true;
                        }
                    }

                    if (_prevRowSlot.TryGetValue(d.CarIdx, out int oldSlot)
                        && _prevClassId.TryGetValue(d.CarIdx, out int oldCls) && oldCls == group.ClassId
                        && _prevClassPos.TryGetValue(d.CarIdx, out int oldPos))
                    {
                        int slotDiff = oldSlot - visualSlot;
                        if (slotDiff != 0 && Math.Abs(slotDiff) <= 8)
                        {
                            bool isGain = d.ClassPosition < oldPos;
                            slideTriggers.Add((rv, slotDiff * rowH, isGain));
                        }

                        if (!triggeredPurple && d.ClassPosition != oldPos)
                        {
                            if (d.ClassPosition < oldPos)
                                flashTriggers.Add((rv, FlashGainBg, FlashGainEdge, 1050, 0.95));
                            else
                                flashTriggers.Add((rv, FlashLossBg, FlashLossEdge, 950, 0.85));
                        }
                    }
                }

                visualSlot++;
            }
        }

        // Only mutate _bodyPanel.Children when ordering or membership actually changes
        // -> zero GC churn, zero SharedSizeGroup thrash, and running GPU animations stay silky smooth!
        if (!SequenceEquals(_lastLayoutKeys, desiredKeys))
        {
            _bodyPanel.Children.Clear();
            foreach (var el in desiredElements)
                _bodyPanel.Children.Add(el);

            _lastLayoutKeys.Clear();
            _lastLayoutKeys.AddRange(desiredKeys);
        }

        // Fire GPU-composited slide & flash animations
        foreach (var (row, fromY, isGain) in slideTriggers)
            TriggerSlideAnimation(row, fromY, isGain);

        foreach (var (row, bg, edge, durMs, peak) in flashTriggers)
            TriggerFlashAnimation(row, bg, edge, durMs, peak);

        _prevRowSlot.Clear();
        foreach (var kv in newRowSlots) _prevRowSlot[kv.Key] = kv.Value;
        _prevClassId.Clear();
        foreach (var kv in newClassIds) _prevClassId[kv.Key] = kv.Value;
        _prevClassPos.Clear();
        foreach (var kv in newClassPos) _prevClassPos[kv.Key] = kv.Value;
        _prevClassFastest.Clear();
        foreach (var kv in newClassFastest) _prevClassFastest[kv.Key] = kv.Value;
        _hasAnimatedBaseline = true;
    }

    private static bool SequenceEquals(List<int> a, List<int> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private static void TriggerSlideAnimation(DriverRowVisual row, double fromY, bool isGain)
    {
        Panel.SetZIndex(row.Container, isGain ? 20 : 5);

        var anim = new DoubleAnimation
        {
            From = fromY,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(340),
            EasingFunction = SlideEase,
            FillBehavior = FillBehavior.Stop
        };
        anim.Completed += (_, _) =>
        {
            row.SlideTransform.Y = 0.0;
            Panel.SetZIndex(row.Container, 0);
        };
        row.SlideTransform.BeginAnimation(TranslateTransform.YProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    private static void TriggerFlashAnimation(DriverRowVisual row, Brush bg, Brush edge, int durationMs, double peakOpacity)
    {
        row.FlashOverlay.Background = bg;
        row.FlashEdge.Background = edge;

        var anim = new DoubleAnimation
        {
            From = peakOpacity,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = FlashEase,
            FillBehavior = FillBehavior.Stop
        };
        anim.Completed += (_, _) => row.FlashOverlay.Opacity = 0.0;
        row.FlashOverlay.BeginAnimation(UIElement.OpacityProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    private ClassStripVisual GetOrCreateClassStrip(int classId)
    {
        if (_classCache.TryGetValue(classId, out var existing))
            return existing;

        bool native = IsIRacingNative;
        var strip = new Grid
        {
            Height = 17,
            Background = native ? FormatHelpers.GetFrozenBrush("#10FFFFFF") : MakeBackdropBrush(1.08, 0x13, 0x18, 0x24)
        };
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var bar = new Border();
        Grid.SetColumn(bar, 0);
        strip.Children.Add(bar);

        var name = new TextBlock
        {
            FontFamily = _uiFont,
            FontSize = FontSz(10.5),
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 6, 0)
        };
        Grid.SetColumn(name, 1);
        strip.Children.Add(name);

        var meta = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        };
        meta.Children.Add(new HudMetricIcon(HudIconKind.Trophy, "#FCD34D", 9.5) { Margin = new Thickness(0, 0, 2, 0) });
        var sofText = new TextBlock
        {
            FontFamily = _monoFont,
            FontSize = FontSz(10),
            FontWeight = FontWeights.Bold,
            Foreground = TextSoft,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        };
        meta.Children.Add(sofText);
        meta.Children.Add(new HudMetricIcon(HudIconKind.CarsGrid, "#CBD5E1", 9.5) { Margin = new Thickness(0, 0, 2, 0) });
        var countText = new TextBlock
        {
            FontFamily = _monoFont,
            FontSize = FontSz(10),
            FontWeight = FontWeights.SemiBold,
            Foreground = TextSoft,
            VerticalAlignment = VerticalAlignment.Center
        };
        meta.Children.Add(countText);

        Grid.SetColumn(meta, 3);
        strip.Children.Add(meta);

        var v = new ClassStripVisual
        {
            Container = new Border
            {
                BorderBrush = Hairline,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = strip
            },
            ColorBar = bar,
            NameText = name,
            SofText = sofText,
            CountText = countText
        };
        _classCache[classId] = v;
        return v;
    }

    private static void UpdateClassStrip(ClassStripVisual v, StandingsClassGroup group)
    {
        var colorBrush = FormatHelpers.GetFrozenBrush(group.ClassColorHex);
        v.ColorBar.Background = colorBrush;
        v.NameText.Text = group.ClassName;
        v.NameText.Foreground = colorBrush;
        v.SofText.Text = FormatHelpers.FormatIRating(group.Sof);
        v.CountText.Text = group.TotalDrivers.ToString();
    }

    private DriverRowVisual GetOrCreateDriverRow(int carIdx, double rowH)
    {
        if (_rowCache.TryGetValue(carIdx, out var existing))
            return existing;

        bool native = IsIRacingNative;
        var rv = new DriverRowVisual();
        rv.SlideTransform = new TranslateTransform(0, 0);

        var g = new Grid { Height = rowH };
        ConfigureRowColumns(g);
        rv.RowGrid = g;

        // GPU Flash Overlay spanning all columns (sits behind text or gently tints entire row)
        rv.FlashEdge = new Border
        {
            Width = 3,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        rv.FlashOverlay = new Border
        {
            Opacity = 0.0,
            IsHitTestVisible = false,
            Child = rv.FlashEdge
        };
        Grid.SetColumnSpan(rv.FlashOverlay, 12);
        g.Children.Add(rv.FlashOverlay);

        // Col 0: Class node (Native) or accent bar (Carbon)
        rv.NodeGrid = new Grid { Width = 8, Margin = new Thickness(2, 0, 1, 0) };
        rv.NodeLine = new Border
        {
            Width = 1.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Opacity = 0.72
        };
        rv.NodeDot = new Ellipse
        {
            Width = 5,
            Height = 5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        rv.NodeGrid.Children.Add(rv.NodeLine);
        rv.NodeGrid.Children.Add(rv.NodeDot);
        Grid.SetColumn(rv.NodeGrid, 0);
        g.Children.Add(rv.NodeGrid);

        rv.CarbonBar = new Border
        {
            Width = 3,
            Margin = new Thickness(0, 1, 1, 1),
            CornerRadius = new CornerRadius(1)
        };
        Grid.SetColumn(rv.CarbonBar, 0);
        g.Children.Add(rv.CarbonBar);

        rv.NodeGrid.Visibility = native ? Visibility.Visible : Visibility.Collapsed;
        rv.CarbonBar.Visibility = native ? Visibility.Collapsed : Visibility.Visible;

        // Col 1: Position
        rv.PosText = MakeRowText(_monoFont, FontSz(11.5), HorizontalAlignment.Right, new Thickness(1, 0, 2, 0));
        Grid.SetColumn(rv.PosText, 1);
        g.Children.Add(rv.PosText);

        // Col 2: Pos delta
        rv.PosDeltaText = MakeRowText(_monoFont, FontSz(10), HorizontalAlignment.Center, new Thickness(1, 0, 2, 0));
        rv.PosDeltaText.FontWeight = FontWeights.SemiBold;
        Grid.SetColumn(rv.PosDeltaText, 2);
        g.Children.Add(rv.PosDeltaText);

        // Col 3: Car number
        rv.CarNumText = new TextBlock
        {
            FontFamily = _monoFont,
            FontSize = FontSz(11),
            FontWeight = native ? FontWeights.SemiBold : FontWeights.Bold,
            Foreground = native ? IRacingGold : TextSoft,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        rv.CarNumBox = new Border
        {
            Background = native ? Brushes.Transparent : CarNumBgCarbon,
            CornerRadius = new CornerRadius(2),
            Padding = native ? new Thickness(1, 0, 1, 0) : new Thickness(3, 0, 3, 0),
            Margin = new Thickness(1, 1, 2, 1),
            Child = rv.CarNumText
        };
        Grid.SetColumn(rv.CarNumBox, 3);
        g.Children.Add(rv.CarNumBox);

        // Col 4: Brand icon
        rv.BrandIcon = new CarBrandIcon { Width = 16, Height = 14, Margin = new Thickness(0, 0, 3, 0) };
        Grid.SetColumn(rv.BrandIcon, 4);
        g.Children.Add(rv.BrandIcon);

        // Col 5: Driver name
        rv.NameText = new TextBlock
        {
            FontFamily = _uiFont,
            FontSize = FontSz(12),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 190,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 5, 0)
        };
        Grid.SetColumn(rv.NameText, 5);
        g.Children.Add(rv.NameText);

        // Col 6: Tire compound
        rv.TireText = new TextBlock
        {
            FontFamily = _monoFont,
            FontSize = FontSz(8.5),
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        rv.TirePill = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1.4),
            Margin = new Thickness(1, 0, 2, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = rv.TireText
        };
        Grid.SetColumn(rv.TirePill, 6);
        g.Children.Add(rv.TirePill);

        // Col 7: iRating
        rv.IRText = MakeRowText(_monoFont, FontSz(11.5), HorizontalAlignment.Right, new Thickness(2, 0, 2, 0));
        rv.IRText.Foreground = TextSoft;
        rv.IRText.FontWeight = FontWeights.SemiBold;
        Grid.SetColumn(rv.IRText, 7);
        g.Children.Add(rv.IRText);

        // Col 8: ±iR
        rv.IRDeltaText = MakeRowText(_monoFont, FontSz(11.5), HorizontalAlignment.Right, new Thickness(2, 0, 2, 0));
        rv.IRDeltaText.FontWeight = FontWeights.Bold;
        Grid.SetColumn(rv.IRDeltaText, 8);
        g.Children.Add(rv.IRDeltaText);

        // Col 9: Gap / Interval
        rv.GapText = MakeRowText(_monoFont, FontSz(12), HorizontalAlignment.Right, new Thickness(2, 0, 3, 0));
        rv.GapText.FontWeight = FontWeights.Bold;
        Grid.SetColumn(rv.GapText, 9);
        g.Children.Add(rv.GapText);

        // Col 10: Last / Best Lap
        rv.LapPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 3, 0)
        };
        rv.FastestBolt = new HudMetricIcon(HudIconKind.FastestBolt, "#E879F9", 9.5)
        {
            Margin = new Thickness(0, 0, 1.5, 0),
            Visibility = Visibility.Collapsed
        };
        rv.LapText = new TextBlock
        {
            FontFamily = _monoFont,
            FontSize = FontSz(11.5),
            VerticalAlignment = VerticalAlignment.Center
        };
        rv.LapPanel.Children.Add(rv.FastestBolt);
        rv.LapPanel.Children.Add(rv.LapText);
        Grid.SetColumn(rv.LapPanel, 10);
        g.Children.Add(rv.LapPanel);

        // Col 11: High-contrast Pit / Stint badge
        rv.PitPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        rv.PitWrench = new HudMetricIcon(HudIconKind.Wrench, "#111318", 9)
        {
            Margin = new Thickness(0, 0, 2, 0),
            Visibility = Visibility.Collapsed
        };
        rv.PitText = new TextBlock
        {
            FontFamily = _monoFont,
            FontSize = FontSz(10.5),
            VerticalAlignment = VerticalAlignment.Center
        };
        rv.PitPanel.Children.Add(rv.PitWrench);
        rv.PitPanel.Children.Add(rv.PitText);
        rv.PitBadge = new Border
        {
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(3, 0, 3, 0),
            Margin = new Thickness(1, 2, 4, 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = rv.PitPanel
        };
        Grid.SetColumn(rv.PitBadge, 11);
        g.Children.Add(rv.PitBadge);

        rv.Container = new Border
        {
            RenderTransform = rv.SlideTransform,
            Child = g
        };

        _rowCache[carIdx] = rv;
        return rv;
    }

    private void UpdateDriverRow(DriverRowVisual rv, StandingsEntry d, bool alt, bool isRaceSession, bool isVeryLast, double rowH)
    {
        bool native = IsIRacingNative;
        rv.RowGrid.Height = rowH;

        var classBrush = FormatHelpers.GetFrozenBrush(d.ClassColorHex);
        rv.NodeLine.Background = classBrush;
        rv.NodeDot.Fill = classBrush;
        rv.CarbonBar.Background = classBrush;

        rv.PosText.Text = native ? $"P{d.ClassPosition}" : d.ClassPosition.ToString();
        rv.PosText.Foreground = d.IsPlayer ? (native ? IRacingGoldBright : AccentSky) : (native ? TextSoft : TextWhite);
        rv.PosText.FontWeight = d.IsPlayer || !native ? FontWeights.Bold : FontWeights.Normal;

        bool showPosDelta = Settings.ShowStandingsPosDelta && isRaceSession;
        rv.PosDeltaText.Visibility = showPosDelta ? Visibility.Visible : Visibility.Collapsed;
        if (showPosDelta)
        {
            rv.PosDeltaText.Text = d.PosDelta > 0 ? $"▲{d.PosDelta}" : d.PosDelta < 0 ? $"▼{Math.Abs(d.PosDelta)}" : "–";
            rv.PosDeltaText.Foreground = d.PosDelta > 0 ? GainGreen : d.PosDelta < 0 ? LossRed : NativeMuted;
        }

        rv.CarNumBox.Visibility = Settings.ShowStandingsCarNumber ? Visibility.Visible : Visibility.Collapsed;
        if (Settings.ShowStandingsCarNumber)
            rv.CarNumText.Text = string.IsNullOrEmpty(d.CarNumber) ? "--" : (native ? $"#{d.CarNumber}" : d.CarNumber);

        rv.BrandIcon.Visibility = Settings.ShowCarBrandIcons ? Visibility.Visible : Visibility.Collapsed;
        if (Settings.ShowCarBrandIcons)
            rv.BrandIcon.Brand = d.Brand;

        rv.NameText.Text = d.DriverName;
        rv.NameText.FontWeight = d.IsPlayer ? FontWeights.SemiBold : (native ? FontWeights.Normal : FontWeights.SemiBold);
        rv.NameText.Foreground = d.IsPlayer
            ? (native ? IRacingGoldBright : TextWhite)
            : (d.IsOnPitRoad ? NativeMuted : TextWhite);

        rv.TirePill.Visibility = Settings.ShowStandingsTireCompound ? Visibility.Visible : Visibility.Collapsed;
        if (Settings.ShowStandingsTireCompound)
        {
            var tireColor = FormatHelpers.GetFrozenBrush(d.TireColorHex, "#CBD5E1");
            rv.TirePill.BorderBrush = tireColor;
            rv.TireText.Text = d.TireCode;
            rv.TireText.Foreground = tireColor;
        }

        rv.IRText.Visibility = Settings.ShowStandingsIRating ? Visibility.Visible : Visibility.Collapsed;
        if (Settings.ShowStandingsIRating)
            rv.IRText.Text = FormatHelpers.FormatIRating(d.IRating);

        bool showDeltaIr = Settings.ShowStandingsDeltaIR && isRaceSession;
        rv.IRDeltaText.Visibility = showDeltaIr ? Visibility.Visible : Visibility.Collapsed;
        if (showDeltaIr)
        {
            rv.IRDeltaText.Text = d.ProjectedIRDelta > 0 ? $"+{d.ProjectedIRDelta}" : d.ProjectedIRDelta.ToString();
            rv.IRDeltaText.Foreground = d.ProjectedIRDelta > 0 ? GainGreen : d.ProjectedIRDelta < 0 ? LossRed : TextMuted;
        }

        float gapVal = Settings.ShowStandingsInterval ? d.IntervalSeconds : d.GapToLeaderSeconds;
        rv.GapText.Text = d.ClassPosition == 1
            ? (isRaceSession ? "LDR" : "P1")
            : !isRaceSession && d.BestLapSeconds <= 10f
                ? "--"
                : !isRaceSession
                    ? FormatHelpers.FormatQualiGap(gapVal)
                    : d.LapsDown > 0 && !Settings.ShowStandingsInterval
                        ? $"+{d.LapsDown}L"
                        : FormatHelpers.FormatGap(gapVal);
        rv.GapText.Foreground = d.IsPlayer && native
            ? IRacingGoldBright
            : (d.ClassPosition == 1 ? (native ? IRacingGold : AccentSky) : TextWhite);

        rv.LapPanel.Visibility = Settings.ShowStandingsLastLap ? Visibility.Visible : Visibility.Collapsed;
        if (Settings.ShowStandingsLastLap)
        {
            rv.FastestBolt.Visibility = d.IsClassBestLap ? Visibility.Visible : Visibility.Collapsed;
            float shownLap = isRaceSession ? d.LastLapSeconds : d.BestLapSeconds;
            rv.LapText.Text = FormatHelpers.FormatLapTime(shownLap);
            rv.LapText.Foreground = d.IsClassBestLap ? PurpleBest : TextSoft;
            rv.LapText.FontWeight = d.IsClassBestLap ? FontWeights.Bold : FontWeights.SemiBold;
        }

        rv.PitBadge.Visibility = Settings.ShowStandingsPits ? Visibility.Visible : Visibility.Collapsed;
        if (Settings.ShowStandingsPits)
        {
            if (d.IsOnPitRoad)
            {
                rv.PitBadge.Background = PitBadgeBg;
                rv.PitBadge.Padding = new Thickness(3, 0, 3, 0);
                rv.PitWrench.Visibility = Visibility.Visible;
                rv.PitText.Text = "PIT";
                rv.PitText.FontSize = FontSz(9.5);
                rv.PitText.FontWeight = FontWeights.ExtraBold;
                rv.PitText.Foreground = PitBadgeText;
            }
            else if (d.PitStops > 0)
            {
                rv.PitBadge.Background = StintPittedBg;
                rv.PitBadge.Padding = new Thickness(3, 0, 3, 0);
                rv.PitWrench.Visibility = Visibility.Collapsed;
                rv.PitText.Text = $"P{d.PitStops}·{d.StintLaps}";
                rv.PitText.FontSize = FontSz(10.5);
                rv.PitText.FontWeight = FontWeights.Bold;
                rv.PitText.Foreground = TextWhite;
            }
            else if (d.StintLaps > 0)
            {
                rv.PitBadge.Background = Brushes.Transparent;
                rv.PitBadge.Padding = new Thickness(2, 0, 2, 0);
                rv.PitWrench.Visibility = Visibility.Collapsed;
                rv.PitText.Text = $"L{d.StintLaps}";
                rv.PitText.FontSize = FontSz(10.5);
                rv.PitText.FontWeight = FontWeights.SemiBold;
                rv.PitText.Foreground = StintCleanText;
            }
            else
            {
                rv.PitBadge.Background = Brushes.Transparent;
                rv.PitBadge.Padding = new Thickness(2, 0, 2, 0);
                rv.PitWrench.Visibility = Visibility.Collapsed;
                rv.PitText.Text = "-";
                rv.PitText.FontSize = FontSz(10.5);
                rv.PitText.FontWeight = FontWeights.SemiBold;
                rv.PitText.Foreground = TextMuted;
            }
        }

        rv.Container.Background = d.IsPlayer
            ? (native ? BgPlayerNative : BgPlayerCarbon)
            : (native ? Brushes.Transparent : (alt ? BgRowAltCarbon : Brushes.Transparent));
        rv.Container.BorderBrush = native && !isVeryLast ? NativeRowDivider : Brushes.Transparent;
        rv.Container.BorderThickness = new Thickness(0, 0, 0, native && !isVeryLast ? 1 : 0);
    }

    private static TextBlock MakeRowText(FontFamily font, double size, HorizontalAlignment align, Thickness margin) => new()
    {
        FontFamily = font,
        FontSize = size,
        HorizontalAlignment = align,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = margin
    };
}
