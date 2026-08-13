// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.VisualBasic.FileIO;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MS_SafetyManagementSystem.ImageViewer
{
    public partial class ImageViewerWindow : Window
    {
        #region Win32 — Acrylic 毛玻璃效果 + DWM 窗口阴影

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLIC = 4,
            ACCENT_ENABLE_HOSTBACKDROP = 5,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        private enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19,
        }

        /// <summary>
        /// 全屏深色模糊效果：DWM 将桌面内容模糊后叠加 60% 黑色，模拟半透明遮罩。
        /// </summary>
        private static void EnableFullscreenDarkBlur(IntPtr hwnd)
        {
            try
            {
                var accent = new AccentPolicy
                {
                    AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND,
                    AccentFlags = 0,
                    GradientColor = unchecked((int)0x99000000),
                    AnimationId = 0
                };

                var accentStructSize = Marshal.SizeOf(accent);
                var accentPtr = Marshal.AllocHGlobal(accentStructSize);
                Marshal.StructureToPtr(accent, accentPtr, false);

                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    Data = accentPtr,
                    SizeOfData = accentStructSize
                };

                SetWindowCompositionAttribute(hwnd, ref data);
                Marshal.FreeHGlobal(accentPtr);
            }
            catch (Exception ex) { Log.Debug(ex, "[ImageViewer] 全屏深色模糊效果设置失败"); }
        }

        #endregion

        #region 自定义非客户区（替代 WindowChrome）
        // 通过 WM_NCCALCSIZE + WM_NCHITTEST 手动接管窗口边框/标题栏行为，
        // 避免 WindowChrome.CornerRadius 与 AllowsTransparency 分层窗口的锯齿冲突

        private HwndSource? _hwndSource;

        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_NCHITTEST = 0x0084;

        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private const int ResizeHandleSize = 6;

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        internal const int DWMWCP_ROUND = 2;
        internal const int DWMWCP_DONOTROUND = 1;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_NCCALCSIZE:
                    handled = true;
                    return IntPtr.Zero;

                case WM_NCHITTEST:
                    if (_isFullScreenMode)
                    {
                        handled = true;
                        return (IntPtr)HTCLIENT;
                    }

                    // lParam 是物理像素屏幕坐标，需转为 DIP 后再与 WPF 坐标比较
                    var pt = PointFromLParam(lParam);
                    var source = PresentationSource.FromVisual(this);
                    double dpiX = 1.0, dpiY = 1.0;
                    if (source != null)
                    {
                        dpiX = source.CompositionTarget.TransformFromDevice.M11;
                        dpiY = source.CompositionTarget.TransformFromDevice.M22;
                    }
                    double x = pt.X * dpiX - this.Left;
                    double y = pt.Y * dpiY - this.Top;
                    double w = this.ActualWidth;
                    double h = this.ActualHeight;

                    bool left = x < ResizeHandleSize;
                    bool right = x > w - ResizeHandleSize;
                    bool top = y < ResizeHandleSize;
                    bool bottom = y > h - ResizeHandleSize;

                    if (top && left) { handled = true; return (IntPtr)HTTOPLEFT; }
                    if (top && right) { handled = true; return (IntPtr)HTTOPRIGHT; }
                    if (bottom && left) { handled = true; return (IntPtr)HTBOTTOMLEFT; }
                    if (bottom && right) { handled = true; return (IntPtr)HTBOTTOMRIGHT; }
                    if (left) { handled = true; return (IntPtr)HTLEFT; }
                    if (right) { handled = true; return (IntPtr)HTRIGHT; }
                    if (top) { handled = true; return (IntPtr)HTTOP; }
                    if (bottom) { handled = true; return (IntPtr)HTBOTTOM; }
                    if (y < 35 + ResizeHandleSize)
                    {
                        var hit = this.InputHitTest(new System.Windows.Point(x, y));
                        if (hit is Button || FindParentButton(hit as DependencyObject) != null)
                            return IntPtr.Zero;
                        handled = true;
                        return (IntPtr)HTCAPTION;
                    }
                    // 标题栏以下、边框以内的客户区 → HTCLIENT，由 WPF 自行处理。
                    // 避免 fallthrough 返回 IntPtr.Zero (HTNOWHERE) 导致整个窗口被当作调整大小区。
                    handled = true;
                    return (IntPtr)HTCLIENT;
            }
            return IntPtr.Zero;
        }

        private static System.Windows.Point PointFromLParam(IntPtr lParam)
        {
            int x = (short)(lParam.ToInt32() & 0xFFFF);
            int y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
            return new System.Windows.Point(x, y);
        }

        private static Button? FindParentButton(DependencyObject? element)
        {
            while (element != null)
            {
                if (element is Button btn) return btn;
                element = VisualTreeHelper.GetParent(element);
            }
            return null;
        }

        /// <summary>设置窗口圆角偏好。全屏时用 DONOTROUND 消除拐角透明，普通模式恢复 ROUND。</summary>
        internal void SetCornerPreference(int cornerPref)
        {
            try
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE,
                    ref cornerPref, sizeof(int));
            }
            catch (Exception ex) { Log.Debug(ex, "[ImageViewer] 设置窗口圆角失败"); }
        }

        #endregion

        #region 字段

        internal List<string> _imageFiles = new List<string>();
        internal int _currentIndex = -1;

        internal BitmapSource? _currentBitmap;
        internal int _originalWidth = 0;
        internal int _originalHeight = 0;
        private int _fileOriginalWidth = 0;
        private int _fileOriginalHeight = 0;
        private bool _initialFitDone = false;

        private readonly TaskCompletionSource<bool> _firstImageLoadedTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private float _rotationAngle = 0;
        private float _flipX = 1f;
        private float _flipY = 1f;
        internal float _zoomFactor = 1f;
        private float _panX = 0f;
        private float _panY = 0f;
        private UIElement _parentGrid = null!;
        private bool _isPanning = false;
        private Point _panStartPoint;
        private float _panStartX, _panStartY;
        private bool _panStartValid = false;
        private const float PanThreshold = 5f;

        private readonly Queue<(Point pos, long ticks)> _panVelocitySamples = new();
        private bool _inertiaActive;
        private double _inertiaVelocityX, _inertiaVelocityY;
        private const double InertiaDecay = 0.92;
        private const double InertiaStopThreshold = 0.5;

        private float _targetZoom;
        private bool _zoomChaseActive;
        private long _zoomLastFrameTicks;
        private long _lastZoomInputTick;
        private double _chaseRate = SingleStepRate;
        private const double SingleStepRate = 6.0;
        private const double ContinuousRate = 10.0;
        private const long ZoomInputGapTicks = 200 * TimeSpan.TicksPerMillisecond;

        private const float MinZoom = 0.20f;
        private const float MaxZoom = 8.0f;
        private const float ZoomStepExponent = 0.16f;
        private const float InitialDisplayRatio = 0.9f;

        internal bool _isFullScreenMode = false;
        internal bool PendingFullScreenLaunch = false;
        private double _savedBorderWidth, _savedBorderHeight;
        private Rect _savedWindowBounds;
        private Brush? _savedNormalBackground;

        private static readonly SolidColorBrush NormalBackgroundBrush =
            new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
        private static readonly SolidColorBrush FullScreenBackgroundBrush =
            new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00));

        private readonly Dictionary<string, BitmapSource> _originalCache = new(StringComparer.OrdinalIgnoreCase);
        private const int MaxOriginalCacheItems = 5;
        private readonly LinkedList<string> _originalCacheLru = new();
        private BitmapSource? _originalBitmap;
        private bool _isUsingOriginal = false;
        private string? _infoBarBaseText = null;
        private CancellationTokenSource? _orientationStatusCts;

        internal bool _isSlideShowPlaying = false;
        internal double _slideShowInterval = 3.0;
        private const double SlideShowMinInterval = 1.5;
        private const double SlideShowMaxInterval = 10.0;
        private const double SlideShowStep = 0.5;
        private CancellationTokenSource? _slideShowCts;
        private DateTime _lastNavigateTime = DateTime.MinValue;

        private CancellationTokenSource? _navigationCts;

        internal string _cachedFileName = "";
        internal string _cachedFileSizeStr = "";

        private const int MaxMainImageCacheSize = 20;
        private static readonly Dictionary<string, BitmapSource?> _mainImageCache =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> _mainImageCacheLru = new();
        private static readonly object _mainImageCacheLock = new();

        private static readonly int DecodeMaxWidth =
            (int)(SystemParameters.PrimaryScreenWidth * 1.5);

        private static int FastGetImageWidth(string filePath)
        {
            try
            {
                using var img = System.Drawing.Image.FromFile(filePath);
                return img.Width;
            }
            catch
            {
                return int.MaxValue;
            }
        }

        private static (int width, int height) GetFileImageSize(string filePath)
        {
            try
            {
                using var img = System.Drawing.Image.FromFile(filePath);
                return (img.Width, img.Height);
            }
            catch
            {
                return (0, 0);
            }
        }

        private CancellationTokenSource? _preloadDebounceCts;
        private bool _fitInProgress = false;

        // 快照遮罩：全屏↔普通切换时冻结 MainContentBorder 当前画面，
        // 盖住所有布局/背景/zoom 过渡帧，切换完成后移除
        private Image? _snapshotOverlay;

        #endregion

        #region 构造与初始化

        public ImageViewerWindow()
        {
            InitializeComponent();

            this.SourceInitialized += (_, _) =>
            {
                _hwndSource = (HwndSource)PresentationSource.FromVisual(this);
                _hwndSource.AddHook(WndProc);

                try
                {
                    int cornerPref = DWMWCP_ROUND;
                    DwmSetWindowAttribute(_hwndSource.Handle, DWMWA_WINDOW_CORNER_PREFERENCE,
                        ref cornerPref, sizeof(int));
                }
                catch (Exception ex) { Log.Debug(ex, "[ImageViewer] 设置窗口圆角失败(加载时)"); }
            };

            var screen = SystemParameters.WorkArea;
            this.Width = screen.Width * 0.5;
            this.Height = screen.Height * 0.5;

            SpiritThumbnailPanel.ThumbnailClicked += SpiritThumbnailPanel_ThumbnailClicked;

            _parentGrid = ContentGrid;

            WindowTitleBarControl.Loaded += (_, _) =>
            {
                var maximizeBtn = WindowTitleBarControl.FindName("MaximizeButton") as Button;
                if (maximizeBtn != null)
                {
                    maximizeBtn.PreviewMouseLeftButtonDown += (_, e) =>
                    {
                        e.Handled = true;
                        ToggleFullScreen();
                    };
                }
            };

            WindowTitleBarControl.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2 && e.OriginalSource is not Button)
                {
                    e.Handled = true;
                    ToggleFullScreen();
                }
            };

            this.StateChanged += (_, _) =>
            {
                if (this.WindowState == WindowState.Maximized)
                {
                    this.WindowState = WindowState.Normal;
                    if (!_isFullScreenMode)
                        ToggleFullScreen();
                }
            };

            this.MouseLeftButtonDown += (_, e) =>
            {
                if (_isFullScreenMode)
                {
                    ToggleFullScreen();
                    e.Handled = true;
                }
                else if (e.LeftButton == MouseButtonState.Pressed)
                {
                    this.DragMove();
                }
            };

            NormalBackgroundBrush.Freeze();
            FullScreenBackgroundBrush.Freeze();
            this.Background = NormalBackgroundBrush;
        }

        public ImageViewerWindow(List<string> imagePaths, int startIndex = 0) : this()
        {
            SetImageList(imagePaths, startIndex);
        }

        public void WaitForFirstImage()
        {
            if (_firstImageLoadedTcs.Task.IsCompleted) return;

            var frame = new DispatcherFrame();
            _firstImageLoadedTcs.Task.ContinueWith(_ => frame.Continue = false,
                TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.PushFrame(frame);
        }

        public async Task SetImageList(List<string> imagePaths, int startIndex = 0)
        {
            _imageFiles = imagePaths;
            _currentIndex = Math.Max(0, Math.Min(startIndex, _imageFiles.Count - 1));

            if (_currentIndex >= 0 && _currentIndex < _imageFiles.Count)
            {
                await LoadAndDisplayImage(_imageFiles[_currentIndex]);
            }
        }

        #endregion

        #region 图片加载与显示

        private async Task LoadAndDisplayImage(string filePath)
        {
            try
            {
                var bitmap = await GetOrLoadBitmapAsync(filePath);
                if (bitmap == null)
                {
                    _firstImageLoadedTcs.TrySetResult(true);
                    return;
                }

                (_fileOriginalWidth, _fileOriginalHeight) = GetFileImageSize(filePath);

                await Dispatcher.InvokeAsync(() =>
                {
                    _currentBitmap = bitmap;
                    _originalWidth = bitmap.PixelWidth;
                    _originalHeight = bitmap.PixelHeight;

                    if (!_initialFitDone)
                    {
                        _initialFitDone = true;
                        ResetViewTransform();
                        if (!_isFullScreenMode && !PendingFullScreenLaunch)
                            ApplyInitialWindowSize();
                    }

                    UpdateImageDisplay();
                    UpdateTitle(filePath);
                    if (_isFullScreenMode)
                    {
                        SpiritThumbnailPanel.NavigateTo(_currentIndex);
                        UpdateInfoBar();
                    }
                });

                TriggerPreload(_currentIndex);
                _firstImageLoadedTcs.TrySetResult(true);
            }
            catch
            {
                _firstImageLoadedTcs.TrySetResult(true);
            }
        }

        private async Task<BitmapSource?> LoadBitmapSourceAsync(string filePath, int? decodePixelWidth = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(filePath)) return null;

                    if (decodePixelWidth.HasValue)
                    {
                        int actualWidth = FastGetImageWidth(filePath);
                        if (actualWidth <= decodePixelWidth.Value)
                            decodePixelWidth = null;
                    }

                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(filePath);
                    if (decodePixelWidth.HasValue)
                        bmp.DecodePixelWidth = decodePixelWidth.Value;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
                catch { return null; }
            });
        }

        #endregion

        #region 渲染与显示更新

        internal void UpdateImageDisplay()
        {
            ImageViewer.Source = CurrentBitmap;
            _scaleZoomTransform.ScaleX = _zoomFactor;
            _scaleZoomTransform.ScaleY = _zoomFactor;
            _panTransform.X = _panX;
            _panTransform.Y = _panY;
            _rotateTransform.Angle = _rotationAngle;
            _flipTransform.ScaleX = _flipX;
            _flipTransform.ScaleY = _flipY;
        }

        #endregion

        #region 主图 LRU 缓存与预加载

        private static BitmapSource? GetFromMainCache(string filePath)
        {
            lock (_mainImageCacheLock)
            {
                if (_mainImageCache.TryGetValue(filePath, out var bmp))
                {
                    _mainImageCacheLru.Remove(filePath);
                    _mainImageCacheLru.AddLast(filePath);
                    return bmp;
                }
                return null;
            }
        }

        private static void AddToMainCache(string filePath, BitmapSource? bmp)
        {
            if (bmp == null) return;
            lock (_mainImageCacheLock)
            {
                if (_mainImageCache.ContainsKey(filePath))
                {
                    _mainImageCache[filePath] = bmp;
                    _mainImageCacheLru.Remove(filePath);
                    _mainImageCacheLru.AddLast(filePath);
                    return;
                }
                _mainImageCache[filePath] = bmp;
                _mainImageCacheLru.AddLast(filePath);
                while (_mainImageCache.Count > MaxMainImageCacheSize)
                {
                    var oldest = _mainImageCacheLru.First;
                    if (oldest == null) break;
                    _mainImageCache.Remove(oldest.Value);
                    _mainImageCacheLru.RemoveFirst();
                }
            }
        }

        private async Task<BitmapSource?> GetOrLoadBitmapAsync(string filePath)
        {
            var cached = GetFromMainCache(filePath);
            if (cached != null) return cached;
            var bmp = await LoadBitmapSourceAsync(filePath, DecodeMaxWidth);
            if (bmp != null) AddToMainCache(filePath, bmp);
            return bmp;
        }

        private async void TriggerPreload(int currentIndex)
        {
            _preloadDebounceCts?.Cancel();
            _preloadDebounceCts = new CancellationTokenSource();
            var ct = _preloadDebounceCts.Token;

            try { await Task.Delay(200, ct); }
            catch (TaskCanceledException) { return; }

            if (currentIndex != _currentIndex) return;

            for (int i = 1; i <= 2; i++)
            {
                int idx = currentIndex - i;
                if (idx >= 0)
                {
                    string path = _imageFiles[idx];
                    if (GetFromMainCache(path) == null)
                        _ = PreloadOneAsync(path);
                }
            }
            for (int i = 1; i <= 2; i++)
            {
                int idx = currentIndex + i;
                if (idx < _imageFiles.Count)
                {
                    string path = _imageFiles[idx];
                    if (GetFromMainCache(path) == null)
                        _ = PreloadOneAsync(path);
                }
            }
        }

        private async Task PreloadOneAsync(string filePath)
        {
            try
            {
                var bmp = await LoadBitmapSourceAsync(filePath, DecodeMaxWidth);
                if (bmp != null) AddToMainCache(filePath, bmp);
            }
            catch (Exception ex) { Log.Debug(ex, "[ImageViewer] 图片缓存预加载失败: {Path}", filePath); }
        }

        #endregion

        #region 导航

        internal async void Navigate(int delta)
        {
            if (_imageFiles.Count == 0 || _currentIndex < 0) return;

            _navigationCts?.Cancel();
            _navigationCts = new CancellationTokenSource();
            var ct = _navigationCts.Token;

            int newIndex = _currentIndex + delta;
            if (newIndex < 0 || newIndex >= _imageFiles.Count) return;
            if (newIndex == _currentIndex) return;

            _lastNavigateTime = DateTime.Now;
            string newPath = _imageFiles[newIndex];

            DowngradeFromOriginal();
            ResetRotationOnly();

            try
            {
                var newBitmap = await GetOrLoadBitmapAsync(newPath);
                if (ct.IsCancellationRequested) return;

                if (newBitmap == null)
                {
                    ShowTempMessage($"图片加载失败: {Path.GetFileName(newPath)}");
                    _currentIndex = newIndex;
                    return;
                }

                (_fileOriginalWidth, _fileOriginalHeight) = GetFileImageSize(newPath);

                _currentBitmap = newBitmap;
                _originalWidth = newBitmap.PixelWidth;
                _originalHeight = newBitmap.PixelHeight;
                _currentIndex = newIndex;
                UpdateTitle(newPath);
                UpdateImageDisplay();

                if (_isFullScreenMode)
                {
                    SpiritThumbnailPanel.NavigateTo(_currentIndex);
                    UpdateInfoBar();
                }

                UpdateVirtualImageRect();
                TriggerPreload(_currentIndex);
            }
            catch (Exception ex)
            {
                ShowTempMessage($"切换失败: {ex.Message}");
            }
        }

        internal async Task NavigateToIndex(int index)
        {
            if (index < 0 || index >= _imageFiles.Count || index == _currentIndex) return;

            _navigationCts?.Cancel();
            _navigationCts = new CancellationTokenSource();
            var ct = _navigationCts.Token;

            string filePath = _imageFiles[index];

            try
            {
                DowngradeFromOriginal();
                ResetRotationOnly();

                var newBitmap = await GetOrLoadBitmapAsync(filePath);
                if (ct.IsCancellationRequested) return;

                if (newBitmap != null)
                {
                    (_fileOriginalWidth, _fileOriginalHeight) = GetFileImageSize(filePath);

                    _currentBitmap = newBitmap;
                    _originalWidth = newBitmap.PixelWidth;
                    _originalHeight = newBitmap.PixelHeight;
                    _currentIndex = index;
                    UpdateTitle(filePath);
                    UpdateImageDisplay();
                }
                else
                {
                    ShowTempMessage($"图片加载失败: {Path.GetFileName(filePath)}");
                    return;
                }

                if (_isFullScreenMode)
                {
                    SpiritThumbnailPanel.NavigateTo(_currentIndex);
                    UpdateInfoBar();
                }

                UpdateVirtualImageRect();
                TriggerPreload(_currentIndex);
            }
            catch (Exception ex)
            {
                ShowTempMessage($"加载失败: {ex.Message}");
            }
        }

        private void UpdateTitle(string filePath)
        {
            UpdateFileInfoCache(filePath);
            UpdateTitleBar(filePath);
        }

        private void UpdateFileInfoCache(string filePath)
        {
            try
            {
                var fi = new FileInfo(filePath);
                _cachedFileName = fi.Name;
                long size = fi.Length;
                _cachedFileSizeStr = size < 1024 * 1024
                    ? $"{size / 1024.0:F1} KB"
                    : $"{size / (1024.0 * 1024.0):F1} MB";
            }
            catch
            {
                _cachedFileName = Path.GetFileName(filePath);
                _cachedFileSizeStr = "?";
            }
        }

        private void UpdateTitleBar(string filePath)
        {
            string indexStr = "";
            if (_imageFiles.Count > 0 && _currentIndex >= 0)
                indexStr = $" _ 第 {_currentIndex + 1} / {_imageFiles.Count} 张";

            string title = $"斜月图片查看器 _ {_cachedFileName} _ {_fileOriginalWidth}×{_fileOriginalHeight} _ {_cachedFileSizeStr}{indexStr}";

            if (_isSlideShowPlaying)
                title += $" [▶ 幻灯片 {_slideShowInterval:F1}s]";

            this.Title = title;
            WindowTitleBarControl.Title = title;
        }

        #endregion

        #region 幻灯片

        internal async Task StartSlideShow(int startFrom)
        {
            if (_isSlideShowPlaying) return;
            if (_imageFiles.Count == 0) return;

            if (startFrom >= 0 && startFrom < _imageFiles.Count)
            {
                _currentIndex = startFrom;
                await NavigateToIndex(startFrom);
                await Task.Delay(50);
            }

            _isSlideShowPlaying = true;
            _slideShowCts = new CancellationTokenSource();
            _ = RunSlideShowAsync(_slideShowCts.Token);
            UpdateSlideShowTitle();
        }

        internal void StopSlideShow()
        {
            if (!_isSlideShowPlaying) return;
            _slideShowCts?.Cancel();
            _isSlideShowPlaying = false;
            UpdateSlideShowTitle();
        }

        private async Task RunSlideShowAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_slideShowInterval), ct);
                }
                catch (TaskCanceledException) { break; }

                if (_currentIndex >= _imageFiles.Count - 1)
                {
                    Dispatcher.Invoke(() => StopSlideShow());
                    break;
                }

                Dispatcher.Invoke(() => Navigate(1));
            }
        }

        private void UpdateSlideShowTitle()
        {
            if (_isSlideShowPlaying)
            {
                var baseTitle = this.Title.Split(" [")[0];
                string newTitle = baseTitle + $" [▶ 幻灯片 {_slideShowInterval:F1}s]";
                this.Title = newTitle;
                WindowTitleBarControl.Title = newTitle;
            }
            else
            {
                _initialFitDone = true;
                if (this.Title.Contains(" [▶"))
                {
                    string clean = this.Title.Split(" [")[0];
                    this.Title = clean;
                    WindowTitleBarControl.Title = clean;
                }
            }
        }

        #endregion

        #region 虚拟窗口状态（全屏↔普通模式转换）

        private void UpdateVirtualImageRect() { }

        #endregion
        #region 全屏窗口

        /// <summary>
        /// 全屏模式启动时的窗口预配置：在 Show() 之前执行，将窗口直接设为全屏状态。
        /// 全屏与普通窗口是并列状态，而非"从普通切换过去"——窗口首帧即全屏，零闪现。
        /// </summary>
        internal void ConfigureForFullscreenLaunch()
        {
            var screenArea = ImageViewerHelper.GetScreenWorkingArea(this);
            _savedWindowBounds = new Rect(this.Left, this.Top, this.Width, this.Height);

            // 窗口定位：手动铺满屏幕工作区（非 WindowState.Maximized，分层窗口限制）
            this.WindowStartupLocation = WindowStartupLocation.Manual;
            this.Left = screenArea.Left;
            this.Top = screenArea.Top;
            this.Width = screenArea.Width;
            this.Height = screenArea.Height;

            // 视觉：深色半透明背景，无边框/标题栏
            _savedNormalBackground = MainContentBorder.Background;
            MainContentBorder.Background = null;
            MainContentBorder.BorderThickness = new Thickness(0);
            MainContentBorder.ClipToBounds = false;
            ImageClipBorder.ClipToBounds = false;
            this.Background = FullScreenBackgroundBrush;
            WindowTitleBarControl.Visibility = Visibility.Collapsed;

            // 内容区铺满屏幕
            MainContentBorder.Width = screenArea.Width;
            MainContentBorder.Height = screenArea.Height;
            MainContentBorder.HorizontalAlignment = HorizontalAlignment.Left;
            MainContentBorder.VerticalAlignment = VerticalAlignment.Top;
            MainContentBorder.Margin = new Thickness(0);
            _savedBorderWidth = screenArea.Width;
            _savedBorderHeight = screenArea.Height;

            // 极小初始缩放（动画从 2% → 90%）
            _zoomFactor = 0.02f;
            _panX = 0f; _panY = 0f;
            _scaleZoomTransform.ScaleX = 0.02;
            _scaleZoomTransform.ScaleY = 0.02;
            _panTransform.X = 0; _panTransform.Y = 0;
            DowngradeFromOriginal();
            UpdateImageDisplay();

            // 阻止 LoadAndDisplayImage 中 ResetViewTransform() 将 zoom 重置为 1.0
            _initialFitDone = true;
        }

        /// <summary>
        /// 全屏入场动画：等首图加载完成后，缩略图/信息条就位，zoom 从 2% 动画到 90%。
        /// 仅用于全屏初始打开场景；ToggleFullScreen 不走此路径。
        /// </summary>
        internal async Task AnimateFullscreenEntry()
        {
            await _firstImageLoadedTcs.Task;
            if (_currentBitmap == null) return;

            SpiritThumbnailPanel.Images = _imageFiles;
            SpiritThumbnailPanel.NavigateTo(_currentIndex);
            UpdateInfoBar();

            var animTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var duration = TimeSpan.FromMilliseconds(500);
            var zoomAnim = new DoubleAnimation(0.9, duration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            zoomAnim.Completed += (_, _) =>
            {
                _scaleZoomTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                _scaleZoomTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _scaleZoomTransform.ScaleX = 0.9;
                _scaleZoomTransform.ScaleY = 0.9;
                _zoomFactor = 0.9f;
                FullScreenOverlay.Visibility = Visibility.Visible;
                _isFullScreenMode = true;
                animTcs.TrySetResult(true);
            };
            _scaleZoomTransform.BeginAnimation(ScaleTransform.ScaleXProperty, zoomAnim);
            _scaleZoomTransform.BeginAnimation(ScaleTransform.ScaleYProperty, zoomAnim);
            await animTcs.Task;
        }

        internal void ToggleFullScreen()
        {
            // —— 快照遮罩：冻结当前 MainContentBorder 画面，像素级对齐 ——
            // 窗口定位后置到 Loaded 回调：遮罩存续期间窗口不移动，
            // 根除遮罩随窗口位移导致的"图片瞬移到左侧"问题。
            double snapshotScreenX = 0, snapshotScreenY = 0;
            CaptureAndShowSnapshot(this.Left, this.Top, out snapshotScreenX, out snapshotScreenY);

            // 延迟窗口定位参数
            double? deferredW = null, deferredH = null, deferredLeft = null, deferredTop = null;

            if (_isFullScreenMode)
            {
                // === 退出全屏：视觉变更 + 计算窗口位置参数 ===
                DowngradeFromOriginal();
                var renderRect = ImageViewer.TransformToAncestor(this).TransformBounds(
                    new Rect(0, 0, ImageViewer.ActualWidth, ImageViewer.ActualHeight));
                double imageScreenCenterX = this.Left + renderRect.X + renderRect.Width / 2.0;
                double imageScreenCenterY = this.Top + renderRect.Y + renderRect.Height / 2.0;
                double newW = renderRect.Width + 2;
                double newH = renderRect.Height + 35 + 2;
                newW = Math.Max(newW, MinWidth);
                newH = Math.Max(newH, MinHeight);

                StopZoomSpring();
                _zoomFactor = 1.0f; _panX = 0f; _panY = 0f;
                _scaleZoomTransform.ScaleX = 1.0; _scaleZoomTransform.ScaleY = 1.0;
                _panTransform.X = 0; _panTransform.Y = 0;

                FullScreenOverlay.Visibility = Visibility.Collapsed;
                this.Background = NormalBackgroundBrush;
                MainContentBorder.Background = _savedNormalBackground;
                MainContentBorder.BorderThickness = new Thickness(1);
                MainContentBorder.ClipToBounds = true;
                ImageClipBorder.ClipToBounds = true;
                MainContentBorder.Width = double.NaN;
                MainContentBorder.Height = double.NaN;
                MainContentBorder.Margin = new Thickness(0);
                MainContentBorder.HorizontalAlignment = HorizontalAlignment.Stretch;
                MainContentBorder.VerticalAlignment = VerticalAlignment.Stretch;
                WindowTitleBarControl.Visibility = Visibility.Visible;
                SetCornerPreference(DWMWCP_ROUND);
                ((Grid)_parentGrid).RowDefinitions[0].Height = new GridLength(35);

                // 计算窗口位置（不立即应用）
                var screenArea = ImageViewerHelper.GetScreenWorkingArea(this);
                bool needsReset = renderRect.Width > screenArea.Width || renderRect.Height > screenArea.Height
                    || renderRect.Right <= 0 || renderRect.Bottom <= 0
                    || renderRect.Left >= screenArea.Width || renderRect.Top >= screenArea.Height;

                if (needsReset)
                {
                    ResetViewTransform();
                    CalculateWindowSizeForImage(out newW, out newH, screenArea);
                    deferredW = newW; deferredH = newH;
                    deferredLeft = screenArea.Left + (screenArea.Width - newW) / 2.0;
                    deferredTop = screenArea.Top + (screenArea.Height - newH) / 2.0;
                }
                else
                {
                    deferredW = newW; deferredH = newH;
                    deferredLeft = imageScreenCenterX - newW / 2.0;
                    deferredTop = imageScreenCenterY - (36.0 + renderRect.Height / 2.0);
                }
            }
            else
            {
                // === 进入全屏：视觉变更 + 保存定位参数 ===
                _savedBorderWidth = MainContentBorder.ActualWidth - 2;
                _savedBorderHeight = MainContentBorder.ActualHeight - 2 - 35;
                _savedWindowBounds = new Rect(this.Left, this.Top, this.ActualWidth, this.ActualHeight);
                _savedNormalBackground = MainContentBorder.Background;
                var screenArea = ImageViewerHelper.GetScreenWorkingArea(this);
                double marginLeft = this.Left + 1 - screenArea.Left;
                double marginTop = this.Top + 36 - screenArea.Top;

                MainContentBorder.Background = null;
                MainContentBorder.BorderThickness = new Thickness(0);
                MainContentBorder.ClipToBounds = false;
                ImageClipBorder.ClipToBounds = false;
                MainContentBorder.Width = _savedBorderWidth;
                MainContentBorder.Height = _savedBorderHeight;
                MainContentBorder.HorizontalAlignment = HorizontalAlignment.Left;
                MainContentBorder.VerticalAlignment = VerticalAlignment.Top;
                MainContentBorder.Margin = new Thickness(marginLeft, marginTop, 0, 0);
                WindowTitleBarControl.Visibility = Visibility.Collapsed;
                ((Grid)_parentGrid).RowDefinitions[0].Height = new GridLength(0);
                this.Background = FullScreenBackgroundBrush;
                SetCornerPreference(DWMWCP_DONOTROUND);
                FullScreenOverlay.Visibility = Visibility.Visible;
                SpiritThumbnailPanel.Images = _imageFiles;
                SpiritThumbnailPanel.NavigateTo(_currentIndex);
                if (_zoomFactor > 1.0f) UpgradeToOriginal();
                UpdateInfoBar();

                deferredW = screenArea.Width; deferredH = screenArea.Height;
                deferredLeft = screenArea.Left; deferredTop = screenArea.Top;
            }
            _isFullScreenMode = !_isFullScreenMode;

            // 布局完成后：移除遮罩 + 应用窗口定位（两步合并，单次渲染帧完成）
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RemoveSnapshotOverlay();
                if (deferredW.HasValue)
                {
                    this.Width = deferredW!.Value; this.Height = deferredH!.Value;
                    this.Left = deferredLeft!.Value; this.Top = deferredTop!.Value;
                    if (!_isFullScreenMode) this.WindowStartupLocation = WindowStartupLocation.Manual;
                }
            }), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 捕获 MainContentBorder 当前渲染画面 → 创建全屏级遮罩 Image。
        /// 记录原始窗口屏幕位置，供后续 Margin 修正。
        /// </summary>
        private void CaptureAndShowSnapshot(double windowLeft, double windowTop,
            out double screenX, out double screenY)
        {
            screenX = 0; screenY = 0;
            try
            {
                int w = (int)MainContentBorder.ActualWidth;
                int h = (int)MainContentBorder.ActualHeight;
                if (w <= 0 || h <= 0) return;

                var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(MainContentBorder);
                bmp.Freeze();

                var offset = MainContentBorder.TransformToAncestor(
                    (Visual)VisualTreeHelper.GetParent(MainContentBorder)!).Transform(new Point(0, 0));
                screenX = windowLeft + offset.X;
                screenY = windowTop + offset.Y;

                _snapshotOverlay = new Image
                {
                    Source = bmp,
                    Stretch = Stretch.None,
                    Width = w,
                    Height = h,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(offset.X, offset.Y, 0, 0),
                };
                Panel.SetZIndex(_snapshotOverlay, 9999);
                ((Grid)MainContentBorder.Parent).Children.Add(_snapshotOverlay);
            }
            catch { /* 捕获失败不影响切换流程 */ }
        }

        /// <summary>
        /// 移除快照遮罩，新布局在下方已就位。
        /// </summary>
        private void RemoveSnapshotOverlay()
        {
            if (_snapshotOverlay == null) return;
            var parent = MainContentBorder.Parent as Grid;
            if (parent == null) return;
            parent.Children.Remove(_snapshotOverlay);
            _snapshotOverlay = null;
        }

        #endregion

        #region 全屏信息条

        private void UpdateInfoBar()
        {
            if (_currentIndex < 0 || _currentIndex >= _imageFiles.Count) { InfoTextBlock.Text = ""; return; }

            string fileName = _cachedFileName;
            if (string.IsNullOrEmpty(fileName))
                fileName = Path.GetFileName(_imageFiles[_currentIndex]);
            string format = Path.GetExtension(fileName).TrimStart('.').ToUpper();
            string resolution = $"{_fileOriginalWidth}×{_fileOriginalHeight}";
            string fileSizeStr = _cachedFileSizeStr;

            int total = _imageFiles.Count;
            int current = _currentIndex + 1;
            string indexStr = $"第 {current} / {total} 张";

            if (fileName.Length > 30) fileName = fileName.Substring(0, 27) + "...";

            InfoTextBlock.Text = $"文件名：{fileName} | 格式：{format} | 分辨率：{resolution} | 大小：{fileSizeStr} | {indexStr}";
            _infoBarBaseText = InfoTextBlock.Text;
        }

        private async void ShowOrientationStatus(string text)
        {
            _orientationStatusCts?.Cancel();
            _orientationStatusCts = new CancellationTokenSource();
            var ct = _orientationStatusCts.Token;

            if (_infoBarBaseText == null) _infoBarBaseText = InfoTextBlock.Text;
            InfoTextBlock.Text = _infoBarBaseText + " " + text;

            try
            {
                await Task.Delay(2500, ct);
                if (_infoBarBaseText != null) InfoTextBlock.Text = _infoBarBaseText;
            }
            catch (TaskCanceledException) { /* 幻灯片切换取消属正常流程 */ }
        }

        private void UpdateSlideShowStatusDisplay()
        {
            if (_isSlideShowPlaying)
            {
                SlideShowStatusText.Visibility = Visibility.Visible;
                SlideShowStatusText.Text = $"当前为幻灯片播放模式，按 F7 / F8 调整时间间隔，当前时间间隔为 {_slideShowInterval:F1} 秒";
                PrevButtonBorder.Visibility = Visibility.Collapsed;
                NextButtonBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                SlideShowStatusText.Visibility = Visibility.Collapsed;
                PrevButtonBorder.Visibility = Visibility.Visible;
                NextButtonBorder.Visibility = Visibility.Visible;
            }
        }

        private void SpiritThumbnailPanel_ThumbnailClicked(int index)
        {
            _ = NavigateToIndex(index);
        }

        private void PrevButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Navigate(-1);
            e.Handled = true;
        }

        private void NextButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Navigate(1);
            e.Handled = true;
        }

        #endregion

        #region 图片显示区计算与窗口贴合

        private void ApplyInitialWindowSize()
        {
            if (_currentBitmap == null) return;

            Rect screenArea;
            var source = PresentationSource.FromVisual(this);
            if (source != null)
                screenArea = ImageViewerHelper.GetScreenWorkingArea(this);
            else
                screenArea = new Rect(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Top,
                    SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);

            double targetDim = Math.Min(screenArea.Width, screenArea.Height) * 0.5;
            double imageW = _currentBitmap.PixelWidth;
            double imageH = _currentBitmap.PixelHeight;
            double imgRatio = imageW / imageH;
            double screenRatio = screenArea.Width / screenArea.Height;

            double displayW, displayH;
            if (imgRatio > screenRatio) { displayH = targetDim; displayW = imageW * (displayH / imageH); }
            else { displayW = targetDim; displayH = imageH * (displayW / imageW); }

            double newW = Math.Max(Math.Min(displayW + 2, screenArea.Width * 0.95), MinWidth);
            double newH = Math.Max(Math.Min(displayH + 35 + 2, screenArea.Height * 0.95), MinHeight);

            this.Width = newW; this.Height = newH;
            this.Left = screenArea.Left + (screenArea.Width - newW) / 2.0;
            this.Top = screenArea.Top + (screenArea.Height - newH) / 2.0;
            this.WindowStartupLocation = WindowStartupLocation.Manual;
        }

        private void CalculateWindowSizeForImage(out double windowW, out double windowH, Rect screenArea)
        {
            var bmp = CurrentBitmap ?? _currentBitmap;
            if (bmp == null) { windowW = screenArea.Width * 0.5; windowH = screenArea.Height * 0.5; return; }

            double targetDim = Math.Min(screenArea.Width, screenArea.Height) * 0.5;
            double imageW = bmp.PixelWidth;
            double imageH = bmp.PixelHeight;
            int rot = ((int)_rotationAngle % 360 + 360) % 360;
            if (rot % 180 != 0) (imageW, imageH) = (imageH, imageW);

            double displayW, displayH;
            double imgRatio = imageW / imageH;
            double screenRatio = screenArea.Width / screenArea.Height;
            if (imgRatio > screenRatio) { displayH = targetDim; displayW = imageW * (displayH / imageH); }
            else { displayW = targetDim; displayH = imageH * (displayW / imageW); }

            windowW = Math.Max(Math.Min(displayW + 2, screenArea.Width * 0.95), MinWidth);
            windowH = Math.Max(Math.Min(displayH + 35 + 2, screenArea.Height * 0.95), MinHeight);
        }

        internal async void FitWindowToImage()
        {
            if (_fitInProgress) return;
            if (_isFullScreenMode) return;
            if (_currentBitmap == null) return;
            StopZoomSpring();

            var bmp = CurrentBitmap ?? _currentBitmap;
            if (bmp == null || bmp.PixelWidth <= 0 || bmp.PixelHeight <= 0) return;

            _fitInProgress = true;
            try
            {
                double imageW = bmp.PixelWidth;
                double imageH = bmp.PixelHeight;
                int rot = ((int)_rotationAngle % 360 + 360) % 360;
                if (rot % 180 != 0) (imageW, imageH) = (imageH, imageW);

                if (Math.Abs(_scaleZoomTransform.ScaleX - 1.0) > 0.01)
                {
                    if (_isUsingOriginal) DowngradeFromOriginal();
                    var zoomTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var zoomAnim = new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.2))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                    zoomAnim.Completed += (_, _) =>
                    {
                        _scaleZoomTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                        _scaleZoomTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                        _scaleZoomTransform.ScaleX = 1.0; _scaleZoomTransform.ScaleY = 1.0;
                        _zoomFactor = 1.0f; zoomTcs.TrySetResult(true);
                    };
                    _scaleZoomTransform.BeginAnimation(ScaleTransform.ScaleXProperty, zoomAnim);
                    _scaleZoomTransform.BeginAnimation(ScaleTransform.ScaleYProperty, zoomAnim);
                    await zoomTcs.Task;
                }

                if (Math.Abs(_panX) > 0.5f || Math.Abs(_panY) > 0.5f)
                {
                    var panTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var panXAnim = new DoubleAnimation(0, TimeSpan.FromSeconds(0.15))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                    var panYAnim = new DoubleAnimation(0, TimeSpan.FromSeconds(0.15))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                    panYAnim.Completed += (_, _) =>
                    {
                        _panTransform.BeginAnimation(TranslateTransform.XProperty, null);
                        _panTransform.BeginAnimation(TranslateTransform.YProperty, null);
                        _panTransform.X = 0; _panTransform.Y = 0;
                        _panX = 0f; _panY = 0f; panTcs.TrySetResult(true);
                    };
                    _panTransform.BeginAnimation(TranslateTransform.XProperty, panXAnim);
                    _panTransform.BeginAnimation(TranslateTransform.YProperty, panYAnim);
                    await panTcs.Task;
                }

                double containerW = ImageClipBorder.ActualWidth;
                double containerH = ImageClipBorder.ActualHeight;
                if (containerW <= 0 || containerH <= 0)
                { await Task.Delay(30); containerW = ImageClipBorder.ActualWidth; containerH = ImageClipBorder.ActualHeight; }
                if (containerW <= 0 || containerH <= 0) { ShowTempMessage("贴合失败，请稍后重试"); return; }

                double uniformScale = Math.Min(containerW / imageW, containerH / imageH);
                double newWidth = Math.Max(Math.Min(imageW * uniformScale + (this.ActualWidth - containerW), this.ActualWidth), MinWidth);
                double newHeight = Math.Max(Math.Min(imageH * uniformScale + (this.ActualHeight - containerH), this.ActualHeight), MinHeight);

                var screenArea = ImageViewerHelper.GetScreenWorkingArea(this);
                if (this.Left + newWidth > screenArea.Right) this.Left = screenArea.Right - newWidth;
                if (this.Top + newHeight > screenArea.Bottom) this.Top = screenArea.Bottom - newHeight;

                var windowTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var wAnim = new DoubleAnimation(newWidth, TimeSpan.FromSeconds(0.35))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                var hAnim = new DoubleAnimation(newHeight, TimeSpan.FromSeconds(0.35))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                hAnim.Completed += (_, _) =>
                { this.BeginAnimation(Window.WidthProperty, null); this.BeginAnimation(Window.HeightProperty, null);
                  this.Width = newWidth; this.Height = newHeight; windowTcs.TrySetResult(true); };
                this.BeginAnimation(Window.WidthProperty, wAnim);
                this.BeginAnimation(Window.HeightProperty, hAnim);
                await windowTcs.Task;
            }
            finally { _fitInProgress = false; }
        }

        #endregion

        #region 导航后同步

        private async void SafeCopyImageToClipboard()
        {
            string currentFile = (_currentIndex >= 0 && _currentIndex < _imageFiles.Count) ? _imageFiles[_currentIndex] : "";
            if (string.IsNullOrEmpty(currentFile) || !File.Exists(currentFile)) return;
            try
            {
                BitmapSource? fullResBitmap = _originalBitmap ?? await LoadBitmapSourceAsync(currentFile);
                if (fullResBitmap == null) return;
                var dataObj = new DataObject();
                dataObj.SetImage(fullResBitmap);
                var fileList = new System.Collections.Specialized.StringCollection();
                fileList.Add(currentFile);
                dataObj.SetFileDropList(fileList);
                Clipboard.SetDataObject(dataObj, true);
                ShowCopyToast();
            }
            catch (Exception ex) { Log.Warning(ex, "[ImageViewer] 复制图片到剪贴板失败"); }
        }

        private async void ShowCopyToast()
        {
            CopyToast.BeginAnimation(UIElement.OpacityProperty, null);
            CopyToast.Opacity = 0; CopyToast.Visibility = Visibility.Visible;
            CopyToastText.Text = "已复制到剪贴板";
            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200));
            CopyToast.BeginAnimation(UIElement.OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);
            await Task.Delay(2000);
            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (s, e) => CopyToast.Visibility = Visibility.Collapsed;
            CopyToast.BeginAnimation(UIElement.OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
        }

        private async void ShowTempMessage(string message, TimeSpan? duration = null)
        {
            if (duration == null) duration = TimeSpan.FromSeconds(1.5);
            CopyToast.BeginAnimation(UIElement.OpacityProperty, null);
            CopyToast.Opacity = 0; CopyToast.Visibility = Visibility.Visible;
            CopyToastText.Text = message;
            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200));
            CopyToast.BeginAnimation(UIElement.OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);
            await Task.Delay(duration.Value);
            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (s, e) => CopyToast.Visibility = Visibility.Collapsed;
            CopyToast.BeginAnimation(UIElement.OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
        }

        #endregion

        #region 删除图片

        private async void DeleteCurrentImage()
        {
            if (_imageFiles.Count == 0 || _currentIndex < 0) return;
            string filePath = _imageFiles[_currentIndex];
            if (!File.Exists(filePath)) return;
            try { FileSystem.DeleteFile(filePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); }
            catch (Exception ex) { MessageBox.Show($"删除失败：{ex.Message}", "错误"); return; }

            _currentBitmap = null;
            if (_isFullScreenMode) SpiritThumbnailPanel.RemoveAt(_currentIndex);
            _imageFiles.RemoveAt(_currentIndex);
            if (_imageFiles.Count == 0) { Close(); return; }

            int newIndex = Math.Min(_currentIndex, _imageFiles.Count - 1);
            _currentIndex = newIndex;
            await LoadAndDisplayImage(_imageFiles[_currentIndex]);
        }

        #endregion

        #region 窗口事件

        protected override void OnClosed(EventArgs e)
        {
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource = null;
            _slideShowCts?.Cancel();
            _currentBitmap = null;
            _originalBitmap = null;
            _originalCache.Clear();
            _originalCacheLru.Clear();
            lock (_mainImageCacheLock) { _mainImageCache.Clear(); _mainImageCacheLru.Clear(); }
            base.OnClosed(e);
        }

        #endregion
    }
}
