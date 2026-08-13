using Xunit;

namespace JYPPX.ROCm.MIGraphXSharp.UnitTests;

public sealed class BuildInfoTests
{
    [Fact]
    public void M1ReportsEngineeringVersionAndFrozenBindingAvailability()
    {
        Assert.Equal("0.0.0", MIGraphXBuildInfo.PackageVersion);
        Assert.True(MIGraphXBuildInfo.NativeBindingsAvailable);
    }
}
