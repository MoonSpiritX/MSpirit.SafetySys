// DataRootLocator 测试：记录读写、数据根校验、启动决策链、多候选选择、深扫
// 2026-08-15：拔盘/盘符变化/环境漂移时数据根找回机制

using MS_SafetyManagementSystem.Services;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MSpirit.SafetySys.Tests;

public class DataRootLocatorTests : IDisposable
{
    private readonly string _root;

    public DataRootLocatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MSpiritTests", "DataRoot", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch { /* 清理失败不影响测试结论 */ }
    }

    /// <summary>在临时目录下创建一个"有效数据根"（含 ProjectsDate 子目录 + 提示文件），返回其路径</summary>
    private string CreateValidDataRoot(string name)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(dir, "ProjectsDate"));
        File.WriteAllText(Path.Combine(dir, DataPathService.DataRootNoticeFileName), "");
        return dir;
    }

    private string RecordFile => Path.Combine(_root, "record.txt");

    // ===== IsValidDataRoot =====

    [Fact]
    public void IsValidDataRoot_目录不存在_返回false()
    {
        Assert.False(DataRootLocator.IsValidDataRoot(Path.Combine(_root, "不存在")));
        Assert.False(DataRootLocator.IsValidDataRoot(""));
        Assert.False(DataRootLocator.IsValidDataRoot(null!));
    }

    [Fact]
    public void IsValidDataRoot_空目录_返回false()
    {
        string empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);
        Assert.False(DataRootLocator.IsValidDataRoot(empty));
    }

    [Fact]
    public void IsValidDataRoot_含提示文件或ProjectsDate_返回true()
    {
        string a = CreateValidDataRoot("validA"); // 提示文件 + ProjectsDate
        Assert.True(DataRootLocator.IsValidDataRoot(a));

        // 仅 ProjectsDate（无提示文件，老数据根）
        string b = Path.Combine(_root, "validB");
        Directory.CreateDirectory(Path.Combine(b, "ProjectsDate"));
        Assert.True(DataRootLocator.IsValidDataRoot(b));

        // 仅 master.db（极端场景）
        string c = Path.Combine(_root, "validC");
        Directory.CreateDirectory(Path.Combine(c, "ProjectsDate"));
        File.WriteAllText(Path.Combine(c, "ProjectsDate", "master.db"), "");
        Assert.True(DataRootLocator.IsValidDataRoot(c));
    }

    // ===== 记录读写 =====

    [Fact]
    public void 记录_写读往返()
    {
        DataRootLocator.WriteRecordedRoot(@"D:\MSpirit.SafetySys.Date", RecordFile);
        Assert.Equal(@"D:\MSpirit.SafetySys.Date", DataRootLocator.ReadRecordedRoot(RecordFile));
    }

    [Fact]
    public void 记录_文件不存在_返回null()
    {
        Assert.Null(DataRootLocator.ReadRecordedRoot(RecordFile));
    }

    [Fact]
    public void 记录_空白内容_返回null()
    {
        File.WriteAllText(RecordFile, "   \n");
        Assert.Null(DataRootLocator.ReadRecordedRoot(RecordFile));
    }

    [Fact]
    public void 记录_写空路径_不创建文件()
    {
        DataRootLocator.WriteRecordedRoot("", RecordFile);
        DataRootLocator.WriteRecordedRoot(null!, RecordFile);
        Assert.False(File.Exists(RecordFile));
    }

    // ===== 启动决策链 ResolveRootPath =====

    [Fact]
    public void 决策_记录有效_直接用记录路径()
    {
        string valid = CreateValidDataRoot("recorded");
        DataRootLocator.WriteRecordedRoot(valid, RecordFile);

        string? result = DataRootLocator.ResolveRootPath(Array.Empty<string>(), RecordFile);
        Assert.Equal(valid, result);
    }

    [Fact]
    public void 决策_记录失效但浅扫命中_选最优候选并更新记录()
    {
        // 记录指向已不存在的路径（模拟盘符漂移）
        DataRootLocator.WriteRecordedRoot(Path.Combine(_root, "gone"), RecordFile);
        string candidate = CreateValidDataRoot("recovered");

        string? result = DataRootLocator.ResolveRootPath(new[] { candidate }, RecordFile);
        Assert.Equal(candidate, result);
        Assert.Equal(candidate, DataRootLocator.ReadRecordedRoot(RecordFile)); // 记录已更新
    }

    [Fact]
    public void 决策_无记录无候选_用默认路径并写记录()
    {
        string? result = DataRootLocator.ResolveRootPath(Array.Empty<string>(), RecordFile);
        Assert.NotNull(result);
        Assert.EndsWith(DataPathService.DataRootFolderName, result);
        Assert.Equal(result, DataRootLocator.ReadRecordedRoot(RecordFile));
    }

    [Fact]
    public void 决策_记录失效且无候选_返回null表示疑似丢失()
    {
        DataRootLocator.WriteRecordedRoot(Path.Combine(_root, "gone"), RecordFile);
        string? result = DataRootLocator.ResolveRootPath(Array.Empty<string>(), RecordFile);
        Assert.Null(result);
    }

    // ===== PickBestCandidate 多候选选择 =====

    [Fact]
    public void 多候选_带提示文件的优先()
    {
        string withNotice = CreateValidDataRoot("withNotice");      // 提示文件 + ProjectsDate
        string withoutNotice = Path.Combine(_root, "withoutNotice"); // 仅 ProjectsDate
        Directory.CreateDirectory(Path.Combine(withoutNotice, "ProjectsDate"));

        Assert.Equal(withNotice, DataRootLocator.PickBestCandidate(new[] { withoutNotice, withNotice }));
    }

    [Fact]
    public void 多候选_都带提示文件_masterDb较新的优先()
    {
        string older = CreateValidDataRoot("older");
        string newer = CreateValidDataRoot("newer");
        string olderDb = Path.Combine(older, "ProjectsDate", "master.db");
        string newerDb = Path.Combine(newer, "ProjectsDate", "master.db");
        File.WriteAllText(olderDb, "");
        File.WriteAllText(newerDb, "");
        File.SetLastWriteTime(olderDb, DateTime.Now.AddDays(-10));
        File.SetLastWriteTime(newerDb, DateTime.Now);

        Assert.Equal(newer, DataRootLocator.PickBestCandidate(new[] { older, newer }));
    }

    [Fact]
    public void 多候选_单选_直接返回()
    {
        string only = CreateValidDataRoot("only");
        Assert.Equal(only, DataRootLocator.PickBestCandidate(new[] { only }));
    }

    [Fact]
    public void 多候选_空列表_抛异常()
    {
        Assert.Throws<ArgumentException>(() => DataRootLocator.PickBestCandidate(Array.Empty<string>()));
    }

    // ===== 深扫 SearchDeepAsync =====

    [Fact]
    public async Task 深扫_在深层目录树中找到数据根()
    {
        string dataRoot = Path.Combine(_root, "a", "b", "c", DataPathService.DataRootFolderName);
        Directory.CreateDirectory(Path.Combine(dataRoot, "ProjectsDate"));
        File.WriteAllText(Path.Combine(dataRoot, DataPathService.DataRootNoticeFileName), "");

        // 干扰项：同名但无效的目录（无 ProjectsDate/提示文件，不应被收集）
        Directory.CreateDirectory(Path.Combine(_root, "a", DataPathService.DataRootFolderName));

        var results = await DataRootLocator.SearchDeepAsync(CancellationToken.None, null, new[] { _root });

        Assert.Contains(dataRoot, results);
        Assert.Single(results); // 无效同名目录不被收集
    }

    [Fact]
    public async Task 深扫_无数据根_返回空列表()
    {
        var results = await DataRootLocator.SearchDeepAsync(CancellationToken.None, null, new[] { _root });
        Assert.Empty(results);
    }

    [Fact]
    public async Task 深扫_取消令牌_抛取消异常()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // 预先取消
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DataRootLocator.SearchDeepAsync(cts.Token, null, new[] { _root }));
    }
}
