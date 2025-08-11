using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using FluentMigrator;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(229)]
    public class enable_season_pack_seeding_goal : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Execute.WithConnection(SetSeasonPackSeedingGoal);
        }

        private void SetSeasonPackSeedingGoal(IDbConnection conn, IDbTransaction tran)
        {
            var updatedIndexers = new List<object>();

            using var selectCommand = conn.CreateCommand();

            selectCommand.Transaction = tran;
            selectCommand.CommandText = "SELECT * FROM \"Indexers\"";

            using var reader = selectCommand.ExecuteReader();

            while (reader.Read())
            {
                var idIndex = reader.GetOrdinal("Id");
                var settingsIndex = reader.GetOrdinal("Settings");

                var id = reader.GetInt32(idIndex);
                var settings = Json.Deserialize<Dictionary<string, object>>(reader.GetString(settingsIndex));

                if (settings.TryGetValue("seedCriteria", out var seedCriteriaToken) && seedCriteriaToken is JObject seedCriteria)
                {
                    if (seedCriteria?["seasonPackSeedTime"] != null)
                    {
                        seedCriteria["seasonPackSeedGoal"] = 1;

                        if (seedCriteria["seedRatio"] != null)
                        {
                            seedCriteria["seasonPackSeedRatio"] = seedCriteria["seedRatio"];
                        }

                        updatedIndexers.Add(new
                        {
                            Settings = settings.ToJson(),
                            Id = id,
                        });
                    }
                }
            }

            if (updatedIndexers.Any())
            {
                var updateSql = "UPDATE \"Indexers\" SET \"Settings\" = @Settings WHERE \"Id\" = @Id";
                conn.Execute(updateSql, updatedIndexers, transaction: tran);
            }
        }
    }
}
