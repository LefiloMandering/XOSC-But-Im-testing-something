# XOSC - VRChat OSC Tool (Linux)

![Preview](https://github.com/hollyntt/XOSC/blob/master/Product%20Images/Screenshot_20260418_225445.png?raw=true)

XOSC is a high-performance, native C# OSC manager designed specifically for VRChat users on Linux (Fedora, Arch, Steam Deck). It provides a sleek ImGui-based interface to manage your chatbox, music, and hardware telemetry without the overhead of heavy scripts or external Python dependencies.

## ✨ Features
*   **Horizontal Layout:** A compact, clean status display designed to fit perfectly in the VRChat chatbox bubble (Magic Chatbox style).
*   **Status Manager:** Add, edit, and delete multiple custom statuses with a built-in auto-cycler.
*   **Music Integration:** Native support for `playerctl` and an `xdotool` fallback for browser-based players (SoundCloud, Spotify, YouTube).
*   **Hardware Telemetry:** Deep integration with Linux `/proc` and `/sys` to show real-time CPU, GPU, RAM, and VRAM usage.
*   **AMD & NVIDIA Support:** Automated scraper detects your dGPU to show accurate load and temperatures.
*   **VRChat World Tracking:** Integration with the VRChat API to show your current world name and instance ping.
*   **Manual Chat Override:** Type custom messages directly in the app to send them to VRChat with a 20-second grace period.

---

## 🛠️ Prerequisites

Even when using the pre-compiled releases, your system needs the following tools to handle hardware and music scraping:

### 1. System Packages
**Fedora:**
```bash
sudo dnf install playerctl xdotool lm_sensors
```
**Arch Linux / Steam Deck:**
```bash
sudo pacman -S playerctl xdotool lm_sensors
```

### 2. .NET Runtime (Only required if building from source)
[Install .NET 8.0+ on Linux](https://learn.microsoft.com/en-us/dotnet/core/install/linux)

---

## 🚀 Installation

### Option 1: Pre-compiled Binary (Recommended)
1. Download the latest `XOSC` binary from the [Releases Page](https://github.com/hollyntt/XOSC/releases/).
2. Give the file execution permissions:
   ```bash
   chmod +x XOSC
   ```
3. Run it:
   ```bash
   ./XOSC
   ```

### Option 2: Build from Source
1. Clone the repository:
   ```bash
   git clone https://github.com/hollyntt/XOSC.git
   cd XOSC
   ```
2. Build and run:
   ```bash
   dotnet run
   ```

---

## 🎮 VRChat Setup

### Enable OSC
1. Open VRChat.
2. Open your **Action Menu** (Radial Menu).
3. Go to **Options** > **OSC**.
4. Set **Enabled** to **ON**.

### Important: Real-time Logs
To ensure XOSC can scrape your hardware names correctly from the VRChat logs, add this to your VRChat **Launch Options** in Steam:
```text
-log-file-buffer-size 0
```

---

## ⚙️ Configuration
XOSC saves your preferences automatically to `/home/user/.config/xosc/config.json` / `AppData\Roaming\xosc\config.json`.

*   **Statuses:** Managed via the "Statuses" tab.
*   **Toggles:** Choose exactly what info (Time, Distro, Weather, PC Stats) is sent to VRChat in the "Toggles" tab.
*   **Units:** Configure percentages, Watts, or GB readouts in the "Settings" tab.

## 🤝 Contributing
XOSC is built for the Linux VRChat community. Feel free to fork the repo, submit issues, or create pull requests!

---

### ⚠️ Note for Steam Deck Users
XOSC automatically detects if you are running Steam as a **Flatpak** or a native package to ensure your VRChat logs and hardware data are always found.
