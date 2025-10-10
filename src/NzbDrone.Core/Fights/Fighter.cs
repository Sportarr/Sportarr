using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Fights
{
    /// <summary>
    /// Represents a fighter
    /// Maps to "Fighter" in Fightarr-API
    /// </summary>
    public class Fighter : ModelBase
    {
        public int FightarrFighterId { get; set; }  // ID from Fightarr-API

        public string Name { get; set; }
        public string Slug { get; set; }
        public string Nickname { get; set; }
        public string WeightClass { get; set; }
        public string Nationality { get; set; }

        // Record
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Draws { get; set; }
        public int NoContests { get; set; }

        // Physical stats
        public DateTime? BirthDate { get; set; }
        public string Height { get; set; }
        public string Reach { get; set; }
        public string ImageUrl { get; set; }
        public string Bio { get; set; }
        public bool IsActive { get; set; }
    }
}
