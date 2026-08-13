// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Serialization;

namespace MSpirit.SafetySys.Updater;

/// <summary>
/// .msupd 更新包清单（manifest.json）。
/// 更新包 = 标准 zip 容器，根目录放 manifest.json，其余为程序文件（相对程序文件夹的路径）。
/// 注意：与项目存档 .msarch 是两套格式，切勿混淆。
/// </summary>
public sealed class UpdateManifest
{
    /// <summary>包格式版本（当前 1）</summary>
    public int FormatVersion { get; set; } = 1;

    /// <summary>此更新包对应的应用版本号（如 1.0.0.1），用于防降级/重复更新判断</summary>
    public string AppVersion { get; set; } = string.Empty;

    /// <summary>生成时间（yyyy-MM-dd HH:mm:ss）</summary>
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>全量文件清单：包内所有程序文件（相对路径用 / 分隔）</summary>
    public List<ManifestFileEntry> Files { get; set; } = new();

    /// <summary>需要从程序文件夹删除的旧文件（相对路径用 / 分隔）；对账删除兜底</summary>
    public List<string> DeletedFiles { get; set; } = new();
}

/// <summary>清单中的单个文件条目</summary>
public sealed class ManifestFileEntry
{
    /// <summary>相对程序文件夹的路径，统一用 / 分隔（如 MSpirit.SafetySys.dll、Resources/lan_page.html）</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>文件大小（字节），用于快速校验</summary>
    public long Size { get; set; }

    /// <summary>SHA256 十六进制小写，用于完整性校验</summary>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>
/// System.Text.Json 源生成器上下文。
/// NativeAOT + 全量裁剪下，反射式 JSON 序列化会被裁剪掉，必须用源生成器才能正常工作。
/// </summary>
[JsonSerializable(typeof(UpdateManifest))]
[JsonSerializable(typeof(List<ManifestFileEntry>))]
public partial class UpdaterJsonContext : JsonSerializerContext
{
}
