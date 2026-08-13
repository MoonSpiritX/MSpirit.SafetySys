// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using MS_SafetyManagementSystem.Services;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Views
{
    public partial class AddDateDialog : FluentWindow
    {
        public AddDateDialog(ProjectDataService dataService, string parentGroupId, string parentGroupName)
        {
            InitializeComponent();
            var vm = new AddDateDialogViewModel(dataService, parentGroupId, parentGroupName);
            vm.RequestClose += (result) =>
            {
                DialogResult = result;
                Close();
            };
            DataContext = vm;
        }

        public event Action<string, string, string, string?>? GroupCreated
        {
            add => ((AddDateDialogViewModel)DataContext).OnGroupCreated += value;
            remove => ((AddDateDialogViewModel)DataContext).OnGroupCreated -= value;
        }
    }
}