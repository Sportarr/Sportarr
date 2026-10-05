namespace Sportarr.Api.Tests;

// Tests that start a local server or a file-backed database and then hold
// wall-clock deadlines: a search through a fake indexer, a reaper pass, a
// queued qBittorrent login. Run alongside each other and the rest of the
// suite, a busy machine pushed them past those deadlines, and they failed at
// random, a different one each run. One at a time, each deadline measures the
// code under test rather than the load beside it.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WallClockFixtureCollection
{
    public const string Name = "Wall-clock fixtures";
}
