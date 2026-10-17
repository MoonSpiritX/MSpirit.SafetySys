// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace MS_SafetyManagementSystem.Services
{
    public static class ToastService
    {
        private static Popup? _currentPopup;
        private static DispatcherTimer? _closeTimer;

        /// <summary>
        /// 在指定目标元素的中心显示一条 Toast 提示，带淡入淡出效果，自动消失。
        /// </summary>
        /// <param name="message">提示文本</param>
        /// <param name="targetElement">目标元素（如图片显示区 Border）</param>
        /// <param name="durationMs">显示时长（毫秒），默认 1500</param>
        public static void Show(string message, UIElement targetElement, int durationMs = 1500)
        {
            if (targetElement == null) return;

            // 关闭已有的 Toast（如果正在显示）
            CloseCurrentToast();

            // 构建 Toast 内容
            var textBlock = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 13,
                FontFamily = new FontFamily("Microsoft YaHei"),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,   // 长文案自动换行（2026-08-16：LAN 诊断提示为多行内容）
                MaxWidth = 400,                     // 换行上限，避免整屏横贯
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)), // 半透明黑色
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 8, 16, 8),
                Child = textBlock,
                Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.3 },
                // Toast 为纯展示提示、无交互：禁用命中测试，鼠标穿透到下层窗口，
                // 避免拖放过程中鼠标移到 Toast 上时被 Popup 拦截导致松手失败（2026-08-12）
                IsHitTestVisible = false
            };

            // 创建 Popup：由 WPF 原生居中于目标元素（PlacementMode.Center + PlacementTarget）。
            // 2026-08-16 修复：此前手工计算中心（PointToScreen + RenderSize/2）存在坐标双重偏移——
            // 偏移量正比于窗口屏幕位置（窗口越靠右下，Toast 越偏右下）；且手工换算在非 100% DPI /
            // FluentWindow 自定义标题栏下都会出错。改用框架原生居中，全部由 WPF 处理（含 DPI）。
            _currentPopup = new Popup
            {
                Child = border,
                AllowsTransparency = true,
                StaysOpen = true,
                Placement = PlacementMode.Center,
                PlacementTarget = targetElement
            };

            // 初始透明度为 0
            border.Opacity = 0;
            _currentPopup.IsOpen = true;

            // 淡入动画
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            border.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            // 设置定时器自动关闭
            _closeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(durationMs)
            };
            _closeTimer.Tick += (s, e) =>
            {
                _closeTimer?.Stop();
                FadeOutAndClose();
            };
            _closeTimer.Start();
        }

        private static void FadeOutAndClose()
        {
            if (_currentPopup?.Child is Border border)
            {
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                fadeOut.Completed += (s, _) =>
                {
                    if (_currentPopup != null)
                        _currentPopup.IsOpen = false;
                    _currentPopup = null;
                };
                border.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
            else
            {
                if (_currentPopup != null)
                    _currentPopup.IsOpen = false;
                _currentPopup = null;
            }
        }

        private static void CloseCurrentToast()
        {
            if (_currentPopup != null && _currentPopup.IsOpen)
            {
                if (_closeTimer != null)
                {
                    _closeTimer.Stop();
                    _closeTimer = null;
                }
                // 立即关闭，不播放淡出（避免残留）
                _currentPopup.IsOpen = false;
                _currentPopup = null;
            }
        }
    }
}