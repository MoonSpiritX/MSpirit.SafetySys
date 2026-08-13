// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace MS_SafetyManagementSystem.Helpers;

/// <summary>
/// OCR 服务接口，支持多种 OCR 引擎实现（PaddleOCR / Windows OCR）
/// 调用方只依赖此接口，运行时按优先级选择具体实现
/// </summary>
public interface IOcrService
{
    /// <summary>服务名称（日志/调试用）</summary>
    string Name { get; }

    /// <summary>是否可用（引擎初始化成功）</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 识别图片中的文字，返回按行排列的文字块
    /// </summary>
    /// <param name="imagePath">图片文件绝对路径</param>
    /// <returns>文字块列表，每块对应一行文字</returns>
    Task<List<OcrTextBlock>> RecognizeAsync(string imagePath);
}
