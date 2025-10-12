using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Fights;
using Fightarr.Http;
using System.Linq;

namespace Fightarr.Api.Events
{
    [FightarrApiController("events")]
    public class EventController : Controller
    {
        private readonly IFightEventService _eventService;
        private readonly IFightarrMetadataService _metadataService;

        public EventController(IFightEventService eventService, IFightarrMetadataService metadataService)
        {
            _eventService = eventService;
            _metadataService = metadataService;
        }

        [HttpGet]
        public IActionResult GetEvents([FromQuery] string organization = null, [FromQuery] bool upcoming = false, [FromQuery] string search = null)
        {
            try
            {
                List<FightEvent> events;

                if (!string.IsNullOrWhiteSpace(search))
                {
                    events = _eventService.SearchEvents(search);
                }
                else if (!string.IsNullOrWhiteSpace(organization))
                {
                    events = _eventService.GetEventsByOrganization(organization);
                }
                else if (upcoming)
                {
                    events = _eventService.GetUpcomingEvents();
                }
                else
                {
                    events = _eventService.GetAllEvents();
                }

                return Ok(events.ToResource());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{id:int}")]
        public IActionResult GetEvent(int id)
        {
            try
            {
                var fightEvent = _eventService.GetEvent(id);

                if (fightEvent == null)
                {
                    return NotFound(new { error = $"Event with ID {id} not found" });
                }

                return Ok(fightEvent.ToResource());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("sync")]
        public async Task<IActionResult> SyncEvents()
        {
            try
            {
                await _eventService.SyncWithFightarrApi();
                return Ok(new { message = "Events synced successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPut("{id:int}")]
        public IActionResult UpdateEvent(int id, [FromBody] EventUpdateResource update)
        {
            try
            {
                var fightEvent = _eventService.GetEvent(id);

                if (fightEvent == null)
                {
                    return NotFound(new { error = $"Event with ID {id} not found" });
                }

                if (update.Monitored.HasValue)
                {
                    fightEvent.Monitored = update.Monitored.Value;
                }

                var updated = _eventService.UpdateEvent(fightEvent);
                return Ok(updated.ToResource());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class EventUpdateResource
    {
        public bool? Monitored { get; set; }
    }
}
