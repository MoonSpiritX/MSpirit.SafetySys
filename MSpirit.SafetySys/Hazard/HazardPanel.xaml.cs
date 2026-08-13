// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfListView = System.Windows.Controls.ListView;
using WpfDataGrid = System.Windows.Controls.DataGrid;
using WpfRichTextBox = System.Windows.Controls.RichTextBox;
using MS_SafetyManagementSystem.Controls;
using MS_SafetyManagementSystem.Services;

namespace MS_SafetyManagementSystem.Hazard
{
    public partial class HazardPanel : UserControl
    {
        // ========== 公开事件（委托给内部控件） ==========
        public event RoutedPropertyChangedEventHandler<object>? SelectedNodeChanged
        {
            add => TreePanel.SelectedNodeChanged += value;
            remove => TreePanel.SelectedNodeChanged -= value;
        }

        public event MouseButtonEventHandler? NodeRightButtonDown
        {
            add => TreePanel.NodeRightButtonDown += value;
            remove => TreePanel.NodeRightButtonDown -= value;
        }

        // ========== 公开属性（委托给内部控件） ==========
        public TreeView TreeView => TreePanel.TreeView;
        public Hazard.WorkArea WorkAreaInstance => WorkArea;

        // ========== 公开方法（委托给内部控件） ==========
        public double GetGroupTreeScrollOffset() => TreePanel.GetScrollOffset();
        public void SetGroupTreeScrollOffset(double offset) => TreePanel.SetScrollOffset(offset);
        public void Initialize(ProjectDataService? dataService, string projectName, string moduleName)
        {
            WorkArea.Initialize(dataService, projectName, moduleName);
        }

        public void ReloadRecordsView()
        {
            WorkArea.ReloadRecordsView();
        }

        public void ShowMonthlyStatistics(MonthlyStatisticsView statsView)
        {
            WorkArea.ShowMonthlyStatistics(statsView);
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

            int count = vm.Records.TotalCount;

            if (!string.IsNullOrEmpty(node.ParentId))
            {
                // 日期分组：父级 = yyyy年MM月, 子级 = MM月dd日 或 dd日(含标签)
                var parentNode = vm.GroupTree.FindNodeById(node.ParentId);
                if (parentNode != null)
                {
                    // 从子节点名称中提取日部分（去掉重复的月份前缀），例如 "06月19日" → "19日"
                    string dayPart = node.Name;
                    var dayMatch = System.Text.RegularExpressions.Regex.Match(node.Name, @"\d{1,2}月(\d{1,2}日.*)");
                    if (dayMatch.Success)
                        dayPart = dayMatch.Groups[1].Value;

                    StatusTextBlock.Text = $"【记录显示区】{parentNode.Name}{dayPart}_{count}条记录";
                    return;
                }
            }

            // 根节点（年月分组）或无父级
            StatusTextBlock.Text = string.IsNullOrEmpty(node.Name)
                ? "【记录显示区】"
                : $"【记录显示区】{node.Name}_{count}条记录";
        }

        // ========== 构造函数 ==========
        public HazardPanel()
        {
            InitializeComponent();
        }
    }
}
