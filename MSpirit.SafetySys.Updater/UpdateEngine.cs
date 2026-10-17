// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.IO.Compression;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MSpirit.SafetySys.Updater;

/// <summary>
/// 更新引擎：定位程序文件夹、读取/校验更新包、清单对账替换、失败回滚、打包 .msupd。
/// 设计要点：
///  - 更新包 .msupd = 标准 zip + 根目录 manifest.json（全量文件清单 + SHA256）。
///  - 替换为"清单对账式"：包内文件逐一覆盖；程序文件夹里清单外的多余文件删除（logs、包自身、更新器自身除外）。
///  - 先解压到临时目录并校验通过，再备份旧文件、复制新文件、清理多余文件；任一失败从备份回滚。
///  - zip slip 防护：解压时校验条目路径，禁止逃出目标目录。
/// </summary>
public static class UpdateEngine
{
    /// <summary>主程序文件名（定位程序文件夹的依据）</summary>
    public const string MainExeName = "MSpirit.SafetySys.exe";

    /// <summary>更新包扩展名</summary>
    public const string PackageExtension = ".msupd";

    /// <summary>清单文件名（zip 内根目录）</summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>日志目录名（对账删除时保留）</summary>
    private const string LogsDirName = "logs";

    /// <summary>备份目录前缀（父目录下临时备份，回滚用）</summary>
    public const string BackupDirPrefixPublic = ".updater-backup-";

    /// <summary>更新器自身文件名（对账删除时保留）</summary>
    private static readonly string[] SelfFiles = { "Updater.exe", "Updater.pdb", "Updater.dll" };

    // ============ 定位 ============

    /// <summary>
    /// 定位主程序路径：① baseDir 同目录直放 → ② baseDir 第一层子文件夹内。
    /// 返回 null 表示未找到。
    /// </summary>
    public static string? FindMainExe(string baseDir)
    {
        string direct = Path.Combine(baseDir, MainExeName);
        if (File.Exists(direct)) return direct;

        try
        {
            foreach (string dir in Directory.GetDirectories(baseDir))
            {
                string candidate = Path.Combine(dir, MainExeName);
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"扫描子文件夹失败：{ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// 查找更新包：① baseDir 同目录 → ② 程序文件夹内。同位置有多个时取最新的。
    /// </summary>
    public static string? FindUpdatePackage(string baseDir, string appFolder)
    {
        string[] searchDirs = { baseDir, appFolder };
        string? best = null;
        DateTime bestTime = DateTime.MinValue;

        foreach (string dir in searchDirs)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            try
            {
                foreach (string file in Directory.GetFiles(dir, "*" + PackageExtension))
                {
                    DateTime t = File.GetLastWriteTimeUtc(file);
                    if (best == null || t > bestTime)
                    {
                        best = file;
                        bestTime = t;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"扫描更新包失败：{ex.Message}");
            }
        }
        return best;
    }

    /// <summary>等待主程序进程退出（轮询），返回是否已退出；超时返回 false。</summary>
    public static bool WaitMainProcessExit(int timeoutSeconds = 10)
    {
        string procName = Path.GetFileNameWithoutExtension(MainExeName);
        int waited = 0;
        while (waited < timeoutSeconds * 2)
        {
            try
            {
                var procs = Process.GetProcessesByName(procName);
                if (procs.Length == 0) return true;
                foreach (var p in procs) p.Dispose();
            }
            catch
            {
                // 枚举进程失败不致命，按未退出处理继续等待
            }
            Thread.Sleep(500);
            waited++;
        }
        return false;
    }

    /// <summary>
    /// 通过命名管道请求主程序"保存数据并退出"（主程序启动时监听 UpdateIpcService）。
    /// 管道名与命令必须与主程序一致。返回是否成功发出请求（不代表主程序已退出）。
    /// </summary>
    public static bool RequestMainProcessExit()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", UpdatePipeName, PipeDirection.Out);
            pipe.Connect(2000); // 主程序未监听则快速失败（2 秒）
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            writer.Write(UpdatePipeCommand);
            return true;
        }
        catch
        {
            // 管道不可达：主程序可能未运行或未开监听（如查看器子进程模式），忽略
            return false;
        }
    }

    /// <summary>更新 IPC 管道名（与主程序 UpdateIpcService.PipeName 一致）</summary>
    public const string UpdatePipeName = "MSpirit.SafetySys.Update";

    /// <summary>请求保存并退出的命令（与主程序 UpdateIpcService.CommandSaveExit 一致）</summary>
    public const string UpdatePipeCommand = "save-exit";

    // ============ 读取与校验 ============

    /// <summary>检查更新包是否含 manifest.json：有 = 全量对账模式；无 = 增量覆盖模式。</summary>
    public static bool HasManifest(string packagePath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(packagePath);
            return zip.GetEntry(ManifestFileName) != null;
        }
        catch
        {
            return false; // 打不开视为无清单（后续解压会再次报错）
        }
    }

