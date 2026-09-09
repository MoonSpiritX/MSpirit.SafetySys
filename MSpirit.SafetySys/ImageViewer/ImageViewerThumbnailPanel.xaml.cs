// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using SkiaSharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MS_SafetyManagementSystem.ImageViewer
{
    /// <summary>
    /// 缩略图面板 — 水平滚动缩略图条，仿 Picasa 3 风格。
    /// 2026-07-02 重构：稳定列表(消除闪烁) + 固定项宽估算居中(消除 UpdateLayout 遍历)
    ///             + SkiaSharp 像素直拷解码(消除 PNG 编码绕路) + 选中项高亮。
    /// </summary>
    public partial class ImageViewerThumbnailPanel : UserControl
    {
        // 依赖属性
        public static readonly DependencyProperty ImagesProperty =
            DependencyProperty.Register("Images", typeof(IEnumerable), typeof(ImageViewerThumbnailPanel),
                new PropertyMetadata(null, OnImagesChanged));

        public static readonly DependencyProperty SelectedIndexProperty =
            DependencyProperty.Register("SelectedIndex", typeof(int), typeof(ImageViewerThumbnailPanel),
                new PropertyMetadata(-1, OnSelectedIndexChanged));

        // 固定项宽度：Border 54 + 左右 margin 各 1 = 56（用于估算居中，避免 UpdateLayout 遍历）
        private const double ItemWidth = 56.0;
        // 兜底半径（容器未就绪时使用）
        private const int FallbackVisibleRadius = 17;
        // 加载缓冲：在可见半径外多加载的项数（每方向3张），保证切换时不闪占位
        private const int LoadBuffer = 3;
        // 跳转取消阈值：差集大于 2×loadRadius 视为跳转，取消旧任务
        private const int JumpCancelMultiplier = 2;

        // 数据存储
        private List<string> _allPaths = new List<string>();
        private ObservableCollection<ThumbnailItem>? _items;
        // 缩略图缓存带 LRU 上限，避免来回切换时内存无限增长
        private const int MaxThumbnailCacheSize = 200;
        private readonly Dictionary<int, ImageSource?> _thumbnailCache = new Dictionary<int, ImageSource?>();
        private readonly LinkedList<int> _thumbnailCacheLru = new LinkedList<int>();
        private readonly Dictionary<int, LinkedListNode<int>> _thumbnailCacheLruNodes = new Dictionary<int, LinkedListNode<int>>();
        private int _currentRangeStart = -1;
        private int _currentRangeEnd = -1;
        private int _currentIndex = -1;
        // 当前可见半径（供 LoadOneThumbnailAsync 完成时范围检查用）
        private int _currentVisibleRadius = FallbackVisibleRadius;
        private readonly object _thumbnailCacheLock = new object();
        // 跳转取消：仅在大差集跳转时取消旧任务，相邻切换不取消
        private CancellationTokenSource? _loadCts;

        // 平滑滚动动画：用 CompositionTarget.Rendering 逐帧驱动 ScrollViewer 的 HorizontalOffset
        // ScrollViewer.HorizontalOffset 不是 DP，不能直接用 DoubleAnimation，需手动逐帧插值
        private double _scrollFrom;
        private double _scrollTo;
        private System.Diagnostics.Stopwatch? _scrollStopwatch;
        private const double ScrollAnimationDurationMs = 280; // 平滑滚动时长

        // 缩略图较少时(总宽≤视口宽)使用 TranslateTransform 平移居中，与 ScrollViewer 滚动互斥
        private TranslateTransform? _itemsTranslate;
        private double _translateFrom;
        private double _translateTo;
        private bool _isTranslateAnimation;

        // 缩略图解码并发限制：避免线程池被缩略图任务占满，保证主图加载优先
        // 主图加载（LoadBitmapSourceAsync）与缩略图加载都依赖线程池，
        // 100+ 张图快速切换时若不限制，缩略图任务会挤压主图加载线程，
        // 导致切换卡顿 + 缩略图加载延迟完成（进一步触发根因3的范围检查失效）。
        private static readonly SemaphoreSlim _thumbnailDecodeSemaphore = new(4, 4);

        public event Action<int>? ThumbnailClicked;

        private bool _isPotentialClick = false;

        public IEnumerable Images
        {
            get => (IEnumerable)GetValue(ImagesProperty);
            set => SetValue(ImagesProperty, value);
        }

        public int SelectedIndex
        {
            get => (int)GetValue(SelectedIndexProperty);
            set => SetValue(SelectedIndexProperty, value);
        }

        public ImageViewerThumbnailPanel()
        {
            InitializeComponent();
            // 获取 ItemsControl 的 TranslateTransform 引用（缩略图较少时平移居中用）
            _itemsTranslate = PART_ItemsControl.RenderTransform as TranslateTransform;
            Loaded += OnLoaded;
            // ScrollViewer 尺寸变化时重新居中：覆盖窗口首次布局完成、窗口缩放等场景
            PART_ScrollViewer.SizeChanged += (s, e) => CenterCurrentThumbnail();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Loaded 时布局已完成，直接居中（无需延迟）
            if (_allPaths.Count > 0 && _currentIndex >= 0)
                CenterCurrentThumbnail();
        }

        #region 占位图

        private static readonly ImageSource PlaceholderImage;

        static ImageViewerThumbnailPanel()
        {
            var drawing = new DrawingImage();
            var drawingGroup = new DrawingGroup();
            var rectGeometry = new RectangleGeometry(new Rect(0, 0, 50, 50));
            var rectDrawing = new GeometryDrawing(Brushes.LightGray, null, rectGeometry);
            drawingGroup.Children.Add(rectDrawing);
            var crossGeometry = new GeometryGroup();
            crossGeometry.Children.Add(new LineGeometry(new Point(10, 10), new Point(40, 40)));
            crossGeometry.Children.Add(new LineGeometry(new Point(40, 10), new Point(10, 40)));
            var crossDrawing = new GeometryDrawing(Brushes.Red, null, crossGeometry);
            drawingGroup.Children.Add(crossDrawing);
            drawing.Drawing = drawingGroup;
            drawing.Freeze();
            PlaceholderImage = drawing;
        }

        #endregion

        #region 属性变更

        private static void OnImagesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageViewerThumbnailPanel panel && e.NewValue is IEnumerable<string> paths)
            {
                panel.InitializeImages(paths.ToList());
            }
        }

        private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageViewerThumbnailPanel panel)
            {
                int newIndex = (int)e.NewValue;
                panel.SetCurrentIndex(newIndex);
            }
        }

        private void InitializeImages(List<string> paths)
        {
            _allPaths = paths;
            _thumbnailCache.Clear();
            _thumbnailCacheLru.Clear();
            _thumbnailCacheLruNodes.Clear();
            _currentIndex = -1;
            _currentRangeStart = -1;
            _currentRangeEnd = -1;
            _loadCts?.Cancel();
            _loadCts = null;

            // 创建全量占位列表，一次性绑定 ItemsSource（不再每次切换重建，消除闪烁）
            var items = new ObservableCollection<ThumbnailItem>();
            for (int i = 0; i < _allPaths.Count; i++)
            {
                items.Add(new ThumbnailItem { Index = i, Image = PlaceholderImage, IsSelected = false });
            }
            _items = items;
            PART_ItemsControl.ItemsSource = items;

            if (SelectedIndex >= 0 && SelectedIndex < _allPaths.Count)
            {
                SetCurrentIndex(SelectedIndex);
            }
        }

        private async void SetCurrentIndex(int newIndex)
        {
            if (newIndex < 0 || newIndex >= _allPaths.Count) return;

            // 索引相同但当前窗口范围已不包含该索引时，仍需重新加载
            bool idxDiff = _currentIndex != newIndex;
            bool rangeUninit = _currentRangeStart < 0;
            bool belowRange = newIndex < _currentRangeStart;
            bool aboveRange = newIndex > _currentRangeEnd;
            bool needsReload = idxDiff || rangeUninit || belowRange || aboveRange;

            if (!needsReload) return;

            int oldIndex = _currentIndex;
            _currentIndex = newIndex;

            // 更新选中高亮状态
            if (_items != null)
            {
                if (oldIndex >= 0 && oldIndex < _items.Count)
                    _items[oldIndex].IsSelected = false;
                _items[newIndex].IsSelected = true;
            }

            // 双半径：visibleRadius=屏幕可见半径，loadRadius=visibleRadius+3缓冲
            int visibleRadius = GetVisibleRadius();
            int loadRadius = visibleRadius + LoadBuffer;
            _currentVisibleRadius = visibleRadius;

            int newStart = Math.Max(0, newIndex - loadRadius);
            int newEnd = Math.Min(_allPaths.Count - 1, newIndex + loadRadius);

            await RefreshWindowIncrementalAsync(newStart, newEnd);

            // 加载窗口更新后再记录当前范围（RefreshWindowIncrementalAsync 内部会用到旧值）
            _currentRangeStart = newStart;
            _currentRangeEnd = newEnd;

            // 补刷：将当前窗口内已缓存的缩略图应用到列表
            // 修复：RefreshWindowIncrementalAsync 中重叠区被跳过，但历史异步任务可能已完成并写入缓存，
            //       此处补刷确保已缓存的缩略图不再保持占位图状态
            ApplyCachedToCurrentWindow();

            CenterCurrentThumbnail();
        }

        /// <summary>
        /// 强制切换到指定索引并加载其缩略图窗口。
        /// 供外部（如全屏窗口 OnMainImageChanged）直接调用，绕过依赖属性去重，
        /// 确保切换图片时缩略图面板一定能加载新窗口。
        /// </summary>
        public void NavigateTo(int index)
        {
            if (index < 0 || index >= _allPaths.Count) return;
            if (SelectedIndex != index)
                SelectedIndex = index;
            SetCurrentIndex(index);
        }

        /// <summary>屏幕可见半径：容器宽 / 项宽 / 2。容器未就绪时返回兜底值。</summary>
        private int GetVisibleRadius()
        {
            double containerWidth = GetContainerWidth();
            if (containerWidth <= 0) return FallbackVisibleRadius;
            return (int)(containerWidth / ItemWidth / 2.0);
        }

        /// <summary>加载半径：visibleRadius + 缓冲，保证切换时不闪占位。</summary>
        private int GetLoadRadius() => GetVisibleRadius() + LoadBuffer;

        #endregion

        #region 动态加载缩略图（增量更新版）

        /// <summary>
        /// 增量更新缩略图窗口。基于旧范围(_currentRangeStart/End)与新范围的差集：
        ///   toAdd = 新范围中不在旧范围的索引 → 缓存命中赋值，未命中异步加载
        /// 相邻切换差集仅1-2项，O(1) 任务量，彻底消除任务堆积。
        /// 跳转差集大时取消旧 CTS 再加载新的。
        /// 注意：不做 toRemove（不把移出窗口的项设回占位图），保留已加载的缩略图。
        ///       原因：WPF 的 Image 控件在 ClipToBounds 裁剪区外设置 Source 后，
        ///       移回视口时可能不重新渲染；反复在占位/缩略图之间切换会加剧此问题。
        ///       保留缩略图可确保项移回视口时 Image.Source 已是正确值。
        /// </summary>
        private async Task RefreshWindowIncrementalAsync(int newStart, int newEnd)
        {
            if (_items == null) return;

            int oldStart = _currentRangeStart;
            int oldEnd = _currentRangeEnd;
            bool isFirstLoad = (oldStart < 0 || oldEnd < 0);

            // 计算差集大小，判定是否为跳转
            int oldCount = isFirstLoad ? 0 : (oldEnd - oldStart + 1);
            int newCount = newEnd - newStart + 1;
            // 简化估算：若新旧窗口完全不重叠或重叠少，视为跳转
            bool isJump = !isFirstLoad
                && (newEnd < oldStart || newStart > oldEnd
                    || Math.Abs(newStart - oldStart) > newCount);

            CancellationToken token = CancellationToken.None;
            if (isJump)
            {
                _loadCts?.Cancel();
                _loadCts = new CancellationTokenSource();
                token = _loadCts.Token;
            }

            // 不做 toRemove：旧窗口中不在新窗口的项保留其缩略图，不设回占位图
            // （WPF 渲染层问题：裁剪区外设占位图后移回视口不渲染，保留缩略图可避免此问题）

            // toAdd：新范围中不在旧范围的项 → 缓存命中赋值，未命中异步加载
            for (int i = newStart; i <= newEnd; i++)
            {
                if (i < 0 || i >= _items.Count) continue;

                // 跳过重叠区（旧范围已加载过的，不动）
                if (!isFirstLoad && i >= oldStart && i <= oldEnd) continue;

                int idx = i;

                ImageSource? cached;
                bool has;
                lock (_thumbnailCacheLock)
                {
                    has = _thumbnailCache.TryGetValue(idx, out cached);
                    if (has) TouchThumbnailLruLocked(idx);
                }

                if (has)
                {
                    _items[idx].Image = cached;
                }
                else
                {
                    // 仅在项当前为 null 或占位图时设占位，避免覆盖已加载的缩略图
                    if (_items[idx].Image == null || ReferenceEquals(_items[idx].Image, PlaceholderImage))
                        _items[idx].Image = PlaceholderImage;
                    _ = LoadOneThumbnailAsync(idx, token);
                }
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 独立加载单个缩略图。
        /// 加载完成后：无条件写入 LRU 缓存并无条件赋值显示。
        /// 不做范围检查：因为不再 toRemove（移出窗口不设占位），
        /// 所以即使 idx 已不在当前窗口，赋值也是安全的——项移回视口时直接显示正确缩略图。
        /// token 仅用于跳转场景取消尚未开始的加载。
        /// </summary>
        private async Task LoadOneThumbnailAsync(int idx, CancellationToken token)
        {
            if (idx < 0 || idx >= _allPaths.Count) return;
            if (token.IsCancellationRequested) return;
            string path = _allPaths[idx];

            ImageSource? bmp;
            try
            {
                bmp = await LoadThumbnailDirectAsync(path);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                return;
            }

            if (bmp == null) return;

            // 无条件写入 LRU 缓存（供后续回切命中）
            lock (_thumbnailCacheLock)
                AddToThumbnailCacheLocked(idx, bmp);

            // 无条件赋值：不做范围检查，直接更新 UI
            // 原因：不再 toRemove，所以赋值不会与后续的占位重置冲突
            // 即使项当前在裁剪区外，赋值后 WPF 会保留 Image.Source，移回视口时直接显示
            await Dispatcher.InvokeAsync(() =>
            {
                if (_items != null && idx >= 0 && idx < _items.Count)
                    _items[idx].Image = bmp;
            }, DispatcherPriority.Background);
        }

        /// <summary>
        /// 将当前加载窗口内已缓存的缩略图应用到列表（补刷，用于延迟完成的任务写入缓存后兜底）。
        /// 使用 _currentRangeStart/End 作为窗口边界，与 RefreshWindowIncrementalAsync 一致。
        /// </summary>
        private void ApplyCachedToCurrentWindow()
        {
            if (_items == null || _currentIndex < 0) return;
            if (_currentRangeStart < 0 || _currentRangeEnd < 0) return;

            int s = _currentRangeStart;
            int e = _currentRangeEnd;
            for (int i = s; i <= e; i++)
            {
                if (i < 0 || i >= _items.Count) continue;
                lock (_thumbnailCacheLock)
                {
                    if (_thumbnailCache.TryGetValue(i, out var cached))
                    {
                        // 仅在当前为占位图或 null 时赋值，避免覆盖刚由异步任务赋值的缩略图
                        if (_items[i].Image == null || ReferenceEquals(_items[i].Image, PlaceholderImage))
                        {
                            _items[i].Image = cached;
                        }
                    }
                }
            }
        }

        /// <summary>将指定索引移到 LRU 链表尾部（最近使用）。调用方须持有 _thumbnailCacheLock。</summary>
        private void TouchThumbnailLruLocked(int idx)
        {
            if (_thumbnailCacheLruNodes.TryGetValue(idx, out var node))
            {
                _thumbnailCacheLru.Remove(node);
                _thumbnailCacheLru.AddLast(node);
            }
        }

        /// <summary>写入缩略图缓存并维护 LRU，超限时淘汰最旧项。调用方须持有 _thumbnailCacheLock。</summary>
        private void AddToThumbnailCacheLocked(int idx, ImageSource? bmp)
        {
            if (_thumbnailCacheLruNodes.TryGetValue(idx, out var existingNode))
            {
                _thumbnailCache[idx] = bmp;
                _thumbnailCacheLru.Remove(existingNode);
                _thumbnailCacheLru.AddLast(existingNode);
                return;
            }

            _thumbnailCache[idx] = bmp;
            var node = _thumbnailCacheLru.AddLast(idx);
            _thumbnailCacheLruNodes[idx] = node;

            while (_thumbnailCache.Count > MaxThumbnailCacheSize)
            {
                if (_thumbnailCacheLru.First == null) break;
                int oldest = _thumbnailCacheLru.First.Value;
                _thumbnailCacheLru.RemoveFirst();
                _thumbnailCacheLruNodes.Remove(oldest);
                _thumbnailCache.Remove(oldest);
            }
        }

        /// <summary>
        /// 缩略图解码：SkiaSharp 解码 + 缩放到 50px 高 + 像素直拷创建 BitmapSource。
        /// 消除原 SKBitmap→PNG编码→MemoryStream→BitmapImage解码 的绕路，
        /// 保留 SkiaSharp 的格式兼容性（webp/avif 等），用 Pbgra32 匹配预乘 alpha。
        /// </summary>
        private static async Task<ImageSource?> LoadThumbnailDirectAsync(string path)
        {
            if (!File.Exists(path)) return null;
            return await Task.Run(async () =>
            {
                // 限制并发：等待信号量后再解码，避免线程池被缩略图任务占满
                await _thumbnailDecodeSemaphore.WaitAsync();
                try
                {
                    using var sk = SKBitmap.Decode(path);
                    if (sk == null) return null;

                    int newH = 50;
                    int newW = Math.Max(1, (int)(sk.Width * (double)newH / sk.Height));

                    // 缩放到目标尺寸，强制 Bgra8888 预乘（与 WPF Pbgra32 匹配）
                    using var thumb = new SKBitmap(newW, newH, SKColorType.Bgra8888, SKAlphaType.Premul);
                    using (var canvas = new SKCanvas(thumb))
                    {
                        canvas.Clear(SKColors.Transparent);
                        var dst = new SKRect(0, 0, newW, newH);
                        var src = new SKRect(0, 0, sk.Width, sk.Height);
                        // 缩略图尺寸小（50px高），使用默认采样即可，无需 paint
                        canvas.DrawBitmap(sk, src, dst);
                        canvas.Flush();
                    }

                    // 直接从像素数组创建 BitmapSource，跳过 PNG 编/解码
                    int stride = newW * 4;
                    byte[] pixels = new byte[stride * newH];
                    Marshal.Copy(thumb.GetPixels(), pixels, 0, pixels.Length);

                    var bs = BitmapSource.Create(newW, newH, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
                    bs.Freeze();
                    return bs;
                }
                catch
                {
                    Serilog.Log.Debug("[Thumb] 缩略图解码失败（返回 null）");
                    return null;
                }
                finally
                {
                    _thumbnailDecodeSemaphore.Release();
                }
            });
        }

        /// <summary>
        /// 从缩略图列表中移除指定索引的项。
        /// 供父窗口在删除图片后调用，保持缩略图面板与图片列表同步。
        /// </summary>
        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _allPaths.Count) return;
            if (_items == null) return;

            // 取消正在进行的加载任务（索引变化后旧任务结果可能错位）
            _loadCts?.Cancel();
            _loadCts = null;

            // 从数据源和显示集合中移除
            _allPaths.RemoveAt(index);
            _items.RemoveAt(index);

            // 清空缩略图缓存：删除后所有后续索引整体偏移，
            // 缓存按旧索引键已全量失效，清除后按需重新加载
            lock (_thumbnailCacheLock)
            {
                _thumbnailCache.Clear();
                _thumbnailCacheLru.Clear();
                _thumbnailCacheLruNodes.Clear();
            }

            // 调整当前索引（若删除项在当前项之前或就是当前项）
            if (_currentIndex > index)
                _currentIndex--;
            else if (_currentIndex == index && _currentIndex >= _allPaths.Count)
                _currentIndex = _allPaths.Count - 1;

            // 重置加载窗口范围，下次 SetCurrentIndex 将全量重载
            _currentRangeStart = -1;
            _currentRangeEnd = -1;

            // 刷新选中高亮状态
            if (_items.Count > 0)
            {
                for (int i = 0; i < _items.Count; i++)
                    _items[i].IsSelected = (i == _currentIndex);
            }

            // 重新加载当前窗口缩略图并居中
            if (_currentIndex >= 0 && _currentIndex < _allPaths.Count)
            {
                SetCurrentIndex(_currentIndex);
            }
        }

        #endregion

        #region 自动居中（固定项宽估算，无需 UpdateLayout 遍历）

        /// <summary>
        /// 居中当前缩略图。统一使用 TranslateTransform 平移 ItemsControl，
        /// 使选中缩略图恒位于视口水平中央。ScrollViewer 仅作裁剪容器。
        /// 规则：
        ///   - 第1张时面板右移，左边露出背景（看图软件标准观感）
        ///   - 最后一张时面板左移，右边露出背景
        ///   - 任意分辨率/DPI 均自适应（containerWidth 取运行时实际值，ItemWidth 为 DIP）
        /// </summary>
        private void CenterCurrentThumbnail()
        {
            if (_currentIndex < 0 || _items == null)
                return;

            double containerWidth = GetContainerWidth();
            if (containerWidth <= 0)
            {
                // 容器尚未布局完成，延迟一帧重试一次
                Dispatcher.BeginInvoke(new Action(CenterCurrentThumbnail), DispatcherPriority.Render);
                return;
            }

            // ScrollViewer 滚动归零，只用作裁剪
            PART_ScrollViewer.ScrollToHorizontalOffset(0);

            // 当前项中心点在面板中的 X 坐标
            double itemCenterX = _currentIndex * ItemWidth + ItemWidth / 2.0;
            // 目标平移量：让 itemCenterX 对齐视口中央（无钳制，允许任意正负偏移）
            double targetTranslate = containerWidth / 2.0 - itemCenterX;
            AnimateTranslateTo(targetTranslate);
        }

        /// <summary>
        /// 平滑滚动 ScrollViewer 到目标水平偏移。
        /// 用 DispatcherTimer 逐帧插值，QuadraticEase 缓动，280ms 完成。
        /// 快速连续切换时中断上一次动画，从当前位置开始新动画（避免卡顿）。
        /// 同时重置 TranslateTransform 和 _isTranslateAnimation，确保模式切换干净。
        /// </summary>
        private void AnimateScrollTo(double targetOffset)
        {
            // 切换为滚动模式，清空平移状态
            _isTranslateAnimation = false;
            if (_itemsTranslate != null)
                _itemsTranslate.X = 0;

            double currentOffset = PART_ScrollViewer.HorizontalOffset;
            if (Math.Abs(currentOffset - targetOffset) < 0.5)
            {
                PART_ScrollViewer.ScrollToHorizontalOffset(targetOffset);
                return;
            }

            _scrollFrom = currentOffset;
            _scrollTo = targetOffset;
            _scrollStopwatch = System.Diagnostics.Stopwatch.StartNew();

            // 用 Rendering 事件逐帧驱动，确保与显示器刷新同步
            CompositionTarget.Rendering -= OnScrollRendering;
            CompositionTarget.Rendering += OnScrollRendering;
        }

        /// <summary>
        /// 平滑平移 TranslateTransform 到目标 X 偏移。
        /// 复用同一套逐帧驱动机制（CompositionTarget.Rendering + QuadraticEaseOut），
        /// 与 AnimateScrollTo 共用 _scrollStopwatch 和 OnScrollRendering 回调。
        /// </summary>
        private void AnimateTranslateTo(double targetTranslate)
        {
            if (_itemsTranslate == null) return;

            // 切换为平移模式，重置滚动状态
            _isTranslateAnimation = true;

            double currentX = _itemsTranslate.X;
            if (Math.Abs(currentX - targetTranslate) < 0.5)
            {
                _itemsTranslate.X = targetTranslate;
                return;
            }

            _translateFrom = currentX;
            _translateTo = targetTranslate;
            _scrollStopwatch = System.Diagnostics.Stopwatch.StartNew();

            CompositionTarget.Rendering -= OnScrollRendering;
            CompositionTarget.Rendering += OnScrollRendering;
        }

        /// <summary>
        /// CompositionTarget.Rendering 回调：每帧根据缓动函数计算当前滚动/平移位置。
        /// 通过 _isTranslateAnimation 标志位切换两种模式。
        /// </summary>
        private void OnScrollRendering(object? sender, EventArgs e)
        {
            if (_scrollStopwatch == null)
            {
                CompositionTarget.Rendering -= OnScrollRendering;
                return;
            }

            double elapsedMs = _scrollStopwatch.Elapsed.TotalMilliseconds;
            double progress = Math.Clamp(elapsedMs / ScrollAnimationDurationMs, 0, 1);
            // QuadraticEaseOut: t' = 1 - (1-t)^2，前快后慢，符合自然减速感
            double eased = 1 - (1 - progress) * (1 - progress);

            if (_isTranslateAnimation)
            {
                // 模式B：TranslateTransform 平移
                double current = _translateFrom + (_translateTo - _translateFrom) * eased;
                if (_itemsTranslate != null)
                    _itemsTranslate.X = current;

                if (progress >= 1.0)
                {
                    _scrollStopwatch.Stop();
                    _scrollStopwatch = null;
                    _isTranslateAnimation = false;
                    CompositionTarget.Rendering -= OnScrollRendering;
                    // 最终精确归位
                    if (_itemsTranslate != null)
                        _itemsTranslate.X = _translateTo;
                }
            }
            else
            {
                // 模式A：ScrollViewer 滚动
                double current = _scrollFrom + (_scrollTo - _scrollFrom) * eased;
                PART_ScrollViewer.ScrollToHorizontalOffset(current);

                if (progress >= 1.0)
                {
                    _scrollStopwatch.Stop();
                    _scrollStopwatch = null;
                    CompositionTarget.Rendering -= OnScrollRendering;
                    // 最终精确归位
                    PART_ScrollViewer.ScrollToHorizontalOffset(_scrollTo);
                }
            }
        }

        /// <summary>获取缩略图条容器的有效宽度（优先 ScrollViewer，回退 ThumbnailPanelHost 父级，再回退窗口）。</summary>
        private double GetContainerWidth()
        {
            double w = PART_ScrollViewer.ActualWidth;
            if (w <= 0)
            {
                var parent = VisualTreeHelper.GetParent(this) as FrameworkElement;
                while (parent != null)
                {
                    if (parent.Name == "ThumbnailPanelHost")
                    {
                        w = parent.ActualWidth;
                        break;
                    }
                    parent = VisualTreeHelper.GetParent(parent) as FrameworkElement;
                }
            }
            if (w <= 0)
            {
                var parentWindow = Window.GetWindow(this);
                if (parentWindow != null && parentWindow.ActualWidth > 0)
                    w = parentWindow.ActualWidth;
            }
            return w;
        }

        #endregion

        #region 缩略图点击

        private void ThumbnailItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border != null)
            {
                border.CaptureMouse();
                _isPotentialClick = true;
                e.Handled = true;
            }
        }

        private void ThumbnailItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isPotentialClick) return;
            _isPotentialClick = false;

            var border = sender as Border;
            if (border != null)
            {
                border.ReleaseMouseCapture();
                if (border.Tag is int index)
                {
                    ThumbnailClicked?.Invoke(index);
                    e.Handled = true;
                }
            }
        }

        #endregion

        #region 辅助类

        private class ThumbnailItem : INotifyPropertyChanged
        {
            public int Index { get; set; }

            private ImageSource? _image;
            public ImageSource? Image
            {
                get => _image;
                set
                {
                    _image = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
                }
            }

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        #endregion
    }
}
