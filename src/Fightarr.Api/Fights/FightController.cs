using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Fights;
using Fightarr.Http;
using System.Linq;

namespace Fightarr.Api.Fights
{
    [FightarrApiController("fights")]
    public class FightController : Controller
    {
        private readonly IFightarrMetadataService _metadataService;

        public FightController(IFightarrMetadataService metadataService)
        {
            _metadataService = metadataService;
        }

        [HttpGet("event/{eventId:int}")]
        public async Task<IActionResult> GetFightsByEvent(int eventId)
        {
            try
            {
                var fightEvent = await _metadataService.GetEvent(eventId);

                if (fightEvent == null)
                {
                    return NotFound(new { error = $"Event with ID {eventId} not found" });
                }

                // Extract all fights from all fight cards
                var allFights = fightEvent.FightCards
                    .SelectMany(card => card.Fights)
                    .OrderBy(f => f.FightOrder)
                    .ToList();

                return Ok(allFights);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("card/{eventId:int}/{cardNumber:int}")]
        public async Task<IActionResult> GetFightsByCard(int eventId, int cardNumber)
        {
            try
            {
                if (cardNumber < 1 || cardNumber > 3)
                {
                    return BadRequest(new { error = "Card number must be 1 (Early Prelims), 2 (Prelims), or 3 (Main Card)" });
                }

                var fightEvent = await _metadataService.GetEvent(eventId);

                if (fightEvent == null)
                {
                    return NotFound(new { error = $"Event with ID {eventId} not found" });
                }

                var fightCard = fightEvent.FightCards.FirstOrDefault(c => c.CardNumber == cardNumber);

                if (fightCard == null)
                {
                    return NotFound(new { error = $"Card {cardNumber} not found for event {eventId}" });
                }

                return Ok(fightCard.Fights);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("upcoming")]
        public async Task<IActionResult> GetUpcomingFights([FromQuery] string organization = null)
        {
            try
            {
                var events = await _metadataService.GetUpcomingEvents(organization);

                // Extract all fights from upcoming events
                var upcomingFights = events
                    .SelectMany(e => e.FightCards.SelectMany(card => card.Fights.Select(fight => new
                    {
                        Fight = fight,
                        Event = new
                        {
                            e.Id,
                            e.Title,
                            e.EventNumber,
                            e.EventDate,
                            e.Location,
                            e.Venue,
                            e.Organization
                        },
                        CardSection = card.CardSection
                    })))
                    .OrderBy(f => f.Event.EventDate)
                    .ThenBy(f => f.Fight.FightOrder)
                    .ToList();

                return Ok(upcomingFights);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
