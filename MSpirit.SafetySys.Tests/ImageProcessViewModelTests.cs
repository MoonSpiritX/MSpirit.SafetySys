// 图片处理（Tools/ImageProcessViewModel）核心行为测试：
// 拖放添加（AddDroppedFiles）的文件过滤 / 文件夹递归 / 去重，以及文件项派生属性（文件名/路径/格式）
// 约定：只用临时目录，绝不触碰真实数据目录

using System.IO;
using MS_SafetyManagementSystem.Tools;

namespace MSpirit.SafetySys.Tests;

public class ImageProcessViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public ImageProcessViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MSpiritTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // 清理失败不影响测试结论，忽略
        }
    }

    /// <summary>在临时目录创建文件（自动建父目录），返回完整路径</summary>
    private string CreateFile(string relativePath)
    {
        var path = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "test");
        return path;
    }

    [Fact]
    public void AddDroppedFiles_拖入多个图片文件_全部添加且派生属性正确()
    {
        var vm = new ImageProcessViewModel();
        var a = CreateFile("a.png");
        var b = CreateFile("sub/b.jpg");

        vm.AddDroppedFiles(new[] { a, b });

        Assert.Equal(2, vm.RotateFiles.Count);
        var first = vm.RotateFiles[0];
        Assert.Equal("a.png", first.FileName);
        Assert.Equal(_tempDir, first.DirectoryPath);
        Assert.Equal("PNG", first.Extension);
        Assert.Equal("JPEG", vm.RotateFiles[1].Extension); // .jpg → JPEG
        Assert.Equal(Path.Combine(_tempDir, "sub"), vm.RotateFiles[1].DirectoryPath);
    }

    [Fact]
    public void AddDroppedFiles_拖入非图片文件_忽略不添加()
    {
        var vm = new ImageProcessViewModel();
        var txt = CreateFile("readme.txt");

        vm.AddDroppedFiles(new[] { txt });

        Assert.Empty(vm.RotateFiles);
    }

    [Fact]
    public void AddDroppedFiles_拖入文件夹_递归收集图片并跳过非图片()
    {
        var vm = new ImageProcessViewModel();
        CreateFile("dir/1.png");
        CreateFile("dir/2.jpg");
        CreateFile("dir/sub/3.webp");
        CreateFile("dir/sub/note.txt");

        vm.AddDroppedFiles(new[] { Path.Combine(_tempDir, "dir") });

        Assert.Equal(3, vm.RotateFiles.Count);
        Assert.Equal("1.png", vm.RotateFiles[0].FileName);
        Assert.Equal("3.webp", vm.RotateFiles[2].FileName);
    }

    [Fact]
    public void AddDroppedFiles_重复拖入同一文件_自动去重()
    {
        var vm = new ImageProcessViewModel();
        var a = CreateFile("a.png");
        var b = CreateFile("b.gif");

        vm.AddDroppedFiles(new[] { a });
        vm.AddDroppedFiles(new[] { a, b });

        Assert.Equal(2, vm.RotateFiles.Count);
    }

    [Fact]
    public void AddDroppedFiles_支持常见图片格式扩展名()
    {
        var vm = new ImageProcessViewModel();
        var files = new[]
        {
            CreateFile("f1.png"), CreateFile("f2.jpg"), CreateFile("f3.jpeg"),
            CreateFile("f4.bmp"), CreateFile("f5.webp"), CreateFile("f6.tiff"),
            CreateFile("f7.tif"), CreateFile("f8.gif")
        };

        vm.AddDroppedFiles(files);

        Assert.Equal(8, vm.RotateFiles.Count);
    }

    [Fact]
    public void AddDroppedFiles_混合拖入图片与非图片及文件夹_只添加图片()
    {
        var vm = new ImageProcessViewModel();
        CreateFile("mix/pic.png");
        CreateFile("mix/note.txt");

        var (added, ignored) = vm.AddDroppedFiles(new[] { CreateFile("top.bmp"), CreateFile("doc.docx"), Path.Combine(_tempDir, "mix") });

        Assert.Equal(2, added);                          // 仅图片计入新增（文件夹内部 txt 不算）
        Assert.Single(ignored);                          // 仅直接拖入的 doc.docx 计入忽略
        Assert.Equal("doc.docx", Path.GetFileName(ignored[0]));
        Assert.Equal(new[] { "top.bmp", "pic.png" }, vm.RotateFiles.Select(f => f.FileName).ToArray());
    }

    [Fact]
    public void AddDroppedFiles_重复拖入_新增数量只计去重后部分()
    {
        var vm = new ImageProcessViewModel();
        var a = CreateFile("a.png");
        var b = CreateFile("b.jpg");

        var first = vm.AddDroppedFiles(new[] { a, b });
        Assert.Equal(2, first.Added);

        var second = vm.AddDroppedFiles(new[] { a, b, CreateFile("c.gif") });
        Assert.Equal(1, second.Added);                   // a、b 已存在，仅 c 新增
        Assert.Equal(3, vm.RotateFiles.Count);
    }

    [Fact]
    public void GetOutputFormat_保持原格式_无编码支持的格式转PNG()
    {
        // 支持编码的格式：保持原格式与原扩展名（JPEG 保留源大小写）
        Assert.Equal((SkiaSharp.SKEncodedImageFormat.Jpeg, ".JPG"), ImageProcessViewModel.GetOutputFormat("a.JPG"));
        Assert.Equal((SkiaSharp.SKEncodedImageFormat.Jpeg, ".jpeg"), ImageProcessViewModel.GetOutputFormat("a.jpeg"));
        Assert.Equal((SkiaSharp.SKEncodedImageFormat.Png, ".png"), ImageProcessViewModel.GetOutputFormat("a.png"));
        Assert.Equal((SkiaSharp.SKEncodedImageFormat.Webp, ".webp"), ImageProcessViewModel.GetOutputFormat("a.webp"));
        Assert.Equal((SkiaSharp.SKEncodedImageFormat.Bmp, ".bmp"), ImageProcessViewModel.GetOutputFormat("a.bmp"));
        // 无编码支持（tiff/gif）：回退 PNG，扩展名同步改 .png（保证扩展名与内容一致）
        Assert.Equal((SkiaSharp.SKEncodedImageFormat.Png, ".png"), ImageProcessViewModel.GetOutputFormat("a.tiff"));
        Assert.Equal((SkiaSharp.SKEncodedImageFormat.Png, ".png"), ImageProcessViewModel.GetOutputFormat("a.gif"));
    }

    [Fact]
    public void GetUniquePath_目标已存在_自动追加序号()
    {
        string basePath = Path.Combine(_tempDir, "photo_右转90°.png");

        // 不存在：原路径
        Assert.Equal(basePath, ImageProcessViewModel.GetUniquePath(basePath));

        // 存在：依次追加 _1、_2
        File.WriteAllText(basePath, "a");
        Assert.Equal(Path.Combine(_tempDir, "photo_右转90°_1.png"), ImageProcessViewModel.GetUniquePath(basePath));

        File.WriteAllText(Path.Combine(_tempDir, "photo_右转90°_1.png"), "b");
        Assert.Equal(Path.Combine(_tempDir, "photo_右转90°_2.png"), ImageProcessViewModel.GetUniquePath(basePath));
    }

    [Fact]
    public void GetExifTransform_八种方向映射正确()
    {
        // 1 正常 / 2 水平翻转 / 3 旋转180° / 4 垂直翻转
        Assert.Equal((0f, false, false), ImageProcessViewModel.GetExifTransform(SkiaSharp.SKEncodedOrigin.TopLeft));
        Assert.Equal((0f, true, false), ImageProcessViewModel.GetExifTransform(SkiaSharp.SKEncodedOrigin.TopRight));
        Assert.Equal((180f, false, false), ImageProcessViewModel.GetExifTransform(SkiaSharp.SKEncodedOrigin.BottomRight));
        Assert.Equal((0f, false, true), ImageProcessViewModel.GetExifTransform(SkiaSharp.SKEncodedOrigin.BottomLeft));
        // 5 旋转90°+水平翻转 / 6 顺时针90°（手机竖拍）/ 7 旋转270°+水平翻转 / 8 逆时针90°
        Assert.Equal((90f, true, false), ImageProcessViewModel.GetExifTransform(SkiaSharp.SKEncodedOrigin.LeftTop));
        Assert.Equal((90f, false, false), ImageProcessViewModel.GetExifTransform(SkiaSharp.SKEncodedOrigin.RightTop));
        Assert.Equal((270f, true, false), ImageProcessViewModel.GetExifTransform(SkiaSharp.SKEncodedOrigin.RightBottom));
        Assert.Equal((270f, false, false), ImageProcessViewModel.GetExifTransform(SkiaSharp.SKEncodedOrigin.LeftBottom));
    }

    [Fact]
    public void RotateRemoveSelected_多选移除全部选中项()
    {
        var vm = new ImageProcessViewModel();
        vm.AddDroppedFiles(new[] { CreateFile("a.png"), CreateFile("b.png"), CreateFile("c.png") });
        Assert.Equal(3, vm.RotateFiles.Count);

        // 多选 a、c（Ctrl/Shift 多选经 SyncSelection 同步）
        vm.SyncSelection(new[] { vm.RotateFiles[0], vm.RotateFiles[2] });
        Assert.True(vm.RotateRemoveSelectedCommand.CanExecute(null));

        vm.RotateRemoveSelectedCommand.Execute(null);

        Assert.Single(vm.RotateFiles);
        Assert.Equal("b.png", vm.RotateFiles[0].FileName);
    }

    [Fact]
    public void RotateRemoveSelected_单选移除_空选中时命令禁用()
    {
        var vm = new ImageProcessViewModel();
        vm.AddDroppedFiles(new[] { CreateFile("a.png") });
        Assert.False(vm.RotateRemoveSelectedCommand.CanExecute(null)); // 未选中不可用

        vm.SyncSelection(new[] { vm.RotateFiles[0] });
        Assert.True(vm.RotateRemoveSelectedCommand.CanExecute(null));
        vm.RotateRemoveSelectedCommand.Execute(null);

        Assert.Empty(vm.RotateFiles);
        // 移除后 DataGrid 会触发 SelectionChanged 清空选中（UI 联动），VM 层模拟同步后命令重新禁用
        vm.SyncSelection(Array.Empty<RotateFileItem>());
        Assert.False(vm.RotateRemoveSelectedCommand.CanExecute(null));
    }

    [Fact]
    public void AddFilesFromDialogPaths_多文件添加并去重()
    {
        var vm = new ImageProcessViewModel();
        var a = CreateFile("a.png");
        var b = CreateFile("b.jpg");

        Assert.Equal(2, vm.AddFilesFromDialogPaths(new[] { a, b }));
        Assert.Equal(2, vm.RotateFiles.Count);

        // 重复路径 + 新文件：仅新增 c
        var c = CreateFile("c.gif");
        Assert.Equal(1, vm.AddFilesFromDialogPaths(new[] { a, b, c }));
        Assert.Equal(3, vm.RotateFiles.Count);
    }

    [Fact]
    public void AddImagesFromFolder_递归收集图片_跳过非图片_重复导入不增加()
    {
        var vm = new ImageProcessViewModel();
        CreateFile("dir/1.png");
        CreateFile("dir/2.jpg");
        CreateFile("dir/sub/3.webp");
        CreateFile("dir/sub/note.txt");

        Assert.Equal(3, vm.AddImagesFromFolder(Path.Combine(_tempDir, "dir")));
        Assert.Equal(3, vm.RotateFiles.Count);

        // 重复导入同一文件夹：全部已存在，新增 0
        Assert.Equal(0, vm.AddImagesFromFolder(Path.Combine(_tempDir, "dir")));
        Assert.Equal(3, vm.RotateFiles.Count);
    }

    [Fact]
    public void AddImagesFromFolder_目录不存在_不抛异常返回0()
    {
        var vm = new ImageProcessViewModel();
        Assert.Equal(0, vm.AddImagesFromFolder(Path.Combine(_tempDir, "不存在的目录")));
        Assert.Empty(vm.RotateFiles);
    }

    [Fact]
    public void AddImagesFromFolder_空目录_返回0()
    {
        var vm = new ImageProcessViewModel();
        Directory.CreateDirectory(Path.Combine(_tempDir, "empty"));
        Assert.Equal(0, vm.AddImagesFromFolder(Path.Combine(_tempDir, "empty")));
        Assert.Empty(vm.RotateFiles);
    }

    [Fact]
    public void RotateFiles_序号列_增删后连续重编号()
    {
        var vm = new ImageProcessViewModel();
        vm.AddDroppedFiles(new[] { CreateFile("a.png"), CreateFile("b.png"), CreateFile("c.png") });
        Assert.Equal(new[] { 1, 2, 3 }, vm.RotateFiles.Select(f => f.Index).ToArray());

        // 移除中间项（b）后重新编号
        vm.SyncSelection(new[] { vm.RotateFiles[1] });
        vm.RotateRemoveSelectedCommand.Execute(null);
        Assert.Equal(new[] { 1, 2 }, vm.RotateFiles.Select(f => f.Index).ToArray());
        Assert.Equal("c.png", vm.RotateFiles[1].FileName);

        // 追加后继续编号
        vm.AddDroppedFiles(new[] { CreateFile("d.png") });
        Assert.Equal(new[] { 1, 2, 3 }, vm.RotateFiles.Select(f => f.Index).ToArray());

        // 清空后新增从 1 重新开始
        vm.RotateClearCommand.Execute(null);
        Assert.Empty(vm.RotateFiles);
        vm.AddDroppedFiles(new[] { CreateFile("e.png") });
        Assert.Equal(1, vm.RotateFiles[0].Index);
    }

    [Fact]
    public void 执行中_列表操作与下拉命令全部禁用_结束后恢复()
    {
        var vm = new ImageProcessViewModel();
        vm.AddDroppedFiles(new[] { CreateFile("a.png"), CreateFile("b.png") });
        vm.SyncSelection(new[] { vm.RotateFiles[0] });

        // 空闲：全部可用
        Assert.True(vm.RotateAddFilesCommand.CanExecute(null));
        Assert.True(vm.RotateAddFolderCommand.CanExecute(null));
        Assert.True(vm.RotateRemoveSelectedCommand.CanExecute(null));
        Assert.True(vm.RotateClearCommand.CanExecute(null));

        // 执行中：全部禁用
        vm.IsProcessing = true;
        Assert.False(vm.RotateAddFilesCommand.CanExecute(null));
        Assert.False(vm.RotateAddFolderCommand.CanExecute(null));
        Assert.False(vm.RotateRemoveSelectedCommand.CanExecute(null));
        Assert.False(vm.RotateClearCommand.CanExecute(null));

        // 结束后恢复
        vm.IsProcessing = false;
        Assert.True(vm.RotateAddFilesCommand.CanExecute(null));
        Assert.True(vm.RotateAddFolderCommand.CanExecute(null));
        Assert.True(vm.RotateRemoveSelectedCommand.CanExecute(null));
        Assert.True(vm.RotateClearCommand.CanExecute(null));
    }

    [Fact]
    public void SelectedRotationOption_切换下拉_同步操作与提示文本()
    {
        var vm = new ImageProcessViewModel();
        // 默认值：校正方向
        Assert.Equal("校正方向", vm.SelectedRotationOption);
        Assert.Equal(RotationOperation.CorrectCopy, vm.SelectedRotation);
        Assert.Equal("当前: 校正副本", vm.SelectedRotationText);

        vm.SelectedRotationOption = "水平翻转";
        Assert.Equal(RotationOperation.FlipH, vm.SelectedRotation);
        Assert.Equal("当前: 水平翻转", vm.SelectedRotationText);

        vm.SelectedRotationOption = "校正方向";
        Assert.Equal(RotationOperation.CorrectCopy, vm.SelectedRotation);
        Assert.Equal("当前: 校正副本", vm.SelectedRotationText);

        vm.SelectedRotationOption = "左转90°";
        Assert.Equal(RotationOperation.Rotate270, vm.SelectedRotation);
        Assert.Equal("当前: 左转90°", vm.SelectedRotationText);
    }

    [Fact]
    public void 移除后重新添加同一路径_可再次添加()
    {
        // 去重路径集合（HashSet）必须与列表同步：移除后应能重新添加同一文件
        var vm = new ImageProcessViewModel();
        var a = CreateFile("a.png");
        Assert.Equal(1, vm.AddFilesFromDialogPaths(new[] { a }));
        Assert.Single(vm.RotateFiles);

        vm.SyncSelection(new[] { vm.RotateFiles[0] });
        vm.RotateRemoveSelectedCommand.Execute(null);
        Assert.Empty(vm.RotateFiles);

        Assert.Equal(1, vm.AddFilesFromDialogPaths(new[] { a }));
        Assert.Single(vm.RotateFiles);
    }

    [Fact]
    public void 清空后重新添加同一路径_可再次添加()
    {
        var vm = new ImageProcessViewModel();
        var a = CreateFile("a.png");
        vm.AddDroppedFiles(new[] { a });
        Assert.Single(vm.RotateFiles);

        vm.RotateClearCommand.Execute(null);
        Assert.Empty(vm.RotateFiles);

        Assert.Equal(1, vm.AddDroppedFiles(new[] { a }).Added);
        Assert.Single(vm.RotateFiles);
    }

    [Fact]
    public void 大小写不同路径_视为同一文件去重()
    {
        // Windows 文件系统不区分大小写：A.PNG 与 a.png 是同一文件，去重按 OrdinalIgnoreCase
        var vm = new ImageProcessViewModel();
        var a = CreateFile("a.png");
        var upper = Path.Combine(Path.GetDirectoryName(a)!, Path.GetFileName(a).ToUpperInvariant());

        vm.AddDroppedFiles(new[] { a });
        var (added, _) = vm.AddDroppedFiles(new[] { upper });

        Assert.Equal(0, added);
        Assert.Single(vm.RotateFiles);
    }

    [Fact]
    public void EnumerateImagesFromPaths_目录递归收集_文件过滤_忽略列表()
    {
        CreateFile("dir/1.png");
        CreateFile("dir/note.txt");

        var (images, ignored) = ImageProcessViewModel.EnumerateImagesFromPaths(new[]
        {
            CreateFile("top.bmp"), CreateFile("doc.docx"), Path.Combine(_tempDir, "dir")
        });

        Assert.Equal(2, images.Count);                     // top.bmp + dir/1.png
        Assert.Single(ignored);                            // 仅直接传入的 doc.docx 计入忽略
        Assert.Equal("doc.docx", Path.GetFileName(ignored[0]));
        Assert.Contains(Path.Combine(_tempDir, "dir", "1.png"), images);
    }

    [Fact]
    public void EnumerateImagesFromPaths_不存在的路径_不抛异常()
    {
        var (images, ignored) = ImageProcessViewModel.EnumerateImagesFromPaths(new[]
        {
            Path.Combine(_tempDir, "不存在.png"), Path.Combine(_tempDir, "不存在目录")
        });

        Assert.Empty(images);
        Assert.Empty(ignored);
    }

    [Fact]
    public void ResetAllStatuses_混合状态文件全部复位为待处理()
    {
        var vm = new ImageProcessViewModel();
        vm.AddDroppedFiles(new[] { CreateFile("a.png"), CreateFile("b.png"), CreateFile("c.png") });
        // 模拟上次执行后的状态残留
        vm.RotateFiles[0].Status = "成功";
        vm.RotateFiles[1].Status = "失败";
        vm.RotateFiles[2].Status = "待处理";

        vm.ResetAllStatuses();

        // 全部复位为"待处理"（第二次执行时状态列不再残留上一次结果）
        Assert.All(vm.RotateFiles, f => Assert.Equal("待处理", f.Status));
    }

    [Fact]
    public void ResetAllStatuses_空列表_不抛异常()
    {
        var vm = new ImageProcessViewModel();
        vm.ResetAllStatuses(); // 空列表复位应安全
        Assert.Empty(vm.RotateFiles);
    }
}
