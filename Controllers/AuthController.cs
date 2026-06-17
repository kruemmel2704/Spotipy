using Microsoft.AspNetCore.Mvc;
using SpotifyAPI.Web;
using Spotipy.Services;
using System.Web;

namespace Spotipy.Controllers
{
    public class ConfigPayload
    {
        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";
    }

    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly SpotifyApiService _spotifyApiService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;

        public AuthController(SpotifyApiService spotifyApiService, IConfiguration configuration, ILogger<AuthController> logger)
        {
            _spotifyApiService = spotifyApiService;
            _configuration = configuration;
            _logger = logger;
        }

        private string? ClientId => Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID");
        private string? ClientSecret => Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_SECRET");
        private string RedirectUri
        {
            get
            {
                var configuredUri = _configuration.GetValue<string>("SpotifySettings:RedirectUri");
                if (!string.IsNullOrEmpty(configuredUri) && configuredUri != "http://localhost:5000/api/auth/callback")
                {
                    return configuredUri;
                }
                return $"{Request.Scheme}://{Request.Host}/api/auth/callback";
            }
        }

        // Initiates the Spotify Login OAuth Flow
        [HttpGet("login")]
        public IActionResult Login()
        {
            if (string.IsNullOrEmpty(ClientId))
            {
                return BadRequest("Spotify Client ID is not configured. Please configure credentials first.");
            }

            _logger.LogInformation($"Initiating Spotify OAuth with Redirect URI: {RedirectUri}");

            var loginRequest = new LoginRequest(
                new Uri(RedirectUri),
                ClientId,
                LoginRequest.ResponseType.Code
            )
            {
                Scope = new[] {
                    Scopes.UserReadPlaybackState,
                    Scopes.UserModifyPlaybackState,
                    Scopes.UserReadCurrentlyPlaying,
                    Scopes.PlaylistReadPrivate,
                    Scopes.PlaylistReadCollaborative
                }
            };

            var uri = loginRequest.ToUri();
            return Redirect(uri.ToString());
        }

        // OAuth Callback Handler
        [HttpGet("callback")]
        public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string? error)
        {
            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogError($"Spotify login error callback: {error}");
                return Redirect("/?error=" + HttpUtility.UrlEncode(error));
            }

            if (string.IsNullOrEmpty(code))
            {
                return BadRequest("Missing OAuth authorization code.");
            }

            try
            {
                _logger.LogInformation("Exchanging OAuth code for Access Tokens...");
                var response = await new OAuthClient().RequestToken(new AuthorizationCodeTokenRequest(
                    ClientId!, ClientSecret!, code, new Uri(RedirectUri)
                ));

                await _spotifyApiService.SaveUserTokensAsync(response.AccessToken, response.RefreshToken, response.ExpiresIn);

                _logger.LogInformation("Authorization completed successfully. Redirecting back to home.");
                return Redirect("/?login=success");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exchanging auth code for tokens.");
                return Redirect("/?error=" + HttpUtility.UrlEncode("Failed to exchange authentication tokens."));
            }
        }

        // Endpoint to configure ClientId and ClientSecret dynamically from Web UI
        [HttpPost("config")]
        public IActionResult SaveConfig([FromBody] ConfigPayload payload)
        {
            if (string.IsNullOrWhiteSpace(payload.ClientId) || string.IsNullOrWhiteSpace(payload.ClientSecret))
            {
                return BadRequest(new { success = false, message = "Client ID and Client Secret cannot be empty." });
            }

            try
            {
                _logger.LogInformation("Saving Spotify Developer credentials to .env file...");
                var envPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
                
                // If BaseDirectory doesn't have it (e.g. running in dev), use current directory
                if (!System.IO.File.Exists(envPath))
                {
                    envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
                }

                var content = $"SPOTIFY_CLIENT_ID={payload.ClientId.Trim()}\nSPOTIFY_CLIENT_SECRET={payload.ClientSecret.Trim()}\n";
                System.IO.File.WriteAllText(envPath, content);

                // Reload environment variables in process memory
                Environment.SetEnvironmentVariable("SPOTIFY_CLIENT_ID", payload.ClientId.Trim());
                Environment.SetEnvironmentVariable("SPOTIFY_CLIENT_SECRET", payload.ClientSecret.Trim());

                _logger.LogInformation("Spotify credentials configured successfully.");
                return Ok(new { success = true, message = "Credentials saved! Ready for login." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save developer credentials to .env.");
                return StatusCode(500, new { success = false, message = "Failed to save configuration: " + ex.Message });
            }
        }
    }
}
