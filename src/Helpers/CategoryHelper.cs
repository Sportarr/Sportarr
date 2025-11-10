namespace Sportarr.Api.Helpers;

/// <summary>
/// Helper for mapping Newznab/Torznab category IDs to names
/// </summary>
public static class CategoryHelper
{
    /// <summary>
    /// Map category IDs to names (Newznab/Torznab standard categories)
    /// </summary>
    public static string GetCategoryName(int categoryId)
    {
        return categoryId switch
        {
            // Movies (2000 series)
            2000 => "Movies",
            2010 => "Movies/Foreign",
            2020 => "Movies/Other",
            2030 => "Movies/SD",
            2040 => "Movies/HD",
            2045 => "Movies/UHD",
            2050 => "Movies/BluRay",
            2060 => "Movies/3D",
            2070 => "Movies/DVD",
            2080 => "Movies/WEB-DL",

            // TV (5000 series)
            5000 => "TV",
            5010 => "TV/WEB-DL",
            5020 => "TV/Foreign",
            5030 => "TV/SD",
            5040 => "TV/HD",
            5045 => "TV/UHD",
            5050 => "TV/Other",
            5060 => "TV/Sport",
            5070 => "TV/Anime",
            5080 => "TV/Documentary",

            // Other categories for completeness
            1000 => "Console",
            3000 => "Audio",
            4000 => "PC",
            6000 => "XXX",
            7000 => "Books",
            8000 => "Other",

            _ => $"Category {categoryId}"
        };
    }
}
