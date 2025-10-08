using System;

namespace NzbDrone.Core.MetadataSource.Fightarr.Resource
{
    public class OrganizationResource
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Slug { get; set; }
        public string Type { get; set; }
        public string Country { get; set; }
        public string Description { get; set; }
        public string PosterUrl { get; set; }
        public string BannerUrl { get; set; }
        public string LogoUrl { get; set; }
        public string Website { get; set; }
        public int? Founded { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
