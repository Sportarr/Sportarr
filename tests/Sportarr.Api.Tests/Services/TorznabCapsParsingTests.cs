using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

public class TorznabCapsParsingTests
{
    // Mirrors Prowlarr's caps shape: standard categories with nested subcats,
    // plus indexer-specific (100000+) forum categories that also nest.
    private const string NestedCapsXml = """
        <caps>
          <searching>
            <search available="yes" supportedParams="q"/>
            <tv-search available="yes" supportedParams="q,season,ep"/>
          </searching>
          <categories>
            <category id="5000" name="TV">
              <subcat id="5030" name="TV/SD"/>
              <subcat id="5040" name="TV/HD"/>
              <subcat id="5045" name="TV/UHD"/>
            </category>
            <category id="102145" name="|- Футбол">
              <subcat id="102147" name="|- Чемпионат Мира 2026"/>
            </category>
            <category id="100136" name="|- Хоккей"/>
          </categories>
        </caps>
        """;

    private static TorznabCapabilities Parse(string xml)
    {
        var caps = new TorznabCapabilities();
        TorznabClient.ParseCapabilitiesXml(xml, caps);
        return caps;
    }

    [Fact]
    public void NestedSubcategories_AreIncluded()
    {
        var caps = Parse(NestedCapsXml);
        var ids = caps.Categories.Select(c => c.Id).ToList();

        Assert.Contains("5000", ids);
        Assert.Contains("5030", ids);
        Assert.Contains("5040", ids);
        Assert.Contains("5045", ids);
        Assert.Contains("102145", ids);
        Assert.Contains("102147", ids);
        Assert.Contains("100136", ids);
    }

    [Fact]
    public void StandardSubcats_KeepTheirFullNames()
    {
        var caps = Parse(NestedCapsXml);

        // "TV/HD" already contains the parent name, so no prefixing.
        Assert.Equal("TV/HD", caps.Categories.Single(c => c.Id == "5040").Name);
    }

    [Fact]
    public void IndexerForumSubcats_GetParentContext()
    {
        var caps = Parse(NestedCapsXml);

        // Forum names don't repeat the parent, so it gets prefixed for the picker.
        Assert.Equal("|- Футбол / |- Чемпионат Мира 2026", caps.Categories.Single(c => c.Id == "102147").Name);
    }

    [Fact]
    public void DuplicateIds_AreNotAddedTwice()
    {
        var xml = """
            <caps>
              <categories>
                <category id="5000" name="TV">
                  <subcat id="5040" name="TV/HD"/>
                </category>
                <category id="5040" name="TV/HD"/>
              </categories>
            </caps>
            """;
        var caps = Parse(xml);

        Assert.Single(caps.Categories.Where(c => c.Id == "5040"));
    }

    [Fact]
    public void SearchingFlags_StillParse()
    {
        var caps = Parse(NestedCapsXml);

        Assert.True(caps.SearchAvailable);
        Assert.True(caps.TvSearchAvailable);
    }
}
