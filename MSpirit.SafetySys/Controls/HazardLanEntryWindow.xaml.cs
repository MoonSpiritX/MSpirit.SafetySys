// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.Services;
using QRCoder;
using Serilog;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Controls
{
    public partial class HazardLanEntryWindow : FluentWindow
    {
        private readonly string _groupId;
        private readonly string _groupName;
        private readonly string _yearMonth;
        private HazardLanService? _lanService;
        private readonly QRCodeGenerator _qrGenerator = new();
        private int _entryCount;

        public HazardLanEntryWindow(string groupId, string groupName, string yearMonth)
        {
            InitializeComponent();
            _groupId = groupId;
            _groupName = groupName;
            _yearMonth = yearMonth;

            GroupText.Text = $"年月分组：{groupName}";
            Owner = Application.Current.MainWindow;

            // 请求禁用项目列表菜单（引用计数，与导出/导入共用；防止切换项目导致服务崩溃）
            RequestProjectMenuDisable();

            Loaded += OnLoaded;
            Activated += OnActivated;
            Closed += OnClosedWindow;
        }

        private void OnClosedWindow(object? sender, EventArgs e)
        {
            StopService();
            _qrGenerator.Dispose();
            ReleaseProjectMenuDisable();
        }

        private static void RequestProjectMenuDisable()
        {
            if (Application.Current.MainWindow is MainWindow mainWin)
                mainWin.MainMenuBar?.RequestProjectMenuDisable();
        }

        private static void ReleaseProjectMenuDisable()
        {
            if (Application.Current.MainWindow is MainWindow mainWin)
                mainWin.MainMenuBar?.ReleaseProjectMenuDisable();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshIPDisplay();
        }

        /// <summary>换网络后回到本窗口时自动检测：未启动则刷新 IP 显示，已启动且 IP 变化则提示二维码失效</summary>
        private void OnActivated(object? sender, EventArgs e)
        {
            if (_lanService?.IsRunning == true)
            {
                string currentIp = LanService.GetLocalIPAddress();
                if (currentIp != _lanService.BoundIpAddress)
                {
                    StatusText.Text = "本机 IP 已变化，二维码失效，请停止后重新启动";
                    StatusIndicator.Fill = Brushes.Orange;
                }
            }
            else
            {
                RefreshIPDisplay();
            }
        }

        private void RefreshIPDisplay()
        {
            var ip = LanService.GetLocalIPAddress();
            PortText.Text = $"{ip}:5000";
        }

        private void DragMoveWindow(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(this);
            if (pos.Y < 50) return;
            try { DragMove(); e.Handled = true; }
            catch (InvalidOperationException) { /* 未按住左键时 DragMove 抛此异常，属正常流程 */ }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lanService?.IsRunning == true) return;

            try
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                var dataService = mainWindow?.ViewModel?.GetProjectDataService();
                if (dataService == null)
                {
                    System.Windows.MessageBox.Show("无法获取数据服务，请先打开一个项目。", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                _lanService = new HazardLanService(dataService, _yearMonth, _groupName);
                _lanService.RecordConfirmed += OnRecordConfirmed;

                int port = StartOnAvailablePort(5000);
                if (port < 0)
                {
                    System.Windows.MessageBox.Show("无法找到可用端口（5000-5099 均被占用）", "错误",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                StatusIndicator.Fill = Brushes.LimeGreen;
                StatusText.Text = "服务运行中";
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                PortText.Text = $"{_lanService.BoundIpAddress}:{_lanService.Port}";

                GenerateQRCode();
                Log.Information("HazardLanEntryWindow: 服务已启动，年月分组={GroupName}", _groupName);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"启动服务失败：{ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e) => StopService();

        private void StopService()
        {
            try
            {
                if (_lanService != null)
                {
                    _lanService.RecordConfirmed -= OnRecordConfirmed;
                    _lanService.Stop();
                    _lanService.Dispose();
                    _lanService = null;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "HazardLanEntryWindow: 停止服务时出现异常");
            }
            finally
            {
                StatusIndicator.Fill = Brushes.Gray;
                StatusText.Text = "服务未启动";
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                QRCodeImage.Visibility = Visibility.Collapsed;
                QRPlaceholder.Visibility = Visibility.Visible;
            }
        }

        private int StartOnAvailablePort(int startPort)
        {
            for (int port = startPort; port < startPort + 100; port++)
            {
                try
                {
                    _lanService!.Start(port);
                    return port;
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Net.HttpListenerException)
                {
                    // 端口绑定失败（占用等），继续尝试下一个
                    Log.Information("HazardLanEntryWindow: 端口 {Port} 不可用，尝试下一个", port);
                    continue;
                }
            }
            return -1;
        }

        private void GenerateQRCode()
        {
            string boundIp = _lanService?.BoundIpAddress ?? LanService.GetLocalIPAddress();
            int port = _lanService?.Port ?? 5000;
            string url = $"http://{boundIp}:{port}/?groupId={_groupId}&groupName={Uri.EscapeDataString(_groupName)}&yearMonth={Uri.EscapeDataString(_yearMonth)}&token={_lanService?.Token}";

            var qrData = _qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            using var png = new PngByteQRCode(qrData);
            var bytes = png.GetGraphic(20);

            using var ms = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();

            QRCodeImage.Source = bitmap;
            QRCodeImage.Visibility = Visibility.Visible;
            QRPlaceholder.Visibility = Visibility.Collapsed;
            Log.Information("HazardLanEntryWindow: QR码已生成");
        }

        private void OnRecordConfirmed(string groupId, string recordId)
        {
            Dispatcher.Invoke(async () =>
            {
                _entryCount = _lanService?.EntryCount ?? Interlocked.Increment(ref _entryCount);
                CountText.Text = _entryCount.ToString();

                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow?.ViewModel != null)
                {
                    await Task.Delay(100);
                    // 刷新树视图（新创建的日期分组需要显示）
                    mainWindow.ViewModel.GroupTree?.LoadGroups();
                    // 刷新记录列表
                    var currentGroupId = mainWindow.ViewModel.GroupTree?.SelectedNode?.Id;
                    if (currentGroupId != null)
                        mainWindow.ViewModel.Records.LoadRecords(currentGroupId);
                    mainWindow.UpdateStatusText();
                }
            });
        }
    }
}
