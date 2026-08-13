// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;

namespace MS_SafetyManagementSystem.Views
{
    public partial class DeleteProjectConfirmationViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _projectName = string.Empty;

        public DeleteProjectConfirmationViewModel(string projectName)
        {
            _projectName = projectName;
        }

        [RelayCommand]
        private void Confirm()
        {
            CloseWindow(true);
        }

        [RelayCommand]
        private void Cancel()
        {
            CloseWindow(false);
        }

        private void CloseWindow(bool result)
        {
            var window = Application.Current.Windows.OfType<Window>()
                .FirstOrDefault(w => w.DataContext == this);
            if (window != null)
            {
                window.DialogResult = result;
                window.Close();
            }
        }
    }
}