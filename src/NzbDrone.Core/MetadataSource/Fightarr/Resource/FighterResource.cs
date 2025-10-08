using System;

namespace NzbDrone.Core.MetadataSource.Fightarr.Resource
{
    public class FighterResource
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Slug { get; set; }
        public string Nickname { get; set; }
        public string WeightClass { get; set; }
        public string Nationality { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Draws { get; set; }
        public int NoContests { get; set; }
        public DateTime? BirthDate { get; set; }
        public string Height { get; set; }
        public string Reach { get; set; }
        public string ImageUrl { get; set; }
        public string Bio { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Computed property for displaying record
        public string Record => $"{Wins}-{Losses}-{Draws}";
    }
}
