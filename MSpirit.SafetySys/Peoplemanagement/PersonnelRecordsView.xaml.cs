// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace MS_SafetyManagementSystem.Peoplemanagement
{
    public partial class PersonnelRecordsView : UserControl
    {
        public static readonly DependencyProperty RecordsProperty =
            DependencyProperty.Register(nameof(Records), typeof(ObservableCollection<PersonnelRecord>), typeof(PersonnelRecordsView), null);

        public ObservableCollection<PersonnelRecord>? Records
        {
            get => (ObservableCollection<PersonnelRecord>?)GetValue(RecordsProperty);
            set => SetValue(RecordsProperty, value);
        }

        private DispatcherTimer? _inertiaTimer;
        private double _inertiaVelocity;

        public PersonnelRecordsView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Preview 阶段拦截滚轮，绕过焦点控件抢事件，带惯性效果
        /// </summary>
        private void RecordsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = true;

            const double baseStep = 15.0;

            double delta = -Math.Sign(e.Delta) * baseStep;
            RecordsScrollViewer.ScrollToVerticalOffset(RecordsScrollViewer.VerticalOffset + delta);

            _inertiaVelocity = delta;

            if (_inertiaTimer == null)
            {
                _inertiaTimer = new DispatcherTimer();
                _inertiaTimer.Interval = TimeSpan.FromMilliseconds(16);
                _inertiaTimer.Tick += OnInertiaTick;
            }
            _inertiaTimer.Start();
        }

        private void OnInertiaTick(object? sender, EventArgs e)
        {
            _inertiaVelocity *= 0.9;

            if (Math.Abs(_inertiaVelocity) < 0.5)
            {
                _inertiaTimer?.Stop();
                return;
            }

            RecordsScrollViewer.ScrollToVerticalOffset(RecordsScrollViewer.VerticalOffset + _inertiaVelocity);
        }
    }
}
