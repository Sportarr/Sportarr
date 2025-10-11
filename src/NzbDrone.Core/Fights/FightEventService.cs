using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Fights
{
    public class FightEventService : IFightEventService, IHandle<FightEventAddedEvent>
    {
        private readonly IFightEventRepository _fightEventRepository;
        private readonly IFightarrMetadataService _metadataService;
        private readonly IEventAggregator _eventAggregator;

        public FightEventService(
            IFightEventRepository fightEventRepository,
            IFightarrMetadataService metadataService,
            IEventAggregator eventAggregator)
        {
            _fightEventRepository = fightEventRepository;
            _metadataService = metadataService;
            _eventAggregator = eventAggregator;
        }

        public FightEvent GetEvent(int id)
        {
            return _fightEventRepository.Get(id);
        }

        public FightEvent FindByFightarrEventId(int fightarrEventId)
        {
            return _fightEventRepository.FindByFightarrEventId(fightarrEventId);
        }

        public List<FightEvent> GetAllEvents()
        {
            return _fightEventRepository.All().ToList();
        }

        public List<FightEvent> GetUpcomingEvents()
        {
            return _fightEventRepository.GetUpcomingEvents();
        }

        public List<FightEvent> GetEventsByOrganization(string organizationSlug)
        {
            return _fightEventRepository.GetEventsByOrganization(organizationSlug);
        }

        public List<FightEvent> SearchEvents(string query)
        {
            return _fightEventRepository.SearchEvents(query);
        }

        public FightEvent AddEvent(FightEvent fightEvent)
        {
            var addedEvent = _fightEventRepository.Insert(fightEvent);
            _eventAggregator.PublishEvent(new FightEventAddedEvent(addedEvent));
            return addedEvent;
        }

        public FightEvent UpdateEvent(FightEvent fightEvent)
        {
            return _fightEventRepository.Update(fightEvent);
        }

        public void DeleteEvent(int id)
        {
            _fightEventRepository.Delete(id);
        }

        public async Task SyncWithFightarrApi()
        {
            // Fetch upcoming events from Fightarr API
            var apiEvents = await _metadataService.GetUpcomingEvents();

            foreach (var apiEvent in apiEvents)
            {
                // Check if event already exists in local database
                var existingEvent = FindByFightarrEventId(apiEvent.FightarrEventId);

                if (existingEvent == null)
                {
                    // Add new event
                    AddEvent(apiEvent);
                }
                else
                {
                    // Update existing event
                    existingEvent.Title = apiEvent.Title;
                    existingEvent.EventNumber = apiEvent.EventNumber;
                    existingEvent.EventDate = apiEvent.EventDate;
                    existingEvent.EventType = apiEvent.EventType;
                    existingEvent.Location = apiEvent.Location;
                    existingEvent.Venue = apiEvent.Venue;
                    existingEvent.Broadcaster = apiEvent.Broadcaster;
                    existingEvent.Status = apiEvent.Status;

                    // Images are stored in the Images collection
                    // existingEvent.Organization and OrganizationLogoUrl are stored separately
                    existingEvent.FightCards = apiEvent.FightCards;

                    UpdateEvent(existingEvent);
                }
            }
        }

        public void Handle(FightEventAddedEvent message)
        {
            // Handle event added notifications if needed
        }
    }

    public class FightEventAddedEvent : IEvent
    {
        public FightEvent FightEvent { get; }

        public FightEventAddedEvent(FightEvent fightEvent)
        {
            FightEvent = fightEvent;
        }
    }
}
