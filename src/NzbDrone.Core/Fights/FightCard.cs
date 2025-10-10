using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Fights
{
    /// <summary>
    /// Represents a section of a fight card (Early Prelims, Prelims, Main Card)
    /// Maps to "Episode" concept but for fight cards
    /// </summary>
    public class FightCard : ModelBase
    {
        public FightCard()
        {
            Fights = new List<Fight>();
        }

        public int FightEventId { get; set; }
        public FightEvent FightEvent { get; set; }

        // Card section: 1 = Early Prelims, 2 = Prelims, 3 = Main Card
        public int CardNumber { get; set; }
        public string CardSection { get; set; }  // "Early Prelims", "Prelims", "Main Card"

        public string Title { get; set; }  // e.g., "UFC 300 - Main Card"
        public DateTime AirDateUtc { get; set; }

        // Download/monitoring info
        public bool Monitored { get; set; }
        public bool HasFile { get; set; }
        public string FilePath { get; set; }
        public long FileSize { get; set; }
        public string Quality { get; set; }

        public List<Fight> Fights { get; set; }
    }
}
