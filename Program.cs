using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;
using ImGuiNET;
using rlImGui_cs;

namespace XOSC
{
    public class AppConfig
    {
        public bool ChatboxEnabled = true;
        public int Interval = 1;
        public string City = "";
        public string Pronouns = "";
        public string StatusIcon = "";
        public bool ThinMode = false;
        public bool PcMode = false;
        public bool ShowRam = false;
        public bool ShowVram = false;
        public bool DistroMode = false;
        public bool WeatherMode = false;
        public bool TimeMode = false;
        public bool SongMode = true;
        public bool NetMode = false;
        public bool VrcPingMode = false;
        public bool HwNameMode = false;
        public bool CustomCpuNameOn = false;
        public bool CustomGpuNameOn = false;
        public string CustomCpuName = "CPU";
        public string CustomGpuName = "GPU";
        public bool StatusTextMode = false;
        public bool PronounsMode = false;
        public bool CpuTempOn = false;
        public bool GpuTempOn = false;
        public string CpuUnit = "%";
        public string RamUnit = "GB";
        public string GpuUnit = "%";
        public string VramUnit = "GB";
        public string TempUnit = "°C";
        public List<string> StatusList = new() { };
        public bool AutoCycleStatus = false;
        public string PublishPath = "https://github.com/hollyntt/XOSC/raw/master/publish/XOSC.zip";
        public bool BetaOptIn = false;
        public string Cookie = "";
    }

