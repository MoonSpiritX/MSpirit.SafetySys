// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Controls;

namespace MS_SafetyManagementSystem.Views
{
    public class RightContentTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? RecordCardTemplate { get; set; }
        public DataTemplate? StatisticsTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is string viewType && viewType == "Statistics")
                return StatisticsTemplate!;
            return RecordCardTemplate!;
        }
    }
}