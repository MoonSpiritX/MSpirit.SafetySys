// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using Serilog;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace MS_SafetyManagementSystem.Helpers
{
    public static class OcrService
    {
        private static OcrEngine? _ocrEngine = null;
        private static readonly object _initLock = new();

        public static bool IsInitialized => _ocrEngine != null;

        private static void EnsureEngine()
        {
            if (_ocrEngine != null) return;
            lock (_initLock)
            {
                if (_ocrEngine != null) return;
                Log.Information("WindowsOCR: 初始化中文 OCR 引擎...");
                var lang = new Language("zh-CN");
                _ocrEngine = OcrEngine.TryCreateFromLanguage(lang);
                if (_ocrEngine == null)
                {
                    // 回退到系统语言
                    _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
                    Log.Warning("WindowsOCR: 中文语言包不可用，使用系统默认语言");
                }
                Log.Information("WindowsOCR: 初始化完成，可识别语言: {Lang}", _ocrEngine.RecognizerLanguage);
            }
        }

        public static async Task<List<OcrTextBlock>> RecognizeAsync(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                Log.Warning("WindowsOCR: 图片文件不存在 {Path}", imagePath);
                return new List<OcrTextBlock>();
            }

            try
            {
                EnsureEngine();

                // 读取图片原始字节
                byte[] fileBytes = await File.ReadAllBytesAsync(imagePath);

                // ===== SkiaSharp 预处理：小图放大 + 轻度锐化 =====
                byte[] processedBytes;
                using (var skBitmap = SKBitmap.Decode(fileBytes))
                {
                    if (skBitmap == null) throw new InvalidOperationException("图片解码失败");

                    // 1. 小图（<500px）先 2x 线性放大
                    SKBitmap working = skBitmap;
                    if (skBitmap.Width < 500 || skBitmap.Height < 500)
                    {
                        int newW = skBitmap.Width * 2;
                        int newH = skBitmap.Height * 2;
                        var resized = skBitmap.Resize(new SKImageInfo(newW, newH),
                            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                        if (resized != null)
                            working = resized;
                    }

                    // 2. 轻度锐化卷积核 [0,-1,0; -1,5,-1; 0,-1,0]
                    var kernel = new float[] { 0, -1, 0, -1, 5, -1, 0, -1, 0 };
                    using var sharpened = new SKBitmap(working.Width, working.Height);
                    using var canvas = new SKCanvas(sharpened);
                    using var filter = SKImageFilter.CreateMatrixConvolution(
                        new SKSizeI(3, 3), kernel, 1.0f, 0, new SKPointI(0, 0), SKShaderTileMode.Clamp, true);
                    using var paint = new SKPaint { ImageFilter = filter };
                    canvas.DrawBitmap(working, 0, 0, paint);
                    canvas.Flush();

                    // 3. 编码为 PNG
                    using var pngData = sharpened.Encode(SKEncodedImageFormat.Png, 100);
                    processedBytes = pngData.ToArray();

                    if (working != skBitmap) working.Dispose();
                }

                // 将预处理后的 PNG 送入 Windows OCR
                using var memStream = new InMemoryRandomAccessStream();
                using var writer = new DataWriter(memStream);
                writer.WriteBytes(processedBytes);
                await writer.StoreAsync();
                memStream.Seek(0);
                var decoder = await BitmapDecoder.CreateAsync(memStream);
                var bitmap = await decoder.GetSoftwareBitmapAsync();

                Log.Information("WindowsOCR: 开始识别 {Path} ({Width}x{Height})",
                    Path.GetFileName(imagePath), bitmap.PixelWidth, bitmap.PixelHeight);

                var result = await _ocrEngine!.RecognizeAsync(bitmap);

                var blocks = result.Lines
                    .Select(l =>
                    {
                        // 合并该行所有 word 的 bounding rect 为该行的整体区域
                        double minX = double.MaxValue, minY = double.MaxValue;
                        double maxX = double.MinValue, maxY = double.MinValue;
                        foreach (var word in l.Words)
                        {
                            var r = word.BoundingRect;
                            if (r.X < minX) minX = r.X;
                            if (r.Y < minY) minY = r.Y;
                            if (r.X + r.Width > maxX) maxX = r.X + r.Width;
                            if (r.Y + r.Height > maxY) maxY = r.Y + r.Height;
                        }
                        return new OcrTextBlock
                        {
                            // Windows OCR 输出每个字符用空格分隔，去除空格合并成完整文本
                            Text = Regex.Replace(l.Text, @"\s+", ""),
                            Score = 1.0f,
                            BoundingBox = new System.Windows.Rect(
                                minX, minY, maxX - minX, maxY - minY),
                        };
                    })
                    .Where(b => !string.IsNullOrEmpty(b.Text))
                    .ToList();

                Log.Information("WindowsOCR: 识别完成，共 {Count} 行文字，全文：{Text}",
                    blocks.Count, string.Join(" ", blocks.Select(b => b.Text)));

                return blocks;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "WindowsOCR: 识别失败 {Path}", imagePath);
                throw;
            }
        }

        public static OcrResult ParseFields(List<OcrTextBlock> blocks)
        {
            var result = new OcrResult();
            if (blocks.Count == 0) return result;

            var allText = string.Join(" ", blocks.Select(b => b.Text));

            bool isIdCard = blocks.Any(b => Regex.IsMatch(b.Text, @"^\d{17}[\dXx\u00D7]$"))
                         || Regex.IsMatch(allText, @"\d{17}[\dXx\u00D7]")
                         || allText.Contains("公民身份号码")
                         || allText.Contains("居民身份证");

            if (isIdCard)
                ParseIdCard(blocks, result);
            else
                ParseCertificate(blocks, result);

            if (string.IsNullOrEmpty(result.IDNumber))
            {
                var m = Regex.Match(allText, @"(\d{17}[\dXx\u00D7])");
                if (m.Success) result.IDNumber = m.Groups[1].Value.ToUpper().Replace("\u00D7", "X");
                else
                {
                    // 容错：找含混淆字母的疑似号码并清洗
                    m = Regex.Match(allText, @"[\dDOBSSdobss\u00D7Xx]{17,19}");
                    if (m.Success)
                    {
                        string cleaned = CleanIdNumber(m.Value);
                        if (cleaned.Length >= 18) result.IDNumber = cleaned.Substring(0, 18);
                    }
                }
            }
            // 兜底：OCR 可能切掉末位只识别出 17 位，尝试补齐校验码
            if (string.IsNullOrEmpty(result.IDNumber))
            {
                var m17 = Regex.Match(allText, @"\b(\d{17})\b");
                if (m17.Success)
                {
                    string partial = m17.Groups[1].Value;
                    char checksum = CalculateIdChecksum(partial);
                    result.IDNumber = partial + checksum;
                    result.Warnings.Add($"身份证号只有17位，已自动补齐末位({checksum})，请核对。");
                }
            }
            if (string.IsNullOrEmpty(result.Gender))
            {
                if (allText.Contains("男")) result.Gender = "男";
                else if (allText.Contains("女")) result.Gender = "女";
            }

            // 性别兜底：从身份证号第 17 位推断（奇男偶女，100% 准确）
            if (string.IsNullOrEmpty(result.Gender) && !string.IsNullOrEmpty(result.IDNumber) && IsValidIdNumber(result.IDNumber))
            {
                result.Gender = (result.IDNumber[16] - '0') % 2 == 1 ? "男" : "女";
            }

            return result;
        }

        private static void ParseIdCard(List<OcrTextBlock> blocks, OcrResult result)
        {
            var allText = string.Join(" ", blocks.Select(b => b.Text));

            // 身份证号：先严格匹配 18 位纯数字+X，再容错匹配含 D/O/S/B 混淆的号码
            string? bestId = null;
            float bestScore = 0;
            foreach (var b in blocks)
            {
                var match = Regex.Match(b.Text, @"(\d{17}[\dXx\u00D7])");
                if (match.Success)
                {
                    string candidate = match.Groups[1].Value.ToUpper().Replace("\u00D7", "X");
                    // 校验码不通过的不采用（避免错误号码入库），留给后续容错/补全逻辑
                    if (!IsValidIdNumber(candidate)) continue;
                    float score = 1.0f;
                    if (score > bestScore) { bestId = candidate; bestScore = score; }
                }
            }
            // 第二遍：放宽匹配，处理 D(0) O(0) S(5) B(8) 等 OCR 混淆
            if (string.IsNullOrEmpty(bestId))
            {
                foreach (var b in blocks)
                {
                    // 找 16-20 位含字母的疑似号码
                    var match = Regex.Match(b.Text, @"(\d{10,17}[DOBSdobss\u00D7Xx]\d{1,8})|(\d{6,10}[DOBSdobss\u00D7Xx]\d{8,12})");
                    if (match.Success)
                    {
                        string candidate = CleanIdNumber(match.Groups[0].Value);
                        if (candidate.Length >= 17)
                        {
                            // 补齐到 18 位或取 18 位
                            if (candidate.Length > 18) candidate = candidate.Substring(0, 18);
                            // 校验码不通过的不采用（避免错误号码入库），留给第三遍补全
                            if (!IsValidIdNumber(candidate)) continue;
                            float score = 0.9f;
                            if (score > bestScore) { bestId = candidate; bestScore = score; }
                        }
                    }
                }
            }
            // 第三遍：17 位纯数字，尝试补齐校验码
            if (string.IsNullOrEmpty(bestId))
            {
                foreach (var b in blocks)
                {
                    var m17 = Regex.Match(b.Text, @"\b(\d{17})\b");
                    if (m17.Success)
                    {
                        string partial = m17.Groups[1].Value;
                        char checksum = CalculateIdChecksum(partial);
                        bestId = partial + checksum;
                        break;
                    }
                }
            }
            if (bestId != null)
            {
                result.IDNumber = bestId;
                if (bestScore < 1f)
                    result.Warnings.Add("身份证号校验码不匹配，请核对。");
            }

            // 姓名：优先找"姓名"关键词后的文本（跳过标签词，如"性别""民族"）
            // PaddleOCR 细粒度块中常见复合标签（如"民族汉""性别男"），加入排除集
            var nameLabels = new HashSet<string> { "姓名", "性别", "民族", "出生", "住址", "住量", "身份证", "公民身份号码",
                "签发机关", "有效期限", "国籍", "民族汉", "民旅汉", "性别男", "性别女", "性制男",
                "出生住址", "居民身份证", "中华人民共和国" };
            for (int i = 0; i < blocks.Count; i++)
            {
                var text = blocks[i].Text;
                if (text.Contains("姓名"))
                {
                    // 处理"姓名"和姓名合并到同一块的情况：支持前缀字符（如"》，姓名王阿鑫"）
                    int nameIdx = text.IndexOf("姓名");
                    if (nameIdx >= 0 && text.Length > nameIdx + 2)
                    {
                        var inlineName = text.Substring(nameIdx + 2);
                        if (Regex.IsMatch(inlineName, @"^[\u4e00-\u9fff·]{2,4}$") && IsPlausibleName(inlineName))
                        { result.Name = inlineName; break; }
                    }
                    // 从"姓名"后面连续找第一个合理的姓名块（跳过标签和明显非姓名文本）
                    for (int j = i + 1; j < blocks.Count; j++)
                    {
                        var candidate = blocks[j].Text;
                        if (Regex.IsMatch(candidate, @"^[\u4e00-\u9fff·]{2,4}$") && !nameLabels.Contains(candidate))
                        {
                            if (IsPlausibleName(candidate))
                            { result.Name = candidate; break; }
                            // 不合理则继续往后找（避免"民族汉"等被误当名字）
                            continue;
                        }
                        if (candidate.Length > 4 && !nameLabels.Contains(candidate)) break;
                    }
                    // 往后找失败时，往前找（PaddleOCR 常见：姓名在"姓名"标签之前的独立块）
                    if (string.IsNullOrEmpty(result.Name))
                    {
                        for (int k = i - 1; k >= 0; k--)
                        {
                            var candidate = blocks[k].Text;
                            if (Regex.IsMatch(candidate, @"^[\u4e00-\u9fff·]{2,4}$") && !nameLabels.Contains(candidate))
                            {
                                if (IsPlausibleName(candidate))
                                { result.Name = candidate; break; }
                            }
                            if (candidate.Length > 4 && !nameLabels.Contains(candidate)) break;
                        }
                    }
                    break; // 找到"姓名"后只处理一次
                }
            }
            if (string.IsNullOrEmpty(result.Name))
            {
                // OCR 可能未单独识别"姓名"二字（与其他标签同行），兜底找独立中文块
                var commonLabels = new HashSet<string> { "性别", "民族", "出生", "住址", "身份证", "公民身份号码",
                    "签发机关", "有效期限", "国籍", "性别出生住址", "出生住址", "住量" };
                foreach (var b in blocks)
                {
                    var text = b.Text.Trim();
                    // 处理"姓"+姓名合并：如"姓汤凯"→提取"汤凯"
                    if (text.Length >= 3 && text.Length <= 5 && text.StartsWith("姓"))
                    {
                        var namePart = text.Substring(1);
                        if (Regex.IsMatch(namePart, @"^[\u4e00-\u9fff·]+$") && IsPlausibleName(namePart))
                        { result.Name = namePart; break; }
                    }
                    // 处理"名"+姓名合并（"姓名"被分成两块）：如"名何世翡"→提取"何世翡"
                    if (text.Length >= 3 && text.Length <= 5 && text.StartsWith("名"))
                    {
                        var namePart = text.Substring(1);
                        if (Regex.IsMatch(namePart, @"^[\u4e00-\u9fff·]+$") && !namePart.Contains("名") && IsPlausibleName(namePart))
                        { result.Name = namePart; break; }
                    }
                    // 通用规则：以"性"开头的 2 字块跳过（OCR 误识的"性别"标签，不会是姓名）
                    if (text.StartsWith("性") && text.Length == 2) continue;
                    // 以"男"/"女"开头的多字块跳过（"男民族汉"→性别+民族合并，姓名如"胜男"在末尾则保留）
                    if (text.Length > 1 && (text[0] == '男' || text[0] == '女')) continue;
                    // 以"址"结尾的 2 字块跳过（OCR 误识的"住址"→"使址""住量"等，不会是姓名）
                    if (text.Length == 2 && text.EndsWith("址")) continue;
                    // 以明显非姓名字符开头的块跳过：公/身/份/有/签/户/居/民/出/国
                    if (text.Length >= 2 && "公身份有签户居民出国".Contains(text[0])) continue;
                    // 以明显非姓名字符结尾的块跳过：男/女/汉/族/份/号/址/期/书/局
                    if ("男女汉族份号址期书局".Contains(text[^1])) continue;
                    if (text.Length >= 2 && text.Length <= 4 &&
                        Regex.IsMatch(text, @"^[\u4e00-\u9fff·]+$") &&
                        !commonLabels.Contains(text) &&
                        text != "男" && text != "女" && text != "汉" &&
                        IsPlausibleName(text))
                    {
                        result.Name = text;
                        break;
                    }
                }
            }

            // 性别：优先块级精确匹配，其次查找块内包含"男"/"女"
            foreach (var b in blocks)
            {
                if (b.Text == "男" || b.Text == "女") { result.Gender = b.Text; break; }
            }
            if (string.IsNullOrEmpty(result.Gender))
            {
                foreach (var b in blocks)
                {
                    if (b.Text.Contains("男")) { result.Gender = "男"; break; }
                    if (b.Text.Contains("女")) { result.Gender = "女"; break; }
                }
            }

            // 出生日期 + 年龄：优先从校验通过的身份证号提取（比 OCR 直接识别的日期更准）
            if (!string.IsNullOrEmpty(result.IDNumber) && result.IDNumber.Length >= 14 && IsValidIdNumber(result.IDNumber))
            {
                string birth = result.IDNumber.Substring(6, 8);
                if (DateTime.TryParse($"{birth.Substring(0, 4)}-{birth.Substring(4, 2)}-{birth.Substring(6, 2)}", out var bd)
                    && bd.Year >= 1900 && bd <= DateTime.Now)
                {
                    result.BirthDate = bd.ToString("yyyy-MM-dd");
                    int age = DateTime.Now.Year - bd.Year;
                    if (DateTime.Now < bd.AddYears(age)) age--;
                    if (age >= 0 && age <= 120) result.Age = age.ToString();
                    else result.Warnings.Add($"年龄({age}岁)不合理");
                }
            }

            // 籍贯：从住址文本中提取到县区级（xxxx省xxxx市xxxx区/县，或直辖市如"北京市"）
            // 用 Concat 而非 Join 避免跨块地址被空格截断（如"高新技术开发"+"区"→断裂）
            string allBlocksText = string.Concat(blocks.Select(b => b.Text));
            var nativeMatch = Regex.Match(allBlocksText, @"[\u4e00-\u9fff]{2,4}(?:省|市)(?:[\u4e00-\u9fff]{2,8}(?:市))?(?:[\u4e00-\u9fff]{2,8}(?:区|县))?");
            if (nativeMatch.Success)
            {
                result.NativePlace = nativeMatch.Value;

                // 方案 B：省名白名单校验 —— 反复剥离首字，直到结果以已知省名为开头
                // 避免"出生山东省"、"佳址河北省"等非籍贯前缀被错误保留
                var knownProvinces = new HashSet<string> {
                    "山东", "山西", "河南", "河北", "湖南", "湖北",
                    "广东", "广西", "江苏", "浙江", "安徽", "福建",
                    "江西", "四川", "云南", "贵州", "陕西", "甘肃",
                    "青海", "辽宁", "吉林", "黑龙江", "海南", "台湾",
                    "北京", "上海", "天津", "重庆",
                    "内蒙古", "宁夏", "新疆", "西藏",
                    "香港", "澳门"
                };
                while (result.NativePlace.Length >= 2)
                {
                    if (knownProvinces.Any(p => result.NativePlace.StartsWith(p + "省")
                                             || result.NativePlace.StartsWith(p + "市")))
                        break;
                    result.NativePlace = result.NativePlace.Substring(1);
                }
                // 必须包含"省"或"市"才认为是有效籍贯（去掉前缀后如只剩"省"则不合法）
                if (result.NativePlace.Length < 3 || !Regex.IsMatch(result.NativePlace, @"省|市"))
                    result.NativePlace = "";
            }

            var validDates = blocks.SelectMany(b => ExtractDates(b.Text))
                .Where(d => d.Year >= 2000 && d.Year <= 2060)
                .Distinct()  // 去重：避免同一个日期被多个正则模式重复匹配而导致 Count 误判
                .OrderBy(d => d).ToList();
            if (validDates.Count >= 2)
            { result.ValidFrom = validDates[0].ToString("yyyy-MM-dd"); result.ValidTo = validDates[^1].ToString("yyyy-MM-dd"); }
            else if (validDates.Count == 1)
            {
                // 只有 1 个日期时判断是否为"长期"有效
                if (allText.Contains("长期"))
                {
                    result.ValidFrom = validDates[0].ToString("yyyy-MM-dd");
                    result.ValidTo = "长期";
                }
                else
                    result.ValidTo = validDates[0].ToString("yyyy-MM-dd");
            }
        }

        private static void ParseCertificate(List<OcrTextBlock> blocks, OcrResult result)
        {
            // 去除空格后的全文（用于关键词匹配，手写体 OCR 常见字间空格）
            var allTextClean = string.Concat(blocks.Select(b => RemoveSpaces(b.Text)));

            // 1. 提取证件编号
            result.IDNumber = ExtractCertNumber(blocks, allTextClean);

            // 2. 提取有效期（区分其他无关日期）
            ExtractCertValidDates(blocks, allTextClean, result);
        }

        /// <summary>去除文本中所有空白字符（含空格、制表符等）</summary>
        private static string RemoveSpaces(string text) =>
            string.IsNullOrEmpty(text) ? "" : Regex.Replace(text, @"\s+", "");

        /// <summary>从证书 OCR 结果中提取证件编号</summary>
        private static string ExtractCertNumber(List<OcrTextBlock> blocks, string allTextClean)
        {
            // 中文编号关键词（按长度降序，优先匹配长词）
            var cnLabels = new[] { "证件编号", "证书编号", "注册编号", "证书编号", "编号", "证号" };
            // 英文编号关键词
            var enLabels = new[] { "CERTIFICATENO", "CERTIFICATE", "NUMBER", "NO" };

            // Step 1: 找关键词标签 → 取紧跟其后的内容
            foreach (var label in cnLabels.Concat(enLabels))
            {
                for (int i = 0; i < blocks.Count; i++)
                {
                    var clean = RemoveSpaces(blocks[i].Text);
                    int idx = clean.IndexOf(label, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) continue;

                    string afterLabel = clean.Substring(idx + label.Length).Trim();
                    // 同一 block 关键词后面有内容 → 提取
                    if (afterLabel.Length >= 4)
                    {
                        var m = Regex.Match(afterLabel, @"[A-Za-z0-9\-/\u4e00-\u9fff\u00D7]{4,40}");
                        if (m.Success) return m.Value;
                    }
                    // 取下一 block
                    if (i + 1 < blocks.Count)
                    {
                        var next = RemoveSpaces(blocks[i + 1].Text);
                        if (next.Length >= 4 && next.Length <= 40)
                        {
                            var m = Regex.Match(next, @"[A-Za-z0-9\-/\u4e00-\u9fff\u00D7]{4,40}");
                            if (m.Success) return m.Value;
                        }
                    }
                }
            }

            // Step 2: 正则兜底 — 按匹配精度降序
            // 省份简称+建安/交安+字母+括号数字+数字（鲁建安C3（2026）4025645）
            var m1 = Regex.Match(allTextClean, @"[\u4e00-\u9fff]{1,3}(?:建安|交安)[A-Za-z]\d?（\d{4}）\d{5,10}");
            if (m1.Success) return m1.Value;
            // 省份简称+字母+字母+数字（京交安C26G00570）
            var m2 = Regex.Match(allTextClean, @"[\u4e00-\u9fff]{1,3}[A-Za-z]{1,3}\d{1,3}[A-Za-z0-9]{2,15}");
            if (m2.Success) return m2.Value;
            // 省份简称+字母+数字（辽A072020000569 / 黑1372023202304480）
            var m3 = Regex.Match(allTextClean, @"[\u4e00-\u9fff]{1,3}[A-Za-z0-9]\d{7,20}");
            if (m3.Success) return m3.Value;
            // T+身份证号
            var m4 = Regex.Match(allTextClean, @"T\d{17}[\dXx]");
            if (m4.Success) return m4.Value;
            // 字母开头+数字（AH2023011M0158 等通用格式）
            var m5 = Regex.Match(allTextClean, @"[A-Za-z]{1,8}\d{6,24}[A-Za-z0-9]{0,8}");
            if (m5.Success) return m5.Value;
            // 数字+字母
            var m6 = Regex.Match(allTextClean, @"\d{8,24}[A-Za-z]{1,8}");
            if (m6.Success) return m6.Value;
            // 纯数字（较长，通常>8位）
            var m7 = Regex.Match(allTextClean, @"\d{12,24}");
            if (m7.Success) return m7.Value;

            return "";
        }

        /// <summary>从证书 OCR 结果中提取有效期（排除无关日期）</summary>
        private static void ExtractCertValidDates(List<OcrTextBlock> blocks, string allTextClean, OcrResult result)
        {
            // 收集所有 block 中提取到的日期，标记其上下文
            var validityDates = new List<DateTime>();   // 有效期附近的日期
            var otherDates = new List<DateTime>();       // 其他日期的日期
            var fallbackDates = new List<DateTime>();    // 无上下文的日期

            // 应排除的日期关键词（含空格版本，用 RemoveSpaces 后匹配）
            var skipLabels = new[] { "初次申领日期", "初次领证日期", "发证日期", "初次发证日期",
                "初次注册日期", "初领日期", "复审日期", "出生日期", "出生", "签发日期",
                "注册日期", "申领日期", "领证日期" };
            // 有效期关键词
            var validLabels = new[] { "有效期", "有效期限", "有效期至", "有效日期", "有效时间",
                "VALIDITY", "VALIDFROM", "VALIDTO", "EXPIRY", "EXPIRATION" };

            foreach (var block in blocks)
            {
                var dates = ExtractDates(block.Text);
                if (dates.Count == 0) continue;

                var cleanBlock = RemoveSpaces(block.Text);
                bool isValidityBlock = validLabels.Any(l => cleanBlock.Contains(l, StringComparison.OrdinalIgnoreCase));
                bool isSkipBlock = skipLabels.Any(l => cleanBlock.Contains(l, StringComparison.OrdinalIgnoreCase));

                // 同时包含有效期和排除关键词，检查谁更靠近
                if (isValidityBlock && isSkipBlock)
                {
                    // 确定性判断：看日期前后的具体标签
                    // 逐关键词检查位置
                    int validPos = cleanBlock.Length;
                    foreach (var l in validLabels)
                    {
                        int p = cleanBlock.IndexOf(l, StringComparison.OrdinalIgnoreCase);
                        if (p >= 0 && p < validPos) validPos = p;
                    }
                    int skipPos = cleanBlock.Length;
                    foreach (var l in skipLabels)
                    {
                        int p = cleanBlock.IndexOf(l, StringComparison.OrdinalIgnoreCase);
                        if (p >= 0 && p < skipPos) skipPos = p;
                    }
                    // 有效期关键词更靠前 → 有效期主导，否则跳过
                    isValidityBlock = validPos <= skipPos;
                    isSkipBlock = skipPos < validPos;
                }

                foreach (var d in dates)
                {
                    if (isValidityBlock)
                        validityDates.Add(d);
                    else if (isSkipBlock)
                        otherDates.Add(d);
                    else
                        fallbackDates.Add(d);
                }
            }

            // 按时间排序
            validityDates = validityDates.OrderBy(d => d).Distinct().ToList();
            fallbackDates = fallbackDates.OrderBy(d => d).Distinct().ToList();

            // 使用有效期日期（优先）
            if (validityDates.Count >= 2)
            {
                result.ValidFrom = validityDates[0].ToString("yyyy-MM-dd");
                result.ValidTo = validityDates[^1].ToString("yyyy-MM-dd");
            }
            else if (validityDates.Count == 1)
            {
                if (allTextClean.Contains("长期"))
                {
                    result.ValidFrom = validityDates[0].ToString("yyyy-MM-dd");
                    result.ValidTo = "长期";
                }
                else
                    result.ValidTo = validityDates[0].ToString("yyyy-MM-dd");
            }
            else
            {
                // 无有效期标签 → 用 fallback 日期
                if (fallbackDates.Count >= 2)
                {
                    result.ValidFrom = fallbackDates[0].ToString("yyyy-MM-dd");
                    result.ValidTo = fallbackDates[^1].ToString("yyyy-MM-dd");
                }
                else if (fallbackDates.Count == 1)
                {
                    if (allTextClean.Contains("长期"))
                    {
                        result.ValidFrom = fallbackDates[0].ToString("yyyy-MM-dd");
                        result.ValidTo = "长期";
                    }
                    else
                        result.ValidTo = fallbackDates[0].ToString("yyyy-MM-dd");
                }
            }
        }

        private static List<DateTime> ExtractDates(string text)
        {
            var dates = new List<DateTime>();

            // 预处理：统一常见 OCR 混淆符号
            text = text
                .Replace('℃', '.');   // 温度符号 → 点号（背面日期常见误识）

            // 标准分隔符: - . / 年月日
            var mc = Regex.Matches(text, @"(\d{4})\s*[-\.年/]\s*(\d{1,2})\s*[-\.月/]\s*(\d{1,2})");
            foreach (Match m in mc)
                if (DateTime.TryParse($"{m.Groups[1].Value}-{int.Parse(m.Groups[2].Value):D2}-{int.Parse(m.Groups[3].Value):D2}", out var dt))
                    dates.Add(dt);

            // 中文/特殊标点分隔符（身份证背面常见）：2017，08．15 或 2017、08、15 或 2017℃2．06
            var mc3 = Regex.Matches(text, @"(\d{4})\s*[，,．.、℃]\s*(\d{1,2})\s*[．.、℃]\s*(\d{1,2})");
            foreach (Match m in mc3)
                if (DateTime.TryParse($"{m.Groups[1].Value}-{int.Parse(m.Groups[2].Value):D2}-{int.Parse(m.Groups[3].Value):D2}", out var dt))
                    dates.Add(dt);

            // 无分隔符连写: YYYYMMDD
            var mc2 = Regex.Matches(text, @"\b((?:19|20)\d{2})(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])\b");
            foreach (Match m in mc2)
                dates.Add(new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)));

            // 2 位年份格式（背面常见）：17．12．29 → 2017-12-29
            if (dates.Count == 0)
            {
                var mc4 = Regex.Matches(text, @"(\d{2})\s*[．.、]\s*(\d{1,2})\s*[．.、]\s*(\d{1,2})");
                foreach (Match m in mc4)
                {
                    int year = int.Parse(m.Groups[1].Value) + 2000;
                    if (year >= 2000 && year <= 2060 &&
                        DateTime.TryParse($"{year}-{int.Parse(m.Groups[2].Value):D2}-{int.Parse(m.Groups[3].Value):D2}", out var dt))
                        dates.Add(dt);
                }
            }

            return dates;
        }

        private static bool IsValidIdNumber(string id)
        {
            if (id.Length != 18) return false;
            if (!Regex.IsMatch(id, @"^\d{17}[\dXx\u00D7]$")) return false;
            int[] w = { 7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2 };
            char[] c = { '1', '0', 'X', '9', '8', '7', '6', '5', '4', '3', '2' };
            int s = 0;
            for (int i = 0; i < 17; i++) { if (!char.IsDigit(id[i])) return false; s += (id[i] - '0') * w[i]; }
            return id[17] == c[s % 11];
        }

        /// <summary>
        /// 计算身份证号第 18 位校验码
        /// </summary>
        private static char CalculateIdChecksum(string first17Digits)
        {
            if (first17Digits.Length < 17) return '0';
            int[] w = { 7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2 };
            char[] c = { '1', '0', 'X', '9', '8', '7', '6', '5', '4', '3', '2' };
            int s = 0;
            for (int i = 0; i < 17; i++)
                if (char.IsDigit(first17Digits[i])) s += (first17Digits[i] - '0') * w[i];
            return c[s % 11];
        }

        /// <summary>
        /// 清洗 OCR 识别出的身份证号：将 D→0, O→0, S→5, B→8, ×→X 等混淆字母还原
        /// </summary>
        private static string CleanIdNumber(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char ch in raw)
            {
                if (ch >= '0' && ch <= '9') sb.Append(ch);
                else if (ch is 'D' or 'd' or 'O' or 'o') sb.Append('0');
                else if (ch is 'S' or 's') sb.Append('5');
                else if (ch is 'B' or 'b') sb.Append('8');
                else if (ch is 'I' or 'l') sb.Append('1');
                else if (ch is 'Z' or 'z') sb.Append('2');
                else if (ch is 'G' or 'g') sb.Append('6');
                else if (ch is 'X' or 'x' or '\u00D7') sb.Append('X');
                else if (ch == '牙') sb.Append('X');  // 常见 OCR 混淆：牙→X
            }
            return sb.ToString();
        }

        /// <summary>
        /// 判断候选文本是否可能是真实姓名（拒绝明显不属于姓名的文本）
        /// 用于 PaddleOCR 细粒度块结构下过滤"民族汉""性别男"等复合标签误匹配
        /// </summary>
        private static bool IsPlausibleName(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 2 || text.Length > 4) return false;

            // 精确匹配已知的非姓名词
            if (new HashSet<string> { "居民", "公民", "号码", "身份证", "有效期",
                "出生", "住址", "民族", "姓名", "性别", "国籍", "签发",
                "编号", "证号" }.Contains(text)) return false;

            // 以明显非姓名字符开头的块：公/身/份/有/签/户/居/民/出/国/籍/性
            if ("公身份有签户居民出国籍性".Contains(text[0])) return false;

            // 以明显非姓名字符结尾的块：男/女/汉/族/份/号/址/期/书/局/公/安/部
            if ("男女汉族份号址期书局公安部".Contains(text[^1])) return false;

            // 包含非纯中文的字符（数字、标点、符号等）
            if (!Regex.IsMatch(text, @"^[\u4e00-\u9fff·]{2,4}$")) return false;

            return true;
        }

        /// <summary>
        /// 检测身份证 OCR 结果属于正面还是背面。
        /// 返回 "Front" / "Back"，若无法判定（非身份证图片）则返回 null。
        /// </summary>
        public static string? DetectIdCardSide(List<OcrTextBlock> blocks)
        {
            if (blocks == null || blocks.Count == 0) return null;

            var allText = string.Join(" ", blocks.Select(b => b.Text));

            // 完全不像身份证图片 → 不干预
            if (!allText.Contains("居民身份证") && !allText.Contains("签发机关")
                && !allText.Contains("姓名") && !allText.Contains("性别")
                && !allText.Contains("有效期限") && !Regex.IsMatch(allText, @"\d{17}[\dXx\u00D7]")
                && !allText.Contains("公民身份号码"))
                return null;

            // 背面：签发机关 或 有效期限（且无姓名）
            if (allText.Contains("签发机关")) return "Back";
            if (allText.Contains("有效期限") && !allText.Contains("姓名") && !allText.Contains("性别"))
                return "Back";

            // 正面：姓名/性别/民族/住址/公民身份号码
            if (allText.Contains("姓名") || allText.Contains("性别")
                || allText.Contains("民族") || allText.Contains("公民身份号码"))
                return "Front";

            // 有身份证号但无法确定正反面 → 默认正面
            if (Regex.IsMatch(allText, @"\d{17}[\dXx\u00D7]"))
                return "Front";

            return null;
        }
    }

    public class OcrTextBlock
    {
        public string Text { get; set; } = "";
        public float Score { get; set; }
        /// <summary>文字块在原始图片上的位置（像素坐标）</summary>
        public Rect BoundingBox { get; set; }
    }

    public class OcrResult
    {
        public string Name { get; set; } = "";
        public string Gender { get; set; } = "";
        public string Age { get; set; } = "";
        public string BirthDate { get; set; } = "";
        public string IDNumber { get; set; } = "";
        public string NativePlace { get; set; } = "";
        public string ValidFrom { get; set; } = "";
        public string ValidTo { get; set; } = "";
        public List<string> Warnings { get; } = new();
        public bool HasAnyField =>
            !string.IsNullOrEmpty(Name) || !string.IsNullOrEmpty(Gender) ||
            !string.IsNullOrEmpty(Age) || !string.IsNullOrEmpty(IDNumber) ||
            !string.IsNullOrEmpty(ValidTo);
        public string ToConfirmText(bool isIdCard)
        {
            var parts = new List<string>();
            if (isIdCard)
            {
                parts.Add($"姓名：{Name}");
                parts.Add($"性别：{Gender}");
                parts.Add($"出生：{BirthDate}");
                parts.Add($"年龄：{Age}");
                parts.Add($"籍贯：{NativePlace}");
                parts.Add($"身份证号：{IDNumber}");
                string vs = "";
                if (!string.IsNullOrEmpty(ValidFrom)) vs = ValidFrom;
                if (!string.IsNullOrEmpty(ValidTo)) vs += (string.IsNullOrEmpty(vs) ? "" : " ~ ") + ValidTo;
                parts.Add($"有效期：{vs}");
            }
            else
            {
                parts.Add($"证件号码：{IDNumber}");
                string vs = "";
                if (!string.IsNullOrEmpty(ValidFrom)) vs = ValidFrom;
                if (!string.IsNullOrEmpty(ValidTo)) vs += (string.IsNullOrEmpty(vs) ? "" : " ~ ") + ValidTo;
                parts.Add($"有效期：{vs}");
            }
            string t = string.Join("\n", parts);
            if (Warnings.Count > 0) t += "\n\n⚠️ 警告：\n" + string.Join("\n", Warnings);
            return t;
        }
    }
}
