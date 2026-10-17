// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MS_SafetyManagementSystem.ViewModels
{
    public class LevelMarginConverter : IValueConverter
    {
        public double IndentSize { get; set; } = 0.0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 如果 ParentId 不为空，说明是子节点，需要缩进
            if (value is string parentId && !string.IsNullOrEmpty(parentId))
                return new Thickness(IndentSize, 0, 0, 0);
            return new Thickness(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}