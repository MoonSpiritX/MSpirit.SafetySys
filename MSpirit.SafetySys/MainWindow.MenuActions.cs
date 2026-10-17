// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Export;
using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.Views;
using Serilog;
using System;
using System.Windows;

namespace MS_SafetyManagementSystem
{
    /// <summary>
    /// MainWindow 分部类：菜单命令（菜单栏/设置菜单各入口，含项目切换与模块切换）。
    /// 2026-08-13 从 MainWindow.xaml.cs 拆出，纯搬移、逻辑不变。
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// 项目切换事件（静态）：供导出/导入等常驻控件订阅，切换项目时重置下拉等界面状态。
        /// 由 SwitchToProject 触发；订阅方需在控件销毁时退订，避免内存泄漏。
        /// </summary>
        public static event Action? ProjectSwitched;

        private void OnNewProject_Click(object sender, RoutedEventArgs e)
        {
            // 正在导出/导入时禁止新建项目（创建成功会切换项目，导致旧数据服务被释放、数据写一半）
            if (PersonnelExportViewModel.AnyBusy || ExportWindowViewModel.AnyBusy)
            {
                ToastService.Show("正在导出/导入数据，请稍候再操作", this);
                return;
            }

            ForceClearFocusAndSave();
            var newProjectWindow = new Views.NewProjectWindow(_configManager);
            newProjectWindow.Owner = this;
            if (newProjectWindow.ShowDialog() == true)
            {
                RefreshProjectMenu();
                _viewModel.RefreshCurrentProject();
                UpdateStatusText();
            }
        }

        private void OnManageCommonHazardTypes_Click(object sender, RoutedEventArgs e)
        {
            ForceClearFocusAndSave();
            if (_commonHazardTypeService == null) return;
            var win = new ManageCommonHazardTypesWindow(_commonHazardTypeService, _settingsService);
            win.Owner = this;
            win.ShowDialog();
        }

        private void OnManageProject_Click(object sender, RoutedEventArgs e)
        {
            // 正在导出/导入时禁止打开项目管理（内部可能切换项目，导致旧数据服务被释放、数据写一半）
            if (PersonnelExportViewModel.AnyBusy || ExportWindowViewModel.AnyBusy)
            {
                ToastService.Show("正在导出/导入数据，请稍候再操作", this);
                return;
            }

            ForceClearFocusAndSave();

            // 关闭所有批量录入窗口（防止切换项目后残存服务崩溃）
            CloseAllLanEntryWindows();

            var manageWindow = new Views.ManageProjectsWindow(_configManager);
            manageWindow.Owner = this;
            string oldProjectId = _configManager.GetCurrentProjectId();
            _viewModel.SaveCurrentViewState();
            manageWindow.ShowDialog();

            ForceClearFocusAndSave();

            string newProjectId = _configManager.GetCurrentProjectId();
            RefreshProjectMenu();

            _viewModel.RefreshCurrentProject();
            UpdateStatusText();

            // 在管理窗口中切换了当前项目 → 主窗口中心 Toast 提示
            if (oldProjectId != newProjectId)
                ShowProjectSwitchToast(newProjectId);
        }

        /// <summary>关闭所有打开的批量录入窗口（人员 + 隐患；窗口 Closed 事件内停止各自 LAN 服务）</summary>
        private static void CloseAllLanEntryWindows()
        {
            for (int i = System.Windows.Application.Current.Windows.Count - 1; i >= 0; i--)
            {
                if (System.Windows.Application.Current.Windows[i] is Controls.LanPersonnelEntryWindow or Controls.LanHazardEntryWindow)
                    System.Windows.Application.Current.Windows[i].Close();
            }
        }

