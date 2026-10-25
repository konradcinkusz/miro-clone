using MiroClone.Web.Canvas.Core;
using Xunit;

namespace MiroClone.Web.Tests.Canvas.Core;

public sealed class MutationApplierTests
{
    private static readonly Guid[] Ids = Fixtures.Pool(8);

    [Fact]
    public void ANewLayerIsAppendedOnTopAndTheInverseDeletesIt()
    {
        var document = CanvasDocument.Load([Fixtures.Rectangle(Ids[0], 0, 0, 10, 10)]);

        var outcome = MutationApplier.Apply(document, LayerMutation.Upsert(Fixtures.Rectangle(Ids[1], 5, 5, 10, 10)));

        Assert.Equal(MutationResult.Applied, outcome.Result);
        Assert.Equal([Ids[0], Ids[1]], document.Order);
        Assert.Equal([LayerMutation.Delete(Ids[1])], outcome.Inverse);
    }

    [Fact]
    public void AnUpsertOfTheSameContentChangesNothing()
    {
        var layer = Fixtures.Rectangle(Ids[0], 0, 0, 10, 10);
        var document = CanvasDocument.Load([layer]);

        var outcome = MutationApplier.Apply(document, LayerMutation.Upsert(layer));

        Assert.Equal(MutationResult.Unchanged, outcome.Result);
        Assert.Empty(outcome.Inverse);
    }

    [Fact]
    public void AChangedLayerIsReplacedInPlaceAndTheInverseRestoresTheOldOne()
    {
        var before = Fixtures.Rectangle(Ids[0], 0, 0, 10, 10);
        var after = before with { X = 40 };
        var document = CanvasDocument.Load([before, Fixtures.Rectangle(Ids[1], 0, 0, 1, 1)]);

        var outcome = MutationApplier.Apply(document, LayerMutation.Upsert(after));

        Assert.Equal(MutationResult.Applied, outcome.Result);
        Assert.Equal([Ids[0], Ids[1]], document.Order);
        Assert.Equal(40, document.Ordered().First().X);
        Assert.Equal([LayerMutation.Upsert(before)], outcome.Inverse);
    }

    [Fact]
    public void ANewLayerIsRefusedAtTheLimitButAnExistingOneStillUpdates()
    {
        var layers = Enumerable.Range(0, CanvasDocument.MaxLayers)
            .Select(i => Fixtures.Rectangle(new Guid(i + 1, 1, 0, new byte[8]), i, 0, 1, 1))
            .ToArray();
        var document = CanvasDocument.Load(layers);

        var refused = MutationApplier.Apply(document, LayerMutation.Upsert(Fixtures.Rectangle(Ids[7], 0, 0, 1, 1)));
        var updated = MutationApplier.Apply(document, LayerMutation.Upsert(layers[0] with { X = 9 }));

        Assert.Equal(MutationResult.LimitReached, refused.Result);
        Assert.Empty(refused.Inverse);
        Assert.Equal(CanvasDocument.MaxLayers, document.Count);
        Assert.Equal(MutationResult.Applied, updated.Result);
    }

    [Fact]
    public void DeletingALayerAndUndoingItRestoresItsPlace()
    {
        var document = CanvasDocument.Load(
        [
            Fixtures.Rectangle(Ids[0], 0, 0, 1, 1),
            Fixtures.Rectangle(Ids[1], 0, 0, 1, 1),
            Fixtures.Rectangle(Ids[2], 0, 0, 1, 1),
        ]);

        var outcome = MutationApplier.Apply(document, LayerMutation.Delete(Ids[1]));
        Assert.Equal([Ids[0], Ids[2]], document.Order);
        Assert.False(document.Contains(Ids[1]));

        foreach (var inverse in outcome.Inverse)
        {
            MutationApplier.Apply(document, inverse);
        }

        Assert.Equal([Ids[0], Ids[1], Ids[2]], document.Order);
    }

    [Fact]
    public void DeletingAMissingLayerChangesNothing()
    {
        var document = new CanvasDocument();

        Assert.Equal(MutationResult.Unchanged, MutationApplier.Apply(document, LayerMutation.Delete(Ids[0])).Result);
    }

    [Fact]
    public void ReorderKeepsKnownIdsInTheRequestedOrderAndIgnoresUnknownOnes()
    {
        var document = CanvasDocument.Load(
        [
            Fixtures.Rectangle(Ids[0], 0, 0, 1, 1),
            Fixtures.Rectangle(Ids[1], 0, 0, 1, 1),
            Fixtures.Rectangle(Ids[2], 0, 0, 1, 1),
        ]);

        var outcome = MutationApplier.Apply(document, LayerMutation.Reorder([Ids[2], Ids[5], Ids[0], Ids[2]]));

        Assert.Equal(MutationResult.Applied, outcome.Result);
        Assert.Equal([Ids[2], Ids[0], Ids[1]], document.Order);
        var inverse = Assert.Single(outcome.Inverse);
        Assert.Equal(LayerMutationKind.Reorder, inverse.Kind);
        Assert.Equal([Ids[0], Ids[1], Ids[2]], inverse.Order);
    }

