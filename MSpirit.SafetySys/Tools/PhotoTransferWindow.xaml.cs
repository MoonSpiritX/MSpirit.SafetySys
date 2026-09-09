// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Controls;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.Helpers;
using QRCoder;
using Serilog;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Tools
{
    /// <summary>位置列表项：默认位置显示"默认位置"，自定义位置显示末层文件夹名；ToolTip 均为完整路径</summary>
    public class LocationItem
    {
        public string FullPath { get; init; } = "";
        public bool IsDefault { get; init; }
        public string DisplayName => IsDefault ? "默认位置" : GetLeafName(FullPath);

        private static string GetLeafName(string path)
        {
            var trimmed = path.TrimEnd('\\', '/');
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? trimmed : name;
        }
    }

    /// <summary>传输文件列表项：序号 / 拍摄时间 / 文件名 / 大小 / 完整落盘路径</summary>
    public class PhotoTransferItem
    {
        public int Index { get; set; }
        /// <summary>拍摄（生成）时间：以手机内 EXIF 拍摄时间为准，无 EXIF 用手机文件生成时间或上传时间</summary>
        public DateTime ShootTime { get; init; }
        public string FileName { get; init; } = "";
        public long SizeBytes { get; init; }
        /// <summary>图片在电脑上的完整落盘路径（双击打开 / 右键操作使用）</summary>
        public string FullPath { get; init; } = "";

        public string ShootTimeText => ShootTime.ToString("yyyy-MM-dd HH:mm:ss");
        public string SizeText => FormatSize(SizeBytes);

        private static string FormatSize(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
            _ => $"{bytes / (1024.0 * 1024):0.#} MB"
        };
    }

    /// <summary>
    /// 手机图片传输窗口：左侧保存位置列表（默认用户-图片 + 自定义，PhotoTransferLocations.json 持久化），
    /// 右侧说明、传输文件列表、二维码与启动/停止。支持把文件夹拖入窗口任意位置添加到保存位置列表。
    /// 不绑定项目（纯落盘工具），无需切换项目保护。Owner 由调用方设置（托盘入口不设 Owner 独立弹出）。
    /// </summary>
    public partial class PhotoTransferWindow : FluentWindow
    {
        private readonly PhotoTransferLocationsService _locationsService;
        private PhotoTransferService? _service;
        private readonly QRCodeGenerator _qrGenerator = new();

        public ObservableCollection<PhotoTransferItem> FileItems { get; } = new();

        public PhotoTransferWindow()
        {
            InitializeComponent();
            DataContext = this;
            FileListGrid.ItemsSource = FileItems;

            var dataPathService = AppServiceProvider.GetDataPathService();
            _locationsService = new PhotoTransferLocationsService(dataPathService.RootPath);

            // 窗口级拖放：文件夹拖入任意位置 → 添加到左侧保存位置列表
            DragOver += (s, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                    ? DragDropEffects.Copy
                    : DragDropEffects.None;
            };
            Drop += (s, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
                {
                    bool any = false;
                    foreach (var p in paths)
                    {
                        if (Directory.Exists(p) && _locationsService.AddCustom(p))
                            any = true;
                    }
                    if (any)
                        RefreshLocationList();
                }
            };

            Closed += OnClosedWindow;
            Loaded += OnLoadedWindow;
        }

        private void OnLoadedWindow(object sender, RoutedEventArgs e)
        {
            RefreshLocationList();
        }

        private void OnClosedWindow(object? sender, EventArgs e)
        {
            StopService();
            _qrGenerator.Dispose();
        }

        // ═══ 位置列表 ═══

        private void RefreshLocationList()
        {
            var locations = _locationsService.GetAllLocations();
            // 默认位置显示"默认位置"文本；自定义位置显示末层文件夹名；ToolTip 均为完整路径
            var items = new List<LocationItem>();
            for (int i = 0; i < locations.Count; i++)
                items.Add(new LocationItem { FullPath = locations[i], IsDefault = i == 0 });

            string current = _locationsService.CurrentLocation;
            LocationListBox.ItemsSource = null;
            LocationListBox.ItemsSource = items;

            int idx = items.FindIndex(p => string.Equals(p.FullPath, current, StringComparison.OrdinalIgnoreCase));
            LocationListBox.SelectedIndex = idx >= 0 ? idx : 0;
        }

        private void LocationListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // 位置变更立即持久化当前选中（启动服务用的是"当前选中位置"）
            if (LocationListBox.SelectedItem is LocationItem item && Directory.Exists(item.FullPath))
                _locationsService.SetCurrent(item.FullPath);
        }

        private void OnAddLocationClick(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "选择保存位置（照片将按日期归档到此文件夹）" };
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.FolderName))
            {
                bool added = _locationsService.AddCustom(dlg.FolderName);
                if (!added)
                {
                    _locationsService.SetCurrent(dlg.FolderName);
                }
                RefreshLocationList();
            }
        }

        // ═══ 位置列表右键菜单（WPF-UI 样式 DropdownMenu） ═══

        /// <summary>位置项右键：打开文件夹 / 移出列表（默认位置禁用移出）</summary>
        private void LocationListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var container = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (container?.DataContext is not LocationItem loc) return;

            // 屏幕物理像素 → DIP：与主窗口其他右键菜单一致（2026-08-16 修复：
            // 原 PointToScreen 直接传物理像素，Popup 偏移按 DIP 解释，非 100% 缩放屏上位置偏右下）
            var mouseScreenPoint = System.Windows.Forms.Cursor.Position;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null) return;
            var wpfPoint = source.CompositionTarget.TransformFromDevice.Transform(new Point(mouseScreenPoint.X, mouseScreenPoint.Y));

            var menu = new DropdownMenu();
            menu.AddItem("打开文件夹", () => OpenFolder(loc.FullPath));
            menu.AddSeparator();
            var removeBtn = menu.AddItem("移出列表", () =>
            {
                if (_locationsService.RemoveCustom(loc.FullPath))
                    RefreshLocationList();
            });
            // 默认位置不可移出（保留默认图片位置作为兜底）
            removeBtn.IsEnabled = !loc.IsDefault;

            menu.ShowAt(wpfPoint);
            e.Handled = true;
        }

        private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
        {
            while (node != null)
            {
                if (node is T match) return match;
                node = VisualTreeHelper.GetParent(node);
            }
            return null;
        }

        /// <summary>用系统资源管理器打开文件夹</summary>
        private static void OpenFolder(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "打开文件夹失败：{Path}", path);
            }
        }

        // ═══ 服务 ═══

        private void OnStartClick(object sender, RoutedEventArgs e)
        {
            if (_service?.IsRunning == true) return;

            string rootFolder = _locationsService.CurrentLocation;
            if (string.IsNullOrEmpty(rootFolder) || !Directory.Exists(rootFolder))
            {
                System.Windows.MessageBox.Show("请先选择有效的保存位置。", "提示",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 首次使用引导：检测防火墙放行（程序级阻止规则/放行规则），缺失则弹引导窗口。
                // 返回 false 不阻断：手机首次连接时 Windows 会弹「是否允许访问网络」窗口，点允许即放行。
                if (!LanFirewallService.EnsureFirewallAccess(this))
                    ToastService.Show("未自动放行：手机首次连接时，Windows 会弹出\n" +
                        "『是否允许访问网络』窗口，请务必点【允许访问】。\n" +
                        "若点【取消】会生成阻止规则导致无法连接，\n" +
                        "可停止服务重新启动后选『立即解除阻止』。", this, 3000);

                _service = new PhotoTransferService();
                _service.PhotoReceived += OnPhotoReceived;
                _deviceTracker.Reset();
                DeviceCountText.Text = "0";
                _service.DeviceConnected += OnDeviceConnected;
                _service.DeviceLeft += OnDeviceLeft;

                // 设备在线兜底扫描：正常情况下手机页面关闭会经 /bye 即时移除；
                // 心跳超时仅兜底 beacon 失败/浏览器被强杀等场景（后台节流误判风险已由 30s 兜底缓解）
                _deviceSweepTimer = new System.Windows.Threading.DispatcherTimer { Interval = DeviceSweepInterval };
                _deviceSweepTimer.Tick += (s, e) => SweepDisconnectedDevices();
                _deviceSweepTimer.Start();

                // 自动寻找可用端口：从 LanPorts.Start 递增尝试（共用 LanPortProbe，捕获含 SocketException）
                int port = LanPortProbe.FindAvailable(LanPorts.Start, p => _service!.Start(p, rootFolder));
                if (port < 0)
                {
                    System.Windows.MessageBox.Show($"无法找到可用端口（{LanPorts.Start}-{LanPorts.ProbeEnd} 均被占用）", "错误",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    StopService();
                    return;
                }
                Log.Information("PhotoTransferWindow: 端口探测成功：{Port}（绑定 IP={Ip}）", port, _service.BoundIpAddress);

                StatusIndicator.Fill = Brushes.LimeGreen;
                StatusText.Text = "服务运行中";
                // 双按钮切换：运行中显示"停止服务"
                StartButton.Visibility = Visibility.Collapsed;
                StopButton.Visibility = Visibility.Visible;
                PortText.Text = $"{_service.BoundIpAddress}:{_service.Port}（保存到：{rootFolder}）";
                // 运行中锁定保存位置（避免一次接收分散到多个目录），停止后恢复
                LocationListBox.IsEnabled = false;
                AddLocationBtn.IsEnabled = false;
                // 水印互斥：运行中显示"停止后可切换位置"，隐藏拖放提示
                RunningHint.Visibility = Visibility.Visible;
                DragHint.Visibility = Visibility.Collapsed;

                GenerateQRCode();

                Log.Information("PhotoTransferWindow: 服务已启动，目标={Root}", rootFolder);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PhotoTransferWindow: 启动服务失败");
                System.Windows.MessageBox.Show($"启动服务失败：{ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                StopService();
            }
        }

        private void OnStopClick(object sender, RoutedEventArgs e) => StopService();

        private void StopService()
        {
            try
            {
                if (_service != null)
                {
                    _service.PhotoReceived -= OnPhotoReceived;
                    _service.DeviceConnected -= OnDeviceConnected;
                    _service.DeviceLeft -= OnDeviceLeft;
                    _service.Stop();
                    _service.Dispose();
                    _service = null;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "PhotoTransferWindow: 停止服务时出现异常");
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

                StatusIndicator.Fill = Brushes.Gray;
                StatusText.Text = "服务未启动";
                StartButton.Visibility = Visibility.Visible;
                StopButton.Visibility = Visibility.Collapsed;
                PortText.Text = "";
                QRCodeImage.Visibility = Visibility.Collapsed;
                QRPlaceholder.Visibility = Visibility.Visible;
                // 恢复位置列表可操作
                LocationListBox.IsEnabled = true;
                AddLocationBtn.IsEnabled = true;
                // 水印互斥：停止后隐藏运行中提示，恢复拖放提示
                RunningHint.Visibility = Visibility.Collapsed;
                DragHint.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// 端口探测已抽到 <see cref="LanPortProbe.FindAvailable"/>（三窗口共用，见 Services/LanPortProbe.cs）：
        /// 从 LanPorts.Start 递增尝试启动，捕获 Kestrel 绑定失败（含 Windows 保留端口的 SocketException），
        /// 范围与 LanPorts.ProbeEnd 对齐（不再硬编码 +100），防火墙放行范围覆盖整个探测范围。
        /// </summary>

        private void GenerateQRCode()
        {
            string boundIp = _service?.BoundIpAddress ?? PhotoTransferService.GetLocalIPAddress();
            int port = _service?.Port ?? LanPorts.Start;
            string url = $"http://{boundIp}:{port}/?token={_service?.Token}";

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
        }

        /// <summary>收到图片：加入文件列表（序号重排、空态隐藏）、更新计数</summary>
        private void OnPhotoReceived(DateTime shootTime, string fileName, long sizeBytes, string fullPath)
        {
            Dispatcher.Invoke(() =>
            {
                FileItems.Add(new PhotoTransferItem
                {
                    ShootTime = shootTime,
                    FileName = fileName,
                    SizeBytes = sizeBytes,
                    FullPath = fullPath
                });
                // 序号重排（1 起）
                for (int i = 0; i < FileItems.Count; i++)
                    FileItems[i].Index = i + 1;
                CountText.Text = FileItems.Count.ToString();
                EmptyHint.Visibility = Visibility.Collapsed;
            });
        }

        // ═══ 传输列表操作（不依赖服务状态：双击打开 / 右键菜单） ═══

        /// <summary>双击文件行：用系统默认查看器打开图片</summary>
        private void FileListGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FileListGrid.SelectedItem is PhotoTransferItem item && !string.IsNullOrEmpty(item.FullPath))
                OpenWithDefaultApp(item.FullPath);
        }

        /// <summary>文件行右键：打开文件位置 / 查看图片属性（系统属性弹窗）</summary>
        private void FileListGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row?.DataContext is not PhotoTransferItem item || string.IsNullOrEmpty(item.FullPath)) return;

            // 屏幕物理像素 → DIP：与主窗口其他右键菜单一致（2026-08-16 修复，原因同位置列表菜单）
            var mouseScreenPoint = System.Windows.Forms.Cursor.Position;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null) return;
            var wpfPoint = source.CompositionTarget.TransformFromDevice.Transform(new Point(mouseScreenPoint.X, mouseScreenPoint.Y));

            var menu = new DropdownMenu();
            menu.AddItem("打开文件位置", () => OpenInExplorer(item.FullPath));

            menu.ShowAt(wpfPoint);
            e.Handled = true;
        }

        /// <summary>用系统默认关联程序打开文件（图片）</summary>
        private static void OpenWithDefaultApp(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "打开图片失败：{Path}", path);
            }
        }

        /// <summary>资源管理器中定位到文件（explorer /select）</summary>
        private static void OpenInExplorer(string path)
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "打开文件位置失败：{Path}", path);
            }
        }

        /// <summary>已连接设备跟踪（三窗口共用，见 Services/LanDeviceTracker.cs；心跳 30s 兜底，正常关页经 /bye 即时移除）</summary>
        private readonly LanDeviceTracker _deviceTracker = new(TimeSpan.FromSeconds(30));

        private static readonly TimeSpan DeviceSweepInterval = TimeSpan.FromSeconds(5);
        private System.Windows.Threading.DispatcherTimer? _deviceSweepTimer;

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