        private void RefreshProjectMenu()
        {
            // 防御：程序退出（Shutdown）过程中若管理窗口被关闭，ShowDialog 返回后此处仍会执行，
            // 此时菜单树可能正在拆除，刷新失败不应弹窗打扰退出流程
            try
            {
                var projects = _configManager.GetProjectList();
                string currentId = _configManager.GetCurrentProjectId();
                MainMenuBar.RefreshProjectList(projects, currentId);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "退出流程中刷新项目菜单失败（忽略）");
            }
        }

        private void OnManageLinks_Click(object sender, RoutedEventArgs e)
        {
            ForceClearFocusAndSave();
            var dataPathService = AppServiceProvider.GetDataPathService();
            var linksService = new FavoriteLinksService(dataPathService.RootPath);
            var window = new ManageLinksWindow(linksService);
            window.Owner = this;
            window.ShowDialog();

            // 关闭后刷新菜单中的链接列表
            _linksService = new FavoriteLinksService(dataPathService.RootPath);
            MainMenuBar.RefreshLinkMenus(_linksService.GetAllCategories());
        }

        private void OnImageProcess_Click(object? sender, RoutedEventArgs? e)
        {
            OpenImageProcessWindow(owned: true);
        }

        /// <summary>
        /// 打开图片处理工具窗口。
        /// owned=true（主菜单入口）：设为 Owner，随主窗口管理；
        /// owned=false（托盘入口）：独立弹出、不设 Owner——主窗口保持隐藏，工具窗口单独显示
        /// （WPF owned 窗口在 owner 隐藏时会跟随隐藏，故托盘场景不能设 Owner）。
        /// </summary>
        private void OpenImageProcessWindow(bool owned)
        {
            ForceClearFocusAndSave();
            var window = new Tools.ImageProcessWindow();
            if (owned)
                window.Owner = this;
            window.Show();
        }

        private void OnUsageGuide_Click(object? sender, RoutedEventArgs? e)
        {
            ForceClearFocusAndSave();
            var window = new Views.UsageGuideWindow();
            window.Owner = this;
            window.ShowDialog();
        }

        private void OnAbout_Click(object? sender, RoutedEventArgs? e)
        {
            ForceClearFocusAndSave();
            var window = new Views.AboutWindow();
            window.Owner = this;
            window.ShowDialog();
        }

        /// <summary>
        /// 设置菜单「关闭程序」：弹出关闭方式选择提示窗口，
        /// 「最小化到托盘」= 隐藏主窗口（等效点窗口 X）；「退出程序」= 走统一退出管理器（保存→停服务→备份→退出）。
        /// </summary>
        private void OnCloseProgramMenu()
        {
            ForceClearFocusAndSave();
            var choice = Views.ExitConfirmWindow.ShowDialog(this);
            if (choice == Views.ExitChoice.MinimizeToTray)
            {
                Hide(); // 等效点窗口 X：隐藏到托盘，程序继续后台运行
            }
            else if (choice == Views.ExitChoice.ExitProgram)
            {
                AppExitManager.RequestExit(ExitReason.TrayMenu);
            }
        }

        /// <summary>
        /// 打开手机图片传输窗口。
        /// owned=true（菜单入口）：设 Owner 随主窗口管理；
        /// owned=false（托盘入口）：独立弹出、不设 Owner——主窗口隐藏时工具窗口单独显示。
        /// </summary>
        private void OpenPhotoTransferWindow(bool owned)
        {
            var window = new Tools.PhotoTransferWindow();
            if (owned)
                window.Owner = this;
            window.Show();
        }

        private void SwitchToProject(string projectId)
        {
            // 正在导出/导入时禁止切换项目（后台写库/读数据期间切换会导致旧数据服务被释放、数据写一半）
            if (PersonnelExportViewModel.AnyBusy || ExportWindowViewModel.AnyBusy)
            {
                ToastService.Show("正在导出/导入数据，请稍候再切换项目", this);
                return;
            }

            ForceClearFocusAndSave();
            _viewModel.SaveCurrentViewState();
            _configManager.SetCurrentProjectId(projectId);
            _viewModel.RefreshCurrentProject();
            UpdateStatusText();
            LoadGroupTree();
            RefreshProjectMenu();
            ShowProjectSwitchToast(projectId);
            // 通知常驻控件（导出/导入等）重置界面状态
            try { ProjectSwitched?.Invoke(); }
            catch (Exception ex) { Log.Warning(ex, "项目切换通知订阅方异常（忽略）"); }
        }

        /// <summary>在主窗口中心弹出 Toast，提示当前项目已切换</summary>
        private void ShowProjectSwitchToast(string projectId)
        {
            try
            {
                string projectName = _configManager.GetProjectById(projectId)?.Name ?? projectId;
                ToastService.Show($"当前项目已切换为：{projectName}", this);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "显示项目切换 Toast 失败（忽略）");
            }
        }

        private void OnModuleSafety_Click(object? sender, RoutedEventArgs? e)
        {
            CloseCurrentOverlay();
            ForceClearFocusAndSave();
            _viewModel.SaveCurrentViewState();
            _settingsService.SetCurrentModule(ModuleNames.Hazard);
            _viewModel.RefreshCurrentModule();
            ShowHazardPanel();
            _viewModel.RefreshCurrentModuleData();
            UpdateStatusText();
        }

        private void OnModulePersonnel_Click(object? sender, RoutedEventArgs? e)
        {
            CloseCurrentOverlay();
            ForceClearFocusAndSave();
            _viewModel.SaveCurrentViewState();
            _settingsService.SetCurrentModule(ModuleNames.Personnel);
            _viewModel.RefreshCurrentModule();
            ShowPersonnelPanel();
            _viewModel.RefreshCurrentModuleData();
            UpdateStatusText();
        }

        private void InternalViewerStartMode_Click(string mode)
        {
            _settingsService.SetImageViewerType(ImageViewerType.Internal);
            _settingsService.SetImageViewerStartMode(mode);
        }

        private void SystemViewer_Click()
        {
            _settingsService.SetImageViewerType(ImageViewerType.System);
        }
    }
}
