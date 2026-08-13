// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;
using Serilog;
using System.IO;
using System.Windows;

namespace MS_SafetyManagementSystem.Helpers;

/// <summary>
/// PaddleOCR 服务 —— 使用 Sdcb.PaddleOCR（PaddleSharp）调用 PaddleOCR V5 ONNX 模型
/// 作为主 OCR 引擎，线程安全，支持懒加载初始化和资源释放
/// </summary>
public sealed class PaddleOcrService : IOcrService, IDisposable
{
    private PaddleOcrAll? _ocr;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public string Name => "PaddleOCR V5";

    /// <summary>
    /// 引擎是否已初始化。首次调用不会触发加载（只返回当前状态）
    /// </summary>
    public bool IsAvailable => _initialized && _ocr != null;

    /// <summary>
    /// 异步初始化引擎。可提前调用以预热加载模型，避免首次识别时卡顿
    /// </summary>
    public async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            Log.Information("PaddleOcrService: 正在加载 PaddleOCR V5 中文模型...");

            // 从 NuGet 嵌入的模型包中加载本地 V5 中文模型
            // 模型文件(~30MB) 首次使用时自动解压到缓存目录
            var model = LocalFullModels.ChineseV5;

            _ocr = new PaddleOcrAll(model, PaddleDevice.Mkldnn())
            {
                // 身份证照片基本都是横向正立，无需过度旋转检测
                AllowRotateDetection = true,
                // 180° 分类对身份证场景无意义，关闭以提升约 10% 速度
                Enable180Classification = false,
            };

            _initialized = true;
            Log.Information("PaddleOcrService: 模型加载完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PaddleOcrService: 模型加载失败");
            _initialized = false;
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<List<OcrTextBlock>> RecognizeAsync(string imagePath)
    {
        await EnsureInitializedAsync();

        if (_ocr == null)
            throw new InvalidOperationException("PaddleOCR 引擎未初始化");

        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
        {
            Log.Warning("PaddleOcrService: 图片文件不存在 {Path}", imagePath);
            return new List<OcrTextBlock>();
        }

        try
        {
            // PaddleOcrAll.Run 不是线程安全的，用信号量串行化
            await _runLock.WaitAsync();

            var fileName = Path.GetFileName(imagePath);
            Log.Information("PaddleOcrService: 开始识别 {Path}", fileName);

            // 使用 OpenCvSharp 读取图片（Sdcb.PaddleOCR 原生 API 基于 Mat）
            using var mat = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (mat == null || mat.Empty())
            {
                Log.Warning("PaddleOcrService: 图片读取失败 {Path}", imagePath);
                return new List<OcrTextBlock>();
            }

            // CPU 密集型操作，使用 Task.Run 避免阻塞 UI 线程
            PaddleOcrResult result = await Task.Run(() => _ocr.Run(mat));

            var blocks = result.Regions
                .Select(r =>
                {
                    var bbox = r.Rect.BoundingRect();
                    return new OcrTextBlock
                    {
                        Text = r.Text,
                        Score = r.Score,
                        BoundingBox = new System.Windows.Rect(bbox.X, bbox.Y, bbox.Width, bbox.Height),
                    };
                })
                .Where(b => !string.IsNullOrEmpty(b.Text))
                .ToList();

            Log.Information("PaddleOcrService: 识别完成，共 {Count} 行文字，全文：{Text}",
                blocks.Count, string.Join(" ", blocks.Select(b => b.Text)));

            return blocks;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PaddleOcrService: 识别失败 {Path}", imagePath);
            return new List<OcrTextBlock>();
        }
        finally
        {
            if (_runLock.CurrentCount == 0)
                _runLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ocr?.Dispose();
        _ocr = null;
        _initLock.Dispose();
        _runLock.Dispose();
    }
}
