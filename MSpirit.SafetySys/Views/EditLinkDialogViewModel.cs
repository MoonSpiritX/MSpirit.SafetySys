// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Security.Policy;
using System.Windows;

namespace MS_SafetyManagementSystem.Views
{
    public partial class EditLinkDialogViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _url = string.Empty;

        [ObservableProperty]
        private string? _errorMessage;

        public EditLinkDialogViewModel(string defaultName = "", string defaultUrl = "")
        {
            Name = defaultName;
            Url = defaultUrl;
        }

        [RelayCommand]
        private void Ok()
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                ErrorMessage = "网站名称不能为空。";
                return;
            }
            if (string.IsNullOrWhiteSpace(Url))
            {
                ErrorMessage = "网址不能为空。";
                return;
            }
            if (!Uri.IsWellFormedUriString(Url, UriKind.Absolute))
            {
                // 简单检查，也可以自动补 https://
                if (!Url.StartsWith("http://") && !Url.StartsWith("https://"))
                {
                    Url = "https://" + Url;
                    if (!Uri.IsWellFormedUriString(Url, UriKind.Absolute))
                    {
                        ErrorMessage = "请输入有效的网址（例如 example.com 或 http://example.com）。";
                        return;
                    }
                }
                else
                {
                    ErrorMessage = "请输入有效的网址。";
                    return;
                }
            }

            var window = Application.Current.Windows.OfType<Window>()
                .FirstOrDefault(w => w.DataContext == this);
            if (window != null)
            {
                window.DialogResult = true;
                window.Close();
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