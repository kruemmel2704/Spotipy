using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Spotipy.Services
{
    public class SpotifyPlayerService : BackgroundService
    {
        private readonly ILogger<SpotifyPlayerService> _logger;
        private readonly IConfiguration _configuration;
        private Process? _librespotProcess;
        private string? _scriptPath;

        public SpotifyPlayerService(ILogger<SpotifyPlayerService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var enabled = _configuration.GetValue<bool>("LibrespotSettings:Enabled");
            if (!enabled)
            {
                _logger.LogInformation("librespot service is disabled in appsettings.json.");
                return;
            }

            try
            {
                CreateEventScript();
                
                while (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Starting librespot process...");
                    
                    try
                    {
                        StartLibrespot();
                        
                        // Wait for process exit or cancellation
                        while (_librespotProcess != null && !_librespotProcess.HasExited && !stoppingToken.IsCancellationRequested)
                        {
                            await Task.Delay(1000, stoppingToken);
                        }

                        if (_librespotProcess != null && _librespotProcess.HasExited)
                        {
                            _logger.LogWarning($"librespot process exited with code {_librespotProcess.ExitCode}. Restarting in 5 seconds...");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to run librespot process. Retrying in 5 seconds...");
                    }

                    if (!stoppingToken.IsCancellationRequested)
                    {
                        await Task.Delay(5000, stoppingToken);
                    }
                }
            }
            finally
            {
                StopLibrespot();
                CleanupScript();
            }
        }

        private void CreateEventScript()
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            _scriptPath = Path.Combine(appDir, isWindows ? "spotify-event.bat" : "spotify-event.sh");

            _logger.LogInformation($"Creating event webhook script at: {_scriptPath}");

            if (isWindows)
            {
                // Simple batch file for Windows environment testing (if librespot-c or similar is run)
                var content = @"@echo off
curl -s -X POST -H ""Content-Type: application/json"" -d ""{\""event\"":\""%PLAYER_EVENT%\"",\""trackId\"":\""%TRACK_ID%\"",\""positionMs\"":\""%POSITION_MS%\"",\""durationMs\"":\""%DURATION_MS%\""}"" http://localhost:5000/api/player/event
";
                File.WriteAllText(_scriptPath, content);
            }
            else
            {
                // Shell script for Raspberry Pi (Linux)
                var content = @"#!/bin/sh
curl -s -X POST -H ""Content-Type: application/json"" \
     -d ""{\""event\"":\""$PLAYER_EVENT\"",\""trackId\"":\""$TRACK_ID\"",\""positionMs\"":\""$POSITION_MS\"",\""durationMs\"":\""$DURATION_MS\""}"" \
     http://localhost:5000/api/player/event
";
                File.WriteAllText(_scriptPath, content);
                
                // Set executable permission
                try
                {
                    var chmod = Process.Start(new ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"+x \"{_scriptPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    chmod?.WaitForExit();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not set executable permission on event script using chmod.");
                }
            }
        }

        private void StartLibrespot()
        {
            var path = _configuration.GetValue<string>("LibrespotSettings:Path") ?? "librespot";
            var name = _configuration.GetValue<string>("LibrespotSettings:DeviceName") ?? "Spotipy Connect";
            var device = _configuration.GetValue<string>("LibrespotSettings:AudioDevice") ?? "hw:Headphones";
            var bitrate = _configuration.GetValue<string>("LibrespotSettings:Bitrate") ?? "320";
            var extraArgsSetting = _configuration.GetValue<string>("LibrespotSettings:ExtraArgs") ?? "";

            // Construct standard arguments for librespot
            // --name: Spotify Connect device name
            // --backend: alsa (Linux standard)
            // --device: headphone jack or specified audio card
            // --bitrate: 320 for Premium quality
            // --onevent: hook script for track/state updates
            var args = $"--name \"{name}\" --backend alsa --device \"{device}\" --bitrate {bitrate} --onevent \"{_scriptPath}\" {extraArgsSetting}";

            _logger.LogInformation($"Launching: {path} {args}");

            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _librespotProcess = new Process { StartInfo = psi };
            
            // Log outputs as Info/Warning to server console
            _librespotProcess.OutputDataReceived += (s, e) => { if (e.Data != null) _logger.LogInformation($"[librespot] {e.Data}"); };
            _librespotProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) _logger.LogWarning($"[librespot-err] {e.Data}"); };

            _librespotProcess.Start();
            _librespotProcess.BeginOutputReadLine();
            _librespotProcess.BeginErrorReadLine();
        }

        private void StopLibrespot()
        {
            if (_librespotProcess != null)
            {
                try
                {
                    if (!_librespotProcess.HasExited)
                    {
                        _logger.LogInformation("Stopping librespot daemon...");
                        _librespotProcess.Kill();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping librespot process.");
                }
                finally
                {
                    _librespotProcess.Dispose();
                    _librespotProcess = null;
                }
            }
        }

        private void CleanupScript()
        {
            if (!string.IsNullOrEmpty(_scriptPath) && File.Exists(_scriptPath))
            {
                try
                {
                    File.Delete(_scriptPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete temporary event script.");
                }
            }
        }
    }
}
