namespace LegacyFixture;
public sealed class Probe
{
    public Probe()
    {
        var path = System.Environment.GetEnvironmentVariable("SHIROBOT_ABI_FIXTURE_SENTINEL");
        if (path != null) System.IO.File.WriteAllText(path, "activated");
    }
    private static void DeferredOnly() => LegacyHelper.Helper.Run();
}
