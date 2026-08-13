// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace MS_SafetyManagementSystem.Models
{
    public class Attachment
    {
        public string Id { get; set; } = "";
        public string RecordId { get; set; } = "";
        public string FileName { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public long? FileSize { get; set; }
        public string? MimeType { get; set; }
        public string? UploadedAt { get; set; }
        public string AttachmentType { get; set; } = "";   // "Hazard" / "Remedy" / "IDCard" / "Certificate"
        public int DisplayOrder { get; set; }              // 1-3 (Hazard), 1-2 (IDCard), 1-5 (Certificate)

        /// <summary>
        /// 缩略图相对路径（相对于项目附件根目录）
        /// </summary>
        public string? ThumbnailPath { get; set; }

        /// <summary>证书编号（仅 Certificate 类型使用）</summary>
        public string? CertNumber { get; set; }

        /// <summary>证书名称（仅 Certificate 类型使用）</summary>
        public string? CertName { get; set; }

        /// <summary>证书有效期起（仅 Certificate 类型使用）</summary>
        public string? CertStart { get; set; }

        /// <summary>证书有效期止（仅 Certificate 类型使用）</summary>
        public string? CertEnd { get; set; }
    }
}