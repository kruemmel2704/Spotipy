// Remote Controller Script

document.addEventListener("DOMContentLoaded", () => {
    // UI Elements
    const statusDot = document.getElementById("status-dot");
    const statusText = document.getElementById("status-text");
    const authBanner = document.getElementById("auth-banner");
    const btnLogin = document.getElementById("btn-login");
    const btnQr = document.getElementById("btn-qr");
    const btnConfig = document.getElementById("btn-config");
    
    // Player Elements
    const albumCover = document.getElementById("album-cover");
    const coverGlowBg = document.getElementById("cover-glow-bg");
    const trackTitle = document.getElementById("track-title");
    const trackArtist = document.getElementById("track-artist");
    const trackAlbum = document.getElementById("track-album");
    const jamIndicator = document.getElementById("jam-indicator");
    
    // Progress
    const timeCurrent = document.getElementById("time-current");
    const timeDuration = document.getElementById("time-duration");
    const progressSlider = document.getElementById("progress-slider");
    
    // Controls
    const btnPrev = document.getElementById("btn-prev");
    const btnPlay = document.getElementById("btn-play");
    const playIcon = document.getElementById("play-icon");
    const pauseIcon = document.getElementById("pause-icon");
    const btnNext = document.getElementById("btn-next");
    
    // Volume
    const btnMute = document.getElementById("btn-mute");
    const volumeIconHigh = document.getElementById("volume-icon-high");
    const volumeIconMute = document.getElementById("volume-icon-mute");
    const volumeSlider = document.getElementById("volume-slider");
    
    // Modals
    const modalConfig = document.getElementById("modal-config");
    const btnConfigClose = document.getElementById("btn-config-close");
    const btnConfigSave = document.getElementById("btn-config-save");
    const cfgClientId = document.getElementById("cfg-client-id");
    const cfgClientSecret = document.getElementById("cfg-client-secret");
    
    const modalQr = document.getElementById("modal-qr");
    const btnQrClose = document.getElementById("btn-qr-close");
    const qrImage = document.getElementById("qr-image");
    const qrUrl = document.getElementById("qr-url");

    // Local Player State
    let isPlaying = false;
    let progressMs = 0;
    let durationMs = 0;
    let volumePercent = 50;
    let preMuteVolume = 50;
    let progressTimer = null;
    let isSeeking = false;
    let isSettingVolume = false;

    // Check query params for alerts
    const urlParams = new URLSearchParams(window.location.search);
    if (urlParams.get("login") === "success") {
        alert("Erfolgreich mit Spotify verbunden!");
        // Clear query parameters
        window.history.replaceState({}, document.title, window.location.pathname);
    } else if (urlParams.get("error")) {
        alert("Fehler bei der Verbindung: " + urlParams.get("error"));
        window.history.replaceState({}, document.title, window.location.pathname);
    }

    // Initialize SignalR
    initializeSignalR(onReceiveState, onReceiveAuthStatus);

    // SignalR Callback: State updates
    function onReceiveState(state) {
        isPlaying = state.isPlaying;
        durationMs = state.durationMs;
        
        // Sync progress if user isn't currently dragging the slider
        if (!isSeeking) {
            progressMs = state.progressMs;
            updateProgressUi();
        }

        // Sync volume if user isn't currently adjusting it
        if (!isSettingVolume) {
            volumePercent = state.volumePercent;
            volumeSlider.value = volumePercent;
            updateVolumeIcon();
        }

        // Metadata updates
        trackTitle.textContent = state.trackName;
        trackArtist.textContent = state.artistName;
        trackAlbum.textContent = state.albumName;
        
        // Update images
        albumCover.src = state.coverUrl;
        coverGlowBg.style.backgroundImage = `url('${state.coverUrl}')`;

        // Update Play/Pause Icons
        if (isPlaying) {
            playIcon.style.display = "none";
            pauseIcon.style.display = "block";
            startProgressTimer();
        } else {
            playIcon.style.display = "block";
            pauseIcon.style.display = "none";
            stopProgressTimer();
        }

        // Jam session indicator
        jamIndicator.style.display = state.jamActive ? "inline-flex" : "none";

        // Update server connection indicators
        statusDot.className = "status-dot connected";
        statusText.textContent = "Verbunden";
    }

    // SignalR Callback: Auth status update
    function onReceiveAuthStatus(isAuthenticated) {
        if (isAuthenticated) {
            authBanner.style.display = "none";
        } else {
            authBanner.style.display = "flex";
        }
    }

    // Local Progress Ticking
    function startProgressTimer() {
        stopProgressTimer();
        progressTimer = setInterval(() => {
            if (!isSeeking && isPlaying && progressMs < durationMs) {
                progressMs += 1000;
                updateProgressUi();
            }
        }, 1000);
    }

    function stopProgressTimer() {
        if (progressTimer) {
            clearInterval(progressTimer);
            progressTimer = null;
        }
    }

    function updateProgressUi() {
        timeCurrent.textContent = formatTime(progressMs);
        timeDuration.textContent = formatTime(durationMs);
        
        if (durationMs > 0) {
            progressSlider.max = durationMs;
            progressSlider.value = progressMs;
        } else {
            progressSlider.max = 100;
            progressSlider.value = 0;
        }
    }

    function updateVolumeIcon() {
        if (volumePercent === 0) {
            volumeIconHigh.style.display = "none";
            volumeIconMute.style.display = "block";
        } else {
            volumeIconHigh.style.display = "block";
            volumeIconMute.style.display = "none";
        }
    }

    // Control WebAPI triggers
    async function sendControl(command, value = null) {
        try {
            const response = await fetch("/api/player/control", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ command, value })
            });
            const data = await response.json();
            if (!response.ok) {
                console.error("Control error: ", data.message);
                if (data.message && data.message.includes("logged in")) {
                    authBanner.style.display = "flex";
                }
            }
        } catch (ex) {
            console.error("Network error sending control: ", ex);
        }
    }

    // Event Handlers: Playback Control
    btnPlay.addEventListener("click", () => {
        const command = isPlaying ? "pause" : "play";
        sendControl(command);
    });

    btnPrev.addEventListener("click", () => sendControl("previous"));
    btnNext.addEventListener("click", () => sendControl("next"));

    // Seek input handlers
    progressSlider.addEventListener("mousedown", () => { isSeeking = true; });
    progressSlider.addEventListener("touchstart", () => { isSeeking = true; });
    
    progressSlider.addEventListener("input", (e) => {
        progressMs = parseInt(e.target.value);
        timeCurrent.textContent = formatTime(progressMs);
    });
    
    progressSlider.addEventListener("change", (e) => {
        isSeeking = false;
        const targetMs = parseInt(e.target.value);
        sendControl("seek", targetMs);
    });

    // Volume input handlers
    volumeSlider.addEventListener("mousedown", () => { isSettingVolume = true; });
    volumeSlider.addEventListener("touchstart", () => { isSettingVolume = true; });
    
    volumeSlider.addEventListener("input", (e) => {
        volumePercent = parseInt(e.target.value);
        updateVolumeIcon();
    });

    volumeSlider.addEventListener("change", (e) => {
        isSettingVolume = false;
        const vol = parseInt(e.target.value);
        sendControl("volume", vol);
    });

    btnMute.addEventListener("click", () => {
        if (volumePercent > 0) {
            preMuteVolume = volumePercent;
            volumePercent = 0;
            volumeSlider.value = 0;
            sendControl("volume", 0);
        } else {
            volumePercent = preMuteVolume;
            volumeSlider.value = preMuteVolume;
            sendControl("volume", preMuteVolume);
        }
        updateVolumeIcon();
    });

    // OAuth Login trigger
    btnLogin.addEventListener("click", () => {
        window.location.href = "/api/auth/login";
    });

    // Config Modal
    btnConfig.addEventListener("click", () => {
        modalConfig.classList.add("show");
    });

    btnConfigClose.addEventListener("click", () => {
        modalConfig.classList.remove("show");
    });

    btnConfigSave.addEventListener("click", async () => {
        const clientId = cfgClientId.value.trim();
        const clientSecret = cfgClientSecret.value.trim();

        if (!clientId || !clientSecret) {
            alert("Bitte gib sowohl die Client ID als auch das Client Secret ein.");
            return;
        }

        try {
            const response = await fetch("/api/auth/config", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ clientId, clientSecret })
            });

            const data = await response.json();
            if (response.ok) {
                alert(data.message);
                modalConfig.classList.remove("show");
                // Trigger OAuth redirection automatically since credentials are saved
                window.location.href = "/api/auth/login";
            } else {
                alert("Fehler beim Speichern: " + data.message);
            }
        } catch (ex) {
            alert("Netzwerkfehler beim Speichern der Konfiguration.");
        }
    });

    // QR Sharing Modal
    btnQr.addEventListener("click", () => {
        const rawUrl = window.location.href.split('?')[0]; // Remove query params
        // Replace localhost with the window host (which could be the Pi's IP address on the network)
        const currentUrl = rawUrl;
        
        // Generate QR Code URL from free qrserver API
        qrImage.src = `https://api.qrserver.com/v1/create-qr-code/?size=180x180&data=${encodeURIComponent(currentUrl)}`;
        qrUrl.textContent = currentUrl;
        
        modalQr.classList.add("show");
    });

    btnQrClose.addEventListener("click", () => {
        modalQr.classList.remove("show");
    });

    // Click outside modal to close
    window.addEventListener("click", (e) => {
        if (e.target === modalConfig) modalConfig.classList.remove("show");
        if (e.target === modalQr) modalQr.classList.remove("show");
    });
});
