using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(224)]
    public class add_fight_tables : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Create FightEvents table
            Create.TableForModel("FightEvents")
                .WithColumn("FightarrEventId").AsInt32().NotNullable().Unique()
                .WithColumn("OrganizationId").AsInt32().NotNullable()
                .WithColumn("OrganizationName").AsString().Nullable()
                .WithColumn("OrganizationType").AsString().Nullable()
                .WithColumn("Title").AsString().NotNullable()
                .WithColumn("CleanTitle").AsString().NotNullable()
                .WithColumn("Slug").AsString().NotNullable()
                .WithColumn("EventNumber").AsString().Nullable()
                .WithColumn("EventDate").AsDateTime().NotNullable()
                .WithColumn("EventType").AsString().Nullable()
                .WithColumn("Location").AsString().Nullable()
                .WithColumn("Venue").AsString().Nullable()
                .WithColumn("Broadcaster").AsString().Nullable()
                .WithColumn("Overview").AsString().Nullable()
                .WithColumn("Status").AsString().NotNullable()
                .WithColumn("Monitored").AsBoolean().NotNullable()
                .WithColumn("PosterUrl").AsString().Nullable()
                .WithColumn("BannerUrl").AsString().Nullable()
                .WithColumn("Organization").AsString().Nullable()
                .WithColumn("OrganizationLogoUrl").AsString().Nullable()
                .WithColumn("Images").AsString().Nullable(); // JSON field for images

            // Create FightCards table (replaces Episodes)
            Create.TableForModel("FightCards")
                .WithColumn("FightEventId").AsInt32().NotNullable()
                .WithColumn("CardNumber").AsInt32().NotNullable()
                .WithColumn("CardSection").AsString().NotNullable()
                .WithColumn("Title").AsString().Nullable()
                .WithColumn("AirDateUtc").AsDateTime().NotNullable()
                .WithColumn("Monitored").AsBoolean().NotNullable();

            // Create Fights table
            Create.TableForModel("Fights")
                .WithColumn("FightCardId").AsInt32().NotNullable()
                .WithColumn("FightarrFightId").AsInt32().NotNullable()
                .WithColumn("Fighter1Id").AsInt32().NotNullable()
                .WithColumn("Fighter1Name").AsString().Nullable()
                .WithColumn("Fighter1Record").AsString().Nullable()
                .WithColumn("Fighter2Id").AsInt32().NotNullable()
                .WithColumn("Fighter2Name").AsString().Nullable()
                .WithColumn("Fighter2Record").AsString().Nullable()
                .WithColumn("WeightClass").AsString().Nullable()
                .WithColumn("IsTitleFight").AsBoolean().NotNullable()
                .WithColumn("IsMainEvent").AsBoolean().NotNullable()
                .WithColumn("FightOrder").AsInt32().NotNullable()
                .WithColumn("Result").AsString().Nullable()
                .WithColumn("Method").AsString().Nullable()
                .WithColumn("Round").AsInt32().Nullable()
                .WithColumn("Time").AsString().Nullable()
                .WithColumn("Referee").AsString().Nullable()
                .WithColumn("Notes").AsString().Nullable();

            // Create Fighters table
            Create.TableForModel("Fighters")
                .WithColumn("FightarrFighterId").AsInt32().NotNullable().Unique()
                .WithColumn("Name").AsString().NotNullable()
                .WithColumn("Slug").AsString().Nullable()
                .WithColumn("Nickname").AsString().Nullable()
                .WithColumn("WeightClass").AsString().Nullable()
                .WithColumn("Nationality").AsString().Nullable()
                .WithColumn("Wins").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Losses").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Draws").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("NoContests").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("BirthDate").AsDateTime().Nullable()
                .WithColumn("Height").AsString().Nullable()
                .WithColumn("Reach").AsString().Nullable()
                .WithColumn("ImageUrl").AsString().Nullable()
                .WithColumn("Bio").AsString().Nullable()
                .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true);

            // Create indexes for performance
            // Note: FightarrEventId already has unique index from .Unique() constraint
            Create.Index("IX_FightEvents_EventDate").OnTable("FightEvents").OnColumn("EventDate");
            Create.Index("IX_FightEvents_Status").OnTable("FightEvents").OnColumn("Status");
            Create.Index("IX_FightEvents_Monitored").OnTable("FightEvents").OnColumn("Monitored");

            Create.Index("IX_FightCards_FightEventId").OnTable("FightCards").OnColumn("FightEventId");
            Create.Index("IX_FightCards_CardNumber").OnTable("FightCards").OnColumn("CardNumber");

            Create.Index("IX_Fights_FightCardId").OnTable("Fights").OnColumn("FightCardId");
            Create.Index("IX_Fights_FightarrFightId").OnTable("Fights").OnColumn("FightarrFightId");

            // Note: FightarrFighterId already has unique index from .Unique() constraint
            Create.Index("IX_Fighters_Name").OnTable("Fighters").OnColumn("Name");
        }
    }
}
