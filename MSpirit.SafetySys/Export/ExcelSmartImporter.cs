// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using MS_SafetyManagementSystem.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MS_SafetyManagementSystem.Export
{
    /// <summary>
    /// Excel 智能导入器：解析 .xlsx 文件，识别表头、匹配列映射、提取文本和图片
    /// 纯 I/O 操作，与具体业务模块无关
    /// </summary>
    public class ExcelSmartImporter
    {
        private readonly MappingDictionaryService _mappingService;
        private readonly string _module;

        public ExcelSmartImporter(MappingDictionaryService mappingService, string module = "Hazard")
        {
            _mappingService = mappingService;
            _module = module;
        }

        /// <summary>
        /// 解析 Excel 文件，返回通用解析结果
        /// </summary>
        public ImportParseResult Parse(string filePath, int sheetIndex = 1)
        {
            var result = new ImportParseResult
            {
                SourceFilePath = filePath,
                SourceFileType = "Excel 工作簿"
            };

            if (!File.Exists(filePath))
            {
                result.Warnings.Add($"文件不存在: {filePath}");
                return result;
            }

            try
            {
                using var workbook = new XLWorkbook(filePath);

                if (sheetIndex < 1 || sheetIndex > workbook.Worksheets.Count)
                {
                    result.Warnings.Add($"工作表序号 {sheetIndex} 无效，共 {workbook.Worksheets.Count} 个工作表");
                    return result;
                }

                var worksheet = workbook.Worksheet(sheetIndex);

                // 1. 定位表头行
                var headerRow = FindHeaderRow(worksheet);
                if (headerRow == null)
                {
                    result.Warnings.Add("未在表格中检测到有效的表头行（文档内容至少需两个字段）");
                    return result;
                }

                // 2. 构建列映射
                var columnMapping = BuildColumnMapping(headerRow);
                if (columnMapping.Count == 0)
                {
                    result.Warnings.Add("表头列映射为空，无法解析数据");
                    return result;
                }

                // 统计匹配到的字段
                foreach (var kv in columnMapping)
                {
                    if (result.FieldColumnCount.ContainsKey(kv.Value))
                        result.FieldColumnCount[kv.Value]++;
                    else
                        result.FieldColumnCount[kv.Value] = 1;
                }

                Log.Debug("[导入调试] 匹配到 {Count} 个字段: {Fields}",
                    columnMapping.Count,
                    string.Join(", ", columnMapping.OrderBy(kv => kv.Key).Select(kv => $"[{kv.Key}]{kv.Value}")));

                // 3. 预提取嵌入图片 → 按单元格引用建立映射
                var embeddedImages = ExtractEmbeddedImages(filePath, sheetIndex);

                // 4. 读取数据行
                int dataStartRow = headerRow.RowNumber() + 1;
                int lastDataRow = FindLastDataRow(worksheet, dataStartRow, columnMapping, embeddedImages);

                Log.Debug("[导入调试] 数据行范围: {StartRow} ~ {LastRow} (共 {Count} 行)", dataStartRow, lastDataRow, lastDataRow - dataStartRow + 1);

                int rowIdx = 0;
                for (int r = dataStartRow; r <= lastDataRow; r++)
                {
                    var row = worksheet.Row(r);
                    var importRow = new ImportRowData { RowIndex = rowIdx };

                    foreach (var kv in columnMapping)
                    {
                        int col = kv.Key;
                        string field = kv.Value;
                        var cell = row.Cell(col);
                        string cellText = cell.GetString().Trim();

                        if (field == "HazardPhoto" || field == "RemedyPhoto")
                        {
                            var photos = new List<string>();

                            // 方案A: 单元格文本作为文件路径
                            if (!string.IsNullOrEmpty(cellText))
                            {
                                TryResolvePhotoPath(cellText, filePath, photos);
                            }

                            // 方案B: 检查嵌入图片
                            string cellRef = $"{GetColumnLetter(col)}{r}";
                            if (embeddedImages.TryGetValue(cellRef, out var imgDataList))
                            {
                                string tempDir = Path.Combine(Path.GetTempPath(), "HazardImport");
                                if (!Directory.Exists(tempDir))
                                    Directory.CreateDirectory(tempDir);

                                foreach (var imgData in imgDataList)
                                {
                                    string tempFile = Path.Combine(tempDir, $"{Guid.NewGuid():N}.jpg");
                                    File.WriteAllBytes(tempFile, imgData);
                                    if (!photos.Contains(tempFile))
                                        photos.Add(tempFile);
                                }
                            }

                            if (photos.Count > 0)
                                importRow.PhotoPaths[field] = photos;
                        }
                        else if (field != "RowNumber")
                        {
                            if (!string.IsNullOrEmpty(cellText))
                                importRow.TextValues[field] = cellText;
                        }
                    }

                    // 基础行有效性校验
                    bool hasTextField = importRow.TextValues.Values.Any(v => !string.IsNullOrWhiteSpace(v));
                    bool hasPhotos = importRow.PhotoPaths.Any(kv => kv.Value != null && kv.Value.Count > 0);
                    importRow.IsValid = hasTextField || hasPhotos;
                    if (!hasTextField && !hasPhotos)
                    {
                        importRow.ValidationMessage = "该行无有效文本数据或图片，已跳过";
                        result.InvalidRowIndices.Add(rowIdx);
                    }

                    // 调试日志：打印第一行和每行文本字段数
                    if (rowIdx == 0 || !hasTextField)
                    {
                        Log.Debug("[导入调试] 行 {RowIdx}(r={Row}): 文本字段 {TextCount} 个, 图片字段 {PhotoCount} 个, 有效={IsValid}",
                            rowIdx, r, importRow.TextValues.Count, importRow.PhotoPaths.Count, importRow.IsValid);
                        if (importRow.TextValues.Count > 0)
                        {
                            foreach (var tv in importRow.TextValues.Take(3))
                                Log.Debug("[导入调试]   {Field} = \"{Value}\"", tv.Key, tv.Value.Length > 50 ? tv.Value[..50] + "…" : tv.Value);
                        }
                    }

                    result.Rows.Add(importRow);
                    rowIdx++;
                }

                Log.Information("Excel 解析完成: {Path}, 有效行 {ValidCount}/{TotalCount}, 匹配字段 {FieldCount}",
                    filePath, result.ValidRowCount, result.TotalRowCount, result.MatchedFieldCount);

                if (result.InvalidRowIndices.Count > 0)
                {
                    Log.Debug("[导入调试] 无效行索引: {Indices}", string.Join(", ", result.InvalidRowIndices));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "解析 Excel 文件失败: {Path}", filePath);
                result.Warnings.Add($"解析失败: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 查找表头行（匹配字段数 >= 2）
        /// </summary>
        private IXLRow? FindHeaderRow(IXLWorksheet worksheet)
        {
            var usedRows = worksheet.RowsUsed();
            foreach (var row in usedRows)
            {
                int matchCount = 0;
                var distinctFields = new HashSet<string>();
                foreach (var cell in row.CellsUsed())
                {
                    string text = cell.GetString().Trim();
                    string? field = _mappingService.MatchField(text, _module);
                    if (field != null)
                    {
                        matchCount++;
                        distinctFields.Add(field);
                    }
                }
                // 匹配字段须互不相同（≥2 个不同字段），避免“标签+内容”排版行被误判为表头
                if (matchCount >= 2 && distinctFields.Count >= 2)
                    return row;
            }
            return null;
        }

        /// <summary>
        /// 构建列映射：列号 → 数据字段名
        /// </summary>
        private Dictionary<int, string> BuildColumnMapping(IXLRow headerRow)
        {
            var mapping = new Dictionary<int, string>();
            foreach (var cell in headerRow.CellsUsed())
            {
                string headerText = cell.GetString().Trim();
                string? dataField = _mappingService.MatchField(headerText);
                if (dataField != null)
                    mapping[cell.Address.ColumnNumber] = dataField;
            }
            return mapping;
        }

        /// <summary>
        /// 查找数据区域的最后一行。
        /// 规则（2026-08-14 修复：数据行优先，避免数据单元格以"日期/审核/注："等
        /// 底部关键词开头时被误判为底部说明行而截断）：
        ///  - 行内任一映射列（序号列除外，序号仅是占位不算数据）有文本或嵌入图片 → 数据行，继续扫描；
        ///  - 非数据行且行内有任何内容（底部说明行、落款、非映射列内容等）→ 结构边界，停止读取；
        ///  - 完全空行 → 跳过。
        /// </summary>
        private int FindLastDataRow(IXLWorksheet worksheet, int startRow, Dictionary<int, string> columnMapping, Dictionary<string, List<byte[]>> embeddedImages)
        {
            int lastDataRow = startRow - 1;

            // 扫描上限：取最后使用行与所有图片锚点行中的较大者，避免纯图片行被忽略
            int upperBound = worksheet.LastRowUsed()?.RowNumber() ?? (startRow - 1);
            foreach (var cellRef in embeddedImages.Keys)
            {
                if (TryParseRowNumber(cellRef, out int imageRow) && imageRow > upperBound)
                    upperBound = imageRow;
            }

            for (int r = startRow; r <= upperBound; r++)
            {
                var row = worksheet.Row(r);
                var usedCells = row.CellsUsed().ToList();

                // 数据行判定：任一映射列（排除序号列）有文本或嵌入图片
                bool hasData = false;
                foreach (var col in columnMapping.Keys)
                {
                    if (columnMapping[col] == "RowNumber")
                        continue;
                    if (!string.IsNullOrWhiteSpace(row.Cell(col).GetString()))
                    {
                        hasData = true;
                        break;
                    }
                    string cellRef = $"{GetColumnLetter(col)}{r}";
                    if (embeddedImages.ContainsKey(cellRef))
                    {
                        hasData = true;
                        break;
                    }
                }

                if (hasData)
                {
                    lastDataRow = r;
                    continue;
                }

                // 非数据行：行内有内容（底部说明行/落款/非映射列内容）→ 结构边界，停止读取
                // 完全空行直接跳过，避免中间空行截断数据
                if (usedCells.Count > 0)
                    break;
            }
            return lastDataRow;
        }

        /// <summary>
        /// 从单元格引用解析行号（如 "E4" → 4）
        /// </summary>
        private static bool TryParseRowNumber(string cellRef, out int rowNumber)
        {
            int i = 0;
            while (i < cellRef.Length && char.IsLetter(cellRef[i]))
                i++;
            return int.TryParse(cellRef.Substring(i), out rowNumber);
        }

        /// <summary>
        /// 提取 Excel 嵌入图片：返回 单元格引用 → 图片字节列表 的映射
        /// 支持 OneCellAnchor（程序自身导出的图片）与 TwoCellAnchor（其他工具插入的浮动图片）
        /// </summary>
        private Dictionary<string, List<byte[]>> ExtractEmbeddedImages(string filePath, int sheetIndex)
        {
            var result = new Dictionary<string, List<byte[]>>();

            try
            {
                using var doc = SpreadsheetDocument.Open(filePath, false);
                var workbookPart = doc.WorkbookPart;
                if (workbookPart == null) return result;

                var sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>().ToList();
                if (sheets == null || sheetIndex < 1 || sheetIndex > sheets.Count)
                    return result;

                var sheet = sheets[sheetIndex - 1];
                string? relId = sheet.Id?.Value;
                if (string.IsNullOrEmpty(relId)) return result;

                var worksheetPart = (WorksheetPart?)workbookPart.GetPartById(relId);
                if (worksheetPart == null) return result;

                var drawingsPart = worksheetPart.DrawingsPart;
                if (drawingsPart == null) return result;

                var worksheetDrawing = drawingsPart.WorksheetDrawing;
                if (worksheetDrawing == null) return result;

                foreach (var anchor in worksheetDrawing.Elements<DocumentFormat.OpenXml.Drawing.Spreadsheet.OneCellAnchor>())
                {
                    TryExtractAnchorImage(anchor, anchor.FromMarker, drawingsPart, result);
                }
                foreach (var anchor in worksheetDrawing.Elements<DocumentFormat.OpenXml.Drawing.Spreadsheet.TwoCellAnchor>())
                {
                    TryExtractAnchorImage(anchor, anchor.FromMarker, drawingsPart, result);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "提取 Excel 嵌入图片时发生异常，跳过图片提取");
            }

            return result;
        }

        /// <summary>
        /// 从单个绘图锚点提取图片并登记到 单元格引用 → 图片列表
        /// </summary>
        private static void TryExtractAnchorImage(
            DocumentFormat.OpenXml.OpenXmlCompositeElement anchor,
            DocumentFormat.OpenXml.Drawing.Spreadsheet.MarkerType? fromMarker,
            DrawingsPart drawingsPart,
            Dictionary<string, List<byte[]>> result)
        {
            if (fromMarker == null) return;

            if (!int.TryParse(fromMarker.ColumnId?.Text, out int colIdx)) return;
            if (!int.TryParse(fromMarker.RowId?.Text, out int rowIdx)) return;

            string colLetter = GetColumnLetter(colIdx + 1);
            string cellRef = $"{colLetter}{rowIdx + 1}";

            var picture = anchor.GetFirstChild<DocumentFormat.OpenXml.Drawing.Spreadsheet.Picture>();
            if (picture == null) return;

            var blipFill = picture.GetFirstChild<DocumentFormat.OpenXml.Drawing.Spreadsheet.BlipFill>();
            if (blipFill == null) return;

            var blip = blipFill.GetFirstChild<DocumentFormat.OpenXml.Drawing.Blip>();
            if (blip?.Embed == null) return;

            var imagePart = (ImagePart?)drawingsPart.GetPartById(blip.Embed.Value!);
            if (imagePart == null) return;

            using var stream = imagePart.GetStream();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] imageBytes = ms.ToArray();

            if (result.TryGetValue(cellRef, out var existing))
                existing.Add(imageBytes);
            else
                result[cellRef] = new List<byte[]> { imageBytes };
        }

        /// <summary>
        /// 解析单元格文件路径：支持绝对路径和相对于源文件的相对路径
        /// </summary>
        private void TryResolvePhotoPath(string cellText, string sourceFilePath, List<string> results)
        {
            string path = cellText.Trim().Trim('"', '\'');

            if (string.IsNullOrEmpty(path)) return;

            if (Path.IsPathRooted(path) && File.Exists(path))
            {
                results.Add(path);
                return;
            }

            string sourceDir = Path.GetDirectoryName(sourceFilePath) ?? "";
            string relativePath = Path.Combine(sourceDir, path);
            if (File.Exists(relativePath))
            {
                results.Add(relativePath);
                return;
            }

            string fileNameOnly = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(fileNameOnly))
            {
                var files = Directory.GetFiles(sourceDir, fileNameOnly, SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    results.Add(files[0]);
                    return;
                }
            }
        }

        /// <summary>
        /// 将列号转换为 Excel 列字母（如 1→A, 27→AA）
        /// </summary>
        private static string GetColumnLetter(int columnNumber)
        {
            string letter = "";
            while (columnNumber > 0)
            {
                int mod = (columnNumber - 1) % 26;
                letter = (char)('A' + mod) + letter;
                columnNumber = (columnNumber - mod) / 26;
            }
            return letter;
        }
    }
}
