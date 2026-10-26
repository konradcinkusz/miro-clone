using Xunit;

namespace MiroClone.Web.E2E.Stack;

/// <summary>
/// One estate and one browser for the whole suite. The journeys share a PostgreSQL and each creates its own accounts and boards,
/// so they run one after another: parallel journeys on one shared estate would race on the invitation outbox.
/// </summary>
[CollectionDefinition(Name)]
public sealed class EstateGroup : ICollectionFixture<EstateFixture>
{
    public const string Name = "Estate";
}
