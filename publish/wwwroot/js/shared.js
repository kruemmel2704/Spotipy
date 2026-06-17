// Common helpers for Spotipy Client Frontend

// Format milliseconds to MM:SS string
function formatTime(ms) {
    if (isNaN(ms) || ms < 0) return "00:00";
    const totalSeconds = Math.floor(ms / 1000);
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    return `${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
}

// Establishes a global SignalR connection to the PlaybackHub
function initializeSignalR(stateCallback, authCallback) {
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/playbackHub")
        .withAutomaticReconnect({
            nextRetryDelayInMilliseconds: retryContext => {
                // Reconnect every 3 seconds if disconnected
                return 3000;
            }
        })
        .build();

    connection.on("ReceivePlaybackState", (state) => {
        if (stateCallback) stateCallback(state);
    });

    connection.on("ReceiveAuthStatus", (isAuthenticated) => {
        if (authCallback) authCallback(isAuthenticated);
    });

    connection.onclose(() => {
        console.warn("SignalR connection closed. Attempting reconnect...");
    });

    connection.start()
        .then(() => {
            console.log("SignalR connected successfully.");
        })
        .catch(err => {
            console.error("Error establishing SignalR connection:", err);
            // Retry after 5s
            setTimeout(() => initializeSignalR(stateCallback, authCallback), 5000);
        });

    return connection;
}
