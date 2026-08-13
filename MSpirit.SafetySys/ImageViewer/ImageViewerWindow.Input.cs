// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MS_SafetyManagementSystem.ImageViewer
{
    public partial class ImageViewerWindow
    {
        #region 鼠标事件（缩放/平移/窗口拖动）

        private void ImageViewer_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0) ZoomInFullScreen();
            else ZoomOutFullScreen();
            e.Handled = true;
        }

        private bool IsClickOnImage(float x, float y, float offsetX, float offsetY)
        {
            var displayBmp = CurrentBitmap ?? _currentBitmap;
            if (displayBmp == null) return false;

            float skW = (float)ImageViewer.ActualWidth;
            float skH = (float)ImageViewer.ActualHeight;
            if (skW <= 0 || skH <= 0) return false;

            float imageCenterX = offsetX + skW * 0.5f;
            float imageCenterY = offsetY + skH * 0.5f;
            float rx = x - imageCenterX;
            float ry = y - imageCenterY;

            rx -= _panX;
            ry -= _panY;

            if (Math.Abs(_flipX) > 0.001f) rx /= _flipX;
            if (Math.Abs(_flipY) > 0.001f) ry /= _flipY;

            float angleRad = -_rotationAngle * MathF.PI / 180f;
            float cosA = MathF.Cos(angleRad);
            float sinA = MathF.Sin(angleRad);
            float trx = rx * cosA - ry * sinA;
            float try_ = rx * sinA + ry * cosA;
            rx = trx;
            ry = try_;

            if (Math.Abs(_zoomFactor) > 0.001f)
            {
                rx /= _zoomFactor;
                ry /= _zoomFactor;
            }

            float fitScale = Math.Min(skW / displayBmp.PixelWidth, skH / displayBmp.PixelHeight);
            float halfW = displayBmp.PixelWidth * fitScale * 0.5f;
            float halfH = displayBmp.PixelHeight * fitScale * 0.5f;

            return Math.Abs(rx) <= halfW && Math.Abs(ry) <= halfH;
        }

        private void ImageViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_inertiaActive)
            {
                _inertiaActive = false;
                CompositionTarget.Rendering -= OnPanInertiaRendering;
            }

            Point clickPos = e.GetPosition(_parentGrid);

            var imageOffsetInBorder = VisualTreeHelper.GetOffset(ImageViewer);
            var borderOffsetInGrid = VisualTreeHelper.GetOffset(ImageClipBorder);
            var offset = new Point(
                imageOffsetInBorder.X + borderOffsetInGrid.X,
                imageOffsetInBorder.Y + borderOffsetInGrid.Y);

            if (!IsClickOnImage((float)clickPos.X, (float)clickPos.Y, (float)offset.X, (float)offset.Y))
                return;

            if (e.ClickCount == 2)
            {
                ToggleFullScreen();
                e.Handled = true;
                return;
            }

            _isPanning = false;
            _panStartPoint = clickPos;
            _panStartX = _panX;
            _panStartY = _panY;
            _panStartValid = true;
            ImageViewer.CaptureMouse();
            e.Handled = true;
        }

        private void ImageViewer_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_panStartValid || e.LeftButton != MouseButtonState.Pressed) return;

            Point currentPos = e.GetPosition(_parentGrid);
            float dx = (float)(currentPos.X - _panStartPoint.X);
            float dy = (float)(currentPos.Y - _panStartPoint.Y);
            float distance = MathF.Sqrt(dx * dx + dy * dy);

            if (!_isPanning && distance > PanThreshold)
            {
                _isPanning = true;
                ImageViewer.Cursor = Cursors.ScrollAll;
            }

            if (_isPanning)
            {
                _panX = _panStartX + dx;
                _panY = _panStartY + dy;
                _panTransform.X = _panX;
                _panTransform.Y = _panY;
                UpdateVirtualImageRect();

                _panVelocitySamples.Enqueue((currentPos, DateTime.UtcNow.Ticks));
                while (_panVelocitySamples.Count > 5)
                    _panVelocitySamples.Dequeue();
            }
        }

        private void ImageViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            bool wasPanning = _isPanning;
            _isPanning = false;
            _panStartValid = false;
            ImageViewer.ReleaseMouseCapture();
            ImageViewer.Cursor = Cursors.Hand;

            if (wasPanning && _panVelocitySamples.Count >= 2)
            {
                var samples = _panVelocitySamples.ToArray();
                var first = samples[0];
                var last = samples[^1];
                double dt = (last.ticks - first.ticks) / (double)TimeSpan.TicksPerSecond;
                if (dt > 0.001)
                {
                    _inertiaVelocityX = (last.pos.X - first.pos.X) / dt;
                    _inertiaVelocityY = (last.pos.Y - first.pos.Y) / dt;
                    _inertiaActive = true;
                    CompositionTarget.Rendering += OnPanInertiaRendering;
                }
            }
            _panVelocitySamples.Clear();
        }

        private void OnPanInertiaRendering(object? sender, EventArgs e)
        {
            if (!_inertiaActive) return;

            double frameDt = 1.0 / 60.0;

            _panX += (float)(_inertiaVelocityX * frameDt);
            _panY += (float)(_inertiaVelocityY * frameDt);
            _panTransform.X = _panX;
            _panTransform.Y = _panY;

            _inertiaVelocityX *= InertiaDecay;
            _inertiaVelocityY *= InertiaDecay;

            if (Math.Abs(_inertiaVelocityX) < InertiaStopThreshold &&
                Math.Abs(_inertiaVelocityY) < InertiaStopThreshold)
            {
                _inertiaActive = false;
                CompositionTarget.Rendering -= OnPanInertiaRendering;
            }

            UpdateVirtualImageRect();
        }

        private void Grid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isFullScreenMode) return;

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
                e.Handled = true;
            }
        }

        #endregion

        #region 键盘快捷键

        protected override async void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SafeCopyImageToClipboard();
                e.Handled = true;
                return;
            }

            if (_isSlideShowPlaying && (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Delete
                || e.Key == Key.Up || e.Key == Key.Down))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete)
            {
                DeleteCurrentImage();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F5)
            {
                if (_isSlideShowPlaying) StopSlideShow();
                else await StartSlideShow(startFrom: -1);
                UpdateSlideShowStatusDisplay();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F6)
            {
                if (!_isSlideShowPlaying) await StartSlideShow(startFrom: 0);
                UpdateSlideShowStatusDisplay();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                if (_isSlideShowPlaying)
                {
                    StopSlideShow();
                    UpdateSlideShowStatusDisplay();
                    e.Handled = true;
                    return;
                }
                Close();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F7 || e.Key == Key.F8)
            {
                if (_isSlideShowPlaying)
                {
                    if (e.Key == Key.F7)
                        _slideShowInterval = Math.Max(SlideShowMinInterval, _slideShowInterval - SlideShowStep);
                    else
                        _slideShowInterval = Math.Min(SlideShowMaxInterval, _slideShowInterval + SlideShowStep);
                    UpdateSlideShowTitle();
                }
                e.Handled = true;
                return;
            }

            if (!_isSlideShowPlaying)
            {
                if (e.Key == Key.F11)
                {
                    RotateImageCounterClockwise();
                    if (_isFullScreenMode) ShowOrientationStatus("[↺ 90°]");
                    e.Handled = true; return;
                }
                if (e.Key == Key.F12)
                {
                    RotateImageClockwise();
                    if (_isFullScreenMode) ShowOrientationStatus("[↻ 90°]");
                    e.Handled = true; return;
                }
                if (e.Key == Key.F9)
                {
                    if (Keyboard.Modifiers == ModifierKeys.Shift)
                    {
                        FlipImageVertical();
                        if (_isFullScreenMode) ShowOrientationStatus("[↕ 垂直翻转]");
                    }
                    else
                    {
                        FlipImageHorizontal();
                        if (_isFullScreenMode) ShowOrientationStatus("[↔ 水平翻转]");
                    }
                    e.Handled = true; return;
                }
                if (e.Key == Key.Back)
                {
                    ResetImageOrientation();
                    if (_isFullScreenMode) ShowOrientationStatus("[已重置]");
                    e.Handled = true; return;
                }
            }

            if (e.Key == Key.Space)
            {
                float targetZoom;
                float targetPanX, targetPanY;

                if (_isFullScreenMode)
                {
                    var screenArea = ImageViewerHelper.GetScreenWorkingArea(this);
                    double borderCenterX = MainContentBorder.Margin.Left + MainContentBorder.ActualWidth / 2.0;
                    double borderCenterY = MainContentBorder.Margin.Top + MainContentBorder.ActualHeight / 2.0;
                    targetPanX = (float)(screenArea.Width / 2.0 - borderCenterX);
                    targetPanY = (float)(screenArea.Height / 2.0 - borderCenterY);

                    var bmp = CurrentBitmap ?? _currentBitmap;
                    if (bmp != null && bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
                    {
                        double screenFitScale = Math.Min(
                            screenArea.Width / bmp.PixelWidth, screenArea.Height / bmp.PixelHeight);
                        double lockedFitScale = Math.Min(
                            MainContentBorder.ActualWidth / bmp.PixelWidth, MainContentBorder.ActualHeight / bmp.PixelHeight);
                        targetZoom = (float)Math.Clamp(
                            InitialDisplayRatio * screenFitScale / lockedFitScale, MinZoom, MaxZoom);
                    }
                    else
                    {
                        targetZoom = InitialDisplayRatio;
                    }
                }
                else
                {
                    targetZoom = 1.0f;
                    targetPanX = 0f;
                    targetPanY = 0f;
                }

                if (_isUsingOriginal) DowngradeFromOriginal();

                AnimateReset(targetZoom, targetPanX, targetPanY);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.D0 || e.Key == Key.NumPad0)
            {
                if (_isFullScreenMode) { e.Handled = true; return; }
                FitWindowToImage();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                ZoomInFullScreen();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Down)
            {
                ZoomOutFullScreen();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Left)
            {
                if (_currentIndex <= 0) ShowTempMessage("已是第一张");
                else Navigate(-1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Right)
            {
                if (_currentIndex >= _imageFiles.Count - 1) ShowTempMessage("已是最后一张");
                else Navigate(1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                ToggleFullScreen();
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }

        #endregion

        #region 拖放

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0) return;
            string filePath = files[0];

            if (Directory.Exists(filePath))
            {
                if (_isSlideShowPlaying) StopSlideShow();
                bool success = await StartSlideShowFromFolder(filePath);
                if (!success) MessageBox.Show("文件夹中没有支持的图片文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!ImageViewerHelper.IsSupportedImage(filePath))
            {
                MessageBox.Show($"不支持的格式。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            await LoadDirectoryAndShow(filePath);
        }

        private async Task<bool> StartSlideShowFromFolder(string folderPath)
        {
            var allFiles = Directory.GetFiles(folderPath).Where(f => ImageViewerHelper.IsSupportedImage(f)).OrderBy(f => f).ToList();
            if (allFiles.Count == 0) return false;
            _imageFiles = allFiles; _currentIndex = 0;
            if (_isFullScreenMode) ToggleFullScreen();
            if (!this.IsVisible) this.Show();
            await StartSlideShow(startFrom: 0);
            return true;
        }

        private async Task LoadDirectoryAndShow(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath) ?? "";
            var allFiles = Directory.GetFiles(directory).Where(f => ImageViewerHelper.IsSupportedImage(f)).OrderBy(f => f).ToList();
            _imageFiles = allFiles;
            _currentIndex = _imageFiles.FindIndex(f => string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));
            await LoadAndDisplayImage(filePath);
        }

        #endregion
    }
}
