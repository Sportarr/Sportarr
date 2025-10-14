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
    public List<QualityItem> Items { get; set; } = new();
}

public class QualityItem
{
    public required string Name { get; set; }
    public int Quality { get; set; }
    public bool Allowed { get; set; }
}