    public static class HardwareService
    {
        private static long _lastTotal, _lastIdle;
        public static string GetCpuLoad()
        {
            try {
                if (!File.Exists("/proc/stat")) return "--";
                var parts = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                long idle = long.Parse(parts[4]);
                long total = parts.Skip(1).Select(long.Parse).Sum();
                long dIdle = idle - _lastIdle;
                long dTotal = total - _lastTotal;
                _lastIdle = idle; _lastTotal = total;
                if (dTotal == 0) return "0";
                return Math.Round(100.0 * (1.0 - (double)dIdle / dTotal), 0).ToString();
            } catch { return "--"; }
        }
        public static string GetCpuTemp(string unit)
        {
            try {
                string path = "";
                if (Directory.Exists("/sys/class/hwmon/")) {
                    foreach (var dir in Directory.GetDirectories("/sys/class/hwmon/")) {
                        if (!File.Exists($"{dir}/name")) continue;
                        string name = File.ReadAllText($"{dir}/name");
                        if (name.Contains("k10temp") || name.Contains("coretemp")) {
                            if (File.Exists($"{dir}/temp1_input")) { path = $"{dir}/temp1_input"; break; }
                        }
                    }
                }
                if (string.IsNullOrEmpty(path)) return "--";
                int c = int.Parse(File.ReadAllText(path).Trim()) / 1000;
                return unit == "°C" ? $"{c}°C" : $"{(c * 9 / 5) + 32}°F";
            } catch { return "--"; }
        }
        public static (string Load, string Vram, string Temp) GetGpuStats(string gUnit, string vUnit, string tUnit)
        {
            if (File.Exists("/usr/bin/nvidia-smi")) {
                try {
                    var psi = new ProcessStartInfo("nvidia-smi", "--query-gpu=utilization.gpu,memory.used,memory.total,temperature.gpu --format=csv,noheader,nounits") { RedirectStandardOutput = true, UseShellExecute = false };
                    using var p = Process.Start(psi);
                    var vals = p?.StandardOutput.ReadToEnd().Trim().Split(',');
                    if (vals?.Length >= 4) {
                        string load = vals[0].Trim() + "%";
                        long used = long.Parse(vals[1].Trim()), total = long.Parse(vals[2].Trim());
                        string vram = vUnit == "GB" ? $"{Math.Round(used / 1024.0, 1)}/{Math.Round(total / 1024.0, 1)}GB" : $"{Math.Round(100.0 * used / total, 0)}%";
                        string temp = tUnit == "°C" ? $"{vals[3].Trim()}°C" : $"{(int.Parse(vals[3].Trim()) * 9 / 5) + 32}°F";
                        return (load, vram, temp);
                    }
                } catch { }
            }
            try {
                string gpu = Directory.GetDirectories("/sys/class/drm/").Where(d => d.Contains("card")).OrderByDescending(d => File.Exists($"{d}/device/mem_info_vram_total") ? long.Parse(File.ReadAllText($"{d}/device/mem_info_vram_total").Trim()) : 0).FirstOrDefault() ?? "";
                if (string.IsNullOrEmpty(gpu)) return ("--", "--", "--");
                string load = File.Exists($"{gpu}/device/gpu_busy_percent") ? File.ReadAllText($"{gpu}/device/gpu_busy_percent").Trim() + "%" : "--%";
                long used = long.Parse(File.ReadAllText($"{gpu}/device/mem_info_vram_used").Trim()), total = long.Parse(File.ReadAllText($"{gpu}/device/mem_info_vram_total").Trim());
                string vram = vUnit == "GB" ? $"{Math.Round(used / 1073741824.0, 1)}/{Math.Round(total / 1073741824.0, 1)}GB" : $"{Math.Round(100.0 * used / total, 0)}%";
                string temp = "--", hwmon = $"{gpu}/device/hwmon/";
                if (Directory.Exists(hwmon)) {
                    var dir = Directory.GetDirectories(hwmon).FirstOrDefault();
                    if (dir != null && File.Exists($"{dir}/temp1_input")) {
                        int c = int.Parse(File.ReadAllText($"{dir}/temp1_input").Trim()) / 1000;
                        temp = tUnit == "°C" ? $"{c}°C" : $"{(c * 9 / 5) + 32}°F";
                    }
                }
                return (load, vram, temp);
            } catch { return ("--", "--", "--"); }
        }
        public static string GetRamUsage(string unit)
        {
            try {
                var m = File.ReadLines("/proc/meminfo").ToList();
                long total = long.Parse(m.FirstOrDefault(x => x.StartsWith("MemTotal:"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1] ?? "0");
                long avail = long.Parse(m.FirstOrDefault(x => x.StartsWith("MemAvailable:"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1] ?? "0");
                return unit == "GB" ? $"{Math.Round((total - avail) / 1048576.0, 1)}/{Math.Round(total / 1048576.0, 1)}GB" : $"{Math.Round(100.0 * (total - avail) / total, 0)}%";
            } catch { return "--"; }
        }
    }

    public static class Updater
    {
        public static string Status = "idle";
        public static bool NewVersionFound = false;
        private static byte[]? _pendingData;
        private const string BetaUrl = "https://github.com/hollyntt/XOSC/raw/master/publish/XOSC.zip";
        private const string StableApiUrl = "https://api.github.com/repos/hollyntt/XOSC/releases/latest";
        public static async Task CheckForUpdates()
        {
            Status = "checking GitHub..."; NewVersionFound = false;
            try {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
                http.DefaultRequestHeaders.Add("User-Agent", "XOSC-Updater");
                string downloadUrl = BetaUrl;
                if (!Program.Config.BetaOptIn) {
                    var resp = await http.GetStringAsync(StableApiUrl);
                    using var doc = JsonDocument.Parse(resp);
                    if (doc.RootElement.GetProperty("tag_name").GetString() == Program.AppVersion) { Status = "already up to date"; return; }
                    downloadUrl = doc.RootElement.GetProperty("assets")[0].GetProperty("browser_download_url").GetString() ?? BetaUrl;
                }
                var zipData = await http.GetByteArrayAsync(downloadUrl);
                using var ms = new MemoryStream(zipData);
                using var archive = new ZipArchive(ms);
                var entry = archive.GetEntry("linux-x64/XOSC") ?? archive.GetEntry("XOSC");
                if (entry == null) { Status = "XOSC binary not found in zip"; return; }
                string self = Environment.ProcessPath ?? "";
                if (Program.Config.BetaOptIn && entry.Length == new FileInfo(self).Length) { Status = "already up to date"; return; }
                Status = "update found!"; NewVersionFound = true;
                using var es = entry.Open(); using var msWrite = new MemoryStream();
                await es.CopyToAsync(msWrite); _pendingData = msWrite.ToArray();
            } catch (Exception e) { Status = $"error: {e.Message}"; }
        }
        public static void ApplyUpdate()
        {
            if (_pendingData == null) return;
            try {
                string self = Environment.ProcessPath ?? "";
                File.Move(self, self + ".bak", true);
                File.WriteAllBytes(self, _pendingData);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) Process.Start("chmod", $"+x \"{self}\"").WaitForExit();
                Thread.Sleep(500); Process.Start(new ProcessStartInfo(self) { UseShellExecute = true });
                Environment.Exit(0);
            } catch (Exception e) { Status = $"apply error: {e.Message}"; }
        }
    }

