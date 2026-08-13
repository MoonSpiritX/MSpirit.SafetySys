// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Analysis;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Controls;
using MS_SafetyManagementSystem.Services;
using System.Windows;
using System.Windows.Controls;

namespace MS_SafetyManagementSystem.Hazard
{
    public partial class WorkArea : UserControl
    {
        private RecordsView? _recordsView;
        private AnalysisControl? _analysisControl;
        private DashboardPlaceholder? _dashboardPlaceholder;

        private ProjectDataService? _dataService;
        private string _projectName = "";
        private string _moduleName = "";

        /// <summary>
        /// 当前视图类型
        /// </summary>
        public string CurrentView { get; private set; } = "records";

        /// <summary>
        /// 记录视图实例，供 MainWindow 绑定
        /// </summary>
        public RecordsView? RecordsViewInstance => _recordsView;

        /// <summary>
        /// 退出数据分析时触发
        /// </summary>
        public event Action? AnalysisExited;

        public WorkArea()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 初始化数据服务，必须在视图加载前调用
        /// </summary>
        public void Initialize(ProjectDataService dataService, string projectName, string moduleName)
        {
            _dataService = dataService;
            _projectName = projectName;
            _moduleName = moduleName;

            _recordsView = new RecordsView();
            _recordsView.SetBinding(RecordsView.RecordsProperty,
                new System.Windows.Data.Binding("Records.Records")
                {
                    Source = System.Windows.Application.Current.MainWindow?.DataContext
                });
            // 不在此处显示记录卡视图——由 RestoreViewState/SelectedItemChanged 决定显示内容
            Content = null;
        }

        /// <summary>
        /// 切换视图
        /// </summary>
        public void SwitchTo(string viewName)
        {
            if (viewName == CurrentView) return;

            if (viewName == "records")
            {
                if (_analysisControl != null)
                {
                    _analysisControl.ExitRequested -= OnAnalysisExit;
                    _analysisControl = null;
                }
                Content = _recordsView ?? new RecordsView();
                CurrentView = "records";
            }
            else if (viewName == "analysis" && _dataService != null)
            {
                _analysisControl = new AnalysisControl(_dataService, _projectName, _moduleName);
                _analysisControl.ExitRequested += OnAnalysisExit;
                Content = _analysisControl;
                CurrentView = "analysis";
            }
            else if (viewName == "dashboard")
            {
                if (_analysisControl != null)
                {
                    _analysisControl.ExitRequested -= OnAnalysisExit;
                    _analysisControl = null;
                }
                _dashboardPlaceholder ??= new DashboardPlaceholder();
                Content = _dashboardPlaceholder;
                CurrentView = "dashboard";
            }
        }

        /// <summary>
        /// 显示月度统计数据视图
        /// </summary>
        public void ShowMonthlyStatistics(MonthlyStatisticsView statsView)
        {
            if (_analysisControl != null)
            {
                _analysisControl.ExitRequested -= OnAnalysisExit;
                _analysisControl = null;
            }
            Content = statsView;
            CurrentView = "monthlystats";
        }

        private void OnAnalysisExit()
        {
            SwitchTo("records");
            AnalysisExited?.Invoke();
        }

        /// <summary>
        /// 清空当前显示内容，用于切换视图前避免闪烁
        /// </summary>
        public void ClearContent()
        {
            Content = null;
        }

        /// <summary>
        /// 重新加载记录视图数据（由 MainWindow 调用）
        /// </summary>
        public void ReloadRecordsView()
        {
            if (_recordsView == null)
            {
                Initialize(_dataService!, _projectName, _moduleName);
            }
            Content = _recordsView;
            CurrentView = "records";
        }
    }
}
