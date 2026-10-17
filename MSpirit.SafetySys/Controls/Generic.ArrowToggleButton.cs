// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MS_SafetyManagementSystem.Controls
{
    /// <summary>箭头初始方向（决定旋转模式）。</summary>
    public enum ArrowDirection
    {
        /// <summary>朝下（下拉菜单风格）：收起朝下 ▼，展开旋转 180° 朝上 ▲。</summary>
        Down,
        /// <summary>朝右（树/文件夹展开风格）：折叠朝右 ▶，展开旋转 90° 朝下 ▼。</summary>
        Right,
    }

    /// <summary>
    /// 带旋转动画的箭头切换按钮（独立控件）。
    /// 参考记录卡下拉箭头样式与 MSpirit 的 WindowAnimationHelper 动画逻辑，合并为可复用控件。
    /// 默认样式（箭头模板 + 旋转动画）由 Themes/Generic.xaml 提供，
    /// 控件类通过 DefaultStyleKeyProperty 指定默认样式类型，
    /// Generic.xaml 以同 TargetType 的隐式样式提供模板；
    /// 复用本控件时需同时拷贝本文件与 Themes/Generic.xaml。
    /// </summary>
    public class ArrowToggleButton : ToggleButton
    {
        public static readonly DependencyProperty DirectionProperty =
            DependencyProperty.Register(nameof(Direction), typeof(ArrowDirection), typeof(ArrowToggleButton),
                new PropertyMetadata(ArrowDirection.Down, OnDirectionChanged));

        /// <summary>旋转动画时长（毫秒）。</summary>
        public static readonly DependencyProperty DurationProperty =
            DependencyProperty.Register(nameof(Duration), typeof(double), typeof(ArrowToggleButton),
                new PropertyMetadata(150.0));

        public static readonly DependencyProperty StrokeProperty =
            DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(ArrowToggleButton),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66))));

        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(ArrowToggleButton),
                new PropertyMetadata(1.0));

        static ArrowToggleButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ArrowToggleButton),
                new FrameworkPropertyMetadata(typeof(ArrowToggleButton)));

            // 监听 IsChecked 变化（含代码/绑定设置），统一驱动旋转动画
            IsCheckedProperty.OverrideMetadata(typeof(ArrowToggleButton),
                new FrameworkPropertyMetadata(OnIsCheckedPropertyChanged));
        }

        public ArrowDirection Direction
        {
            get => (ArrowDirection)GetValue(DirectionProperty);
            set => SetValue(DirectionProperty, value);
        }

        public double Duration
        {
            get => (double)GetValue(DurationProperty);
            set => SetValue(DurationProperty, value);
        }

        public Brush Stroke
        {
            get => (Brush)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            UpdateArrow(animate: false);
        }

        private static void OnIsCheckedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((ArrowToggleButton)d).UpdateArrow(animate: true);
        }

        private static void OnDirectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((ArrowToggleButton)d).UpdateArrow(animate: false);
        }

        private void UpdateArrow(bool animate)
        {
            if (GetTemplateChild("Arrow") is not Path arrow) return;
            if (arrow.RenderTransform is not RotateTransform rotate) return;

            // 模板中的 Freezable（RotateTransform）可能被 WPF 冻结以共享实例，
            // 冻结对象无法修改/动画，先克隆解冻（与 MSpirit WindowAnimationHelper 一致）。
            if (rotate.IsFrozen)
            {
                rotate = rotate.Clone();
                arrow.RenderTransform = rotate;
            }

            double target = Direction switch
            {
                // Right：折叠朝右（270°），展开朝下（360°），与 MSpirit ModuleFolderGroup 一致
                ArrowDirection.Right => IsChecked == true ? 360 : 270,
                // Down：收起朝下（0°），展开朝上（180°），与记录卡下拉箭头一致
                _ => IsChecked == true ? 180 : 0,
            };

            if (animate)
            {
                var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(Duration));
                rotate.BeginAnimation(RotateTransform.AngleProperty, animation);
            }
            else
            {
                rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                rotate.Angle = target;
            }
        }
    }
}
