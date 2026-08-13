// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MS_SafetyManagementSystem.Services;

namespace MS_SafetyManagementSystem.Views
{
    public partial class RenameProjectViewModel : ObservableObject
    {
        private readonly ConfigManager _configManager;
        private readonly string _projectId;
        private readonly string _oldName;

        [ObservableProperty]
        private string _newName = string.Empty;

        [ObservableProperty]
        private string? _errorMessage;

        public RenameProjectViewModel(ConfigManager configManager, string projectId, string currentName)
        {
            _configManager = configManager;
            _projectId = projectId;
            _oldName = currentName;
            NewName = currentName;   // 初始显示原名称
        }

        [RelayCommand]
        private void Ok()
        {
            if (string.IsNullOrWhiteSpace(NewName))
            {
                ErrorMessage = "项目名称不能为空。";
                return;
            }

            string trimmedName = NewName.Trim();

            // 如果名称未改变，直接成功返回
            if (trimmedName == _oldName)
            {
                CloseWindow(true);
                return;
            }

            if (_configManager.RenameProject(_projectId, trimmedName))
            {
                CloseWindow(true);
            }
            else
            {
                ErrorMessage = "项目名称已存在，请使用其他名称。";
            }
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