// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using Microsoft.Win32;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Export;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;

namespace MS_SafetyManagementSystem.Analysis
{
    /// <summary>
    /// 类型统计项
    /// </summary>
    public class TypeStatItem
    {
        public int SequenceNumber { get; set; }
        public string HazardType { get; set; } = "";
        public int Count { get; set; }
        public double Percentage { get; set; }
        public string PercentageText => $"{Percentage:F1}%";
    }

    /// <summary>
    /// 月度统计项
    /// </summary>
    public class MonthlyStatItem
    {
        public string Month { get; set; } = "";       // 如 "2026-05"
        public string MonthDisplay { get; set; } = ""; // 如 "05月"
        public int NewCount { get; set; }
        public int CumulativeCount { get; set; }
    }

    /// <summary>
    /// 用于列表显示的记录行
    /// </summary>
    public class AnalysisRecordRow
    {
        public int DisplayIndex { get; set; }
        public string RecordId { get; set; } = "";
        public string DateName { get; set; } = "";
        public string HazardType { get; set; } = "";
        public string HazardLevel { get; set; } = "";
        public string Description { get; set; } = "";
        public string Measure { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public partial class AnalysisViewModel : ObservableObject
    {
        /// <summary>
        /// 导出操作完成时触发，参数为提示文本
        /// </summary>
        public event Action<string>? ExportMessage;

        private readonly ProjectDataService _dataService;
        private readonly string _moduleKey;
        private List<Record> _allRecords = new();
        private List<Group> _yearMonthGroups = new();  // 所有年月分组

        // ========== 标题 ==========
        [ObservableProperty]
        private string _windowTitle = "数据统计分析";

        // ========== 时间范围（快捷按钮 + 自定义下拉） ==========
        [ObservableProperty]
        private string _customStartText = "起始日期";

        [ObservableProperty]
        private string _customEndText = "结束日期";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsCustomRangeSelectable))]
        [NotifyCanExecuteChangedFor(nameof(ClearCustomRangeCommand))]
        private string _selectedRangeType = "thisMonth";  // thisYear, thisQuarter, thisMonth, prevMonth, custom

        public bool IsCustomRangeSelectable => SelectedRangeType == "custom";

        // 所有年月分组（完整列表）
        public ObservableCollection<YearMonthGroupInfo> YearMonthGroups { get; } = new();

        // ========== 统计卡片 ==========
        [ObservableProperty]
        private int _totalRecords;

        /// <summary>
        /// 有效记录数：已填写隐患类型与级别、可参与类型分布统计的记录数
        /// </summary>
        [ObservableProperty]
        private int _validRecordCount;

        [ObservableProperty]
        private int _hazardTypeCount;

        [ObservableProperty]
        private double _completionRate;

        [ObservableProperty]
        private string _completionRateText = "0%";

        // 动态摘要文本
        [ObservableProperty]
        private string _summaryText = "";

        // ========== 类型分布 ==========
        public ObservableCollection<TypeStatItem> TypeDistribution { get; } = new();

        // ========== 月度趋势 ==========
        public ObservableCollection<MonthlyStatItem> MonthlyTrend { get; } = new();

        // ========== 详细记录列表 ==========
        public ObservableCollection<AnalysisRecordRow> FilteredRecords { get; } = new();

        // ========== 状态 ==========
        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _statusText = "";

        // ========== 图表标签切换 ==========
        [ObservableProperty]
        private int _selectedChartIndex;

        // ========== 图表数据（每种图表类型独立属性，互不干扰） ==========
        // 柱状图
        public ISeries[] BarSeries { get; set; } = Array.Empty<ISeries>();
        public Axis[] BarXAxes { get; set; } = Array.Empty<Axis>();
        public Axis[] BarYAxes { get; set; } = Array.Empty<Axis>();
        // 饼图
        public ISeries[] PieSeries { get; set; } = Array.Empty<ISeries>();
        // 日折线图
        public ISeries[] DailySeries { get; set; } = Array.Empty<ISeries>();
        public Axis[] DailyXAxes { get; set; } = Array.Empty<Axis>();
        public Axis[] DailyYAxes { get; set; } = Array.Empty<Axis>();
        // 月折线图
        public ISeries[] MonthlySeries { get; set; } = Array.Empty<ISeries>();
        public Axis[] MonthlyXAxes { get; set; } = Array.Empty<Axis>();
        public Axis[] MonthlyYAxes { get; set; } = Array.Empty<Axis>();

