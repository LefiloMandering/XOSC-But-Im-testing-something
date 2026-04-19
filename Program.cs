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
        public string Version = "2.0.0-linux";
        public bool ChatboxEnabled = true;
        public int Interval = 5;
        public string City = "Springfield,IL";
        public string Pronouns = "she/her + bi/trans";
        public string StatusIcon = "💬";

        public bool ThinMode = false;
        public bool PcMode = false;
        public bool ShowRam = true;
        public bool ShowVram = true;
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

        public string PublishPath = "";
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
                    { RedirectStandardOutput = true, UseShellExecute = false };
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

    public static class VrchatService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
        public static string CurrentWorldPing = "--";
        public static string CurrentWorldName = "";
        private static string _authCookie = "";
        private static DateTime _lastPing = DateTime.MinValue;

        public static void SetAuthCookie(string cookie) => _authCookie = cookie;

        public static async Task FetchWorldPing()
        {
            if ((DateTime.Now - _lastPing).TotalSeconds < 30) return;
            _lastPing = DateTime.Now;

            try
            {
                if (string.IsNullOrEmpty(_authCookie)) { CurrentWorldPing = "no auth"; return; }

                var req = new HttpRequestMessage(HttpMethod.Get, "https://api.vrchat.cloud/api/1/auth/user");
                req.Headers.Add("Cookie", _authCookie);
                req.Headers.Add("User-Agent", "XOSC/2.0");
                var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) { CurrentWorldPing = "auth fail"; return; }

                string body = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("location", out var loc)) { CurrentWorldPing = "offline"; return; }

                string location = loc.GetString() ?? "";
                if (string.IsNullOrEmpty(location) || location == "offline") { CurrentWorldPing = "offline"; return; }

                string worldId = location.Contains(':') ? location.Split(':')[0] : location;

                var t = Stopwatch.StartNew();
                var req2 = new HttpRequestMessage(HttpMethod.Get, $"https://api.vrchat.cloud/api/1/worlds/{worldId}");
                req2.Headers.Add("Cookie", _authCookie);
                req2.Headers.Add("User-Agent", "XOSC/2.0");
                var resp2 = await _http.SendAsync(req2);
                t.Stop();

                if (resp2.IsSuccessStatusCode)
                {
                    string body2 = await resp2.Content.ReadAsStringAsync();
                    using var doc2 = JsonDocument.Parse(body2);
                    if (doc2.RootElement.TryGetProperty("name", out var wn))
                        CurrentWorldName = wn.GetString() ?? "";
                }

                CurrentWorldPing = $"{t.ElapsedMilliseconds}ms";
            }
            catch
            {
                CurrentWorldPing = "err";
            }
        }
    }

    public static class Updater
    {
        public static string Status = "idle";

        public static void CheckAndApply(string publishPath)
        {
            if (string.IsNullOrWhiteSpace(publishPath) || !Directory.Exists(publishPath))
            {
                Status = "publish path not found";
                return;
            }

            try
            {
                string selfPath = Environment.ProcessPath ?? "";
                if (string.IsNullOrEmpty(selfPath)) { Status = "can't resolve self path"; return; }

                string exeName = Path.GetFileName(selfPath);
                string srcExe = Path.Combine(publishPath, exeName);

                if (!File.Exists(srcExe))
                {
                    string[] candidates = Directory.GetFiles(publishPath, "XOSC*", SearchOption.TopDirectoryOnly)
                        .Where(f => !f.EndsWith(".json") && !f.EndsWith(".pdb") && !f.EndsWith(".xml"))
                        .ToArray();
                    if (candidates.Length == 0) { Status = "no binary found in publish path"; return; }
                    srcExe = candidates[0];
                }

                var srcInfo = new FileInfo(srcExe);
                var selfInfo = new FileInfo(selfPath);

                if (srcInfo.LastWriteTimeUtc <= selfInfo.LastWriteTimeUtc)
                {
                    Status = "already up to date";
                    return;
                }

                Status = "update found, applying...";

                string backup = selfPath + ".bak";
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(selfPath, backup);
                File.Copy(srcExe, selfPath);
                new FileInfo(selfPath).Attributes &= ~FileAttributes.ReadOnly;

                string destDir = Path.GetDirectoryName(selfPath)!;
                foreach (var f in Directory.GetFiles(publishPath))
                {
                    if (f == srcExe) continue;
                    try { File.Copy(f, Path.Combine(destDir, Path.GetFileName(f)), true); } catch { }
                }

                Status = "updated! restarting...";
                Thread.Sleep(800);
                Process.Start(new ProcessStartInfo(selfPath) { UseShellExecute = true });
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Status = $"update error: {ex.Message}";
            }
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
        private static DateTime _lastSentTime = DateTime.MinValue;

        public static string LastError = "None";
        public static int PacketsSent = 0;
        public static string EngineState = "Initialized";
        public static string LastSentText => _lastSentText;

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
                if (cfg.VrcPingMode) await VrchatService.FetchWorldPing();
                _lastRefresh = DateTime.Now;

                if (cfg.AutoCycleStatus && cfg.StatusList.Count > 0)
                    _statusIdx = (_statusIdx + 1) % cfg.StatusList.Count;
            }

            bool timeForSend = (DateTime.Now - _lastSentTime).TotalSeconds >= Math.Max(cfg.Interval, 2.0);
            if (!timeForSend) { EngineState = "Idle (respecting interval)"; return; }

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
                string cpuLabel = cfg.CustomCpuNameOn ? cfg.CustomCpuName : (cfg.HwNameMode ? Stylize(_cpuName) : "CPU");
                string gpuLabel = cfg.CustomGpuNameOn ? cfg.CustomGpuName : (cfg.HwNameMode ? Stylize(_gpuName) : "GPU");

                lines.Add($"🖥️ {cpuLabel} ─ {cStat}");
                lines.Add($"🎮 {gpuLabel} ─ {g.Load}" + (cfg.GpuTempOn ? $" ({g.Temp})" : ""));

                string memLine = "";
                if (cfg.ShowRam) memLine += $"🐏 ʳᵃᵐ: {HardwareService.GetRamUsage(cfg.RamUnit)}";
                if (cfg.ShowVram) memLine += (memLine == "" ? "" : " | ") + $"🎞️ ᵛʳᵃᵐ: {g.Vram}";
                if (!string.IsNullOrEmpty(memLine)) lines.Add(memLine);
            }

            if (cfg.NetMode) lines.Add($"🌐 Ping: {GetPing()}ms");
            if (cfg.VrcPingMode && !string.IsNullOrEmpty(VrchatService.CurrentWorldName))
                lines.Add($"🌍 {VrchatService.CurrentWorldName}: {VrchatService.CurrentWorldPing}");

            if (cfg.SongMode && _music != "Chilling")
                lines.Add($"♪ {_music}");

            string fullText = string.Join("\n", lines);

            if (cfg.ThinMode)
            {
                const int maxLen = 140;
                if (fullText.Length > maxLen) fullText = fullText.Substring(0, maxLen);
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
                AddOscString(address);
                AddOscString(",sTT");
                AddOscString(text);
                _client.Send(packet.ToArray(), packet.Count, new IPEndPoint(IPAddress.Loopback, 9000));
                LastError = "None";
            }
            catch (Exception ex) { LastError = "OSC Error: " + ex.Message; }
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
                    foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!line.StartsWith("XOSC_SEP")) continue;
                        var parts = line.Split("XOSC_SEP", StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 2) continue;
                        string status = parts[0].Trim();
                        string song = parts[1].Trim();
                        if (status == "Playing" && !string.IsNullOrWhiteSpace(song) && song != " - " && song != "-")
                            return song;
                    }
                }
                return "Chilling";
            }
            catch { return "Chilling"; }
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
                if (!File.Exists("/etc/os-release")) return "Linux";
                string name = "Linux", version = "";
                foreach (var line in File.ReadLines("/etc/os-release"))
                {
                    if (line.StartsWith("NAME=")) name = line.Substring(5).Trim('"', '\'').Replace("Linux", "").Trim();
                    else if (line.StartsWith("VERSION_ID=")) version = line.Substring(11).Trim('"', '\'');
                }
                string shortName = name + (string.IsNullOrEmpty(version) ? "" : " " + version);
                return shortName.Length > 25 ? shortName.Substring(0, 22) + "..." : shortName;
            }
            catch { return "Linux"; }
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
                    if (!Directory.Exists(p)) continue;
                    var log = Directory.GetFiles(p, "output_log_*.txt").OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
                    if (log == null) continue;
                    foreach (var l in File.ReadLines(log).Take(2000))
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
            catch { }
        }

        private static string Stylize(string t)
        {
            string n = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            string s = "ᵃᵇᶜᵈᵉᶠᵍʰᶦʲᵏˡᵐⁿᵒᵖᵠʳˢᵗᵘᵛʷˣʸᶻᵃᵇᶜᵈᵉᶠᵍʰᶦʲᵏˡᵐⁿᵒᵖᵠʳˢᵗᵘᵛʷˣʸᶻ⁰¹²³⁴⁵⁶⁷⁸⁹";
            StringBuilder sb = new();
            foreach (char c in t) { int i = n.IndexOf(c); sb.Append(i != -1 ? s[i] : c); }
            return sb.ToString();
        }
    }

    class Program
    {
        public static AppConfig Config = new();
        private static string _configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "XOSC");
        private static string _path = Path.Combine(_configDir, "config.json");
        private static string _chatIn = "";
        private static string _vrcCookie = "";
        private static Mutex? _mtx;

        private static int _navPage = 0;
        private static readonly string[] _navLabels = { "Dashboard", "Statuses", "Chatbox", "Hardware", "Network", "Updater" };

        private static readonly Vector4 ColAccent  = new(0.38f, 0.73f, 1.00f, 1f);
        private static readonly Vector4 ColGreen   = new(0.35f, 0.95f, 0.55f, 1f);
        private static readonly Vector4 ColYellow  = new(1.00f, 0.82f, 0.25f, 1f);
        private static readonly Vector4 ColRed     = new(1.00f, 0.33f, 0.33f, 1f);
        private static readonly Vector4 ColMuted   = new(0.52f, 0.52f, 0.62f, 1f);
        private static readonly Vector4 ColBg      = new(0.10f, 0.10f, 0.13f, 1f);
        private static readonly Vector4 ColSidebar = new(0.07f, 0.07f, 0.09f, 1f);
        private static readonly Vector4 ColCard    = new(0.14f, 0.14f, 0.18f, 1f);

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

            Raylib.InitWindow(960, 640, "XOSC");
            Raylib.SetWindowState(ConfigFlags.ResizableWindow);
            rlImGui.Setup(true);
            Raylib.SetTargetFPS(60);
            ApplyTheme();

            while (!Raylib.WindowShouldClose())
            {
                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Color(26, 26, 33, 255));
                rlImGui.Begin();
                DrawUI();
                rlImGui.End();
                Raylib.EndDrawing();
            }

            SaveConfig();
            rlImGui.Shutdown();
            Raylib.CloseWindow();
        }

        static void ApplyTheme()
        {
            var style = ImGui.GetStyle();
            style.WindowRounding    = 0;
            style.ChildRounding     = 6;
            style.FrameRounding     = 5;
            style.PopupRounding     = 5;
            style.ScrollbarRounding = 5;
            style.GrabRounding      = 4;
            style.TabRounding       = 5;
            style.WindowBorderSize  = 0;
            style.FrameBorderSize   = 0;
            style.ItemSpacing       = new Vector2(10, 8);
            style.FramePadding      = new Vector2(10, 6);
            style.WindowPadding     = new Vector2(12, 12);
            style.IndentSpacing     = 18;

            var c = ImGui.GetStyle().Colors;
            c[(int)ImGuiCol.WindowBg]         = ColBg;
            c[(int)ImGuiCol.ChildBg]          = ColCard;
            c[(int)ImGuiCol.PopupBg]          = new Vector4(0.12f, 0.12f, 0.16f, 1f);
            c[(int)ImGuiCol.Border]           = new Vector4(0.22f, 0.22f, 0.30f, 1f);
            c[(int)ImGuiCol.FrameBg]          = new Vector4(0.18f, 0.18f, 0.24f, 1f);
            c[(int)ImGuiCol.FrameBgHovered]   = new Vector4(0.22f, 0.22f, 0.30f, 1f);
            c[(int)ImGuiCol.FrameBgActive]    = new Vector4(0.26f, 0.26f, 0.36f, 1f);
            c[(int)ImGuiCol.TitleBgActive]    = new Vector4(0.08f, 0.08f, 0.10f, 1f);
            c[(int)ImGuiCol.CheckMark]        = ColAccent;
            c[(int)ImGuiCol.SliderGrab]       = ColAccent;
            c[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.55f, 0.85f, 1.00f, 1f);
            c[(int)ImGuiCol.Button]           = new Vector4(0.20f, 0.20f, 0.28f, 1f);
            c[(int)ImGuiCol.ButtonHovered]    = new Vector4(0.28f, 0.28f, 0.40f, 1f);
            c[(int)ImGuiCol.ButtonActive]     = new Vector4(0.38f, 0.73f, 1.00f, 0.8f);
            c[(int)ImGuiCol.Header]           = new Vector4(0.38f, 0.73f, 1.00f, 0.18f);
            c[(int)ImGuiCol.HeaderHovered]    = new Vector4(0.38f, 0.73f, 1.00f, 0.28f);
            c[(int)ImGuiCol.HeaderActive]     = new Vector4(0.38f, 0.73f, 1.00f, 0.40f);
            c[(int)ImGuiCol.Tab]              = new Vector4(0.12f, 0.12f, 0.16f, 1f);
            c[(int)ImGuiCol.TabHovered]       = new Vector4(0.38f, 0.73f, 1.00f, 0.3f);
            c[(int)ImGuiCol.TabSelected]      = new Vector4(0.20f, 0.20f, 0.28f, 1f);
            c[(int)ImGuiCol.ScrollbarBg]      = new Vector4(0.08f, 0.08f, 0.10f, 1f);
            c[(int)ImGuiCol.ScrollbarGrab]    = new Vector4(0.28f, 0.28f, 0.38f, 1f);
            c[(int)ImGuiCol.Separator]        = new Vector4(0.22f, 0.22f, 0.30f, 1f);
            c[(int)ImGuiCol.Text]             = new Vector4(0.90f, 0.90f, 0.95f, 1f);
            c[(int)ImGuiCol.TextDisabled]     = ColMuted;
        }

        static void DrawUI()
        {
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            const float sideW = 172f;
            float contentW = sw - sideW;

            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(new Vector2(sw, sh));
            ImGui.Begin("##root", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove);

            // Sidebar - fixed width, no scroll
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(new Vector2(sideW, sh));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, ColSidebar);
            ImGui.BeginChild("##sidebar", new Vector2(sideW, sh), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);

            ImGui.Dummy(new Vector2(0, 20));
            ImGui.SetCursorPosX(20);
            ImGui.TextColored(ColAccent, "XOSC");
            ImGui.SetCursorPosX(20);
            ImGui.TextColored(ColMuted, $"v{Config.Version}");
            ImGui.Dummy(new Vector2(0, 20));

            for (int i = 0; i < _navLabels.Length; i++)
            {
                bool active = _navPage == i;
                ImGui.PushStyleColor(ImGuiCol.Button, active ? new Vector4(0.38f, 0.73f, 1.00f, 0.13f) : Vector4.Zero);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.38f, 0.73f, 1.00f, 0.09f));
                ImGui.PushStyleColor(ImGuiCol.Text, active ? ColAccent : new Vector4(0.72f, 0.72f, 0.80f, 1f));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6);
                ImGui.SetCursorPosX(10);
                if (ImGui.Button(_navLabels[i], new Vector2(sideW - 20, 36)))
                    _navPage = i;
                ImGui.PopStyleColor(3);
                ImGui.PopStyleVar();
            }

            // Bottom status bar
            ImGui.SetCursorPosY(sh - 70);
            ImGui.SetCursorPosX(20);
            ImGui.TextColored(Config.ChatboxEnabled ? ColGreen : ColRed, Config.ChatboxEnabled ? "● Live" : "● Paused");

            ImGui.SetCursorPosX(20);
            if (ImGui.Button(Config.ChatboxEnabled ? "Disable" : "Enable", new Vector2(sideW - 40, 32)))
            {
                Config.ChatboxEnabled = !Config.ChatboxEnabled;
                SaveConfig();
            }

            ImGui.EndChild();
            ImGui.PopStyleColor();

            // Content area - fills remaining space, no scroll
            ImGui.SetNextWindowPos(new Vector2(sideW, 0));
            ImGui.SetNextWindowSize(new Vector2(contentW, sh));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, ColBg);
            ImGui.BeginChild("##content", new Vector2(contentW, sh), ImGuiChildFlags.None);

            ImGui.Dummy(new Vector2(0, 24));

            switch (_navPage)
            {
                case 0: DrawDashboard(contentW); break;
                case 1: DrawStatuses(contentW); break;
                case 2: DrawChatbox(contentW); break;
                case 3: DrawHardware(contentW); break;
                case 4: DrawNetwork(contentW); break;
                case 5: DrawUpdater(contentW); break;
            }

            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.End();
        }

        static void Card(string title, Action drawContent)
        {
            ImGui.SetCursorPosX(24);
            ImGui.TextColored(new Vector4(0.92f, 0.92f, 0.97f, 1f), title);
            ImGui.Dummy(new Vector2(0, 8));

            ImGui.SetCursorPosX(24);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, ColCard);
            ImGui.BeginChild($"##card{title}", new Vector2(ImGui.GetContentRegionAvail().X - 48, 0), ImGuiChildFlags.Borders);
            ImGui.Dummy(new Vector2(0, 10));
            drawContent();
            ImGui.Dummy(new Vector2(0, 10));
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(0, 12));
        }

        static bool Toggle(string label, ref bool val)
        {
            bool changed = ImGui.Checkbox(label, ref val);
            if (changed) SaveConfig();
            return changed;
        }

        static void DrawDashboard(float w)
        {
            Card("Dashboard", () =>
            {
                ImGui.Text($"Engine: {MusicChatEngine.EngineState}");
                ImGui.Text($"Packets sent: {MusicChatEngine.PacketsSent}");
                ImGui.Text($"Interval: {Config.Interval}s");
            });
        }

        static void DrawStatuses(float w)
        {
            Card("Statuses", () =>
            {
                Toggle("Show Status Text", ref Config.StatusTextMode);
                Toggle("Auto-Cycle", ref Config.AutoCycleStatus);
                // Status list editor can be added here later
            });
        }

        static void DrawChatbox(float w)
        {
            Card("Chatbox", () =>
            {
                Toggle("Song Mode", ref Config.SongMode);
                Toggle("Pronouns", ref Config.PronounsMode);
                Toggle("Time", ref Config.TimeMode);
                Toggle("Distro", ref Config.DistroMode);
                Toggle("Weather", ref Config.WeatherMode);
                Toggle("Thin Mode", ref Config.ThinMode);
            });
        }

        static void DrawHardware(float w)
        {
            Card("Hardware", () =>
            {
                Toggle("Show Hardware Stats", ref Config.PcMode);

                if (Config.PcMode)
                {
                    Toggle("Show RAM", ref Config.ShowRam);
                    Toggle("Show VRAM", ref Config.ShowVram);
                    Toggle("Show CPU Temp", ref Config.CpuTempOn);
                    Toggle("Show GPU Temp", ref Config.GpuTempOn);
                    Toggle("Use Stylized Names", ref Config.HwNameMode);
                    Toggle("Custom CPU Label", ref Config.CustomCpuNameOn);
                    if (Config.CustomCpuNameOn)
                        ImGui.InputText("CPU Label", ref Config.CustomCpuName, 32);
                    Toggle("Custom GPU Label", ref Config.CustomGpuNameOn);
                    if (Config.CustomGpuNameOn)
                        ImGui.InputText("GPU Label", ref Config.CustomGpuName, 32);
                }
            });
        }

        static void DrawNetwork(float w)
        {
            Card("Network", () =>
            {
                Toggle("Internet Ping", ref Config.NetMode);
                Toggle("VRChat World Ping", ref Config.VrcPingMode);
            });
        }

        static void DrawUpdater(float w)
        {
            Card("Updater", () =>
            {
                ImGui.InputText("Publish Path", ref Config.PublishPath, 512);
                if (ImGui.Button("Check & Apply Update"))
                    Task.Run(() => Updater.CheckAndApply(Config.PublishPath));
                ImGui.Text($"Status: {Updater.Status}");
            });
        }
    }
}