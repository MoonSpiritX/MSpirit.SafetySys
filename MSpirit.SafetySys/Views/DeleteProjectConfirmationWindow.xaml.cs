// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Input;

namespace MS_SafetyManagementSystem.Views
{
    public partial class DeleteProjectConfirmationWindow : Wpf.Ui.Controls.FluentWindow
    {
        public DeleteProjectConfirmationWindow(string projectName)
        {
            InitializeComponent();
            DataContext = new DeleteProjectConfirmationViewModel(projectName);
        }
    }
}