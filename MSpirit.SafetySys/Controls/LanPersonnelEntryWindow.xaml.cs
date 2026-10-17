// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.Peoplemanagement;
using MS_SafetyManagementSystem.Services;
using QRCoder;
using Serilog;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Controls
{
    public partial class LanPersonnelEntryWindow : FluentWindow
    {
        private readonly string _groupId;
        private readonly string _groupName;
        private readonly string _unitName;
        private LanService? _lanService;
        private readonly QRCodeGenerator _qrGenerator = new();
        private int _entryCount;

        /// <summary>已连接设备跟踪（三窗口共用，见 Services/LanDeviceTracker.cs；超时值见 LanDeviceTracker.DefaultTimeout，正常关页经 /bye 即时移除）</summary>
        private readonly LanDeviceTracker _deviceTracker = new();

        /// <summary>巡检间隔：配合手机端"仅页面可见时上报"，切后台/断网后最多约 12 秒内归零</summary>
        private static readonly TimeSpan DeviceSweepInterval = TimeSpan.FromSeconds(2);
        private System.Windows.Threading.DispatcherTimer? _deviceSweepTimer;

        public LanPersonnelEntryWindow(string groupId, string groupName, string? unitName = null)
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
                Log.Debug("[LanEntry] 查询单位名称失败（返回空）");
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
            PortText.Text = $"{ip}:{LanPorts.Start}";
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

                // 首次使用引导：检测防火墙放行（程序级阻止规则/放行规则），缺失则弹引导窗口。
                // 返回 false 不阻断：手机首次连接时 Windows 会弹「是否允许访问网络」窗口，点允许即放行。
                // 商店版（MSIX 包内）不走原生弹窗这条路径（规则由清单 windows.firewallRules 预置，
                // 且包内无法提权）→ 提示已由手动指引窗给出，此处不再叠加"等系统弹窗"的文案（2026-10-15，R3）。
                if (!LanFirewallService.EnsureFirewallAccess(this) && !LanFirewallService.IsRunningPackaged())
                    ToastService.Show("未自动放行：手机首次连接时，Windows 会弹出\n" +
                        "『是否允许访问网络』窗口，请务必点【允许访问】。\n" +
                        "若点【取消】会生成阻止规则导致无法连接，\n" +
                        "可停止服务重新启动后选『立即解除阻止』。", this, 3000);

                // 获取 OCR 服务实例（复用 PersonnelRecordCardView 的初始化逻辑）
                var ocrService = PersonnelRecordCardView.GetOcrService();

                _lanService = new LanService(dataService, ocrService)
                {
                    GroupName = _groupName,
                    UnitName = _unitName
                };
                _lanService.RecordConfirmed += OnRecordConfirmed;
                _lanService.PendingUpdated += OnPendingUpdated;
                _deviceTracker.Reset();
                DeviceCountText.Text = "0";
                _lanService.DeviceConnected += OnDeviceConnected;
                _lanService.DeviceLeft += OnDeviceLeft;
                _deviceSweepTimer = new System.Windows.Threading.DispatcherTimer { Interval = DeviceSweepInterval };
                _deviceSweepTimer.Tick += (s, e) => SweepDisconnectedDevices();
                _deviceSweepTimer.Start();

                // 自动寻找可用端口：从 LanPorts.Start 开始递增尝试 Start()（共用 LanPortProbe，捕获含 SocketException）
                int port = LanPortProbe.FindAvailable(LanPorts.Start, p => _lanService!.Start(p));
                if (port < 0)
                {
                    System.Windows.MessageBox.Show($"无法找到可用端口（{LanPorts.Start}-{LanPorts.ProbeEnd} 均被占用）", "错误",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }
                Log.Information("LanPersonnelEntryWindow: 端口探测成功：{Port}（绑定 IP={Ip}）", port, _lanService.BoundIpAddress);

                // 更新 UI 状态
                StatusIndicator.Fill = System.Windows.Media.Brushes.LimeGreen;
                StatusText.Text = "服务运行中";
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                PortText.Text = $"{_lanService.BoundIpAddress}:{_lanService.Port}";

                GenerateQRCode();

                Log.Information("LanPersonnelEntryWindow: 服务已启动，部门={GroupName}", _groupName);
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
                    _lanService.DeviceConnected -= OnDeviceConnected;
                    _lanService.DeviceLeft -= OnDeviceLeft;
                    _lanService.Stop();
                    _lanService.Dispose();
                    _lanService = null;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "LanPersonnelEntryWindow: 停止服务时出现异常");
            }
            finally
            {
                // 停止服务：停止心跳扫描、已连接设备归零（设备集合为本次服务会话的）
                if (_deviceSweepTimer != null)
                {
                    _deviceSweepTimer.Stop();
                    _deviceSweepTimer = null;
                }
                _deviceTracker.Reset();
                DeviceCountText.Text = "0";

                StatusIndicator.Fill = System.Windows.Media.Brushes.Gray;
                StatusText.Text = "服务未启动";
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                QRCodeImage.Visibility = Visibility.Collapsed;
                QRPlaceholder.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// 端口探测已抽到 <see cref="LanPortProbe.FindAvailable"/>（三窗口共用，见 Services/LanPortProbe.cs）：
        /// 从 LanPorts.Start 递增尝试启动，捕获 Kestrel 绑定失败（含 Windows 保留端口的 SocketException），
        /// 范围与 LanPorts.ProbeEnd 对齐（不再硬编码 +100），防火墙放行范围覆盖整个探测范围。
        /// </summary>

        /// <summary>生成 QR 码</summary>
        private void GenerateQRCode()
        {
            string boundIp = _lanService?.BoundIpAddress ?? LanService.GetLocalIPAddress();
            int port = _lanService?.Port ?? LanPorts.Start;
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

            Log.Information("LanPersonnelEntryWindow: QR码已生成，URL={Url}", url);
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

        /// <summary>任何授权请求到达服务时触发（参数：来源 IP）——记录心跳并统计"已连接设备"</summary>
        private void OnDeviceConnected(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return;
            Dispatcher.Invoke(() =>
            {
                _deviceTracker.OnConnected(ip);
                DeviceCountText.Text = _deviceTracker.Count.ToString();
            });
        }

        /// <summary>手机页面关闭上报（/bye）→ 立即从已连接设备移除（无需等心跳超时）</summary>
        private void OnDeviceLeft(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return;
            Dispatcher.Invoke(() =>
            {
                if (_deviceTracker.OnLeft(ip))
                    DeviceCountText.Text = _deviceTracker.Count.ToString();
            });
        }

        /// <summary>周期扫描：把超过超时无请求的设备从计数中移除（beacon 失败兜底）</summary>
        private void SweepDisconnectedDevices()
        {
            if (_deviceTracker.Sweep())
                DeviceCountText.Text = _deviceTracker.Count.ToString();
        }
    }
}
