// 图片剪裁面板（阶段 1）测试：分组构建 / 添加来源（文件/文件夹/拖入加载同目录全部）/ 移除清空

using MS_SafetyManagementSystem.Tools;
using System.IO;

namespace MSpirit.SafetySys.Tests;

public class ImageProcessCropViewModelTests : IDisposable
{
    private readonly string _root;

    public ImageProcessCropViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MSpiritTests", "CropVm", Guid.NewGuid().ToString("N"));
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

    private string CreateFile(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    // ===== AddFiles 分组构建 =====

    [Fact]
    public void AddFiles_按目录分组_组内按文件名排序()
    {
        var vm = new ImageProcessCropViewModel();
        string dirA = Path.Combine(_root, "甲文件夹");
        string dirB = Path.Combine(_root, "乙文件夹");
        var a2 = CreateFile(dirA, "b.jpg");
        var a1 = CreateFile(dirA, "a.png");
        var b1 = CreateFile(dirB, "c.jpg");

        int added = vm.AddFiles(new[] { a2, a1, b1 });

        Assert.Equal(3, added);
        Assert.Equal(2, vm.Groups.Count);
        Assert.Equal("甲文件夹", vm.Groups[0].FolderName);
        Assert.Equal("乙文件夹", vm.Groups[1].FolderName);
        // 组内按文件名排序
        Assert.Equal(new[] { "a.png", "b.jpg" }, vm.Groups[0].Files.Select(f => f.FileName));
        Assert.Equal("c.jpg", vm.Groups[1].Files.Single().FileName);
        Assert.Equal("总计: 3 张", vm.TotalCountText);
    }

    [Fact]
    public void AddFiles_重复添加_去重()
    {
        var vm = new ImageProcessCropViewModel();
        string file = CreateFile(Path.Combine(_root, "d"), "a.jpg");

        Assert.Equal(1, vm.AddFiles(new[] { file }));
        Assert.Equal(0, vm.AddFiles(new[] { file }));          // 同路径
        Assert.Equal(0, vm.AddFiles(new[] { file.ToUpperInvariant() })); // 大小写不同视为同一文件
        Assert.Equal(1, vm.TotalFileCount);
    }

    // ===== 自然排序（2026-08-16 新增）=====

    [Fact]
    public void 组内排序_数字感知_a2在前a10在后()
    {
        var vm = new ImageProcessCropViewModel();
        string dir = Path.Combine(_root, "自然排序");
        // 故意乱序添加，数字段应数值比较而非逐字符
        vm.AddFiles(new[] {
            Path.Combine(dir, "a10.jpg"),
            Path.Combine(dir, "a2.jpg"),
            Path.Combine(dir, "a1.jpg")
        });

        var names = vm.Groups[0].Files.Select(f => f.FileName).ToList();
        Assert.Equal(new[] { "a1.jpg", "a2.jpg", "a10.jpg" }, names);
    }

    [Fact]
    public void SelectFirstAdded_单张_选中该张()
    {
        var vm = new ImageProcessCropViewModel();
        string dir = Path.Combine(_root, "sf");
        var a = CreateFile(dir, "a.jpg");
        CreateFile(dir, "b.jpg");
        vm.AddFiles(new[] { a, Path.Combine(dir, "b.jpg") });

        CropFileItem? fired = null;
        vm.SelectedFileChanged += f => fired = f;
        vm.SelectFirstAdded(new[] { a }); // 只新增一张

        Assert.NotNull(fired);
        Assert.Equal(a, fired!.FilePath);
        Assert.Same(vm.SelectedFile, fired);
    }

    [Fact]
    public void SelectFirstAdded_多张_选中自然排序第一张()
    {
        var vm = new ImageProcessCropViewModel();
        string dir = Path.Combine(_root, "mf");
        vm.AddFiles(new[] {
            Path.Combine(dir, "b10.jpg"),
            Path.Combine(dir, "a2.jpg"),
            Path.Combine(dir, "b2.jpg")
        });

        // 新增路径乱序传入，应选"第一个分组（文件夹名自然序）的第一张（文件自然序）"
        CropFileItem? fired = null;
        vm.SelectedFileChanged += f => fired = f;
        vm.SelectFirstAdded(new[] { Path.Combine(dir, "b10.jpg"), Path.Combine(dir, "a2.jpg"), Path.Combine(dir, "b2.jpg") });

        Assert.NotNull(fired);
        Assert.Equal("a2.jpg", fired!.FileName); // 组内自然序第一张
    }

    [Fact]
    public void SelectFirstAdded_无新增_不动当前选中()
    {
        var vm = new ImageProcessCropViewModel();
        string dir = Path.Combine(_root, "nf");
        vm.AddFiles(new[] { CreateFile(dir, "a.jpg") });
        var original = vm.SelectedFile = vm.Groups[0].Files[0];

        vm.SelectFirstAdded(Array.Empty<string>());

        Assert.Same(original, vm.SelectedFile);
    }

    // ===== PositionText 位置指示（2026-08-16 新增）=====

    [Fact]
    public void PositionText_未选中_显示总数()
    {
        var vm = new ImageProcessCropViewModel();
        string dir = Path.Combine(_root, "pt0");
        vm.AddFiles(new[] { CreateFile(dir, "a.jpg"), CreateFile(dir, "b.jpg") });

        Assert.Equal("共 2 张", vm.PositionText);
    }

    [Fact]
    public void PositionText_选中_显示序号()
    {
        var vm = new ImageProcessCropViewModel();
        string dir = Path.Combine(_root, "pt1");
        vm.AddFiles(new[] { Path.Combine(dir, "a.jpg"), Path.Combine(dir, "b.jpg") });

        vm.SelectedFile = vm.Groups[0].Files[1]; // 第二张
        Assert.Equal("第 2/2 张", vm.PositionText);
    }

    [Fact]
    public void PositionText_跨分组_按列表顺序编号()
    {
        var vm = new ImageProcessCropViewModel();
        string dirA = Path.Combine(_root, "组A");
        string dirB = Path.Combine(_root, "组B");
        vm.AddFiles(new[] {
            Path.Combine(dirA, "a.jpg"), Path.Combine(dirA, "b.jpg"),
            Path.Combine(dirB, "c.jpg")
        });

        // 选中组B的唯一一张 → 第 3/3 张（跨分组扁平顺序）
        vm.SelectedFile = vm.Groups.First(g => g.FolderName == "组B").Files[0];
        Assert.Equal("第 3/3 张", vm.PositionText);
    }

    [Fact]
    public void PositionText_移除后_序号前移()
    {
        var vm = new ImageProcessCropViewModel();
        string dir = Path.Combine(_root, "pt2");
        var a = CreateFile(dir, "a.jpg");
        var b = CreateFile(dir, "b.jpg");
        var c = CreateFile(dir, "c.jpg");
        vm.AddFiles(new[] { a, b, c });
        vm.SelectedFile = vm.Groups[0].Files[2]; // c → 第 3/3 张

        vm.RemoveSelectedFile(vm.Groups[0].Files[0]); // 删 a → c 变第 2/2 张

        Assert.Equal("第 2/2 张", vm.PositionText);
    }

    [Fact]
    public void AddFiles_同目录混合两次添加_并入同一分组()
    {
        var vm = new ImageProcessCropViewModel();
        string dir = Path.Combine(_root, "混合");
        vm.AddFiles(new[] { CreateFile(dir, "a.jpg") });
        vm.AddFiles(new[] { CreateFile(dir, "b.jpg") });

        Assert.Single(vm.Groups);
        Assert.Equal(2, vm.Groups[0].Files.Count);
    }

    // ===== AddFolder 递归分组 =====

    [Fact]
    public void AddFolder_递归子目录_每目录一组()
    {
        var vm = new ImageProcessCropViewModel();
        string root = Path.Combine(_root, "项目照片");
        CreateFile(root, "根图.jpg");
        CreateFile(Path.Combine(root, "子一"), "s1.jpg");
        CreateFile(Path.Combine(root, "子一", "深层"), "s2.png");
        CreateFile(Path.Combine(root, "子二"), "t.jpg");
        CreateFile(Path.Combine(root, "子二"), "说明.txt"); // 非图片忽略

        int added = vm.AddFolder(root);

        Assert.Equal(4, added);
        Assert.Equal(4, vm.Groups.Count); // 根 + 子一 + 深层 + 子二
        Assert.Contains(vm.Groups, g => g.FolderName == "深层");
        Assert.All(vm.Groups, g => Assert.All(g.Files, f => Assert.True(ImageProcessCropViewModel.IsImageFile(f.FilePath))));
    }

    [Fact]
    public void AddFolder_目录不存在_返回0不抛异常()
    {
        var vm = new ImageProcessCropViewModel();
        Assert.Equal(0, vm.AddFolder(Path.Combine(_root, "不存在")));
    }

    // ===== AddDroppedFiles 拖入 =====

    [Fact]
    public void 拖入单张图片_自动加载同目录全部()
    {
        var vm = new ImageProcessCropViewModel();
        string dir = Path.Combine(_root, "工地照片");
        CreateFile(dir, "a.jpg");
        CreateFile(dir, "b.jpg");
        CreateFile(dir, "c.png");
        string dropped = CreateFile(dir, "x.jpg");

        var (added, ignored) = vm.AddDroppedFiles(new[] { dropped });

        Assert.Equal(4, added); // 同目录全部 4 张
        Assert.Empty(ignored);
        Assert.Single(vm.Groups);
        Assert.Equal("工地照片", vm.Groups[0].FolderName);
    }

    [Fact]
    public void 拖入文件夹_递归分组_与添加文件夹一致()
    {
        var vm = new ImageProcessCropViewModel();
        string root = Path.Combine(_root, "拖入文件夹");
        CreateFile(root, "a.jpg");
        CreateFile(Path.Combine(root, "子"), "b.jpg");

        var (added, ignored) = vm.AddDroppedFiles(new[] { root });

        Assert.Equal(2, added);
        Assert.Equal(2, vm.Groups.Count);
        Assert.Empty(ignored);
    }

    [Fact]
    public void 拖入不支持格式_计入忽略_不影响图片()
    {
        var vm = new ImageProcessCropViewModel();
        string dir = Path.Combine(_root, "d");
        string img = CreateFile(dir, "a.jpg");
        string pdf = CreateFile(dir, "b.pdf");
        string missing = Path.Combine(_root, "不存在的文件.png");

        var (added, ignored) = vm.AddDroppedFiles(new[] { img, pdf, missing });

        Assert.Equal(1, added);
        Assert.Equal(2, ignored.Count);
        Assert.Contains("b.pdf", ignored.Select(Path.GetFileName));
    }

    // ===== 移除 / 清空 =====

    [Fact]
    public void RemoveSelectedFile_移除后分组空则删除分组()
    {
        var vm = new ImageProcessCropViewModel();
        string dir = Path.Combine(_root, "r");
        var file = CreateFile(dir, "a.jpg");
        vm.AddFiles(new[] { file });
        var item = vm.Groups[0].Files[0];

        vm.SelectedFile = item;
        vm.RemoveSelectedFile(item);

        Assert.Empty(vm.Groups);
        Assert.Null(vm.SelectedFile);
        Assert.Equal(0, vm.TotalFileCount);
    }

    [Fact]
    public void ClearAll_清空所有分组与选中()
    {
        var vm = new ImageProcessCropViewModel();
        string dir = Path.Combine(_root, "c");
        vm.AddFiles(new[] { CreateFile(dir, "a.jpg") });
        vm.SelectedFile = vm.Groups[0].Files[0];

        vm.ClearAll();

        Assert.Empty(vm.Groups);
        Assert.Null(vm.SelectedFile);
        Assert.Equal("总计: 0 张", vm.TotalCountText);
    }

    [Fact]
    public void SelectedFile_变化触发事件与命令状态()
    {
        var vm = new ImageProcessCropViewModel();
        string dir = Path.Combine(_root, "s");
        var file = CreateFile(dir, "a.jpg");
        vm.AddFiles(new[] { file });
        var item = vm.Groups[0].Files[0];

        CropFileItem? fired = null;
        vm.SelectedFileChanged += f => fired = f;
        vm.SelectedFile = item;

        Assert.Same(item, fired);
        Assert.True(vm.ClearPreviewCommand.CanExecute(null)); // 有选中 → 可清空预览
        vm.SelectedFile = null;
        Assert.False(vm.ClearPreviewCommand.CanExecute(null));
    }

    [Fact]
    public void ClearPreview_触发视图清空_列表选中保留()
    {
        var vm = new ImageProcessCropViewModel();
        string dir = Path.Combine(_root, "cp");
        var file = CreateFile(dir, "a.jpg");
        vm.AddFiles(new[] { file });
        var item = vm.Groups[0].Files[0];
        vm.SelectedFile = item;

        bool previewReady = false;
        vm.PreviewReady += () => previewReady = true;
        vm.ClearPreview();

        Assert.True(previewReady);            // 通知视图清空裁剪控件（SetPreview(null,0,0)）
        Assert.Null(vm.PreviewImage);
        Assert.Equal(0, vm.PixelWidth);
        Assert.Equal(0, vm.PixelHeight);
        Assert.Same(item, vm.SelectedFile);   // 列表选中保留
        Assert.Single(vm.Groups[0].Files);    // 列表内容不动
        Assert.True(vm.ClearPreviewCommand.CanExecute(null)); // 选中保留 → 可再次清空（无害）
    }

    // ===== RefreshGroup 文件夹级刷新 =====

    [Fact]
    public void RefreshGroup_新增Edit文件_列表出现()
    {
        var vm = new ImageProcessCropViewModel();
        string dir = Path.Combine(_root, "rf");
        vm.AddFiles(new[] { CreateFile(dir, "a.jpg") });

        CreateFile(dir, "a_Edit.jpg"); // 模拟裁剪保存产生的新文件
        vm.RefreshGroup(dir);

        var group = Assert.Single(vm.Groups);
        Assert.Equal(2, group.Files.Count);
        Assert.Contains(group.Files, f => f.FileName == "a_Edit.jpg");
        Assert.Equal(2, vm.TotalFileCount);
    }

    [Fact]
    public void RefreshGroup_原图删除_从列表移除()
    {
        var vm = new ImageProcessCropViewModel();
        string dir = Path.Combine(_root, "rf2");
        string file = CreateFile(dir, "a.jpg");
        vm.AddFiles(new[] { file });

        File.Delete(file); // 模拟覆盖模式原图进回收站
        vm.RefreshGroup(dir);

        Assert.Empty(vm.Groups[0].Files);
        Assert.Equal(0, vm.TotalFileCount);
    }

    [Fact]
    public void RefreshGroup_仍在的选中项_保留选中()
    {
        var vm = new ImageProcessCropViewModel();
        string dir = Path.Combine(_root, "rf3");
        var file = CreateFile(dir, "a.jpg");
        CreateFile(dir, "b.jpg");
        vm.AddFiles(new[] { file, Path.Combine(dir, "b.jpg") });
        vm.SelectedFile = vm.Groups[0].Files.First(f => f.FileName == "a.jpg");

        vm.RefreshGroup(dir);

        Assert.NotNull(vm.SelectedFile);
        Assert.Equal("a.jpg", vm.SelectedFile!.FileName);
    }

    [Fact]
    public void RefreshGroup_非图片文件_忽略()
    {
        var vm = new ImageProcessCropViewModel();
        string dir = Path.Combine(_root, "rf4");
        vm.AddFiles(new[] { CreateFile(dir, "a.jpg") });
        CreateFile(dir, "说明.txt");

        vm.RefreshGroup(dir);

        Assert.Single(vm.Groups[0].Files);
        Assert.All(vm.Groups[0].Files, f => Assert.True(ImageProcessCropViewModel.IsImageFile(f.FilePath)));
    }

    [Fact]
    public void RefreshGroup_目录不存在_分组清空不抛异常()
    {
        var vm = new ImageProcessCropViewModel();
        string dir = Path.Combine(_root, "rf5");
        vm.AddFiles(new[] { CreateFile(dir, "a.jpg") });

        Directory.Delete(dir, true);
        vm.RefreshGroup(dir); // 不抛异常

        Assert.Empty(vm.Groups[0].Files);
    }
}
