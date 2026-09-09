// Word 测试基础设施：程序化生成带表格的 .docx（OpenXml SDK）
// 用途：WordSmartImporter / WordSmartExporter 测试的动态模板/文档构造，零二进制资产入库

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.IO;
using W = DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace MSpirit.SafetySys.Tests;

/// <summary>Word 测试文档构造器</summary>
internal static class WordDocBuilder
{
    /// <summary>
    /// 创建空白 docx，填充逻辑由 fill 完成（可追加表格/图片关系），返回文件路径。
    /// </summary>
    public static string CreateDoc(string dir, Action<MainDocumentPart, Body> fill)
    {
        string file = Path.Combine(dir, $"{Guid.NewGuid():N}.docx");
        using (var doc = WordprocessingDocument.Create(file, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();
            fill(mainPart, body);
            mainPart.Document = new Document(body);
            doc.Save();
        }
        return file;
    }

    /// <summary>创建含若干表格的文档</summary>
    public static string CreateDocWithTables(string dir, params W.Table[] tables)
    {
        return CreateDoc(dir, (main, body) =>
        {
            foreach (var t in tables) body.Append(t);
        });
    }

    /// <summary>创建表格（自动生成 TableGrid）</summary>
    public static W.Table CreateTable(params W.TableRow[] rows)
    {
        var table = new W.Table();
        table.AppendChild(new W.TableProperties(
            new W.TableWidth() { Type = TableWidthUnitValues.Dxa, Width = "5000" }));
        table.AppendChild(new W.TableGrid());
        foreach (var row in rows) table.AppendChild(row);
        return table;
    }

    /// <summary>创建一行（单元格数由 texts 决定）</summary>
    public static W.TableRow CreateRow(params string[] texts)
    {
        var row = new W.TableRow();
        foreach (var t in texts) row.AppendChild(TextCell(t));
        return row;
    }

    /// <summary>创建一行（直接接收已构造的单元格，如图片单元格）</summary>
    public static W.TableRow CreateRow(params W.TableCell[] cells)
    {
        var row = new W.TableRow();
        foreach (var c in cells) row.AppendChild(c);
        return row;
    }

    /// <summary>创建一行，指定某列的 GridSpan 跨列数</summary>
    public static W.TableRow CreateRowWithSpan(int spanColumnIndex, int span, params string[] texts)
    {
        var row = new W.TableRow();
        for (int i = 0; i < texts.Length; i++)
        {
            var cell = TextCell(texts[i]);
            if (i == spanColumnIndex && span > 1)
            {
                cell.GetFirstChild<W.TableCellProperties>()!.AppendChild(new W.GridSpan() { Val = span });
            }
            row.AppendChild(cell);
        }
        return row;
    }

    /// <summary>文本单元格</summary>
    public static W.TableCell TextCell(string text)
    {
        var cell = new W.TableCell();
        cell.AppendChild(new W.TableCellProperties());
        var paragraph = new W.Paragraph();
        var run = new W.Run();
        run.AppendChild(new W.Text(text ?? ""));
        paragraph.AppendChild(run);
        cell.AppendChild(paragraph);
        return cell;
    }

    /// <summary>添加图片到文档并返回关系 ID</summary>
    public static string AddImage(MainDocumentPart mainPart, byte[] imageBytes, string contentType = "image/png")
    {
        var imagePart = mainPart.AddImagePart(contentType);
        using var ms = new MemoryStream(imageBytes);
        imagePart.FeedData(ms);
        return mainPart.GetIdOfPart(imagePart);
    }

    /// <summary>构造内联图片 Drawing 元素</summary>
    public static W.Drawing MakeDrawing(string relationshipId, long widthEmu = 100000, long heightEmu = 100000)
    {
        return new W.Drawing(
            new DW.Inline(
                new DW.Extent() { Cx = widthEmu, Cy = heightEmu },
                new DW.EffectExtent() { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties() { Id = 1U, Name = "Picture" },
                new DW.NonVisualGraphicFrameDrawingProperties(),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties() { Id = 0U, Name = "Picture" },
                                new PIC.NonVisualPictureDrawingProperties()
                            ),
                            new PIC.BlipFill(
                                new A.Blip() { Embed = relationshipId },
                                new A.Stretch(new A.FillRectangle())
                            ),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset() { X = 0L, Y = 0L },
                                    new A.Extents() { Cx = widthEmu, Cy = heightEmu }
                                ),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }
                            )
                        )
                    )
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }
                )
            )
            { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U }
        );
    }

    /// <summary>嵌入图片单元格（inline drawing）</summary>
    public static W.TableCell ImageCell(string relationshipId, long widthEmu = 100000, long heightEmu = 100000)
    {
        var cell = new W.TableCell();
        cell.AppendChild(new W.TableCellProperties());
        var paragraph = new W.Paragraph();
        var run = new W.Run();
        run.AppendChild(MakeDrawing(relationshipId, widthEmu, heightEmu));
        paragraph.AppendChild(run);
        cell.AppendChild(paragraph);
        return cell;
    }

    /// <summary>读取文档全部表格单元格文本（合并所有 Run/Text）</summary>
    public static List<List<string>> ReadTableTexts(string docxPath)
    {
        var result = new List<List<string>>();
        using var doc = WordprocessingDocument.Open(docxPath, false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        foreach (var table in body.Elements<W.Table>())
        {
            var rows = new List<string>();
            foreach (var row in table.Elements<W.TableRow>())
            {
                var sb = new System.Text.StringBuilder();
                foreach (var cell in row.Elements<W.TableCell>())
                {
                    sb.Append(GetMergedText(cell));
                    sb.Append('|');
                }
                rows.Add(sb.ToString().TrimEnd('|'));
            }
            result.Add(rows);
        }
        return result;
    }

    private static string GetMergedText(W.TableCell cell)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var paragraph in cell.Elements<W.Paragraph>())
        {
            foreach (var run in paragraph.Elements<W.Run>())
            {
                foreach (var text in run.Elements<W.Text>())
                    sb.Append(text.Text);
            }
        }
        return sb.ToString().Trim();
    }
}
