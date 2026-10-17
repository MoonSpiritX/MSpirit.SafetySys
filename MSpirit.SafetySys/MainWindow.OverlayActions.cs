// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Views;
using System.Windows;

namespace MS_SafetyManagementSystem
{
    /// <summary>
    /// MainWindow 分部类：导出/分析覆盖层入口（菜单命令 + 功能区公开入口 + 覆盖层关闭）。
    /// 2026-08-13 从 MainWindow.xaml.cs 拆出，纯搬移、逻辑不变。
    /// </summary>
    public partial class MainWindow
    {
        private void OnDataExport_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;

            HideOverlay();

            var exportControl = new Export.ExportControl(_viewModel.CurrentProjectName, _viewModel.CurrentModuleName);
            ShowOverlay(exportControl);
        }

        private void OnExportClosed()
        {
            HideOverlay();
        }

        private void OnDataAnalysis_Click()
        {
            var dataService = _viewModel.GetProjectDataService();
            if (dataService == null)
            {
                MessageDialog.Show("请先打开一个项目", "提示");
                return;
            }

            HideOverlay();

            var analysisControl = new Analysis.AnalysisControl(dataService, _viewModel.CurrentProjectName, _viewModel.CurrentModuleName);
            ShowOverlay(analysisControl);
        }

        /// <summary>
        /// 公开入口：供功能区调用，进入数据导出界面（隐患模块使用）
        /// </summary>
        public void ShowDataExport()
        {
            OnDataExport_Click(null!, null!);
        }

        /// <summary>
        /// 公开入口：供功能区调用，进入数据分析界面（隐患模块使用）
        /// </summary>
        public void ShowDataAnalysis()
        {
            OnDataAnalysis_Click();
        }

        /// <summary>
        /// 公开入口：供人员管理功能区调用，进入人员数据导出/导入界面
        /// </summary>
        public void ShowPersonnelDataExport()
        {
            if (_viewModel == null) return;
            HideOverlay();

            var exportControl = new Export.PersonnelExportControl();
            exportControl.CloseRequested += () => HideOverlay();
            ShowOverlay(exportControl);
        }

        /// <summary>
        /// 公开入口：供人员管理功能区调用，触动人员统计数据表（在当前工作区显示）
        /// </summary>
        public async void ShowPersonnelAnalysis()
        {
            if (_viewModel == null) return;

            // 获取当前选中节点的单位（根节点）
            var selectedNode = _viewModel.GroupTree.SelectedNode;
            if (selectedNode == null)
            {
                MessageDialog.Show("请先在分组树中选择一个单位", "提示");
                return;
            }

            // 找到根节点（单位）
            var rootNode = selectedNode;
            while (!string.IsNullOrEmpty(rootNode.ParentId))
            {
                var parent = _viewModel.GroupTree.FindNodeById(rootNode.ParentId);
                if (parent == null) break;
                rootNode = parent;
            }

            // 触发树节点选中事件，显示统计数据表
            var dataService = _viewModel.GetProjectDataService();
            if (dataService == null)
            {
                MessageDialog.Show("请先打开一个项目", "提示");
                return;
            }

            // 隐藏覆盖层（如果存在）
            HideOverlay();

            // 若当前已选中该根节点，SelectedNode 同值赋值不会触发树选中事件，需直接构建统计表
            if (_viewModel.GroupTree.SelectedNode == rootNode)
            {
                _currentPersonnelPanel?.WorkAreaInstance.ClearContent();
                await ShowPersonnelStatisticsAsync(rootNode);
                return;
            }

            // 触发树节点选中事件，显示统计数据表
            _currentPersonnelPanel?.WorkAreaInstance.ClearContent();
            _viewModel.GroupTree.SelectedNode = rootNode;
        }

        private void OnAnalysisExit()
        {
            HideOverlay();
        }

        /// <summary>
        /// 关闭所有覆盖层，恢复底板显示
        /// </summary>
        private void CloseCurrentOverlay()
        {
            HideOverlay();
        }
    }
}
