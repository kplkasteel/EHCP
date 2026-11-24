namespace Ehcp.Core;

// TEMPORARY: compiler warning to test the build warning output. Remove before merging.
internal static class TemporaryWarningProbe
{
    public static int Value()
    {
        int unused = UndefinedTemporaryName;
        return 1;
    }
}
