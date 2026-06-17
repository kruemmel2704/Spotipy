using Microsoft.AspNetCore.Mvc;
using Spotipy.Services;

namespace Spotipy.Controllers
{
    public class EventPayload
    {
        public string Event { get; set; } = "";
        public string? TrackId { get; set; }
        public int PositionMs { get; set; }
        public int DurationMs { get; set; }
    }

    public class ControlPayload
    {
        public string Command { get; set; } = "";
        public int? Value { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class PlayerController : ControllerBase
    {
        private readonly SpotifyApiService _spotifyApiService;
        private readonly ILogger<PlayerController> _logger;

        public PlayerController(SpotifyApiService spotifyApiService, ILogger<PlayerController> logger)
        {
            _spotifyApiService = spotifyApiService;
            _logger = logger;
        }

        // Webhook endpoint called by librespot's --onevent script
        [HttpPost("event")]
        public IActionResult ReceiveEvent([FromBody] EventPayload payload)
        {
            _logger.LogInformation($"Received local librespot event: {payload.Event}, Track: {payload.TrackId}, Position: {payload.PositionMs}ms");

            bool isPlaying = payload.Event.ToLower() switch
            {
                "play" or "playing" or "change" => true,
                "pause" or "paused" or "stop" or "stopped" => false,
                _ => _spotifyApiService.GetCurrentState().IsPlaying
            };

            // If it's a volume change event, update the volume locally
            if (payload.Event.ToLower() == "volume_change" && payload.PositionMs > 0)
            {
                // In some versions of librespot, volume is reported in the position variable (0-65536)
                // Let's normalize it to 0-100 percent
                int volumePercent = (int)Math.Round((payload.PositionMs / 65536.0) * 100.0);
                _spotifyApiService.UpdateVolume(volumePercent);
            }
            else
            {
                _spotifyApiService.UpdateLocalState(isPlaying, payload.TrackId, payload.DurationMs, payload.PositionMs);
            }

            return Ok(new { success = true });
        }

        // Endpoint called by remote control Web UI to trigger actions
        [HttpPost("control")]
        public async Task<IActionResult> ExecuteControl([FromBody] ControlPayload payload)
        {
            if (string.IsNullOrEmpty(payload.Command))
            {
                return BadRequest("Command is required.");
            }

            _logger.LogInformation($"Remote control request: Command = {payload.Command}, Value = {payload.Value}");
            
            bool success = await _spotifyApiService.SendControlCommandAsync(payload.Command, payload.Value);
            if (!success)
            {
                return BadRequest(new { success = false, message = "Failed to execute player command. Make sure you are logged in via Spotify." });
            }

            return Ok(new { success = true });
        }

        // Endpoint to fetch current status immediately
        [HttpGet("state")]
        public ActionResult<PlaybackStateModel> GetState()
        {
            return Ok(_spotifyApiService.GetCurrentState());
        }

        // Force a synchronization with Spotify's Web API
        [HttpPost("sync")]
        public async Task<IActionResult> ForceSync()
        {
            if (!_spotifyApiService.IsUserAuthenticated)
            {
                return BadRequest("User is not authenticated with Spotify.");
            }

            await _spotifyApiService.SyncPlaybackStateWithWebApiAsync();
            return Ok(new { success = true });
        }

        // Fetch host LAN IP configuration for remote sharing
        [HttpGet("host-info")]
        public IActionResult GetHostInfo()
        {
            try
            {
                var ipList = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Where(i => i.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                    .SelectMany(i => i.GetIPProperties().UnicastAddresses)
                    .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a.Address))
                    .Select(a => a.Address.ToString())
                    .ToList();
                
                var ip = ipList.FirstOrDefault() ?? "localhost";
                return Ok(new { lanUrl = $"http://{ip}:5000" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to determine local network IP.");
                return Ok(new { lanUrl = "http://localhost:5000" });
            }
        }
    }
}
