# VRChat MusicChatBox (Linux)

A feature-rich OSC Chatbox manager for VRChat on Linux (Fedora/Steam Deck/Ubuntu). Features a vertical stacked UI, animated typewriter/scroll statuses, PC hardware monitoring, and music integration.

![Preview](https://gitlab.com/hollyntii/vrclinuxchatbox/-/raw/main/Screenshot_20260415_182633.png)
![Preview](https://gitlab.com/hollyntii/vrclinuxchatbox/-/raw/main/Screenshot_20260415_185452.png)

## ✨ Features
*   **Animated Statuses:** Choice of Typewriter (with blinking cursor), Scrolling Marquee, or Static.
*   **Music Integration:** Automatically fetches "Artist - Song" from desktop players (`playerctl`) or Browsers (`SoundCloud`, `Spotify`, `YouTube`).
*   **System Stats:** Real-time CPU usage, RAM, and Linux Distro display.
*   **VRChat Integration:** Real-time FPS and Ping pulled directly from your Steam/Proton logs.
*   **Interactive Menu:** Beautiful TUI powered by `gum` to toggle features and edit settings on the fly.
*   **Persistent Config:** Settings are saved automatically to `~/.config/vrc-magicchatbox.conf`.

---

## 🛠️ Prerequisites

Before running the script, ensure you have the following dependencies installed.

### 1. System Packages (Fedora)
```bash
sudo dnf install gum playerctl xdotool lm_sensors curl
```
*(For Ubuntu/Debian: `sudo apt install gum playerctl xdotool lm-sensors curl`)*

### 2. Python Dependencies
The script uses a tiny Python snippet to handle OSC networking:
```bash
pip install python-osc --user
```

---

### 🏔️ Arch Linux / Steam Deck Installation

Arch users are "built different," so the dependency names are slightly different.

#### 1. Install System Dependencies
```bash
sudo pacman -S gum playerctl xdotool lm_sensors curl
```

#### 2. Install Python OSC
On Arch, it is highly recommended to install Python packages via the AUR to avoid breaking your system environment:
```bash
# If you use yay:
yay -S python-python-osc

# Alternatively, if you prefer pip (use --break-system-packages if on a modern Arch build):
pip install python-osc --user --break-system-packages
```

#### 3. Initial Hardware Setup
Arch doesn't always auto-configure sensors. Run this once so the script can read your temperatures:
```bash
sudo sensors-detect  # Answer YES to all prompts
```

---

### 🎮 Steam Deck (SteamOS) Specifics
If you are running this on a **Steam Deck** (in Desktop Mode), Steam is often installed as a **Flatpak**. This changes the path where your VRChat logs are stored.

Open `musicchat.sh` and change the `VRCLOG` line at the top to this:

```bash
# FLATPAK PATH (Uncomment this if on Steam Deck/Flatpak)
# VRCLOG="$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/compatdata/438100/pfx/drive_c/users/steamuser/AppData/LocalLow/VRChat/VRChat/output_log.txt"
```

---

### 🛠️ Troubleshooting for Arch
*   **"command not found: gum"**: Ensure `/usr/bin` is in your `$PATH` (it usually is).
*   **"No music"**: If you use a niche window manager (like Hyprland or Sway), `xdotool` might struggle with browser titles. In that case, the script will rely on `playerctl` for native apps like Spotify/Cider.
*   **Permissions**: Ensure the script is executable: `chmod +x ~/Applications/musicchat.sh`.

---

### Final Master Code Update
I've added a **Flatpak path check** to the top of the script so it works for Steam Deck users out of the box.

```bash
#!/bin/bash
# =================================================================
# VRChat MusicChatBox - GitLab Repository Master
# =================================================================

OSC_HOST="127.0.0.1"
OSC_PORT=9000
CONFIG_FILE="$HOME/.config/vrc-magicchatbox.conf"

# PATH AUTO-DETECTION (Steam vs Flatpak)
STEAM_PATH="$HOME/.local/share/Steam"
FLATPAK_PATH="$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"

if [ -d "$FLATPAK_PATH" ]; then
    VRC_BASE="$FLATPAK_PATH"
else
    VRC_BASE="$STEAM_PATH"
fi

VRCLOG="$VRC_BASE/steamapps/compatdata/438100/pfx/drive_c/users/steamuser/AppData/LocalLow/VRChat/VRChat/output_log.txt"

---

## 🚀 Installation

### 1. Clone the Repository
```bash
cd ~/Applications
git clone https://gitlab.com/hollyntii/vrclinuxchatbox.git
cd vrclinuxchatbox
```

### 2. Make it Executable
```bash
chmod +x musicchat.sh
```

### 3. (Optional) Create a Global Shortcut
To run the chatbox by just typing `musicchat` from any terminal:
```bash
sudo ln -sf ~/Applications/vrclinuxchatbox/musicchat.sh /usr/local/bin/musicchat
```

---

## 🎮 Usage

Simply run the script:
```bash
./musicchat.sh
# OR, if you created the shortcut:
musicchat
```

### ⚠️ Important Note on VRChat Logs
The script is configured to look for your Steam/Proton logs at:
`~/.local/share/Steam/steamapps/compatdata/438100/pfx/drive_c/users/steamuser/AppData/LocalLow/VRChat/VRChat/`

If your Steam library is installed in a custom location, open `musicchat.sh` and update the `VRCLOG` variable at the top of the file.

### Enable OSC in VRChat
1. Open VRChat.
2. Open your **Action Menu** (Radial Menu).
3. Go to **Options** > **OSC**.
4. Set **Enabled** to **ON**.

---

## ⚙️ Configuration
The first time you run the script, it will create a configuration file at `~/.config/vrc-magicchatbox.conf`. You can edit this file manually or use the "Edit Settings" menu in-app to change:
*   Update Interval
*   Pronouns & Status Icons
*   Weather City
*   Feature Toggles

## 🤝 Contributing
Feel free to fork this project and submit Merge Requests on GitLab!

