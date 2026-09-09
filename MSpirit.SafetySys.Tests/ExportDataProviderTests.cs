// C 层：Export 数据提供者测试（HazardExportDataProvider / PersonnelExportDataProvider 取值逻辑）

using MS_SafetyManagementSystem.Export;
using MS_SafetyManagementSystem.Models;
using System.IO;
using Record = MS_SafetyManagementSystem.Models.Record;

namespace MSpirit.SafetySys.Tests;

public class ExportDataProviderTests : IDisposable
{
    private readonly TestProject _tp = new();

    public void Dispose() => _tp.Dispose();

    // ===== PersonnelExportDataProvider =====

    [Fact]
    public void 人员提供者_字段取值映射与一键元数据()
    {
        var rows = new List<PersonnelStatisticsRow>
        {
            new()
            {
                RowNumber = 1,
                Title = "章北海",
                Unit = "星舰建设",
                Department = "一号班组",
                Gender = "男",
                Position = "安全员",
                ThreeCategoryPersonnelDisplay = "是",
                SpecialOperationsPersonnelDisplay = "否",
                IDCardValidPeriod = "2021-01-01 至 2041-01-01",
                CertNumber = "京A-001",
                Remark = "备注"
            }
        };
        var provider = new PersonnelExportDataProvider(rows, "2026年08月人员统计");

        Assert.Equal(1, provider.RowCount);
        Assert.Equal("1", provider.GetTextValue(0, "RowNumber"));
        Assert.Equal("章北海", provider.GetTextValue(0, "Title"));
        Assert.Equal("星舰建设", provider.GetTextValue(0, "Unit"));
        Assert.Equal("一号班组", provider.GetTextValue(0, "Department"));
        Assert.Equal("是", provider.GetTextValue(0, "ThreeCategoryPersonnelDisplay"));
        Assert.Equal("2021-01-01 至 2041-01-01", provider.GetTextValue(0, "IDCardValidPeriod"));
        Assert.Equal("京A-001", provider.GetTextValue(0, "CertNumber"));
        Assert.Equal("", provider.GetTextValue(0, "不存在的字段")); // 未知字段返回空
        Assert.Empty(provider.GetPhotoPaths(0, "Title", "")); // 人员无图片

        Assert.Equal("2026年08月人员统计", provider.OneClickTitleSuffix);
        Assert.Equal(provider.OneClickHeaders.Length, provider.OneClickFieldNames.Length);
        Assert.Equal(provider.OneClickHeaders.Length, provider.OneClickColumnWidths.Length);
    }

    // ===== HazardExportDataProvider =====

    private string SeedHazardRecordWithPhoto()
    {
        // 建年月/日期分组 + 记录 + 附件文件
        var ym = new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = _tp.ProjectId,
            Module = "Hazard",
            Name = "2026年08月",
            CreatedDate = "2026-08-01"
        };
        _tp.Service.AddGroup(ym);
        var date = new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = _tp.ProjectId,
            Module = "Hazard",
            ParentId = ym.Id,
            Name = "08月15日",
            CreatedDate = "2026-08-15"
        };
        _tp.Service.AddGroup(date);
        var record = new Record
        {
            Id = Guid.NewGuid().ToString("N"),
            GroupId = date.Id,
            Title = "隐患记录",
            YearMonth = "2026-08",
            Status = "进行",
            Location = "工地A",
            HazardType = "临边防护",
            Description = "描述",
            Measure = "措施"
        };
        _tp.Service.AddRecord(record);

        // 附件文件
        string cardDir = Path.Combine(_tp.Service.GetProjectsDateRoot(),
            $"Project_{_tp.ProjectId}", "Hazard_2026-08", $"Card_{record.Id}");
        Directory.CreateDirectory(cardDir);
        string photoFile = Path.Combine(cardDir, "photo.jpg");
        File.WriteAllText(photoFile, "img");
        _tp.Service.AddAttachment(new Attachment
        {
            Id = Guid.NewGuid().ToString("N"),
            RecordId = record.Id,
            FileName = "photo.jpg",
            RelativePath = $"Project_{_tp.ProjectId}/Hazard_2026-08/Card_{record.Id}/photo.jpg",
            AttachmentType = "Hazard",
            DisplayOrder = 1
        });
        return record.Id;
    }

    [Fact]
    public void 隐患提供者_文本取值映射()
    {
        var row = new MonthlyRecordRow
        {
            RowNumber = 1,
            RecordId = "r1",
            RecordDate = new DateTime(2026, 8, 15),
            Status = "进行",
            ResponsibleUnit = "施工单位",
            Location = "工地A",
            HazardType = "临边防护",
            Deadline = new DateTime(2026, 8, 20),
            CompletedDate = new DateTime(2026, 8, 18),
            Description = "描述",
            Measure = "措施",
            HazardPhotoCount = 2,
            RemedyPhotoCount = 1
        };
        var provider = new HazardExportDataProvider(new List<MonthlyRecordRow> { row }, _tp.Service);

        Assert.Equal("进行", provider.GetTextValue(0, "Status"));
        Assert.Equal("1", provider.GetTextValue(0, "RowNumber"));
        Assert.Equal("2026-08-15", provider.GetTextValue(0, "RecordDate"));
        Assert.Equal("08-20", provider.GetTextValue(0, "Deadline")); // 期限显示 MM-dd
        Assert.Equal("2", provider.GetTextValue(0, "HazardPhotoCount"));
        Assert.Equal("", provider.GetTextValue(0, "未知字段"));
    }

    [Fact]
    public void 隐患提供者_图片路径_只返回存在的文件()
    {
        string recordId = SeedHazardRecordWithPhoto();
        var row = new MonthlyRecordRow { RowNumber = 1, RecordId = recordId };
        var provider = new HazardExportDataProvider(new List<MonthlyRecordRow> { row }, _tp.Service);
        string attachmentsRoot = _tp.Service.GetProjectsDateRoot();

        var hazardPhotos = provider.GetPhotoPaths(0, "HazardPhoto", attachmentsRoot);
        Assert.Single(hazardPhotos); // photo.jpg 存在
        Assert.True(File.Exists(hazardPhotos[0]));

        // 整改照片类型无附件 → 空
        Assert.Empty(provider.GetPhotoPaths(0, "RemedyPhoto", attachmentsRoot));
    }
}
