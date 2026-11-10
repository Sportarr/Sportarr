using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A library import names files through the same template and part tokens
/// as the download import and the rename, so a rename afterwards finds
/// nothing to change.
/// </summary>
public class LibraryImportNamingTests
{
    [Theory]
    [InlineData("Prelims", "UFC.9999.2020.09.01.720p.WEB-DL.mkv", "UFC 9999 - pt2 - Prelims.mkv", "Prelims", 2)]
    [InlineData(null, "UFC.9999.2020.09.01.Prelims.720p.WEB-DL.mkv", "UFC 9999 - pt2 - Prelims.mkv", "Prelims", 2)]
    [InlineData("Full Event", "UFC.9999.2020.09.01.Prelims.720p.WEB-DL.mkv", "UFC 9999.mkv", null, null)]
    public async Task LibraryImport_NamesTheFileAsARenameWould(
        string? chosenPart, string sourceName, string expectedName, string? storedPart, int? storedNumber)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var source = Path.Combine(Path.GetTempPath(), "sportarr-library-naming-" + Guid.NewGuid().ToString("N"), sourceName);
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await File.WriteAllBytesAsync(source, new byte[4096]);
        try
        {
            var result = await rig.Services.GetRequiredService<LibraryImportService>().ImportFilesAsync(new List<FileImportRequest>
            {
                new() { FilePath = source, EventId = rig.Event.Id, PartName = chosenPart, ImportMode = "copy" }
            });

            result.Failed.Should().BeEmpty(string.Join("; ", result.Errors));
            var file = await rig.Db.EventFiles.SingleAsync();
            Path.GetFileName(file.FilePath).Should().Be(expectedName);
            file.PartName.Should().Be(storedPart);
            file.PartNumber.Should().Be(storedNumber);

            var rename = ActivatorUtilities.CreateInstance<FileRenameService>(rig.Services);
            (await rename.PreviewEventRenamesAsync(rig.Event.Id)).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(source)!, recursive: true);
        }
    }
}
