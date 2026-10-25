using MiroClone.Web.Canvas.Core;
using Xunit;

namespace MiroClone.Web.Tests.Canvas.Core;

/// <summary>
/// A seeded xorshift generator for property-style tests. Same seed, same sequence, so a failing case reproduces.
/// It is not used for anything security-sensitive, so System.Random's analyzer rule is not a concern here.
/// </summary>
internal sealed class TestRandom(ulong seed)
{
    private ulong _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    public int Next(int maxExclusive) => (int)(NextBits() % (ulong)maxExclusive);

    public int Next(int minInclusive, int maxExclusive) => minInclusive + Next(maxExclusive - minInclusive);

    public double NextDouble() => (NextBits() >> 11) * (1.0 / (1UL << 53));

    public double Between(double minInclusive, double maxExclusive) => minInclusive + (NextDouble() * (maxExclusive - minInclusive));

    private ulong NextBits()
    {
        _state ^= _state << 13;
        _state ^= _state >> 7;
        _state ^= _state << 17;
        return _state;
    }
}

internal static class Fixtures
{
    public const int Iterations = 300;

    public static readonly Rgb Red = new(255, 0, 0);

    /// <summary>Distinct, stable ids: the same pool every run.</summary>
    public static Guid[] Pool(int count) =>
        Enumerable.Range(1, count).Select(i => new Guid(i, 0, 0, new byte[8])).ToArray();

    public static Layer Rectangle(Guid id, double x, double y, double width, double height) =>
        new(id, LayerType.Rectangle, x, y, width, height, Red);

    public static double Coordinate(TestRandom random) => random.Between(-100, 300);

    public static double Size(TestRandom random) => random.Between(1, 150);

    public static Point RandomPoint(TestRandom random) => new(Coordinate(random), Coordinate(random));

    public static Rgb RandomRgb(TestRandom random) => new((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));

    public static Layer RandomLayer(TestRandom random, Guid id)
    {
        var fill = RandomRgb(random);
        var type = (LayerType)random.Next(5);
        if (type == LayerType.Path)
        {
            var points = Enumerable.Range(0, random.Next(1, 6)).Select(_ => RandomPoint(random)).ToArray();
            return LayerFactory.Path(id, points, fill);
        }

        var value = type is LayerType.Text or LayerType.Note ? "text" : null;
        return new Layer(id, type, Coordinate(random), Coordinate(random), Size(random), Size(random), fill, value);
    }

    /// <summary>A document holding a random subset of the pool, in random order.</summary>
    public static CanvasDocument RandomDocument(TestRandom random, Guid[] pool)
    {
        var chosen = Shuffle(random, pool).Take(random.Next(pool.Length + 1));
        return CanvasDocument.Load(chosen.Select(id => RandomLayer(random, id)));
    }

    public static LayerMutation RandomMutation(TestRandom random, Guid[] pool) => random.Next(3) switch
    {
        0 => LayerMutation.Upsert(RandomLayer(random, pool[random.Next(pool.Length)])),
        1 => LayerMutation.Delete(pool[random.Next(pool.Length)]),
        _ => LayerMutation.Reorder(Shuffle(random, pool).Take(random.Next(pool.Length + 1)).ToArray()),
    };

    public static Guid[] Shuffle(TestRandom random, Guid[] ids)
    {
        var copy = (Guid[])ids.Clone();
        for (var i = copy.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }

        return copy;
    }

    public static CanvasDocument Copy(CanvasDocument document) => CanvasDocument.Load(document.Ordered());

    /// <summary>Same order, and every layer holds the same content.</summary>
    public static void AssertSameDocument(CanvasDocument expected, CanvasDocument actual)
    {
        Assert.Equal(expected.Order, actual.Order);
        Assert.Equal(expected.Count, actual.Count);
        foreach (var id in expected.Order)
        {
            Assert.True(expected.TryGet(id, out var left));
            Assert.True(actual.TryGet(id, out var right));
            Assert.True(left.HasSameContent(right), $"Layer {id} differs.");
        }
    }
}
