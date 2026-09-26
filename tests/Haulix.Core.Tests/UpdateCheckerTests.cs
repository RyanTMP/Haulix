using Haulix.Core.Services;

namespace Haulix.Core.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("0.0.9.1-beta", "0.0.9-beta")] // hotfix (shown as "0.0.9-1 BETA") is newer than the release it fixes
    [InlineData("0.0.10-beta", "0.0.9.1-beta")]
    [InlineData("0.0.9-beta", "0.0.8-beta")]
    [InlineData("1.0.0", "1.0.0-beta")]
    public void NewerVersionIsOffered(string latest, string current)
    {
        Assert.True(UpdateChecker.Compare(latest, current) > 0);
        Assert.True(UpdateChecker.Compare(current, latest) < 0);
    }

    [Fact]
    public void SameVersionIsNotAnUpdate() => Assert.Equal(0, UpdateChecker.Compare("v0.0.9.1-beta", "0.0.9.1-beta"));
}
