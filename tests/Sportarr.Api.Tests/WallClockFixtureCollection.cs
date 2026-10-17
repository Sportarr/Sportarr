namespace Sportarr.Api.Tests;

// Keep tests with real-time deadlines away from competing test load.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WallClockFixtureCollection
{
    public const string Name = "Wall-clock fixtures";
}
