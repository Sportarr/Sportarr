using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sportarr.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use INSERT OR IGNORE to handle existing databases that already have CustomFormats
            // New databases: formats will be inserted
            // Existing databases: if IDs 1-7 already exist, they will be silently skipped
            migrationBuilder.Sql(@"
                INSERT OR IGNORE INTO ""CustomFormats"" (""Id"", ""Created"", ""IncludeCustomFormatWhenRenaming"", ""IsCustomized"", ""IsSynced"", ""LastModified"", ""LastSyncedAt"", ""Name"", ""Specifications"", ""TrashCategory"", ""TrashDefaultScore"", ""TrashDescription"", ""TrashId"")
                VALUES (1, '2025-01-01 00:00:00', 0, 0, 1, NULL, NULL, 'BR-DISK', '[{""Id"":1,""Name"":""BR-DISK"",""Implementation"":""ReleaseTitleSpecification"",""Negate"":false,""Required"":false,""Fields"":{""value"":""(?i)\\b(M2TS|BDMV|MPEG-?[24])\\b""}}]', 'unwanted', -10000, 'BR-DISK refers to raw Blu-ray disc structures that are not video files', '85c61753-c413-4d8b-9e0d-f7f6f61e8c42');
            ");

            migrationBuilder.Sql(@"
                INSERT OR IGNORE INTO ""CustomFormats"" (""Id"", ""Created"", ""IncludeCustomFormatWhenRenaming"", ""IsCustomized"", ""IsSynced"", ""LastModified"", ""LastSyncedAt"", ""Name"", ""Specifications"", ""TrashCategory"", ""TrashDefaultScore"", ""TrashDescription"", ""TrashId"")
                VALUES (2, '2025-01-01 00:00:00', 0, 0, 1, NULL, NULL, 'LQ', '[{""Id"":2,""Name"":""LQ Groups"",""Implementation"":""ReleaseTitleSpecification"",""Negate"":false,""Required"":false,""Fields"":{""value"":""(?i)\\b(YIFY|YTS|RARBG|PSA|MeGusta|SPARKS|EVO|MZABI)\\b""}}]', 'unwanted', -10000, 'Releases from groups known for low quality encodes', '90a6f9a0-8c26-40f7-b4e2-25d86656e7a8');
            ");

            migrationBuilder.Sql(@"
                INSERT OR IGNORE INTO ""CustomFormats"" (""Id"", ""Created"", ""IncludeCustomFormatWhenRenaming"", ""IsCustomized"", ""IsSynced"", ""LastModified"", ""LastSyncedAt"", ""Name"", ""Specifications"", ""TrashCategory"", ""TrashDefaultScore"", ""TrashDescription"", ""TrashId"")
                VALUES (3, '2025-01-01 00:00:00', 0, 0, 1, NULL, NULL, 'Repack/Proper', '[{""Id"":3,""Name"":""Repack/Proper"",""Implementation"":""ReleaseTitleSpecification"",""Negate"":false,""Required"":false,""Fields"":{""value"":""(?i)\\b(REPACK|PROPER)\\b""}}]', 'release-version', 5, 'Repack or Proper releases fix issues with the original release', 'e6258996-0e87-4d8d-8c5e-4e5ab1a7c8e3');
            ");

            migrationBuilder.Sql(@"
                INSERT OR IGNORE INTO ""CustomFormats"" (""Id"", ""Created"", ""IncludeCustomFormatWhenRenaming"", ""IsCustomized"", ""IsSynced"", ""LastModified"", ""LastSyncedAt"", ""Name"", ""Specifications"", ""TrashCategory"", ""TrashDefaultScore"", ""TrashDescription"", ""TrashId"")
                VALUES (4, '2025-01-01 00:00:00', 0, 0, 1, NULL, NULL, 'x265 (HD)', '[{""Id"":4,""Name"":""x265/HEVC"",""Implementation"":""ReleaseTitleSpecification"",""Negate"":false,""Required"":true,""Fields"":{""value"":""(?i)\\b(x265|HEVC)\\b""}},{""Id"":5,""Name"":""Not 2160p"",""Implementation"":""ReleaseTitleSpecification"",""Negate"":true,""Required"":true,""Fields"":{""value"":""(?i)\\b2160p\\b""}}]', 'unwanted', -10000, 'x265/HEVC for non-4K content can have compatibility issues', 'dc98083d-a25b-4e2e-9dcc-9aa4c3c33e87');
            ");

            migrationBuilder.Sql(@"
                INSERT OR IGNORE INTO ""CustomFormats"" (""Id"", ""Created"", ""IncludeCustomFormatWhenRenaming"", ""IsCustomized"", ""IsSynced"", ""LastModified"", ""LastSyncedAt"", ""Name"", ""Specifications"", ""TrashCategory"", ""TrashDefaultScore"", ""TrashDescription"", ""TrashId"")
                VALUES (5, '2025-01-01 00:00:00', 0, 0, 1, NULL, NULL, 'Upscaled', '[{""Id"":6,""Name"":""Upscaled"",""Implementation"":""ReleaseTitleSpecification"",""Negate"":false,""Required"":false,""Fields"":{""value"":""(?i)\\b(upscale[sd]?|AI[-\\. ]?enhanced)\\b""}}]', 'unwanted', -10000, 'Content that has been upscaled from a lower resolution', '1b3994c5-51c6-4d4d-9c82-f5f6c46f2c3d');
            ");

            migrationBuilder.Sql(@"
                INSERT OR IGNORE INTO ""CustomFormats"" (""Id"", ""Created"", ""IncludeCustomFormatWhenRenaming"", ""IsCustomized"", ""IsSynced"", ""LastModified"", ""LastSyncedAt"", ""Name"", ""Specifications"", ""TrashCategory"", ""TrashDefaultScore"", ""TrashDescription"", ""TrashId"")
                VALUES (6, '2025-01-01 00:00:00', 0, 0, 1, NULL, NULL, 'Scene', '[{""Id"":7,""Name"":""Scene Flag"",""Implementation"":""IndexerFlagSpecification"",""Negate"":false,""Required"":false,""Fields"":{""value"":1}}]', 'indexer-flags', 0, 'Scene releases follow strict naming and encoding rules', 'a1b2c3d4-5e6f-7a8b-9c0d-e1f2a3b4c5d6');
            ");

            migrationBuilder.Sql(@"
                INSERT OR IGNORE INTO ""CustomFormats"" (""Id"", ""Created"", ""IncludeCustomFormatWhenRenaming"", ""IsCustomized"", ""IsSynced"", ""LastModified"", ""LastSyncedAt"", ""Name"", ""Specifications"", ""TrashCategory"", ""TrashDefaultScore"", ""TrashDescription"", ""TrashId"")
                VALUES (7, '2025-01-01 00:00:00', 0, 0, 1, NULL, NULL, 'WEB-DL', '[{""Id"":8,""Name"":""WEB-DL"",""Implementation"":""ReleaseTitleSpecification"",""Negate"":false,""Required"":false,""Fields"":{""value"":""(?i)\\bWEB[-\\. ]?DL\\b""}}]', 'source', 10, 'WEB-DL is typically higher quality than WEBRip', '2b3c4d5e-6f7a-8b9c-0d1e-2f3a4b5c6d7e');
            ");

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CutoffQuality", "IsDefault", "Items", "MinFormatScore", "Name" },
                values: new object[] { 15, true, "[{\"Name\":\"WEB 1080p\",\"Quality\":0,\"Allowed\":true,\"Items\":[{\"Name\":\"WEBDL-1080p\",\"Quality\":15,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEBRip-1080p\",\"Quality\":14,\"Allowed\":true,\"Items\":null,\"Id\":null}],\"Id\":null},{\"Name\":\"Bluray-1080p\",\"Quality\":11,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"HDTV-1080p\",\"Quality\":6,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEB 720p\",\"Quality\":0,\"Allowed\":true,\"Items\":[{\"Name\":\"WEBDL-720p\",\"Quality\":3,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEBRip-720p\",\"Quality\":10,\"Allowed\":true,\"Items\":null,\"Id\":null}],\"Id\":null},{\"Name\":\"Bluray-720p\",\"Quality\":7,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"HDTV-720p\",\"Quality\":5,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEB 480p\",\"Quality\":0,\"Allowed\":true,\"Items\":[{\"Name\":\"WEBDL-480p\",\"Quality\":2,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEBRip-480p\",\"Quality\":8,\"Allowed\":true,\"Items\":null,\"Id\":null}],\"Id\":null},{\"Name\":\"Bluray-576p\",\"Quality\":16,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"Bluray-480p\",\"Quality\":9,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"DVD\",\"Quality\":4,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"SDTV\",\"Quality\":1,\"Allowed\":true,\"Items\":null,\"Id\":null}]", 0, "WEB-1080p (Alternative)" });

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CutoffQuality", "IsDefault", "Items", "MinFormatScore", "Name" },
                values: new object[] { 19, false, "[{\"Name\":\"WEB 2160p\",\"Quality\":0,\"Allowed\":true,\"Items\":[{\"Name\":\"WEBDL-2160p\",\"Quality\":19,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEBRip-2160p\",\"Quality\":18,\"Allowed\":true,\"Items\":null,\"Id\":null}],\"Id\":null},{\"Name\":\"Bluray-2160p\",\"Quality\":13,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"HDTV-2160p\",\"Quality\":17,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEB 1080p\",\"Quality\":0,\"Allowed\":true,\"Items\":[{\"Name\":\"WEBDL-1080p\",\"Quality\":15,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEBRip-1080p\",\"Quality\":14,\"Allowed\":true,\"Items\":null,\"Id\":null}],\"Id\":null},{\"Name\":\"Bluray-1080p\",\"Quality\":11,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"HDTV-1080p\",\"Quality\":6,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEB 720p\",\"Quality\":0,\"Allowed\":true,\"Items\":[{\"Name\":\"WEBDL-720p\",\"Quality\":3,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEBRip-720p\",\"Quality\":10,\"Allowed\":true,\"Items\":null,\"Id\":null}],\"Id\":null},{\"Name\":\"Bluray-720p\",\"Quality\":7,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"HDTV-720p\",\"Quality\":5,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEB 480p\",\"Quality\":0,\"Allowed\":true,\"Items\":[{\"Name\":\"WEBDL-480p\",\"Quality\":2,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEBRip-480p\",\"Quality\":8,\"Allowed\":true,\"Items\":null,\"Id\":null}],\"Id\":null},{\"Name\":\"Bluray-576p\",\"Quality\":16,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"Bluray-480p\",\"Quality\":9,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"DVD\",\"Quality\":4,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"SDTV\",\"Quality\":1,\"Allowed\":true,\"Items\":null,\"Id\":null}]", 0, "WEB-2160p (Alternative)" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "CustomFormats",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "CustomFormats",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "CustomFormats",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "CustomFormats",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "CustomFormats",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "CustomFormats",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "CustomFormats",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CutoffQuality", "IsDefault", "Items", "MinFormatScore", "Name" },
                values: new object[] { null, false, "[{\"Name\":\"1080p\",\"Quality\":1080,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"720p\",\"Quality\":720,\"Allowed\":false,\"Items\":null,\"Id\":null},{\"Name\":\"480p\",\"Quality\":480,\"Allowed\":false,\"Items\":null,\"Id\":null}]", null, "HD 1080p" });

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CutoffQuality", "IsDefault", "Items", "MinFormatScore", "Name" },
                values: new object[] { null, true, "[{\"Name\":\"1080p\",\"Quality\":1080,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"720p\",\"Quality\":720,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"480p\",\"Quality\":480,\"Allowed\":true,\"Items\":null,\"Id\":null}]", null, "Any" });
        }
    }
}
