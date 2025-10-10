namespace NzbDrone.Core.Fights
{
    public interface IFightEventService
    {
        FightEvent GetEvent(int id);
        FightEvent FindByFightarrEventId(int fightarrEventId);
        List<FightEvent> GetAllEvents();
        List<FightEvent> GetUpcomingEvents();
        List<FightEvent> GetEventsByOrganization(string organizationSlug);
        List<FightEvent> SearchEvents(string query);
        FightEvent AddEvent(FightEvent fightEvent);
        FightEvent UpdateEvent(FightEvent fightEvent);
        void DeleteEvent(int id);
        Task SyncWithFightarrApi();
    }
}
