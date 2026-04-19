using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Numerics;
using Raylib_cs;
using ImGuiNET;
using rlImGui_cs;

namespace XOSC
{
    public class AppConfig
    {
        public string Version = "1.2.3-linux";
        public bool ChatboxEnabled = true;
        public int Interval = 5;
        public string City = "Springfield,IL";
        public string Pronouns = "she/her + bi/trans";
        public string StatusIcon = "💬";

        public bool ThinMode = false;
        public bool PcMode = false;
        public bool DistroMode = false;
        public bool WeatherMode = false;
        public bool TimeMode = false;
        public bool SongMode = true;
        public bool NetMode = false;
        public bool HwNameMode = false;
        public bool StatusTextMode = true;
        public bool PronounsMode = true;
        public bool CpuTempOn = false;
        public bool GpuTempOn = false;

        public string CpuUnit = "%";
        public string RamUnit = "GB";
        public string GpuUnit = "%";
        public string VramUnit = "GB";
        public string TempUnit = "°C";

        public List<string> StatusList = new() { "Just vibing", "Gaming", "AFK" };
        public bool AutoCycleStatus = true;
    }

    public static class HardwareService
    {
        private static long _lastTotal, _lastIdle;

        public static string GetCpuLoad()
        {
            try
            {
                if (!File.Exists("/proc/stat")) return "--";
                var parts = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                long idle = long.Parse(parts[4]);
                long total = parts.Skip(1).Select(long.Parse).Sum();
                long dIdle = idle - _lastIdle;
                long dTotal = total - _lastTotal;
                _lastIdle = idle; _lastTotal = total;
                if (dTotal == 0) return "0";
                double load = 100.0 * (1.0 - (double)dIdle / dTotal);
                return Math.Round(load, 0).ToString();
            }
            catch { return "--"; }
        }

        public static string GetCpuTemp(string unit)
        {
            try
            {
                string path = "";
                if (Directory.Exists("/sys/class/hwmon/"))
                {
                    foreach (var dir in Directory.GetDirectories("/sys/class/hwmon/"))
                    {
                        string nameFile = $"{dir}/name";
                        if (File.Exists(nameFile) && (File.ReadAllText(nameFile).Contains("k10temp") || File.ReadAllText(nameFile).Contains("coretemp")))
                        {
                            if (File.Exists($"{dir}/temp1_input")) { path = $"{dir}/temp1_input"; break; }
                        }
                    }
                }
                if (string.IsNullOrEmpty(path) && File.Exists("/sys/class/thermal/thermal_zone0/temp"))
                    path = "/sys/class/thermal/thermal_zone0/temp";

                if (string.IsNullOrEmpty(path)) return "--";
                int c = int.Parse(File.ReadAllText(path).Trim()) / 1000;
                return unit == "°C" ? $"{c}°C" : $"{(c * 9 / 5) + 32}°F";
            }
            catch { return "--"; }
        }

        public static (string Load, string Vram, string Temp) GetGpuStats(string gUnit, string vUnit, string tUnit)
        {
            if (File.Exists("/usr/bin/nvidia-smi"))
            {
                try
                {
                    var psi = new ProcessStartInfo("nvidia-smi",
                        "--query-gpu=utilization.gpu,memory.used,memory.total,temperature.gpu --format=csv,noheader,nounits")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false
                    };
                    using var p = Process.Start(psi);
                    var output = p?.StandardOutput.ReadToEnd().Trim() ?? "";
                    if (!string.IsNullOrEmpty(output))
                    {
                        var values = output.Split(',');
                        if (values.Length >= 4)
                        {
                            string load = values[0].Trim() + "%";
                            long used = long.Parse(values[1].Trim());
                            long total = long.Parse(values[2].Trim());
                            string vram = vUnit == "GB"
                                ? $"{Math.Round(used / 1024.0, 1)}/{Math.Round(total / 1024.0, 1)}GB"
                                : $"{Math.Round(100.0 * used / total, 0)}%";
                            string temp = tUnit == "°C" ? $"{values[3].Trim()}°C" : $"{(int.Parse(values[3].Trim()) * 9 / 5) + 32}°F";
                            return (load, vram, temp);
                        }
                    }
                }
                catch { }
            }

