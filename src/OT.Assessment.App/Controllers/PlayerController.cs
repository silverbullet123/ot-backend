using Microsoft.AspNetCore.Mvc;
using OT.Assessment.Application.Services;
using OT.Assessment.Core.Dtos;
using OT.Assessment.Infrastructure.Messaging;

namespace OT.Assessment.App.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PlayerController : ControllerBase
    {
        private readonly WagerService _wagerService;
        private readonly RabbitMqPublisher _publisher;
        private readonly ILogger<PlayerController> _logger;

        public PlayerController(
            WagerService wagerService,
            RabbitMqPublisher publisher,
            ILogger<PlayerController> logger)
        {
            _wagerService = wagerService;
            _publisher = publisher;
            _logger = logger;
        }

        /// <summary>
        /// Submit a new Casino Wager. 
        /// Publishes to RabbitMQ for ingestion by the Consumer.
        /// </summary>
        [HttpPost("casinoWager")]
        public async Task<IActionResult> PostCasinoWager([FromBody] CasinoWagerDto dto)
        {
            if (dto == null)
                return BadRequest("Invalid payload");

            try
            {
                await _publisher.PublishAsync("ot.casino.wager", dto);
                return Accepted(new { message = "Wager published for processing" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish wager");
                return StatusCode(500, "Error publishing wager");
            }
        }

        /// <summary>
        /// Get paged wagers for a specific player.
        /// </summary>
        [HttpGet("{playerId:guid}/casino")]
        public async Task<IActionResult> GetCasinoWagers(Guid playerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                var result = await _wagerService.GetPlayerWagersAsync(playerId, page, pageSize);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving wagers for player {PlayerId}", playerId);
                return StatusCode(500, "Error retrieving wagers");
            }
        }

        /// <summary>
        /// Get top N spenders.
        /// </summary>
        [HttpGet("topSpenders")]
        public async Task<IActionResult> GetTopSpenders([FromQuery] int count = 10)
        {
            try
            {
                var result = await _wagerService.GetTopSpendersAsync(count);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving top spenders");
                return StatusCode(500, "Error retrieving top spenders");
            }
        }
    }
}
