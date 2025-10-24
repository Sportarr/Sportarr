namespace Fightarr.Api.Models;

public class FightarrConfig
{
    public string ApiKey { get; set; } = Guid.NewGuid().ToString("N");
    public string InstanceName { get; set; } = "Fightarr";
    public string Theme { get; set; } = "auto";
    public string Branch { get; set; } = "main";
    public bool Analytics { get; set; } = false;
    public string UrlBase { get; set; } = string.Empty;
    public bool IsProduction { get; set; }
}

public class Tag
{
    public int Id { get; set; }
    public required string Label { get; set; }
    public string Color { get; set; } = "#3b82f6";
}

public class QualityProfile
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>
    /// If disabled, qualities will not be upgraded
    /// </summary>
    public bool UpgradesAllowed { get; set; } = true;

    /// <summary>
    /// Upgrade until this quality is reached, then stop
    /// </summary>
    public int? CutoffQuality { get; set; }

    /// <summary>
    /// Allowed quality levels in this profile
    /// </summary>
    public List<QualityItem> Items { get; set; } = new();

    /// <summary>
    /// Custom format scores for this profile
    /// </summary>
    public List<ProfileFormatItem> FormatItems { get; set; } = new();

    /// <summary>
    /// Minimum custom format score required (-10000 to +10000)
    /// Releases with lower score are rejected
    /// </summary>
    public int? MinFormatScore { get; set; }

    /// <summary>
    /// Upgrade until this custom format score is reached
    /// </summary>
    public int? CutoffFormatScore { get; set; }

    /// <summary>
    /// Minimum required improvement of custom format score for upgrades
    /// </summary>
    public int FormatScoreIncrement { get; set; } = 1;

    /// <summary>
    /// Minimum size in MB
    /// </summary>
    public double? MinSize { get; set; }

    /// <summary>
    /// Maximum size in MB
    /// </summary>
    public double? MaxSize { get; set; }
}

public class QualityItem
{
    public required string Name { get; set; }
    public int Quality { get; set; }
    public bool Allowed { get; set; }
}
