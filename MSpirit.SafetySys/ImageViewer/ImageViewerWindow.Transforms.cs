// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MS_SafetyManagementSystem.ImageViewer
{
    public partial class ImageViewerWindow
    {
        #region 变换操作（旋转/翻转/复位）

        private void ResetViewTransform()
        {
            _rotationAngle = 0;
            _flipX = 1f;
            _flipY = 1f;
            StopZoomSpring();
            _zoomFactor = 1.0f;
            _targetZoom = _zoomFactor;
            _panX = 0f;
            _panY = 0f;
            _isPanning = false;
            _panStartValid = false;
            _scaleZoomTransform.ScaleX = 1.0;
            _scaleZoomTransform.ScaleY = 1.0;
            _panTransform.X = 0;
            _panTransform.Y = 0;
            _rotateTransform.Angle = 0;
            _flipTransform.ScaleX = 1;
            _flipTransform.ScaleY = 1;
        }

        /// <summary>
        /// 仅重置旋转/翻转，保留缩放和平移（切图时调用，zoom/pan 跨图片传递）。
        /// </summary>
        private void ResetRotationOnly()
        {
            _rotationAngle = 0;
            _flipX = 1f;
            _flipY = 1f;
            _rotateTransform.Angle = 0;
            _flipTransform.ScaleX = 1;
            _flipTransform.ScaleY = 1;
            UpdateImageDisplay();
        }

        internal void RotateImageCounterClockwise()
        {
            _rotationAngle = (_rotationAngle - 90) % 360;
            AnimateRotationTo(_rotationAngle);
        }

        internal void RotateImageClockwise()
        {
            _rotationAngle = (_rotationAngle + 90) % 360;
            AnimateRotationTo(_rotationAngle);
        }

        private void AnimateRotationTo(float targetAngle)
        {
            _rotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
            double from = _rotateTransform.Angle;
            double to = targetAngle;
            // 取最短旋转路径
            double delta = to - from;
            if (Math.Abs(delta) > 180)
                to -= Math.Sign(delta) * 360;

            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            Timeline.SetDesiredFrameRate(anim, 120);
            anim.Completed += (_, _) =>
            {
                _rotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
                _rotateTransform.Angle = _rotationAngle;
            };
            _rotateTransform.BeginAnimation(RotateTransform.AngleProperty, anim);
        }

        internal void FlipImageHorizontal()
        {
            _flipX *= -1f;
            _flipTransform.ScaleX = _flipX;
        }

        internal void FlipImageVertical()
        {
            _flipY *= -1f;
            _flipTransform.ScaleY = _flipY;
        }

        internal void ResetImageOrientation()
        {
            _rotationAngle = 0;
            _flipX = 1f;
            _flipY = 1f;
            StopZoomSpring();
            _zoomFactor = InitialDisplayRatio;
            _targetZoom = _zoomFactor;
            _panX = 0f;
            _panY = 0f;
            UpdateImageDisplay();
        }

        /// <summary>
        /// 动画复位：zoom、pan、旋转并行动画过渡到目标状态，翻转直接设置。
        /// </summary>
        private void AnimateReset(float targetZoom, float targetPanX, float targetPanY)
        {
            // 停止正在进行的惯性滑行和缩放动画
            if (_inertiaActive)
            {
                _inertiaActive = false;
                CompositionTarget.Rendering -= OnPanInertiaRendering;
            }
            StopZoomSpring();

            // 旋转动画
            _rotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
            double fromRot = _rotateTransform.Angle;
            double toRot = 0;
            double delta = toRot - fromRot;
            if (Math.Abs(delta) > 180) toRot -= Math.Sign(delta) * 360;
            var rotAnim = new DoubleAnimation(fromRot, toRot, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            Timeline.SetDesiredFrameRate(rotAnim, 120);

            // 缩放动画
            _scaleZoomTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _scaleZoomTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            var zoomAnim = new DoubleAnimation(_zoomFactor, targetZoom, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            Timeline.SetDesiredFrameRate(zoomAnim, 120);

            // 平移动画
            _panTransform.BeginAnimation(TranslateTransform.XProperty, null);
            _panTransform.BeginAnimation(TranslateTransform.YProperty, null);
            var panXAnim = new DoubleAnimation(_panX, targetPanX, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            Timeline.SetDesiredFrameRate(panXAnim, 120);
            var panYAnim = new DoubleAnimation(_panY, targetPanY, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            Timeline.SetDesiredFrameRate(panYAnim, 120);

            // 翻转直接设置（无需动画）
            _flipX = 1f; _flipY = 1f;
            _flipTransform.ScaleX = 1; _flipTransform.ScaleY = 1;

            // 状态立即更新（计算用），动画结束后清理
            _rotationAngle = 0;
            _zoomFactor = targetZoom;
            _panX = targetPanX;
            _panY = targetPanY;

            _rotateTransform.BeginAnimation(RotateTransform.AngleProperty, rotAnim);
            _scaleZoomTransform.BeginAnimation(ScaleTransform.ScaleXProperty, zoomAnim);
            _scaleZoomTransform.BeginAnimation(ScaleTransform.ScaleYProperty, zoomAnim);
            _panTransform.BeginAnimation(TranslateTransform.XProperty, panXAnim);
            _panTransform.BeginAnimation(TranslateTransform.YProperty, panYAnim);

            // 动画结束后清理
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(350);
                _rotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
                _scaleZoomTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                _scaleZoomTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _panTransform.BeginAnimation(TranslateTransform.XProperty, null);
                _panTransform.BeginAnimation(TranslateTransform.YProperty, null);
                UpdateImageDisplay();
            }, DispatcherPriority.Background);
        }

        public (float angle, float scaleX, float scaleY) GetViewerOrientation()
        {
            return (_rotationAngle, _flipX, _flipY);
        }

        /// <summary>
        /// 同步变换状态（从全屏窗口切换过来时调用）
        /// </summary>
        internal void ApplyTransform(float zoom, float panX, float panY, float angle, float flipX, float flipY)
        {
            _zoomFactor = zoom;
            _panX = panX;
            _panY = panY;
            _rotationAngle = angle;
            _flipX = flipX;
            _flipY = flipY;
            UpdateImageDisplay();
        }

        #endregion

        #region 缩放 Chase 引擎

        private void ZoomInFullScreen() => SetZoomFullScreen(_zoomFactor * MathF.Exp(ZoomStepExponent));
        private void ZoomOutFullScreen() => SetZoomFullScreen(_zoomFactor * MathF.Exp(-ZoomStepExponent));

        private void SetZoomFullScreen(float newZoom)
        {
            newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
            newZoom = MathF.Round(newZoom, 3);
            if (Math.Abs(newZoom - _targetZoom) < 0.0001f) return;

            _targetZoom = newZoom;

            // 根据距上次输入的间隔判断单次/连续模式，动态切换 chase rate
            long now = DateTime.UtcNow.Ticks;
            _chaseRate = (now - _lastZoomInputTick < ZoomInputGapTicks)
                ? ContinuousRate : SingleStepRate;
            _lastZoomInputTick = now;

            if (!_zoomChaseActive)
            {
                _zoomChaseActive = true;
                _zoomLastFrameTicks = now;
                _zoomFactor = (float)_scaleZoomTransform.ScaleX;
                CompositionTarget.Rendering += OnZoomChaseRendering;
            }

            if (!_isUsingOriginal && _targetZoom > 1.0f) UpgradeToOriginal();
            else if (_isUsingOriginal && _targetZoom <= 1.0f) DowngradeFromOriginal();
        }

        private void OnZoomChaseRendering(object? sender, EventArgs e)
        {
            if (!_zoomChaseActive) return;

            long nowTicks = DateTime.UtcNow.Ticks;
            double dt = (nowTicks - _zoomLastFrameTicks) / (double)TimeSpan.TicksPerSecond;
            _zoomLastFrameTicks = nowTicks;

            double gap = (double)_targetZoom - _zoomFactor;
            double absGap = Math.Abs(gap);

            // 比例速度：speed ∝ gap，动画时长恒定，与 zoom 级别无关
            // gap 以指数衰减逼近零点：gap(t) = gap(0) × e^(-rate × t)
            // _chaseRate 由 SetZoomFullScreen 按单次/连续模式动态设置
            double speed = absGap * _chaseRate;
            double step = speed * dt;

            if (absGap <= step || absGap < 0.0005f)
            {
                _zoomFactor = _targetZoom;
                _zoomChaseActive = false;
                CompositionTarget.Rendering -= OnZoomChaseRendering;
            }
            else
            {
                _zoomFactor += (float)(Math.Sign(gap) * step);
            }

            _scaleZoomTransform.ScaleX = _zoomFactor;
            _scaleZoomTransform.ScaleY = _zoomFactor;
            UpdateVirtualImageRect();
        }

        private void StopZoomSpring()
        {
            if (!_zoomChaseActive) return;
            _zoomChaseActive = false;
            CompositionTarget.Rendering -= OnZoomChaseRendering;
        }

        #endregion

        #region 全屏原图模式

        private async void UpgradeToOriginal()
        {
            if (_isUsingOriginal) return;
            if (_currentIndex < 0) return;
            string originalPath = _imageFiles[_currentIndex];
            if (string.IsNullOrEmpty(originalPath)) return;

            int currentIndex = _currentIndex;
            _isUsingOriginal = true;

            if (_originalCache.TryGetValue(originalPath, out var cached))
            {
                _originalBitmap = cached;
                UpdateImageDisplay();
                UpdateVirtualImageRect();
                return;
            }

            var originalBmp = await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(originalPath)) return null;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(originalPath);
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
                catch { return null; }
            });

            if (originalBmp == null) { _isUsingOriginal = false; return; }

            // 写入原图 LRU 缓存
            _originalCache[originalPath] = originalBmp;
            _originalCacheLru.Remove(originalPath);
            _originalCacheLru.AddLast(originalPath);
            while (_originalCache.Count > MaxOriginalCacheItems)
            {
                var oldest = _originalCacheLru.First;
                if (oldest == null) break;
                _originalCache.Remove(oldest.Value);
                _originalCacheLru.RemoveFirst();
            }

            if (_currentIndex != currentIndex) { _isUsingOriginal = false; return; }

            await Dispatcher.InvokeAsync(() =>
            {
                _originalBitmap?.Freeze();
                _originalBitmap = originalBmp;
                UpdateImageDisplay();
                UpdateVirtualImageRect();
            });
        }

        // 原图模式下 CurrentBitmap 返回原图，否则返回缓存图
        private BitmapSource? CurrentBitmap =>
            _isUsingOriginal && _originalBitmap != null ? _originalBitmap : _currentBitmap;

        private void DowngradeFromOriginal()
        {
            if (!_isUsingOriginal) return;
            _isUsingOriginal = false;
            _originalBitmap = null;
            UpdateImageDisplay();
            UpdateVirtualImageRect();
        }

        #endregion
    }
}
