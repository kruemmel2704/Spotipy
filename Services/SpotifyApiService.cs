using SpotifyAPI.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Spotipy.Services
{
    public class PlaybackStateModel
    {
        public bool IsPlaying { get; set; }
        public string TrackName { get; set; } = "Bereit für Spotify Connect";
        public string ArtistName { get; set; } = "Warte auf Verbindung...";
        public string AlbumName { get; set; } = "";
        public string CoverUrl { get; set; } = "/img/default_cover.png";
        public int DurationMs { get; set; }
        public int ProgressMs { get; set; }
        public string? TrackId { get; set; }
        public int VolumePercent { get; set; } = 50;
        public bool JamActive { get; set; }
        public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("o");
    }

    public class SpotifyApiService
    {
        private readonly ILogger<SpotifyApiService> _logger;
        private readonly IConfiguration _configuration;
        
        // Tokens for User OAuth Flow (Remote Control)
        private string? _userAccessToken;
        private string? _userRefreshToken;
        private DateTime _userTokenExpiry = DateTime.MinValue;

        // Token for Client Credentials Flow (Fallback metadata viewer)
        private string? _clientCredentialsToken;
        private DateTime _clientCredentialsTokenExpiry = DateTime.MinValue;

        private PlaybackStateModel _currentState = new();

        public event Action<PlaybackStateModel>? OnStateChanged;

        public SpotifyApiService(ILogger<SpotifyApiService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        private string? ClientId => Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID");
        private string? ClientSecret => Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_SECRET");

        public bool IsUserAuthenticated => !string.IsNullOrEmpty(_userRefreshToken);

        public PlaybackStateModel GetCurrentState() => _currentState;

        public void UpdateLocalState(bool isPlaying, string? trackId, int durationMs, int progressMs)
        {
            _currentState.IsPlaying = isPlaying;
            _currentState.DurationMs = durationMs;
            _currentState.ProgressMs = progressMs;
            _currentState.UpdatedAt = DateTime.UtcNow.ToString("o");

            if (!string.IsNullOrEmpty(trackId) && trackId != _currentState.TrackId)
            {
                _currentState.TrackId = trackId;
                // Fetch track details asynchronously
                _ = FetchTrackMetadataAsync(trackId);
            }
            else
            {
                TriggerStateChanged();
            }
        }

        public void UpdateVolume(int volumePercent)
        {
            _currentState.VolumePercent = volumePercent;
            _currentState.UpdatedAt = DateTime.UtcNow.ToString("o");
            TriggerStateChanged();
        }

        private void TriggerStateChanged()
        {
            OnStateChanged?.Invoke(_currentState);
        }

        public async Task SaveUserTokensAsync(string accessToken, string refreshToken, int expiresInSeconds)
        {
            _userAccessToken = accessToken;
            _userRefreshToken = refreshToken;
            _userTokenExpiry = DateTime.UtcNow.AddSeconds(expiresInSeconds);
            _logger.LogInformation("Spotify user OAuth tokens saved successfully.");
            
            // Get current playback status immediately after logging in
            await SyncPlaybackStateWithWebApiAsync();
        }

        // Returns SpotifyClient authorized for user control
        public async Task<SpotifyClient?> GetUserClientAsync()
        {
            if (string.IsNullOrEmpty(ClientId) || string.IsNullOrEmpty(ClientSecret))
            {
                _logger.LogWarning("Spotify ClientId or ClientSecret is not configured in .env!");
                return null;
            }

            if (!IsUserAuthenticated)
            {
                return null;
            }

            if (DateTime.UtcNow >= _userTokenExpiry || string.IsNullOrEmpty(_userAccessToken))
            {
                await RefreshUserTokenAsync();
            }

            if (string.IsNullOrEmpty(_userAccessToken))
            {
                return null;
            }

            return new SpotifyClient(_userAccessToken);
        }

        // Returns SpotifyClient authorized with Client Credentials (metadata only)
        public async Task<SpotifyClient?> GetClientCredentialsClientAsync()
        {
            if (string.IsNullOrEmpty(ClientId) || string.IsNullOrEmpty(ClientSecret))
            {
                _logger.LogWarning("Spotify ClientId or ClientSecret is not configured in .env!");
                return null;
            }

            if (DateTime.UtcNow >= _clientCredentialsTokenExpiry || string.IsNullOrEmpty(_clientCredentialsToken))
            {
                try
                {
                    _logger.LogInformation("Requesting Spotify Client Credentials token...");
                    var config = SpotifyClientConfig.CreateDefault();
                    var request = new ClientCredentialsRequest(ClientId, ClientSecret);
                    var response = await new OAuthClient(config).RequestToken(request);
                    
                    _clientCredentialsToken = response.AccessToken;
                    _clientCredentialsTokenExpiry = DateTime.UtcNow.AddSeconds(response.ExpiresIn - 60);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fetch Client Credentials token.");
                    return null;
                }
            }

            return new SpotifyClient(_clientCredentialsToken);
        }

        private async Task RefreshUserTokenAsync()
        {
            if (string.IsNullOrEmpty(_userRefreshToken)) return;

            try
            {
                _logger.LogInformation("Refreshing user access token...");
                var response = await new OAuthClient().RequestToken(new AuthorizationCodeRefreshRequest(
                    ClientId!, ClientSecret!, _userRefreshToken
                ));

                _userAccessToken = response.AccessToken;
                _userTokenExpiry = DateTime.UtcNow.AddSeconds(response.ExpiresIn - 60);
                if (!string.IsNullOrEmpty(response.RefreshToken))
                {
                    _userRefreshToken = response.RefreshToken;
                }
                _logger.LogInformation("User access token refreshed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh user Spotify access token.");
            }
        }

        // Helper to fetch details about a specific track ID (metadata and cover)
        private async Task FetchTrackMetadataAsync(string trackId)
        {
            try
            {
                // Prefer user client if available, else fallback to Client Credentials
                SpotifyClient? client = await GetUserClientAsync();
                if (client == null)
                {
                    client = await GetClientCredentialsClientAsync();
                }

                if (client == null)
                {
                    _logger.LogWarning("No Spotify client available to fetch track metadata.");
                    return;
                }

                _logger.LogInformation($"Fetching metadata for track: {trackId}");
                var track = await client.Tracks.Get(trackId);
                
                _currentState.TrackName = track.Name;
                _currentState.ArtistName = string.Join(", ", track.Artists.Select(a => a.Name));
                _currentState.AlbumName = track.Album.Name;
                
                if (track.Album.Images.Count > 0)
                {
                    // Get highest resolution image
                    _currentState.CoverUrl = track.Album.Images[0].Url;
                }
                else
                {
                    _currentState.CoverUrl = "/img/default_cover.png";
                }

                _currentState.DurationMs = track.DurationMs;
                _currentState.UpdatedAt = DateTime.UtcNow.ToString("o");
                
                TriggerStateChanged();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to fetch metadata for track ID: {trackId}");
            }
        }

        // Periodically sync general status from Spotify Web API (especially for Jam status/remote edits)
        public async Task SyncPlaybackStateWithWebApiAsync()
        {
            var client = await GetUserClientAsync();
            if (client == null) return;

            try
            {
                var playback = await client.Player.GetCurrentPlayback(new PlayerCurrentPlaybackRequest());
                if (playback != null && playback.Item != null)
                {
                    _currentState.IsPlaying = playback.IsPlaying;
                    _currentState.ProgressMs = playback.ProgressMs;
                    _currentState.VolumePercent = playback.Device?.VolumePercent ?? _currentState.VolumePercent;
                    
                    // A collaborative playlist or a profile/active context could suggest collaborative listening/Jam.
                    // Since direct Jam participant API is private, we can detect if context is collaborative, 
                    // or if it's playing in a shared session context.
                    _currentState.JamActive = playback.Context != null && 
                                               (playback.Context.Type == "playlist" || playback.Context.Type == "collection");

                    if (playback.Item is FullTrack track)
                    {
                        _currentState.TrackId = track.Id;
                        _currentState.TrackName = track.Name;
                        _currentState.ArtistName = string.Join(", ", track.Artists.Select(a => a.Name));
                        _currentState.AlbumName = track.Album.Name;
                        _currentState.DurationMs = track.DurationMs;
                        if (track.Album.Images.Count > 0)
                        {
                            _currentState.CoverUrl = track.Album.Images[0].Url;
                        }
                    }
                    
                    _currentState.UpdatedAt = DateTime.UtcNow.ToString("o");
                    TriggerStateChanged();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync playback state with Spotify Web API.");
            }
        }

        // Send control commands to Spotify Player
        public async Task<bool> SendControlCommandAsync(string command, int? value = null)
        {
            var client = await GetUserClientAsync();
            if (client == null)
            {
                _logger.LogWarning("Cannot send control command. User is not authenticated.");
                return false;
            }

            try
            {
                // Find our target device first
                var devices = await client.Player.GetAvailableDevices();
                var targetDeviceName = _configuration.GetValue<string>("LibrespotSettings:DeviceName") ?? "Spotipy Connect";
                var targetDevice = devices.Devices.FirstOrDefault(d => d.Name.Equals(targetDeviceName, StringComparison.OrdinalIgnoreCase));
                
                string? deviceId = targetDevice?.Id;

                // If device found but not active, we transfer playback to it (except if user is trying to pause/stop)
                if (targetDevice != null && !targetDevice.IsActive && command != "pause")
                {
                    _logger.LogInformation($"Transferring playback to target device: {targetDevice.Name} ({targetDevice.Id})");
                    await client.Player.TransferPlayback(new PlayerTransferPlaybackRequest(new List<string> { targetDevice.Id }) { Play = true });
                    // Wait a moment for transfer to complete
                    await Task.Delay(500);
                }

                switch (command.ToLower())
                {
                    case "play":
                        await client.Player.ResumePlayback(new PlayerResumePlaybackRequest { DeviceId = deviceId });
                        _currentState.IsPlaying = true;
                        break;
                    case "pause":
                        await client.Player.PausePlayback(new PlayerPausePlaybackRequest { DeviceId = deviceId });
                        _currentState.IsPlaying = false;
                        break;
                    case "next":
                        await client.Player.SkipNext(new PlayerSkipNextRequest { DeviceId = deviceId });
                        break;
                    case "previous":
                        await client.Player.SkipPrevious(new PlayerSkipPreviousRequest { DeviceId = deviceId });
                        break;
                    case "volume":
                        if (value.HasValue)
                        {
                            await client.Player.SetVolume(new PlayerVolumeRequest(value.Value) { DeviceId = deviceId });
                            _currentState.VolumePercent = value.Value;
                        }
                        break;
                    case "seek":
                        if (value.HasValue)
                        {
                            await client.Player.SeekTo(new PlayerSeekToRequest((long)value.Value) { DeviceId = deviceId });
                            _currentState.ProgressMs = value.Value;
                        }
                        break;
                    default:
                        _logger.LogWarning($"Unknown control command: {command}");
                        return false;
                }

                _currentState.UpdatedAt = DateTime.UtcNow.ToString("o");
                TriggerStateChanged();
                
                // Trigger an immediate background sync to fetch updated queue metadata
                _ = Task.Run(async () => {
                    await Task.Delay(800);
                    await SyncPlaybackStateWithWebApiAsync();
                });

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to execute player command: {command}");
                return false;
            }
        }
    }
}