    /// <summary>读取 zip 内根目录的 manifest.json，解析失败抛异常。</summary>
    public static UpdateManifest ReadManifest(string packagePath)
    {
        using var zip = ZipFile.OpenRead(packagePath);
        var entry = zip.GetEntry(ManifestFileName)
            ?? throw new InvalidDataException("更新包缺少 manifest.json，不是有效的 .msupd 包");

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        string json = reader.ReadToEnd();
        var manifest = JsonSerializer.Deserialize(json, UpdaterJsonContext.Default.UpdateManifest)
            ?? throw new InvalidDataException("manifest.json 内容为空或格式错误");
        if (manifest.Files == null) manifest.Files = new List<ManifestFileEntry>();
        if (manifest.DeletedFiles == null) manifest.DeletedFiles = new List<string>();
        return manifest;
    }

    /// <summary>
    /// 解压更新包到临时目录（仅 zip slip 防护，不做哈希校验）。供全量/增量两种模式共用。
    /// 返回临时目录路径（调用方负责用完删除）。
    /// </summary>
    public static string ExtractOnly(string packagePath)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "MSpirit.Updater", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        using (var zip = ZipFile.OpenRead(packagePath))
        {
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName == ManifestFileName) continue; // 元数据不落盘
                if (entry.FullName.EndsWith("/")) continue;      // 目录条目

                string relPath = NormalizeZipPath(entry.FullName);
                string dest = Path.GetFullPath(Path.Combine(tempDir, relPath));

                // zip slip 防护：解压目标必须仍在 tempDir 内
                string tempRoot = Path.GetFullPath(tempDir) + Path.DirectorySeparatorChar;
                if (!dest.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"更新包包含非法路径：{entry.FullName}");

