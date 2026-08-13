// AppExitManager（Services/AppExitManager.cs）退出管理器行为测试：
// 钩子执行顺序 / 异常隔离 / 防重入 / 注册幂等
// 约定：测试环境无 Application.Current，RequestExit 仅执行钩子不真正关应用

using MS_SafetyManagementSystem.Services;

namespace MSpirit.SafetySys.Tests;

public class AppExitManagerTests : IDisposable
{
    public AppExitManagerTests() => AppExitManager.ResetForTests();

    public void Dispose() => AppExitManager.ResetForTests();

    [Fact]
    public void RequestExit_按注册顺序执行所有钩子()
    {
        var order = new List<string>();
        AppExitManager.RegisterCleanup(() => order.Add("A"));
        AppExitManager.RegisterCleanup(() => order.Add("B"));
        AppExitManager.RegisterCleanup(() => order.Add("C"));

        AppExitManager.RequestExit();

        Assert.Equal(new[] { "A", "B", "C" }, order);
    }

    [Fact]
    public void RequestExit_钩子抛异常_不影响其他钩子执行()
    {
        var order = new List<string>();
        AppExitManager.RegisterCleanup(() =>
        {
            order.Add("A");
            throw new InvalidOperationException("模拟钩子失败");
        });
        AppExitManager.RegisterCleanup(() => order.Add("B"));

        // 不抛异常（异常被隔离），且 B 仍执行
        AppExitManager.RequestExit();

        Assert.Equal(new[] { "A", "B" }, order);
    }

    [Fact]
    public void RequestExit_防重入_第二次请求不再执行钩子()
    {
        var order = new List<string>();
        AppExitManager.RegisterCleanup(() => order.Add("A"));

        AppExitManager.RequestExit();
        AppExitManager.RequestExit(); // 退出流程已开始，应被忽略

        Assert.Single(order);
    }

    [Fact]
    public void RegisterCleanup_同一委托重复注册_只执行一次()
    {
        var order = new List<string>();
        Action cleanup = () => order.Add("A");

        AppExitManager.RegisterCleanup(cleanup);
        AppExitManager.RegisterCleanup(cleanup); // 幂等：忽略重复

        AppExitManager.RequestExit();

        Assert.Equal(new[] { "A" }, order);
    }

    [Fact]
    public void RegisterCleanup_空参数_抛出异常()
    {
        Assert.Throws<ArgumentNullException>(() => AppExitManager.RegisterCleanup(null!));
    }
}
