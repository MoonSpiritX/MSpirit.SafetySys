// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Peoplemanagement;
using System;
using System.Windows;
using System.Windows.Controls;

namespace MS_SafetyManagementSystem
{
    /// <summary>
    /// MainWindow 分部类：内容面板管理（统计大屏 / 隐患模块 / 人员模块的底板切换）与覆盖层（分析/导出）显隐。
    /// 大屏计划：将来新增「统计大屏」面板时，在此处新增 ShowDashboardPanel 并复用 SetBasePanel 机制。
    /// 2026-08-13 从 MainWindow.xaml.cs 拆出，纯搬移、逻辑不变。
    /// </summary>
    public partial class MainWindow
    {
        private Hazard.HazardPanel? _currentHazardPanel;
        private PersonnelPanel? _currentPersonnelPanel;
        private Action? _currentPersonnelStatsFilterHandler; // 统计视图过滤变更订阅

        private void ShowHazardPanel()
        {
            _currentPersonnelPanel = null;
            var panel = new Hazard.HazardPanel();
            _currentHazardPanel = panel;
            panel.SelectedNodeChanged += GroupTreeView_SelectedItemChanged;
            panel.NodeRightButtonDown += GroupTreeView_PreviewMouseRightButtonDown;
            SetBasePanel(panel);

            // 每次创建 HazardPanel 时初始化 WorkArea
            var dataService = _viewModel?.GetProjectDataService();
            if (dataService != null && _viewModel != null)
                panel.Initialize(dataService, _viewModel.CurrentProjectName, _viewModel.CurrentModuleName);
        }

        private void ShowPersonnelPanel()
        {
            _currentHazardPanel = null;
            var panel = new PersonnelPanel();
            _currentPersonnelPanel = panel;
            panel.SelectedNodeChanged += GroupTreeView_SelectedItemChanged;
            panel.NodeRightButtonDown += GroupTreeView_PreviewMouseRightButtonDown;
            SetBasePanel(panel);

            // 初始化 WorkArea
            var dataService = _viewModel?.GetProjectDataService();
            if (dataService != null && _viewModel != null)
                panel.Initialize(dataService, _viewModel.CurrentProjectName, _viewModel.CurrentModuleName);
        }

        /// <summary>
        /// 设置底板内容（模块面板，如 Hazard/Panel；大屏计划下将增加 Dashboard 面板）
        /// </summary>
        private void SetBasePanel(System.Windows.Controls.UserControl? view)
        {
            // 清理旧底板事件
            if (BasePanel.Content is Hazard.HazardPanel oldHazardPanel)
            {
                oldHazardPanel.SelectedNodeChanged -= GroupTreeView_SelectedItemChanged;
                oldHazardPanel.NodeRightButtonDown -= GroupTreeView_PreviewMouseRightButtonDown;
            }
            if (BasePanel.Content is PersonnelPanel oldPersonnelPanel)
            {
                oldPersonnelPanel.SelectedNodeChanged -= GroupTreeView_SelectedItemChanged;
                oldPersonnelPanel.NodeRightButtonDown -= GroupTreeView_PreviewMouseRightButtonDown;
            }

            BasePanel.Content = view;
        }

        /// <summary>
        /// 在覆盖层显示视图（分析 / 导出），供外部控件调用
        /// </summary>
        public void ShowOverlay(System.Windows.Controls.UserControl? view)
        {
            // 清理旧覆盖层事件
            if (OverlayPanel.Content is Analysis.AnalysisControl oldAnalysis)
                oldAnalysis.ExitRequested -= OnAnalysisExit;
            if (OverlayPanel.Content is Export.ExportControl oldExport)
                oldExport.CloseRequested -= OnExportClosed;

            // 绑定新覆盖层事件
            if (view is Analysis.AnalysisControl analysis)
                analysis.ExitRequested += OnAnalysisExit;
            if (view is Export.ExportControl export)
                export.CloseRequested += OnExportClosed;

            OverlayPanel.Content = view;
            OverlayBackground.Visibility = view != null ? Visibility.Visible : Visibility.Collapsed;
            BasePanel.Visibility = view != null ? Visibility.Hidden : Visibility.Visible;
        }

        /// <summary>
        /// 隐藏覆盖层，透出底板
        /// </summary>
        private void HideOverlay()
        {
            // 清理旧覆盖层事件
            if (OverlayPanel.Content is Analysis.AnalysisControl oldAnalysis)
                oldAnalysis.ExitRequested -= OnAnalysisExit;
            if (OverlayPanel.Content is Export.ExportControl oldExport)
                oldExport.CloseRequested -= OnExportClosed;

            OverlayPanel.Content = null;
            OverlayBackground.Visibility = Visibility.Collapsed;
            BasePanel.Visibility = Visibility.Visible;
        }

        public void UpdateStatusText()
        {
            _currentHazardPanel?.UpdateStatusText();
            _currentPersonnelPanel?.UpdateStatusText();
        }
    }
}
