using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using System.Linq;

namespace NzbDrone.Core.Fights
{
    public class FightEventRepository : BasicRepository<FightEvent>, IFightEventRepository
    {
        public FightEventRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public FightEvent FindByFightarrEventId(int fightarrEventId)
        {
            return Query(e => e.FightarrEventId == fightarrEventId).FirstOrDefault();
        }

        public List<FightEvent> GetUpcomingEvents()
        {
            var now = DateTime.UtcNow;
            return Query(e => e.EventDate >= now && (e.Status == "Announced" || e.Status == "Upcoming"))
                .OrderBy(e => e.EventDate)
                .ToList();
        }

        public List<FightEvent> GetEventsByOrganization(string organizationSlug)
        {
            return Query(e => e.Organization.ToLower() == organizationSlug.ToLower())
                .OrderByDescending(e => e.EventDate)
                .ToList();
        }

        public List<FightEvent> SearchEvents(string query)
        {
            var lowerQuery = query.ToLower();
            return Query(e =>
                e.Title.ToLower().Contains(lowerQuery) ||
                e.Organization.ToLower().Contains(lowerQuery) ||
                e.Location.ToLower().Contains(lowerQuery) ||
                e.Venue.ToLower().Contains(lowerQuery))
                .OrderByDescending(e => e.EventDate)
                .ToList();
        }

        public List<FightEvent> GetEventsByDateRange(DateTime startDate, DateTime endDate)
        {
            return Query(e => e.EventDate >= startDate && e.EventDate <= endDate)
                .OrderBy(e => e.EventDate)
                .ToList();
        }
    }
}
