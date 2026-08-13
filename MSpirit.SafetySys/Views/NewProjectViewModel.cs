// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Linq;
using System.Windows;
using MS_SafetyManagementSystem.Services;

namespace MS_SafetyManagementSystem.Views
{
    public partial class NewProjectViewModel : ObservableObject
    {
        private readonly ConfigManager _configManager;

        [ObservableProperty]
        private string? _projectName;

        [ObservableProperty]
        private string? _errorMessage;

        public NewProjectViewModel(ConfigManager configManager)
        {
            _configManager = configManager;
        }

        [RelayCommand]
        private void Ok()
        {
            if (string.IsNullOrWhiteSpace(ProjectName))
            {
                ErrorMessage = "项目名称不能为空。";
                return;
            }

            if (_configManager.AddProject(ProjectName.Trim()))
            {
                ErrorMessage = null;
                var window = Application.Current.Windows.OfType<Window>()
                    .FirstOrDefault(w => w.DataContext == this);
                if (window != null)
                {
                    window.DialogResult = true;
                    window.Close();
                }
            }
            else
            {
                ErrorMessage = "项目名称已存在，请使用其他名称。";
            }
        }

        [RelayCommand]
        private void Cancel()
        {
            var window = Application.Current.Windows.OfType<Window>()
                .FirstOrDefault(w => w.DataContext == this);
            if (window != null)
            {
                window.DialogResult = false;
                window.Close();
            }
        }
    }
}