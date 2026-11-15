using LidarViewer.Core.Spatial;

namespace LidarViewer.Core.Tests;

public class KdTreeTests
{
    private static float[] RandomCloud(int count, int seed)
    {
        var random = new Random(seed);
        var positions = new float[count * 3];
        for (int i = 0; i < positions.Length; i++)
            positions[i] = (float)(random.NextDouble() * 100 - 50);

        return positions;
    }

    private static double BruteForceSum(float[] positions, int count, int point, int k)
    {
        var distances = new List<double>();
        for (int j = 0; j < count; j++)
        {
            if (j == point) continue;

            double dx = positions[j * 3] - positions[point * 3];
            double dy = positions[j * 3 + 1] - positions[point * 3 + 1];
            double dz = positions[j * 3 + 2] - positions[point * 3 + 2];
            distances.Add(Math.Sqrt(dx * dx + dy * dy + dz * dz));
        }

        distances.Sort();
        double sum = 0;
        for (int i = 0; i < k && i < distances.Count; i++) sum += distances[i];

        return sum;
    }

    [Fact]
    public void CountMatchesInput()
    {
        var positions = RandomCloud(64, seed: 7);

        var tree = new KdTree(positions, 64);

        Assert.Equal(64, tree.Count);
    }

    [Fact]
    public void EmptyTreeHasNoNodes()
    {
        var tree = new KdTree([], 0);

        Assert.Equal(0, tree.Count);
    }

    [Fact]
    public void NonPositiveKReturnsZero()
    {
        var positions = RandomCloud(16, seed: 3);
        var tree = new KdTree(positions, 16);
        var buffer = new double[4];

        Assert.Equal(0, tree.SumOfNearestDistances(0, 0, buffer));
        Assert.Equal(0, tree.SumOfNearestDistances(0, -3, buffer));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(9)]
    public void MatchesBruteForceNearestDistances(int k)
    {
        var positions = RandomCloud(200, seed: 11);
        var tree = new KdTree(positions, 200);
        var buffer = new double[k];

        for (int i = 0; i < 200; i++)
        {
            double expected = BruteForceSum(positions, 200, i, k);
            double actual = tree.SumOfNearestDistances(i, k, buffer);

            Assert.Equal(expected, actual, 4);
        }
    }

    [Fact]
    public void ExcludesThePointItself()
    {
        var positions = new float[] { 1f, 2f, 3f };
        var tree = new KdTree(positions, 1);
        var buffer = new double[1];

        Assert.Equal(0, tree.SumOfNearestDistances(0, 1, buffer), 6);
    }

    [Fact]
    public void DuplicatePointsGiveZeroDistance()
    {
        var positions = new float[] { 0f, 0f, 0f, 0f, 0f, 0f };
        var tree = new KdTree(positions, 2);
        var buffer = new double[1];

        Assert.Equal(0, tree.SumOfNearestDistances(0, 1, buffer), 6);
    }
}
