// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MS_SafetyManagementSystem.Hazard
{
    public partial class RecordCardView : UserControl
    {
        #region 自动保存（TextBox 防抖定时器）

        private Dictionary<TextBox, DispatcherTimer> _autoSaveTimers = new Dictionary<TextBox, DispatcherTimer>();
        private const int AutoSaveDelayMilliseconds = 500;
        private readonly List<string> _autoSaveTextBoxNames = new List<string>
        {
            "ResponsibleUnitTextBox",
            "ResponsiblePersonTextBox",
            "LocationTextBox",
            "ReviewerTextBox",
            "DescriptionTextBox",
            "MeasureTextBox"
        };

        private void AttachAutoSaveTextChangedEvents()
        {
            foreach (string name in _autoSaveTextBoxNames)
            {
                var textBox = this.FindName(name) as TextBox;
                if (textBox != null)
                {
                    textBox.TextChanged += OnAutoSaveTextChanged;
                }
            }
        }

        private void OnAutoSaveTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                if (!_autoSaveTimers.TryGetValue(textBox, out var timer))
                {
                    timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AutoSaveDelayMilliseconds) };
                    timer.Tick += (s, args) =>
                    {
                        timer.Stop();
                        SaveRecord();
                    };
                    _autoSaveTimers[textBox] = timer;
                }
                timer.Stop();
                timer.Start();
            }
        }

        private void CleanupAutoSaveTimers()
        {
            foreach (var timer in _autoSaveTimers.Values)
            {
                timer.Stop();
            }
            _autoSaveTimers.Clear();
            foreach (string name in _autoSaveTextBoxNames)
            {
                var textBox = this.FindName(name) as TextBox;
                if (textBox != null)
                {
                    textBox.TextChanged -= OnAutoSaveTextChanged;
                }
            }
        }

        #endregion
    }
}
