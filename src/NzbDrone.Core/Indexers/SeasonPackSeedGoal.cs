using NzbDrone.Core.Annotations;

namespace NzbDrone.Core.Indexers;

public enum SeasonPackSeedGoal
{
    [FieldOption(Label = "IndexerSettingsSeasonPackSeedGoalUseStandardGoals")]
    UseStandardSeedGoal = 0,
    [FieldOption(Label = "IndexerSettingsSeasonPackSeedGoalUseSeasonPackGoals")]
    UseSeasonPackSeedGoal = 1
}
