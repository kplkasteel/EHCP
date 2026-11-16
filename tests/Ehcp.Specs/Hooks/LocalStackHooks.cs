using Reqnroll;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Ehcp.Specs.Hooks;

[Binding]
public static class LocalStackHooks
{
    private static readonly Lazy<Task> Startup = new(LocalTestStack.StartAsync);

    [BeforeTestRun(Order = -10000)]
    public static Task StartAsync() => Startup.Value;

    [AfterTestRun(Order = 10000)]
    public static Task StopAsync() => LocalTestStack.StopAsync();
}
