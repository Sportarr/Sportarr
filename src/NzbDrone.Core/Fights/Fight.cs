using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Fights
{
    /// <summary>
    /// Represents an individual fight/matchup
    /// Maps to "Fight" in Fightarr-API
    /// </summary>
    public class Fight : ModelBase
    {
        public int FightarrFightId { get; set; }  // ID from Fightarr-API
        public int FightCardId { get; set; }
        public FightCard FightCard { get; set; }

        // Fighter information
        public int Fighter1Id { get; set; }
        public string Fighter1Name { get; set; }
        public string Fighter1Record { get; set; }  // "20-5-0"

        public int Fighter2Id { get; set; }
        public string Fighter2Name { get; set; }
        public string Fighter2Record { get; set; }

        // Fight details
        public string WeightClass { get; set; }     // "Heavyweight", "Welterweight"
        public bool IsTitleFight { get; set; }
        public bool IsMainEvent { get; set; }
        public int FightOrder { get; set; }         // 1 = main event, higher = earlier on card

        // Result (if completed)
        public string Result { get; set; }          // "Fighter1 Win - KO Round 2"
        public string Method { get; set; }          // "KO", "TKO", "Submission", "Decision"
        public int? Round { get; set; }
        public string Time { get; set; }            // "4:32"
        public string Referee { get; set; }
        public string Notes { get; set; }
    }
}
