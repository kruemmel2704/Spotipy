#!/usr/bin/env bash

# Spotipy Installation Script for Raspberry Pi 4B
# Run with: sudo bash install.sh

set -e

# Make sure the script is run with sudo
if [ "$EUID" -ne 0 ]; then
  echo "Bitte starte das Skript mit Root-Rechten: sudo bash install.sh"
  exit 1
fi

REAL_USER=${SUDO_USER:-$USER}
USER_HOME=$(getent passwd "$REAL_USER" | cut -d: -f6)
APP_DIR=$(pwd)

echo "===================================================="
echo " Starting Spotipy Connect Client Installation..."
echo " Running directory: $APP_DIR"
echo " Target User: $REAL_USER (Home: $USER_HOME)"
echo "===================================================="

# 1. Update package lists
echo "1. Aktualisiere Paketquellen..."
apt-get update

# 2. Install ALSA utilities & dependencies for audio
echo "2. Installiere Audio-Abhängigkeiten (ALSA)..."
apt-get install -y alsa-utils libasound2 libasound2-dev curl wget tar

# 3. Force audio jack output (analog out) on Pi 4B
echo "3. Konfiguriere Audio-Ausgang auf Klinkenbuchse (AudioJack)..."
# Set headphones jack (numid=3 is headphone output on classic Pi models)
# On modern Pi OS Bookworm with Pipewire, this is fallback but useful.
amixer cset numid=3 1 || true

# 4. Install ASP.NET Core 8 Runtime
echo "4. Installiere .NET 8 ASP.NET Core Runtime..."
if ! command -v dotnet &> /dev/null; then
  echo "Lade .NET Installationsskript herunter..."
  curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
  chmod +x dotnet-install.sh
  
  # Install runtime as system-wide or user-wide
  # We install for the real user, then symlink system-wide
  echo "Installiere .NET 8 Runtime..."
  su - "$REAL_USER" -c "bash $APP_DIR/dotnet-install.sh --channel 8.0 --runtime aspnetcore"
  
  # Create symlink so dotnet is globally accessible
  if [ -d "$USER_HOME/.dotnet" ]; then
    ln -sf "$USER_HOME/.dotnet/dotnet" /usr/bin/dotnet
    echo ".NET erfolgreich unter /usr/bin/dotnet verlinkt."
  fi
  rm dotnet-install.sh
else
  echo ".NET ist bereits installiert: $(dotnet --version)"
fi

# 5. Download and install Librespot
echo "5. Installiere librespot (Spotify Connect Daemon)..."
ARCH=$(uname -m)
LIBRESPOT_VERSION="v0.4.2"
DOWNLOAD_URL=""

if [ "$ARCH" = "x86_64" ]; then
  DOWNLOAD_URL="https://github.com/librespot-org/librespot/releases/download/${LIBRESPOT_VERSION}/librespot-linux-x86_64.tar.gz"
elif [ "$ARCH" = "aarch64" ] || [ "$ARCH" = "arm64" ]; then
  DOWNLOAD_URL="https://github.com/librespot-org/librespot/releases/download/${LIBRESPOT_VERSION}/librespot-linux-arm64.tar.gz"
else
  # 32-bit Pi OS fallback
  DOWNLOAD_URL="https://github.com/librespot-org/librespot/releases/download/${LIBRESPOT_VERSION}/librespot-linux-armhf.tar.gz"
fi

echo "Lade librespot für Architektur ($ARCH) herunter..."
wget -qO librespot.tar.gz "$DOWNLOAD_URL"
tar -xzf librespot.tar.gz
mv librespot /usr/local/bin/librespot
chmod +x /usr/local/bin/librespot
rm -f librespot.tar.gz
echo "librespot erfolgreich unter /usr/local/bin/librespot installiert."

# 6. Publish C# Web Application
echo "6. Kompiliere C# Projekt (Publishing)..."
# Change ownership back temporarily so build succeeds under correct user
chown -R "$REAL_USER":"$REAL_USER" "$APP_DIR"
su - "$REAL_USER" -c "cd $APP_DIR && dotnet publish -c Release -r linux-$(if [ "$ARCH" = "aarch64" ] || [ "$ARCH" = "arm64" ]; then echo "arm64"; else echo "arm"; fi) --self-contained false -o publish"

# 7. Setup Systemd Service
echo "7. Erstelle Autostart-Dienst (systemd)..."
SERVICE_FILE="/etc/systemd/system/spotipy.service"