                string? dir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                entry.ExtractToFile(dest, overwrite: true);
            }
        }

        return tempDir;
    }

    /// <summary>
    /// 解压更新包到临时目录并校验（zip slip 防护 + SHA256 + 大小）。
    /// 返回临时目录路径（调用方负责用完删除）。
    /// </summary>
    public static string ExtractAndVerify(string packagePath, UpdateManifest manifest)
    {
        string tempDir = ExtractOnly(packagePath);

        // 校验清单文件（hash + 大小），临时目录里多出的文件不校验（解压即信任包内结构）
        foreach (var item in manifest.Files)
        {
            string rel = NormalizeZipPath(item.Path);
            string file = Path.Combine(tempDir, rel);
            if (!File.Exists(file))
                throw new InvalidDataException($"更新包缺少清单内文件：{item.Path}");

            var fi = new FileInfo(file);
            if (item.Size > 0 && fi.Length != item.Size)
                throw new InvalidDataException($"文件大小不符：{item.Path}（期望 {item.Size}，实际 {fi.Length}）");

            if (!string.IsNullOrEmpty(item.Sha256))
            {
                string actual = ComputeSha256(file);
                if (!string.Equals(actual, item.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"文件校验失败（SHA256 不符）：{item.Path}");
            }
        }

        return tempDir;
    }

    // ============ 替换 ============

    /// <summary>
    /// 应用更新：备份旧文件 → 复制新文件 → 对账删除多余文件。
    /// 任一环节失败抛异常，调用方负责回滚。
    /// </summary>
    public static void Apply(string appFolder, string tempDir, UpdateManifest manifest, string backupDir)
    {
        Directory.CreateDirectory(backupDir);

        // 1. 备份将被覆盖/删除的文件（保留相对结构，供回滚）
        foreach (var item in manifest.Files)
        {
            string rel = NormalizeZipPath(item.Path);
            string dest = Path.Combine(appFolder, rel);
            if (File.Exists(dest))
                MoveToBackup(dest, backupDir, rel);
        }
        foreach (string rel in manifest.DeletedFiles)
        {
            string dest = Path.Combine(appFolder, NormalizeZipPath(rel));
            if (File.Exists(dest))
                MoveToBackup(dest, backupDir, rel);
        }

        // 2. 复制新文件（覆盖或新增）
        foreach (var item in manifest.Files)
        {
            string rel = NormalizeZipPath(item.Path);
            string src = Path.Combine(tempDir, rel);
            string dest = Path.Combine(appFolder, rel);
            string? dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.Copy(src, dest, overwrite: true);
        }

        // 3. 对账删除：程序文件夹里清单外的多余文件（保留 logs / 更新包 / 更新器自身 / 备份目录）
        DeleteExtraFiles(appFolder, manifest, backupDir);
    }

    /// <summary>
    /// 增量覆盖模式：把临时目录内的所有文件复制到程序文件夹，覆盖同名文件。
    /// 不做对账删除、不做哈希校验（无 manifest 时信任包内文件）。
    /// 保护项（logs / 更新包 / 更新器自身 / 备份目录）仍跳过，不覆盖。
    /// 备份逻辑与全量模式一致，失败可回滚。
    /// </summary>
    public static void ApplyIncremental(string appFolder, string tempDir, string backupDir)
    {
        Directory.CreateDirectory(backupDir);

        var filePaths = new List<string>();
        foreach (string file in Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(tempDir, file);
            if (IsProtected(rel, backupDir)) continue; // 保护项不覆盖
            filePaths.Add(file);
        }

        // 1. 备份将被覆盖的文件（保留相对结构，供回滚）
        foreach (string src in filePaths)
        {
            string rel = Path.GetRelativePath(tempDir, src);
            string dest = Path.Combine(appFolder, rel);
            if (File.Exists(dest))
                MoveToBackup(dest, backupDir, rel);
        }

        // 2. 复制新文件（覆盖或新增）
        foreach (string src in filePaths)
        {
            string rel = Path.GetRelativePath(tempDir, src);
            string dest = Path.Combine(appFolder, rel);
            string? dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.Copy(src, dest, overwrite: true);
        }
    }

    /// <summary>把程序文件夹内清单外的多余文件删除（深度遍历 + 清理空目录）。</summary>
    private static void DeleteExtraFiles(string appFolder, UpdateManifest manifest, string backupDir)
    {
        var keepSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.Files)
            keepSet.Add(NormalizePathForCompare(item.Path)); // 统一 / 分隔的键（与扫描比对一致）
        // DeletedFiles 显式要删，不进 keepSet

        // 收集多余文件
        var extraFiles = new List<string>();
        foreach (string file in Directory.EnumerateFiles(appFolder, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(appFolder, file);
            if (IsProtected(rel, backupDir)) continue;
            if (!keepSet.Contains(NormalizePathForCompare(rel)))
                extraFiles.Add(file);
        }

        foreach (string file in extraFiles)
        {
            try
            {
                string rel = Path.GetRelativePath(appFolder, file);
                MoveToBackup(file, backupDir, rel);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"删除多余文件失败（跳过）：{file}：{ex.Message}");
            }
        }

        // 清理空目录（自底向上，跳过受保护目录）
        foreach (string dir in Directory.EnumerateDirectories(appFolder, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            string rel = Path.GetRelativePath(appFolder, dir);
            if (IsProtected(rel, backupDir)) continue;
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"清理空目录失败（跳过）：{dir}：{ex.Message}");
            }
        }
    }

    /// <summary>文件是否为受保护项（logs、更新包、更新器自身、备份目录），对账删除时跳过。</summary>
    private static bool IsProtected(string relPath, string backupDir)
    {
        string rel = relPath.Replace('\\', '/');

        // 日志目录整棵保留
        if (rel.Equals(LogsDirName, StringComparison.OrdinalIgnoreCase) ||
            rel.StartsWith(LogsDirName + "/", StringComparison.OrdinalIgnoreCase))
            return true;

        // 更新包自身保留（可能放在程序文件夹内）
        if (rel.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase))
            return true;

        // 更新器自身保留（调试时更新器与主程序同目录）
        foreach (string self in SelfFiles)
        {
            if (rel.Equals(self, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // 备份目录保留（可能在程序文件夹内/外，双保险）
        string backupName = Path.GetFileName(backupDir.TrimEnd('\\', '/'));
        if (rel.Equals(backupName, StringComparison.OrdinalIgnoreCase) ||
            rel.StartsWith(backupName + "/", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>把文件移动到备份目录（保持相对结构），回滚用。</summary>
    private static void MoveToBackup(string sourceFile, string backupDir, string relPath)
    {
        string rel = NormalizeZipPath(relPath);
        string dest = Path.Combine(backupDir, rel);
        string? dir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(dest)) File.Delete(dest);
        File.Move(sourceFile, dest);
    }

    /// <summary>从备份目录恢复全部文件（回滚），返回是否成功。</summary>
    public static bool Rollback(string appFolder, string backupDir)
    {
        if (!Directory.Exists(backupDir)) return false;
        try
        {
            foreach (string file in Directory.EnumerateFiles(backupDir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(backupDir, file);
                string dest = Path.Combine(appFolder, rel);
                string? dir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(file, dest);
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"回滚失败：{ex.Message}");
            return false;
        }
    }

    // ============ 打包模式 ============

    /// <summary>
    /// 打包模式：把程序文件夹打成 .msupd 更新包（全量文件清单 + SHA256）。
    /// 排除：logs、*.msupd、更新器自身、备份目录。
    /// appVersion 自动读取 MSpirit.SafetySys.dll 的文件版本，读取失败用 "0.0.0.0"。
    /// </summary>
    public static void Pack(string sourceFolder, string outPath)
    {
        if (!Directory.Exists(sourceFolder))
            throw new DirectoryNotFoundException($"源目录不存在：{sourceFolder}");

        var manifest = new UpdateManifest
        {
            AppVersion = ReadAppVersion(sourceFolder),
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        };

        foreach (string file in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(sourceFolder, file).Replace('\\', '/');
            if (IsProtected(rel, backupDir: string.Empty)) continue;
            if (rel.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase)) continue; // 避免打包旧清单

            manifest.Files.Add(new ManifestFileEntry
            {
                Path = rel,
                Size = new FileInfo(file).Length,
                Sha256 = ComputeSha256(file),
            });
        }

        if (manifest.Files.Count == 0)
            throw new InvalidOperationException("源目录没有可打包的文件");

        string outFull = Path.GetFullPath(outPath);
        string? outDir = Path.GetDirectoryName(outFull);
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
        if (File.Exists(outFull)) File.Delete(outFull);

        using (var zip = ZipFile.Open(outFull, ZipArchiveMode.Create))
        {
            // 清单放根目录
            string json = JsonSerializer.Serialize(manifest, UpdaterJsonContext.Default.UpdateManifest);
            var manifestEntry = zip.CreateEntry(ManifestFileName);
            using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
                writer.Write(json);

            // 文件按相对路径写入
            foreach (var item in manifest.Files)
            {
                zip.CreateEntryFromFile(Path.Combine(sourceFolder, item.Path.Replace('/', Path.DirectorySeparatorChar)),
                    item.Path, CompressionLevel.Optimal);
            }
        }
    }

    /// <summary>读取源目录内主程序程序集的文件版本（dll 优先，exe 兜底）。</summary>
    private static string ReadAppVersion(string sourceFolder)
    {
        string[] candidates = { Path.Combine(sourceFolder, "MSpirit.SafetySys.dll"),
                                Path.Combine(sourceFolder, MainExeName) };
        foreach (string candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate))
                {
                    var ver = FileVersionInfo.GetVersionInfo(candidate);
                    string? v = ver.FileVersion;
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
            catch
            {
                // 读取失败继续尝试下一个
            }
        }
        return "0.0.0.0";
    }

    // ============ 工具 ============

    /// <summary>计算文件 SHA256（十六进制小写）。</summary>
    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>把 zip 内的 / 路径规整为系统路径（防 ..、防前导斜杠）。</summary>
    private static string NormalizeZipPath(string path)
    {
        string p = path.Replace('\\', '/');
        if (p.StartsWith("/", StringComparison.Ordinal)) p = p.TrimStart('/');
        if (p.Contains(".."))
            throw new InvalidDataException($"更新包包含非法路径段（..）：{path}");
        return p.Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>用于清单比对：统一分隔符为 /、去掉前导 .\</summary>
    private static string NormalizePathForCompare(string relPath)
    {
        string p = relPath.Replace('\\', '/');
        return p.StartsWith("./", StringComparison.Ordinal) ? p[2..] : p;
    }
}
