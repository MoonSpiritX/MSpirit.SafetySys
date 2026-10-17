// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MS_SafetyManagementSystem.Tools
{
    /// <summary>
    /// 裁剪交互控件：Uniform 预览 + 自绘裁剪框（遮罩/边框/8 手柄）。
    /// 鼠标事件 → CropRegion 状态机（显示坐标域）→ 渲染；像素选区经 CropGeometry 换算后通过
    /// PixelSelectionChanged 事件对外暴露（状态栏显示 / 裁剪执行用）。
    /// 本控件整体为"交互元素"，已加入 WindowDragHelper 白名单（预览区内不触发窗口拖拽）。
    /// </summary>
    public partial class CropOverlay : UserControl
    {
        private readonly CropRegion _region = new();
        private double _scale, _offsetX, _offsetY;
        private int _pixelWidth, _pixelHeight;
        private bool _dragging;
        private CropHandle _activeHandle = CropHandle.None;
        private bool _moving;

        /// <summary>像素选区变化事件（拖拽中实时触发）：(x, y, width, height) 摆正后像素坐标</summary>
        public event Action<int, int, int, int>? PixelSelectionChanged;

        /// <summary>裁剪比例（null=自由；变化时重置选区）</summary>
        public static readonly DependencyProperty AspectRatioProperty =
            DependencyProperty.Register("AspectRatio", typeof(double?), typeof(CropOverlay),
                new PropertyMetadata(null, (d, e) => ((CropOverlay)d).OnAspectRatioChanged((double?)e.NewValue)));

        public double? AspectRatio
        {
            get => (double?)GetValue(AspectRatioProperty);
            set => SetValue(AspectRatioProperty, value);
        }

        private void OnAspectRatioChanged(double? ratio)
        {
            SetAspectRatio(ratio);
        }

        public CropOverlay()
        {
            InitializeComponent();
            RootGrid.SizeChanged += (s, e) => Relayout();
            OverlayCanvas.PreviewMouseLeftButtonDown += OnCanvasMouseDown;
            OverlayCanvas.PreviewMouseMove += OnCanvasMouseMove;
            OverlayCanvas.PreviewMouseLeftButtonUp += OnCanvasMouseUp;
            OverlayCanvas.MouseLeave += (s, e) =>
            {
                EndDrag();
                OverlayCanvas.Cursor = Cursors.Arrow; // 移出时恢复默认指针
            };
        }

        /// <summary>设置预览图与像素尺寸（预览切换时调用，自动重置裁剪框为默认选区）</summary>
        public void SetPreview(BitmapSource? image, int pixelWidth, int pixelHeight)
        {
            PreviewImageControl.Source = image;
            _pixelWidth = pixelWidth;
            _pixelHeight = pixelHeight;
            if (image == null || pixelWidth <= 0 || pixelHeight <= 0)
            {
                ShowSelection(false);
                return;
            }
            ShowSelection(true);
            Relayout();
        }

        /// <summary>设置裁剪比例（null = 自由），自动重置选区</summary>
        public void SetAspectRatio(double? ratio)
        {
            _region.SetAspectRatio(ratio, GetImageDisplayWidth(), GetImageDisplayHeight());
            Render();
            RaisePixelSelection();
        }

        /// <summary>当前像素选区（摆正后像素坐标；无有效预览返回 null）</summary>
        public (int X, int Y, int Width, int Height)? GetPixelSelection()
        {
            if (_pixelWidth <= 0 || _pixelHeight <= 0 || _scale <= 0) return null;
            // 2026-08-16 修复：region 坐标是"相对图片显示区域"（不含 offset），必须走
            // RegionRectToPixelRect（内部加 offset）——原直接调 DisplayRectToPixelRect 传 region 坐标，
            // 非满幅（Stretch=Uniform 留白）时像素选区向左上偏移 offset/scale，裁剪位置与框选不一致。
            var rect = CropGeometry.RegionRectToPixelRect(
                _region.X, _region.Y, _region.Width, _region.Height,
                _scale, _offsetX, _offsetY, _pixelWidth, _pixelHeight);
            if (rect.Width <= 0 || rect.Height <= 0) return null;
            return (rect.X, rect.Y, rect.Width, rect.Height);
        }

        // ═══ 布局 ═══

        private double GetImageDisplayWidth() => _pixelWidth > 0 ? _pixelWidth * _scale : 0;
        private double GetImageDisplayHeight() => _pixelHeight > 0 ? _pixelHeight * _scale : 0;

        private void Relayout()
        {
            double availW = RootGrid.ActualWidth, availH = RootGrid.ActualHeight;
            if (availW <= 0 || availH <= 0 || _pixelWidth <= 0 || _pixelHeight <= 0) return;
            (_scale, _offsetX, _offsetY) = CropGeometry.ComputeUniformTransform(availW, availH, _pixelWidth, _pixelHeight);
            _region.Reset(GetImageDisplayWidth(), GetImageDisplayHeight());
            Render();
            RaisePixelSelection();
            RenderAnnotations(); // 标注按新 scale/offset 重绘（2026-08-16 阶段 5）
        }

        private void ShowSelection(bool visible)
        {
            var v = visible ? Visibility.Visible : Visibility.Collapsed;
            foreach (var el in new[] { MaskLeft, MaskRight, MaskTop, MaskBottom, SelectionRect,
                HandleTL, HandleT, HandleTR, HandleR, HandleBR, HandleB, HandleBL, HandleL })
                el.Visibility = v;
        }

        private void Render()
        {
            if (_pixelWidth <= 0 || _scale <= 0) return;
            double x = _offsetX + _region.X;
            double y = _offsetY + _region.Y;
            double w = _region.Width;
            double h = _region.Height;
            double imgW = GetImageDisplayWidth();
            double imgH = GetImageDisplayHeight();

            // 遮罩：四块覆盖"图片显示区域内、选区外"（外边缘固定对齐图片显示区域边界，内边缘贴选区）。
            // 2026-08-16 修复：原实现左/上从控件原点(0,0)开始（盖住留白区）、右/下只到 imgW/imgH
            // （不含 offset，右/下留白漏出）→ 灰块左右不对称、右侧出现错位"边框"，且随选区拖动时
            // 外边缘钉死在错误位置。现统一以图片显示区域 [offsetX..offsetX+imgW]×[offsetY..offsetY+imgH] 为界。
            SetMask(MaskLeft,   _offsetX,          _offsetY,          x - _offsetX,                 imgH); // 左：[图片左缘, 选区左缘]
            SetMask(MaskRight,  x + w,             _offsetY,          _offsetX + imgW - x - w,      imgH); // 右：[选区右缘, 图片右缘]
            SetMask(MaskTop,    x,                 _offsetY,          w,                            y - _offsetY); // 上：[图片上缘, 选区上缘]
            SetMask(MaskBottom, x,                 y + h,             w,                            _offsetY + imgH - y - h); // 下：[选区下缘, 图片下缘]

            // 边框 + 手柄
            Canvas.SetLeft(SelectionRect, x);
            Canvas.SetTop(SelectionRect, y);
            SelectionRect.Width = w;
            SelectionRect.Height = h;

            const double hw = 9;
            PositionHandle(HandleTL, x - hw / 2, y - hw / 2);
            PositionHandle(HandleT, x + w / 2 - hw / 2, y - hw / 2);
            PositionHandle(HandleTR, x + w - hw / 2, y - hw / 2);
            PositionHandle(HandleR, x + w - hw / 2, y + h / 2 - hw / 2);
            PositionHandle(HandleBR, x + w - hw / 2, y + h - hw / 2);
            PositionHandle(HandleB, x + w / 2 - hw / 2, y + h - hw / 2);
            PositionHandle(HandleBL, x - hw / 2, y + h - hw / 2);
            PositionHandle(HandleL, x - hw / 2, y + h / 2 - hw / 2);
        }

        private static void PositionHandle(Rectangle handle, double x, double y)
        {
            Canvas.SetLeft(handle, x);
            Canvas.SetTop(handle, y);
        }

        /// <summary>遮罩块定位（WPF Rectangle 用 Width/Height + Canvas 定位，无 Rect 属性）</summary>
        private static void SetMask(Rectangle mask, double x, double y, double w, double h)
        {
            Canvas.SetLeft(mask, x);
            Canvas.SetTop(mask, y);
            mask.Width = Math.Max(0, w);
            mask.Height = Math.Max(0, h);
        }

        // ═══ 鼠标交互 ═══

        private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_pixelWidth <= 0 || _scale <= 0) return;
            var pos = e.GetPosition(OverlayCanvas);
            double px = pos.X - _offsetX; // 相对图片显示区域
            double py = pos.Y - _offsetY;

            // ── 标注分派（2026-08-16 阶段 5）──
            // 注意：标注存像素坐标，须传**控件坐标** pos.X/pos.Y 给 DisplayToPxX（内部减 offset 一次）；
            // 不能传已减过 offset 的 px/py（那是裁剪 region 坐标域）——否则双重减 offset，标注整体偏左。
            if (Tool != null)
            {
                if (Tool.ActiveKind.HasValue)
                {
                    // 绘制模式：按下即创建 Draft（文字/矩形/...）
                    Tool.OnMouseDown(DisplayToPxX(pos.X), DisplayToPxY(pos.Y));
                    OverlayCanvas.CaptureMouse();
                    e.Handled = true;
                    RenderAnnotations();
                    return;
                }
                if (Tool.Items.Count > 0)
                {
                    // 选择/裁剪模式：先标注命中（点击标注 → 选中/移动），未命中再走裁剪
                    Tool.OnMouseDown(DisplayToPxX(pos.X), DisplayToPxY(pos.Y));
                    if (Tool.Selected != null)
                    {
                        _annotationDragActive = true;
                        _lastAnnotationPxX = DisplayToPxX(pos.X);
                        _lastAnnotationPxY = DisplayToPxY(pos.Y);
                        OverlayCanvas.CaptureMouse();
                        e.Handled = true;
                        RenderAnnotations();
                        return;
                    }
                    // 2026-08-16：点击空白处取消标注选中（Tool.OnMouseDown 已把 Selected 置 null，
                    // 这里刷新视觉层让选中框/✕ 消失；未命中继续走裁剪分支）
                    if (Tool.Selected == null && Tool.Draft == null)
                        RenderAnnotations();
                }
            }

            var hit = HitTestHandle(px, py);
            if (hit != CropHandle.None)
            {
                _activeHandle = hit;
                _dragging = true;
                _moving = false;
            }
            else if (IsInsideSelection(px, py) && HasMoveSpace())
            {
                // 2026-08-16 修复：仅在裁剪框**未充满**（有平移空间）时才进入"内部移动"分支。
                // 原逻辑全图时 IsInsideSelection 恒 true → 点击空白（取消标注选中后）会激活移动裁剪框
                // 状态并抢占焦点，导致后续无法再次选中标注。
                _dragging = true;
                _moving = true;
                // 按住剪裁框内部立即显示 4 向移动箭头（UpdateCursor 只在非拖拽时跑）
                OverlayCanvas.Cursor = Cursors.SizeAll;
            }
            else
            {
                return; // 点击图片区域外 / 裁剪框充满时的内部点击：不响应
            }
            _lastPointerX = e.GetPosition(this).X;
            _lastPointerY = e.GetPosition(this).Y;
            OverlayCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(OverlayCanvas);
            double px = pos.X - _offsetX;
            double py = pos.Y - _offsetY;

            // ── 标注分派（2026-08-16 阶段 5；传控件坐标 pos.X/pos.Y，勿用已减 offset 的 px/py）──
            if (Tool != null)
            {
                if (Tool.Draft != null)
                {
                    // 绘制中：更新 Draft 终点（像素坐标）
                    Tool.OnMouseMove(DisplayToPxX(pos.X), DisplayToPxY(pos.Y));
                    RenderAnnotations();
                    return;
                }
                if (_annotationDragActive && Tool.Selected != null)
                {
                    // 移动选中标注：按像素增量平移
                    float curPx = DisplayToPxX(pos.X), curPy = DisplayToPxY(pos.Y);
                    Tool.MoveSelectedBy(curPx - _lastAnnotationPxX, curPy - _lastAnnotationPxY);
                    _lastAnnotationPxX = curPx;
                    _lastAnnotationPxY = curPy;
                    RenderAnnotations();
                    return;
                }
            }

            // 非拖拽：实时更新指针形态（边框/角=双箭头、内部=移动、外部=默认）
            if (!_dragging)
            {
                UpdateCursor(px, py);
                return;
            }

            double bw = GetImageDisplayWidth();
            double bh = GetImageDisplayHeight();

            double curX = e.GetPosition(this).X;
            double curY = e.GetPosition(this).Y;
            if (_moving)
            {
                _region.Move(curX - _lastPointerX, curY - _lastPointerY, bw, bh);
            }
            else
            {
                _region.ResizeTo(_activeHandle, px, py, bw, bh);
            }
            _lastPointerX = curX;
            _lastPointerY = curY;
            Render();
            RaisePixelSelection();
        }

        /// <summary>裁剪框是否有平移空间（未充满图片显示区；充满时内部点击不响应，避免抢占标注焦点，2026-08-16）</summary>
        private bool HasMoveSpace() =>
            _region.Width < GetImageDisplayWidth() - 0.5 || _region.Height < GetImageDisplayHeight() - 0.5;

        private void UpdateCursor(double px, double py)
        {
            // 标注绘制模式：十字光标（2026-08-16 阶段 5）
            if (Tool?.ActiveKind.HasValue == true)
            {
                OverlayCanvas.Cursor = Cursors.Cross;
                return;
            }
            if (_pixelWidth <= 0 || _scale <= 0)
            {
                OverlayCanvas.Cursor = Cursors.Arrow;
                return;
            }
            var hit = HitTestHandle(px, py);
            if (hit != CropHandle.None)
                OverlayCanvas.Cursor = GetHandleCursor(hit);
            else if (IsInsideSelection(px, py) && HasMoveSpace())
                OverlayCanvas.Cursor = Cursors.SizeAll;
            else
                OverlayCanvas.Cursor = Cursors.Arrow;
        }

        /// <summary>手柄/边框对应的调整指针形态（角=对角双箭头、左右=横向、上下=纵向）</summary>
        private static Cursor GetHandleCursor(CropHandle handle) => handle switch
        {
            CropHandle.TopLeft or CropHandle.BottomRight => Cursors.SizeNWSE,
            CropHandle.TopRight or CropHandle.BottomLeft => Cursors.SizeNESW,
            CropHandle.Left or CropHandle.Right => Cursors.SizeWE,
            CropHandle.Top or CropHandle.Bottom => Cursors.SizeNS,
            _ => Cursors.Arrow
        };

        private double _lastPointerX, _lastPointerY;
        private bool _annotationDragActive;
        private float _lastAnnotationPxX, _lastAnnotationPxY;

        private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
        {
            // ── 标注分派（2026-08-16 阶段 5）──
            if (Tool != null && (Tool.Draft != null || _annotationDragActive))
            {
                if (Tool.Draft != null)
                {
                    var pending = Tool.OnMouseUp();
                    if (pending != null && pending.Kind == AnnotationKind.Text)
                    {
                        BeginTextInput(pending); // 文字：就地输入框（微信同款），提交/取消时复位工具（见 Commit/Cancel）
                    }
                    else
                    {
                        // 2026-08-16 修复：画完自动回选择模式（微信行为）——否则 ActiveKind 仍为绘制工具，
                        // 点击已画图形会再次走"绘制"分支，永远无法选中/移动标注。
                        Tool.ActiveKind = null;
                        RenderAnnotations();
                        AnnotationsChanged?.Invoke(); // 窗口据此刷新工具栏按钮高亮
                    }
                }
                else
                {
                    Tool.OnMouseUp(); // 结束移动（IsMoving=false）
                }
                _annotationDragActive = false;
                OverlayCanvas.ReleaseMouseCapture();
                e.Handled = true;
                return;
            }
            EndDrag();
            e.Handled = true;
        }

        private void EndDrag()
        {
            if (_dragging)
            {
                _dragging = false;
                _moving = false;
                _activeHandle = CropHandle.None;
                OverlayCanvas.ReleaseMouseCapture();
            }
        }

        private CropHandle HitTestHandle(double px, double py)
        {
            const double handleHalf = 6; // 手柄命中半径
            const double edgeTol = 5;    // 整条边框线命中容差
            double x = _region.X, y = _region.Y, w = _region.Width, h = _region.Height;
            double right = x + w, bottom = y + h;

            // 1) 8 个手柄点（4 角 + 4 边中点）
            if (Math.Abs(px - x) <= handleHalf && Math.Abs(py - y) <= handleHalf) return CropHandle.TopLeft;
            if (Math.Abs(px - right) <= handleHalf && Math.Abs(py - y) <= handleHalf) return CropHandle.TopRight;
            if (Math.Abs(px - right) <= handleHalf && Math.Abs(py - bottom) <= handleHalf) return CropHandle.BottomRight;
            if (Math.Abs(px - x) <= handleHalf && Math.Abs(py - bottom) <= handleHalf) return CropHandle.BottomLeft;
            if (Math.Abs(px - (x + w / 2)) <= handleHalf && Math.Abs(py - y) <= handleHalf) return CropHandle.Top;
            if (Math.Abs(px - (x + w / 2)) <= handleHalf && Math.Abs(py - bottom) <= handleHalf) return CropHandle.Bottom;
            if (Math.Abs(px - right) <= handleHalf && Math.Abs(py - (y + h / 2)) <= handleHalf) return CropHandle.Right;
            if (Math.Abs(px - x) <= handleHalf && Math.Abs(py - (y + h / 2)) <= handleHalf) return CropHandle.Left;

            // 2) 边框线角部（手柄半径外、线容差内的角区）→ 对角调整
            if (Math.Abs(px - x) <= edgeTol && Math.Abs(py - y) <= edgeTol) return CropHandle.TopLeft;
            if (Math.Abs(px - right) <= edgeTol && Math.Abs(py - y) <= edgeTol) return CropHandle.TopRight;
            if (Math.Abs(px - right) <= edgeTol && Math.Abs(py - bottom) <= edgeTol) return CropHandle.BottomRight;
            if (Math.Abs(px - x) <= edgeTol && Math.Abs(py - bottom) <= edgeTol) return CropHandle.BottomLeft;

            // 3) 整条边框线（任意位置拖动对应边）
            if (Math.Abs(px - x) <= edgeTol && py >= y - edgeTol && py <= bottom + edgeTol) return CropHandle.Left;
            if (Math.Abs(px - right) <= edgeTol && py >= y - edgeTol && py <= bottom + edgeTol) return CropHandle.Right;
            if (Math.Abs(py - y) <= edgeTol && px >= x - edgeTol && px <= right + edgeTol) return CropHandle.Top;
            if (Math.Abs(py - bottom) <= edgeTol && px >= x - edgeTol && px <= right + edgeTol) return CropHandle.Bottom;

            return CropHandle.None;
        }

        private bool IsInsideSelection(double px, double py)
        {
            return px >= _region.X && px <= _region.X + _region.Width
                && py >= _region.Y && py <= _region.Y + _region.Height;
        }

        private void RaisePixelSelection()
        {
            var sel = GetPixelSelection();
            if (sel.HasValue)
                PixelSelectionChanged?.Invoke(sel.Value.X, sel.Value.Y, sel.Value.Width, sel.Value.Height);
        }
    }
}
