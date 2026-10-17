// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MS_SafetyManagementSystem.ViewModels
{
    public class ExistsButtonStyleConverter : IValueConverter
    {
        public Style? ExistsStyle { get; set; }
        public Style? NormalStyle { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool exists && exists)
                return ExistsStyle ?? throw new InvalidOperationException("ExistsStyle not set");
            return NormalStyle ?? throw new InvalidOperationException("NormalStyle not set");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}