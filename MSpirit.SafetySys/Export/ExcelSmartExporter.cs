// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using ClosedXML.Excel;
using MS_SafetyManagementSystem.Services;
using Serilog;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MS_SafetyManagementSystem.Export
{
    public class ExcelSmartExporter
    {
        private readonly MappingDictionaryService _mappingService;
        private readonly string _attachmentsRootPath;
        private readonly string _module;
        private IExportDataProvider _dataProvider = null!;

        public ExcelSmartExporter(MappingDictionaryService mappingService, string attachmentsRootPath, string module = "Hazard")
        {
            _mappingService = mappingService;
            _attachmentsRootPath = attachmentsRootPath ?? "";
            _module = module;
        }

        public (bool Success, string? ErrorMessage) Export(string templatePath, string outputPath,
            IExportDataProvider dataProvider, string mode, string sheetName)
        {
            _dataProvider = dataProvider;

            if (!File.Exists(templatePath))
                return (false, "模板文件不存在");
            if (_dataProvider == null || _dataProvider.RowCount == 0)
                return (false, "没有可导出的数据");

            try
            {
                Log.Information("开始加载 Excel 模板文件: {TemplatePath}", templatePath);
                File.Copy(templatePath, outputPath, true);

                using var workbook = new XLWorkbook(outputPath);

                // 根据用户输入的工作表序号获取对应工作表
                if (!int.TryParse(sheetName, out int sheetIndex) || sheetIndex < 1)
                    sheetIndex = 1;
                var worksheet = workbook.Worksheet(sheetIndex);
                if (worksheet == null)
                    return (false, $"未找到序号为 {sheetIndex} 的工作表");

                // 1. 定位表头行
                var headerRow = FindHeaderRow(worksheet);
                if (headerRow == null)
                    return (false, "未检测到有效的表头行（文档内容至少需两个字段）");

                // 2. 构建列映射
                var columnMapping = BuildColumnMapping(headerRow);
                if (columnMapping.Count == 0)
                    return (false, "表头列映射为空，无法导出");

                // 获取每列的中文可容纳字数（Width ÷ 2，中文约为英文字符 2 倍宽）
                var columnCharWidths = new Dictionary<int, int>();
                foreach (int col in columnMapping.Keys)
                {
                    double colWidth = worksheet.Column(col).Width;
                    int chineseChars = (int)Math.Round(colWidth / 2d);
                    if (chineseChars < 3) chineseChars = 8; // 最少 8 字/行
                    columnCharWidths[col] = chineseChars;
                }

                // 3. 数据起始行（表头下一行）
                int dataStartRow = headerRow.RowNumber() + 1;

                // 4. 找到连续数据区域的最后一行（不包括底部说明行和空行）
                int lastDataRow = FindLastDataRow(worksheet, dataStartRow, columnMapping);
                bool hasDataRowInTemplate = lastDataRow >= dataStartRow;

                // 5. 确定样式模板行（只复制单元格样式，不复制行高）
                bool useHeaderAsTemplate = false;
                int templateRowNumber = dataStartRow;
                if (hasDataRowInTemplate)
                {
                    templateRowNumber = dataStartRow;
                }
                else
                {
                    useHeaderAsTemplate = true;
                    templateRowNumber = headerRow.RowNumber();
                }
                var templateRow = worksheet.Row(templateRowNumber);

                // 6. 删除原有数据行（从 dataStartRow 到 lastDataRow）
                if (hasDataRowInTemplate && lastDataRow >= dataStartRow)
                {
                    var rowsToDelete = worksheet.Rows(dataStartRow, lastDataRow);
                    rowsToDelete.Delete();
                }

                // 6.5 插入空行，将底部行下推，以便写入新数据
                if (_dataProvider.RowCount > 0)
                {
                    worksheet.Row(dataStartRow).InsertRowsAbove(_dataProvider.RowCount);
                }

                // 7. 写入新数据（现在 dataStartRow 是插入的第一个空行）
                int currentRow = dataStartRow;
                var photoHeights = new Dictionary<int, int>(); // 记录每行图片总高度(px)
                for (int rowIdx = 0; rowIdx < _dataProvider.RowCount; rowIdx++)
                {
                    // 复制单元格样式（不复制行高）
                    CopyCellStyles(templateRow, worksheet.Row(currentRow), useHeaderAsTemplate);

                    foreach (var kv in columnMapping)
                    {
                        int colIndex = kv.Key;
                        string dataField = kv.Value;
                        var cell = worksheet.Cell(currentRow, colIndex);

                        if (dataField == "HazardPhoto" || dataField == "RemedyPhoto")
                        {
                            if (mode == "Full" || mode == "OnlyImage")
                            {
                                var photoPaths = _dataProvider.GetPhotoPaths(rowIdx, dataField, _attachmentsRootPath);
                                if (photoPaths.Count > 0)
                                {
                                    // 清除单元格原有内容
                                    cell.Value = "";
                                    int photoOffset = 0;
                                    const int maxPhotoWidth = 120; // 图片最大宽度（像素）
                                    const int photoSpacing = 5;    // 图片间距（像素）

                                    foreach (var photoPath in photoPaths)
                                    {
                                        using var srcBitmap = SKBitmap.Decode(photoPath);
                                        if (srcBitmap == null) continue;

                                        // 压缩图片
                                        float scale = (float)maxPhotoWidth / srcBitmap.Width;
                                        if (scale > 1f) scale = 1f;
                                        int newWidth = (int)(srcBitmap.Width * scale);
                                        int newHeight = (int)(srcBitmap.Height * scale);

                                        byte[] compressed;
                                        if (scale < 1f)
                                        {
                                            var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
                                            using var resized = srcBitmap.Resize(new SKImageInfo(newWidth, newHeight), sampling);
                                            if (resized == null) continue;
                                            using var imageResized = SKImage.FromBitmap(resized);
                                            using var dataResized = imageResized.Encode(SKEncodedImageFormat.Jpeg, 80);
                                            compressed = dataResized.ToArray();
                                        }
                                        else
                                        {
                                            using var image = SKImage.FromBitmap(srcBitmap);
                                            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80);
                                            compressed = data.ToArray();
                                        }

                                        // 嵌入图片到单元格
                                        using var stream = new MemoryStream(compressed);
                                        var picture = worksheet.AddPicture(stream)
                                            .MoveTo(cell, 0, photoOffset);

                                        photoOffset += newHeight + photoSpacing;
                                    }
                                    // 记录该行的图片总高度
                                    photoHeights[currentRow] = photoOffset;
                                }
                                else
                                {
                                    cell.Value = "";
                                }
                            }
                            else
                            {
                                cell.Value = "";
                            }
                            continue;
                        }

                        if (mode == "OnlyImage")
                        {
                            if (dataField != "RowNumber")
                                continue;
                        }

                        if (dataField == "RowNumber")
                        {
                            cell.Value = rowIdx + 1;
                            continue;
                        }

                        string textValue = _dataProvider.GetTextValue(rowIdx, dataField);
                        cell.Value = textValue;
                    }
                    currentRow++;
                }
                // 7.5 强制所有数据行字体不加粗
                for (int i = dataStartRow; i < currentRow; i++)
                {
                    var row = worksheet.Row(i);
                    foreach (var cell in row.CellsUsed())
                    {
                        cell.Style.Font.Bold = false;
                    }
                }

                // 8. 自动适应行高，并保证多行文本的行高充足
                for (int i = dataStartRow; i < currentRow; i++)
                {
                    var row = worksheet.Row(i);
                    row.AdjustToContents();

                    // 基于文本内容和列宽估算最少所需行高
                    int maxLines = 1;
                    foreach (var cell in row.CellsUsed())
                    {
                        string text = cell.GetString();
                        if (string.IsNullOrEmpty(text)) continue;

                        int lineBreaks = text.Count(c => c == '\n');
                        int colChars = columnCharWidths.TryGetValue(cell.Address.ColumnNumber, out int c) ? c : 8;
                        double linesByLength = Math.Ceiling((double)text.Length / colChars);
                        maxLines = Math.Max(maxLines, Math.Max(lineBreaks + 1, (int)linesByLength));
                    }

                    double minHeight = maxLines * 15;          // 15 point / 行

                    // 图片高度兜底（MoveTo 的 offset 单位是像素，转 point ÷ 1.33）
                    if (photoHeights.TryGetValue(i, out int totalPhotoPx))
                    {
                        double photoHeightPt = totalPhotoPx / 1.33;
                        if (minHeight < photoHeightPt)
                            minHeight = photoHeightPt;
                    }

                    if (row.Height < minHeight)
                        row.Height = minHeight;

                    const double GlobalMinHeight = 25;
                    if (row.Height < GlobalMinHeight)
                        row.Height = GlobalMinHeight;

                    row.Height += 5;                          // 额外呼吸空间
                }

                workbook.Save();
                Log.Information("Excel 导出成功，文件: {OutputPath}", outputPath);
                return (true, null);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Excel 导出过程中发生异常");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// 找到模板中示例数据区域的最后一行（模板数据行将被删除后重写）。
        /// 规则（2026-08-14 与 ExcelSmartImporter 同步修复：数据行优先，避免示例数据单元格
        /// 以"日期/审核/注："等底部关键词开头时被误判为底部说明行而截断，导致旧数据删不干净）：
        ///  - 行内任一映射列有文本 → 数据行，继续扫描；
        ///  - 非数据行且行内有任何内容（底部说明行、落款、非映射列内容等）→ 结构边界，停止读取；
        ///  - 完全空行不会出现在 RowsUsed() 中，天然跳过（避免中间空行截断数据）。
        /// </summary>
        private int FindLastDataRow(IXLWorksheet worksheet, int startRow, Dictionary<int, string> columnMapping)
        {
            int lastDataRow = startRow - 1;
            var usedRows = worksheet.RowsUsed().Where(r => r.RowNumber() >= startRow).OrderBy(r => r.RowNumber());
            foreach (var row in usedRows)
            {
                // 数据行判定：任一映射列（排除序号列，序号仅是占位不算数据）有文本
                //（模板数据行均为文本，无需考虑嵌入图片）
                bool hasData = false;
                foreach (var col in columnMapping.Keys)
                {
                    if (columnMapping[col] == "RowNumber")
                        continue;
                    var cell = row.Cell(col);
                    if (!string.IsNullOrWhiteSpace(cell.GetString()))
                    {
                        hasData = true;
                        break;
                    }
                }

                if (hasData)
                {
                    lastDataRow = row.RowNumber();
                    continue;
                }

                // 非数据行：行内有内容（底部说明行/落款/非映射列内容）→ 结构边界，停止读取
                break;
            }
            return lastDataRow;
        }

        private IXLRow? FindHeaderRow(IXLWorksheet worksheet)
        {
            var usedRows = worksheet.RowsUsed();
            foreach (var row in usedRows)
            {
                int matchCount = 0;
                foreach (var cell in row.CellsUsed())
                {
                    string text = cell.GetString().Trim();
                    if (_mappingService.MatchField(text, _module) != null)
                    {
                        matchCount++;
                        if (matchCount >= 2)
                            return row;
                    }
                }
            }
            return null;
        }

        private Dictionary<int, string> BuildColumnMapping(IXLRow headerRow)
        {
            var mapping = new Dictionary<int, string>();
            foreach (var cell in headerRow.CellsUsed())
            {
                string headerText = cell.GetString().Trim();
                string? dataField = _mappingService.MatchField(headerText);
                if (dataField != null)
                    mapping[cell.Address.ColumnNumber] = dataField;
                else
                    Log.Information("列 {Col} 未匹配: {HeaderText}", cell.Address.ColumnNumber, headerText);
            }
            return mapping;
        }

        /// <summary>
        /// 获取指定记录的指定类型图片完整路径列表
        /// </summary>
        /// <summary>
        /// 复制单元格样式（不复制行高），并强制开启自动换行
        /// </summary>
        private void CopyCellStyles(IXLRow sourceRow, IXLRow targetRow, bool useHeaderAsTemplate)
        {
            // 不复制行高，让 Excel 使用默认高度，后续统一调整

            var usedColumns = sourceRow.CellsUsed().Select(c => c.Address.ColumnNumber).Distinct();
            foreach (int col in usedColumns)
            {
                var sourceCell = sourceRow.Cell(col);
                var targetCell = targetRow.Cell(col);

                targetCell.Style.Font = sourceCell.Style.Font;
                targetCell.Style.Fill = sourceCell.Style.Fill;
                targetCell.Style.Border = sourceCell.Style.Border;
                targetCell.Style.Alignment = sourceCell.Style.Alignment;
                targetCell.Style.NumberFormat = sourceCell.Style.NumberFormat;

                // 强制开启自动换行（即使模板未开启）
                targetCell.Style.Alignment.WrapText = true;

                // 强制数据行字体不加粗（覆盖模板可能存在的加粗设置）
                targetCell.Style.Font.Bold = false;
            }
        }
    }
}