    public static class MusicChatEngine
    {
        private static UdpClient _client = new();
        private static CancellationTokenSource? _cts;
        private static int _statusIdx = 0;
        private static string _cpu = "CPU", _gpu = "GPU", _music = "Chilling", _weather = "...";
        private static DateTime _lastRefresh = DateTime.MinValue, _manualExpiry = DateTime.MinValue, _lastSent = DateTime.MinValue;
        private static string _manualMsg = "";
        public static int PacketsSent = 0;
        public static string EngineState = "Idle";
        public static readonly object ListLock = new();
        public static void Init() { _client = new UdpClient(); _cts?.Cancel(); _cts = new CancellationTokenSource(); ScrapeHardwareNames(); Task.Run(() => Loop(_cts.Token)); }
        public static void SetManual(string m) { _manualMsg = m; _manualExpiry = DateTime.Now.AddSeconds(20); }
        private static async Task Loop(CancellationToken t) { while (!t.IsCancellationRequested) { if (Program.Config.ChatboxEnabled) try { await Update(); } catch { } await Task.Delay(1000, t); } }
        private static async Task Update()
        {
            var cfg = Program.Config;
            if (DateTime.Now < _manualExpiry) { EngineState = "Manual"; SendOsc("/chatbox/input", $"💬 {_manualMsg}"); return; }
            if ((DateTime.Now - _lastRefresh).TotalSeconds >= cfg.Interval) {
                EngineState = "Refresh"; _music = FetchMusic();
                if (cfg.WeatherMode && !string.IsNullOrEmpty(cfg.City)) _weather = (await new HttpClient().GetStringAsync($"https://wttr.in/{cfg.City}?format=%C+%t")).Trim();
                lock (ListLock) { if (cfg.AutoCycleStatus && cfg.StatusList.Count > 0) _statusIdx = (_statusIdx + 1) % cfg.StatusList.Count; }
                _lastRefresh = DateTime.Now;
            }
            if ((DateTime.Now - _lastSent).TotalSeconds < Math.Max(cfg.Interval, 1.5)) return;
            var lines = new List<string>();
            lock (ListLock) { if (cfg.StatusTextMode && cfg.StatusList.Count > _statusIdx) { if (_statusIdx >= cfg.StatusList.Count) _statusIdx = 0; lines.Add(cfg.StatusList[_statusIdx]); } }
            if (cfg.PronounsMode && !string.IsNullOrEmpty(cfg.Pronouns)) lines.Add($"{cfg.StatusIcon} {cfg.Pronouns}");
            var env = new List<string>();
            if (cfg.TimeMode) env.Add($"🕒 {DateTime.Now:hh:mm tt}");
            if (cfg.DistroMode) env.Add($"| 🐧 Fedora");
            if (cfg.WeatherMode) env.Add($"| 🌤️ {_weather}");
            if (env.Count > 0) lines.Add(string.Join(" ", env));
            if (cfg.PcMode) {
                var g = HardwareService.GetGpuStats(cfg.GpuUnit, cfg.VramUnit, cfg.TempUnit);
                string cStat = cfg.CpuUnit == "Watt" ? "--W" : HardwareService.GetCpuLoad() + "%";
                if (cfg.CpuTempOn) cStat += $" ({HardwareService.GetCpuTemp(cfg.TempUnit)})";
                string cpuL = cfg.CustomCpuNameOn ? cfg.CustomCpuName : (cfg.HwNameMode ? Stylize(_cpu) : "CPU");
                string gpuL = cfg.CustomGpuNameOn ? cfg.CustomGpuName : (cfg.HwNameMode ? Stylize(_gpu) : "GPU");
                lines.Add($"🖥️ {cpuL}: {cStat} | 🎮 {gpuL}: {g.Load} ({g.Temp})");
                if (cfg.ShowRam || cfg.ShowVram) {
                    string m = "";
                    if (cfg.ShowRam) m += $"🐏 ʳᵃᵐ: {HardwareService.GetRamUsage(cfg.RamUnit)}";
                    if (cfg.ShowVram) m += (m == "" ? "" : " | ") + $"🎞️ ᵛʳᵃᵐ: {g.Vram}";
                    lines.Add(m);
                }
            }
            if (cfg.NetMode) lines.Add($"🌐 {new System.Net.NetworkInformation.Ping().Send("1.1.1.1", 300).RoundtripTime}ms");
            if (cfg.SongMode && _music != "Chilling") lines.Add($"♪ {_music}");
            string output = string.Join("\n", lines);
            if (cfg.ThinMode) { if (output.Length > 138) output = output.Substring(0, 138); output += "\u0003\u001f"; }
            SendOsc("/chatbox/input", output);
            _lastSent = DateTime.Now; PacketsSent++; EngineState = "Idle";
        }
        private static void SendOsc(string addr, string text) {
            try { List<byte> p = new(); void Add(string s) { byte[] b = Encoding.UTF8.GetBytes(s); p.AddRange(b); p.Add(0); while (p.Count % 4 != 0) p.Add(0); }
                Add(addr); Add(",sTT"); Add(text); _client.Send(p.ToArray(), p.Count, "127.0.0.1", 9000);
            } catch { }
        }
        private static string FetchMusic() {
            try {
                var psi = new ProcessStartInfo("playerctl", "metadata --format \"{{artist}} - {{title}}\"") { RedirectStandardOutput = true, UseShellExecute = false };
                using var p = Process.Start(psi); string r = p?.StandardOutput.ReadToEnd().Trim() ?? "";
                if (!string.IsNullOrEmpty(r) && r != " - ") return r;
                var x = new ProcessStartInfo("xdotool", "search --name \" - \"") { RedirectStandardOutput = true, UseShellExecute = false };
                using var px = Process.Start(x); string[] ids = px?.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                foreach (var id in ids) {
                    var xn = new ProcessStartInfo("xdotool", $"getwindowname {id}") { RedirectStandardOutput = true, UseShellExecute = false };
                    using var pn = Process.Start(xn); string t = pn?.StandardOutput.ReadToEnd().Trim() ?? "";
                    if (t.Contains("SoundCloud") || t.Contains("Spotify") || t.Contains("YouTube")) return Regex.Replace(t, @" - (SoundCloud|YouTube|Spotify).*", "").Trim();
                }
                return "Chilling";
            } catch { return "Chilling"; }
        }
        private static void ScrapeHardwareNames() {
            string p = "/home/soap/.local/share/Steam/steamapps/compatdata/438100/pfx/drive_c/users/steamuser/AppData/LocalLow/VRChat/VRChat";
            if (Directory.Exists(p)) {
                var log = Directory.GetFiles(p, "output_log_*.txt").OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
                if (log != null) foreach (var l in File.ReadLines(log).Take(1500)) {
                    if (l.Contains("Processor Type:")) _cpu = Regex.Replace(l.Split(": ")[1], @"(\s\d+-Core| Processor| @.*|AMD |Intel |Core |Ryzen )", "").Trim();
                    if (l.Contains("Graphics Device Name:")) _gpu = Regex.Replace(l.Split(": ")[1], @"(\(RADV.*| Graphics|AMD |NVIDIA |GeForce |Radeon |\sRX\s|\sXT)", "").Trim();
                }
            }
        }
        private static string Stylize(string t) {
            string n = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", s = "ᵃᵇᶜᵈᵉᶠᵍʰᶦʲᵏˡᵐⁿᵒᵖᵠʳˢᵗᵘᵛʷˣʸᶻᵃᵇᶜᵈᵉᶠᵍʰᶦʲᵏˡᵐⁿᵒᵖᵠʳˢᵗᵘᵛʷˣʸᶻ⁰¹²³⁴⁵⁶⁷⁸⁹";
            StringBuilder sb = new(); foreach (char c in t) { int i = n.IndexOf(c); sb.Append(i != -1 ? s[i] : c); }
            return sb.ToString();
        }
    }

