// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Controls;
using MS_SafetyManagementSystem.Export;
using MS_SafetyManagementSystem.Hazard;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Peoplemanagement;
using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.ViewModels;
using MS_SafetyManagementSystem.Views;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using Image = System.Windows.Controls.Image;
using DataGridCell = System.Windows.Controls.DataGridCell;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;
using TreeViewItem = System.Windows.Controls.TreeViewItem;
using WinForms = System.Windows.Forms;
using InputMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfListView = System.Windows.Controls.ListView;
using WpfTreeView = System.Windows.Controls.TreeView;
using WpfRichTextBox = System.Windows.Controls.RichTextBox;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;

namespace MS_SafetyManagementSystem
{
    /// <summary>
    /// 主窗口：导航宿主（统计大屏 / 隐患模块 / 人员模块的切换框架）。
    /// 本文件保留核心与生命周期；其余职责按类别拆分到 MainWindow.*.cs 分部文件：
    /// Panels（面板/覆盖层）、MenuActions（菜单命令）、Popups（右键菜单）、Tray（托盘）、
    /// TreeView（分组树/统计）、Navigation（记录定位）、OverlayActions（导出/分析入口）、Drag（窗口拖拽）。
    /// 2026-08-13 拆分，纯搬移、逻辑不变。
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        private readonly SettingsService _settingsService;
        private readonly ConfigManager _configManager;
        private readonly MainWindowViewModel _viewModel;
        private readonly TrayMenuPopupService _trayMenuService;
        public MainWindowViewModel ViewModel => _viewModel;
        private bool _isRefreshingDisplay = false;
        private NotifyIcon? _notifyIcon;
        private CommonHazardTypeService? _commonHazardTypeService;
        private FavoriteLinksService? _linksService;

