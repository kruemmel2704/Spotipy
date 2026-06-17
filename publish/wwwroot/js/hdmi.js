// HDMI Kiosk Display Script

document.addEventListener("DOMContentLoaded", () => {
    // UI Elements
    const ambientBg = document.getElementById("ambient-bg");
    const coverGlow = document.getElementById("cover-glow");
    const albumCover = document.getElementById("album-cover");
    
    const trackTitle = document.getElementById("track-title");
    const trackArtist = document.getElementById("track-artist");
    const trackAlbum = document.getElementById("track-album");
    const jamIndicator = document.getElementById("jam-indicator");
    
    const progressFill = document.getElementById("progress-fill");
    const timeCurrent = document.getElementById("time-current");
    const timeDuration = document.getElementById("time-duration");
    
    const shareUrl = document.getElementById("share-url");
    const shareQrImage = document.getElementById("share-qr-image");

    // Local state
    let isPlaying = false;
    let progressMs = 0;
    let durationMs = 0;
    let progressTimer = null;
    let currentCoverUrl = "";

    // Fetch local network IP for guest access
    fetchHostInfo();

    // Initialize SignalR connection
    initializeSignalR(onReceiveState);

    // Fetch LAN address for sharing
    async function fetchHostInfo() {
        try {
            const response = await fetch("/api/player/host-info");
            const data = await response.json();
            if (response.ok && data.lanUrl) {
                shareUrl.textContent = data.lanUrl;
                shareQrImage.src = `https://api.qrserver.com/v1/create-qr-code/?size=100x100&data=${encodeURIComponent(data.lanUrl)}`;
            } else {
                fallbackShareInfo();
            }
        } catch (ex) {
            console.error("Failed to fetch host IP, using fallback.", ex);
            fallbackShareInfo();
        }
    }

    function fallbackShareInfo() {
        const fallbackUrl = window.location.origin;
        shareUrl.textContent = fallbackUrl;
        shareQrImage.src = `https://api.qrserver.com/v1/create-qr-code/?size=100x100&data=${encodeURIComponent(fallbackUrl)}`;
    }

    // SignalR Callback: State updates
    function onReceiveState(state) {
        isPlaying = state.isPlaying;
        durationMs = state.durationMs;
        progressMs = state.progressMs;

        // Visual updates
        trackTitle.textContent = state.trackName;
        trackArtist.textContent = state.artistName;
        
        if (state.albumName) {
            trackAlbum.textContent = `—  ${state.albumName}`;
        } else {
            trackAlbum.textContent = "";
        }

        // Crossfade cover art if it has changed
        if (state.coverUrl && state.coverUrl !== currentCoverUrl) {
            currentCoverUrl = state.coverUrl;
            
            // Add subtle fade animation
            albumCover.style.opacity = "0.3";
            setTimeout(() => {
                albumCover.src = state.coverUrl;
                ambientBg.style.backgroundImage = `url('${state.coverUrl}')`;
                coverGlow.style.backgroundImage = `url('${state.coverUrl}')`;
                albumCover.style.opacity = "1";
            }, 300);
        }

        // Jam Indicator
        jamIndicator.style.display = state.jamActive ? "inline-flex" : "none";

        // Progress bar updating
        updateProgressUi();

        // Control local timeline ticker
        if (isPlaying) {
            startProgressTimer();
        } else {
            stopProgressTimer();
        }
    }

    // Local Progress Ticking for smooth transitions
    function startProgressTimer() {
        stopProgressTimer();
        progressTimer = setInterval(() => {
            if (isPlaying && progressMs < durationMs) {
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
            const percentage = (progressMs / durationMs) * 100;
            progressFill.style.width = `${Math.min(100, percentage)}%`;
        } else {
            progressFill.style.width = "0%";
        }
    }
});
