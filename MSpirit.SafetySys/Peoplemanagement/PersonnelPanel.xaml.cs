// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MS_SafetyManagementSystem.Peoplemanagement
{
    public partial class PersonnelPanel : UserControl
    {
        /// <summary>
        /// 左侧分组树选中节点变更事件
        /// </summary>
        public event RoutedPropertyChangedEventHandler<object>? SelectedNodeChanged;

        /// <summary>
        /// 左侧分组树右键点击事件
        /// </summary>
        public event MouseButtonEventHandler? NodeRightButtonDown;

        /// <summary>
        /// 公开左侧 TreeView 引用，供主窗口操作
        /// </summary>
        public TreeView TreeView => TreePanel.TreeView;

        /// <summary>
        /// 公开右侧 WorkArea 引用
        /// </summary>
        public PersonnelWorkArea WorkAreaInstance => WorkArea;

        public PersonnelPanel()
        {
            InitializeComponent();

            // 代理 TreePanel 的事件
            TreePanel.SelectedNodeChanged += (s, e) => SelectedNodeChanged?.Invoke(s, e);
            TreePanel.NodeRightButtonDown += (s, e) => NodeRightButtonDown?.Invoke(s, e);
        }

        public void Initialize(ProjectDataService? dataService, string projectName, string moduleName)
        {
            WorkArea.Initialize();
        }

        public void ReloadRecordsView()
        {
            WorkArea.ReloadRecordsView();
        }

        /// <summary>
        /// 显示单位人员统计数据表
        /// </summary>
        public void ShowPersonnelStatistics(PersonnelStatisticsView statsView)
        {
            WorkArea.ShowPersonnelStatistics(statsView);
        }

        /// <summary>
        /// 直接设置状态栏标题文本
        /// </summary>
        public void SetStatusTextDirect(string text)
        {
            StatusTextBlock.Text = text;
        }

        /// <summary>
        /// 获取分组树的滚动偏移量
        /// </summary>
        public double GetGroupTreeScrollOffset()
        {
            return TreePanel.GetScrollOffset();
        }

        /// <summary>
        /// 设置分组树的滚动偏移量
        /// </summary>
        public void SetGroupTreeScrollOffset(double offset)
        {
            TreePanel.SetScrollOffset(offset);
        }

        /// <summary>
        /// 更新记录显示区的动态标题文本
        /// </summary>
        public void UpdateStatusText()
        {
            // 防御：Application.Current 在 WPF Shutdown 过程中会被置为 null，加 ?. 避免退出时的 NRE
            var mainWindow = Application.Current?.MainWindow as MainWindow;
            var vm = mainWindow?.ViewModel;
            if (vm == null)
            {
                StatusTextBlock.Text = "【记录显示区】";
                return;
            }

            var node = vm.GroupTree.SelectedNode;
            if (node == null)
            {
                StatusTextBlock.Text = "【记录显示区】";
                return;
            }

            if (!string.IsNullOrEmpty(node.ParentId))
            {
                var parentNode = vm.GroupTree.FindNodeById(node.ParentId);
                if (parentNode != null)
                {
                    StatusTextBlock.Text = $"【记录显示区】{parentNode.Name}_{node.Name}_{vm.PersonnelRecords.TotalCount}条记录";
                    return;
                }
            }

            StatusTextBlock.Text = $"【记录显示区】{node.Name}_{vm.PersonnelRecords.TotalCount}条记录";
        }
    }
}
