// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MS_SafetyManagementSystem.ViewModels
{
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 如果值为 null 或空字符串，则隐藏；否则显示
            if (value == null || (value is string s && string.IsNullOrWhiteSpace(s)))
                return Visibility.Collapsed;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}