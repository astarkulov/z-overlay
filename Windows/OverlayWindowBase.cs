using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ApexHUD.Core.Native;
using ApexHUD.Core.Settings;
using Forms = System.Windows.Forms;

namespace ApexHUD.Windows;

public enum WidgetAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}

/// <summary>
/// Base class for transparent click-through HUD overlay widgets.
/// Features:
/// - Lock Mode: 100% click-through, no focus steal
/// - Edit Mode: drag-to-move, drag right/bottom/corner edge to scale, arrow-key micro-nudge (1px / Shift=10px)
/// - Strict Monitor Bounds clamping (impossible to drag or resize off-screen)
/// - Magnetic snap to screen edges & center + 9-point anchor snapping
/// </summary>
public abstract class OverlayWindowBase : Window
{
    public string WidgetKey { get; }
    protected readonly AppSettings Settings;
    private bool _locked;
    private bool _initialized;
    private bool _isClamping;
    private bool _isSelected;

    private FrameworkElement? _contentElement;
    private Grid? _scaleRoot;
    private Border? _editFrame;
    private Border? _editPill;
    private Border? _sizeHintBadge;
    private TextBlock? _sizeHintText;
    private Grid? _resizeHandlesLayer;

    // Resize drag state (screen-space coordinates -> zero LayoutTransform feedback jitter)
    private Win32.POINT _resizeStartCursor;
    private double _resizeStartCombinedScale;
    private double _resizeBaseWidth;
    private double _resizeBaseHeight;
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    private static readonly SolidColorBrush FrameIdleBrush = Freeze(Color.FromArgb(0x88, 0x38, 0xBD, 0xF8));
    private static readonly SolidColorBrush FrameSelectedBrush = Freeze(Color.FromArgb(0xFF, 0x60, 0xA5, 0xFA));
    private static readonly SolidColorBrush FrameIdleFill = Freeze(Color.FromArgb(0x0C, 0x38, 0xBD, 0xF8));
    private static readonly SolidColorBrush FrameSelectedFill = Freeze(Color.FromArgb(0x18, 0x3B, 0x82, 0xF6));

    /// <summary>
    /// Crisp GPU-composited dark outline/shadow so HUD text stays razor-sharp even at 0-30% backdrop over bright sky.
    /// </summary>
    protected static readonly DropShadowEffect HudTextShadow = CreateTextShadow(0.92, 2.5);
    protected static readonly DropShadowEffect NativeTextShadow = CreateTextShadow(0.45, 1.5);

    private static DropShadowEffect CreateTextShadow(double opacity, double blur)
    {
        var eff = new DropShadowEffect
        {
            Color = Colors.Black,
            Direction = 315,
            ShadowDepth = 1.0,
            BlurRadius = blur,
            Opacity = opacity
        };
        eff.Freeze();
        return eff;
    }

    protected bool IsIRacingNative =>
        string.Equals(Settings.UiSkin, "IRacingNative", StringComparison.OrdinalIgnoreCase);

    protected Effect? GetActiveShadow() => IsIRacingNative ? null : HudTextShadow;
    protected CornerRadius GetCardRadius() => new(IsIRacingNative ? 6 : 4);

    public event Action? LayoutChangedByUser;
    public event Action<string>? WidgetSelected;

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    protected OverlayWindowBase(string widgetKey, AppSettings settings)
    {
        WidgetKey = widgetKey;
        Settings = settings;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = true;

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => ClampToMonitorBounds(saveToSettings: true);
        SizeChanged += (_, _) => ClampToMonitorBounds(saveToSettings: false);
        LocationChanged += OnLocationChanged;
        MouseLeftButtonDown += OnOverlayMouseLeftButtonDown;
        PreviewKeyDown += OnOverlayPreviewKeyDown;
    }

