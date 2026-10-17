// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using MS_SafetyManagementSystem.Helpers;

namespace MS_SafetyManagementSystem.Controls
{
    public partial class CustomCalendarPopup : UserControl
    {
        public static readonly DependencyProperty SelectedDateProperty =
            DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime?), typeof(CustomCalendarPopup),
                new PropertyMetadata(null, OnSelectedDateChanged));

        public DateTime? SelectedDate
        {
            get => (DateTime?)GetValue(SelectedDateProperty);
            set => SetValue(SelectedDateProperty, value);
        }

        private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((CustomCalendarPopup)d).UpdateCalendar();
        }

        public event EventHandler<DateTime?>? DateSelected;

        private DateTime _displayMonth = DateTime.Now;
        private readonly List<Button> _dateButtons = new List<Button>();

        private static readonly SolidColorBrush SelectedBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#17c3b2"));
        private static readonly SolidColorBrush TodayBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#19528BD6"));

        public CustomCalendarPopup()
        {
            InitializeComponent();
            PrevMonthButton.Click += (s, e) => ChangeMonth(-1);
            NextMonthButton.Click += (s, e) => ChangeMonth(1);
            GenerateDateButtons();
            UpdateCalendar();
            this.Loaded += (s, e) => SetSeasonalBackground();
            this.Loaded += (s, e) =>
            {
                var firstEnabled = _dateButtons.FirstOrDefault(b => b.IsEnabled);
                firstEnabled?.Focus();
            };
        }

        private void GenerateDateButtons()
        {
            for (int i = 0; i < 42; i++)
            {
                var btn = new Button
                {
                    Style = (Style)FindResource("DateButtonStyle"),
                    Tag = i
                };
                btn.Click += DateButton_Click;
                _dateButtons.Add(btn);
                DateGrid.Children.Add(btn);
            }
        }

        private void ChangeMonth(int delta)
        {
            _displayMonth = _displayMonth.AddMonths(delta);
            UpdateCalendar();
        }

        private void UpdateCalendar()
        {
            MonthYearText.Text = _displayMonth.ToString("yyyy年MM月");

            DateTime firstOfMonth = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
            int daysInMonth = DateTime.DaysInMonth(_displayMonth.Year, _displayMonth.Month);
            int startWeek = ((int)firstOfMonth.DayOfWeek + 6) % 7;

            DateTime prevMonth = firstOfMonth.AddMonths(-1);
            int daysInPrevMonth = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);

            for (int i = 0; i < 42; i++)
            {
                int dayNumber;
                bool isCurrentMonth;
                DateTime actualDate;
                if (i < startWeek)
                {
                    dayNumber = daysInPrevMonth - startWeek + i + 1;
                    isCurrentMonth = false;
                    actualDate = new DateTime(prevMonth.Year, prevMonth.Month, dayNumber);
                }
                else if (i < startWeek + daysInMonth)
                {
                    dayNumber = i - startWeek + 1;
                    isCurrentMonth = true;
                    actualDate = new DateTime(_displayMonth.Year, _displayMonth.Month, dayNumber);
                }
                else
                {
                    dayNumber = i - (startWeek + daysInMonth) + 1;
                    isCurrentMonth = false;
                    actualDate = firstOfMonth.AddMonths(1).AddDays(dayNumber - 1);
                }

                var btn = _dateButtons[i];
                btn.Content = dayNumber.ToString();
                btn.IsEnabled = true;
                btn.Tag = actualDate;

                btn.MouseLeave -= Btn_MouseLeave;
                btn.MouseLeave += Btn_MouseLeave;

                btn.ClearValue(Button.BackgroundProperty);
                btn.ClearValue(Button.BorderBrushProperty);
                btn.ClearValue(Button.BorderThicknessProperty);
                btn.ClearValue(Button.ForegroundProperty);

                // 设置前景色：本月黑色，非本月灰色
                if (isCurrentMonth)
                    btn.Foreground = Brushes.Black;
                else
                    btn.Foreground = Brushes.Gray;

                // 特殊样式：选中日期
                if (SelectedDate.HasValue && actualDate.Date == SelectedDate.Value.Date)
                {
                    SetSelectedStyle(btn);
                }
                else if (actualDate.Date == DateTime.Now.Date && isCurrentMonth)
                {
                    btn.SetCurrentValue(Button.BorderBrushProperty, TodayBorderBrush);
                    btn.SetCurrentValue(Button.BorderThicknessProperty, new Thickness(1));
                }
            }
        }

        private void Btn_MouseLeave(object sender, MouseEventArgs e)
        {
            var btn = (Button)sender;
            if (SelectedDate.HasValue && btn.Tag is DateTime date && date.Date == SelectedDate.Value.Date)
            {
                SetSelectedStyle(btn);
            }
        }

        private void SetSelectedStyle(Button btn)
        {
            btn.SetCurrentValue(Button.BackgroundProperty, SelectedBackgroundBrush);
            btn.SetCurrentValue(Button.ForegroundProperty, Brushes.Black);
        }

        private void DateButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            if (btn.Tag is DateTime date)
            {
                if (date.Year != _displayMonth.Year || date.Month != _displayMonth.Month)
                {
                    _displayMonth = new DateTime(date.Year, date.Month, 1);
                    UpdateCalendar();
                }
                SelectedDate = date;
                DateSelected?.Invoke(this, date);
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedDate = null;
            DateSelected?.Invoke(this, null);
        }

        public void ShowMonth(DateTime date)
        {
            _displayMonth = date;
            UpdateCalendar();
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
                ChangeMonth(-1);
            else
                ChangeMonth(1);
            e.Handled = true;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (SelectedDate == null) return;
            DateTime newDate = SelectedDate.Value;
            switch (e.Key)
            {
                case Key.Left:
                    newDate = newDate.AddDays(-1);
                    break;
                case Key.Right:
                    newDate = newDate.AddDays(1);
                    break;
                case Key.Up:
                    newDate = newDate.AddDays(-7);
                    break;
                case Key.Down:
                    newDate = newDate.AddDays(7);
                    break;
                default:
                    return;
            }

            if (newDate.Year < _displayMonth.Year || (newDate.Year == _displayMonth.Year && newDate.Month < _displayMonth.Month))
            {
                ChangeMonth(-1);
                if (newDate.Year == _displayMonth.Year && newDate.Month == _displayMonth.Month)
                {
                    SelectedDate = newDate;
                    DateSelected?.Invoke(this, newDate);
                }
            }
            else if (newDate.Year > _displayMonth.Year || (newDate.Year == _displayMonth.Year && newDate.Month > _displayMonth.Month))
            {
                ChangeMonth(1);
                if (newDate.Year == _displayMonth.Year && newDate.Month == _displayMonth.Month)
                {
                    SelectedDate = newDate;
                    DateSelected?.Invoke(this, newDate);
                }
            }
            else
            {
                SelectedDate = newDate;
                DateSelected?.Invoke(this, newDate);
            }
            e.Handled = true;
        }

        private void SetSeasonalBackground()
        {
            CalendarBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5AAAAAA"));
        }
    }
}