// 分组树（Groups 表）核心行为测试：
// 增删改查、Order 自动分配、Hazard/Personnel 排序规则、级联删除、
// Tag 重复检测、GetOrCreateDateGroup 幂等、年月解析

using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;

namespace MSpirit.SafetySys.Tests;

public class GroupTests
{
    [Fact]
    public void AddGroup_顶级年月组_可查询且字段完整()
    {
        using var tp = new TestProject();
        var g = tp.AddYearMonthGroup("2026年08月");

        var groups = tp.Service.GetGroups("Hazard");
        var loaded = Assert.Single(groups);
        Assert.Equal(g.Id, loaded.Id);
        Assert.Equal("2026年08月", loaded.Name);
        Assert.Equal(tp.ProjectId, loaded.ProjectId);
        Assert.Null(loaded.ParentId);
        Assert.Equal(0, loaded.Order); // 首个分组 Order 从 0 开始
    }

    [Fact]
    public void AddGroup_同级分组_Order自动递增()
    {
        using var tp = new TestProject();
        tp.AddYearMonthGroup("2026年08月");
        tp.AddYearMonthGroup("2026年09月");
        tp.AddYearMonthGroup("2026年10月");

        var groups = tp.Service.GetGroups("Hazard");
        Assert.Equal(3, groups.Count);
        Assert.Equal(new[] { 0, 1, 2 }, groups.OrderBy(x => x.Order).Select(x => x.Order).ToArray());
    }

    [Fact]
    public void GetGroups_子分组_按父级过滤()
    {
        using var tp = new TestProject();
        var ym = tp.AddYearMonthGroup("2026年08月");
        var d1 = tp.AddDateGroup(ym.Id, "08月05日");
        tp.AddDateGroup(ym.Id, "08月06日");

        var children = tp.Service.GetGroups("Hazard", ym.Id);
        Assert.Equal(2, children.Count);
        Assert.Contains(children, c => c.Id == d1.Id);
        Assert.All(children, c => Assert.Equal(ym.Id, c.ParentId));

        // 顶级分组不受子分组影响
        var top = tp.Service.GetGroups("Hazard");
        Assert.Single(top);
    }

    [Fact]
    public void GetGroups_Hazard模块_顶级按名称降序()
    {
        using var tp = new TestProject();
        tp.AddYearMonthGroup("2026年07月");
        tp.AddYearMonthGroup("2026年09月");
        tp.AddYearMonthGroup("2026年08月");

        var groups = tp.Service.GetGroups("Hazard");
        // 年月倒序：9月 > 8月 > 7月（字符串降序）
        Assert.Equal(
            new[] { "2026年09月", "2026年08月", "2026年07月" },
            groups.Select(x => x.Name).ToArray());
    }

    [Fact]
    public void GetGroups_Personnel模块_顶级按Order升序()
    {
        using var tp = new TestProject();
        var unit1 = tp.AddPersonnelUnitGroup("一建");
        var unit2 = tp.AddPersonnelUnitGroup("二建");
        var unit3 = tp.AddPersonnelUnitGroup("三建");

        // 按添加顺序（Order 0,1,2）返回
        var groups = tp.Service.GetGroups("Personnel");
        Assert.Equal(new[] { unit1.Id, unit2.Id, unit3.Id }, groups.Select(x => x.Id).ToArray());
    }

    [Fact]
    public void UpdateGroup_修改名称与标签_生效()
    {
        using var tp = new TestProject();
        var ym = tp.AddYearMonthGroup("2026年08月");
        var d = tp.AddDateGroup(ym.Id, "08月05日", tag: "上午");

        d.Name = "08月05日（整改）";
        d.Tag = "下午";
        Assert.True(tp.Service.UpdateGroup(d));

        var loaded = tp.Service.GetGroups("Hazard", ym.Id).Single(x => x.Id == d.Id);
        Assert.Equal("08月05日（整改）", loaded.Name);
        Assert.Equal("下午", loaded.Tag);
    }

    [Fact]
    public void DeleteGroup_顶级分组_级联删除子分组()
    {
        using var tp = new TestProject();
        var ym = tp.AddYearMonthGroup("2026年08月");
        var d1 = tp.AddDateGroup(ym.Id, "08月05日");
        var d2 = tp.AddDateGroup(ym.Id, "08月06日");

        Assert.True(tp.Service.DeleteGroup(ym.Id));

        Assert.Empty(tp.Service.GetGroups("Hazard"));
        Assert.Empty(tp.Service.GetGroups("Hazard", ym.Id));
        Assert.DoesNotContain(tp.Service.GetAllGroups("Hazard"), g => g.Id == d1.Id);
    }