    protected void AttachOverlayRoot(FrameworkElement content, string editTitle)
    {
        _contentElement = content;

        var outerShell = new Grid();

        _scaleRoot = new Grid();
        _scaleRoot.Children.Add(content);
        outerShell.Children.Add(_scaleRoot);

        _editFrame = new Border
        {
            BorderBrush = FrameIdleBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Background = FrameIdleFill,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        outerShell.Children.Add(_editFrame);

        _editPill = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xDC, 0x15, 0x1D, 0x2B)),
            BorderBrush = FrameIdleBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(4, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = $"✥ {editTitle}",
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xF2, 0xFE))
            }
        };
        outerShell.Children.Add(_editPill);

        _sizeHintText = new TextBlock
        {
            Text = "100%",
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC))
        };
        _sizeHintBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x25, 0x63, 0xEB)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 16, 16),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = _sizeHintText
        };
        outerShell.Children.Add(_sizeHintBadge);

        _resizeHandlesLayer = BuildResizeHandles();
        outerShell.Children.Add(_resizeHandlesLayer);

        Content = outerShell;
        ApplyLayoutFromSettings();
    }

    public void SetSelectedVisual(bool selected)
    {
        _isSelected = selected;
        if (_editFrame != null)
        {
            _editFrame.BorderBrush = selected ? FrameSelectedBrush : FrameIdleBrush;
            _editFrame.BorderThickness = new Thickness(selected ? 1.8 : 1.0);
            _editFrame.Background = selected ? FrameSelectedFill : FrameIdleFill;
        }
        if (_editPill != null)
            _editPill.BorderBrush = selected ? FrameSelectedBrush : FrameIdleBrush;
    }

    private (double DpiX, double DpiY) GetDpiScale()
    {
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget != null)
        {
            return (
                Math.Max(0.5, src.CompositionTarget.TransformToDevice.M11),
                Math.Max(0.5, src.CompositionTarget.TransformToDevice.M22));
        }
        return (1.0, 1.0);
    }

    private Forms.Screen[] GetAllowedScreens()
    {
        var all = Forms.Screen.AllScreens;
        if (all.Length == 0) return Array.Empty<Forms.Screen>();

        if (Settings.AllowedMonitorDevices != null && Settings.AllowedMonitorDevices.Count > 0)
        {
            var matched = Array.FindAll(all, s => Settings.AllowedMonitorDevices.Contains(s.DeviceName));
            if (matched.Length > 0) return matched;
        }

        return new[] { Forms.Screen.PrimaryScreen ?? all[0] };
    }

    private Rect ToDipRect(System.Drawing.Rectangle b, double dpiX, double dpiY)
        => new(b.Left / dpiX, b.Top / dpiY, b.Width / dpiX, b.Height / dpiY);

    private Rect GetTargetAllowedMonitorDipRect()
    {
        var (dpiX, dpiY) = GetDpiScale();
        var allowed = GetAllowedScreens();
        if (allowed.Length == 0)
            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);

        double cx = (double.IsNaN(Left) ? 100 : Left) + Math.Max(20, ActualWidth) * 0.5;
        double cy = (double.IsNaN(Top) ? 100 : Top) + Math.Max(20, ActualHeight) * 0.5;

        // Pick the allowed monitor containing the widget center, or closest allowed monitor
        Forms.Screen best = allowed[0];
        double bestDistSq = double.MaxValue;
        foreach (var s in allowed)
        {
            var r = ToDipRect(s.Bounds, dpiX, dpiY);
            if (r.Contains(new Point(cx, cy)))
                return r;

            double clampedX = Math.Clamp(cx, r.Left, r.Right);
            double clampedY = Math.Clamp(cy, r.Top, r.Bottom);
            double dSq = (cx - clampedX) * (cx - clampedX) + (cy - clampedY) * (cy - clampedY);
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                best = s;
            }
        }

        return ToDipRect(best.Bounds, dpiX, dpiY);
    }

    private Rect GetAllowedUnionDipRect()
    {
        var (dpiX, dpiY) = GetDpiScale();
        var allowed = GetAllowedScreens();
        if (allowed.Length == 0)
            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);

        Rect union = ToDipRect(allowed[0].Bounds, dpiX, dpiY);
        for (int i = 1; i < allowed.Length; i++)
            union.Union(ToDipRect(allowed[i].Bounds, dpiX, dpiY));
        return union;
    }

    public void ClampToMonitorBounds(bool saveToSettings = true)
    {
        if (_isClamping || !_initialized) return;
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 1 || h <= 1 || double.IsNaN(Left) || double.IsNaN(Top)) return;

        var allowed = GetAllowedScreens();
        // Single allowed monitor -> strict rect; multiple allowed monitors -> clamp per closest allowed screen
        // so widgets can travel freely across checked monitors without falling into unchecked monitors or dead zones.
        Rect bounds = allowed.Length <= 1
            ? GetTargetAllowedMonitorDipRect()
            : GetTargetAllowedMonitorDipRect();

        if (allowed.Length > 1)
        {
            // Allow smooth crossing between adjacent allowed monitors along shared edges
            var union = GetAllowedUnionDipRect();
            double cx = Left + w * 0.5;
            double cy = Top + h * 0.5;
            var (dpiX, dpiY) = GetDpiScale();
            bool centerInAnyAllowed = false;
            foreach (var s in allowed)
            {
                if (ToDipRect(s.Bounds, dpiX, dpiY).Contains(new Point(cx, cy)))
                {
                    centerInAnyAllowed = true;
                    break;
                }
            }
            if (centerInAnyAllowed)
            {
                // Clamp vertical to current monitor so it never goes off-top/off-bottom of a shorter monitor,
                // and horizontal to the full allowed union so it glides cleanly across monitors!
                var curMon = GetTargetAllowedMonitorDipRect();
                bounds = new Rect(union.Left, curMon.Top, union.Width, curMon.Height);
            }
        }

        double maxLeft = Math.Max(bounds.Left, bounds.Right - w);
        double maxTop = Math.Max(bounds.Top, bounds.Bottom - h);

        double clampedLeft = Math.Clamp(Left, bounds.Left, maxLeft);
        double clampedTop = Math.Clamp(Top, bounds.Top, maxTop);

        if (Math.Abs(clampedLeft - Left) > 0.5 || Math.Abs(clampedTop - Top) > 0.5)
        {
            _isClamping = true;
            try
            {
                Left = Math.Round(clampedLeft);
                Top = Math.Round(clampedTop);
            }
            finally
            {
                _isClamping = false;
            }
        }

        if (saveToSettings)
        {
            var layout = Settings.GetWidget(WidgetKey);
            layout.Left = Math.Round(Left);
            layout.Top = Math.Round(Top);
        }
    }

    private void MagneticSnapToEdgesOrCenter(double thresholdPx = 14.0)
    {
        if (!_initialized || ActualWidth <= 1 || ActualHeight <= 1) return;
        var mon = GetTargetAllowedMonitorDipRect();

        double w = ActualWidth;
        double h = ActualHeight;
        double newLeft = Left;
        double newTop = Top;

        double centerX = mon.Left + (mon.Width - w) * 0.5;
        double rightX = mon.Right - w;
        if (Math.Abs(newLeft - mon.Left) <= thresholdPx) newLeft = mon.Left;
        else if (Math.Abs(newLeft - rightX) <= thresholdPx) newLeft = rightX;
        else if (Math.Abs(newLeft - centerX) <= thresholdPx) newLeft = centerX;

        double centerY = mon.Top + (mon.Height - h) * 0.5;
        double bottomY = mon.Bottom - h;
        if (Math.Abs(newTop - mon.Top) <= thresholdPx) newTop = mon.Top;
        else if (Math.Abs(newTop - bottomY) <= thresholdPx) newTop = bottomY;
        else if (Math.Abs(newTop - centerY) <= thresholdPx) newTop = centerY;

        _isClamping = true;
        try
        {
            Left = Math.Round(newLeft);
            Top = Math.Round(newTop);
        }
        finally
        {
            _isClamping = false;
        }
        ClampToMonitorBounds(saveToSettings: true);
    }

    public void SnapTo(WidgetAnchor anchor, double margin = 0.0)
    {
        UpdateLayout();
        var mon = GetTargetAllowedMonitorDipRect();
        double w = ActualWidth > 1 ? ActualWidth : 260;
        double h = ActualHeight > 1 ? ActualHeight : 140;

        double x = anchor switch
        {
            WidgetAnchor.TopLeft or WidgetAnchor.MiddleLeft or WidgetAnchor.BottomLeft => mon.Left + margin,
            WidgetAnchor.TopCenter or WidgetAnchor.Center or WidgetAnchor.BottomCenter => mon.Left + (mon.Width - w) * 0.5,
            _ => mon.Right - w - margin
        };

        double y = anchor switch
        {
            WidgetAnchor.TopLeft or WidgetAnchor.TopCenter or WidgetAnchor.TopRight => mon.Top + margin,
            WidgetAnchor.MiddleLeft or WidgetAnchor.Center or WidgetAnchor.MiddleRight => mon.Top + (mon.Height - h) * 0.5,
            _ => mon.Bottom - h - margin
        };

        _isClamping = true;
        try
        {
            Left = Math.Round(x);
            Top = Math.Round(y);
        }
        finally
        {
            _isClamping = false;
        }

        ClampToMonitorBounds(saveToSettings: true);
        LayoutChangedByUser?.Invoke();
    }

    public void Nudge(double dx, double dy)
    {
        if (double.IsNaN(Left) || double.IsNaN(Top)) return;

        _isClamping = true;
        try
        {
            Left = Math.Round(Left + dx);
            Top = Math.Round(Top + dy);
        }
        finally
        {
            _isClamping = false;
        }

        ClampToMonitorBounds(saveToSettings: true);
        LayoutChangedByUser?.Invoke();
    }

    private Grid BuildResizeHandles()
    {
        var layer = new Grid { Visibility = Visibility.Collapsed };

        var rightThumb = CreateInvisibleThumb(Cursors.SizeWE);
        rightThumb.Width = 8;
        rightThumb.HorizontalAlignment = HorizontalAlignment.Right;
        rightThumb.VerticalAlignment = VerticalAlignment.Stretch;
        rightThumb.Margin = new Thickness(0, 0, 0, 14);
        WireResizeThumb(rightThumb, ResizeAxis.Horizontal);
        layer.Children.Add(rightThumb);

        var bottomThumb = CreateInvisibleThumb(Cursors.SizeNS);
        bottomThumb.Height = 8;
        bottomThumb.VerticalAlignment = VerticalAlignment.Bottom;
        bottomThumb.HorizontalAlignment = HorizontalAlignment.Stretch;
        bottomThumb.Margin = new Thickness(0, 0, 14, 0);
        WireResizeThumb(bottomThumb, ResizeAxis.Vertical);
        layer.Children.Add(bottomThumb);

        var cornerThumb = CreateCornerThumb();
        WireResizeThumb(cornerThumb, ResizeAxis.Both);
        layer.Children.Add(cornerThumb);

        return layer;
    }

    private static Thumb CreateInvisibleThumb(Cursor cursor)
    {
        var tpl = new ControlTemplate(typeof(Thumb));
        var rect = new FrameworkElementFactory(typeof(Border));
        rect.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        tpl.VisualTree = rect;

        return new Thumb
        {
            Cursor = cursor,
            Template = tpl
        };
    }

    private static Thumb CreateCornerThumb()
    {
        var tpl = new ControlTemplate(typeof(Thumb));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);

        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        grid.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom);
        grid.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 2, 2));

        var cornerVisual = new FrameworkElementFactory(typeof(Border));
        cornerVisual.SetValue(FrameworkElement.WidthProperty, 10.0);
        cornerVisual.SetValue(FrameworkElement.HeightProperty, 10.0);
        cornerVisual.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)));
        cornerVisual.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 2.5, 2.5));
        cornerVisual.SetValue(Border.CornerRadiusProperty, new CornerRadius(0, 0, 2, 0));

        grid.AppendChild(cornerVisual);
        bd.AppendChild(grid);
        tpl.VisualTree = bd;

        return new Thumb
        {
            Width = 18,
            Height = 18,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE,
            ToolTip = "Потяни за край, чтобы изменить размер",
            Template = tpl
        };
    }

    private enum ResizeAxis { Horizontal, Vertical, Both }

    private void WireResizeThumb(Thumb thumb, ResizeAxis axis)
    {
        thumb.DragStarted += (_, _) =>
        {
            if (_locked || _contentElement == null) return;
            WidgetSelected?.Invoke(WidgetKey);
            Win32.GetCursorPos(out _resizeStartCursor);

            var src = PresentationSource.FromVisual(this);
            if (src?.CompositionTarget != null)
            {
                _dpiScaleX = src.CompositionTarget.TransformToDevice.M11;
                _dpiScaleY = src.CompositionTarget.TransformToDevice.M22;
            }

            var layout = Settings.GetWidget(WidgetKey);
            _resizeStartCombinedScale = Math.Clamp(Settings.GlobalScale * layout.Scale, 0.50, 2.80);
            _resizeBaseWidth = Math.Max(40.0, _contentElement.ActualWidth);
            _resizeBaseHeight = Math.Max(40.0, _contentElement.ActualHeight);

            if (_sizeHintBadge != null && _sizeHintText != null)
            {
                _sizeHintText.Text = $"{(int)Math.Round(_resizeStartCombinedScale * 100)}%";
                _sizeHintBadge.Visibility = Visibility.Visible;
            }
        };

        thumb.DragDelta += (_, _) =>
        {
            if (_locked || _scaleRoot == null || _resizeBaseWidth <= 0) return;

            Win32.GetCursorPos(out var cur);
            double dx = (cur.X - _resizeStartCursor.X) / Math.Max(0.5, _dpiScaleX);
            double dy = (cur.Y - _resizeStartCursor.Y) / Math.Max(0.5, _dpiScaleY);

            double startScaledW = _resizeBaseWidth * _resizeStartCombinedScale;
            double startScaledH = _resizeBaseHeight * _resizeStartCombinedScale;

            double targetCombined = axis switch
            {
                ResizeAxis.Horizontal => (startScaledW + dx) / _resizeBaseWidth,
                ResizeAxis.Vertical => (startScaledH + dy) / _resizeBaseHeight,
                _ => ((startScaledW + dx) / _resizeBaseWidth + (startScaledH + dy) / _resizeBaseHeight) * 0.5
            };

            // Prevent scaling bigger than monitor dimensions
            var mon = GetTargetAllowedMonitorDipRect();
            double maxFitScale = Math.Min((mon.Width - 8) / _resizeBaseWidth, (mon.Height - 8) / _resizeBaseHeight);
            targetCombined = Math.Min(targetCombined, maxFitScale);

            double newWidgetScale = Math.Clamp(targetCombined / Math.Max(0.5, Settings.GlobalScale), 0.55, 2.50);
            newWidgetScale = Math.Round(newWidgetScale, 2);

            var layout = Settings.GetWidget(WidgetKey);
            if (Math.Abs(layout.Scale - newWidgetScale) >= 0.01)
            {
                layout.Scale = newWidgetScale;
                double combined = Math.Clamp(Settings.GlobalScale * layout.Scale, 0.55, 2.80);
                _scaleRoot.LayoutTransform = new ScaleTransform(combined, combined);

                if (_sizeHintText != null)
                    _sizeHintText.Text = $"{(int)Math.Round(combined * 100)}%";

                ClampToMonitorBounds(saveToSettings: true);
            }
        };

        thumb.DragCompleted += (_, _) =>
        {
            if (_sizeHintBadge != null)
                _sizeHintBadge.Visibility = Visibility.Collapsed;
            ClampToMonitorBounds(saveToSettings: true);
            LayoutChangedByUser?.Invoke();
        };
    }

    protected double FontSz(double basePx)
        => Math.Round(basePx * Math.Clamp(Settings.HudFontScale, 0.80, 1.35), 1);

    protected FontFamily GetUiFont()
    {
        string f = string.IsNullOrWhiteSpace(Settings.UiFontFamily) ? "Segoe UI Variable Text" : Settings.UiFontFamily;
        return new FontFamily($"{f}, Segoe UI");
    }

    protected FontFamily GetMonoFont()
    {
        string f = string.IsNullOrWhiteSpace(Settings.MonoFontFamily) ? "Cascadia Mono" : Settings.MonoFontFamily;
        return new FontFamily($"{f}, Consolas");
    }

    protected SolidColorBrush MakeBackdropBrush(double multiplier = 1.0, byte? r = null, byte? g = null, byte? b = null)
    {
        byte cr = r ?? (byte)(IsIRacingNative ? 0x1E : 0x0B);
        byte cg = g ?? (byte)(IsIRacingNative ? 0x20 : 0x0E);
        byte cb = b ?? (byte)(IsIRacingNative ? 0x26 : 0x14);
        double alpha = Math.Clamp(Settings.BackgroundOpacity * multiplier, 0.0, 0.96);
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255.0), cr, cg, cb));
        brush.Freeze();
        return brush;
    }

    protected SolidColorBrush MakeBorderBrush()
    {
        if (IsIRacingNative)
        {
            var b = new SolidColorBrush(Color.FromArgb(0xB8, 0x33, 0x37, 0x42));
            b.Freeze();
            return b;
        }
        double alpha = Math.Clamp(Settings.BackgroundOpacity * 0.34, 0.06, 0.28);
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255.0), 0xFF, 0xFF, 0xFF));
        brush.Freeze();
        return brush;
    }

    protected virtual void OnAppearanceUpdated() { }

    public void ApplyLayoutFromSettings()
    {
        var layout = Settings.GetWidget(WidgetKey);
        _isClamping = true;
        try
        {
            Left = layout.Left;
            Top = layout.Top;
        }
        finally
        {
            _isClamping = false;
        }

        Opacity = Math.Clamp(layout.Opacity, 0.25, 1.0);

        if (_scaleRoot != null)
        {
            double combined = Math.Clamp(Settings.GlobalScale * layout.Scale, 0.55, 2.80);
            _scaleRoot.LayoutTransform = new ScaleTransform(combined, combined);
        }

        OnAppearanceUpdated();
        ApplyLockState(Settings.Locked);
        ClampToMonitorBounds(saveToSettings: true);
    }

    public void ApplyLockState(bool locked)
    {
        _locked = locked;

        Visibility editVis = locked ? Visibility.Collapsed : Visibility.Visible;
        if (_editFrame != null) _editFrame.Visibility = editVis;
        if (_editPill != null) _editPill.Visibility = editVis;
        if (_resizeHandlesLayer != null) _resizeHandlesLayer.Visibility = editVis;
        if (locked && _sizeHintBadge != null) _sizeHintBadge.Visibility = Visibility.Collapsed;

        Cursor = locked ? Cursors.Arrow : Cursors.SizeAll;

        if (_initialized)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Win32.SetOverlayWindowStyle(hwnd, locked);
        }
    }

    public void EnsureTopmost()
    {
        if (!_initialized || !IsVisible) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    public bool IsEditMode => !_locked;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _initialized = true;
        var hwnd = new WindowInteropHelper(this).Handle;
        Win32.SetOverlayWindowStyle(hwnd, _locked);
    }

    private void OnOverlayMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_locked) return;
        WidgetSelected?.Invoke(WidgetKey);
        Focus();

        if (e.OriginalSource is DependencyObject d && FindAncestorThumb(d) != null)
            return;

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
                MagneticSnapToEdgesOrCenter();
                LayoutChangedByUser?.Invoke();
            }
            catch { /* Ignore drag cancel */ }
        }
    }

    private void OnOverlayPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_locked) return;
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10.0 : 1.0;

        switch (e.Key)
        {
            case Key.Left:
                Nudge(-step, 0);
                e.Handled = true;
                break;
            case Key.Right:
                Nudge(step, 0);
                e.Handled = true;
                break;
            case Key.Up:
                Nudge(0, -step);
                e.Handled = true;
                break;
            case Key.Down:
                Nudge(0, step);
                e.Handled = true;
                break;
        }
    }

    private static Thumb? FindAncestorThumb(DependencyObject? current)
    {
        while (current != null)
        {
            if (current is Thumb t) return t;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_isClamping || !_initialized || double.IsNaN(Left) || double.IsNaN(Top)) return;
        ClampToMonitorBounds(saveToSettings: true);
    }
}