            try
            {
                string gpu = Directory.GetDirectories("/sys/class/drm/")
                    .Where(d => d.Contains("card"))
                    .OrderByDescending(d => File.Exists($"{d}/device/mem_info_vram_total")
                        ? long.Parse(File.ReadAllText($"{d}/device/mem_info_vram_total").Trim()) : 0)
                    .FirstOrDefault() ?? "";

                if (string.IsNullOrEmpty(gpu)) return ("--", "--", "--");

                string load = File.Exists($"{gpu}/device/gpu_busy_percent")
                    ? File.ReadAllText($"{gpu}/device/gpu_busy_percent").Trim() + "%"
                    : "--%";

                long used = long.Parse(File.ReadAllText($"{gpu}/device/mem_info_vram_used").Trim());
                long total = long.Parse(File.ReadAllText($"{gpu}/device/mem_info_vram_total").Trim());
                string vram = vUnit == "GB"
                    ? $"{Math.Round(used / 1073741824.0, 1)}/{Math.Round(total / 1073741824.0, 1)}GB"
                    : $"{Math.Round(100.0 * used / total, 0)}%";

                string temp = "--";
                string hwmonPath = $"{gpu}/device/hwmon/";
                if (Directory.Exists(hwmonPath))
                {
                    var hwDirs = Directory.GetDirectories(hwmonPath);
                    if (hwDirs.Length > 0 && File.Exists($"{hwDirs[0]}/temp1_input"))
                    {
                        int c = int.Parse(File.ReadAllText($"{hwDirs[0]}/temp1_input").Trim()) / 1000;
                        temp = tUnit == "°C" ? $"{c}°C" : $"{(c * 9 / 5) + 32}°F";
                    }
                }
                return (load, vram, temp);
            }
            catch { return ("--", "--", "--"); }
        }

