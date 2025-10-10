using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Fights
{
    /// <summary>
    /// Represents a fighting event (UFC 300, Bellator 301, etc.)
    /// Maps to "Event" in Fightarr-API
    /// </summary>
    public class FightEvent : ModelBase
    {
        public FightEvent()
        {
            Images = new List<MediaCover.MediaCover>();
            Tags = new HashSet<int>();
            FightCards = new List<FightCard>();
        }

        // Fightarr-API Event fields
        public int FightarrEventId { get; set; }  // ID from Fightarr-API
        public int OrganizationId { get; set; }   // UFC, Bellator, etc.
        public string OrganizationName { get; set; }
        public string OrganizationType { get; set; }  // MMA, Boxing, etc.

        public string Title { get; set; }         // "UFC 300: Pereira vs Hill"
        public string CleanTitle { get; set; }
        public string Slug { get; set; }
        public string EventNumber { get; set; }   // "300" or "202406" for non-numbered events

        public DateTime EventDate { get; set; }
        public string EventType { get; set; }     // "PPV", "Fight Night", etc.
        public string Location { get; set; }       // "Las Vegas, Nevada, USA"
        public string Venue { get; set; }          // "T-Mobile Arena"
        public string Broadcaster { get; set; }    // "ESPN+", "PPV"
        public string Overview { get; set; }
        public string Status { get; set; }         // "Announced", "Upcoming", "Completed"

        // Fightarr-specific fields
        public bool Monitored { get; set; }
        public int QualityProfileId { get; set; }
        public string Path { get; set; }
        public DateTime? LastInfoSync { get; set; }
        public List<MediaCover.MediaCover> Images { get; set; }
        public HashSet<int> Tags { get; set; }

        // Related data
        public List<FightCard> FightCards { get; set; }  // Early Prelims, Prelims, Main Card
    }
}
