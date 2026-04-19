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
        public string Version = "2.0.1-linux";
        public bool ChatboxEnabled = true;
        public int Interval = 5;
        public string City = "Springfield,IL";
        public string Pronouns = "she/her + bi/trans";
        public string StatusIcon = "💬";
        public bool ThinMode = false;
        public bool PcMode = true;
        public bool ShowRam = true;
        public bool ShowVram = true;
        public bool DistroMode = true;
        public bool WeatherMode = true;
        public bool TimeMode = true;
        public bool SongMode = true;
        public bool NetMode = true;
        public bool VrcPingMode = false;
        public bool HwNameMode = true;
        public bool CustomCpuNameOn = false;
        public bool CustomGpuNameOn = false;
        public string CustomCpuName = "CPU";
        public string CustomGpuName = "GPU";
        public bool StatusTextMode = true;
        public bool PronounsMode = true;
        public bool CpuTempOn = true;
        public bool GpuTempOn = true;
        public string CpuUnit = "%";
        public string RamUnit = "GB";
        public string GpuUnit = "%";
        public string VramUnit = "GB";
        public string TempUnit = "°C";
        public List<string> StatusList = new() { "Just vibing", "Gaming", "AFK" };
        public bool AutoCycleStatus = true;
        public string PublishPath = "https://github.com/hollyntt/XOSC/raw/refs/heads/master/publish/linux-x64/XOSC";
        public string Cookie = "";
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
                return Math.Round(100.0 * (1.0 - (double)dIdle / dTotal), 0).ToString();
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
                        if (!File.Exists($"{dir}/name")) continue;
                        string name = File.ReadAllText($"{dir}/name");
                        if (name.Contains("k10temp") || name.Contains("coretemp"))
                        {
                            if (File.Exists($"{dir}/temp1_input")) { path = $"{dir}/temp1_input"; break; }
                        }
                    }
                }
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
                    var psi = new ProcessStartInfo("nvidia-smi", "--query-gpu=utilization.gpu,memory.used,memory.total,temperature.gpu --format=csv,noheader,nounits") { RedirectStandardOutput = true, UseShellExecute = false };
                    using var p = Process.Start(psi);
                    var vals = p?.StandardOutput.ReadToEnd().Trim().Split(',');
                    if (vals?.Length >= 4)
                    {
                        string load = vals[0].Trim() + "%";
                        long used = long.Parse(vals[1].Trim());
                        long total = long.Parse(vals[2].Trim());
                        string vram = vUnit == "GB" ? $"{Math.Round(used / 1024.0, 1)}/{Math.Round(total / 1024.0, 1)}GB" : $"{Math.Round(100.0 * used / total, 0)}%";
                        int c = int.Parse(vals[3].Trim());
                        string temp = tUnit == "°C" ? $"{c}°C" : $"{(c * 9 / 5) + 32}°F";
                        return (load, vram, temp);
                    }
                } catch { }
            }

            try
            {
                string gpu = Directory.GetDirectories("/sys/class/drm/").Where(d => d.Contains("card")).OrderByDescending(d => File.Exists($"{d}/device/mem_info_vram_total") ? long.Parse(File.ReadAllText($"{d}/device/mem_info_vram_total").Trim()) : 0).FirstOrDefault() ?? "";
                if (string.IsNullOrEmpty(gpu)) return ("--", "--", "--");

                string load = File.Exists($"{gpu}/device/gpu_busy_percent") ? File.ReadAllText($"{gpu}/device/gpu_busy_percent").Trim() + "%" : "--%";
                long used = long.Parse(File.ReadAllText($"{gpu}/device/mem_info_vram_used").Trim());
                long total = long.Parse(File.ReadAllText($"{gpu}/device/mem_info_vram_total").Trim());
                string vram = vUnit == "GB" ? $"{Math.Round(used / 1073741824.0, 1)}/{Math.Round(total / 1073741824.0, 1)}GB" : $"{Math.Round(100.0 * used / total, 0)}%";

                string temp = "--";
                string hwmon = $"{gpu}/device/hwmon/";
                if (Directory.Exists(hwmon))
                {
                    var dirs = Directory.GetDirectories(hwmon);
                    if (dirs.Length > 0 && File.Exists($"{dirs[0]}/temp1_input"))
                    {
                        int c = int.Parse(File.ReadAllText($"{dirs[0]}/temp1_input").Trim()) / 1000;
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
                return unit == "GB" ? $"{Math.Round((total - avail) / 1048576.0, 1)}/{Math.Round(total / 1048576.0, 1)}GB" : $"{Math.Round(100.0 * (total - avail) / total, 0)}%";
            }
            catch { return "--"; }
        }
    }

    public static class VrchatService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
        public static string CurrentWorldPing = "--";
        public static string CurrentWorldName = "";
        private static DateTime _lastPing = DateTime.MinValue;

        public static async Task FetchWorldPing()
        {
            if ((DateTime.Now - _lastPing).TotalSeconds < 30) return;
            _lastPing = DateTime.Now;
            try
            {
                if (string.IsNullOrEmpty(Program.Config.Cookie)) return;
                var req = new HttpRequestMessage(HttpMethod.Get, "https://api.vrchat.cloud/api/1/auth/user");
                req.Headers.Add("Cookie", Program.Config.Cookie);
                req.Headers.Add("User-Agent", "XOSC/2.0");
                var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return;

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                string loc = doc.RootElement.GetProperty("location").GetString() ?? "";
                if (string.IsNullOrEmpty(loc) || loc == "offline") return;

                string worldId = loc.Split(':')[0];
                var sw = Stopwatch.StartNew();
                var req2 = new HttpRequestMessage(HttpMethod.Get, $"https://api.vrchat.cloud/api/1/worlds/{worldId}");
                req2.Headers.Add("Cookie", Program.Config.Cookie);
                var resp2 = await _http.SendAsync(req2);
                sw.Stop();

                if (resp2.IsSuccessStatusCode)
                {
                    using var doc2 = JsonDocument.Parse(await resp2.Content.ReadAsStringAsync());
                    CurrentWorldName = doc2.RootElement.GetProperty("name").GetString() ?? "";
                    CurrentWorldPing = $"{sw.ElapsedMilliseconds}ms";
                }
            } catch { }
        }
    }

    public static class Updater
    {
        public static string Status = "idle";
        public static async Task CheckAndApply(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) { Status = "invalid url"; return; }
            try
            {
                Status = "checking GitHub...";
                using var http = new HttpClient();
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) { Status = "failed to fetch remote binary"; return; }

                string self = Environment.ProcessPath ?? "";
                var remoteBytes = await resp.Content.ReadAsByteArrayAsync();
                var localBytes = File.ReadAllBytes(self);

                if (remoteBytes.Length == localBytes.Length && remoteBytes.SequenceEqual(localBytes))
                {
                    Status = "already up to date";
                    return;
                }

                Status = "update found, applying...";
                File.Move(self, self + ".bak", true);
                File.WriteAllBytes(self, remoteBytes);
                
                Status = "restarting...";
                Thread.Sleep(500);
                Process.Start(new ProcessStartInfo(self) { UseShellExecute = true });
                Environment.Exit(0);
            } catch (Exception e) { Status = $"error: {e.Message}"; }
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
        public static string EngineState = "Init";

        public static void Init()
        {
            _client = new UdpClient();
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            ScrapeHardwareNames();
            Task.Run(() => Loop(_cts.Token));
        }

        public static void SetManual(string m) { _manualMsg = m; _manualExpiry = DateTime.Now.AddSeconds(20); }

        private static async Task Loop(CancellationToken t)
        {
            while (!t.IsCancellationRequested)
            {
                if (Program.Config.ChatboxEnabled) try { await Update(); } catch { }
                await Task.Delay(1000, t);
            }
        }

        private static async Task Update()
        {
            var cfg = Program.Config;
            if (DateTime.Now < _manualExpiry) { EngineState = "Manual"; SendOsc("/chatbox/input", $"💬 {_manualMsg}"); return; }

            if ((DateTime.Now - _lastRefresh).TotalSeconds >= cfg.Interval)
            {
                EngineState = "Refresh";
                _music = FetchMusic();
                if (cfg.WeatherMode) _weather = (await new HttpClient().GetStringAsync($"https://wttr.in/{cfg.City}?format=%C+%t")).Trim();
                if (cfg.VrcPingMode) await VrchatService.FetchWorldPing();
                if (cfg.AutoCycleStatus && cfg.StatusList.Count > 0) _statusIdx = (_statusIdx + 1) % cfg.StatusList.Count;
                _lastRefresh = DateTime.Now;
            }

            if ((DateTime.Now - _lastSent).TotalSeconds < Math.Max(cfg.Interval, 2)) return;

            var lines = new List<string>();
            if (cfg.StatusTextMode && cfg.StatusList.Count > _statusIdx) lines.Add(cfg.StatusList[_statusIdx]);
            if (cfg.PronounsMode) lines.Add($"{cfg.StatusIcon} {cfg.Pronouns}");

            var env = new List<string>();
            if (cfg.TimeMode) env.Add($"🕒 {DateTime.Now:hh:mm tt}");
            if (cfg.DistroMode) env.Add($"🐧 {GetDistro()}");
            if (cfg.WeatherMode) env.Add($"🌤️ {_weather}");
            if (env.Count > 0) lines.Add(string.Join(" | ", env));

            if (cfg.PcMode)
            {
                var g = HardwareService.GetGpuStats(cfg.GpuUnit, cfg.VramUnit, cfg.TempUnit);
                string cStat = cfg.CpuUnit == "Watt" ? "--W" : HardwareService.GetCpuLoad() + "%";
                if (cfg.CpuTempOn) cStat += $" ({HardwareService.GetCpuTemp(cfg.TempUnit)})";
                string cpuL = cfg.CustomCpuNameOn ? cfg.CustomCpuName : (cfg.HwNameMode ? Stylize(_cpu) : "CPU");
                string gpuL = cfg.CustomGpuNameOn ? cfg.CustomGpuName : (cfg.HwNameMode ? Stylize(_gpu) : "GPU");
                lines.Add($"🖥️ {cpuL}: {cStat} | 🎮 {gpuL}: {g.Load} ({g.Temp})");
                var mem = new List<string>();
                if (cfg.ShowRam) mem.Add($"🐏 ʳᵃᵐ: {HardwareService.GetRamUsage(cfg.RamUnit)}");
                if (cfg.ShowVram) mem.Add($"🎞️ ᵛʳᵃᵐ: {g.Vram}");
                if (mem.Count > 0) lines.Add(string.Join(" | ", mem));
            }

            var net = new List<string>();
            if (cfg.NetMode) net.Add($"🌐 {GetPing()}ms");
            if (cfg.VrcPingMode) net.Add($"🌍 {VrchatService.CurrentWorldPing}");
            if (net.Count > 0) lines.Add(string.Join(" | ", net));

            if (cfg.SongMode && _music != "Chilling") lines.Add($"♪ {_music}");

            string output = string.Join("\n", lines);
            if (cfg.ThinMode) output += "\u0003\u001f";

            SendOsc("/chatbox/input", output);
            _lastSent = DateTime.Now;
            PacketsSent++;
            EngineState = "Idle";
        }

        private static void SendOsc(string addr, string text)
        {
            try
            {
                List<byte> p = new();
                void Add(string s) { byte[] b = Encoding.UTF8.GetBytes(s); p.AddRange(b); p.Add(0); while (p.Count % 4 != 0) p.Add(0); }
                Add(addr); Add(",sTT"); Add(text);
                _client.Send(p.ToArray(), p.Count, "127.0.0.1", 9000);
            } catch { }
        }

        private static string GetPing() { try { return new System.Net.NetworkInformation.Ping().Send("1.1.1.1", 300).RoundtripTime.ToString(); } catch { return "--"; } }
        private static string GetDistro() { try { return File.ReadLines("/etc/os-release").First(l => l.StartsWith("NAME=")).Split('"')[1].Split(' ')[0]; } catch { return "Linux"; } }

        private static string FetchMusic()
        {
            try
            {
                var psi = new ProcessStartInfo("playerctl", "metadata --format \"{{artist}} - {{title}}\"") { RedirectStandardOutput = true, UseShellExecute = false };
                using var p = Process.Start(psi);
                string r = p?.StandardOutput.ReadToEnd().Trim() ?? "";
                if (!string.IsNullOrEmpty(r) && r != " - ") return r;
                var x = new ProcessStartInfo("xdotool", "search --name \" - \"") { RedirectStandardOutput = true, UseShellExecute = false };
                using var px = Process.Start(x);
                string[] ids = px?.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                foreach (var id in ids)
                {
                    var xn = new ProcessStartInfo("xdotool", $"getwindowname {id}") { RedirectStandardOutput = true, UseShellExecute = false };
                    using var pn = Process.Start(xn);
                    string t = pn?.StandardOutput.ReadToEnd().Trim() ?? "";
                    if (t.Contains("SoundCloud") || t.Contains("Spotify") || t.Contains("YouTube")) return Regex.Replace(t, @" - (SoundCloud|YouTube|Spotify).*", "").Trim();
                }
                return "Chilling";
            } catch { return "Chilling"; }
        }

        private static void ScrapeHardwareNames()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string p = Path.Combine(home, ".local/share/Steam/steamapps/compatdata/438100/pfx/drive_c/users/steamuser/AppData/LocalLow/VRChat/VRChat");
            if (Directory.Exists(p))
            {
                var log = Directory.GetFiles(p, "output_log_*.txt").OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
                if (log != null) foreach (var l in File.ReadLines(log).Take(1500))
                    {
                        if (l.Contains("Processor Type:")) _cpu = Regex.Replace(l.Split(": ")[1], @"(\s\d+-Core| Processor| @.*|AMD |Intel |Core |Ryzen )", "").Trim();
                        if (l.Contains("Graphics Device Name:")) _gpu = Regex.Replace(l.Split(": ")[1], @"(\(RADV.*| Graphics|AMD |NVIDIA |GeForce |Radeon |\sRX\s|\sXT)", "").Trim();
                    }
            }
        }

        private static string Stylize(string t) {
            string n = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", s = "ᵃᵇᶜᵈᵉᶠᵍʰᶦʲᵏˡᵐⁿᵒᵖᵠʳˢᵗᵘᵛʷˣʸᶻᵃᵇᶜᵈᵉᶠᵍʰᶦʲᵏˡᵐⁿᵒᵖᵠʳˢᵗᵘᵛʷˣʸᶻ⁰¹²³⁴⁵⁶⁷⁸⁹";
            StringBuilder sb = new();
            foreach (char c in t) { int i = n.IndexOf(c); sb.Append(i != -1 ? s[i] : c); }
            return sb.ToString();
        }
    }

    class Program
    {
        public static AppConfig Config = new();
        private static string _path = Path.Combine("/home/soap/xosc", "config.json"), _chatIn = "";
        private static Mutex? _mtx;
        private static int _page = 0;

        public static void Main()
        {
            _mtx = new Mutex(true, "XOSC_VRC_Unique", out bool fresh);
            if (!fresh) Environment.Exit(0);
            Directory.CreateDirectory("/home/soap/xosc");
            if (File.Exists(_path)) Config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_path)) ?? new();
            MusicChatEngine.Init();
            Raylib.InitWindow(960, 640, "XOSC");
            rlImGui.Setup(true);
            Raylib.SetTargetFPS(60);
            while (!Raylib.WindowShouldClose()) { Raylib.BeginDrawing(); Raylib.ClearBackground(new Color(20, 20, 25, 255)); rlImGui.Begin(); DrawUI(); rlImGui.End(); Raylib.EndDrawing(); }
            File.WriteAllText(_path, JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true }));
            Raylib.CloseWindow();
        }

        static void DrawUI()
        {
            int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(sw, sh));
            ImGui.Begin("##root", ImGuiWindowFlags.NoDecoration);

            ImGui.BeginChild("##side", new Vector2(180, sh), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
            string[] nav = { "Dashboard", "Statuses", "Toggles", "Hardware", "Settings", "Updater" };
            for (int i = 0; i < nav.Length; i++) if (ImGui.Selectable(nav[i], _page == i)) _page = i;
            ImGui.EndChild();

            ImGui.SameLine();
            ImGui.BeginChild("##content", new Vector2(sw - 200, sh));
            if (_page == 0)
            {
                ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), $"Engine: {MusicChatEngine.EngineState} | Packets: {MusicChatEngine.PacketsSent}");
                ImGui.InputText("Chat", ref _chatIn, 128); if (ImGui.Button("Send")) { MusicChatEngine.SetManual(_chatIn); _chatIn = ""; }
            }
            if (_page == 1)
            {
                for (int i = 0; i < Config.StatusList.Count; i++) {
                    string s = Config.StatusList[i]; ImGui.PushID(i);
                    if (ImGui.InputText("##s", ref s, 100)) Config.StatusList[i] = s;
                    ImGui.SameLine(); if (ImGui.Button("X")) { Config.StatusList.RemoveAt(i); break; }
                    ImGui.PopID();
                }
                if (ImGui.Button("+ Add")) Config.StatusList.Add("New Status");
            }
            if (_page == 2)
            {
                ImGui.Checkbox("Status Text", ref Config.StatusTextMode); ImGui.Checkbox("Pronouns", ref Config.PronounsMode);
                ImGui.Checkbox("PC Mode", ref Config.PcMode); ImGui.Checkbox("Distro", ref Config.DistroMode);
                ImGui.Checkbox("Weather", ref Config.WeatherMode); ImGui.Checkbox("Song", ref Config.SongMode);
                ImGui.Checkbox("Internet Ping", ref Config.NetMode); ImGui.Checkbox("VRC World Ping", ref Config.VrcPingMode);
                ImGui.Checkbox("Thin Mode", ref Config.ThinMode); ImGui.Checkbox("Auto-Cycle", ref Config.AutoCycleStatus);
            }
            if (_page == 3)
            {
                ImGui.Checkbox("Show RAM", ref Config.ShowRam); ImGui.Checkbox("Show VRAM", ref Config.ShowVram);
                ImGui.Checkbox("CPU Temp", ref Config.CpuTempOn); ImGui.Checkbox("GPU Temp", ref Config.GpuTempOn);
                ImGui.Checkbox("Stylized Names", ref Config.HwNameMode);
                ImGui.Checkbox("Custom CPU Label", ref Config.CustomCpuNameOn); if (Config.CustomCpuNameOn) ImGui.InputText("CPU Label", ref Config.CustomCpuName, 32);
                ImGui.Checkbox("Custom GPU Label", ref Config.CustomGpuNameOn); if (Config.CustomGpuNameOn) ImGui.InputText("GPU Label", ref Config.CustomGpuName, 32);
            }
            if (_page == 4)
            {
                ImGui.InputText("Pronouns", ref Config.Pronouns, 64); ImGui.InputText("Icon", ref Config.StatusIcon, 12);
                ImGui.InputText("City", ref Config.City, 64); ImGui.SliderInt("Interval", ref Config.Interval, 1, 60);
                if (ImGui.Button("Restart Engine")) MusicChatEngine.Init();
            }
            if (_page == 5)
            {
                ImGui.InputText("Path", ref Config.PublishPath, 512);
                if (ImGui.Button("Update")) Task.Run(() => Updater.CheckAndApply(Config.PublishPath));
                ImGui.Text($"Status: {Updater.Status}");
            }
            ImGui.EndChild();
            ImGui.End();
        }
    }
}