// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Drawing.Pictures;
using MS_SafetyManagementSystem.Services;
using Serilog;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using W = DocumentFormat.OpenXml.Wordprocessing;
using DW = DocumentFormat.OpenXml.Drawing;
using DWp = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using Pic = DocumentFormat.OpenXml.Drawing.Pictures;

namespace MS_SafetyManagementSystem.Export
{
    public class WordSmartExporter
    {
        private readonly MappingDictionaryService _mappingService;
        private readonly string _attachmentsRootPath;
        private readonly string _module;
        private IExportDataProvider _dataProvider = null!;

        public WordSmartExporter(MappingDictionaryService mappingService, string attachmentsRootPath, string module = "Hazard")
        {
            _mappingService = mappingService;
            _attachmentsRootPath = attachmentsRootPath;
            _module = module;
        }

        public (bool Success, string? ErrorMessage) Export(string templatePath, string outputPath, IExportDataProvider dataProvider, string mode)
        {
            _dataProvider = dataProvider;

            if (!global::System.IO.File.Exists(templatePath))
            {
                return (false, "模板文件不存在");
            }
            if (_dataProvider == null || _dataProvider.RowCount == 0)
            {
                return (false, "没有可导出的数据");
            }

            try
            {
                Log.Information("开始加载模板文件: {TemplatePath}", templatePath);
                global::System.IO.File.Copy(templatePath, outputPath, true);

                using (var document = WordprocessingDocument.Open(outputPath, true))
                {
                    var mainPart = document.MainDocumentPart!;
                    var body = mainPart.Document!.Body!;
                    if (body == null) throw new InvalidOperationException("文档主体为空");

                    var tables = body.Elements<W.Table>().ToList();
                    if (tables.Count == 0) throw new InvalidOperationException("模板中没有表格");

                    int nextNumId = 1;
                    foreach (var table in tables)
                    {
                        ProcessTable(table, mainPart, _dataProvider, mode, ref nextNumId);
                    }

                    mainPart.Document.Save();
                    Log.Information("文档保存成功");
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "导出过程中发生异常");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// 处理整个表格，找到所有独立的块（表头+数据行）并分别处理
        /// </summary>
        private void ProcessTable(W.Table table, MainDocumentPart mainPart, IExportDataProvider dataProvider, string mode, ref int nextNumId)
        {
            var allRows = table.Elements<W.TableRow>().ToList();
            if (allRows.Count == 0) return;

            // 设置表格为固定列宽布局，禁止自动撑宽列
            var tblPr = table.Elements<W.TableProperties>().FirstOrDefault();
            if (tblPr == null)
            {
                tblPr = new W.TableProperties();
                table.PrependChild(tblPr);
            }
            // 移除旧 TableLayout 后追加新值（若已存在则替换）
            var existingLayout = tblPr.GetFirstChild<W.TableLayout>();
            if (existingLayout != null)
                existingLayout.Remove();
            tblPr.AppendChild(new W.TableLayout { Type = W.TableLayoutValues.Fixed });

            // 1. 找出所有表头行的索引（匹配字段数 >= 2 的行）
            var headerIndices = new List<int>();
            for (int i = 0; i < allRows.Count; i++)
            {
                if (IsHeaderRow(allRows[i]))
                {
                    headerIndices.Add(i);
                }
            }

            if (headerIndices.Count == 0)
            {
                Log.Warning("表格中未识别到任何表头，跳过处理");
                return;
            }

            // 2. 从后往前处理每个表头块，避免删除行导致索引变化
            for (int idx = headerIndices.Count - 1; idx >= 0; idx--)
            {
                int headerRowIndex = headerIndices[idx];
                int nextHeaderIndex = (idx + 1 < headerIndices.Count) ? headerIndices[idx + 1] : allRows.Count;
                int dataEndIndex = nextHeaderIndex;

                var headerRow = allRows[headerRowIndex];
                int headerTotalCols = GetRowTotalCols(headerRow);

                var rowsToDeleteIndices = new List<int>();
                for (int i = headerRowIndex + 1; i < dataEndIndex; i++)
                {
                    int totalCols = GetRowTotalCols(allRows[i]);
                    if (totalCols == headerTotalCols)
                    {
                        rowsToDeleteIndices.Add(i);
                    }
                    else
                    {
                        // 遇到结构不同的行，停止收集
                        break;
                    }
                }

                // 只有存在结构匹配的数据行时，才处理该块（防止不规范表格误操作）
                if (rowsToDeleteIndices.Count > 0)
                {
                    int numId = nextNumId++;
                    EnsureNumbering(mainPart, numId, numId);
                    ProcessTableBlock(table, mainPart, dataProvider, mode, headerRowIndex, rowsToDeleteIndices, numId);
                }
                else
                {
                    Log.Information("表头行下方没有结构匹配的数据行，跳过处理");
                }
            }
        }


        private bool IsHeaderRow(W.TableRow row)
        {
            int matchCount = 0;
            foreach (var cell in row.Elements<W.TableCell>())
            {
                if (MatchHeaderCell(cell) != null)
                {
                    matchCount++;
                    if (matchCount >= 2)
                        return true;
                }
            }
            return false;
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
                else
                {
                    string headerText = GetCellText(cell);
                    Log.Information("列索引 {ColIndex} 未匹配: {HeaderText}", colIndex, headerText);
                }

                var gridSpan = cell.GetFirstChild<W.GridSpan>();
                int span = 1;
                if (gridSpan?.Val != null && int.TryParse(gridSpan.Val, out int parsed))
                    span = parsed;
                colIndex += span;
            }
            return mapping;
        }

        private void ProcessTableBlock(W.Table table, MainDocumentPart mainPart, IExportDataProvider dataProvider, string mode, int headerRowIndex, List<int> rowsToDeleteIndices, int numId)
        {
            var allRows = table.Elements<W.TableRow>().ToList();
            var headerRow = allRows[headerRowIndex];
            var columnMapping = BuildColumnMappingForRow(headerRow);
            if (columnMapping.Count == 0)
            {
                Log.Warning("表格块表头列映射为空，跳过处理");
                return;
            }

            bool hasTextField = false;
            bool hasPhotoField = false;
            foreach (var field in columnMapping.Values)
            {
                if (field == "HazardPhoto" || field == "RemedyPhoto")
                    hasPhotoField = true;
                else if (field != "RowNumber" && !string.IsNullOrEmpty(field))
                    hasTextField = true;
            }

            if (mode == "NoImage" && !hasTextField)
            {
                Log.Information("文本模式下跳过纯图片表格块");
                return;
            }
            if (mode == "OnlyImage" && !hasPhotoField)
            {
                Log.Information("图片模式下跳过纯文本表格块");
                return;
            }

            W.TableRow templateRow;
            if (rowsToDeleteIndices.Count > 0)
            {
                templateRow = (W.TableRow)allRows[rowsToDeleteIndices[0]].CloneNode(true);
            }
            else
            {
                templateRow = CreateDataRowFromHeader(headerRow);
            }

            foreach (var idx in rowsToDeleteIndices.OrderByDescending(i => i))
            {
                var rowToRemove = allRows[idx];
                rowToRemove.Remove();
            }

            allRows = table.Elements<W.TableRow>().ToList();
            var currentHeaderRow = allRows[headerRowIndex];

            var headerCells = currentHeaderRow.Elements<W.TableCell>().ToList();
            var cellFieldMap = new List<(int startCol, int span, string? field)>();
            int colAcc = 0;
            foreach (var cell in headerCells)
            {
                var gridSpan = cell.GetFirstChild<W.GridSpan>();
                int span = (gridSpan?.Val != null && int.TryParse(gridSpan.Val, out int s)) ? s : 1;
                string? field = null;
                if (columnMapping.TryGetValue(colAcc, out var mappedField))
                    field = mappedField;
                cellFieldMap.Add((colAcc, span, field));
                colAcc += span;
            }

            // 预先准备所有新行，避免多次插入导致 UI 卡死
            var newRows = new List<W.TableRow>();
            for (int i = 0; i < dataProvider.RowCount; i++)
            {
                var newRow = (W.TableRow)templateRow!.CloneNode(true);
                newRows.Add(newRow);
            }

            // 先删除旧行（上面已删），然后批量插入新行
            W.TableRow? lastInsertedRow = null;
            for (int i = 0; i < newRows.Count; i++)
            {
                var newRow = newRows[i];
                if (i == 0)
                    table.InsertAfter(newRow, currentHeaderRow);
                else
                    table.InsertAfter(newRow, lastInsertedRow);
                lastInsertedRow = newRow;
            }

            // 现在填充数据（每个新行单独处理）
            for (int rowIdx = 0; rowIdx < newRows.Count; rowIdx++)
            {
                var newRow = newRows[rowIdx];

                var newCells = newRow.Elements<W.TableCell>().ToList();

                int cellIdx = 0;
                foreach (var cell in newCells)
                {
                    var paragraph = cell.GetFirstChild<W.Paragraph>();
                    if (paragraph == null) continue;

                    string? field = null;
                    int startCol = 0;
                    for (int j = 0; j < cellFieldMap.Count; j++)
                    {
                        var entry = cellFieldMap[j];
                        if (cellIdx == j)
                        {
                            field = entry.field;
                            startCol = entry.startCol;
                            break;
                        }
                    }

                    var runs = paragraph.Elements<W.Run>().ToList();
                    foreach (var run in runs)
                    {
                        var texts = run.Elements<W.Text>().ToList();
                        foreach (var t in texts) t.Remove();
                        var drawings = run.Elements<W.Drawing>().ToList();
                        foreach (var d in drawings) d.Remove();
                    }

                    if (!paragraph.Elements<W.Run>().Any())
                    {
                        var emptyRun = new W.Run();
                        emptyRun.AppendChild(new W.Text(""));
                        paragraph.AppendChild(emptyRun);
                    }

                    cellIdx++;
                }

                foreach (var kv in columnMapping)
                {
                    int colIndex = kv.Key;
                    string dataField = kv.Value;
                    var cells = newRow.Elements<W.TableCell>().ToList();
                    W.TableCell? targetCell = null;
                    int curCol = 0;
                    foreach (var cell in cells)
                    {
                        var gridSpan = cell.GetFirstChild<W.GridSpan>();
                        int span = (gridSpan?.Val != null && int.TryParse(gridSpan.Val, out int s)) ? s : 1;
                        if (curCol <= colIndex && colIndex < curCol + span)
                        {
                            targetCell = cell;
                            break;
                        }
                        curCol += span;
                    }
                    if (targetCell == null) continue;

                    var paragraph = targetCell.GetFirstChild<W.Paragraph>() ?? targetCell.AppendChild(new W.Paragraph());
                    var run = paragraph.GetFirstChild<W.Run>() ?? paragraph.AppendChild(new W.Run());

                    if (dataField == "HazardPhoto" || dataField == "RemedyPhoto")
                    {
                        if (mode == "Full" || mode == "OnlyImage")
                        {
                            var photoPaths = dataProvider.GetPhotoPaths(rowIdx, dataField, _attachmentsRootPath);
                            if (photoPaths.Count > 0)
                            {
                                int? cellWidthDxa = GetCellWidthInDxa(targetCell);
                                // 保存对齐属性，先移除原有 pPr，再追加克隆副本（避免重复 pPr 导致格式丢失）
                                var savedPPr = paragraph.GetFirstChild<W.ParagraphProperties>();
                                var existingPPr = savedPPr != null ? (W.ParagraphProperties)savedPPr.CloneNode(true) : null;
                                if (savedPPr != null) savedPPr.Remove();
                                var runs = paragraph.Elements<W.Run>().ToList();
                                foreach (var r in runs) r.Remove();
                                if (existingPPr != null)
                                    paragraph.AppendChild(existingPPr);
                                for (int i = 0; i < photoPaths.Count; i++)
                                {
                                    var imgRun = new W.Run();
                                    InsertPictureIntoCell(mainPart, imgRun, targetCell, photoPaths[i], cellWidthDxa);
                                    paragraph.AppendChild(imgRun);
                                    if (i < photoPaths.Count - 1)
                                    {
                                        paragraph.AppendChild(new W.Break());
                                    }
                                }
                            }
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
                        // 使用自动编号：纯数字 "1, 2, 3"，居中，无缩进
                        // 保留模板中的段落属性（含段落级字号、行距等）
                        var oldPPr = paragraph.GetFirstChild<W.ParagraphProperties>();
                        W.ParagraphProperties mergedPPr;
                        if (oldPPr != null)
                        {
                            mergedPPr = (W.ParagraphProperties)oldPPr.CloneNode(true);
                            // 移除旧的编号设置，避免冲突
                            var oldNumProps = mergedPPr.GetFirstChild<W.NumberingProperties>();
                            if (oldNumProps != null)
                                mergedPPr.RemoveChild(oldNumProps);
                        }
                        else
                        {
                            mergedPPr = new W.ParagraphProperties();
                        }

                        mergedPPr.AppendChild(new W.NumberingProperties(
                            new W.NumberingLevelReference() { Val = 0 },
                            new W.NumberingId() { Val = numId }
                        ));
                        mergedPPr.AppendChild(new W.Justification() { Val = W.JustificationValues.Center });
                        mergedPPr.AppendChild(new W.Indentation() { Left = "0", FirstLine = "0" });

                        // 保留模板中的运行级字号
                        W.RunProperties? fontProps = null;
                        var firstRun = paragraph.GetFirstChild<W.Run>();
                        if (firstRun != null)
                        {
                            var rPr = firstRun.GetFirstChild<W.RunProperties>();
                            if (rPr != null)
                                fontProps = (W.RunProperties)rPr.CloneNode(true);
                        }

                        paragraph.RemoveAllChildren();
                        paragraph.AppendChild(mergedPPr);
                        var newRun = new W.Run(new W.Text(""));
                        if (fontProps != null)
                            newRun.AppendChild(fontProps);
                        paragraph.AppendChild(newRun);
                        continue;
                    }

                    string textValue = dataProvider.GetTextValue(rowIdx, dataField);
                    var textChildren = run.Elements<W.Text>().ToList();
                    foreach (var t in textChildren) t.Remove();
                    var text = new W.Text(textValue);
                    run.AppendChild(text);
                }
            }
        }

        /// <summary>
        /// 确保文档中存在指定 numId 的自动编号定义（纯数字 "1, 2, 3"，无点号、无缩进）
        /// </summary>
        private void EnsureNumbering(MainDocumentPart mainPart, int abstractNumId, int numId)
        {
            var numberingPart = mainPart.NumberingDefinitionsPart;
            if (numberingPart == null)
            {
                numberingPart = mainPart.AddNewPart<NumberingDefinitionsPart>();
                numberingPart.Numbering = new W.Numbering();
            }

            var numbering = numberingPart.Numbering!;

            // 检查 numId 是否已存在
            bool exists = numbering.Elements<W.NumberingInstance>().Any(n => n.NumberID?.Value == numId);
            if (exists) return;

            // 创建抽象编号："1, 2, 3" 格式，无点号后缀
            var abstractNum = new W.AbstractNum(
                new W.Level(
                    new W.StartNumberingValue() { Val = 1 },
                    new W.NumberingFormat() { Val = W.NumberFormatValues.Decimal },
                    new W.LevelText() { Val = "%1" },   // 纯数字，不加点号
                    new W.ParagraphProperties(
                        new W.Indentation() { Left = "0", FirstLine = "0" },
                        new W.Justification() { Val = W.JustificationValues.Center }
                    ),
                    new W.NumberingSymbolRunProperties()
                )
            )
            {
                AbstractNumberId = abstractNumId
            };
            // 只设置 Level 0，不设多级编号
            numbering.AppendChild(abstractNum);

            // 创建编号实例，指向该抽象编号
            var numberingInstance = new W.NumberingInstance(
                new W.AbstractNumId() { Val = abstractNumId }
            )
            {
                NumberID = numId
            };
            numbering.AppendChild(numberingInstance);
        }

        private int GetRowTotalCols(W.TableRow row)
        {
            int total = 0;
            foreach (var cell in row.Elements<W.TableCell>())
            {
                var gridSpan = cell.GetFirstChild<W.GridSpan>();
                int span = (gridSpan?.Val != null && int.TryParse(gridSpan.Val, out int s)) ? s : 1;
                total += span;
            }
            return total;
        }

        private W.TableRow CreateDataRowFromHeader(W.TableRow headerRow)
        {
            var newRow = new W.TableRow();
            foreach (var headerCell in headerRow.Elements<W.TableCell>())
            {
                var newCell = (W.TableCell)headerCell.CloneNode(false);
                newCell.RemoveAllChildren<W.Paragraph>();

                var paragraph = new W.Paragraph();
                var run = new W.Run();

                var headerParagraph = headerCell.GetFirstChild<W.Paragraph>();
                W.RunProperties? baseRunProps = null;
                if (headerParagraph != null)
                {
                    var headerRun = headerParagraph.GetFirstChild<W.Run>();
                    if (headerRun != null)
                        baseRunProps = headerRun.RunProperties?.CloneNode(true) as W.RunProperties;
                }

                var runProps = new W.RunProperties();
                if (baseRunProps != null)
                {
                    foreach (var element in baseRunProps.ChildElements)
                    {
                        if (!(element is W.Bold))
                            runProps.AppendChild((OpenXmlElement)element.CloneNode(true));
                    }
                }
                runProps.AppendChild(new W.Bold { Val = false });
                run.RunProperties = runProps;

                run.AppendChild(new W.Text(""));
                paragraph.AppendChild(run);
                newCell.AppendChild(paragraph);
                newRow.AppendChild(newCell);
            }
            return newRow;
        }

        /// <summary>
        /// 对单元格执行智能表头匹配：优先方案A合并全部 Text 后匹配，
        /// 失败后方案B逐 Run 单独匹配。
        /// </summary>
        private string? MatchHeaderCell(W.TableCell cell)
        {
            // 方案 A：合并单元格内全部 Text 后匹配
            string merged = GetMergedCellText(cell);
            if (!string.IsNullOrWhiteSpace(merged))
            {
                var result = _mappingService.MatchField(merged, _module);
                if (result != null) return result;
            }

            // 方案 B：每个 Run 的 Text 分别匹配
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
        /// 合并单元格内所有段落/所有Run的全部 Text（修复跨 Run 拆分问题）
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

        private int? GetCellWidthInDxa(W.TableCell cell)
        {
            var tcPr = cell.GetFirstChild<W.TableCellProperties>();
            if (tcPr != null)
            {
                var tcW = tcPr.GetFirstChild<W.TableCellWidth>();
                if (tcW != null && tcW.Type != null && tcW.Type.Value == TableWidthUnitValues.Dxa && int.TryParse(tcW.Width, out int width))
                    return width;
            }
            return null;
        }

        private byte[]? CompressImageForWord(string imagePath, int? targetCellWidthDxa, double safeFactor = 2, int quality = 85)
        {
            using var srcBitmap = SKBitmap.Decode(imagePath);
            if (srcBitmap == null) return null;

            int targetWidthPx;
            const int defaultWidthPx = 800;
            const int maxWidthPx = 2000;

            if (targetCellWidthDxa.HasValue && targetCellWidthDxa.Value > 0)
            {
                double inches = targetCellWidthDxa.Value / 1440.0;
                double pixelNeeded = inches * 96 * safeFactor;
                targetWidthPx = (int)Math.Round(pixelNeeded);
                if (targetWidthPx < 100) targetWidthPx = 100;
                if (targetWidthPx > maxWidthPx) targetWidthPx = maxWidthPx;
            }
            else
            {
                targetWidthPx = defaultWidthPx;
            }

            if (srcBitmap.Width <= targetWidthPx)
            {
                using var image = SKImage.FromBitmap(srcBitmap);
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
                return data.ToArray();
            }

            float ratio = (float)targetWidthPx / srcBitmap.Width;
            int newHeight = (int)(srcBitmap.Height * ratio);
            if (newHeight < 1) newHeight = 1;

            var samplingOptions = new SKSamplingOptions(SKCubicResampler.Mitchell);
            using var resized = srcBitmap.Resize(new SKImageInfo(targetWidthPx, newHeight), samplingOptions);
            if (resized == null) return null;
            using var imageResized = SKImage.FromBitmap(resized);
            using var dataResized = imageResized.Encode(SKEncodedImageFormat.Jpeg, quality);
            return dataResized.ToArray();
        }

        private void InsertPictureIntoCell(MainDocumentPart mainPart, W.Run run, W.TableCell cell, string imagePath, int? cellWidthDxa = null)
        {
            byte[]? compressed = CompressImageForWord(imagePath, cellWidthDxa);
            if (compressed == null) return;

            using var stream = new MemoryStream(compressed);
            var imagePart = mainPart.AddImagePart(ImagePartType.Jpeg);
            imagePart.FeedData(stream);

            int? targetWidthDxa = cellWidthDxa ?? GetCellWidthInDxa(cell);
            int displayWidthDxa = targetWidthDxa ?? 4500;
            int finalWidthDxa = (int)(displayWidthDxa * 0.95);
            int widthEmu = finalWidthDxa * 635;

            using var bitmap = SKBitmap.Decode(compressed);
            int imgWidthPx = bitmap.Width;
            int imgHeightPx = bitmap.Height;
            int heightEmu = (int)((double)widthEmu / imgWidthPx * imgHeightPx);
            if (heightEmu <= 0) heightEmu = widthEmu;

            string relationshipId = mainPart.GetIdOfPart(imagePart);

            var element = new W.Drawing(
                new DWp.Inline(
                    new DWp.Extent() { Cx = widthEmu, Cy = heightEmu },
                    new DWp.EffectExtent() { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                    new DWp.DocProperties() { Id = (uint)mainPart.ImageParts.Count(), Name = "Picture" },
                    new DWp.NonVisualGraphicFrameDrawingProperties(),
                    new DW.Graphic(
                        new DW.GraphicData(
                            new Pic.Picture(
                                new Pic.NonVisualPictureProperties(
                                    new Pic.NonVisualDrawingProperties() { Id = 0U, Name = "Picture" },
                                    new Pic.NonVisualPictureDrawingProperties()
                                ),
                                new Pic.BlipFill(
                                    new Blip() { Embed = relationshipId },
                                    new Stretch(new FillRectangle())
                                ),
                                new Pic.ShapeProperties(
                                    new Transform2D(
                                        new Offset() { X = 0L, Y = 0L },
                                        new Extents() { Cx = widthEmu, Cy = heightEmu }
                                    ),
                                    new PresetGeometry(new AdjustValueList()) { Preset = ShapeTypeValues.Rectangle }
                                )
                            )
                        )
                        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }
                    )
                )
                { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U }
            );

            run.RemoveAllChildren();
            run.AppendChild(element);
        }
    }
}