    class Program
    {
        public const string AppVersion = "2.0.5-linux-rel";
        public static AppConfig Config = new();
        private static string _path = "/home/soap/xosc/config.json", _chatIn = "";
        private static Mutex? _mtx;
        private static int _navPage = 0;
        private static readonly string[] _navLabels = { "Dashboard", "Statuses", "Chatbox", "Hardware", "Network", "Updater" };
        private static readonly Vector4 ColAccent = new(0.38f, 0.73f, 1.00f, 1f), ColBg = new(0.10f, 0.10f, 0.13f, 1f), ColSidebar = new(0.07f, 0.07f, 0.09f, 1f), ColCard = new(0.14f, 0.14f, 0.18f, 1f);
        public static void Main()
        {
            _mtx = new Mutex(true, "XOSC_VRC_Unique_REL", out bool fresh); if (!fresh) Environment.Exit(0);
            Directory.CreateDirectory("/home/soap/xosc");
            LoadConfig(); MusicChatEngine.Init(); Raylib.InitWindow(960, 640, "XOSC"); rlImGui.Setup(true); Raylib.SetTargetFPS(60); ApplyTheme();
            while (!Raylib.WindowShouldClose()) { Raylib.BeginDrawing(); Raylib.ClearBackground(new Color(26, 26, 33, 255)); rlImGui.Begin(); DrawUI(); rlImGui.End(); Raylib.EndDrawing(); }
            SaveConfig(); Raylib.CloseWindow();
        }
        static void ApplyTheme() { var s = ImGui.GetStyle(); s.WindowRounding = 0; s.ChildRounding = 6; s.FrameRounding = 5; s.PopupRounding = 5; s.ScrollbarRounding = 5; s.GrabRounding = 4; s.TabRounding = 5; s.WindowPadding = new Vector2(12, 12); }
        static void DrawUI()
        {
            int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(sw, sh));
            ImGui.Begin("##root", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, ColSidebar);
            ImGui.BeginChild("##sidebar", new Vector2(172, sh), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
            ImGui.Dummy(new Vector2(0, 20)); ImGui.SetCursorPosX(20); ImGui.TextColored(ColAccent, "XOSC");
            ImGui.SetCursorPosX(20); ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.6f, 1f), $"v{AppVersion}"); ImGui.Dummy(new Vector2(0, 20));
            for (int i = 0; i < _navLabels.Length; i++) { if (ImGui.Selectable(_navLabels[i], _navPage == i)) _navPage = i; }
            ImGui.SetCursorPosY(sh - 50); ImGui.SetCursorPosX(20);
            ImGui.TextColored(Config.ChatboxEnabled ? new Vector4(0.35f, 0.95f, 0.55f, 1f) : new Vector4(1f, 0.33f, 0.33f, 1f), Config.ChatboxEnabled ? "● Live" : "● Paused");
            ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.ChildBg, ColBg);
            ImGui.BeginChild("##content", new Vector2(sw - 172, sh), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar); ImGui.Dummy(new Vector2(0, 24));
            switch (_navPage) {
                case 0: Card("Dashboard", () => {
                    ImGui.Text($"Engine: {MusicChatEngine.EngineState} | Packets: {MusicChatEngine.PacketsSent}");
                    ImGui.InputText("Override##field", ref _chatIn, 128); if (ImGui.Button("Send")) { MusicChatEngine.SetManual(_chatIn); _chatIn = ""; }
                }); break;
                case 1: Card("Statuses", () => {
                    int toRemove = -1; lock (MusicChatEngine.ListLock) {
                        for (int i = 0; i < Config.StatusList.Count; i++) {
                            string s = Config.StatusList[i]; ImGui.PushID(i);
                            if (ImGui.InputText("##s", ref s, 100)) Config.StatusList[i] = s;
                            ImGui.SameLine(); if (ImGui.Button("X")) toRemove = i;
                            ImGui.PopID();
                        }
                        if (toRemove != -1) { Config.StatusList.RemoveAt(toRemove); SaveConfig(); }
                    }
                    if (ImGui.Button("+ Add")) Config.StatusList.Add("New Status");
                }); break;
                case 2: Card("Chatbox", () => {
                    Toggle("Status Text", ref Config.StatusTextMode); Toggle("Pronouns", ref Config.PronounsMode);
                    Toggle("Song Mode", ref Config.SongMode); Toggle("Time", ref Config.TimeMode);
                    Toggle("Distro", ref Config.DistroMode); Toggle("Weather", ref Config.WeatherMode);
                    Toggle("Thin Mode", ref Config.ThinMode); Toggle("Auto-Cycle", ref Config.AutoCycleStatus);
                    ImGui.InputText("Pronouns##field", ref Config.Pronouns, 64);
                    ImGui.InputText("Weather City##field", ref Config.City, 64);
                    ImGui.SliderInt("Interval##slider", ref Config.Interval, 1, 60);
                }); break;
                case 3: Card("Hardware", () => {
                    Toggle("Show Stats", ref Config.PcMode); Toggle("Show RAM", ref Config.ShowRam);
                    Toggle("Show VRAM", ref Config.ShowVram); Toggle("Stylized Names", ref Config.HwNameMode);
                    Toggle("CPU Temp", ref Config.CpuTempOn); Toggle("GPU Temp", ref Config.GpuTempOn);
                    Toggle("Custom CPU Name", ref Config.CustomCpuNameOn); if (Config.CustomCpuNameOn) ImGui.InputText("##c_cpu", ref Config.CustomCpuName, 32);
                    Toggle("Custom GPU Name", ref Config.CustomGpuNameOn); if (Config.CustomGpuNameOn) ImGui.InputText("##c_gpu", ref Config.CustomGpuName, 32);
                }); break;
                case 4: Card("Network", () => { Toggle("Internet Ping", ref Config.NetMode); }); break;
                case 5: Card("Updater", () => {
                    ImGui.Checkbox("Beta Opt-in", ref Config.BetaOptIn);
                    if (ImGui.Button("Check for Update")) Task.Run(() => Updater.CheckForUpdates());
                    if (Updater.NewVersionFound && ImGui.Button("Apply Update")) Updater.ApplyUpdate();
                    ImGui.Text($"Status: {Updater.Status}");
                }); break;
            }
            ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.End();
        }
        static void Card(string t, Action d) {
            ImGui.SetCursorPosX(24); ImGui.TextColored(new Vector4(0.92f, 0.92f, 0.97f, 1f), t); ImGui.Dummy(new Vector2(0, 8));
            ImGui.SetCursorPosX(24); ImGui.PushStyleColor(ImGuiCol.ChildBg, ColCard);
            ImGui.BeginChild($"##c{t}", new Vector2(ImGui.GetContentRegionAvail().X - 48, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY);
            ImGui.Dummy(new Vector2(0, 10)); d(); ImGui.Dummy(new Vector2(0, 10)); ImGui.EndChild(); ImGui.PopStyleColor();
        }
        static void Toggle(string l, ref bool v) { if (ImGui.Checkbox(l, ref v)) SaveConfig(); }
        public static void SaveConfig() { try { var options = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }; File.WriteAllText(_path, JsonSerializer.Serialize(Config, options)); } catch { } }
        static void LoadConfig() { if (!File.Exists(_path)) return; try { var options = new JsonSerializerOptions { IncludeFields = true }; var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_path), options); if (loaded != null) Config = loaded; } catch { } }
    }
}