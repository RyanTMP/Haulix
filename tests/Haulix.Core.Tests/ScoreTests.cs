using Haulix.Core.Services;
using Xunit;

namespace Haulix.Core.Tests;

public class ScoreTests
{
    [Fact]
    public void CleanDeliveryScoresFull()
    {
        var s = DrivingScore.Compute(3600, 0, 0, 0, 0, false);
        Assert.Equal(100, s.Score);
    }

    [Fact]
    public void PenaltiesAddUpAndAreCapped()
    {
        // 50 % speeding (30 cap), 4 % cargo damage (10), 2 % truck damage (3), 1 fine (8), late (15) → 34
        var s = DrivingScore.Compute(1000, 500, 0.04, 0.02, 1, true);
        Assert.Equal(30, s.SpeedingPenalty);
        Assert.Equal(10, s.CargoPenalty);
        Assert.Equal(3, s.TruckPenalty);
        Assert.Equal(8, s.FinePenalty);
        Assert.Equal(15, s.LatePenalty);
        Assert.Equal(34, s.Score);
    }

    [Fact]
    public void ScoreNeverBelowZero()
    {
        var s = DrivingScore.Compute(1000, 1000, 1, 1, 10, true);
        Assert.Equal(0, s.Score);
    }
}