        private const int WM_SHOWMAINWINDOW = 0x8000 + 1;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource;
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SHOWMAINWINDOW)
            {
                Dispatcher.BeginInvoke(new Action(() => ShowWindow()));
                handled = true;
            }
            return IntPtr.Zero;
        }

        public MainWindow(SettingsService settingsService)
        {
            _settingsService = settingsService;
            var dataPathService = new DataPathService();
            _configManager = new ConfigManager(dataPathService.RootPath);
            _commonHazardTypeService = new CommonHazardTypeService(dataPathService.RootPath);
            AppServiceProvider.CommonHazardTypeService = _commonHazardTypeService;
            _viewModel = new MainWindowViewModel(_configManager, _settingsService, dataPathService.RootPath);
            _viewModel.Records.RecordAdded += OnRecordAdded;
            _trayMenuService = new TrayMenuPopupService();
            DataContext = _viewModel;

            InitializeComponent();

            // 恢复窗口规格
            RestoreWindowBounds();

            // 窗口关闭改为隐藏到托盘
            Closing += MainWindow_Closing;

            // 注册退出清理钩子（AppExitManager 统一调度）：保存所有记录（清焦点提交绑定）+ 保存视图状态
            // 新模块需要退出清理（停止服务、落盘数据等）时在此或模块启动处追加 RegisterCleanup，无需改动退出入口
            AppExitManager.RegisterCleanup(() =>
            {
                ForceClearFocusAndSave();
                _viewModel.SaveCurrentViewState();
            });
            // 退出时关闭所有 LAN 录入窗口（窗口 Closed 事件内停止各自服务，避免服务残留占用端口/数据服务）
            AppExitManager.RegisterCleanup(CloseAllLanEntryWindows);

            // 连接 MenuBar 事件
            MainMenuBar.NewProjectClicked += () => OnNewProject_Click(null!, null!);
            MainMenuBar.ManageProjectClicked += () => OnManageProject_Click(null!, null!);
            MainMenuBar.ModuleSafetyClicked += () => OnModuleSafety_Click(null!, null!);
            MainMenuBar.ModulePersonnelClicked += () => OnModulePersonnel_Click(null!, null!);
            MainMenuBar.InternalViewerStartModeClicked += InternalViewerStartMode_Click;
            MainMenuBar.SystemViewerClicked += SystemViewer_Click;
            MainMenuBar.ManageCommonHazardTypesClicked += () => OnManageCommonHazardTypes_Click(null!, null!);
            MainMenuBar.ManageLinksClicked += () => OnManageLinks_Click(null!, null!);
            MainMenuBar.ImageProcessClicked += () => OnImageProcess_Click(null!, null!);
            MainMenuBar.UsageGuideClicked += () => OnUsageGuide_Click(null!, null!);
            MainMenuBar.AboutClicked += () => OnAbout_Click(null!, null!);
            MainMenuBar.CloseProgramClicked += OnCloseProgramMenu;
            MainMenuBar.PhotoTransferClicked += () => OpenPhotoTransferWindow(owned: true);
            MainMenuBar.ProjectSwitchRequested += (projectId) => SwitchToProject(projectId);

            // 同步菜单栏状态（模块切换 + 图片查看器）
            MainMenuBar.IsSafetyModuleActive = (_settingsService.GetCurrentModule() == ModuleNames.Hazard);
            MainMenuBar.SetViewerType(_settingsService.GetImageViewerType());
            MainMenuBar.SetViewerStartMode(_settingsService.GetImageViewerStartMode());
            RefreshProjectMenu();

            // 初始化链接菜单
            _linksService = new FavoriteLinksService(dataPathService.RootPath);
            MainMenuBar.RefreshLinkMenus(_linksService.GetAllCategories());

            // 启动时根据上次保存的模块加载对应面板
            if (_settingsService.GetCurrentModule() == ModuleNames.Personnel)
                ShowPersonnelPanel();
            else
                ShowHazardPanel();

            try
            {
                if (_viewModel.InitializeFirstRun())
                {
                    _viewModel.RefreshCurrentProject();
                    UpdateStatusText();
                    _viewModel.GroupTree.LoadGroups();
                    if (!string.IsNullOrEmpty(_viewModel.FirstRunDateGroupId))
                    {
                        var targetNode = _viewModel.GroupTree.FindNodeById(_viewModel.FirstRunDateGroupId);
                        if (targetNode != null)
                            _viewModel.GroupTree.SelectedNode = targetNode;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "首次初始化失败");
                MessageDialog.Show($"初始化示例数据失败：{ex.Message}\n您仍可手动创建项目。", "提示");
            }

            _notifyIcon = new NotifyIcon();
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "斜月安全管理系统.ico");
            if (System.IO.File.Exists(iconPath))
                _notifyIcon.Icon = new System.Drawing.Icon(iconPath);
            else
                _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
            _notifyIcon.Text = "斜月安全管理系统";
            _notifyIcon.Visible = true;

            // 注册快捷键 Ctrl+Alt+DEL → 打开非标准记录扫描窗口
            this.KeyDown += OnMainWindowKeyDown;

            _notifyIcon.DoubleClick += (s, e) =>
            {
                if (WindowState == WindowState.Minimized)
                    ShowWindow();
                else if (IsVisible)
                    Hide();
                else
                    ShowWindow();
            };

            _notifyIcon.MouseClick += OnNotifyIconMouseClick;

            // 界面加载完成后，后台执行启动清理（处理 setting.json 记录的待清理目录 + 空文件夹）
            Loaded += (s, e) =>
            {
                try
                {
                    var dataPathService = new DataPathService();
                    var rootPath = dataPathService.RootPath;
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        // 数据根提示文件幂等检查（存在跳过、缺失创建），冷启动不阻塞
                        dataPathService.EnsureDataRootNoticeFile();
                        StartupCleanupService.Run(rootPath);
                    });
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "启动清理触发失败（忽略）");
                }
            };
        }

        private async void CleanupCacheDirectory()
        {
            await Task.Run(() =>
            {
                var dataPathService = new DataPathService();
                string cacheRoot = Path.Combine(dataPathService.RootPath, "ProjectsCache");
                if (!Directory.Exists(cacheRoot)) return;

                var cutoff = DateTime.Now.AddDays(-10);
                foreach (var file in Directory.GetFiles(cacheRoot, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < cutoff)
                            File.Delete(file);
                    }
                    catch { /* 缓存文件删除失败可忽略 */ }
                }
                foreach (var dir in Directory.GetDirectories(cacheRoot, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(dir).Any())
                            Directory.Delete(dir);
                    }
                    catch { /* 缓存目录删除失败可忽略 */ }
                }
            });
        }

        private void ForceClearFocusAndSave()
        {
            Keyboard.ClearFocus();
            _viewModel.Records.SaveAllRecords();
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// 更新前保存并退出：由 UpdateIpcService（更新器请求）调用。
        /// 保存动作（清焦点提交绑定 → 保存所有记录 → 保存视图状态）已注册为 AppExitManager 退出钩子，
        /// 此处只请求退出；防重入与异常隔离由退出管理器统一负责。
        /// 注意：不能用 Close()（会被 Closing 拦截转为隐藏到托盘），必须走 Shutdown。
        /// </summary>
        public void SaveAndExitForUpdate()
        {
            AppExitManager.RequestExit(ExitReason.UpdateRequest);
        }

        public void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (_isRefreshingDisplay) return;
            _isRefreshingDisplay = true;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    RefreshAcrylicEffect();
                    InvalidateVisual();
                    var oldWidth = Width;
                    Width = oldWidth + 1;
                    Width = oldWidth;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "刷新显示设置时出错");
                }
                finally
                {
                    _isRefreshingDisplay = false;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void RefreshAcrylicEffect()
        {
            var currentBackdrop = WindowBackdropType;
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None;
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            WindowBackdropType = currentBackdrop;
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveWindowBounds();
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _viewModel.SaveCurrentViewState();
            base.OnClosed(e);
            _notifyIcon?.Dispose();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // 关闭窗口时隐藏到托盘而非退出
            e.Cancel = true;
            Hide();
        }

        /// <summary>
        /// 全局快捷键处理
        /// Ctrl+Alt+0 → 打开非标准记录扫描窗口
        /// </summary>
        private void OnMainWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.D0 &&
                System.Windows.Input.Keyboard.Modifiers == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt))
            {
                var scanner = new Views.OrphanRecordScannerWindow();
                scanner.Owner = this;
                scanner.ShowDialog();
                e.Handled = true;
            }
        }

        // ========== 窗口规格保存/恢复 ==========

        /// <summary>保存窗口位置和大小到数据库</summary>
        private void SaveWindowBounds()
        {
            try
            {
                _settingsService.SetWindowBounds(
                    Left, Top, Width, Height,
                    WindowState.ToString());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "保存窗口规格失败");
            }
        }

        /// <summary>从数据库恢复窗口位置和大小</summary>
        private void RestoreWindowBounds()
        {
            try
            {
                var bounds = _settingsService.GetWindowBounds();
                if (bounds == null) return;

                var (left, top, width, height, state) = bounds.Value;

                // 检查窗口是否在可见屏幕范围内
                bool isOnScreen = false;
                foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                {
                    var screenRect = screen.WorkingArea;
                    // 检查窗口中心点是否在某个屏幕内
                    double centerX = left + width / 2;
                    double centerY = top + height / 2;
                    if (centerX >= screenRect.Left && centerX <= screenRect.Right &&
                        centerY >= screenRect.Top && centerY <= screenRect.Bottom)
                    {
                        isOnScreen = true;
                        break;
                    }
                }

                if (!isOnScreen) return; // 窗口在屏幕外，使用默认位置

                Left = left;
                Top = top;
                Width = Math.Max(width, 800);  // 最小宽度（与 XAML MinWidth=800 一致）
                Height = Math.Max(height, 600); // 最小高度（与 XAML MinHeight=600 一致）

                if (Enum.TryParse<WindowState>(state, out var ws))
                    WindowState = ws;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "恢复窗口规格失败");
            }
        }
    }
}
