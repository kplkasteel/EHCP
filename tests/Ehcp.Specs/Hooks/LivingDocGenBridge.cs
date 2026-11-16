using Reqnroll;
using LivingDocGen.Reqnroll.Integration.Bootstrap;

namespace Ehcp.Specs.Hooks
{
    [Binding]
    public class LivingDocGenBridge
    {
        [BeforeTestRun(Order = int.MinValue)]
        public static void BeforeAllTests() => LivingDocBootstrap.BeforeTestRun();

        [AfterTestRun(Order = int.MaxValue)]
        public static void AfterAllTests() => LivingDocBootstrap.AfterTestRun();
    }
}
