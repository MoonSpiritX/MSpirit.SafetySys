// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.ViewModels;

namespace MS_SafetyManagementSystem.Hazard
{
    public partial class RecordsView : UserControl
    {
        public static readonly System.Windows.DependencyProperty RecordsProperty =
            System.Windows.DependencyProperty.Register(nameof(Records), typeof(ObservableCollection<Record>), typeof(RecordsView), new System.Windows.PropertyMetadata(null));

        public ObservableCollection<Record>? Records
        {
            get => (ObservableCollection<Record>?)GetValue(RecordsProperty);
            set => SetValue(RecordsProperty, value);
        }

        private DispatcherTimer? _inertiaTimer;
        private double _inertiaVelocity;

        public RecordsView()
        {
            InitializeComponent();
        }

        private async void RecordsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                double remaining = sv.ExtentHeight - sv.VerticalOffset - sv.ViewportHeight;
                if (remaining < 200 && remaining >= 0)
                {
                    var mainWin = System.Windows.Application.Current.MainWindow as MainWindow;
                    var vm = mainWin?.ViewModel;
                    if (vm?.Records != null)
                    {
                        await vm.Records.LoadNextPageAsync();
                    }
                }
            }
        }

        /// <summary>
        /// Preview 阶段拦截滚轮，绕过 TextBox 焦点抢事件，带惯性效果
        /// </summary>
        private void RecordsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = true;

            const double baseStep = 15.0;

            // 立即滚动
            double delta = -Math.Sign(e.Delta) * baseStep;
            RecordsScrollViewer.ScrollToVerticalOffset(RecordsScrollViewer.VerticalOffset + delta);

            // 设置惯性初速度
            _inertiaVelocity = delta;

            // 启动惯性 Timer
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