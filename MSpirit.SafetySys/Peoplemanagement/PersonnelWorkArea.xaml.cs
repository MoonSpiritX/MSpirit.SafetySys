// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.ViewModels;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MS_SafetyManagementSystem.Peoplemanagement
{
    public partial class PersonnelWorkArea : UserControl
    {
        private PersonnelRecordsView? _recordsView;
        public string CurrentView { get; private set; } = "records";

        public PersonnelWorkArea()
        {
            InitializeComponent();
        }

        public void Initialize()
        {
            _recordsView = new PersonnelRecordsView();
            _recordsView.SetBinding(PersonnelRecordsView.RecordsProperty,
                new Binding("PersonnelRecords.Records") { Source = Application.Current.MainWindow?.DataContext });
            // 不在此处显示记录卡视图——由 RestoreViewState/SelectedItemChanged 决定显示内容
            Content = null;
        }

        /// <summary>
        /// 清空当前显示内容，用于切换视图前避免闪烁
        /// </summary>
        public void ClearContent()
        {
            Content = null;
        }

        public void ReloadRecordsView()
        {
            if (_recordsView == null)
            {
                Initialize();
            }
            Content = _recordsView;
            CurrentView = "records";
        }

        /// <summary>
        /// 显示单位人员统计数据表
        /// </summary>
        public void ShowPersonnelStatistics(PersonnelStatisticsView statsView)
        {
            Content = statsView;
            CurrentView = "stats";
        }
    }
}