        public static string GetRamUsage(string unit)
        {
            try
            {
                var m = File.ReadLines("/proc/meminfo").ToList();
                long total = long.Parse(m.FirstOrDefault(x => x.StartsWith("MemTotal:"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1] ?? "0");
                long avail = long.Parse(m.FirstOrDefault(x => x.StartsWith("MemAvailable:"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1] ?? "0");
                if (total == 0) return "--";
                return unit == "GB"
                    ? $"{Math.Round((total - avail) / 1048576.0, 1)}/{Math.Round(total / 1048576.0, 1)}GB"
                    : $"{Math.Round(100.0 * (total - avail) / total, 0)}%";
            }
            catch { return "--"; }
        }
    }

    public static class MusicChatEngine
    {
        private static UdpClient _client = new();
        private static CancellationTokenSource? _cts;

        private static int _statusIdx = 0;

        private static string _cpuName = "CPU", _gpuName = "GPU", _music = "Chilling", _weather = "...";
        private static DateTime _lastRefresh = DateTime.MinValue;
        private static DateTime _manualExpiry = DateTime.MinValue;
        private static string _manualMsg = "";

        private static string _lastSentText = "";
        public static string LastSentText => _lastSentText;
        private static DateTime _lastSentTime = DateTime.MinValue;

        public static string LastError = "None";
        public static int PacketsSent = 0;
        public static string EngineState = "Initialized";

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

        public static void Init()
        {
            _client = new UdpClient();
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            ScrapeHardwareNames();
            Task.Run(() => Loop(_cts.Token));
        }

        public static void SetManual(string m)
        {
            _manualMsg = m;
            _manualExpiry = DateTime.Now.AddSeconds(20);
            _lastSentText = "";
        }

        private static async Task Loop(CancellationToken t)
        {
            while (!t.IsCancellationRequested)
            {
                if (Program.Config.ChatboxEnabled)
                {
                    try { await Update(); }
                    catch (Exception ex) { LastError = ex.Message; EngineState = "Error"; }
                }
                else
                {
                    EngineState = "Paused";
                }

                await Task.Delay(500, t);
            }
        }

        private static async Task Update()
        {
            var cfg = Program.Config;

            if (DateTime.Now < _manualExpiry)
            {
                EngineState = "Sending Manual Msg";
                string manualText = $"💬 {_manualMsg}";
                if (manualText != _lastSentText)
                {
                    SendOsc("/chatbox/input", manualText);
                    _lastSentText = manualText;
                    _lastSentTime = DateTime.Now;
                }
                return;
            }

            bool timeForRefresh = (DateTime.Now - _lastRefresh).TotalSeconds >= cfg.Interval;

            if (timeForRefresh)
            {
                EngineState = "Fetching Metadata";
                _music = FetchMusic();
                if (cfg.WeatherMode) _weather = await FetchWeather(cfg.City);
                _lastRefresh = DateTime.Now;

                if (cfg.AutoCycleStatus && cfg.StatusList.Count > 0)
                    _statusIdx = (_statusIdx + 1) % cfg.StatusList.Count;
            }

            bool timeForSend = (DateTime.Now - _lastSentTime).TotalSeconds >= Math.Max(cfg.Interval, 2.0);

            if (!timeForSend)
            {
                EngineState = "Idle (respecting interval)";
                return;
            }

            EngineState = "Building Layout";
            var lines = new List<string>();

            if (cfg.StatusTextMode && cfg.StatusList.Count > 0)
            {
                if (_statusIdx >= cfg.StatusList.Count) _statusIdx = 0;
                lines.Add(cfg.StatusList[_statusIdx]);
            }

            if (cfg.PronounsMode)
                lines.Add($"{cfg.StatusIcon} {cfg.Pronouns}");

            string env = "";
            if (cfg.TimeMode) env += $"🕒 {DateTime.Now:hh:mm tt}";
            if (cfg.DistroMode) env += (env == "" ? "" : " | ") + "🐧 " + GetDistroName();
            if (!string.IsNullOrEmpty(env)) lines.Add(env);

            if (cfg.WeatherMode) lines.Add($"🌤️ {_weather}");

            if (cfg.PcMode)
            {
                var g = HardwareService.GetGpuStats(cfg.GpuUnit, cfg.VramUnit, cfg.TempUnit);
                string cStat = cfg.CpuUnit == "Watt" ? "--W" : HardwareService.GetCpuLoad() + "%";
                if (cfg.CpuTempOn) cStat += $" ({HardwareService.GetCpuTemp(cfg.TempUnit)})";

                lines.Add($"🖥️ {(cfg.HwNameMode ? Stylize(_cpuName) : "CPU")} ─ {cStat}");
                lines.Add($"🎮 {(cfg.HwNameMode ? Stylize(_gpuName) : "GPU")} ─ {g.Load}" + (cfg.GpuTempOn ? $" ({g.Temp})" : ""));
                lines.Add($"🐏 ʳᵃᵐ: {HardwareService.GetRamUsage(cfg.RamUnit)} | 🎞️ ᵛʳᵃᵐ: {g.Vram}");
            }

            if (cfg.NetMode) lines.Add($"🌐 Ping: {GetPing()}ms");

            if (cfg.SongMode && _music != "Chilling")
                lines.Add($"♪ {_music}");

            string fullText = string.Join("\n", lines);

            if (cfg.ThinMode)
            {
                const int maxLen = 140;
                if (fullText.Length > maxLen)
                    fullText = fullText.Substring(0, maxLen);
                fullText += "\u0003\u001f";
            }

            EngineState = "Sending OSC";
            SendOsc("/chatbox/input", fullText);
            _lastSentText = fullText;
            _lastSentTime = DateTime.Now;
            PacketsSent++;
        }

        private static void SendOsc(string address, string text)
        {
            try
            {
                List<byte> packet = new();

                void AddOscString(string s)
                {
                    byte[] b = Encoding.UTF8.GetBytes(s);
                    packet.AddRange(b);
                    int pad = 4 - (b.Length % 4);
                    if (pad == 0) pad = 4;
                    for (int i = 0; i < pad; i++) packet.Add(0);
                }

                void AddOscInt(int v)
                {
                    byte[] b = BitConverter.GetBytes(v);
                    if (BitConverter.IsLittleEndian) Array.Reverse(b);
                    packet.AddRange(b);
                }

                AddOscString(address);
                AddOscString(",sTT");
                AddOscString(text);

                _client.Send(packet.ToArray(), packet.Count, new IPEndPoint(IPAddress.Loopback, 9000));
                LastError = "None";
            }
            catch (Exception ex)
            {
                LastError = "OSC Error: " + ex.Message;
            }
        }

        private static string GetPing()
        {
            try { return new System.Net.NetworkInformation.Ping().Send("1.1.1.1", 300).RoundtripTime.ToString(); }
            catch { return "--"; }
        }

        private static string FetchMusic()
        {
            try
            {
                var psi = new ProcessStartInfo("playerctl")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                psi.ArgumentList.Add("--all-players");
                psi.ArgumentList.Add("metadata");
                psi.ArgumentList.Add("--format");
                psi.ArgumentList.Add("XOSC_SEP{{status}}XOSC_SEP{{artist}} - {{title}}");

                using var p = Process.Start(psi);
                string output = p?.StandardOutput.ReadToEnd().Trim() ?? "";
                p?.WaitForExit(1500);

                if (!string.IsNullOrEmpty(output))
                {
                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (!line.StartsWith("XOSC_SEP")) continue;
                        var parts = line.Split("XOSC_SEP", StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 2) continue;
                        string status = parts[0].Trim();
                        string song = parts.Length >= 2 ? parts[1].Trim() : "";
                        if (status == "Playing" && !string.IsNullOrWhiteSpace(song) && song != " - " && song != "-")
                            return song;
                    }
                }

                return "Chilling";
            }
            catch
            {
                return "Chilling";
            }
        }

        private static async Task<string> FetchWeather(string c)
        {
            try { return (await _http.GetStringAsync($"https://wttr.in/{c}?format=%C+%t")).Trim(); }
            catch { return "N/A"; }
        }

        private static string GetDistroName()
        {
            try
            {
                if (!File.Exists("/etc/os-release"))
                    return "Linux";

                string name = "Linux";
                string version = "";

                foreach (var line in File.ReadLines("/etc/os-release"))
                {
                    if (line.StartsWith("NAME="))
                        name = line.Substring(5).Trim('"', '\'').Replace("Linux", "").Trim();
                    else if (line.StartsWith("VERSION_ID="))
                        version = line.Substring(11).Trim('"', '\'');
                }

                string shortName = name;
                if (!string.IsNullOrEmpty(version))
                    shortName += " " + version;

                if (shortName.Length > 25)
                    shortName = shortName.Substring(0, 22) + "...";

                return shortName;
            }
            catch
            {
                return "Linux";
            }
        }

        private static void ScrapeHardwareNames()
        {
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string[] paths =
                {
                    Path.Combine(home, ".local/share/Steam/steamapps/compatdata/438100/pfx/drive_c/users/steamuser/AppData/LocalLow/VRChat/VRChat"),
                    Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/compatdata/438100/pfx/drive_c/users/steamuser/AppData/LocalLow/VRChat/VRChat")
                };

                foreach (var p in paths)
                {
                    if (Directory.Exists(p))
                    {
                        var log = Directory.GetFiles(p, "output_log_*.txt").OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
                        if (log != null)
                        {
                            var lines = File.ReadLines(log).Take(2000);
                            foreach (var l in lines)
                            {
                                if (l.Contains("Processor Type:"))
                                {
                                    string raw = l.Substring(l.IndexOf("Processor Type:") + 15).Trim();
                                    _cpuName = Regex.Replace(raw, @"(\s\d+-Core| Processor| @.*|AMD |Intel |Core |Ryzen )", "").Trim();
                                    if (_cpuName.Length > 12) _cpuName = _cpuName.Substring(0, 12);
                                }
                                if (l.Contains("Graphics Device Name:"))
                                {
                                    string raw = l.Substring(l.IndexOf("Graphics Device Name:") + 21).Trim();
                                    _gpuName = Regex.Replace(raw, @"(\(RADV.*| Graphics|AMD |NVIDIA |GeForce |Radeon |\sRX\s|\sXT)", "").Trim();
                                    if (_gpuName.Length > 12) _gpuName = _gpuName.Substring(0, 12);
                                }
                            }
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        private static string Stylize(string t)
        {
            string n = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            string s = "ᵃᵇᶜᵈᵉᶠᵍʰᶦʲᵏˡᵐⁿᵒᵖᵠʳˢᵗᵘᵛʷˣʸᶻᵃᵇᶜᵈᵉᶠᵍʰᶦʲᵏˡᵐⁿᵒᵖᵠʳˢᵗᵘᵛʷˣʸᶻ⁰¹²³⁴⁵⁶⁷⁸⁹";
            StringBuilder sb = new();
            foreach (char c in t)
            {
                int i = n.IndexOf(c);
                sb.Append(i != -1 ? s[i] : c);
            }
            return sb.ToString();
        }
    }

    class Program
    {
        public static AppConfig Config = new();
        private static string _configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "XOSC");
        private static string _path = Path.Combine(_configDir, "config.json");
        private static string _chatIn = "";
        private static Mutex? _mtx;

        public static void SaveConfig()
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static void Main(string[] args)
        {
            _mtx = new Mutex(true, "XOSC_VRC_Unique_Runner", out bool fresh);
            if (!fresh) Environment.Exit(0);

            Directory.CreateDirectory(_configDir);

            if (File.Exists(_path))
            {
                try { Config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_path)) ?? new(); }
                catch { }
            }

            MusicChatEngine.Init();

            Raylib.InitWindow(920, 720, "XOSC - VRChat OSC Chatbox (Linux)");
            rlImGui.Setup(true);
            Raylib.SetTargetFPS(60);

            while (!Raylib.WindowShouldClose())
            {
                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Color(25, 25, 30, 255));
                rlImGui.Begin();
                DrawUI();
                rlImGui.End();
                Raylib.EndDrawing();
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true }));