cat <<EOF > "$SERVICE_FILE"
[Unit]
Description=Spotipy Connect Client and Web Service
After=network.target

[Service]
WorkingDirectory=$APP_DIR/publish
ExecStart=/usr/bin/dotnet $APP_DIR/publish/Spotipy.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=spotipy
User=$REAL_USER
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
EOF

# Reload and enable service
systemctl daemon-reload
systemctl enable spotipy.service
systemctl start spotipy.service
echo "Spotipy-Systemd-Dienst gestartet und für automatischen Systemstart aktiviert."

# 8. Configure HDMI 0 Kiosk Mode (Chromium Autostart)
echo "8. Richte HDMI 0 Kiosk-Modus ein..."

# Install chromium-browser if not installed (for kiosk mode display)
apt-get install -y chromium-browser || apt-get install -y chromium || true

KIOSK_CMD="chromium-browser --kiosk --noerrdialogs --disable-infobars --no-first-run --ozone-platform=wayland --enable-features=UseOzonePlatform http://localhost:5000/hdmi"

# 8a. Autostart for X11 / LXDE-pi
LXDE_DIR="$USER_HOME/.config/lxsession/LXDE-pi"
if [ -d "$USER_HOME/.config" ]; then
  mkdir -p "$LXDE_DIR"
  AUTOSTART_LXDE="$LXDE_DIR/autostart"
  
  if [ ! -f "$AUTOSTART_LXDE" ] || ! grep -q "localhost:5000/hdmi" "$AUTOSTART_LXDE"; then
    echo "@xset s off" >> "$AUTOSTART_LXDE"
    echo "@xset -dpms" >> "$AUTOSTART_LXDE"
    echo "@xset s noblank" >> "$AUTOSTART_LXDE"
    echo "@$KIOSK_CMD" >> "$AUTOSTART_LXDE"
    chown -R "$REAL_USER":"$REAL_USER" "$USER_HOME/.config"
    echo "Kiosk-Modus in LXDE-pi (X11) konfiguriert."
  fi
fi

# 8b. Autostart for Labwc (Default Window Manager on Pi OS Bookworm Desktop)
LABWC_DIR="$USER_HOME/.config/labwc"
if [ -d "$USER_HOME/.config" ]; then
  mkdir -p "$LABWC_DIR"
  AUTOSTART_LABWC="$LABWC_DIR/autostart"
  
  if [ ! -f "$AUTOSTART_LABWC" ] || ! grep -q "localhost:5000/hdmi" "$AUTOSTART_LABWC"; then
    # Disable screen blanking
    echo "xset s off || true" >> "$AUTOSTART_LABWC"
    echo "xset -dpms || true" >> "$AUTOSTART_LABWC"
    # Launch kiosk mode
    echo "$KIOSK_CMD &" >> "$AUTOSTART_LABWC"
    chown -R "$REAL_USER":"$REAL_USER" "$LABWC_DIR"
    echo "Kiosk-Modus in Labwc (Wayland) konfiguriert."
  fi
fi

# 8c. Autostart for Wayfire (Alternative on early Pi OS Wayland)
WAYFIRE_CONFIG="$USER_HOME/.config/wayfire.ini"
if [ -f "$WAYFIRE_CONFIG" ] && ! grep -q "localhost:5000/hdmi" "$WAYFIRE_CONFIG"; then
  cat <<EOF >> "$WAYFIRE_CONFIG"

[autostart]
spotipy_kiosk = $KIOSK_CMD
dpms_disable = wlopm --off HDMI-A-1
EOF
  chown "$REAL_USER":"$REAL_USER" "$WAYFIRE_CONFIG"
  echo "Kiosk-Modus in Wayfire (Wayland) konfiguriert."
fi

echo "===================================================="
echo " Spotipy Connect Client successfully installed!"
echo " "
echo " NÄCHSTE SCHRITTE:"
echo " 1. Öffne die Steuerungs-Web-UI im Netzwerk: http://<raspberrypi-ip>:5000"
echo " 2. Trage dort unter 'Einstellungen' deine Spotify Developer Keys ein."
echo " 3. Klicke auf 'Mit Spotify verbinden' zum Einloggen."
echo " 4. Der HDMI-Bildschirm zeigt die Visualisierung vollautomatisch."
echo "===================================================="
EOF
chmod +x /usr/local/bin/librespot || true
