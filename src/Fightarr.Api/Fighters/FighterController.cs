using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Fights;
using Fightarr.Http;

namespace Fightarr.Api.Fighters
{
    [FightarrApiController("fighters")]
    public class FighterController : Controller
    {
        private readonly IFightarrMetadataService _metadataService;

        public FighterController(IFightarrMetadataService metadataService)
        {
            _metadataService = metadataService;
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetFighter(int id)
        {
            try
            {
                var fighter = await _metadataService.GetFighter(id);

                if (fighter == null)
                {
                    return NotFound(new { error = $"Fighter with ID {id} not found" });
                }

                return Ok(fighter.ToResource());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