            rlImGui.Shutdown();
            Raylib.CloseWindow();
        }

        static void DrawUI()
        {
            ImGui.SetNextWindowPos(new Vector2(0, 0));
            ImGui.SetNextWindowSize(new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()));
            ImGui.Begin("XOSC Main", ImGuiWindowFlags.NoDecoration);

            if (ImGui.BeginTabBar("Tabs"))
            {
                if (ImGui.BeginTabItem("Overview"))
                {
                    ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), $"Packets Sent: {MusicChatEngine.PacketsSent}");
                    ImGui.TextColored(new Vector4(1f, 1f, 0.4f, 1f), $"Engine State: {MusicChatEngine.EngineState}");
                    ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), $"Last Error: {MusicChatEngine.LastError}");
                    ImGui.Separator();

                    ImGui.InputText("Chat Override", ref _chatIn, 128);
                    ImGui.SameLine();
                    if (ImGui.Button("Send (20s)"))
                    {
                        MusicChatEngine.SetManual(_chatIn);
                        _chatIn = "";
                    }

                    ImGui.Separator();
                    if (ImGui.Checkbox("Master Engine Enable", ref Config.ChatboxEnabled)) SaveConfig();
                    ImGui.Text($"Update Interval: {Config.Interval}s | v{Config.Version}");
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Statuses"))
                {
                    ImGui.Text("Animation disabled - Static mode");
                    ImGui.Separator();

                    for (int i = 0; i < Config.StatusList.Count; i++)
                    {
                        string s = Config.StatusList[i];
                        ImGui.PushID(i);
                        if (ImGui.InputText("##edit", ref s, 100)) { Config.StatusList[i] = s; SaveConfig(); }
                        ImGui.SameLine();
                        if (ImGui.Button("Delete"))
                        {
                            Config.StatusList.RemoveAt(i);
                            SaveConfig();
                            ImGui.PopID();
                            break;
                        }
                        ImGui.PopID();
                    }
                    if (ImGui.Button("+ Add Status")) { Config.StatusList.Add("New Status"); SaveConfig(); }
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Toggles"))
                {
                    if (ImGui.Checkbox("Status Text", ref Config.StatusTextMode)) SaveConfig();
                    if (ImGui.Checkbox("Pronouns", ref Config.PronounsMode)) SaveConfig();
                    if (ImGui.Checkbox("Hardware Stats", ref Config.PcMode)) SaveConfig();
                    if (ImGui.Checkbox("Stylized Names", ref Config.HwNameMode)) SaveConfig();
                    if (ImGui.Checkbox("CPU Temps", ref Config.CpuTempOn)) SaveConfig();
                    if (ImGui.Checkbox("GPU Temps", ref Config.GpuTempOn)) SaveConfig();
                    if (ImGui.Checkbox("Song Mode", ref Config.SongMode)) SaveConfig();
                    if (ImGui.Checkbox("Distro Mode", ref Config.DistroMode)) SaveConfig();
                    if (ImGui.Checkbox("Weather Mode", ref Config.WeatherMode)) SaveConfig();
                    if (ImGui.Checkbox("Time Mode", ref Config.TimeMode)) SaveConfig();
                    if (ImGui.Checkbox("Ping Mode", ref Config.NetMode)) SaveConfig();
                    if (ImGui.Checkbox("Auto-Cycle", ref Config.AutoCycleStatus)) SaveConfig();
                    if (ImGui.Checkbox("Thin Mode", ref Config.ThinMode)) SaveConfig();

                    ImGui.Separator();
                    int charCount = System.Text.Encoding.UTF8.GetByteCount(MusicChatEngine.LastSentText);
                    string charLabel = $"Last msg: {charCount} / 144 chars";
                    if (charCount > 144)
                        ImGui.TextColored(new Vector4(1f, 0.2f, 0.2f, 1f), charLabel + "  !! OVER LIMIT !!");
                    else if (charCount > 120)
                        ImGui.TextColored(new Vector4(1f, 0.75f, 0.1f, 1f), charLabel + "  (near limit)");
                    else
                        ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), charLabel);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Settings"))
                {
                    if (ImGui.TreeNode("Hardware Units"))
                    {
                        if (ImGui.BeginCombo("CPU", Config.CpuUnit))
                        {
                            if (ImGui.Selectable("%")) { Config.CpuUnit = "%"; SaveConfig(); }
                            if (ImGui.Selectable("Watt")) { Config.CpuUnit = "Watt"; SaveConfig(); }
                            ImGui.EndCombo();
                        }
                        if (ImGui.BeginCombo("RAM", Config.RamUnit))
                        {
                            if (ImGui.Selectable("%")) { Config.RamUnit = "%"; SaveConfig(); }
                            if (ImGui.Selectable("GB")) { Config.RamUnit = "GB"; SaveConfig(); }
                            ImGui.EndCombo();
                        }
                        if (ImGui.BeginCombo("GPU", Config.GpuUnit))
                        {
                            if (ImGui.Selectable("%")) { Config.GpuUnit = "%"; SaveConfig(); }
                            if (ImGui.Selectable("Watt")) { Config.GpuUnit = "Watt"; SaveConfig(); }
                            ImGui.EndCombo();
                        }
                        if (ImGui.BeginCombo("VRAM", Config.VramUnit))
                        {
                            if (ImGui.Selectable("%")) { Config.VramUnit = "%"; SaveConfig(); }
                            if (ImGui.Selectable("GB")) { Config.VramUnit = "GB"; SaveConfig(); }
                            ImGui.EndCombo();
                        }
                        if (ImGui.BeginCombo("Temp", Config.TempUnit))
                        {
                            if (ImGui.Selectable("°C")) { Config.TempUnit = "°C"; SaveConfig(); }
                            if (ImGui.Selectable("°F")) { Config.TempUnit = "°F"; SaveConfig(); }
                            ImGui.EndCombo();
                        }
                        ImGui.TreePop();
                    }
                    ImGui.InputText("Pronouns", ref Config.Pronouns, 64);
                    if (ImGui.IsItemDeactivatedAfterEdit()) SaveConfig();
                    ImGui.InputText("Weather City", ref Config.City, 64);
                    if (ImGui.IsItemDeactivatedAfterEdit()) SaveConfig();
                    ImGui.SliderInt("Interval (s)", ref Config.Interval, 1, 60);
                    if (ImGui.IsItemDeactivatedAfterEdit()) SaveConfig();
                    if (ImGui.Button("Restart OSC Engine")) MusicChatEngine.Init();
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
            ImGui.End();
        }
    }
}