    [Fact]
    public void DeleteGroup_级联删除分组下记录()
    {
        using var tp = new TestProject();
        var ym = tp.AddYearMonthGroup("2026年08月");
        var d = tp.AddDateGroup(ym.Id, "08月05日");
        var rec = tp.AddRecord(d.Id, "配电箱未接地");

        Assert.True(tp.Service.DeleteGroup(ym.Id));

        Assert.Null(tp.Service.GetRecordById(rec.Id)); // 记录随分组级联删除
    }

    [Fact]
    public void DeleteGroup_不存在ID_返回false()
    {
        using var tp = new TestProject();
        Assert.False(tp.Service.DeleteGroup(Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public void IsTagDuplicate_同名同Tag_判重()
    {
        using var tp = new TestProject();
        var ym = tp.AddYearMonthGroup("2026年08月");
        tp.AddDateGroup(ym.Id, "08月05日", tag: "上午");

        Assert.True(tp.Service.IsTagDuplicate(ym.Id, "08月05日", "上午"));
        Assert.False(tp.Service.IsTagDuplicate(ym.Id, "08月05日", "下午")); // 不同 Tag 不冲突
        Assert.False(tp.Service.IsTagDuplicate(ym.Id, "08月06日", "上午")); // 不同名称不冲突
    }

    [Fact]
    public void IsTagDuplicate_排除自身()
    {
        using var tp = new TestProject();
        var ym = tp.AddYearMonthGroup("2026年08月");
        var d = tp.AddDateGroup(ym.Id, "08月05日", tag: "上午");

        // 编辑场景：排除自身后不应判重
        Assert.False(tp.Service.IsTagDuplicate(ym.Id, "08月05日", "上午", excludeGroupId: d.Id));
    }

    [Fact]
    public void GetOrCreateDateGroup_自动创建年月与日期分组()
    {
        using var tp = new TestProject();
        var groupId = tp.Service.GetOrCreateDateGroup("2026-08", 5, tag: null);

        // 年月分组自动创建
        var top = tp.Service.GetGroups("Hazard");
        var ym = Assert.Single(top);
        Assert.Equal("2026年08月", ym.Name);

        // 日期分组自动创建
        var children = tp.Service.GetGroups("Hazard", ym.Id);
        var date = Assert.Single(children);
        Assert.Equal("08月05日", date.Name);
        Assert.Equal(groupId, date.Id);
    }

    [Fact]
    public void GetOrCreateDateGroup_幂等_重复调用返回同一ID()
    {
        using var tp = new TestProject();
        var first = tp.Service.GetOrCreateDateGroup("2026-08", 5, tag: null);
        var second = tp.Service.GetOrCreateDateGroup("2026-08", 5, tag: null);

        Assert.Equal(first, second);
    }

    [Fact]
    public void GetOrCreateDateGroup_不同Tag_创建不同分组()
    {
        using var tp = new TestProject();
        var a = tp.Service.GetOrCreateDateGroup("2026-08", 5, tag: "上午");
        var b = tp.Service.GetOrCreateDateGroup("2026-08", 5, tag: "下午");

        Assert.NotEqual(a, b);
        Assert.Equal(2, tp.Service.GetGroups("Hazard", tp.Service.GetGroups("Hazard")[0].Id).Count);
    }

    [Fact]
    public void GetOrCreateDateGroup_无效年月_抛出异常()
    {
        using var tp = new TestProject();
        Assert.Throws<InvalidOperationException>(() => tp.Service.GetOrCreateDateGroup("abc", 5, tag: null));
    }

    [Fact]
    public void GetYearMonthFromGroupId_日期子分组_从父级解析年月()
    {
        using var tp = new TestProject();
        var ym = tp.AddYearMonthGroup("2026年08月");
        var d = tp.AddDateGroup(ym.Id, "08月05日");

        Assert.Equal("2026-08", tp.Service.GetYearMonthFromGroupId(d.Id));
    }

    [Fact]
    public void GetYearMonthFromGroupId_顶级年月组_直接解析()
    {
        using var tp = new TestProject();
        var ym = tp.AddYearMonthGroup("2026年08月");

        Assert.Equal("2026-08", tp.Service.GetYearMonthFromGroupId(ym.Id));
    }

    [Fact]
    public void GetYearMonthFromGroupId_非年月命名_返回空串()
    {
        using var tp = new TestProject();
        var g = tp.AddPersonnelUnitGroup("一建公司");

        Assert.Equal("", tp.Service.GetYearMonthFromGroupId(g.Id));
    }

    [Fact]
    public void GetYearMonthFromGroupId_不存在的分组_返回空串()
    {
        using var tp = new TestProject();
        Assert.Equal("", tp.Service.GetYearMonthFromGroupId(Guid.NewGuid().ToString("N")));
    }
}
