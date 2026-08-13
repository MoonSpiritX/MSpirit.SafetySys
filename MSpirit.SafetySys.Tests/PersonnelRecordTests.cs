// 人员记录（PersonnelRecords 表）核心行为测试：
// 增删改查、项目过滤查询、DisplayIndex、分页

namespace MSpirit.SafetySys.Tests;

public class PersonnelRecordTests
{
    private static string CreateEnv(TestProject tp)
    {
        var unit = tp.AddPersonnelUnitGroup("一建公司");
        var dept = new MS_SafetyManagementSystem.Models.Group
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = tp.ProjectId,
            Module = "Personnel",
            ParentId = unit.Id,
            Name = "机电班组"
        };
        Assert.True(tp.Service.AddGroup(dept));
        return dept.Id;
    }

    [Fact]
    public void AddPersonnelRecord_新增人员_字段完整落库()
    {
        using var tp = new TestProject();
        var groupId = CreateEnv(tp);

        var p = new MS_SafetyManagementSystem.Models.PersonnelRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            GroupId = groupId,
            Title = "张三",
            Gender = "男",
            PhoneNumber = "13800000000",
            ThreeCategoryPersonnel = true,
            SpecialOperationsPersonnel = false,
            IDCardDocNumber = "110101199001011234"
        };
        Assert.True(tp.Service.AddPersonnelRecord(p));

        var records = tp.Service.GetPersonnelRecordsByGroup(groupId);
        var loaded = Assert.Single(records);
        Assert.Equal("张三", loaded.Title);
        Assert.Equal("男", loaded.Gender);
        Assert.True(loaded.ThreeCategoryPersonnel);
        Assert.Equal("110101199001011234", loaded.IDCardDocNumber);
        Assert.False(string.IsNullOrEmpty(loaded.CreatedAt));
    }

    [Fact]
    public void GetAllPersonnelRecords_只返回当前项目数据()
    {
        using var tp1 = new TestProject();
        var g1 = CreateEnv(tp1);
        tp1.AddPersonnel(g1, "甲班组-张三");

        using var tp2 = new TestProject();
        var g2 = CreateEnv(tp2);
        tp2.AddPersonnel(g2, "乙班组-李四");

        // 项目隔离：各自只返回自己的记录
        var all1 = tp1.Service.GetAllPersonnelRecords();
        var all2 = tp2.Service.GetAllPersonnelRecords();
        Assert.Single(all1);
        Assert.Single(all2);
        Assert.Equal("甲班组-张三", all1[0].Title);
        Assert.Equal("乙班组-李四", all2[0].Title);
    }

    [Fact]
    public void UpdatePersonnelRecord_修改信息_生效()
    {
        using var tp = new TestProject();
        var groupId = CreateEnv(tp);
        var p = tp.AddPersonnel(groupId, "张三");

        p.Position = "安全员";
        p.PhoneNumber = "13900000000";
        Assert.True(tp.Service.UpdatePersonnelRecord(p));

        var loaded = tp.Service.GetPersonnelRecord(p.Id)!;
        Assert.Equal("安全员", loaded.Position);
        Assert.Equal("13900000000", loaded.PhoneNumber);
    }

    [Fact]
    public void GetPersonnelRecord_不存在_返回null()
    {
        using var tp = new TestProject();
        Assert.Null(tp.Service.GetPersonnelRecord(Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public void DeletePersonnelRecord_删除人员()
    {
        using var tp = new TestProject();
        var groupId = CreateEnv(tp);
        var p = tp.AddPersonnel(groupId, "待删除");

        Assert.True(tp.Service.DeletePersonnelRecord(p.Id));
        Assert.Null(tp.Service.GetPersonnelRecord(p.Id));
        Assert.Equal(0, tp.Service.GetPersonnelRecordsCountByGroup(groupId));
    }

    [Fact]
    public void GetMaxPersonnelDisplayIndex_初始为0_插入后返回最大值()
    {
        using var tp = new TestProject();
        var groupId = CreateEnv(tp);

        Assert.Equal(0, tp.Service.GetMaxPersonnelDisplayIndex(groupId));

        var p1 = tp.AddPersonnel(groupId, "甲");
        p1.DisplayIndex = 2;
        tp.Service.UpdatePersonnelRecord(p1);

        var p2 = tp.AddPersonnel(groupId, "乙");
        p2.DisplayIndex = 7;
        tp.Service.UpdatePersonnelRecord(p2);

        Assert.Equal(7, tp.Service.GetMaxPersonnelDisplayIndex(groupId));
    }

    [Fact]
    public void GetPersonnelRecordsByGroupPaged_分页正确()
    {
        using var tp = new TestProject();
        var groupId = CreateEnv(tp);
        for (int i = 0; i < 4; i++)
        {
            var p = tp.AddPersonnel(groupId, $"人员{i}");
            p.DisplayIndex = i;
            tp.Service.UpdatePersonnelRecord(p);
        }

        var page1 = tp.Service.GetPersonnelRecordsByGroupPaged(groupId, 0, 3);
        Assert.Equal(3, page1.Count);

        var page2 = tp.Service.GetPersonnelRecordsByGroupPaged(groupId, 3, 3);
        Assert.Single(page2);
        Assert.Equal("人员3", page2[0].Title);
    }
}
