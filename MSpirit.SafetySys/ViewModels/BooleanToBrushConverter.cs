// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MS_SafetyManagementSystem.ViewModels
{
    public class BooleanToBrushConverter : IValueConverter
    {
        // 选中时的背景色（可自行调整）
        public Brush TrueBrush { get; set; } = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF89B1F3"));
        // 未选中时的背景色
        public Brush FalseBrush { get; set; } = Brushes.Transparent;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSelected && isSelected)
                return TrueBrush;
            return FalseBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}