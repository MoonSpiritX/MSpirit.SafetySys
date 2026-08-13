// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.Peoplemanagement;
using MS_SafetyManagementSystem.Services;
using QRCoder;
using Serilog;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Controls
{
    public partial class LanEntryWindow : FluentWindow
    {
        private readonly string _groupId;
        private readonly string _groupName;
        private readonly string _unitName;
        private LanService? _lanService;
        private readonly QRCodeGenerator _qrGenerator = new();
        private int _entryCount;

        public LanEntryWindow(string groupId, string groupName, string? unitName = null)
        {
            InitializeComponent();
            _groupId = groupId;
            _groupName = groupName;
            _unitName = unitName ?? LookupUnitName();

            UnitText.Text = $"单位：{_unitName}";
            Owner = Application.Current.MainWindow;

            // 请求禁用项目列表菜单（引用计数，与导出/导入共用；防止切换项目导致服务崩溃）
            RequestProjectMenuDisable();

            Loaded += OnLoaded;
            Activated += OnActivated;

            // 关闭时释放禁用请求（引用计数自动处理多窗口并存）
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

        /// <summary>通过数据服务查找上级单位名称</summary>
        private string LookupUnitName()
        {
            try
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                var dataService = mainWindow?.ViewModel?.GetProjectDataService();
                if (dataService == null) return "";

                var rootGroupId = dataService.GetPersonnelUnitGroupId(_groupId);
                var allGroups = dataService.GetAllGroups("");
                var unitGroup = allGroups.FirstOrDefault(g => g.Id == rootGroupId);
                return unitGroup?.Name ?? "";
            }
            catch
            {
                return "";
            }
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
                    StatusIndicator.Fill = System.Windows.Media.Brushes.Orange;
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

        /// <summary>
        /// 内容区域拖拽移动窗口（冒泡事件，按钮会自动消费事件不触发拖拽）
        /// 排除标题栏区域（由 WindowTitleBar 自己处理拖拽）
        /// </summary>
        private void DragMoveWindow(object sender, MouseButtonEventArgs e)
        {
            // 排除标题栏区域（Row 0 高度 50px，由 WindowTitleBar 自己处理）
            var pos = e.GetPosition(this);
            if (pos.Y < 50) return;

            try
            {
                DragMove();
                e.Handled = true;
            }
            catch (InvalidOperationException)
            {
                // DragMove 可能在极端时序下重复调用，静默忽略
            }
        }

        /// <summary>启动服务</summary>
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lanService?.IsRunning == true) return;

            try
            {
                // 获取 ProjectDataService
                var mainWindow = Application.Current.MainWindow as MainWindow;
                var dataService = mainWindow?.ViewModel?.GetProjectDataService();
                if (dataService == null)
                {
                    System.Windows.MessageBox.Show("无法获取数据服务，请先打开一个项目。", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // 获取 OCR 服务实例（复用 PersonnelRecordCardView 的初始化逻辑）
                var ocrService = PersonnelRecordCardView.GetOcrService();

                _lanService = new LanService(dataService, ocrService)
                {
                    GroupName = _groupName,
                    UnitName = _unitName
                };
                _lanService.RecordConfirmed += OnRecordConfirmed;
                _lanService.PendingUpdated += OnPendingUpdated;

                // 自动寻找可用端口：从 5000 开始递增尝试 Start()
                int port = StartOnAvailablePort(5000);
                if (port < 0)
                {
                    System.Windows.MessageBox.Show("无法找到可用端口（5000-5099 均被占用）", "错误",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                // 更新 UI 状态
                StatusIndicator.Fill = System.Windows.Media.Brushes.LimeGreen;
                StatusText.Text = "服务运行中";
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                PortText.Text = $"{_lanService.BoundIpAddress}:{_lanService.Port}";

                GenerateQRCode();

                Log.Information("LanEntryWindow: 服务已启动，部门={GroupName}", _groupName);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"启动服务失败：{ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>停止服务</summary>
        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopService();
        }

        private void StopService()
        {
            try
            {
                if (_lanService != null)
                {
                    _lanService.RecordConfirmed -= OnRecordConfirmed;
                    _lanService.PendingUpdated -= OnPendingUpdated;
                    _lanService.Stop();
                    _lanService.Dispose();
                    _lanService = null;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "LanEntryWindow: 停止服务时出现异常");
            }
            finally
            {
                StatusIndicator.Fill = System.Windows.Media.Brushes.Gray;
                StatusText.Text = "服务未启动";
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                QRCodeImage.Visibility = Visibility.Collapsed;
                QRPlaceholder.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// 从 startPort 开始递增尝试启动 LanService，返回成功绑定的端口号。
        /// 用实际的 HttpListener.Start() 检测，比 TCP 探测更准确。
        /// </summary>
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
                    Log.Information("LanEntryWindow: 端口 {Port} 不可用，尝试下一个", port);
                    continue;
                }
            }
            return -1;
        }

        /// <summary>生成 QR 码</summary>
        private void GenerateQRCode()
        {
            string boundIp = _lanService?.BoundIpAddress ?? LanService.GetLocalIPAddress();
            int port = _lanService?.Port ?? 5000;
            string url = $"http://{boundIp}:{port}/?groupId={_groupId}&groupName={Uri.EscapeDataString(_groupName)}&unitName={Uri.EscapeDataString(_unitName)}&token={_lanService?.Token}";

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

            Log.Information("LanEntryWindow: QR码已生成，URL={Url}", url);
        }

        /// <summary>确认记录后回调</summary>
        private void OnRecordConfirmed(string groupId, string recordId)
        {
            Dispatcher.Invoke(async () =>
            {
                _entryCount++;
                CountText.Text = _entryCount.ToString();

                // 刷新桌面用户当前选中的部门，不改变用户当前视图
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow?.ViewModel?.PersonnelRecords != null)
                {
                    await Task.Delay(100); // 给 DB 一点时间完成写入
                    var currentGroupId = mainWindow.ViewModel.GroupTree?.SelectedNode?.Id;
                    if (currentGroupId != null)
                        mainWindow.ViewModel.PersonnelRecords.LoadRecords(currentGroupId);
                    mainWindow.UpdateStatusText(); // 刷新"xx条记录"显示
                }
            });
        }

        /// <summary>待处理更新（OCR 完成/丢弃等）</summary>
        private void OnPendingUpdated()
        {
            // 目前仅用于计数，可扩展显示待处理数
        }
    }
}