        // 柱状图标签数量（用于导出宽度自适应）
        private int _barChartLabelCount;

        public AnalysisViewModel(ProjectDataService dataService, string projectName, string moduleName)
        {
            _dataService = dataService;
            _moduleKey = moduleName == "隐患管理模块" ? ModuleNames.Hazard : ModuleNames.Personnel;
            WindowTitle = $"数据统计分析 _ {projectName}_{moduleName}";

            LoadYearMonthGroups();

            // 默认选中本月
            int y = DateTime.Now.Year;
            int m = DateTime.Now.Month;
            CustomStartText = $"{y}年{m}月";
            CustomEndText = $"{y}年{m}月";

            // 自动分析
            AnalyzeData();
        }

        /// <summary>
        /// 加载所有年月分组
        /// </summary>
        public void LoadYearMonthGroups()
        {
            _yearMonthGroups = _dataService.GetGroups(_moduleKey, null)
                .OrderByDescending(g => g.Name)
                .ToList();

            YearMonthGroups.Clear();
            foreach (var ym in _yearMonthGroups)
            {
                var dateGroups = _dataService.GetGroups(_moduleKey, ym.Id)
                    .OrderBy(g => ParseDateValue(g.Name))
                    .ToList();

                var info = new YearMonthGroupInfo
                {
                    Id = ym.Id,
                    Name = ym.Name,
                    DateGroups = dateGroups.Select(dg => new DateGroupInfo
                    {
                        Id = dg.Id,
                        Name = dg.Name,
                        Tag = dg.Tag
                    }).ToList()
                };
                YearMonthGroups.Add(info);
            }
        }

        // ========== 快捷时间选择 ==========

        [RelayCommand]
        private void SelectThisYear()
        {
            int y = DateTime.Now.Year;
            int m = DateTime.Now.Month;
            SetCustomRange($"{y}年01月", $"{y}年{m}月", "thisYear");
        }

        [RelayCommand]
        private void SelectPrevYear()
        {
            int y = DateTime.Now.Year - 1;
            SetCustomRange($"{y}年01月", $"{y}年12月", "prevYear");
        }

        [RelayCommand]
        private void SelectThisQuarter()
        {
            int m = DateTime.Now.Month;
            int qStartMonth = ((m - 1) / 3) * 3 + 1;
            int qEndMonth = qStartMonth + 2;
            int y = DateTime.Now.Year;
            SetCustomRange($"{y}年{qStartMonth}月", $"{y}年{qEndMonth}月", "thisQuarter");
        }

        [RelayCommand]
        private void SelectPrevQuarter()
        {
            int m = DateTime.Now.Month;
            int qStartMonth = ((m - 1) / 3) * 3 + 1;
            var prevQuarterStart = new DateTime(DateTime.Now.Year, qStartMonth, 1).AddMonths(-3);
            var prevQuarterEnd = prevQuarterStart.AddMonths(2);
            SetCustomRange(
                $"{prevQuarterStart.Year}年{prevQuarterStart.Month}月",
                $"{prevQuarterEnd.Year}年{prevQuarterEnd.Month}月",
                "prevQuarter");
        }

        [RelayCommand]
        private void SelectThisMonth()
        {
            int y = DateTime.Now.Year;
            int m = DateTime.Now.Month;
            SetCustomRange($"{y}年{m}月", $"{y}年{m}月", "thisMonth");
        }

        [RelayCommand]
        private void SelectPrevMonth()
        {
            var prev = DateTime.Now.AddMonths(-1);
            SetCustomRange($"{prev.Year}年{prev.Month}月", $"{prev.Year}年{prev.Month}月", "prevMonth");
        }

        /// <summary>
        /// 设置时间范围并分析，无需检查互斥（由按钮保证正确范围）
        /// </summary>
        private void SetCustomRange(string start, string end, string rangeType)
        {
            CustomStartText = start;
            CustomEndText = end;
            SelectedRangeType = rangeType;
            AnalyzeData();
        }

        [RelayCommand]
        private void SelectCustom()
        {
            SelectedRangeType = "custom";
        }

