// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using Serilog;

namespace MS_SafetyManagementSystem.Helpers;

/// <summary>
/// Windows OCR 适配器 —— 包装现有的静态 <see cref="OcrService"/>
/// 作为 PaddleOCR 不可用时的降级方案
/// </summary>
public class WindowsOcrService : IOcrService
{
    public string Name => "Windows.Media.Ocr";

    public bool IsAvailable => OcrService.IsInitialized;

    public async Task<List<OcrTextBlock>> RecognizeAsync(string imagePath)
    {
        if (!IsAvailable)
        {
            Log.Warning("WindowsOcrService: 引擎尚未初始化，调用后将自动初始化");
        }

        return await OcrService.RecognizeAsync(imagePath);
    }
}
