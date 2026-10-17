// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using ClosedXML.Excel;
using MS_SafetyManagementSystem.Views;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace MS_SafetyManagementSystem.Export
{
    /// <summary>
    /// 一键导出工具类。
    /// 使用 IExportDataProvider 提供的数据和元数据，生成 Excel 文件。
    /// 与模块数据完全解耦。
    /// </summary>
    public static class OneClickExporter
    {

        /// <summary>
        /// 执行一键导出
        /// </summary>
        /// <param name="provider">数据提供器</param>
        /// <param name="projectName">项目名称（用于标题和文件名）</param>
        /// <param name="owner">父窗口（用于弹出保存对话框）</param>
        /// <returns>是否成功导出</returns>
        public static bool Export(IExportDataProvider provider, string projectName, Window owner)
        {
            if (provider == null || provider.RowCount == 0)
            {
                MessageDialog.Show(owner, "没有可导出的数据", "提示");
                return false;
            }

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "一键导出",
                Filter = "Excel 文件|*.xlsx",
                // 文件名带时间戳（yyyyMMdd-HHmm），同范围同筛选多次导出不会重名覆盖
                FileName = $"{projectName}_{provider.OneClickFileSuffix}-{DateTime.Now:yyyyMMdd-HHmm}.xlsx",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
            };

            if (saveDialog.ShowDialog(owner) != true) return false;

            try
            {
                using var workbook = new XLWorkbook();
                // Sheet 名跟随内容（Excel 限制 31 字符 + 非法字符清理），空则回退"统计"
                var ws = workbook.Worksheets.Add(BuildSheetName(provider.OneClickTitleSuffix));

                int colCount = provider.OneClickHeaders.Length;

                // 第1行：合并标题
                string title = $"{projectName} {provider.OneClickTitleSuffix}";
                ws.Range(1, 1, 1, colCount).Merge().Value = title;
                ws.Cell(1, 1).Style.Font.FontSize = 14;
                ws.Cell(1, 1).Style.Font.Bold = true;
                ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                ws.Row(1).Height = 30;

                // 第2行：表头
                for (int i = 0; i < colCount; i++)
                {
                    var cell = ws.Cell(2, i + 1);
                    cell.Value = provider.OneClickHeaders[i];
                    cell.Style.Font.SetFontName("微软雅黑");
                    cell.Style.Font.SetFontSize(10);
                    cell.Style.Font.SetBold(true);
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    ws.Column(i + 1).Width = provider.OneClickColumnWidths[i];
                }
                ws.Row(2).Height = 30;

                // 数据行 — 写入内容（统一微软雅黑）
                for (int r = 0; r < provider.RowCount; r++)
                {
                    int excelRow = r + 3;
                    for (int c = 0; c < colCount; c++)
                    {
                        var cell = ws.Cell(excelRow, c + 1);
                        cell.Value = provider.GetTextValue(r, provider.OneClickFieldNames[c]);
                        cell.Style.Font.SetFontName("微软雅黑");
                        cell.Style.Font.SetFontSize(10);
                        cell.Style.Font.SetBold(false);
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        cell.Style.Alignment.WrapText = true;
                    }
                }

                // 行高自适应 — 与 ExcelSmartExporter 完全一致的逻辑
                var columnCharWidths = new Dictionary<int, int>();
                for (int c = 1; c <= colCount; c++)
                {
                    double colWidth = ws.Column(c).Width;
                    int chineseChars = (int)Math.Round(colWidth / 2d);
                    if (chineseChars < 3) chineseChars = 8;
                    columnCharWidths[c] = chineseChars;
                }

                int lastDataRow = provider.RowCount + 2;
                for (int i = 3; i <= lastDataRow; i++)
                {
                    var row = ws.Row(i);
                    row.AdjustToContents();

                    // 基于文本内容和列宽估算最少所需行高
                    int maxLines = 1;
                    foreach (var cell in row.CellsUsed())
                    {
                        string text = cell.GetString();
                        if (string.IsNullOrEmpty(text)) continue;

                        int lineBreaks = text.Count(c => c == '\n');
                        int colChars = columnCharWidths.TryGetValue(cell.Address.ColumnNumber, out int cw) ? cw : 8;
                        double linesByLength = Math.Ceiling((double)text.Length / colChars);
                        maxLines = Math.Max(maxLines, Math.Max(lineBreaks + 1, (int)linesByLength));
                    }

                    double minHeight = maxLines * 15;  // 15 point / 行

                    if (row.Height < minHeight)
                        row.Height = minHeight;

                    const double GlobalMinHeight = 25;
                    if (row.Height < GlobalMinHeight)
                        row.Height = GlobalMinHeight;

                    row.Height += 5;  // 额外呼吸空间
                }

                workbook.SaveAs(saveDialog.FileName);

                // 尝试打开文件
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(saveDialog.FileName) { UseShellExecute = true }); }
                catch (Exception ex) { Log.Warning(ex, "导出成功但打开文件失败: {File}", saveDialog.FileName); }

                MessageDialog.Show(owner, $"导出成功！\n文件保存至：{saveDialog.FileName}", "完成");
                return true;
            }
            catch (Exception ex)
            {
                MessageDialog.Show(owner, $"导出失败：{ex.Message}", "错误");
                return false;
            }
        }

        /// <summary>
        /// 生成 Excel 工作表名：清理 Excel 非法字符、去除首尾空白/单引号、截断至 31 字符。
        /// </summary>
        private static string BuildSheetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "统计";
            foreach (char c in new[] { '[', ']', ':', '*', '?', '/', '\\' })
                name = name.Replace(c.ToString(), "");
            name = name.Trim().Trim('\'');
            if (name.Length > 31) name = name.Substring(0, 31);
            return string.IsNullOrWhiteSpace(name) ? "统计" : name;
        }
    }
}
