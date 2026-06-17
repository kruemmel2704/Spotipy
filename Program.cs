using dotenv.net;
using Microsoft.AspNetCore.SignalR;
using Spotipy.Hubs;
using Spotipy.Services;

namespace Spotipy
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Load environment variables from .env file if it exists
            var envPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
            if (!File.Exists(envPath))
            {
                envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
            }

            if (File.Exists(envPath))
            {
                Console.WriteLine($"Loading environment from: {envPath}");
                DotEnv.Load(new DotEnvOptions(envFilePaths: new[] { envPath }));
            }
            else
            {
                Console.WriteLine("No .env file found. Credentials must be configured via the Web UI.");
            }

            var builder = WebApplication.CreateBuilder(args);

            // Configure the web server to listen on port 5000 on all interfaces
            // so it can be reached via Raspberry Pi's IP address on local network.
            builder.WebHost.UseUrls("http://0.0.0.0:5000");

            // Add services to the container
            builder.Services.AddControllers();
            builder.Services.AddSignalR();
            
            // Add CORS to allow the kiosk and mobile apps to interact freely
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyHeader()
                          .AllowAnyMethod()
                          .SetIsOriginAllowed(_ => true)
                          .AllowCredentials();
                });
            });

            // Register our Spotify API service as a singleton to preserve state (OAuth tokens)
            builder.Services.AddSingleton<SpotifyApiService>();

            // Register our background Spotify player supervisor
            builder.Services.AddHostedService<SpotifyPlayerService>();

            var app = builder.Build();

            // Setup pipeline
            app.UseCors();
            app.UseDefaultFiles(); // Serves index.html on "/"
            app.UseStaticFiles();

            app.UseRouting();

            app.MapControllers();
            app.MapHub<PlaybackHub>("/playbackHub");

            // Subscribe to State Changed events in SpotifyApiService and push updates to SignalR clients
            var apiService = app.Services.GetRequiredService<SpotifyApiService>();
            var hubContext = app.Services.GetRequiredService<IHubContext<PlaybackHub>>();
            
            apiService.OnStateChanged += (state) =>
            {
                // Push the update to all connected Web UI clients (remote phone & HDMI screen)
                hubContext.Clients.All.SendAsync("ReceivePlaybackState", state);
            };

            // Start a light background polling task to synchronize with the Spotify Web API
            // every 10 seconds if a user is logged in. This ensures playlist context, track volume,
            // and Jam queues are regularly updated.
            Task.Run(async () =>
            {
                var logger = app.Services.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Starting Spotify Web API synchronization loop...");

                while (true)
                {
                    try
                    {
                        if (apiService.IsUserAuthenticated)
                        {
                            var state = apiService.GetCurrentState();
                            // Only poll if the song is actively playing to save API quota
                            if (state.IsPlaying)
                            {
                                await apiService.SyncPlaybackStateWithWebApiAsync();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error in Spotify Web API synchronization loop.");
                    }

                    await Task.Delay(10000); // Poll every 10 seconds
                }
            });

            app.Run();
        }
    }
}