        /// <summary>
        /// 清除时间范围选择
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanClearCustomRange))]
        private void ClearCustomRange()
        {
            CustomStartText = "起始日期";
            CustomEndText = "结束日期";
        }

        private bool CanClearCustomRange() => SelectedRangeType == "custom";

        // ========== 导出 ==========

        /// <summary>
        /// 导出当前选中图表为 PNG 图片
        /// </summary>
        [RelayCommand]
        private void ExportPng()
        {
            var dialog = new SaveFileDialog
            {
                Title = "导出为图片",
                Filter = "PNG 图片|*.png",
                DefaultExt = "png",
                FileName = $"安全数据分析_{SelectedRangeType}_{DateTime.Now:yyyyMMddHHmmss}",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    RenderChartToPng(dialog.FileName);
                    ExportMessage?.Invoke($"图片已导出到：{dialog.FileName}");
                }
                catch (Exception ex)
                {
                    ExportMessage?.Invoke($"导出失败：{ex.Message}");
                }
            }
        }

        private void RenderChartToPng(string filePath)
        {
            byte[] bytes = RenderChartToBytes();
            File.WriteAllBytes(filePath, bytes);
        }

        private byte[] RenderChartToBytes()
        {
            // 柱状图根据标签数量自适应宽度（每个标签至少 80px），其他图表固定 900x560
            int barWidth = Math.Max(900, _barChartLabelCount * 80 + 100);
            int width = SelectedChartIndex switch
            {
                0 => barWidth,
                _ => 900
            };
            int height = 560;

            using var image = SelectedChartIndex switch
            {
                0 => new LiveChartsCore.SkiaSharpView.SKCharts.SKCartesianChart
                {
                    Series = BarSeries,
                    XAxes = BarXAxes,
                    YAxes = BarYAxes,
                    Width = width,
                    Height = height
                }.GetImage(),

                1 => new LiveChartsCore.SkiaSharpView.SKCharts.SKPieChart
                {
                    Series = PieSeries,
                    Width = width,
                    Height = height
                }.GetImage(),

                2 => new LiveChartsCore.SkiaSharpView.SKCharts.SKCartesianChart
                {
                    Series = DailySeries,
                    XAxes = DailyXAxes,
                    YAxes = DailyYAxes,
                    Width = width,
                    Height = height
                }.GetImage(),

                3 => new LiveChartsCore.SkiaSharpView.SKCharts.SKCartesianChart
                {
                    Series = MonthlySeries,
                    XAxes = MonthlyXAxes,
                    YAxes = MonthlyYAxes,
                    Width = width,
                    Height = height
                }.GetImage(),

                _ => throw new InvalidOperationException("未知图表索引")
            };

            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        /// <summary>
        /// 导出数据表为 Word 文档
        /// </summary>
        [RelayCommand]
        private void ExportTable()
        {
            var dialog = new SaveFileDialog
            {
                Title = "导出数据表",
                Filter = "Word 文档|*.docx",
                DefaultExt = "docx",
                FileName = $"安全数据表_{SelectedRangeType}_{DateTime.Now:yyyyMMddHHmmss}",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    new AnalysisWordExporter(
                        title: "安全数据分析报告",
                        summaryText: SummaryText,
                        typeDistribution: TypeDistribution.ToList()
                    ).Export(dialog.FileName);

                    ExportMessage?.Invoke($"数据表已导出到：{dialog.FileName}");
                }
                catch (Exception ex)
                {
                    ExportMessage?.Invoke($"导出失败：{ex.Message}");
                }
            }
        }

        private List<string> GetLineChartLabels()
        {
            if (SelectedChartIndex == 2)
                return GetLabelsFromXAxis(DailyXAxes);
            if (SelectedChartIndex == 3)
                return GetLabelsFromXAxis(MonthlyXAxes);
            return new List<string>();
        }

        private List<double> GetLineChartValues()
        {
            ISeries<double>? series = SelectedChartIndex switch
            {
                2 when DailySeries.Length > 0 => DailySeries[0] as ISeries<double>,
                3 when MonthlySeries.Length > 0 => MonthlySeries[0] as ISeries<double>,
                _ => null
            };
            return series?.Values?.Select(v => (double)v).ToList() ?? new List<double>();
        }

        private static List<string> GetLabelsFromXAxis(Axis[] axes)
        {
            if (axes.Length == 0) return new List<string>();
            // Axis[] stores LiveChartsCore.SkiaSharpView.Painting.Axis which has Labels
            var labelsProp = axes[0].GetType().GetProperty("Labels");
            if (labelsProp?.GetValue(axes[0]) is IEnumerable<string> labels)
                return labels.ToList();
            return new List<string>();
        }

        // ========== 时间范围变更 → 自动分析（自定义下拉模式下互斥检查） ==========

        partial void OnCustomStartTextChanged(string value)
        {
            // 仅自定义模式下检查互斥
            if (SelectedRangeType == "custom" &&
                !string.IsNullOrEmpty(CustomEndText) && CustomEndText != "结束日期" &&
                !string.IsNullOrEmpty(CustomStartText) && CustomStartText != "起始日期")
            {
                var (sy, sm) = ParseYearMonthFromDisplay(CustomStartText);
                var (ey, em) = ParseYearMonthFromDisplay(CustomEndText);
                if (sy > 0 && sm > 0 && ey > 0 && em > 0)
                {
                    DateTime start = new(sy, sm, 1);
                    DateTime end = new(ey, em, 1);
                    if (end < start)
                    {
                        CustomEndText = "结束日期";
                        return;
                    }
                }
            }
            AnalyzeData();
        }

        partial void OnCustomEndTextChanged(string value) => AnalyzeData();

        // ========== 核心数据分析 ==========

        [RelayCommand]
        private void AnalyzeData()
        {
            IsLoading = true;
            StatusText = "正在分析数据...";

            try
            {
                // 先清空旧图表数据，避免模式切换时序导致的残留
                ClearAllCharts();

                _allRecords = CollectRecordsByRange();
                ComputeStatsCards();
                ComputeTypeDistribution();
                ComputeMonthlyTrend();
                FillFilteredRecords();
                BuildAllCharts();  // 构建所有图表类型
                GenerateSummaryText();

                StatusText = $"分析完成，共 {_allRecords.Count} 条记录";
            }
            catch (Exception ex)
            {
                StatusText = $"分析出错：{ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        // ========== 图表数据 ==========

        [RelayCommand]
        private void SelectChart(string indexStr)
        {
            if (int.TryParse(indexStr, out int index) && index >= 0 && index <= 3)
                SelectedChartIndex = index;
        }

        // ========== 清空图表 ==========

        /// <summary>
        /// 清空所有图表数据，避免模式切换时序导致旧数据残留
        /// </summary>
        private void ClearAllCharts()
        {
            BarSeries = Array.Empty<ISeries>();
            BarXAxes = Array.Empty<Axis>();
            BarYAxes = Array.Empty<Axis>();
            PieSeries = Array.Empty<ISeries>();
            DailySeries = Array.Empty<ISeries>();
            DailyXAxes = Array.Empty<Axis>();
            DailyYAxes = Array.Empty<Axis>();
            MonthlySeries = Array.Empty<ISeries>();
            MonthlyXAxes = Array.Empty<Axis>();
            MonthlyYAxes = Array.Empty<Axis>();

            OnPropertyChanged(nameof(BarSeries));
            OnPropertyChanged(nameof(BarXAxes));
            OnPropertyChanged(nameof(BarYAxes));
            OnPropertyChanged(nameof(PieSeries));
            OnPropertyChanged(nameof(DailySeries));
            OnPropertyChanged(nameof(DailyXAxes));
            OnPropertyChanged(nameof(DailyYAxes));
            OnPropertyChanged(nameof(MonthlySeries));
            OnPropertyChanged(nameof(MonthlyXAxes));
            OnPropertyChanged(nameof(MonthlyYAxes));
        }

        // ========== 构建所有图表 ==========

        /// <summary>
        /// 一次构建所有图表类型，每种图表使用独立属性，互不干扰
        /// </summary>
        private void BuildAllCharts()
        {
            BuildBarChart();
            BuildPieChart();
            BuildDailyChart();
            BuildMonthlyChart();
        }

        private void BuildBarChart()
        {
            var items = TypeDistribution.ToList();
            if (items.Count == 0)
            {
                BarSeries = Array.Empty<ISeries>();
                BarXAxes = Array.Empty<Axis>();
                BarYAxes = Array.Empty<Axis>();
                OnPropertyChanged(nameof(BarSeries));
                OnPropertyChanged(nameof(BarXAxes));
                OnPropertyChanged(nameof(BarYAxes));
                return;
            }

            var labels = items.Select(i => i.HazardType).ToArray();
            var values = items.Select(i => (double)i.Count).ToArray();
            _barChartLabelCount = labels.Length;

            BarSeries = new ISeries[]
            {
                new RowSeries<double>
                {
                    Values = values,
                    Fill = new SolidColorPaint(SKColor.Parse("#84C0E9")),
                    Stroke = null,
                    MaxBarWidth = 20,
                    DataLabelsPaint = new SolidColorPaint(SKColors.Black),
                    DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Right,
                    DataLabelsSize = 11,
                }
            };

            // RowSeries：X 轴为数值轴，Y 轴为分类轴
            BarXAxes = new Axis[]
            {
                new Axis { TextSize = 11, MinLimit = 0 }
            };

            BarYAxes = new Axis[]
            {
                new Axis
                {
                    Labels = labels,
                    MinStep = 1,
                    TextSize = 11
                }
            };

            OnPropertyChanged(nameof(BarSeries));
            OnPropertyChanged(nameof(BarXAxes));
            OnPropertyChanged(nameof(BarYAxes));
        }

        private void BuildPieChart()
        {
            var items = TypeDistribution.ToList();
            if (items.Count == 0)
            {
                PieSeries = Array.Empty<ISeries>();
                OnPropertyChanged(nameof(PieSeries));
                return;
            }

            var colors = new[]
            {
                SKColor.Parse("#E2F0FA"),  // 冰蓝     #1
                SKColor.Parse("#D0E6F7"),  // 淡天蓝   #2
                SKColor.Parse("#B8DBF3"),  // 轻蓝     #3
                SKColor.Parse("#A1D0EF"),  // 柔蓝     #4
                SKColor.Parse("#8CC6EA"),  // 和蓝     #5
                SKColor.Parse("#84C0E9"),  // 基准蓝   #6
                SKColor.Parse("#72B3E4"),  // 纯蓝     #7
                SKColor.Parse("#5EA5DE"),  // 中蓝     #8
                SKColor.Parse("#4A97D7"),  // 深蓝     #9
                SKColor.Parse("#3687CE"),  // 深海     #10
                SKColor.Parse("#2377C3"),  // 暗蓝     #11
                SKColor.Parse("#1467B5"),  // 深暗蓝   #12
                SKColor.Parse("#0A58A5"),  // 海军蓝   #13
                SKColor.Parse("#054A93"),  // 暗海军   #14
                SKColor.Parse("#023D80"),  // 最深蓝   #15
            };

            double total = items.Sum(i => i.Count);

            PieSeries = items.Select((item, i) =>
                new PieSeries<double>
                {
                    Values = new double[] { item.Count },
                    Name = $"{item.HazardType}: {item.Count / total * 100:F1}%",
                    Fill = new SolidColorPaint(colors[i % colors.Length]),
                    Stroke = null,
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                    DataLabelsSize = 11,
                    DataLabelsPaint = new SolidColorPaint(SKColors.Black),
                    DataLabelsFormatter = (point) => $"{item.HazardType}: {item.Count / total * 100:F1}% {item.Count}",
                    HoverPushout = 5
                } as ISeries
            ).ToArray();

            OnPropertyChanged(nameof(PieSeries));
        }

        private void BuildDailyChart()
        {
            // 构建 GroupId → 实际日期 的映射（来自分组层级，而非 CreatedAt）
            var dateMap = new Dictionary<string, DateTime>();
            foreach (var ym in YearMonthGroups)
            {
                var (y, m) = ParseYearMonthName(ym.Name);
                if (y == 0) continue;
                foreach (var dg in ym.DateGroups)
                {
                    int day = ParseDayFromDateName(dg.Name);
                    if (day > 0)
                        dateMap[dg.Id] = new DateTime(y, m, day);
                }
            }

            var dailyGroups = _allRecords
                .Where(r => dateMap.ContainsKey(r.GroupId))
                .GroupBy(r => dateMap[r.GroupId])
                .Select(g => new { Date = g.Key, Label = g.Key.ToString("MM/dd"), Count = g.Count() })
                .OrderBy(x => x.Date)
                .ToList();

            if (dailyGroups.Count == 0)
            {
                DailySeries = Array.Empty<ISeries>();
                DailyXAxes = Array.Empty<Axis>();
                DailyYAxes = Array.Empty<Axis>();
                OnPropertyChanged(nameof(DailySeries));
                OnPropertyChanged(nameof(DailyXAxes));
                OnPropertyChanged(nameof(DailyYAxes));
                return;
            }

            var labels = dailyGroups.Select(d => d.Label).ToArray();
            var values = dailyGroups.Select(d => (double)d.Count).ToArray();

            DailySeries = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = values,
                    Fill = new SolidColorPaint(SKColors.DodgerBlue.WithAlpha(30)),
                    Stroke = new SolidColorPaint(SKColors.DodgerBlue, 2),
                    GeometryFill = new SolidColorPaint(SKColors.DodgerBlue),
                    GeometryStroke = new SolidColorPaint(SKColors.White, 2),
                    GeometrySize = 8,
                    DataLabelsPaint = new SolidColorPaint(SKColors.DodgerBlue),
                    DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Top,
                    DataLabelsSize = 10,
                }
            };

            DailyXAxes = new Axis[]
            {
                new Axis { Labels = labels, LabelsRotation = 45, TextSize = 10 }
            };

            DailyYAxes = new Axis[]
            {
                new Axis { TextSize = 11, MinLimit = 0 }
            };

            OnPropertyChanged(nameof(DailySeries));
            OnPropertyChanged(nameof(DailyXAxes));
            OnPropertyChanged(nameof(DailyYAxes));
        }

        private void BuildMonthlyChart()
        {
            var monthlyGroups = _allRecords
                .Where(r => !string.IsNullOrWhiteSpace(r.YearMonth) && r.YearMonth.Length >= 7)
                .GroupBy(r => r.YearMonth!)
                .Select(g =>
                {
                    var parts = g.Key.Split('-');
                    string label = parts.Length == 2 ? $"{parts[0]}年{int.Parse(parts[1])}月" : g.Key;
                    return new { Key = g.Key, Label = label, Count = g.Count() };
                })
                .OrderBy(x => x.Key)
                .ToList();

            if (monthlyGroups.Count == 0)
            {
                MonthlySeries = Array.Empty<ISeries>();
                MonthlyXAxes = Array.Empty<Axis>();
                MonthlyYAxes = Array.Empty<Axis>();
                OnPropertyChanged(nameof(MonthlySeries));
                OnPropertyChanged(nameof(MonthlyXAxes));
                OnPropertyChanged(nameof(MonthlyYAxes));
                return;
            }

            var labels = monthlyGroups.Select(m => m.Label).ToArray();
            var values = monthlyGroups.Select(m => (double)m.Count).ToArray();

            MonthlySeries = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = values,
                    Fill = new SolidColorPaint(SKColors.OrangeRed.WithAlpha(30)),
                    Stroke = new SolidColorPaint(SKColors.OrangeRed, 2),
                    GeometryFill = new SolidColorPaint(SKColors.OrangeRed),
                    GeometryStroke = new SolidColorPaint(SKColors.White, 2),
                    GeometrySize = 8,
                    DataLabelsPaint = new SolidColorPaint(SKColors.OrangeRed),
                    DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Top,
                    DataLabelsSize = 10,
                }
            };

            MonthlyXAxes = new Axis[]
            {
                new Axis { Labels = labels, LabelsRotation = 45, TextSize = 10 }
            };

            MonthlyYAxes = new Axis[]
            {
                new Axis { TextSize = 11, MinLimit = 0 }
            };

            OnPropertyChanged(nameof(MonthlySeries));
            OnPropertyChanged(nameof(MonthlyXAxes));
            OnPropertyChanged(nameof(MonthlyYAxes));
        }

        // ========== 摘要文本生成 ==========

        private void GenerateSummaryText()
        {
            if (TotalRecords == 0)
            {
                SummaryText = "";
                return;
            }

            string timeDesc = SelectedRangeType switch
            {
                "thisYear" => $"{DateTime.Now.Year}年度",
                "prevYear" => $"{DateTime.Now.Year - 1}年度",
                "thisQuarter" => GetQuarterDesc(DateTime.Now, out _),
                "prevQuarter" => GetQuarterDesc(DateTime.Now.AddMonths(-3), out _),
                "thisMonth" => $"{DateTime.Now.Month}月份",
                "prevMonth" => $"{DateTime.Now.AddMonths(-1).Month}月份",
                "custom" or _ => BuildCustomRangeDesc()
            };

            // 前3隐患类型
            var topTypes = TypeDistribution
                .OrderByDescending(t => t.Count)
                .Take(3)
                .Select(t => $"\"{t.HazardType}\"")
                .ToList();

            string typesText = topTypes.Count switch
            {
                0 => "",
                1 => topTypes[0],
                2 => $"{topTypes[0]}、{topTypes[1]}",
                _ => $"{topTypes[0]}、{topTypes[1]}、{topTypes[2]}"
            };

            string suffix = topTypes.Count > 1 ? "等" : "";

            SummaryText = $"{timeDesc}，排查各类安全隐患共{TotalRecords}条，整改完成率{CompletionRateText}，主要隐患类型为{typesText}{suffix}。";
        }

        private static string GetQuarterDesc(DateTime date, out int q)
        {
            q = ((date.Month - 1) / 3) + 1;
            int qStartMonth = (q - 1) * 3 + 1;
            int qEndMonth = qStartMonth + 2;
            return $"{date.Year}年第一季度（{qStartMonth}月-{qEndMonth}月）"
                .Replace("第一季度", $"第{q}季度");
        }

        private string BuildCustomRangeDesc()
        {
            var (sy, sm) = ParseYearMonthFromDisplay(CustomStartText);
            var (ey, em) = ParseYearMonthFromDisplay(CustomEndText);
            if (sy > 0 && sm > 0 && ey > 0 && em > 0)
            {
                if (sy == ey && sm == em)
                    return $"{sy}年{sm}月";
                return $"{sy}年{sm}月至{ey}年{em}月期间";
            }
            return "";
        }

        private List<Record> CollectRecordsByRange()
        {
            // 仅使用自定义时间范围进行数据筛选
            return CollectRecordsByDateRange();
        }

        private List<Record> CollectRecordsByDateRange()
        {
            var (startYear, startMonth) = ParseYearMonthFromDisplay(CustomStartText);
            var (endYear, endMonth) = ParseYearMonthFromDisplay(CustomEndText);

            if (startYear <= 0 || endYear <= 0) return new List<Record>();

            int startNumeric = startYear * 100 + (startMonth > 0 ? startMonth : 1);
            int endNumeric = endYear * 100 + (endMonth > 0 ? endMonth : 12);

            var records = new List<Record>();
            foreach (var ymGroup in YearMonthGroups)
            {
                var (ymYear, ymMonth) = ParseYearMonthName(ymGroup.Name);
                if (ymYear == 0) continue;

                int ymNumeric = ymYear * 100 + ymMonth;
                if (ymNumeric < startNumeric || ymNumeric > endNumeric)
                    continue;

                // 如果只指定了年份（无月份），包含全年
                if (startMonth <= 0 && endMonth <= 0)
                {
                    // 按年筛选
                    if (ymYear >= startYear && ymYear <= endYear)
                    {
                        foreach (var dg in ymGroup.DateGroups)
                            records.AddRange(_dataService.GetRecordsByGroup(dg.Id));
                    }
                }
                else
                {
                    foreach (var dg in ymGroup.DateGroups)
                    {
                        int day = ParseDayFromDateName(dg.Name);
                        if (ymNumeric == startNumeric && startMonth > 0 && day < 1) continue;
                        if (ymNumeric == endNumeric && endMonth > 0 && day > 31) continue;
                        records.AddRange(_dataService.GetRecordsByGroup(dg.Id));
                    }
                }
            }
            return records;
        }

        // ========== 统计计算 ==========

        private void ComputeStatsCards()
        {
            TotalRecords = _allRecords.Count;

            ValidRecordCount = _allRecords.Count(r =>
                !string.IsNullOrWhiteSpace(r.HazardType) &&
                !string.IsNullOrWhiteSpace(r.HazardLevel));

            HazardTypeCount = _allRecords
                .Select(r => r.HazardType)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct()
                .Count();

            var hasMeasureCount = _allRecords.Count(r => !string.IsNullOrWhiteSpace(r.Measure));
            var completedCount = _allRecords.Count(r =>
                r.CompletedDate.HasValue && !string.IsNullOrWhiteSpace(r.Measure));

            if (hasMeasureCount > 0)
            {
                CompletionRate = (double)completedCount / hasMeasureCount * 100;
                CompletionRateText = $"{CompletionRate:F1}%";
            }
            else
            {
                CompletionRate = 0;
                CompletionRateText = "0%";
            }
        }

        private void ComputeTypeDistribution()
        {
            TypeDistribution.Clear();

            var groups = _allRecords
                .Where(r => !string.IsNullOrWhiteSpace(r.HazardType))
                .GroupBy(r => r.HazardType)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToList();

            int total = groups.Sum(x => x.Count);
            int seq = 0;
            foreach (var g in groups)
            {
                TypeDistribution.Add(new TypeStatItem
                {
                    SequenceNumber = ++seq,
                    HazardType = g.Type,
                    Count = g.Count,
                    Percentage = total > 0 ? (double)g.Count / total * 100 : 0
                });
            }
        }

        private void ComputeMonthlyTrend()
        {
            MonthlyTrend.Clear();

            var monthlyGroups = _allRecords
                .Select(r =>
                {
                    if (DateTime.TryParse(r.CreatedAt, out var dt))
                        return new { Year = dt.Year, Month = dt.Month, Key = $"{dt.Year:D4}-{dt.Month:D2}" };
                    return null;
                })
                .Where(x => x != null)
                .GroupBy(x => x!.Key)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .OrderBy(x => x.Key)
                .ToList();

            foreach (var mg in monthlyGroups)
            {
                var parts = mg.Key.Split('-');
                string display = parts.Length == 2 ? $"{parts[1]}月" : mg.Key;
                MonthlyTrend.Add(new MonthlyStatItem
                {
                    Month = mg.Key,
                    MonthDisplay = display,
                    NewCount = mg.Count
                });
            }

            int cumulative = 0;
            foreach (var item in MonthlyTrend)
            {
                cumulative += item.NewCount;
                item.CumulativeCount = cumulative;
            }
        }

        private void FillFilteredRecords()
        {
            FilteredRecords.Clear();

            var dateGroupNames = new Dictionary<string, string>();
            foreach (var ym in YearMonthGroups)
            {
                foreach (var dg in ym.DateGroups)
                    dateGroupNames[dg.Id] = dg.Name;
            }

            int index = 1;
            foreach (var r in _allRecords)
            {
                string dateName = dateGroupNames.TryGetValue(r.GroupId, out var dn) ? dn : "";
                FilteredRecords.Add(new AnalysisRecordRow
                {
                    DisplayIndex = index++,
                    RecordId = r.Id,
                    DateName = dateName,
                    HazardType = r.HazardType,
                    HazardLevel = r.HazardLevel,
                    Description = r.Description,
                    Measure = r.Measure,
                    Status = ComputeRecordStatus(r)
                });
            }
        }

        // ========== 辅助方法 ==========

        private string ComputeRecordStatus(Record r)
        {
            bool hasContent = !string.IsNullOrWhiteSpace(r.Description) || !string.IsNullOrWhiteSpace(r.HazardType);
            if (!hasContent) return "";
            if (r.CompletedDate.HasValue && !string.IsNullOrWhiteSpace(r.Measure))
                return "完成";
            if (r.Deadline.HasValue && r.Deadline.Value < DateTime.Now)
                return "超期";
            return "进行";
        }

        private static int ParseDateValue(string name)
        {
            var match = System.Text.RegularExpressions.Regex.Match(name, @"(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int val))
                return val;
            return 0;
        }

        public static (int year, int month) ParseYearMonthName(string name)
        {
            // 格式: "2026年05月"
            var match = System.Text.RegularExpressions.Regex.Match(name, @"(\d{4})年(\d{1,2})月");
            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out int y) &&
                int.TryParse(match.Groups[2].Value, out int m))
                return (y, m);
            return (0, 0);
        }

        public static int ParseYearFromText(string text)
        {
            // "2026年度" → 2026
            var match = System.Text.RegularExpressions.Regex.Match(text, @"(\d{4})");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int y))
                return y;
            return 0;
        }

        public static int ParseMonthFromText(string text)
        {
            // "5月" → 5
            var match = System.Text.RegularExpressions.Regex.Match(text, @"(\d{1,2})月");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int m))
                return m;
            return 0;
        }

        private static int ParseDayFromDateName(string name)
        {
            var match = System.Text.RegularExpressions.Regex.Match(name, @"(\d{1,2})月(\d{1,2})日");
            if (match.Success && int.TryParse(match.Groups[2].Value, out int day))
                return day;
            return 0;
        }

        public static (int year, int month) ParseYearMonthFromDisplay(string text)
        {
            // 支持格式: "2026年度"、"2026年5月"、"2026年"
            var match = System.Text.RegularExpressions.Regex.Match(text, @"(\d{4})年度");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int y))
                return (y, 0);

            match = System.Text.RegularExpressions.Regex.Match(text, @"(\d{4})年(\d{1,2})月");
            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out y) &&
                int.TryParse(match.Groups[2].Value, out int m))
                return (y, m);

            match = System.Text.RegularExpressions.Regex.Match(text, @"(\d{4})年");
            if (match.Success && int.TryParse(match.Groups[1].Value, out y))
                return (y, 0);

            return (0, 0);
        }
    }
}
