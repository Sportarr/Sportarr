using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Fights
{
    public interface IFightEventRepository : IBasicRepository<FightEvent>
    {
        FightEvent FindByFightarrEventId(int fightarrEventId);
        List<FightEvent> GetUpcomingEvents();
        List<FightEvent> GetEventsByOrganization(string organizationSlug);
        List<FightEvent> SearchEvents(string query);
        List<FightEvent> GetEventsByDateRange(DateTime startDate, DateTime endDate);
    }
}
