// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.ViewModels;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Views
{
    public partial class HazardGroupDialog : FluentWindow
    {
        public HazardGroupDialog(ProjectDataService projectDataService, string module)
        {
            InitializeComponent();
            DataContext = new HazardGroupDialogViewModel(projectDataService, module);
            ((HazardGroupDialogViewModel)DataContext).RequestClose += (result) =>
            {
                DialogResult = result;
                Close();
            };
        }

        public string? GroupName => (DataContext as HazardGroupDialogViewModel)?.GroupName;
    }
}