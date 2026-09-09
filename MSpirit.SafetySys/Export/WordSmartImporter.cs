// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MS_SafetyManagementSystem.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace MS_SafetyManagementSystem.Export
{
    /// <summary>
    /// Word 智能导入器：解析 .docx 文件，扫描文档内多表格，识别表头，跨表格按行号分组，提取图片
    /// 纯 I/O 操作，与具体业务模块无关
    /// </summary>
    public class WordSmartImporter
    {
        private readonly MappingDictionaryService _mappingService;
        private readonly string _module;

        public WordSmartImporter(MappingDictionaryService mappingService, string module = "Hazard")
        {
            _mappingService = mappingService;
            _module = module;
        }

        /// <summary>
        /// 解析 Word 文件，返回通用解析结果
        /// </summary>
        public ImportParseResult Parse(string filePath)
        {
            var result = new ImportParseResult
            {
                SourceFilePath = filePath,
                SourceFileType = "Word 文档"
            };

            if (!File.Exists(filePath))
            {
                result.Warnings.Add($"文件不存在: {filePath}");
                return result;
            }

            try
            {
                using var document = WordprocessingDocument.Open(filePath, false);
                var mainPart = document.MainDocumentPart;
                if (mainPart?.Document?.Body == null)
                {
                    result.Warnings.Add("文档为空或无法读取");
                    return result;
                }

                var body = mainPart.Document.Body;
                var tables = body.Elements<W.Table>().ToList();

                if (tables.Count == 0)
                {
                    result.Warnings.Add("文档中没有表格，无法导入");
                    return result;
                }

                // 1. 扫描文档，收集所有有效数据块
                var dataBlocks = new List<(int tableIndex, int headerRowIndex, Dictionary<int, string> columnMapping, List<string[]> dataRows)>();

                for (int tableIdx = 0; tableIdx < tables.Count; tableIdx++)
                {
                    var table = tables[tableIdx];
                    var rows = table.Elements<W.TableRow>().ToList();
                    if (rows.Count == 0) continue;

                    // 表头识别：取“首个匹配字段数≥2 的行”作为表头候选，且候选行匹配字段须互不相同（方案B）。
                    // 若首个候选行字段重复（如报告书“整改情况”行两个单元格都命中“整改措施”），
                    // 判定该表为“标签+内容”排版表而非数据表，整体排除，避免污染多表合并。
                    int headerRowIndex = -1;
                    for (int i = 0; i < rows.Count; i++)
                    {
                        if (CountFieldMatches(rows[i]) >= 2)
                        {
                            if (IsHeaderRow(rows[i]))
                                headerRowIndex = i;
                            break;
                        }
                    }

                    if (headerRowIndex < 0) continue;

                    var headerRow = rows[headerRowIndex];
                    var columnMapping = BuildColumnMappingForRow(headerRow);
                    if (columnMapping.Count == 0) continue;

                    int headerTotalCols = GetRowTotalCols(headerRow);

                    // 收集表头下方结构匹配的数据行
                    var dataRows = new List<string[]>();
                    for (int i = headerRowIndex + 1; i < rows.Count; i++)
                    {
                        int totalCols = GetRowTotalCols(rows[i]);
                        if (totalCols == headerTotalCols)
                        {
                            var rowCells = rows[i].Elements<W.TableCell>().ToList();
                            var cellTexts = new string[columnMapping.Keys.DefaultIfEmpty(0).Max() + 1];
                            int colAcc = 0;
                            foreach (var cell in rowCells)
                            {
                                string text = GetMergedCellText(cell);
                                if (colAcc < cellTexts.Length)
                                    cellTexts[colAcc] = text;
                                colAcc++;
                            }
                            dataRows.Add(cellTexts);
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (dataRows.Count == 0) continue;

                    dataBlocks.Add((tableIdx, headerRowIndex, columnMapping, dataRows));
                }

                if (dataBlocks.Count == 0)
                {
                    result.Warnings.Add("未在任何表格中找到有效的表头和数据");
                    return result;
                }

                // 2. 统计匹配字段
                foreach (var block in dataBlocks)
                {
                    foreach (var kv in block.columnMapping)
                    {
                        if (result.FieldColumnCount.ContainsKey(kv.Value))
                            result.FieldColumnCount[kv.Value]++;
                        else
                            result.FieldColumnCount[kv.Value] = 1;
                    }
                }

                // 3. 跨表格分组：同一文档内的多个表格属于同一批隐患记录，
                //    文本表与照片表按“各自表格内的行位置”关联成同一条记录
                //    （表头行算第 0 行，各表格内相同位置的记录行属于同一条记录；
                //    不依赖文档中的序号值，自动编号或手工序号均不可靠）。
                //    以最大行数为准，缺失的行（如某条记录没有照片行）不截断其他表格。
                int maxRowCount = dataBlocks.Max(b => b.dataRows.Count);
                int minRowCount = dataBlocks.Min(b => b.dataRows.Count);
                if (maxRowCount != minRowCount)
                {
                    result.Warnings.Add($"各数据块行数不一致（{string.Join(", ", dataBlocks.Select(b => b.dataRows.Count))}），将按行位置合并，缺失的记录保留为纯文本或纯图片记录");
                }

                // 4. 提取嵌入图片
                var cellImages = ExtractEmbeddedImages(mainPart, tables);
                string tempDir = Path.Combine(Path.GetTempPath(), "HazardImport");
                if (!Directory.Exists(tempDir))
                    Directory.CreateDirectory(tempDir);

                // 5. 按行位置合并各数据块，生成记录（表头行算第 0 行，记录行依次为第 1、2、3…行）
                int total = maxRowCount;
                for (int rowIdx = 0; rowIdx < total; rowIdx++)
                {
                    var importRow = new ImportRowData { RowIndex = rowIdx };
                    for (int b = 0; b < dataBlocks.Count; b++)
                    {
                        if (rowIdx < dataBlocks[b].dataRows.Count)
                            MergeBlockRowInto(importRow, dataBlocks[b], rowIdx, cellImages, filePath, tempDir);
                    }
                    AddParsedRow(result, importRow);
                }
                Log.Information("Word 解析完成: {Path}, 有效行 {ValidCount}/{TotalCount}, 匹配字段 {FieldCount}, 数据块 {BlockCount}",
                    filePath, result.ValidRowCount, result.TotalRowCount, result.MatchedFieldCount, dataBlocks.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "解析 Word 文件失败: {Path}", filePath);
                result.Warnings.Add($"解析失败: {ex.Message}");
            }

            return result;
        }

        private bool IsHeaderRow(W.TableRow row)
        {
            int matchCount = 0;
            var distinctFields = new HashSet<string>();
            foreach (var cell in row.Elements<W.TableCell>())
            {
                string? field = MatchHeaderCell(cell);
                if (field == null) continue;
                matchCount++;
                distinctFields.Add(field);
                if (matchCount >= 2 && distinctFields.Count >= 2)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 统计行内匹配到字段的单元格数量（不校验字段是否重复）
        /// </summary>
        private int CountFieldMatches(W.TableRow row)
        {
            int count = 0;
            foreach (var cell in row.Elements<W.TableCell>())
            {
                if (MatchHeaderCell(cell) != null)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 将某个数据块的一行合并进记录（文本字段与图片附件）
        /// </summary>
        private void MergeBlockRowInto(
            ImportRowData importRow,
            (int tableIndex, int headerRowIndex, Dictionary<int, string> columnMapping, List<string[]> dataRows) block,
            int rowIdx,
            Dictionary<string, List<byte[]>> cellImages,
            string filePath,
            string tempDir)
        {
            string[] cellValues = block.dataRows[rowIdx];
            foreach (var kv in block.columnMapping)
            {
                int colIdx = kv.Key;
                string field = kv.Value;
                if (colIdx >= cellValues.Length) continue;
                string cellText = cellValues[colIdx]?.Trim() ?? "";

                if (field == "HazardPhoto" || field == "RemedyPhoto")
                {
                    var photos = new List<string>();
                    if (!string.IsNullOrEmpty(cellText))
                    {
                        TryResolvePhotoPath(cellText, filePath, photos);
                    }

                    // 图片键使用数据行在表格内的绝对行号（表头行可能不在首行）
                    string cellKey = $"{block.tableIndex}_{block.headerRowIndex + 1 + rowIdx}_{colIdx}";
                    if (cellImages.TryGetValue(cellKey, out var imgList))
                    {
                        // 一个单元格可能有多张图（2026-08-14 修复：原实现把多张图字节拼成
                        // 单个数组写出，产出损坏图片；现每张图单独写文件）
                        foreach (var imgBytes in imgList)
                        {
                            string tempFile = Path.Combine(tempDir, $"{Guid.NewGuid():N}.jpg");
                            File.WriteAllBytes(tempFile, imgBytes);
                            if (!photos.Contains(tempFile))
                                photos.Add(tempFile);
                        }
                    }

                    if (photos.Count > 0)
                    {
                        if (importRow.PhotoPaths.ContainsKey(field))
                            importRow.PhotoPaths[field].AddRange(photos);
                        else
                            importRow.PhotoPaths[field] = photos;
                    }
                }
                else if (field != "RowNumber")
                {
                    if (!string.IsNullOrEmpty(cellText))
                    {
                        importRow.TextValues[field] = cellText;
                    }
                }
            }
        }

        /// <summary>
        /// 记录收尾：有效性判定并加入结果集
        /// </summary>
        private void AddParsedRow(ImportParseResult result, ImportRowData importRow)
        {
            bool hasTextField = importRow.TextValues.Values.Any(v => !string.IsNullOrWhiteSpace(v));
            bool hasPhotos = importRow.PhotoPaths.Any(kv => kv.Value != null && kv.Value.Count > 0);
            importRow.IsValid = hasTextField || hasPhotos;
            if (!hasTextField && !hasPhotos)
            {
                importRow.ValidationMessage = "该行无有效文本数据或图片，已跳过";
                result.InvalidRowIndices.Add(importRow.RowIndex);
            }
            result.Rows.Add(importRow);
        }
        private Dictionary<int, string> BuildColumnMappingForRow(W.TableRow headerRow)
        {
            var mapping = new Dictionary<int, string>();
            int colIndex = 0;
            foreach (var cell in headerRow.Elements<W.TableCell>())
            {
                string? dataField = MatchHeaderCell(cell);
                if (dataField != null)
                    mapping[colIndex] = dataField;

                var gridSpan = cell.GetFirstChild<GridSpan>();
                int span = 1;
                if (gridSpan?.Val != null && int.TryParse(gridSpan.Val, out int parsed))
                    span = parsed;
                colIndex += span;
            }
            return mapping;
        }

        /// <summary>
        /// 对单元格执行智能表头匹配：优先方案A合并全部 Text 后匹配，
        /// 失败后方案B逐 Run 单独匹配。
        /// </summary>
        private string? MatchHeaderCell(W.TableCell cell)
        {
            string merged = GetMergedCellText(cell);
            if (!string.IsNullOrWhiteSpace(merged))
            {
                var result = _mappingService.MatchField(merged, _module);
                if (result != null) return result;
            }

            foreach (var paragraph in cell.Elements<W.Paragraph>())
            {
                foreach (var run in paragraph.Elements<W.Run>())
                {
                    var text = run.GetFirstChild<W.Text>();
                    if (text == null) continue;
                    string runText = text.Text ?? "";
                    if (string.IsNullOrWhiteSpace(runText)) continue;
                    var result = _mappingService.MatchField(runText, _module);
                    if (result != null) return result;
                }
            }

            return null;
        }

        /// <summary>
        /// 合并单元格内所有段落/所有Run的全部 Text
        /// </summary>
        private static string GetMergedCellText(W.TableCell cell)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var paragraph in cell.Elements<W.Paragraph>())
            {
                foreach (var run in paragraph.Elements<W.Run>())
                {
                    foreach (var text in run.Elements<W.Text>())
                    {
                        sb.Append(text.Text);
                    }
                }
            }
            return sb.ToString().Trim();
        }

        private string GetCellText(W.TableCell cell)
        {
            var paragraph = cell.GetFirstChild<W.Paragraph>();
            if (paragraph == null) return "";
            var run = paragraph.GetFirstChild<W.Run>();
            if (run == null) return "";
            var text = run.GetFirstChild<W.Text>();
            return text?.Text ?? "";
        }

        private int GetRowTotalCols(W.TableRow row)
        {
            int total = 0;
            foreach (var cell in row.Elements<W.TableCell>())
            {
                var gridSpan = cell.GetFirstChild<GridSpan>();
                int span = (gridSpan?.Val != null && int.TryParse(gridSpan.Val, out int s)) ? s : 1;
                total += span;
            }
            return total;
        }

        private Dictionary<string, List<byte[]>> ExtractEmbeddedImages(MainDocumentPart mainPart, List<W.Table> tables)
        {
            var result = new Dictionary<string, List<byte[]>>();

            try
            {
                for (int t = 0; t < tables.Count; t++)
                {
                    var rows = tables[t].Elements<W.TableRow>().ToList();
                    for (int r = 0; r < rows.Count; r++)
                    {
                        var cells = rows[r].Elements<W.TableCell>().ToList();
                        for (int c = 0; c < cells.Count; c++)
                        {
                            var drawings = cells[c].Descendants<W.Drawing>().ToList();
                            foreach (var drawing in drawings)
                            {
                                var blip = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
                                if (blip?.Embed == null) continue;

                                string embedId = blip.Embed.Value!;
                                var imagePart = (ImagePart?)mainPart.GetPartById(embedId);
                                if (imagePart == null) continue;

                                using var stream = imagePart.GetStream();
                                using var ms = new MemoryStream();
                                stream.CopyTo(ms);
                                string key = $"{t}_{r}_{c}";

                                if (!result.TryGetValue(key, out var list))
                                {
                                    list = new List<byte[]>();
                                    result[key] = list;
                                }
                                list.Add(ms.ToArray());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "提取 Word 嵌入图片时发生异常，跳过图片提取");
            }

            return result;
        }

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
    }
}
