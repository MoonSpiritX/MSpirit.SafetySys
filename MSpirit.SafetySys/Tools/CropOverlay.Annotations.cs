// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

// CropOverlay 的标注部分（2026-08-16 阶段 5，partial 拆分）：
// 标注视觉层渲染（AnnotationCanvas，显示坐标 = 像素 × scale + offset）、
// 选中高亮（1px 边框 + ✕ 删除）、文字就地输入框、删除/清空接线。
// 鼠标事件分派（绘制模式 vs 选择模式 vs 裁剪）在 CropOverlay.xaml.cs 主文件。

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MS_SafetyManagementSystem.Tools
{
    public partial class CropOverlay
    {
        private AnnotationTool? _annotationTool;
        private Annotation? _pendingTextAnnotation;

        /// <summary>标注状态机（由 ViewModel 注入；Items 即标注列表，保存时读取）</summary>
        public AnnotationTool? AnnotationTool
        {
            get => _annotationTool;
            set
            {
                _annotationTool = value;
                RenderAnnotations();
            }
        }

        private AnnotationTool? Tool => _annotationTool;

        // ═══ 标注渲染 ═══

        /// <summary>重建标注视觉层（所有标注 + 绘制中 Draft + 选中框）</summary>
        private void RenderAnnotations()
        {
            AnnotationCanvas.Children.Clear();
            if (Tool == null || _pixelWidth <= 0 || _scale <= 0)
            {
                HideAnnotationChrome();
                return;
            }

            foreach (var a in Tool.Items)
                AnnotationCanvas.Children.Add(CreateAnnotationShape(a));

            if (Tool.Draft != null)
                AnnotationCanvas.Children.Add(CreateAnnotationShape(Tool.Draft));

            UpdateSelectionChrome();
        }

        /// <summary>把标注对象转成 WPF Shape（显示坐标域）</summary>
        private UIElement CreateAnnotationShape(Annotation a)
        {
            var color = new SolidColorBrush(Color.FromArgb(
                (byte)(a.ColorArgb >> 24), (byte)(a.ColorArgb >> 16), (byte)(a.ColorArgb >> 8), (byte)a.ColorArgb));

            return a.Kind switch
            {
                AnnotationKind.Rectangle => MakeRectShape(a, color),
                AnnotationKind.Oval => MakeOvalShape(a, color),
                AnnotationKind.Arrow => MakeArrowShape(a, color), // 2026-08-16：箭头预览需画箭头头（原与直线同形）
                AnnotationKind.Line => MakeLineShape(a, color),
                AnnotationKind.Text => MakeTextShape(a, color),
                AnnotationKind.Mosaic => MakeMosaicVisual(a),
                _ => MakeRectShape(a, color)
            };
        }

        private Rectangle MakeRectShape(Annotation a, Brush color)
        {
            var r = a.NormalizedRect;
            var rect = new Rectangle
            {
                Stroke = color,
                StrokeThickness = Math.Max(1, a.StrokeWidth),
                Fill = null
            };
            Canvas.SetLeft(rect, PxToDisplayX(r.Left));
            Canvas.SetTop(rect, PxToDisplayY(r.Top));
            rect.Width = PxToDisplay(r.Width);
            rect.Height = PxToDisplay(r.Height);
            return rect;
        }

        private Ellipse MakeOvalShape(Annotation a, Brush color)
        {
            var r = a.NormalizedRect;
            var ellipse = new Ellipse
            {
                Stroke = color,
                StrokeThickness = Math.Max(1, a.StrokeWidth),
                Fill = null
            };
            Canvas.SetLeft(ellipse, PxToDisplayX(r.Left));
            Canvas.SetTop(ellipse, PxToDisplayY(r.Top));
            ellipse.Width = PxToDisplay(r.Width);
            ellipse.Height = PxToDisplay(r.Height);
            return ellipse;
        }

        private Line MakeLineShape(Annotation a, Brush color)
        {
            return new Line
            {
                X1 = PxToDisplayX(a.X1), Y1 = PxToDisplayY(a.Y1),
                X2 = PxToDisplayX(a.X2), Y2 = PxToDisplayY(a.Y2),
                Stroke = color,
                StrokeThickness = Math.Max(1, a.StrokeWidth),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
        }

        /// <summary>箭头预览：主线段 + 终点箭头头（起点=箭尾、终点=箭头尖，与 AnnotationRenderer.DrawArrow 同规则）</summary>
        private Path MakeArrowShape(Annotation a, Brush color)
        {
            double x1 = PxToDisplayX(a.X1), y1 = PxToDisplayY(a.Y1);
            double x2 = PxToDisplayX(a.X2), y2 = PxToDisplayY(a.Y2);

            double angle = Math.Atan2(y2 - y1, x2 - x1);
            double headLen = Math.Max(10, a.StrokeWidth * 4 * _scale);
            const double spread = Math.PI / 6; // 30°
            double hx1 = x2 - headLen * Math.Cos(angle - spread);
            double hy1 = y2 - headLen * Math.Sin(angle - spread);
            double hx2 = x2 - headLen * Math.Cos(angle + spread);
            double hy2 = y2 - headLen * Math.Sin(angle + spread);

            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(x1, y1), false, false);
                g.LineTo(new Point(x2, y2), true, false);
                g.BeginFigure(new Point(x2, y2), false, false);
                g.LineTo(new Point(hx1, hy1), true, false);
                g.BeginFigure(new Point(x2, y2), false, false);
                g.LineTo(new Point(hx2, hy2), true, false);
            }
            geometry.Freeze();

            return new Path
            {
                Data = geometry,
                Stroke = color,
                StrokeThickness = Math.Max(1, a.StrokeWidth),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
        }

        private TextBlock MakeTextShape(Annotation a, Brush color)
        {
            // 2026-08-16 修复：原 MaxWidth=字号×长度×0.55 比中文字宽（≈字号）还窄，配合 Wrap
            // 导致每字换行 → 竖排。Canvas 中 TextBlock 自动按内容伸展，去掉 MaxWidth/Wrap 即正常横排。
            var tb = new TextBlock
            {
                Text = a.Text,
                FontSize = Math.Max(8, a.FontSize * _scale),
                Foreground = color,
                TextWrapping = TextWrapping.NoWrap
            };
            Canvas.SetLeft(tb, PxToDisplayX(a.X1));
            Canvas.SetTop(tb, PxToDisplayY(a.Y1));
            return tb;
        }

        private UIElement MakeMosaicVisual(Annotation a)
        {
            // 2026-08-17：马赛克预览不再用半透明灰块占位——直接从预览图抠出区域、线性降采样
            // （≈块内取均值）再以 NearestNeighbor 放大回显示尺寸，得到真实像素化效果；
            // 块数规则与输出端 AnnotationRenderer.DrawMosaic 一致（宽高各约 1/12），
            // 预览所见即输出所得。
            var r = a.NormalizedRect;
            double dispLeft = PxToDisplayX(r.Left);
            double dispTop = PxToDisplayY(r.Top);
            double dispW = PxToDisplay(r.Width);
            double dispH = PxToDisplay(r.Height);

            var host = new Canvas();
            var pixelated = TryMakePixelatedRegion(a);
            if (pixelated != null)
            {
                Canvas.SetLeft(pixelated, 0);
                Canvas.SetTop(pixelated, 0);
                pixelated.Width = dispW;
                pixelated.Height = dispH;
                host.Children.Add(pixelated);
            }
            else
            {
                // 兜底：无预览图/像素化失败时沿用半透明占位块，保证区域可见
                var fallback = new Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromArgb(0x55, 0x88, 0x88, 0x88))
                };
                Canvas.SetLeft(fallback, 0);
                Canvas.SetTop(fallback, 0);
                fallback.Width = dispW;
                fallback.Height = dispH;
                host.Children.Add(fallback);
            }

            // 区域边界：马赛克内容与原图对比度低时仍能明确看到区域范围
            var border = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromArgb(0xB0, 0x44, 0x44, 0x44)),
                StrokeThickness = 1.2
            };
            Canvas.SetLeft(border, 0);
            Canvas.SetTop(border, 0);
            border.Width = dispW;
            border.Height = dispH;
            host.Children.Add(border);

            Canvas.SetLeft(host, dispLeft);
            Canvas.SetTop(host, dispTop);
            return host;
        }

        /// <summary>
        /// 从预览图抠出马赛克区域并像素化（返回显示用 Image；预览图被降采样时换算到预览坐标）。
        /// 失败返回 null，由调用方兜底。
        /// </summary>
        private Image? TryMakePixelatedRegion(Annotation a)
        {
            if (PreviewImageControl.Source is not BitmapSource src
                || _pixelWidth <= 0 || _pixelHeight <= 0)
                return null;

            // 预览图像素尺寸 ≠ 全尺寸（_pixelWidth/Height），先换算区域到预览坐标
            double kx = src.PixelWidth / (double)_pixelWidth;
            double ky = src.PixelHeight / (double)_pixelHeight;

            var r = a.NormalizedRect;
            int l = (int)Math.Floor(r.Left * kx);
            int t = (int)Math.Floor(r.Top * ky);
            int rr = (int)Math.Ceiling(r.Right * kx);
            int bb = (int)Math.Ceiling(r.Bottom * ky);
            l = Math.Clamp(l, 0, src.PixelWidth);
            t = Math.Clamp(t, 0, src.PixelHeight);
            rr = Math.Clamp(rr, l + 1, src.PixelWidth);
            bb = Math.Clamp(bb, t + 1, src.PixelHeight);
            int w = rr - l, h = bb - t;
            if (w <= 1 || h <= 1) return null;

            try
            {
                var cropped = new CroppedBitmap(src, new Int32Rect(l, t, w, h));
                // 块数同输出端规则：宽高各约 1/12（最小 1）；线性降采样 ≈ 块内均值
                double bw = Math.Max(1, w / 12.0);
                double bh = Math.Max(1, h / 12.0);
                var small = new TransformedBitmap(cropped, new ScaleTransform(1.0 / bw, 1.0 / bh));
                var img = new Image
                {
                    Source = small,
                    Stretch = Stretch.Fill
                };
                // 显示端放大用 NearestNeighbor → 每个小块画成方整色块（像素化观感）
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                return img;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "马赛克预览像素化失败（回退占位块）");
                return null;
            }
        }

        // ═══ 选中高亮 + ✕ ═══

        private void UpdateSelectionChrome()
        {
            if (Tool?.Selected == null || _pixelWidth <= 0 || _scale <= 0)
            {
                HideAnnotationChrome();
                return;
            }

            var a = Tool.Selected;
            double left, top, w, h;
            if (a.Kind is AnnotationKind.Line or AnnotationKind.Arrow)
            {
                left = Math.Min(PxToDisplayX(a.X1), PxToDisplayX(a.X2)) - 3;
                top = Math.Min(PxToDisplayY(a.Y1), PxToDisplayY(a.Y2)) - 3;
                w = Math.Abs(PxToDisplayX(a.X2) - PxToDisplayX(a.X1)) + 6;
                h = Math.Abs(PxToDisplayY(a.Y2) - PxToDisplayY(a.Y1)) + 6;
            }
            else
            {
                var r = a.Kind == AnnotationKind.Text ? a.TextBounds : a.NormalizedRect;
                left = PxToDisplayX(r.Left) - 3;
                top = PxToDisplayY(r.Top) - 3;
                w = PxToDisplay(r.Width) + 6;
                h = PxToDisplay(r.Height) + 6;
            }

            SelectionHighlight.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionHighlight, left);
            Canvas.SetTop(SelectionHighlight, top);
            SelectionHighlight.Width = w;
            SelectionHighlight.Height = h;

            // ✕ 按钮在选中框右上角
            DeleteAnnotationButton.Visibility = Visibility.Visible;
            Canvas.SetLeft(DeleteAnnotationButton, left + w - 8);
            Canvas.SetTop(DeleteAnnotationButton, top - 8);
        }

        private void HideAnnotationChrome()
        {
            SelectionHighlight.Visibility = Visibility.Collapsed;
            DeleteAnnotationButton.Visibility = Visibility.Collapsed;
        }

        private void DeleteAnnotationButton_Click(object sender, RoutedEventArgs e)
        {
            if (Tool?.DeleteSelected() == true)
            {
                RenderAnnotations();
                AnnotationsChanged?.Invoke();
            }
        }

        /// <summary>标注列表变化事件（增删后通知 ViewModel 刷新按钮可用态）</summary>
        public event Action? AnnotationsChanged;

        // ═══ 文字就地输入（微信同款） ═══

        private void BeginTextInput(Annotation pending)
        {
            _pendingTextAnnotation = pending;
            AnnotationTextInput.Text = "";
            AnnotationTextInput.Visibility = Visibility.Visible;
            Canvas.SetLeft(AnnotationTextInput, PxToDisplayX(pending.X1));
            Canvas.SetTop(AnnotationTextInput, PxToDisplayY(pending.Y1) - 14);
            AnnotationTextInput.Focus();
        }

        private void CommitAnnotationText()
        {
            var pending = _pendingTextAnnotation;
            _pendingTextAnnotation = null;
            AnnotationTextInput.Visibility = Visibility.Collapsed;
            if (pending != null && Tool != null)
            {
                Tool.CommitText(pending, AnnotationTextInput.Text);
                // 2026-08-16：文字提交后回选择模式（与绘制后复位一致）
                Tool.ActiveKind = null;
                RenderAnnotations();
                AnnotationsChanged?.Invoke();
            }
        }

        private void AnnotationTextInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) CommitAnnotationText();
            else if (e.Key == Key.Escape)
            {
                if (_pendingTextAnnotation != null && Tool != null) Tool.CancelText(_pendingTextAnnotation);
                _pendingTextAnnotation = null;
                AnnotationTextInput.Visibility = Visibility.Collapsed;
                if (Tool != null) Tool.ActiveKind = null; // 取消也回选择模式
                RenderAnnotations();
                AnnotationsChanged?.Invoke();
            }
        }

        private void AnnotationTextInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_pendingTextAnnotation != null) CommitAnnotationText();
        }

        // ═══ 标注坐标换算（像素 ↔ 显示） ═══

        private double PxToDisplayX(float px) => px * _scale + _offsetX;
        private double PxToDisplayY(float py) => py * _scale + _offsetY;
        private double PxToDisplay(float p) => p * _scale;
        private float DisplayToPxX(double displayX) => (float)((displayX - _offsetX) / _scale);
        private float DisplayToPxY(double displayY) => (float)((displayY - _offsetY) / _scale);

        // ═══ 标注工具切换（由 ViewModel 调用） ═══

        /// <summary>切换标注工具（null=选择/裁剪模式）。切换时清除绘制/选中状态并重建视觉层。</summary>
        public void SetAnnotationKind(AnnotationKind? kind)
        {
            if (Tool == null) return;
            Tool.ActiveKind = kind;
            Tool.ResetInteraction();
            _pendingTextAnnotation = null;
            AnnotationTextInput.Visibility = Visibility.Collapsed;
            HideAnnotationChrome();
            RenderAnnotations();
        }

        /// <summary>清空标注视觉层（切图/清空列表时由外部调用；Tool.Items 由 ViewModel 清）</summary>
        public void RefreshAnnotationsVisual() => RenderAnnotations();
    }
}
