using Microsoft.AspNetCore.SignalR;
using Spotipy.Services;

namespace Spotipy.Hubs
{
    public class PlaybackHub : Hub
    {
        private readonly SpotifyApiService _spotifyApiService;

        public PlaybackHub(SpotifyApiService spotifyApiService)
        {
            _spotifyApiService = spotifyApiService;
        }

        public override async Task OnConnectedAsync()
        {
            // Send the current player state to the newly connected client immediately
            var currentState = _spotifyApiService.GetCurrentState();
            await Clients.Caller.SendAsync("ReceivePlaybackState", currentState);
            
            // Send connection status (is user authenticated with Spotify?)
            await Clients.Caller.SendAsync("ReceiveAuthStatus", _spotifyApiService.IsUserAuthenticated);

            await base.OnConnectedAsync();
        }
    }
}
