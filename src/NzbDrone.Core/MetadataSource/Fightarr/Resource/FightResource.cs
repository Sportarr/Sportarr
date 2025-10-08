using System;

namespace NzbDrone.Core.MetadataSource.Fightarr.Resource
{
    public class FightResource
    {
        public int Id { get; set; }
        public int EventId { get; set; }
        public int Fighter1Id { get; set; }
        public int Fighter2Id { get; set; }
        public string WeightClass { get; set; }
        public bool IsTitleFight { get; set; }
        public bool IsMainEvent { get; set; }
        public int FightOrder { get; set; }
        public string Result { get; set; }
        public string Method { get; set; }
        public int? Round { get; set; }
        public string Time { get; set; }
        public string Referee { get; set; }
        public string Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Nested fighter data
        public FighterResource Fighter1 { get; set; }
        public FighterResource Fighter2 { get; set; }
    }
}
