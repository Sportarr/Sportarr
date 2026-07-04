using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sportarr.Api.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HostSettings = table.Column<string>(type: "text", nullable: false),
                    SecuritySettings = table.Column<string>(type: "text", nullable: false),
                    ProxySettings = table.Column<string>(type: "text", nullable: false),
                    LoggingSettings = table.Column<string>(type: "text", nullable: false),
                    AnalyticsSettings = table.Column<string>(type: "text", nullable: false),
                    BackupSettings = table.Column<string>(type: "text", nullable: false),
                    UpdateSettings = table.Column<string>(type: "text", nullable: false),
                    UISettings = table.Column<string>(type: "text", nullable: false),
                    MediaManagementSettings = table.Column<string>(type: "text", nullable: false),
                    TrashSyncSettings = table.Column<string>(type: "text", nullable: false),
                    DevelopmentSettings = table.Column<string>(type: "text", nullable: false),
                    EnableCompletedDownloadHandling = table.Column<bool>(type: "boolean", nullable: false),
                    CheckForFinishedDownloadInterval = table.Column<int>(type: "integer", nullable: false),
                    RedownloadFailedDownloads = table.Column<bool>(type: "boolean", nullable: false),
                    RedownloadFailedFromInteractiveSearch = table.Column<bool>(type: "boolean", nullable: false),
                    MaxDownloadQueueSize = table.Column<int>(type: "integer", nullable: false),
                    SearchSleepDuration = table.Column<int>(type: "integer", nullable: false),
                    HubChangesCursor = table.Column<long>(type: "bigint", nullable: false),
                    IndexerRetention = table.Column<int>(type: "integer", nullable: false),
                    RssSyncInterval = table.Column<int>(type: "integer", nullable: false),
                    PreferIndexerFlags = table.Column<bool>(type: "boolean", nullable: false),
                    SearchCacheDuration = table.Column<int>(type: "integer", nullable: false),
                    IndexerMinimumAgeMinutes = table.Column<int>(type: "integer", nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthSessions",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "text", nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RememberMe = table.Column<bool>(type: "boolean", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthSessions", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "CustomFormats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IncludeCustomFormatWhenRenaming = table.Column<bool>(type: "boolean", nullable: false),
                    Specifications = table.Column<string>(type: "text", nullable: false),
                    TrashId = table.Column<string>(type: "text", nullable: true),
                    TrashDefaultScore = table.Column<int>(type: "integer", nullable: true),
                    TrashCategory = table.Column<string>(type: "text", nullable: true),
                    TrashDescription = table.Column<string>(type: "text", nullable: true),
                    IsSynced = table.Column<bool>(type: "boolean", nullable: false),
                    IsCustomized = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFormats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DelayProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    PreferredProtocol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UsenetDelay = table.Column<int>(type: "integer", nullable: false),
                    TorrentDelay = table.Column<int>(type: "integer", nullable: false),
                    BypassIfHighestQuality = table.Column<bool>(type: "boolean", nullable: false),
                    BypassIfAboveCustomFormatScore = table.Column<bool>(type: "boolean", nullable: false),
                    MinimumCustomFormatScore = table.Column<int>(type: "integer", nullable: false),
                    Tags = table.Column<string>(type: "text", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelayProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DownloadClients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Host = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: true),
                    Password = table.Column<string>(type: "text", nullable: true),
                    ApiKey = table.Column<string>(type: "text", nullable: true),
                    UrlBase = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PostImportCategory = table.Column<string>(type: "text", nullable: true),
                    Directory = table.Column<string>(type: "text", nullable: true),
                    UseSsl = table.Column<bool>(type: "boolean", nullable: false),
                    DisableSslCertificateValidation = table.Column<bool>(type: "boolean", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    SequentialDownload = table.Column<bool>(type: "boolean", nullable: false),
                    FirstAndLastFirst = table.Column<bool>(type: "boolean", nullable: false),
                    InitialState = table.Column<int>(type: "integer", nullable: false),
                    RemoveCompletedDownloads = table.Column<bool>(type: "boolean", nullable: false),
                    RemoveFailedDownloads = table.Column<bool>(type: "boolean", nullable: false),
                    Tags = table.Column<string>(type: "text", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadClients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DvrQualityProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Preset = table.Column<int>(type: "integer", nullable: false),
                    VideoCodec = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AudioCodec = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VideoBitrate = table.Column<int>(type: "integer", nullable: false),
                    AudioBitrate = table.Column<int>(type: "integer", nullable: false),
                    Resolution = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FrameRate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HardwareAcceleration = table.Column<int>(type: "integer", nullable: false),
                    EncodingPreset = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConstantRateFactor = table.Column<int>(type: "integer", nullable: false),
                    Container = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CustomArguments = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    AudioChannels = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AudioSampleRate = table.Column<int>(type: "integer", nullable: false),
                    Deinterlace = table.Column<bool>(type: "boolean", nullable: false),
                    EstimatedSizePerHourMb = table.Column<int>(type: "integer", nullable: false),
                    EstimatedQualityScore = table.Column<int>(type: "integer", nullable: false),
                    EstimatedCustomFormatScore = table.Column<int>(type: "integer", nullable: false),
                    ExpectedQualityName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ExpectedFormatDescription = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DvrQualityProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EpgSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ProgramCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpgSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FollowedTeams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Sport = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BadgeUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Added = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLeagueDiscovery = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowedTeams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportListExclusions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TvdbId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Added = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportListExclusions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ListType = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ApiKey = table.Column<string>(type: "text", nullable: true),
                    QualityProfileId = table.Column<int>(type: "integer", nullable: false),
                    RootFolderPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    MonitorEvents = table.Column<bool>(type: "boolean", nullable: false),
                    SearchOnAdd = table.Column<bool>(type: "boolean", nullable: false),
                    Tags = table.Column<string>(type: "text", nullable: false),
                    MinimumDaysBeforeEvent = table.Column<int>(type: "integer", nullable: false),
                    LeagueFilter = table.Column<string>(type: "text", nullable: true),
                    LastSync = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncMessage = table.Column<string>(type: "text", nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Indexers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ApiKey = table.Column<string>(type: "text", nullable: true),
                    ApiPath = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    EnableRss = table.Column<bool>(type: "boolean", nullable: false),
                    EnableAutomaticSearch = table.Column<bool>(type: "boolean", nullable: false),
                    EnableInteractiveSearch = table.Column<bool>(type: "boolean", nullable: false),
                    Categories = table.Column<string>(type: "text", nullable: false),
                    AnimeCategories = table.Column<List<string>>(type: "text[]", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    MinimumSeeders = table.Column<int>(type: "integer", nullable: false),
                    SeedRatio = table.Column<double>(type: "double precision", nullable: true),
                    SeedTime = table.Column<int>(type: "integer", nullable: true),
                    SeasonPackSeedTime = table.Column<int>(type: "integer", nullable: true),
                    AdditionalParameters = table.Column<string>(type: "text", nullable: true),
                    MultiLanguages = table.Column<List<string>>(type: "text[]", nullable: true),
                    RejectBlocklistedTorrentHashes = table.Column<bool>(type: "boolean", nullable: false),
                    EarlyReleaseLimit = table.Column<int>(type: "integer", nullable: true),
                    Cookie = table.Column<string>(type: "text", nullable: true),
                    RssAllowZeroSize = table.Column<bool>(type: "boolean", nullable: false),
                    RssUseEzrssFormat = table.Column<bool>(type: "boolean", nullable: false),
                    RssUseEnclosureUrl = table.Column<bool>(type: "boolean", nullable: false),
                    RssUseEnclosureLength = table.Column<bool>(type: "boolean", nullable: false),
                    RssParseSizeInDescription = table.Column<bool>(type: "boolean", nullable: false),
                    RssParseSeedersInDescription = table.Column<bool>(type: "boolean", nullable: false),
                    RssSizeElementName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FailDownloads = table.Column<string>(type: "text", nullable: false),
                    DownloadClientId = table.Column<int>(type: "integer", nullable: true),
                    Tags = table.Column<string>(type: "text", nullable: false),
                    QueryLimit = table.Column<int>(type: "integer", nullable: true),
                    GrabLimit = table.Column<int>(type: "integer", nullable: true),
                    RequestDelayMs = table.Column<int>(type: "integer", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Indexers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IptvSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Username = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Password = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MaxStreams = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ChannelCount = table.Column<int>(type: "integer", nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DetectedCatchupMode = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IptvSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaManagementSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RenameEvents = table.Column<bool>(type: "boolean", nullable: false),
                    RenameFiles = table.Column<bool>(type: "boolean", nullable: false),
                    ReplaceIllegalCharacters = table.Column<bool>(type: "boolean", nullable: false),
                    EnableMultiPartEpisodes = table.Column<bool>(type: "boolean", nullable: false),
                    StandardFileFormat = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreateLeagueFolders = table.Column<bool>(type: "boolean", nullable: false),
                    CreateSeasonFolders = table.Column<bool>(type: "boolean", nullable: false),
                    CreateEventFolders = table.Column<bool>(type: "boolean", nullable: false),
                    LeagueFolderFormat = table.Column<string>(type: "text", nullable: false),
                    SeasonFolderFormat = table.Column<string>(type: "text", nullable: false),
                    EventFolderFormat = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DeleteEmptyFolders = table.Column<bool>(type: "boolean", nullable: false),
                    ReorganizeFolders = table.Column<bool>(type: "boolean", nullable: false),
                    CreateEventFolder = table.Column<bool>(type: "boolean", nullable: false),
                    CopyFiles = table.Column<bool>(type: "boolean", nullable: false),
                    SkipFreeSpaceCheck = table.Column<bool>(type: "boolean", nullable: false),
                    MinimumFreeSpace = table.Column<long>(type: "bigint", nullable: false),
                    UseHardlinks = table.Column<bool>(type: "boolean", nullable: false),
                    ImportExtraFiles = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraFileExtensions = table.Column<string>(type: "text", nullable: false),
                    UserRejectedExtensions = table.Column<string>(type: "text", nullable: true),
                    SetPermissions = table.Column<bool>(type: "boolean", nullable: false),
                    FileChmod = table.Column<string>(type: "text", nullable: false),
                    ChmodFolder = table.Column<string>(type: "text", nullable: false),
                    ChownUser = table.Column<string>(type: "text", nullable: false),
                    ChownGroup = table.Column<string>(type: "text", nullable: false),
                    ChangeFileDate = table.Column<string>(type: "text", nullable: false),
                    RecycleBin = table.Column<string>(type: "text", nullable: false),
                    RecycleBinCleanup = table.Column<int>(type: "integer", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaManagementSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetadataProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    EventNfo = table.Column<bool>(type: "boolean", nullable: false),
                    EventCardNfo = table.Column<bool>(type: "boolean", nullable: false),
                    EventImages = table.Column<bool>(type: "boolean", nullable: false),
                    PlayerImages = table.Column<bool>(type: "boolean", nullable: false),
                    LeagueLogos = table.Column<bool>(type: "boolean", nullable: false),
                    EventNfoFilename = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventPosterFilename = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventFanartFilename = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UseEventFolder = table.Column<bool>(type: "boolean", nullable: false),
                    ImageQuality = table.Column<int>(type: "integer", nullable: false),
                    Tags = table.Column<string>(type: "text", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Implementation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ConfigJson = table.Column<string>(type: "text", nullable: false),
                    Tags = table.Column<string>(type: "text", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QualityDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Quality = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MinSize = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    MaxSize = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    PreferredSize = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QualityProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    UpgradesAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    CutoffQuality = table.Column<int>(type: "integer", nullable: true),
                    Items = table.Column<string>(type: "text", nullable: false),
                    FormatItems = table.Column<string>(type: "text", nullable: false),
                    MinFormatScore = table.Column<int>(type: "integer", nullable: true),
                    CutoffFormatScore = table.Column<int>(type: "integer", nullable: true),
                    FormatScoreIncrement = table.Column<int>(type: "integer", nullable: false),
                    MinSize = table.Column<double>(type: "double precision", nullable: true),
                    MaxSize = table.Column<double>(type: "double precision", nullable: true),
                    TrashId = table.Column<string>(type: "text", nullable: true),
                    IsSynced = table.Column<bool>(type: "boolean", nullable: false),
                    TrashScoreSet = table.Column<string>(type: "text", nullable: true),
                    LastTrashScoreSync = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsCustomized = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseCache",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    NormalizedTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SearchTerms = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Guid = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DownloadUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    InfoUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Indexer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IndexerId = table.Column<int>(type: "integer", nullable: true),
                    Protocol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TorrentInfoHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Quality = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Codec = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Language = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Seeders = table.Column<int>(type: "integer", nullable: true),
                    Leechers = table.Column<int>(type: "integer", nullable: true),
                    PublishDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IndexerFlags = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CachedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FromRss = table.Column<bool>(type: "boolean", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    Month = table.Column<int>(type: "integer", nullable: true),
                    Day = table.Column<int>(type: "integer", nullable: true),
                    RoundNumber = table.Column<int>(type: "integer", nullable: true),
                    SportPrefix = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsPack = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseCache", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Required = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Ignored = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Preferred = table.Column<string>(type: "text", nullable: false),
                    IncludePreferredWhenRenaming = table.Column<bool>(type: "boolean", nullable: false),
                    Tags = table.Column<string>(type: "text", nullable: false),
                    IndexerId = table.Column<string>(type: "text", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RemotePathMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Host = table.Column<string>(type: "text", nullable: false),
                    RemotePath = table.Column<string>(type: "text", nullable: false),
                    LocalPath = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemotePathMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestoreReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BackupFileName = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TotalEventFiles = table.Column<int>(type: "integer", nullable: false),
                    FilesFound = table.Column<int>(type: "integer", nullable: false),
                    FilesMissing = table.Column<int>(type: "integer", nullable: false),
                    FilesSkippedUnreachableRoot = table.Column<int>(type: "integer", nullable: false),
                    ManifestJson = table.Column<string>(type: "text", nullable: true),
                    PathRemapsJson = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    SourceHost = table.Column<string>(type: "text", nullable: true),
                    SourceSportarrVersion = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestoreReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RootFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DefaultQualityProfileId = table.Column<int>(type: "integer", nullable: true),
                    DefaultDownloadClientCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RootFolders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RelatedEntityId = table.Column<int>(type: "integer", nullable: true),
                    RelatedEntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    User = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CommandName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Queued = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Started = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Ended = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Duration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Progress = table.Column<int>(type: "integer", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: true),
                    CancellationId = table.Column<string>(type: "text", nullable: true),
                    IsManual = table.Column<bool>(type: "boolean", nullable: false),
                    Exception = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Identifier = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Password = table.Column<string>(type: "text", nullable: false),
                    Salt = table.Column<string>(type: "text", nullable: false),
                    Iterations = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProfileFormatItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FormatId = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false)
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
                name: "EpgChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EpgSourceId = table.Column<int>(type: "integer", nullable: false),
                    ChannelId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IconUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpgChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EpgChannels_EpgSources_EpgSourceId",
                        column: x => x.EpgSourceId,
                        principalTable: "EpgSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IndexerStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IndexerId = table.Column<int>(type: "integer", nullable: false),
                    QueryFailures = table.Column<int>(type: "integer", nullable: false),
                    QueryDisabledUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastQueryFailure = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastQueryFailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    GrabFailures = table.Column<int>(type: "integer", nullable: false),
                    GrabDisabledUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastGrabFailure = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastGrabFailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    LastFailure = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DisabledUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    QueriesThisHour = table.Column<int>(type: "integer", nullable: false),
                    GrabsThisHour = table.Column<int>(type: "integer", nullable: false),
                    HourResetTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSuccess = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRssSyncAttempt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RateLimitedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConnectionErrors = table.Column<int>(type: "integer", nullable: false),
                    LastConnectionError = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexerStatuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndexerStatuses_Indexers_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "Indexers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IptvChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ChannelNumber = table.Column<int>(type: "integer", nullable: true),
                    StreamUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    LogoUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Group = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TvgId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TvgName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsSportsChannel = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LastChecked = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsFavorite = table.Column<bool>(type: "boolean", nullable: false),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false),
                    Country = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Language = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DetectedQuality = table.Column<string>(type: "text", nullable: true),
                    QualityScore = table.Column<int>(type: "integer", nullable: false),
                    DetectedNetwork = table.Column<string>(type: "text", nullable: true),
                    IptvOrgId = table.Column<string>(type: "text", nullable: true),
                    IptvOrgConfidence = table.Column<int>(type: "integer", nullable: true),
                    HasArchive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchiveDays = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IptvChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IptvChannels_IptvSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "IptvSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Leagues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Sport = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AlternateName = table.Column<string>(type: "text", nullable: true),
                    MetadataLastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Monitored = table.Column<bool>(type: "boolean", nullable: false),
                    MonitorType = table.Column<int>(type: "integer", nullable: false),
                    QualityProfileId = table.Column<int>(type: "integer", nullable: true),
                    RootFolderId = table.Column<int>(type: "integer", nullable: true),
                    SearchForMissingEvents = table.Column<bool>(type: "boolean", nullable: false),
                    SearchForCutoffUnmetEvents = table.Column<bool>(type: "boolean", nullable: false),
                    MonitoredParts = table.Column<string>(type: "text", nullable: true),
                    MonitoredSessionTypes = table.Column<string>(type: "text", nullable: true),
                    MonitoredEventTypes = table.Column<string>(type: "text", nullable: true),
                    MonitorFinals = table.Column<bool>(type: "boolean", nullable: false),
                    MonitorPlayoffs = table.Column<bool>(type: "boolean", nullable: false),
                    MonitorPreseason = table.Column<bool>(type: "boolean", nullable: false),
                    SearchQueryTemplate = table.Column<string>(type: "text", nullable: true),
                    DvrPrePadMinutes = table.Column<int>(type: "integer", nullable: true),
                    DvrPostRollMinutes = table.Column<int>(type: "integer", nullable: true),
                    LogoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BannerUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PosterUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Website = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FormedYear = table.Column<string>(type: "text", nullable: true),
                    Added = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leagues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Leagues_RootFolders_RootFolderId",
                        column: x => x.RootFolderId,
                        principalTable: "RootFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChannelLeagueMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChannelId = table.Column<int>(type: "integer", nullable: false),
                    LeagueId = table.Column<int>(type: "integer", nullable: false),
                    IsPreferred = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Confidence = table.Column<int>(type: "integer", nullable: false),
                    IsManual = table.Column<bool>(type: "boolean", nullable: false),
                    MappingSignals = table.Column<string>(type: "text", nullable: true),
                    LastAutoMapped = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelLeagueMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelLeagueMappings_IptvChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "IptvChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChannelLeagueMappings_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ShortName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AlternateName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LeagueId = table.Column<int>(type: "integer", nullable: true),
                    Sport = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Stadium = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StadiumLocation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StadiumCapacity = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    BadgeUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    JerseyUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BannerUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Website = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FormedYear = table.Column<int>(type: "integer", nullable: true),
                    PrimaryColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SecondaryColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Added = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Sport = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LeagueId = table.Column<int>(type: "integer", nullable: true),
                    HomeTeamExternalId = table.Column<string>(type: "text", nullable: true),
                    AwayTeamExternalId = table.Column<string>(type: "text", nullable: true),
                    HomeTeamName = table.Column<string>(type: "text", nullable: true),
                    AwayTeamName = table.Column<string>(type: "text", nullable: true),
                    HomeTeamId = table.Column<int>(type: "integer", nullable: true),
                    AwayTeamId = table.Column<int>(type: "integer", nullable: true),
                    Season = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SeasonNumber = table.Column<int>(type: "integer", nullable: true),
                    EpisodeNumber = table.Column<int>(type: "integer", nullable: true),
                    Round = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EventDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BroadcastDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Venue = table.Column<string>(type: "text", nullable: true),
                    Location = table.Column<string>(type: "text", nullable: true),
                    Broadcast = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Monitored = table.Column<bool>(type: "boolean", nullable: false),
                    MonitoredParts = table.Column<string>(type: "text", nullable: true),
                    HasFile = table.Column<bool>(type: "boolean", nullable: false),
                    FilePath = table.Column<string>(type: "text", nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: true),
                    Quality = table.Column<string>(type: "text", nullable: true),
                    QualityProfileId = table.Column<int>(type: "integer", nullable: true),
                    Images = table.Column<string>(type: "text", nullable: false),
                    Added = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HomeScore = table.Column<string>(type: "text", nullable: true),
                    AwayScore = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LeagueId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    Monitored = table.Column<bool>(type: "boolean", nullable: false),
                    Added = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Nickname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Sport = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: true),
                    Position = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Nationality = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BirthDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Birthplace = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    Weight = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    Number = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PhotoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActionPhotoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BannerUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Dominance = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Website = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SocialMedia = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    WeightClass = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Record = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Stance = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Reach = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    Added = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TorrentInfoHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Indexer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Protocol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    BlockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Part = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FilePath = table.Column<string>(type: "text", nullable: true)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DownloadId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DownloadClientId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Quality = table.Column<string>(type: "text", nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Downloaded = table.Column<long>(type: "bigint", nullable: false),
                    Progress = table.Column<double>(type: "double precision", nullable: false),
                    TimeRemaining = table.Column<TimeSpan>(type: "interval", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    StatusMessages = table.Column<List<string>>(type: "text[]", nullable: false),
                    Added = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: true),
                    ImportRetryCount = table.Column<int>(type: "integer", nullable: true),
                    LastUpdate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TorrentInfoHash = table.Column<string>(type: "text", nullable: true),
                    Indexer = table.Column<string>(type: "text", nullable: true),
                    IndexerId = table.Column<int>(type: "integer", nullable: true),
                    Protocol = table.Column<string>(type: "text", nullable: true),
                    MissingFromClientCount = table.Column<int>(type: "integer", nullable: true),
                    QualityScore = table.Column<int>(type: "integer", nullable: false),
                    CustomFormatScore = table.Column<int>(type: "integer", nullable: false),
                    Codec = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: true),
                    Part = table.Column<string>(type: "text", nullable: true),
                    PackGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsPack = table.Column<bool>(type: "boolean", nullable: false),
                    IsManualSearch = table.Column<bool>(type: "boolean", nullable: false)
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
                name: "DvrRecordings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<int>(type: "integer", nullable: true),
                    ChannelId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ScheduledStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScheduledEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PrePadding = table.Column<int>(type: "integer", nullable: false),
                    PostPadding = table.Column<int>(type: "integer", nullable: false),
                    ActualStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActualEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OutputPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: true),
                    CurrentBitrate = table.Column<long>(type: "bigint", nullable: true),
                    AverageBitrate = table.Column<long>(type: "bigint", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PartName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Quality = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    QualityScore = table.Column<int>(type: "integer", nullable: true),
                    CustomFormatScore = table.Column<int>(type: "integer", nullable: true),
                    VideoWidth = table.Column<int>(type: "integer", nullable: true),
                    VideoHeight = table.Column<int>(type: "integer", nullable: true),
                    VideoCodec = table.Column<string>(type: "text", nullable: true),
                    AudioCodec = table.Column<string>(type: "text", nullable: true),
                    AudioChannels = table.Column<int>(type: "integer", nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FallbackChannelIds = table.Column<string>(type: "text", nullable: true),
                    AutoRetryCount = table.Column<int>(type: "integer", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DvrRecordings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DvrRecordings_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DvrRecordings_IptvChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "IptvChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EpgPrograms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EpgSourceId = table.Column<int>(type: "integer", nullable: false),
                    ChannelId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IconUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsSportsProgram = table.Column<bool>(type: "boolean", nullable: false),
                    MatchedEventId = table.Column<int>(type: "integer", nullable: true),
                    MatchConfidence = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpgPrograms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EpgPrograms_EpgSources_EpgSourceId",
                        column: x => x.EpgSourceId,
                        principalTable: "EpgSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EpgPrograms_Events_MatchedEventId",
                        column: x => x.MatchedEventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EventFileHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    SourceTitle = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Quality = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Part = table.Column<string>(type: "text", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventFileHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventFileHistory_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EventFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    FilePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Quality = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    QualityScore = table.Column<int>(type: "integer", nullable: false),
                    CustomFormatScore = table.Column<int>(type: "integer", nullable: false),
                    Codec = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: true),
                    PartName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PartNumber = table.Column<int>(type: "integer", nullable: true),
                    Added = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastVerified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Exists = table.Column<bool>(type: "boolean", nullable: false),
                    MissingSince = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OriginalTitle = table.Column<string>(type: "text", nullable: true),
                    ReleaseGroup = table.Column<string>(type: "text", nullable: true),
                    Languages = table.Column<string>(type: "text", nullable: false),
                    IndexerFlags = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventFiles_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GrabHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Indexer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IndexerId = table.Column<int>(type: "integer", nullable: true),
                    DownloadUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Guid = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Protocol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TorrentInfoHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Quality = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Codec = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    QualityScore = table.Column<int>(type: "integer", nullable: false),
                    CustomFormatScore = table.Column<int>(type: "integer", nullable: false),
                    PartName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    GrabbedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WasImported = table.Column<bool>(type: "boolean", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FileExists = table.Column<bool>(type: "boolean", nullable: false),
                    LastRegrabAttempt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RegrabCount = table.Column<int>(type: "integer", nullable: false),
                    DownloadClientId = table.Column<int>(type: "integer", nullable: true),
                    DownloadId = table.Column<string>(type: "text", nullable: true),
                    Superseded = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrabHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrabHistory_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PendingImports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DownloadClientId = table.Column<int>(type: "integer", nullable: true),
                    DownloadId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    FilePath = table.Column<string>(type: "text", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Quality = table.Column<string>(type: "text", nullable: true),
                    QualityScore = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    SuggestedEventId = table.Column<int>(type: "integer", nullable: true),
                    SuggestedPart = table.Column<string>(type: "text", nullable: true),
                    SuggestionConfidence = table.Column<int>(type: "integer", nullable: false),
                    Detected = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Protocol = table.Column<string>(type: "text", nullable: true),
                    TorrentInfoHash = table.Column<string>(type: "text", nullable: true),
                    IsPack = table.Column<bool>(type: "boolean", nullable: false),
                    FileCount = table.Column<int>(type: "integer", nullable: false),
                    MatchedEventsCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingImports_DownloadClients_DownloadClientId",
                        column: x => x.DownloadClientId,
                        principalTable: "DownloadClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PendingImports_Events_SuggestedEventId",
                        column: x => x.SuggestedEventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PendingReleases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Guid = table.Column<string>(type: "text", nullable: false),
                    DownloadUrl = table.Column<string>(type: "text", nullable: false),
                    InfoUrl = table.Column<string>(type: "text", nullable: true),
                    Indexer = table.Column<string>(type: "text", nullable: false),
                    IndexerId = table.Column<int>(type: "integer", nullable: true),
                    TorrentInfoHash = table.Column<string>(type: "text", nullable: true),
                    Protocol = table.Column<string>(type: "text", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Quality = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: true),
                    Codec = table.Column<string>(type: "text", nullable: true),
                    Language = table.Column<string>(type: "text", nullable: true),
                    ReleaseGroup = table.Column<string>(type: "text", nullable: true),
                    QualityScore = table.Column<int>(type: "integer", nullable: false),
                    CustomFormatScore = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    MatchScore = table.Column<int>(type: "integer", nullable: false),
                    Part = table.Column<string>(type: "text", nullable: true),
                    Seeders = table.Column<int>(type: "integer", nullable: true),
                    Leechers = table.Column<int>(type: "integer", nullable: true),
                    PublishDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AddedToPendingAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReleasableAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingReleases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingReleases_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<int>(type: "integer", nullable: true),
                    DownloadQueueItemId = table.Column<int>(type: "integer", nullable: true),
                    SourcePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DestinationPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Quality = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    Warnings = table.Column<string>(type: "text", nullable: false),
                    Errors = table.Column<string>(type: "text", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Part = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportHistories_DownloadQueue_DownloadQueueItemId",
                        column: x => x.DownloadQueueItemId,
                        principalTable: "DownloadQueue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ImportHistories_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "CustomFormats",
                columns: new[] { "Id", "Created", "IncludeCustomFormatWhenRenaming", "IsCustomized", "IsSynced", "LastModified", "LastSyncedAt", "Name", "Specifications", "TrashCategory", "TrashDefaultScore", "TrashDescription", "TrashId" },
                values: new object[,]
                {
                    { 1, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, false, true, null, null, "BR-DISK", "[{\"Id\":1,\"Name\":\"BR-DISK\",\"Implementation\":\"ReleaseTitleSpecification\",\"Negate\":false,\"Required\":false,\"Fields\":{\"value\":\"(?i)\\\\b(M2TS|BDMV|MPEG-?[24])\\\\b\"}}]", "unwanted", -10000, "BR-DISK refers to raw Blu-ray disc structures that are not video files", "85c61753-c413-4d8b-9e0d-f7f6f61e8c42" },
                    { 2, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, false, true, null, null, "LQ", "[{\"Id\":2,\"Name\":\"LQ Groups\",\"Implementation\":\"ReleaseTitleSpecification\",\"Negate\":false,\"Required\":false,\"Fields\":{\"value\":\"(?i)\\\\b(YIFY|YTS|RARBG|PSA|MeGusta|SPARKS|EVO|MZABI)\\\\b\"}}]", "unwanted", -10000, "Releases from groups known for low quality encodes", "90a6f9a0-8c26-40f7-b4e2-25d86656e7a8" },
                    { 3, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, false, true, null, null, "Repack/Proper", "[{\"Id\":3,\"Name\":\"Repack/Proper\",\"Implementation\":\"ReleaseTitleSpecification\",\"Negate\":false,\"Required\":false,\"Fields\":{\"value\":\"(?i)\\\\b(REPACK|PROPER)\\\\b\"}}]", "release-version", 5, "Repack or Proper releases fix issues with the original release", "e6258996-0e87-4d8d-8c5e-4e5ab1a7c8e3" },
                    { 4, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, false, true, null, null, "x265 (HD)", "[{\"Id\":4,\"Name\":\"x265/HEVC\",\"Implementation\":\"ReleaseTitleSpecification\",\"Negate\":false,\"Required\":true,\"Fields\":{\"value\":\"(?i)\\\\b(x265|HEVC)\\\\b\"}},{\"Id\":5,\"Name\":\"Not 2160p\",\"Implementation\":\"ReleaseTitleSpecification\",\"Negate\":true,\"Required\":true,\"Fields\":{\"value\":\"(?i)\\\\b2160p\\\\b\"}}]", "unwanted", -10000, "x265/HEVC for non-4K content can have compatibility issues", "dc98083d-a25b-4e2e-9dcc-9aa4c3c33e87" },
                    { 5, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, false, true, null, null, "Upscaled", "[{\"Id\":6,\"Name\":\"Upscaled\",\"Implementation\":\"ReleaseTitleSpecification\",\"Negate\":false,\"Required\":false,\"Fields\":{\"value\":\"(?i)\\\\b(upscale[sd]?|AI[-\\\\. ]?enhanced)\\\\b\"}}]", "unwanted", -10000, "Content that has been upscaled from a lower resolution", "1b3994c5-51c6-4d4d-9c82-f5f6c46f2c3d" },
                    { 6, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, false, true, null, null, "Scene", "[{\"Id\":7,\"Name\":\"Scene Flag\",\"Implementation\":\"IndexerFlagSpecification\",\"Negate\":false,\"Required\":false,\"Fields\":{\"value\":1}}]", "indexer-flags", 0, "Scene releases follow strict naming and encoding rules", "a1b2c3d4-5e6f-7a8b-9c0d-e1f2a3b4c5d6" },
                    { 7, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, false, true, null, null, "WEB-DL", "[{\"Id\":8,\"Name\":\"WEB-DL\",\"Implementation\":\"ReleaseTitleSpecification\",\"Negate\":false,\"Required\":false,\"Fields\":{\"value\":\"(?i)\\\\bWEB[-\\\\. ]?DL\\\\b\"}}]", "source", 10, "WEB-DL is typically higher quality than WEBRip", "2b3c4d5e-6f7a-8b9c-0d1e-2f3a4b5c6d7e" }
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
                columns: new[] { "Id", "CutoffFormatScore", "CutoffQuality", "FormatItems", "FormatScoreIncrement", "IsCustomized", "IsDefault", "IsSynced", "Items", "LastTrashScoreSync", "MaxSize", "MinFormatScore", "MinSize", "Name", "TrashId", "TrashScoreSet", "UpgradesAllowed" },
                values: new object[,]
                {
                    { 1, null, 15, "[{\"Id\":0,\"FormatId\":1,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":2,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":3,\"Format\":null,\"Score\":5},{\"Id\":0,\"FormatId\":4,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":5,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":6,\"Format\":null,\"Score\":0},{\"Id\":0,\"FormatId\":7,\"Format\":null,\"Score\":10}]", 1, false, true, false, "[{\"Name\":\"WEB 1080p\",\"Quality\":0,\"Allowed\":true,\"Items\":[{\"Name\":\"WEBDL-1080p\",\"Quality\":15,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEBRip-1080p\",\"Quality\":14,\"Allowed\":true,\"Items\":null,\"Id\":null}],\"Id\":null},{\"Name\":\"Bluray-1080p\",\"Quality\":11,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"HDTV-1080p\",\"Quality\":6,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEB 720p\",\"Quality\":0,\"Allowed\":true,\"Items\":[{\"Name\":\"WEBDL-720p\",\"Quality\":3,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEBRip-720p\",\"Quality\":10,\"Allowed\":true,\"Items\":null,\"Id\":null}],\"Id\":null},{\"Name\":\"Bluray-720p\",\"Quality\":7,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"HDTV-720p\",\"Quality\":5,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEB 480p\",\"Quality\":0,\"Allowed\":true,\"Items\":[{\"Name\":\"WEBDL-480p\",\"Quality\":2,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEBRip-480p\",\"Quality\":8,\"Allowed\":true,\"Items\":null,\"Id\":null}],\"Id\":null},{\"Name\":\"Bluray-576p\",\"Quality\":16,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"Bluray-480p\",\"Quality\":9,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"DVD\",\"Quality\":4,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"SDTV\",\"Quality\":1,\"Allowed\":true,\"Items\":null,\"Id\":null}]", null, null, 0, null, "WEB-1080p (Alternative)", null, null, true },
                    { 2, null, 19, "[{\"Id\":0,\"FormatId\":1,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":2,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":3,\"Format\":null,\"Score\":5},{\"Id\":0,\"FormatId\":4,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":5,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":6,\"Format\":null,\"Score\":0},{\"Id\":0,\"FormatId\":7,\"Format\":null,\"Score\":10}]", 1, false, false, false, "[{\"Name\":\"WEB 2160p\",\"Quality\":0,\"Allowed\":true,\"Items\":[{\"Name\":\"WEBDL-2160p\",\"Quality\":19,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEBRip-2160p\",\"Quality\":18,\"Allowed\":true,\"Items\":null,\"Id\":null}],\"Id\":null},{\"Name\":\"Bluray-2160p\",\"Quality\":13,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"HDTV-2160p\",\"Quality\":17,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEB 1080p\",\"Quality\":0,\"Allowed\":true,\"Items\":[{\"Name\":\"WEBDL-1080p\",\"Quality\":15,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEBRip-1080p\",\"Quality\":14,\"Allowed\":true,\"Items\":null,\"Id\":null}],\"Id\":null},{\"Name\":\"Bluray-1080p\",\"Quality\":11,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"HDTV-1080p\",\"Quality\":6,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEB 720p\",\"Quality\":0,\"Allowed\":true,\"Items\":[{\"Name\":\"WEBDL-720p\",\"Quality\":3,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEBRip-720p\",\"Quality\":10,\"Allowed\":true,\"Items\":null,\"Id\":null}],\"Id\":null},{\"Name\":\"Bluray-720p\",\"Quality\":7,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"HDTV-720p\",\"Quality\":5,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEB 480p\",\"Quality\":0,\"Allowed\":true,\"Items\":[{\"Name\":\"WEBDL-480p\",\"Quality\":2,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"WEBRip-480p\",\"Quality\":8,\"Allowed\":true,\"Items\":null,\"Id\":null}],\"Id\":null},{\"Name\":\"Bluray-576p\",\"Quality\":16,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"Bluray-480p\",\"Quality\":9,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"DVD\",\"Quality\":4,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"SDTV\",\"Quality\":1,\"Allowed\":true,\"Items\":null,\"Id\":null}]", null, null, 0, null, "WEB-2160p (Alternative)", null, null, true }
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
                name: "IX_ChannelLeagueMappings_ChannelId_LeagueId",
                table: "ChannelLeagueMappings",
                columns: new[] { "ChannelId", "LeagueId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelLeagueMappings_Confidence",
                table: "ChannelLeagueMappings",
                column: "Confidence");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelLeagueMappings_IsManual",
                table: "ChannelLeagueMappings",
                column: "IsManual");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelLeagueMappings_IsPreferred",
                table: "ChannelLeagueMappings",
                column: "IsPreferred");

            migrationBuilder.CreateIndex(
                name: "UX_ChannelLeagueMappings_PreferredPerLeague",
                table: "ChannelLeagueMappings",
                column: "LeagueId",
                unique: true,
                filter: "\"IsPreferred\" = true");

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
                name: "IX_DownloadQueue_EventId_Status",
                table: "DownloadQueue",
                columns: new[] { "EventId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadQueue_Status",
                table: "DownloadQueue",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DvrRecordings_ChannelId",
                table: "DvrRecordings",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_DvrRecordings_EventId",
                table: "DvrRecordings",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_DvrRecordings_ScheduledEnd",
                table: "DvrRecordings",
                column: "ScheduledEnd");

            migrationBuilder.CreateIndex(
                name: "IX_DvrRecordings_ScheduledStart",
                table: "DvrRecordings",
                column: "ScheduledStart");

            migrationBuilder.CreateIndex(
                name: "IX_DvrRecordings_Status",
                table: "DvrRecordings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EpgChannels_ChannelId",
                table: "EpgChannels",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_EpgChannels_EpgSourceId",
                table: "EpgChannels",
                column: "EpgSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_EpgChannels_NormalizedName",
                table: "EpgChannels",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_EpgPrograms_ChannelId",
                table: "EpgPrograms",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_EpgPrograms_EndTime",
                table: "EpgPrograms",
                column: "EndTime");

            migrationBuilder.CreateIndex(
                name: "IX_EpgPrograms_EpgSourceId",
                table: "EpgPrograms",
                column: "EpgSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_EpgPrograms_IsSportsProgram",
                table: "EpgPrograms",
                column: "IsSportsProgram");

            migrationBuilder.CreateIndex(
                name: "IX_EpgPrograms_MatchedEventId",
                table: "EpgPrograms",
                column: "MatchedEventId");

            migrationBuilder.CreateIndex(
                name: "IX_EpgPrograms_StartTime",
                table: "EpgPrograms",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_EpgSources_IsActive",
                table: "EpgSources",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_EventFileHistory_Date",
                table: "EventFileHistory",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_EventFileHistory_EventId",
                table: "EventFileHistory",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventFiles_EventId",
                table: "EventFiles",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventFiles_Exists",
                table: "EventFiles",
                column: "Exists");

            migrationBuilder.CreateIndex(
                name: "IX_EventFiles_PartNumber",
                table: "EventFiles",
                column: "PartNumber");

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
                name: "IX_Events_Monitored",
                table: "Events",
                column: "Monitored");

            migrationBuilder.CreateIndex(
                name: "IX_Events_Sport",
                table: "Events",
                column: "Sport");

            migrationBuilder.CreateIndex(
                name: "IX_Events_Status",
                table: "Events",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FollowedTeams_ExternalId",
                table: "FollowedTeams",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FollowedTeams_Sport",
                table: "FollowedTeams",
                column: "Sport");

            migrationBuilder.CreateIndex(
                name: "IX_GrabHistory_EventId",
                table: "GrabHistory",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_GrabHistory_FileExists",
                table: "GrabHistory",
                column: "FileExists");

            migrationBuilder.CreateIndex(
                name: "IX_GrabHistory_GrabbedAt",
                table: "GrabHistory",
                column: "GrabbedAt");

            migrationBuilder.CreateIndex(
                name: "IX_GrabHistory_Guid",
                table: "GrabHistory",
                column: "Guid");

            migrationBuilder.CreateIndex(
                name: "IX_GrabHistory_WasImported",
                table: "GrabHistory",
                column: "WasImported");

            migrationBuilder.CreateIndex(
                name: "IX_ImportHistories_DestinationPath",
                table: "ImportHistories",
                column: "DestinationPath");

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
                name: "IX_ImportListExclusions_TvdbId",
                table: "ImportListExclusions",
                column: "TvdbId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndexerStatuses_DisabledUntil",
                table: "IndexerStatuses",
                column: "DisabledUntil");

            migrationBuilder.CreateIndex(
                name: "IX_IndexerStatuses_GrabDisabledUntil",
                table: "IndexerStatuses",
                column: "GrabDisabledUntil");

            migrationBuilder.CreateIndex(
                name: "IX_IndexerStatuses_IndexerId",
                table: "IndexerStatuses",
                column: "IndexerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndexerStatuses_QueryDisabledUntil",
                table: "IndexerStatuses",
                column: "QueryDisabledUntil");

            migrationBuilder.CreateIndex(
                name: "IX_IptvChannels_Group",
                table: "IptvChannels",
                column: "Group");

            migrationBuilder.CreateIndex(
                name: "IX_IptvChannels_IsSportsChannel",
                table: "IptvChannels",
                column: "IsSportsChannel");

            migrationBuilder.CreateIndex(
                name: "IX_IptvChannels_Name",
                table: "IptvChannels",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_IptvChannels_SourceId",
                table: "IptvChannels",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_IptvChannels_Status",
                table: "IptvChannels",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_IptvChannels_TvgId",
                table: "IptvChannels",
                column: "TvgId");

            migrationBuilder.CreateIndex(
                name: "IX_IptvSources_IsActive",
                table: "IptvSources",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_IptvSources_Name",
                table: "IptvSources",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Leagues_ExternalId",
                table: "Leagues",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_Leagues_Name_Sport",
                table: "Leagues",
                columns: new[] { "Name", "Sport" });

            migrationBuilder.CreateIndex(
                name: "IX_Leagues_RootFolderId",
                table: "Leagues",
                column: "RootFolderId");

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
                name: "IX_PendingImports_DownloadClientId",
                table: "PendingImports",
                column: "DownloadClientId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingImports_DownloadId",
                table: "PendingImports",
                column: "DownloadId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingImports_Status",
                table: "PendingImports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PendingImports_SuggestedEventId",
                table: "PendingImports",
                column: "SuggestedEventId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingReleases_EventId",
                table: "PendingReleases",
                column: "EventId");

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
                name: "IX_ReleaseCache_CachedAt",
                table: "ReleaseCache",
                column: "CachedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCache_ExpiresAt",
                table: "ReleaseCache",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCache_Guid",
                table: "ReleaseCache",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCache_Indexer",
                table: "ReleaseCache",
                column: "Indexer");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCache_NormalizedTitle",
                table: "ReleaseCache",
                column: "NormalizedTitle");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCache_PublishDate",
                table: "ReleaseCache",
                column: "PublishDate");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCache_RoundNumber",
                table: "ReleaseCache",
                column: "RoundNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCache_SportPrefix",
                table: "ReleaseCache",
                column: "SportPrefix");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCache_SportPrefix_Year_RoundNumber",
                table: "ReleaseCache",
                columns: new[] { "SportPrefix", "Year", "RoundNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCache_Year",
                table: "ReleaseCache",
                column: "Year");

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
                name: "ChannelLeagueMappings");

            migrationBuilder.DropTable(
                name: "DelayProfiles");

            migrationBuilder.DropTable(
                name: "DvrQualityProfiles");

            migrationBuilder.DropTable(
                name: "DvrRecordings");

            migrationBuilder.DropTable(
                name: "EpgChannels");

            migrationBuilder.DropTable(
                name: "EpgPrograms");

            migrationBuilder.DropTable(
                name: "EventFileHistory");

            migrationBuilder.DropTable(
                name: "EventFiles");

            migrationBuilder.DropTable(
                name: "FollowedTeams");

            migrationBuilder.DropTable(
                name: "GrabHistory");

            migrationBuilder.DropTable(
                name: "ImportHistories");

            migrationBuilder.DropTable(
                name: "ImportListExclusions");

            migrationBuilder.DropTable(
                name: "ImportLists");

            migrationBuilder.DropTable(
                name: "IndexerStatuses");

            migrationBuilder.DropTable(
                name: "LeagueTeams");

            migrationBuilder.DropTable(
                name: "MediaManagementSettings");

            migrationBuilder.DropTable(
                name: "MetadataProviders");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "PendingImports");

            migrationBuilder.DropTable(
                name: "PendingReleases");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "ProfileFormatItems");

            migrationBuilder.DropTable(
                name: "QualityDefinitions");

            migrationBuilder.DropTable(
                name: "QualityProfiles");

            migrationBuilder.DropTable(
                name: "ReleaseCache");

            migrationBuilder.DropTable(
                name: "ReleaseProfiles");

            migrationBuilder.DropTable(
                name: "RemotePathMappings");

            migrationBuilder.DropTable(
                name: "RestoreReports");

            migrationBuilder.DropTable(
                name: "SystemEvents");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "Tasks");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "IptvChannels");

            migrationBuilder.DropTable(
                name: "EpgSources");

            migrationBuilder.DropTable(
                name: "DownloadQueue");

            migrationBuilder.DropTable(
                name: "Indexers");

            migrationBuilder.DropTable(
                name: "CustomFormats");

            migrationBuilder.DropTable(
                name: "IptvSources");

            migrationBuilder.DropTable(
                name: "DownloadClients");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "Leagues");

            migrationBuilder.DropTable(
                name: "RootFolders");
        }
    }
}
