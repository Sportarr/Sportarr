using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Fights;
using Fightarr.Http;

namespace Fightarr.Api.Organizations
{
    [V3ApiController("organizations")]
    public class OrganizationController : Controller
    {
        private readonly IFightarrMetadataService _metadataService;

        public OrganizationController(IFightarrMetadataService metadataService)
        {
            _metadataService = metadataService;
        }

        [HttpGet("{slug}/events")]
        public async Task<IActionResult> GetOrganizationEvents(string slug, [FromQuery] bool upcoming = false)
        {
            try
            {
                var events = await _metadataService.GetUpcomingEvents(slug);

                if (!upcoming)
                {
                    // If not filtering for upcoming only, we might need to add logic to get all events
                    // For now, return upcoming events as default
                }

                return Ok(events);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
