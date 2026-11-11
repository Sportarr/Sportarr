namespace Sportarr.Api.Tests;

// The recording choice gate and claim sets are static. Tests that use them
// run one at a time, so one test never sees the claims of another.
[CollectionDefinition(Name)]
public sealed class DvrChoiceStateCollection
{
    public const string Name = "DVR choice state";
}
