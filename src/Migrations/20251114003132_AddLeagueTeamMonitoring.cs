using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLeagueTeamMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostSettings = table.Column<string>(type: "TEXT", nullable: false),
                    SecuritySettings = table.Column<string>(type: "TEXT", nullable: false),
                    ProxySettings = table.Column<string>(type: "TEXT", nullable: false),
                    LoggingSettings = table.Column<string>(type: "TEXT", nullable: false),
                    AnalyticsSettings = table.Column<string>(type: "TEXT", nullable: false),
                    BackupSettings = table.Column<string>(type: "TEXT", nullable: false),
                    UpdateSettings = table.Column<string>(type: "TEXT", nullable: false),
                    UISettings = table.Column<string>(type: "TEXT", nullable: false),
                    MediaManagementSettings = table.Column<string>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthSessions",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RememberMe = table.Column<bool>(type: "INTEGER", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthSessions", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "CustomFormats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IncludeCustomFormatWhenRenaming = table.Column<bool>(type: "INTEGER", nullable: false),
                    Specifications = table.Column<string>(type: "TEXT", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFormats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DelayProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    PreferredProtocol = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    UsenetDelay = table.Column<int>(type: "INTEGER", nullable: false),
                    TorrentDelay = table.Column<int>(type: "INTEGER", nullable: false),
                    BypassIfHighestQuality = table.Column<bool>(type: "INTEGER", nullable: false),
                    BypassIfAboveCustomFormatScore = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinimumCustomFormatScore = table.Column<int>(type: "INTEGER", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelayProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DownloadClients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: true),
                    Password = table.Column<string>(type: "TEXT", nullable: true),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PostImportCategory = table.Column<string>(type: "TEXT", nullable: true),
                    UseSsl = table.Column<bool>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadClients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ListType = table.Column<int>(type: "INTEGER", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    QualityProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    RootFolderPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    MonitorEvents = table.Column<bool>(type: "INTEGER", nullable: false),
                    SearchOnAdd = table.Column<bool>(type: "INTEGER", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    MinimumDaysBeforeEvent = table.Column<int>(type: "INTEGER", nullable: false),
                    LeagueFilter = table.Column<string>(type: "TEXT", nullable: true),
                    LastSync = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSyncMessage = table.Column<string>(type: "TEXT", nullable: true),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Indexers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    ApiPath = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableRss = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableAutomaticSearch = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableInteractiveSearch = table.Column<bool>(type: "INTEGER", nullable: false),
                    Categories = table.Column<string>(type: "TEXT", nullable: false),
                    AnimeCategories = table.Column<string>(type: "TEXT", nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumSeeders = table.Column<int>(type: "INTEGER", nullable: false),
                    SeedRatio = table.Column<double>(type: "REAL", nullable: true),
                    SeedTime = table.Column<int>(type: "INTEGER", nullable: true),
                    SeasonPackSeedTime = table.Column<int>(type: "INTEGER", nullable: true),
                    AdditionalParameters = table.Column<string>(type: "TEXT", nullable: true),
                    MultiLanguages = table.Column<string>(type: "TEXT", nullable: true),
                    RejectBlocklistedTorrentHashes = table.Column<bool>(type: "INTEGER", nullable: false),
                    EarlyReleaseLimit = table.Column<int>(type: "INTEGER", nullable: true),
                    DownloadClientId = table.Column<int>(type: "INTEGER", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Indexers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Leagues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Sport = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Country = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Monitored = table.Column<bool>(type: "INTEGER", nullable: false),
                    QualityProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    LogoUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    BannerUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PosterUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Website = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    FormedYear = table.Column<string>(type: "TEXT", nullable: true),
                    Added = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdate = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leagues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaManagementSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RootFolders = table.Column<string>(type: "TEXT", nullable: false),
                    RenameEvents = table.Column<bool>(type: "INTEGER", nullable: false),
                    RenameFiles = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReplaceIllegalCharacters = table.Column<bool>(type: "INTEGER", nullable: false),
                    StandardEventFormat = table.Column<string>(type: "TEXT", nullable: false),
                    StandardFileFormat = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreateEventFolders = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreateEventFolder = table.Column<bool>(type: "INTEGER", nullable: false),
                    EventFolderFormat = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DeleteEmptyFolders = table.Column<bool>(type: "INTEGER", nullable: false),
                    CopyFiles = table.Column<bool>(type: "INTEGER", nullable: false),
                    SkipFreeSpaceCheck = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinimumFreeSpace = table.Column<long>(type: "INTEGER", nullable: false),
                    UseHardlinks = table.Column<bool>(type: "INTEGER", nullable: false),
                    ImportExtraFiles = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExtraFileExtensions = table.Column<string>(type: "TEXT", nullable: false),
                    SetPermissions = table.Column<bool>(type: "INTEGER", nullable: false),
                    FileChmod = table.Column<string>(type: "TEXT", nullable: false),
                    ChmodFolder = table.Column<string>(type: "TEXT", nullable: false),
                    ChownUser = table.Column<string>(type: "TEXT", nullable: false),
                    ChownGroup = table.Column<string>(type: "TEXT", nullable: false),
                    RemoveCompletedDownloads = table.Column<bool>(type: "INTEGER", nullable: false),
                    RemoveFailedDownloads = table.Column<bool>(type: "INTEGER", nullable: false),
                    ChangeFileDate = table.Column<string>(type: "TEXT", nullable: false),
                    RecycleBin = table.Column<string>(type: "TEXT", nullable: false),
                    RecycleBinCleanup = table.Column<int>(type: "INTEGER", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaManagementSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetadataProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    EventNfo = table.Column<bool>(type: "INTEGER", nullable: false),
                    EventCardNfo = table.Column<bool>(type: "INTEGER", nullable: false),
                    EventImages = table.Column<bool>(type: "INTEGER", nullable: false),
                    PlayerImages = table.Column<bool>(type: "INTEGER", nullable: false),
                    LeagueLogos = table.Column<bool>(type: "INTEGER", nullable: false),
                    EventNfoFilename = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EventPosterFilename = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EventFanartFilename = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    UseEventFolder = table.Column<bool>(type: "INTEGER", nullable: false),
                    ImageQuality = table.Column<int>(type: "INTEGER", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Implementation = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConfigJson = table.Column<string>(type: "TEXT", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QualityDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Quality = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MinSize = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    MaxSize = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: true),
                    PreferredSize = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QualityProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpgradesAllowed = table.Column<bool>(type: "INTEGER", nullable: false),
                    CutoffQuality = table.Column<int>(type: "INTEGER", nullable: true),
                    Items = table.Column<string>(type: "TEXT", nullable: false),
                    FormatItems = table.Column<string>(type: "TEXT", nullable: false),
                    MinFormatScore = table.Column<int>(type: "INTEGER", nullable: true),
                    CutoffFormatScore = table.Column<int>(type: "INTEGER", nullable: true),
                    FormatScoreIncrement = table.Column<int>(type: "INTEGER", nullable: false),
                    MinSize = table.Column<double>(type: "REAL", nullable: true),
                    MaxSize = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Required = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Ignored = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Preferred = table.Column<string>(type: "TEXT", nullable: false),
                    IncludePreferredWhenRenaming = table.Column<bool>(type: "INTEGER", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    IndexerId = table.Column<string>(type: "TEXT", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RemotePathMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Host = table.Column<string>(type: "TEXT", nullable: false),
                    RemotePath = table.Column<string>(type: "TEXT", nullable: false),
                    LocalPath = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemotePathMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RootFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Accessible = table.Column<bool>(type: "INTEGER", nullable: false),
                    FreeSpace = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalSpace = table.Column<long>(type: "INTEGER", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastChecked = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RootFolders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Details = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    RelatedEntityId = table.Column<int>(type: "INTEGER", nullable: true),
                    RelatedEntityType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    User = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CommandName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Queued = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Started = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Ended = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Progress = table.Column<int>(type: "INTEGER", nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    CancellationId = table.Column<string>(type: "TEXT", nullable: true),
                    IsManual = table.Column<bool>(type: "INTEGER", nullable: false),
                    Exception = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Identifier = table.Column<Guid>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Password = table.Column<string>(type: "TEXT", nullable: false),
                    Salt = table.Column<string>(type: "TEXT", nullable: false),
                    Iterations = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProfileFormatItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FormatId = table.Column<int>(type: "INTEGER", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileFormatItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileFormatItems_CustomFormats_FormatId",
                        column: x => x.FormatId,
                        principalTable: "CustomFormats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ShortName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    AlternateName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LeagueId = table.Column<int>(type: "INTEGER", nullable: true),
                    Sport = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Country = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Stadium = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    StadiumLocation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    StadiumCapacity = table.Column<int>(type: "INTEGER", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    BadgeUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    JerseyUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    BannerUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Website = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    FormedYear = table.Column<int>(type: "INTEGER", nullable: true),
                    PrimaryColor = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    SecondaryColor = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Added = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdate = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teams_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Sport = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LeagueId = table.Column<int>(type: "INTEGER", nullable: true),
                    HomeTeamId = table.Column<int>(type: "INTEGER", nullable: true),
                    AwayTeamId = table.Column<int>(type: "INTEGER", nullable: true),
                    Season = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Round = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    EventDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Venue = table.Column<string>(type: "TEXT", nullable: true),
                    Location = table.Column<string>(type: "TEXT", nullable: true),
                    Broadcast = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Monitored = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasFile = table.Column<bool>(type: "INTEGER", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: true),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: true),
                    Quality = table.Column<string>(type: "TEXT", nullable: true),
                    QualityProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    Images = table.Column<string>(type: "TEXT", nullable: false),
                    Added = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    HomeScore = table.Column<string>(type: "TEXT", nullable: true),
                    AwayScore = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Events_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Events_Teams_AwayTeamId",
                        column: x => x.AwayTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Events_Teams_HomeTeamId",
                        column: x => x.HomeTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LeagueTeams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LeagueId = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: false),
                    Monitored = table.Column<bool>(type: "INTEGER", nullable: false),
                    Added = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeagueTeams_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeagueTeams_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Nickname = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Sport = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: true),
                    Position = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Nationality = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    BirthDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Birthplace = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    Weight = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: true),
                    Number = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    PhotoUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ActionPhotoUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    BannerUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Dominance = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Website = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SocialMedia = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    WeightClass = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Record = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Stance = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Reach = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: true),
                    Added = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdate = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Players_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Blocklist",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    TorrentInfoHash = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Indexer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Reason = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    BlockedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blocklist", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Blocklist_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DownloadQueue",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DownloadId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DownloadClientId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Quality = table.Column<string>(type: "TEXT", nullable: true),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Downloaded = table.Column<long>(type: "INTEGER", nullable: false),
                    Progress = table.Column<double>(type: "REAL", nullable: false),
                    TimeRemaining = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    Added = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: true),
                    LastUpdate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TorrentInfoHash = table.Column<string>(type: "TEXT", nullable: true),
                    Indexer = table.Column<string>(type: "TEXT", nullable: true),
                    Protocol = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadQueue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadQueue_DownloadClients_DownloadClientId",
                        column: x => x.DownloadClientId,
                        principalTable: "DownloadClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DownloadQueue_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    DownloadQueueItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    SourcePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    DestinationPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Quality = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Decision = table.Column<int>(type: "INTEGER", nullable: false),
                    Warnings = table.Column<string>(type: "TEXT", nullable: false),
                    Errors = table.Column<string>(type: "TEXT", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportHistories_DownloadQueue_DownloadQueueItemId",
                        column: x => x.DownloadQueueItemId,
                        principalTable: "DownloadQueue",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ImportHistories_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "MetadataProviders",
                columns: new[] { "Id", "Created", "Enabled", "EventCardNfo", "EventFanartFilename", "EventImages", "EventNfo", "EventNfoFilename", "EventPosterFilename", "ImageQuality", "LastModified", "LeagueLogos", "Name", "PlayerImages", "Tags", "Type", "UseEventFolder" },
                values: new object[] { 1, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, false, "fanart.jpg", true, true, "{Event Title}.nfo", "poster.jpg", 95, null, false, "Kodi/XBMC", false, "[]", 0, true });

            migrationBuilder.InsertData(
                table: "QualityDefinitions",
                columns: new[] { "Id", "Created", "LastModified", "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[,]
                {
                    { 1, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 199.9m, 1m, 194.9m, 0, "Unknown" },
                    { 2, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 100m, 2m, 95m, 1, "SDTV" },
                    { 3, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 100m, 2m, 95m, 8, "WEBRip-480p" },
                    { 4, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 100m, 2m, 95m, 2, "WEBDL-480p" },
                    { 5, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 100m, 2m, 95m, 4, "DVD" },
                    { 6, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 100m, 2m, 95m, 9, "Bluray-480p" },
                    { 7, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 100m, 2m, 95m, 16, "Bluray-576p" },
                    { 8, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 10m, 995m, 5, "HDTV-720p" },
                    { 9, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 15m, 995m, 6, "HDTV-1080p" },
                    { 10, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 4m, 995m, 20, "Raw-HD" },
                    { 11, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 10m, 995m, 10, "WEBRip-720p" },
                    { 12, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 10m, 995m, 3, "WEBDL-720p" },
                    { 13, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 17.1m, 995m, 7, "Bluray-720p" },
                    { 14, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 15m, 995m, 14, "WEBRip-1080p" },
                    { 15, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 15m, 995m, 15, "WEBDL-1080p" },
                    { 16, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 50.4m, 995m, 11, "Bluray-1080p" },
                    { 17, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 69.1m, 995m, 12, "Bluray-1080p Remux" },
                    { 18, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 25m, 995m, 17, "HDTV-2160p" },
                    { 19, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 25m, 995m, 18, "WEBRip-2160p" },
                    { 20, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 25m, 995m, 19, "WEBDL-2160p" },
                    { 21, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 94.6m, 995m, 13, "Bluray-2160p" },
                    { 22, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 187.4m, 995m, 21, "Bluray-2160p Remux" }
                });

            migrationBuilder.InsertData(
                table: "QualityProfiles",
                columns: new[] { "Id", "CutoffFormatScore", "CutoffQuality", "FormatItems", "FormatScoreIncrement", "IsDefault", "Items", "MaxSize", "MinFormatScore", "MinSize", "Name", "UpgradesAllowed" },
                values: new object[,]
                {
                    { 1, null, null, "[]", 1, false, "[{\"Name\":\"1080p\",\"Quality\":1080,\"Allowed\":true},{\"Name\":\"720p\",\"Quality\":720,\"Allowed\":false},{\"Name\":\"480p\",\"Quality\":480,\"Allowed\":false}]", null, null, null, "HD 1080p", true },
                    { 2, null, null, "[]", 1, true, "[{\"Name\":\"1080p\",\"Quality\":1080,\"Allowed\":true},{\"Name\":\"720p\",\"Quality\":720,\"Allowed\":true},{\"Name\":\"480p\",\"Quality\":480,\"Allowed\":true}]", null, null, null, "Any", true }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_ExpiresAt",
                table: "AuthSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Blocklist_BlockedAt",
                table: "Blocklist",
                column: "BlockedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Blocklist_EventId",
                table: "Blocklist",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Blocklist_TorrentInfoHash",
                table: "Blocklist",
                column: "TorrentInfoHash");

            migrationBuilder.CreateIndex(
                name: "IX_CustomFormats_Name",
                table: "CustomFormats",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DownloadQueue_DownloadClientId",
                table: "DownloadQueue",
                column: "DownloadClientId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadQueue_DownloadId",
                table: "DownloadQueue",
                column: "DownloadId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadQueue_EventId",
                table: "DownloadQueue",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadQueue_Status",
                table: "DownloadQueue",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Events_AwayTeamId",
                table: "Events",
                column: "AwayTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_EventDate",
                table: "Events",
                column: "EventDate");

            migrationBuilder.CreateIndex(
                name: "IX_Events_ExternalId",
                table: "Events",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_HomeTeamId",
                table: "Events",
                column: "HomeTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_LeagueId",
                table: "Events",
                column: "LeagueId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_Sport",
                table: "Events",
                column: "Sport");

            migrationBuilder.CreateIndex(
                name: "IX_Events_Status",
                table: "Events",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ImportHistories_DownloadQueueItemId",
                table: "ImportHistories",
                column: "DownloadQueueItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportHistories_EventId",
                table: "ImportHistories",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportHistories_ImportedAt",
                table: "ImportHistories",
                column: "ImportedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Leagues_ExternalId",
                table: "Leagues",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_Leagues_Name_Sport",
                table: "Leagues",
                columns: new[] { "Name", "Sport" });

            migrationBuilder.CreateIndex(
                name: "IX_Leagues_Sport",
                table: "Leagues",
                column: "Sport");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueTeams_LeagueId_TeamId",
                table: "LeagueTeams",
                columns: new[] { "LeagueId", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeagueTeams_Monitored",
                table: "LeagueTeams",
                column: "Monitored");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueTeams_TeamId",
                table: "LeagueTeams",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_ExternalId",
                table: "Players",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_Sport",
                table: "Players",
                column: "Sport");

            migrationBuilder.CreateIndex(
                name: "IX_Players_TeamId",
                table: "Players",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileFormatItems_FormatId",
                table: "ProfileFormatItems",
                column: "FormatId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityDefinitions_Quality",
                table: "QualityDefinitions",
                column: "Quality",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RootFolders_Path",
                table: "RootFolders",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvents_Category",
                table: "SystemEvents",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvents_Timestamp",
                table: "SystemEvents",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvents_Type",
                table: "SystemEvents",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Label",
                table: "Tags",
                column: "Label",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_CommandName",
                table: "Tasks",
                column: "CommandName");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Queued",
                table: "Tasks",
                column: "Queued");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Status",
                table: "Tasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_ExternalId",
                table: "Teams",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_LeagueId",
                table: "Teams",
                column: "LeagueId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_Sport",
                table: "Teams",
                column: "Sport");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "AuthSessions");

            migrationBuilder.DropTable(
                name: "Blocklist");

            migrationBuilder.DropTable(
                name: "DelayProfiles");

            migrationBuilder.DropTable(
                name: "ImportHistories");

            migrationBuilder.DropTable(
                name: "ImportLists");

            migrationBuilder.DropTable(
                name: "Indexers");

            migrationBuilder.DropTable(
                name: "LeagueTeams");

            migrationBuilder.DropTable(
                name: "MediaManagementSettings");

            migrationBuilder.DropTable(
                name: "MetadataProviders");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "ProfileFormatItems");

            migrationBuilder.DropTable(
                name: "QualityDefinitions");

            migrationBuilder.DropTable(
                name: "QualityProfiles");

            migrationBuilder.DropTable(
                name: "ReleaseProfiles");

            migrationBuilder.DropTable(
                name: "RemotePathMappings");

            migrationBuilder.DropTable(
                name: "RootFolders");

            migrationBuilder.DropTable(
                name: "SystemEvents");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "Tasks");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "DownloadQueue");

            migrationBuilder.DropTable(
                name: "CustomFormats");

            migrationBuilder.DropTable(
                name: "DownloadClients");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "Leagues");
        }
    }
}
