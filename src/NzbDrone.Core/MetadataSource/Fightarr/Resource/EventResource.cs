using System;
using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource.Fightarr.Resource
{
    public class EventResource
    {
        public int Id { get; set; }
        public int OrganizationId { get; set; }
        public string Title { get; set; }
        public string Slug { get; set; }
        public string EventNumber { get; set; }
        public DateTime EventDate { get; set; }
        public string EventType { get; set; }
        public string Location { get; set; }
        public string Venue { get; set; }
        public string Broadcaster { get; set; }
        public string Description { get; set; }
        public string PosterUrl { get; set; }
        public string BannerUrl { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Nested organization data
        public OrganizationResource Organization { get; set; }

        // Fights for this event
        public List<FightResource> Fights { get; set; }
    }
}
