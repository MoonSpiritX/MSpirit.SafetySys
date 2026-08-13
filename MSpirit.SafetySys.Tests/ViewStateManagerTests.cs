// 视图状态（ViewState.json）测试：
// 保存/加载往返、多项目隔离、文件持久化、损坏文件容错

using System.IO;
using MS_SafetyManagementSystem.Services;

namespace MSpirit.SafetySys.Tests;

public class ViewStateManagerTests
{
    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "MSpiritTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void SaveState_LoadState_完整往返()
    {
        var root = CreateTempRoot();
        try
        {
            var mgr = new ViewStateManager(root);
            mgr.SaveState("proj1", "Hazard",
                new List<string> { "2026年08月", "2026年09月" }, "node-abc", 123.5);

            var (expanded, selected, scroll) = mgr.LoadState("proj1", "Hazard");
            Assert.Equal(new[] { "2026年08月", "2026年09月" }, expanded);
            Assert.Equal("node-abc", selected);
            Assert.Equal(123.5, scroll);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SaveState_项目与模块隔离_互不影响()
    {
        var root = CreateTempRoot();
        try
        {
            var mgr = new ViewStateManager(root);
            mgr.SaveState("proj1", "Hazard", new List<string> { "展开A" }, "selA", 10);
            mgr.SaveState("proj1", "Personnel", new List<string> { "展开B" }, "selB", 20);
            mgr.SaveState("proj2", "Hazard", new List<string> { "展开C" }, "selC", 30);

            var (e1, s1, _) = mgr.LoadState("proj1", "Hazard");
            Assert.Equal("展开A", e1![0]);
            Assert.Equal("selA", s1);

            var (e2, s2, _) = mgr.LoadState("proj1", "Personnel");
            Assert.Equal("展开B", e2![0]);
            Assert.Equal("selB", s2);

            var (e3, s3, _) = mgr.LoadState("proj2", "Hazard");
            Assert.Equal("展开C", e3![0]);
            Assert.Equal("selC", s3);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SaveState_文件持久化_新实例可恢复()
    {
        var root = CreateTempRoot();
        try
        {
            var mgr1 = new ViewStateManager(root);
            mgr1.SaveState("proj1", "Hazard", new List<string> { "2026年08月" }, "node-1", 55.5);

            // 模拟重启：重新 new 一个实例加载同一文件
            var mgr2 = new ViewStateManager(root);
            var (expanded, selected, scroll) = mgr2.LoadState("proj1", "Hazard");
            Assert.Equal(new[] { "2026年08月" }, expanded);
            Assert.Equal("node-1", selected);
            Assert.Equal(55.5, scroll);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SaveState_覆盖保存_后一次生效()
    {
        var root = CreateTempRoot();
        try
        {
            var mgr = new ViewStateManager(root);
            mgr.SaveState("proj1", "Hazard", new List<string> { "旧展开" }, "old-node", 1);
            mgr.SaveState("proj1", "Hazard", new List<string> { "新展开" }, "new-node", 2);

            var (expanded, selected, scroll) = mgr.LoadState("proj1", "Hazard");
            Assert.Equal(new[] { "新展开" }, expanded);
            Assert.Equal("new-node", selected);
            Assert.Equal(2, scroll);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LoadState_无效参数_返回空状态()
    {
        var root = CreateTempRoot();
        try
        {
            var mgr = new ViewStateManager(root);
            var (e1, s1, sc1) = mgr.LoadState("", "Hazard");
            Assert.Null(e1);
            Assert.Null(s1);
            Assert.Equal(0, sc1);

            var (e2, s2, sc2) = mgr.LoadState("proj1", "");
            Assert.Null(e2);
            Assert.Null(s2);
            Assert.Equal(0, sc2);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LoadState_未保存过的键_返回空状态()
    {
        var root = CreateTempRoot();
        try
        {
            var mgr = new ViewStateManager(root);
            var (e, s, sc) = mgr.LoadState("unknown", "Hazard");
            Assert.Null(e);
            Assert.Null(s);
            Assert.Equal(0, sc);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 损坏的JSON文件_不抛异常_返回空状态()
    {
        var root = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "ViewState.json"), "{ 这不是合法JSON ]");

            var mgr = new ViewStateManager(root); // 构造时 Load 不应抛异常
            var (e, s, sc) = mgr.LoadState("proj1", "Hazard");
            Assert.Null(e);
            Assert.Null(s);
            Assert.Equal(0, sc);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