    [Fact]
    public void ReorderOfTheCurrentOrderChangesNothing()
    {
        var document = CanvasDocument.Load([Fixtures.Rectangle(Ids[0], 0, 0, 1, 1), Fixtures.Rectangle(Ids[1], 0, 0, 1, 1)]);

        Assert.Equal(MutationResult.Unchanged, MutationApplier.Apply(document, LayerMutation.Reorder([Ids[0], Ids[1]])).Result);
    }

    [Fact]
    public void MalformedMutationsAreRejectedWithoutChangingTheDocument()
    {
        var document = CanvasDocument.Load([Fixtures.Rectangle(Ids[0], 0, 0, 1, 1)]);
        var malformed = new[]
        {
            new LayerMutation(LayerMutationKind.Upsert),
            new LayerMutation(LayerMutationKind.Delete),
            new LayerMutation(LayerMutationKind.Reorder),
            new LayerMutation((LayerMutationKind)99),
            LayerMutation.Upsert(Fixtures.Rectangle(Ids[1], double.NaN, 0, 1, 1)),
            LayerMutation.Upsert(Fixtures.Rectangle(Ids[1], 0, 0, -1, 1)),
            LayerMutation.Upsert(new Layer(Ids[1], LayerType.Path, 0, 0, 1, 1, Fixtures.Red)),
            LayerMutation.Upsert(new Layer(Ids[1], LayerType.Path, 0, 0, 1, 1, Fixtures.Red, null, [new Point(double.PositiveInfinity, 0)])),
            LayerMutation.Upsert(new Layer(Ids[1], LayerType.Rectangle, 0, 0, 1, 1, Fixtures.Red, null, [new Point(0, 0)])),
        };

        foreach (var mutation in malformed)
        {
            var outcome = MutationApplier.Apply(document, mutation);

            Assert.Equal(MutationResult.Invalid, outcome.Result);
            Assert.Empty(outcome.Inverse);
        }

        Assert.Equal([Ids[0]], document.Order);
    }

    [Fact]
    public void ANullArgumentIsRejectedAsAProgrammingError()
    {
        var document = new CanvasDocument();

        Assert.Throws<ArgumentNullException>(() => MutationApplier.Apply(null!, LayerMutation.Delete(Ids[0])));
        Assert.Throws<ArgumentNullException>(() => MutationApplier.Apply(document, null!));
        Assert.Throws<ArgumentNullException>(() => LayerMutation.Upsert(null!));
        Assert.Throws<ArgumentNullException>(() => LayerMutation.Reorder(null!));
    }

    [Fact]
    public void EveryMutationIsIdempotent()
    {
        var random = new TestRandom(61);
        for (var i = 0; i < Fixtures.Iterations; i++)
        {
            var document = Fixtures.RandomDocument(random, Ids);
            var mutation = Fixtures.RandomMutation(random, Ids);

            MutationApplier.Apply(document, mutation);
            var once = Fixtures.Copy(document);
            var second = MutationApplier.Apply(document, mutation);

            Assert.Equal(MutationResult.Unchanged, second.Result);
            Fixtures.AssertSameDocument(once, document);
        }
    }

    [Fact]
    public void EveryInverseRestoresTheDocumentItUndid()
    {
        var random = new TestRandom(67);
        for (var i = 0; i < Fixtures.Iterations; i++)
        {
            var document = Fixtures.RandomDocument(random, Ids);
            var before = Fixtures.Copy(document);

            var outcome = MutationApplier.Apply(document, Fixtures.RandomMutation(random, Ids));
            // An inverse may be a no-op when the step already restored that part (a reorder back to the order it had).
            foreach (var inverse in outcome.Inverse)
            {
                var result = MutationApplier.Apply(document, inverse).Result;
                Assert.True(result is MutationResult.Applied or MutationResult.Unchanged, $"inverse was {result}");
            }

            if (outcome.Result is MutationResult.Applied)
            {
                Fixtures.AssertSameDocument(before, document);
            }
        }
    }

    [Fact]
    public void LoadRejectsDuplicateIdsAndMoreThanTheLimit()
    {
        var layer = Fixtures.Rectangle(Ids[0], 0, 0, 1, 1);

        Assert.Throws<ArgumentException>(() => CanvasDocument.Load([layer, layer]));
        Assert.Throws<ArgumentException>(() => CanvasDocument.Load(
            Enumerable.Range(0, CanvasDocument.MaxLayers + 1).Select(i => Fixtures.Rectangle(new Guid(i + 1, 2, 0, new byte[8]), 0, 0, 1, 1))));
    }

    [Fact]
    public void ALoadedDocumentReportsItsLayersBottomToTop()
    {
        var document = CanvasDocument.Load([Fixtures.Rectangle(Ids[3], 0, 0, 1, 1), Fixtures.Rectangle(Ids[4], 0, 0, 2, 2)]);

        Assert.Equal(2, document.Count);
        Assert.Equal([Ids[3], Ids[4]], document.Ordered().Select(l => l.Id));
        Assert.True(document.TryGet(Ids[4], out var top));
        Assert.Equal(2, top.Width);
        Assert.False(document.TryGet(Ids[5], out _));
    }
}
