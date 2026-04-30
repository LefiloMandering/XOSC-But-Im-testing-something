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
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;
using ImGuiNET;
using rlImGui_cs;

namespace XOSC
{
    public class StatusItem { public string Text { get; set; } = "New Status"; public bool IsFavorited { get; set; } = false; }
    
    public static class ThemePresets
    {
        public record Preset(string Name, float[] Accent, float[] Bg, float[] Sidebar, float[] Card);

        public static readonly Preset[] All =
        {
            new("XOSC Default", new[]{ 0.38f, 0.73f, 1.00f }, new[]{ 0.10f, 0.10f, 0.13f }, new[]{ 0.07f, 0.07f, 0.09f }, new[]{ 0.14f, 0.14f, 0.18f }),
            new("Midnight Purple", new[]{ 0.70f, 0.40f, 1.00f }, new[]{ 0.08f, 0.06f, 0.12f }, new[]{ 0.05f, 0.04f, 0.09f }, new[]{ 0.12f, 0.10f, 0.18f }),
            new("Forest Green", new[]{ 0.30f, 0.85f, 0.50f }, new[]{ 0.06f, 0.11f, 0.08f }, new[]{ 0.04f, 0.08f, 0.05f }, new[]{ 0.09f, 0.15f, 0.11f }),
            new("Sunset Orange", new[]{ 1.00f, 0.55f, 0.20f }, new[]{ 0.13f, 0.09f, 0.07f }, new[]{ 0.09f, 0.06f, 0.05f }, new[]{ 0.18f, 0.12f, 0.09f }),
            new("Rose Pink", new[]{ 1.00f, 0.45f, 0.65f }, new[]{ 0.13f, 0.08f, 0.10f }, new[]{ 0.09f, 0.05f, 0.07f }, new[]{ 0.18f, 0.11f, 0.14f }),
            new("Ice White", new[]{ 0.10f, 0.45f, 0.90f }, new[]{ 0.94f, 0.95f, 0.97f }, new[]{ 0.84f, 0.86f, 0.90f }, new[]{ 0.99f, 0.99f, 1.00f }),
            new("Deep Red", new[]{ 1.00f, 0.25f, 0.25f }, new[]{ 0.10f, 0.06f, 0.06f }, new[]{ 0.07f, 0.04f, 0.04f }, new[]{ 0.15f, 0.09f, 0.09f }),
            new("Cyberpunk", new[]{ 1.00f, 0.95f, 0.00f }, new[]{ 0.05f, 0.05f, 0.07f }, new[]{ 0.03f, 0.03f, 0.05f }, new[]{ 0.09f, 0.09f, 0.12f })
        };
    }

    public class AppConfig
    {
        public bool ChatboxEnabled = true;
        public int Interval = 3;
        public string City = "Springfield";
        public string CustomCity = "";
        public string State = "Illinois";
        public string CustomState = "";
        public string Country = "United States";
        public string CustomCountry = "";
        public string Pronouns = "They/Them";
        public string CustomPronouns = "";
        public string StatusIcon = "⭐";
        public bool ThinMode = false;
        public bool PcMode = false;
        public bool ShowRam = false;
        public bool ShowVram = false;
        public bool DistroMode = false;
        public bool WeatherMode = false;
        public bool WeatherTempMode = true;
        public string WeatherTempUnit = "°F";
        public bool WeatherAlertMode = true;
        public bool TimeMode = false;
        public bool MilitaryTime = false;
        public bool SongMode = true;
        public bool SongProgressMode = false;
        public bool AudioVisualizerMode = false;
        public bool AfkDetectionMode = false;
        public bool VrBatteryMode = false;
        public bool EasMode = false;
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
        public bool StylizeTextMode = false;
        public List<StatusItem> StatusList { get; set; } = new();
        public float TabRounding { get; set; }

        public bool AutoCycleStatus = false;
        public string PublishPath = "https://github.com/hollyntt/XOSC/raw/refs/heads/master/publish/XOSC.zip";
        public bool BetaOptIn = false;
        public string Cookie = "";
        public string SavedVersion = "";
        public string OscIP = "127.0.0.1";
        public int OscPort = 9000;
        public float[] AccentColor = { 0.38f, 0.73f, 1.00f };
        public float[] BgColor = { 0.10f, 0.10f, 0.13f };
        public float[] SidebarColor = { 0.07f, 0.07f, 0.09f };
        public float[] CardColor = { 0.14f, 0.14f, 0.18f };
        public float SidebarWidth = 172f;
        public float FontScale = 1.0f;
        public float WindowRounding = 0f;
        public float ChildRounding = 6f;
        public float FrameRounding = 5f;
    }

    public static class HardwareService
    {
        private static long _lastTotal, _lastIdle;
        private static bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        public static string GetCpuLoad() { if (!IsLinux) return "??%"; try { if (!File.Exists("/proc/stat")) return "--"; var parts = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries); long idle = long.Parse(parts[4]), total = parts.Skip(1).Select(long.Parse).Sum(); long dIdle = idle - _lastIdle, dTotal = total - _lastTotal; _lastIdle = idle; _lastTotal = total; return dTotal == 0 ? "0%" : Math.Round(100.0 * (1.0 - (double)dIdle / dTotal), 0) + "%"; } catch { return "--"; } }
        public static string GetCpuTemp(string unit) { if (!IsLinux) return "--"; try { string path = ""; if (Directory.Exists("/sys/class/hwmon/")) { foreach (var dir in Directory.GetDirectories("/sys/class/hwmon/")) { if (!File.Exists($"{dir}/name")) continue; string n = File.ReadAllText($"{dir}/name"); if (n.Contains("k10temp") || n.Contains("coretemp")) { if (File.Exists($"{dir}/temp1_input")) { path = $"{dir}/temp1_input"; break; } } } } if (string.IsNullOrEmpty(path)) return "--"; int c = int.Parse(File.ReadAllText(path).Trim()) / 1000; return unit == "°C" ? $"{c}°C" : $"{(c * 9 / 5) + 32}°F"; } catch { return "--"; } }
        public static (string Load, string Vram, string Temp) GetGpuStats(string gUnit, string vUnit, string tUnit) { string smi = ""; if (IsLinux) { smi = "/usr/bin/nvidia-smi"; } else { smi = "C:\\Windows\\System32\\nvidia-smi.exe"; if (!File.Exists(smi)) smi = "C:\\Program Files\\NVIDIA Corporation\\NVSMI\\nvidia-smi.exe"; } if (File.Exists(smi)) { try { var psi = new ProcessStartInfo(smi, "--query-gpu=utilization.gpu,memory.used,memory.total,temperature.gpu --format=csv,noheader,nounits") { RedirectStandardOutput = true, UseShellExecute = false }; using var p = Process.Start(psi); var v = p?.StandardOutput.ReadToEnd().Trim().Split(','); if (v?.Length >= 4) { string l = v[0].Trim() + "%"; long u = long.Parse(v[1].Trim()), t = long.Parse(v[2].Trim()); string vr = vUnit == "GB" ? $"{Math.Round(u / 1024.0, 1)}/{Math.Round(t / 1024.0, 1)}GB" : $"{(u * 100 / t)}%"; string tmp = tUnit == "°C" ? $"{v[3].Trim()}°C" : $"{(int.Parse(v[3].Trim()) * 9 / 5) + 32}°F"; return (l, vr, tmp); } } catch { } } if (!IsLinux) return ("--", "--", "--"); try { string gpu = Program.GetGpuPath(); if (string.IsNullOrEmpty(gpu)) return ("--", "--", "--"); string l = File.Exists($"{gpu}/device/gpu_busy_percent") ? File.ReadAllText($"{gpu}/device/gpu_busy_percent").Trim() + "%" : "--%"; long used = long.Parse(File.ReadAllText($"{gpu}/device/mem_info_vram_used").Trim()), total = long.Parse(File.ReadAllText($"{gpu}/device/mem_info_vram_total").Trim()); string vr = vUnit == "GB" ? $"{Math.Round(used / 1073741824.0, 1)}/{Math.Round(total / 1073741824.0, 1)}GB" : $"{Math.Round(100.0 * used / total, 0)}%"; string tmp = "--", hw = $"{gpu}/device/hwmon/"; if (Directory.Exists(hw)) { var d = Directory.GetDirectories(hw).FirstOrDefault(); if (d != null && File.Exists($"{d}/temp1_input")) { int c = int.Parse(File.ReadAllText($"{d}/temp1_input").Trim()) / 1000; tmp = tUnit == "°C" ? $"{c}°C" : $"{(c * 9 / 5) + 32}°F"; } } return (l, vr, tmp); } catch { return ("--", "--", "--"); } }
        public static string GetRamUsage(string unit) { if (!IsLinux) return "??GB"; try { var m = File.ReadLines("/proc/meminfo").ToList(); long t = long.Parse(m.FirstOrDefault(x => x.StartsWith("MemTotal:"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1] ?? "0"); long a = long.Parse(m.FirstOrDefault(x => x.StartsWith("MemAvailable:"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1] ?? "0"); return unit == "GB" ? $"{Math.Round((t - a) / 1048576.0, 1)}/{Math.Round(t / 1048576.0, 1)}GB" : $"{Math.Round(100.0 * (t - a) / t, 0)}%"; } catch { return "--"; } }
    }

    public static class Updater { public static string Status = "idle"; public static bool NewVersionFound = false; private static byte[]? _pData; private const string StableApiUrl = "https://api.github.com/repos/hollyntt/XOSC/releases/latest"; public static async Task CheckForUpdates() { Status = "checking GitHub..."; NewVersionFound = false; try { using var http = new HttpClient(); http.DefaultRequestHeaders.Add("User-Agent", "XOSC-Updater"); var r = await http.GetStringAsync(StableApiUrl); using var doc = JsonDocument.Parse(r); string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? ""; if (tag == Program.AppVersion) { Status = "already up to date"; return; } var asset = doc.RootElement.GetProperty("assets").EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString() == "XOSC.zip"); string dUrl = asset.GetProperty("browser_download_url").GetString() ?? ""; var z = await http.GetByteArrayAsync(dUrl); using var ms = new MemoryStream(z); using var arch = new ZipArchive(ms); var entry = arch.GetEntry("linux-x64/XOSC") ?? arch.GetEntry("XOSC"); if (entry == null) { Status = "binary not found in zip"; return; } Status = "update found!"; NewVersionFound = true; using var es = entry.Open(); using var msw = new MemoryStream(); await es.CopyToAsync(msw); _pData = msw.ToArray(); } catch (Exception e) { Status = $"error: {e.Message}"; } } public static void ApplyUpdate() { if (_pData == null) return; try { string self = Environment.ProcessPath!; Program.SaveConfig(); File.Move(self, self + ".bak", true); File.WriteAllBytes(self, _pData); if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) Process.Start("chmod", $"+x \"{self}\"").WaitForExit(); Thread.Sleep(500); Process.Start(new ProcessStartInfo(self) { UseShellExecute = true }); Environment.Exit(0); } catch (Exception e) { Status = $"apply error: {e.Message}"; } } }

    public static class NetworkStats
    {
        private static readonly Queue<double> _latencies = new();
        private static readonly int _windowSize = 8;
        private static readonly string[] _targets = { "1.1.1.1", "8.8.8.8", "vrcoscv4.vrchat.cloud" };
        private static int _targetIndex = 0;
        private static double _lastJitter = 0;
        public static double AvgPing { get; private set; } = 0;
        public static double PacketLoss { get; private set; } = 0;
        public static double Jitter { get; private set; } = 0;
        public static string Status { get; private set; } = "Idle";

        public static async Task UpdateAsync()
        {
            var target = _targets[_targetIndex];
            _targetIndex = (_targetIndex + 1) % _targets.Length;
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(target, 1500);
                if (reply.Status == IPStatus.Success)
                {
                    double rtt = reply.RoundtripTime;
                    lock (_latencies)
                    {
                        _latencies.Enqueue(rtt);
                        if (_latencies.Count > _windowSize) _latencies.Dequeue();
                        if (_latencies.Count >= 2)
                        {
                            var list = _latencies.ToList();
                            AvgPing = Math.Round(list.Average(), 1);
                            PacketLoss = 0;
                            double diff = Math.Abs(rtt - list[^2]);
                            _lastJitter = (_lastJitter * 0.7) + (diff * 0.3);
                            Jitter = Math.Round(_lastJitter, 1);
                        }
                    }
                    Status = "Stable";
                }
                else { PacketLoss = Math.Min(100, PacketLoss + 12); Status = "Timeout"; }
            }
            catch { PacketLoss = Math.Min(100, PacketLoss + 15); Status = "Error"; }
        }
    }

    public static class MusicChatEngine
    {
        private static UdpClient _client = new(); private static CancellationTokenSource? _cts; private static int _statusIdx = 0; private static bool _showHardwareTick = false; private static string _cpu = "CPU", _gpu = "GPU"; private static bool _isAfk = false; private static DateTime _lastWeatherFetch = DateTime.MinValue; private static int _weatherCode = 0; private static double _weatherTempC = 0; private static string _activeAlert = string.Empty; private static string _lastNotifiedAlert = string.Empty; private static DateTime _alertExpire = DateTime.MinValue; private static (string Title, double Position, double Length) _musicData = ("Chilling", 0, 0); private static DateTime _lastR = DateTime.MinValue, _lastS = DateTime.MinValue, _manualE = DateTime.MinValue; private static string _manualM = ""; public static int PacketsSent = 0; public static string EngineState = "Idle"; public static readonly object ListLock = new(); private static Process? _psMediaProcess; private static string _psMediaData = "Chilling|0|0"; private static Random _visRand = new Random(); private static string[] _visBars = { " ", "▂", "▃", "▄", "▅", "▆", "▇", "█" };
        public static string ActiveAlert => _activeAlert;
        
        public static void Init() { _client = new UdpClient(); _cts?.Cancel(); _cts = new CancellationTokenSource(); ScrapeHardwareNames(); StartWindowsMediaScraper(); Task.Run(() => Loop(_cts.Token)); }
        public static void SetManual(string m) { _manualM = m; _manualE = DateTime.Now.AddSeconds(20); }
        private static async Task Loop(CancellationToken t) { while (!t.IsCancellationRequested) { if (Program.Config.ChatboxEnabled) try { await Update(); } catch { } await Task.Delay(1000, t); } }
        
        private static async Task<(double lat, double lon)> GetCoordinatesAsync()
        {
            var cfg = Program.Config; string search = !string.IsNullOrWhiteSpace(cfg.CustomCity) ? cfg.CustomCity : cfg.City;
            if (!string.IsNullOrWhiteSpace(search)) { try { using var http = new HttpClient(); var query = Uri.EscapeDataString(search); var json = await http.GetStringAsync($"https://geocoding-api.open-meteo.com/v1/search?name={query}&count=1"); using var doc = JsonDocument.Parse(json); var results = doc.RootElement.GetProperty("results"); if (results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0) { var first = results[0]; return (first.GetProperty("latitude").GetDouble(), first.GetProperty("longitude").GetDouble()); } } catch { } }
            try { using var http = new HttpClient(); http.DefaultRequestHeaders.Add("User-Agent", "XOSC-Weather"); var json = await http.GetStringAsync("https://ipapi.co/json/"); using var doc = JsonDocument.Parse(json); return (doc.RootElement.GetProperty("latitude").GetDouble(), doc.RootElement.GetProperty("longitude").GetDouble()); } catch { }
            return (39.78, -89.65);
        }

        private static async Task FetchWeatherAsync(double lat, double lon)
        {
            try { using var http = new HttpClient(); var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=weather_code,temperature_2m"; var response = await http.GetStringAsync(url); using var doc = JsonDocument.Parse(response); var current = doc.RootElement.GetProperty("current"); _weatherCode = current.GetProperty("weather_code").GetInt32(); _weatherTempC = current.GetProperty("temperature_2m").GetDouble(); } catch { }
            if (Program.Config.WeatherAlertMode) { try { using var http = new HttpClient(); http.DefaultRequestHeaders.Add("User-Agent", "XOSC-Alerts"); var json = await http.GetStringAsync($"https://api.weather.gov/alerts/active?point={lat:F4},{lon:F4}"); using var doc = JsonDocument.Parse(json); var features = doc.RootElement.GetProperty("features"); if (features.GetArrayLength() > 0) { var props = features[0].GetProperty("properties"); string evt = props.GetProperty("event").GetString() ?? "Alert"; string head = props.TryGetProperty("headline", out var hl) ? (hl.GetString() ?? evt) : evt; if (head.Length > 100) head = head[..100] + "…"; _activeAlert = $"{evt.ToUpper()}: {head}"; _alertExpire = DateTime.Now.AddMinutes(5); } else if (DateTime.Now > _alertExpire) { _activeAlert = string.Empty; } } catch { if (DateTime.Now > _alertExpire) _activeAlert = string.Empty; } }
        }

        private static string WeatherCodeToString(int code, double tempC, string unit)
        {
            string condition = code switch { 0 => "☀️ Clear", 1 => "🌤️ Mostly Clear", 2 => "⛅ Partly Cloudy", 3 => "☁️ Overcast", 45 or 48 => "🌫️ Foggy", 51 or 53 or 55 => "🌦️ Drizzle", 56 or 57 => "🌨️ Freezing Drizzle", 61 or 63 or 65 => "🌧️ Rain", 66 or 67 => "🌨️ Freezing Rain", 71 or 73 or 75 => "❄️ Snow", 77 => "🌨️ Snow Grains", 80 or 81 or 82 => "🌧️ Showers", 85 or 86 => "❄️ Snow Showers", 95 => "⛈️ Thunderstorm", 96 or 99 => "⛈️ Thunderstorm w/ Hail", _ => $"🌡️ Code {code}" };
            if (!Program.Config.WeatherTempMode) return condition;
            string tempStr = unit == "°F" ? $"{Math.Round(tempC * 9.0 / 5.0 + 32, 0)}°F" : $"{Math.Round(tempC, 0)}°C";
            return $"{condition} {tempStr}";
        }

        private static async Task Update()
        {
            var cfg = Program.Config;
            if (DateTime.Now < _manualE) { EngineState = "Manual"; SendOsc("/chatbox/input", $"💬 {_manualM}"); return; }
            if ((DateTime.Now - _lastR).TotalSeconds >= cfg.Interval) {
                _musicData = FetchMusicData();
                if (cfg.WeatherMode) { var (lat, lon) = await GetCoordinatesAsync(); await FetchWeatherAsync(lat, lon); }
                if (cfg.NetMode) await NetworkStats.UpdateAsync();
                _lastR = DateTime.Now;
            }
            if (cfg.AfkDetectionMode) CheckAfk();
            if ((DateTime.Now - _lastS).TotalSeconds < Math.Max(cfg.Interval, 1.5)) return;
            
            // Unified EAS: fire when WeatherAlertMode has fetched a real NWS alert
            if ((cfg.EasMode || cfg.WeatherAlertMode) && !string.IsNullOrEmpty(_activeAlert) && DateTime.Now < _alertExpire)
            {
                // Notify the OS once per unique alert
                if (_activeAlert != _lastNotifiedAlert)
                {
                    _lastNotifiedAlert = _activeAlert;
                    NotifyOS(_activeAlert);
                }
                string alertText = $"⚠️ {_activeAlert}";
                if (alertText.Length > 140) alertText = alertText[..140];
                if (cfg.ThinMode) alertText += "\u0003\u001f";
                SendOsc("/chatbox/input", alertText);
                _lastS = DateTime.Now;
                PacketsSent++;
                EngineState = "Alert";
                return;
            }
            
            var page1 = new List<string>(); bool statusWasAdded = false; string statusText = null;
            lock (ListLock) { if (cfg.StatusTextMode && cfg.StatusList.Count > 0) { if (_statusIdx >= cfg.StatusList.Count) _statusIdx = 0; statusText = cfg.StatusList[_statusIdx].Text; page1.Add(_isAfk ? "AFK" : statusText); statusWasAdded = true; } }
            string actualPronouns = cfg.Pronouns == "Custom..." ? cfg.CustomPronouns : cfg.Pronouns;
            if (cfg.PronounsMode && !string.IsNullOrEmpty(actualPronouns))
                page1.Add($"{cfg.StatusIcon} {(cfg.StylizeTextMode ? Stylize(actualPronouns) : actualPronouns)}");
            var env1 = new List<string>();
            if (cfg.TimeMode) { string timeFmt = cfg.MilitaryTime ? "HH:mm" : "hh:mm tt"; string t = DateTime.Now.ToString(timeFmt); env1.Add($"🕒 {(cfg.StylizeTextMode ? Stylize(t) : t)}"); }
            if (cfg.DistroMode) { string dn = GetDistroName(); env1.Add(cfg.StylizeTextMode ? Stylize(dn) : dn); }
            if (cfg.WeatherMode) env1.Add(WeatherCodeToString(_weatherCode, _weatherTempC, cfg.WeatherTempUnit));
            if (env1.Count > 0) page1.Add(string.Join(" | ", env1));
            if (cfg.SongMode) {
                string songTitle = _musicData.Title == "Chilling" ? "Chilling" : _musicData.Title;
                string songStr = $"♪ {(cfg.StylizeTextMode ? Stylize(songTitle) : songTitle)}";
                string topElement = "";
                if (cfg.SongProgressMode && cfg.AudioVisualizerMode) topElement = ((DateTime.Now.Second / 5) % 2 == 0) ? MakeVisualizer() : MakeProgressBar(_musicData.Position, _musicData.Length);
                else if (cfg.AudioVisualizerMode) topElement = MakeVisualizer();
                else if (cfg.SongProgressMode) { topElement = MakeProgressBar(_musicData.Position, _musicData.Length); if (cfg.StylizeTextMode) topElement = Stylize(topElement); }
                if (!string.IsNullOrEmpty(topElement)) songStr = $"{topElement}\n{songStr}";
                page1.Add(songStr);
            }
            if (cfg.NetMode) page1.Add($"🌐 {NetworkStats.AvgPing}ms ({NetworkStats.PacketLoss}% loss)");
            var page2 = new List<string>(); if (cfg.PcMode) { if (statusText != null) page2.Add(_isAfk ? "AFK" : statusText); var env2 = new List<string>(); if (cfg.TimeMode) { string t2 = DateTime.Now.ToString(cfg.MilitaryTime ? "HH:mm" : "hh:mm tt"); env2.Add($"🕒 {(cfg.StylizeTextMode ? Stylize(t2) : t2)}"); } if (cfg.DistroMode) { string dn2 = GetDistroName(); env2.Add(cfg.StylizeTextMode ? Stylize(dn2) : dn2); } if (env2.Count > 0) page2.Add(string.Join(" | ", env2)); var g = HardwareService.GetGpuStats(cfg.GpuUnit, cfg.VramUnit, "°C"); string c = cfg.CpuUnit == "Watt" ? "--W" : HardwareService.GetCpuLoad(); if (cfg.CpuTempOn) c += $" ({HardwareService.GetCpuTemp("°C")})"; string cpuL = cfg.CustomCpuNameOn ? cfg.CustomCpuName : (cfg.HwNameMode ? Stylize(_cpu) : "CPU"); string gpuL = cfg.CustomGpuNameOn ? cfg.CustomGpuName : (cfg.HwNameMode ? Stylize(_gpu) : "GPU"); string gTempStr = cfg.GpuTempOn ? $" ({g.Temp})" : ""; page2.Add($"🖥️ {cpuL}: {c} | 🎮 {gpuL}: {g.Load}{gTempStr}"); var mem = new List<string>(); if (cfg.ShowRam) mem.Add($"🐏 ʳᵃᵐ: {HardwareService.GetRamUsage(cfg.RamUnit)}"); if (cfg.ShowVram) mem.Add($"🎞️ ᵛʳᵃᵐ: {g.Vram}"); if (cfg.VrBatteryMode && RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) mem.Add($"🔋 VR: {GetVrBattery()}"); if (mem.Count > 0) page2.Add(string.Join(" | ", mem)); }
            List<string> activePage; if (page1.Count > 0 && page2.Count > 0) { _showHardwareTick = !_showHardwareTick; activePage = _showHardwareTick ? page2 : page1; } else if (page2.Count > 0) { _showHardwareTick = true; activePage = page2; } else { _showHardwareTick = false; activePage = page1; }
            string output = string.Join("\n", activePage); if (cfg.ThinMode) { if (output.Length > 138) output = output.Substring(0, 138); output += "\u0003\u001f"; } SendOsc("/chatbox/input", output); _lastS = DateTime.Now; PacketsSent++; EngineState = "Chatting";
            if (!_showHardwareTick && statusWasAdded && cfg.AutoCycleStatus) lock (ListLock) _statusIdx = (_statusIdx + 1) % cfg.StatusList.Count;
        }
        // Sends an urgent OS notification -- once per unique alert
        public static void NotifyOS(string alertText)
        {
            // Strip characters that would break shell quoting
            string safe = alertText.Replace("'", "").Replace("\"", "").Replace("\n", " ");
            if (safe.Length > 200) safe = safe[..200] + "...";
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Build PS command as a plain string -- no interpolated raw literals
                    string psCmd = "Add-Type -AssemblyName System.Windows.Forms; "
                                 + "$n = New-Object System.Windows.Forms.NotifyIcon; "
                                 + "$n.Icon = [System.Drawing.SystemIcons]::Warning; "
                                 + "$n.Visible = $true; "
                                 + "$n.ShowBalloonTip(8000, 'XOSC Emergency Alert', '" + safe + "', [System.Windows.Forms.ToolTipIcon]::Warning); "
                                 + "Start-Sleep -Seconds 9; "
                                 + "$n.Visible = $false";
                    Process.Start(new ProcessStartInfo(
                        "powershell", "-NoProfile -WindowStyle Hidden -Command \"" + psCmd + "\"")
                    { CreateNoWindow = true, UseShellExecute = false });
                }
                else
                {
                    // Linux: notify-send with critical urgency
                    string args = "--urgency=critical --expire-time=10000 --icon=dialog-warning "
                                + "\"XOSC Emergency Alert\" "
                                + "\"" + safe + "\"";
                    Process.Start(new ProcessStartInfo("notify-send", args)
                    { UseShellExecute = false, CreateNoWindow = true });
                }
            }
            catch { }
        }
        private static void SendOsc(string addr, string text) { try { List<byte> p = new(); void Add(string s) { byte[] b = Encoding.UTF8.GetBytes(s); p.AddRange(b); p.Add(0); while (p.Count % 4 != 0) p.Add(0); } Add(addr); Add(",sTT"); Add(text); _client.Send(p.ToArray(), p.Count, Program.Config.OscIP, Program.Config.OscPort); } catch { } }
        private static string GetVrBattery() { try { var psi = new ProcessStartInfo("powershell", "-Command \"if (Get-Process vrserver -ErrorAction SilentlyContinue) { '85%' } else { '0%' }\"") { RedirectStandardOutput = true, CreateNoWindow = true, UseShellExecute = false }; using var p = Process.Start(psi); return p?.StandardOutput.ReadToEnd().Trim() ?? "0%"; } catch { return "0%"; } }
        private static void CheckAfk() { string log = Program.FindVrcLog(); if (log == null) return; try { using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var sr = new StreamReader(fs); string line; string lastLine = ""; while ((line = sr.ReadLine()) != null) { lastLine = line; } if (lastLine.Contains("OnPlayerResting")) _isAfk = true; else if (lastLine.Contains("OnPlayerActive")) _isAfk = false; } catch { } }
        private static string MakeProgressBar(double pos, double len) { if (len <= 0) return ""; int width = 8; int filled = (int)Math.Round((pos / len) * width); filled = Math.Clamp(filled, 0, width); return $"[{new string('■', filled)}{new string('□', width - filled)}] {TimeSpan.FromSeconds(pos):m\\:ss}/{TimeSpan.FromSeconds(len):m\\:ss}"; }
        private static string MakeVisualizer() { StringBuilder sb = new StringBuilder("♪ "); for(int i = 0; i < 14; i++) sb.Append(_visBars[_visRand.Next(0, _visBars.Length)]); return sb.Append(" ♪").ToString(); }
        private static void StartWindowsMediaScraper() { if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return; try { string script = @"[Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media, ContentType = WindowsRuntime] | Out-Null $m =[Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync().GetAwaiter().GetResult() while($true) { try { $s = $m.GetCurrentSession() if ($s) { $t = $s.GetTimelineProperties() $i = $s.TryGetMediaPropertiesAsync().GetAwaiter().GetResult() $art = $i.Artist; $tit = $i.Title $name = if ([string]::IsNullOrWhiteSpace($art)) { $tit } else { ""$art - $tit"" } if ([string]::IsNullOrWhiteSpace($name)) { $name = ""Chilling"" } $pos = if ($t) {[math]::Round($t.Position.TotalSeconds) } else { 0 } $end = if ($t) { [math]::Round($t.EndTime.TotalSeconds) } else { 0 }[Console]::WriteLine(""$name|$pos|$end"") } else { [Console]::WriteLine(""Chilling|0|0"") } } catch { [Console]::WriteLine(""Chilling|0|0"") } Start-Sleep -Seconds 1 }"; string b64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(script)); var psi = new ProcessStartInfo("powershell", $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {b64}") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true }; _psMediaProcess = Process.Start(psi); AppDomain.CurrentDomain.ProcessExit += (s, e) => { try { _psMediaProcess?.Kill(); } catch {} }; Task.Run(() => { if (_psMediaProcess != null) { using var sr = _psMediaProcess.StandardOutput; while (!sr.EndOfStream) { string? line = sr.ReadLine(); if (!string.IsNullOrWhiteSpace(line)) _psMediaData = line; } } }); } catch {} }
        private static (string Title, double Position, double Length) FetchMusicData() { if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { if (!string.IsNullOrEmpty(_psMediaData)) { var parts = _psMediaData.Split('|'); if (parts.Length == 3) { double.TryParse(parts[1], out double pos); double.TryParse(parts[2], out double len); return (parts[0], pos, len); } } StringBuilder buff = new StringBuilder(256); IntPtr handle = GetForegroundWindow(); if (GetWindowText(handle, buff, 256) > 0) { string t = buff.ToString(); if (t.Contains("Spotify") || t.Contains("YouTube") || t.Contains("SoundCloud")) return (Regex.Replace(t, @" - (Spotify|YouTube|SoundCloud).*", "").Trim(), 0, 0); } return ("Chilling", 0, 0); } try { var psi = new ProcessStartInfo("playerctl", "metadata --format \"{{artist}} - {{title}}\"") { RedirectStandardOutput = true, UseShellExecute = false }; using var p = Process.Start(psi); string r = p?.StandardOutput.ReadToEnd().Trim() ?? ""; if (!string.IsNullOrEmpty(r) && r != " - ") { double pos = 0, len = 0; try { var psiPos = new ProcessStartInfo("playerctl", "position") { RedirectStandardOutput = true, UseShellExecute = false }; using var pPos = Process.Start(psiPos); double.TryParse(pPos?.StandardOutput.ReadToEnd().Trim(), out pos); var psiLen = new ProcessStartInfo("playerctl", "metadata mpris:length") { RedirectStandardOutput = true, UseShellExecute = false }; using var pLen = Process.Start(psiLen); if (long.TryParse(pLen?.StandardOutput.ReadToEnd().Trim(), out long lMicro)) len = lMicro / 1000000.0; } catch {} return (r, pos, len); } return ("Chilling", 0, 0); } catch { return ("Chilling", 0, 0); } }
        private static void ScrapeHardwareNames() { string log = Program.FindVrcLog(); if (log != null) try { using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var sr = new StreamReader(fs); string line; int linesRead = 0; while ((line = sr.ReadLine()) != null && linesRead < 2000) { linesRead++; if (line.Contains("Processor Type:")) { string raw = line.Substring(line.IndexOf(':') + 1); string cleaned = Regex.Replace(raw, @"(?i)(AMD|Intel(?:\(R\))?|Core(?:\(TM\))?|Ryzen|\d+-Core|Processor|@.*)", ""); _cpu = Regex.Replace(cleaned, @"\s+", " ").Trim(); } else if (line.Contains("Graphics Device Name:")) { string raw = line.Substring(line.IndexOf(':') + 1); string cleaned = Regex.Replace(raw, @"(?i)(NVIDIA|AMD|GeForce|Radeon|Graphics|\(RADV.*?\)|Direct3D.*)", ""); _gpu = Regex.Replace(cleaned, @"\s+", " ").Trim(); } } } catch { } }
        private static string GetDistroName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "Windows";
            try
            {
                if (File.Exists("/etc/os-release"))
                {
                    string? name = null, versionId = null, prettyName = null;
                    foreach (var line in File.ReadLines("/etc/os-release"))
                    {
                        if (line.StartsWith("NAME="))        name       = line["NAME=".Length..].Trim('"', '\'');
                        if (line.StartsWith("VERSION_ID="))  versionId  = line["VERSION_ID=".Length..].Trim('"', '\'');
                        if (line.StartsWith("PRETTY_NAME=")) prettyName = line["PRETTY_NAME=".Length..].Trim('"', '\'');
                    }

                    // Just the first word — "Fedora Linux 43 (KDE...)" → "Fedora"
                    string source = name ?? prettyName ?? "Linux";
                    return source.Split(' ')[0];
                }
            }
            catch { }
            return "Linux";
        }

        private static string Stylize(string t) { string n = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", s = "ᵃᵇᶜᵈᵉᶠᵍʰᶦʲᵏˡᵐⁿᵒᵖᵠʳˢᵗᵘᵛʷˣʸᶻᵃᵇᶜᵈᵉᶠᵍʰᶦʲᵏˡᵐⁿᵒᵖᵠʳˢᵗᵘᵛʷˣʸᶻ⁰¹²³⁴⁵⁶⁷⁸⁹"; StringBuilder sb = new(); foreach (char c in t) { int i = n.IndexOf(c); sb.Append(i != -1 ? s[i] : c); } return sb.ToString(); } [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();[DllImport("user32.dll")] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    }
    class Program
    {
        public const string AppVersion = "4942d14";
        public static AppConfig Config = new();
        private static string _path = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "xosc", "config.json") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "xosc", "config.json");
        private static string _chatIn = "";
        private static Mutex? _mtx; private static int _navPage = 0;
        private static readonly string[] _navLabels = { "Dashboard", "Statuses", "Chatbox", "Hardware", "Network", "Appearance", "Misc", "Updater" };
        private static Vector4 ColAccent, ColBg, ColSidebar, ColCard, ColText, ColSubText;
        private static HashSet<int> _selectedStatusIndices = new();
        private static readonly string[] _pronounsList = { "He/Him", "She/Her", "They/Them", "He/They", "She/They", "It/Its", "Any", "Custom..." };
        private static readonly string[] _tempUnits = { "°F", "°C" };
        private static readonly string[] _countriesList = { "Afghanistan", "Albania", "Algeria", "Andorra", "Angola", "Antigua and Barbuda", "Argentina", "Armenia", "Australia", "Austria", "Azerbaijan", "Bahamas", "Bahrain", "Bangladesh", "Barbados", "Belarus", "Belgium", "Belize", "Benin", "Bhutan", "Bolivia", "Bosnia and Herzegovina", "Botswana", "Brazil", "Brunei", "Bulgaria", "Burkina Faso", "Burundi", "Cabo Verde", "Cambodia", "Cameroon", "Canada", "Central African Republic", "Chad", "Chile", "China", "Colombia", "Comoros", "Congo", "Costa Rica", "Croatia", "Cuba", "Cyprus", "Czechia", "Democratic Republic of the Congo", "Denmark", "Djibouti", "Dominica", "Dominican Republic", "Ecuador", "Egypt", "El Salvador", "Equatorial Guinea", "Eritrea", "Estonia", "Eswatini", "Ethiopia", "Fiji", "Finland", "France", "Gabon", "Gambia", "Georgia", "Germany", "Ghana", "Greece", "Grenada", "Guatemala", "Guinea", "Guinea-Bissau", "Guyana", "Haiti", "Honduras", "Hungary", "Iceland", "India", "Indonesia", "Iran", "Iraq", "Ireland", "Israel", "Italy", "Jamaica", "Japan", "Jordan", "Kazakhstan", "Kenya", "Kiribati", "Kuwait", "Kyrgyzstan", "Laos", "Latvia", "Lebanon", "Lesotho", "Liberia", "Libya", "Liechtenstein", "Lithuania", "Luxembourg", "Madagascar", "Malawi", "Malaysia", "Maldives", "Mali", "Malta", "Marshall Islands", "Mauritania", "Mauritius", "Mexico", "Micronesia", "Moldova", "Monaco", "Mongolia", "Montenegro", "Morocco", "Mozambique", "Myanmar", "Namibia", "Nauru", "Nepal", "Netherlands", "New Zealand", "Nicaragua", "Niger", "Nigeria", "North Korea", "North Macedonia", "Norway", "Oman", "Pakistan", "Palau", "Palestine", "Panama", "Papua New Guinea", "Paraguay", "Peru", "Philippines", "Poland", "Portugal", "Qatar", "Romania", "Russia", "Rwanda", "Saint Kitts and Nevis", "Saint Lucia", "Saint Vincent and the Grenadines", "Samoa", "San Marino", "Sao Tome and Principe", "Saudi Arabia", "Senegal", "Serbia", "Seychelles", "Sierra Leone", "Singapore", "Slovakia", "Slovenia", "Solomon Islands", "Somalia", "South Africa", "South Korea", "South Sudan", "Spain", "Sri Lanka", "Sudan", "Suriname", "Sweden", "Switzerland", "Syria", "Tajikistan", "Tanzania", "Thailand", "Timor-Leste", "Togo", "Tonga", "Trinidad and Tobago", "Tunisia", "Turkey", "Turkmenistan", "Tuvalu", "Uganda", "Ukraine", "United Arab Emirates", "United Kingdom", "United States", "Uruguay", "Uzbekistan", "Vanuatu", "Vatican City", "Venezuela", "Vietnam", "Yemen", "Zambia", "Zimbabwe", "Custom..." };
        private static readonly Dictionary<string, string[]> _statesMap = new() { { "United States", new[] { "Alabama", "Alaska", "Arizona", "Arkansas", "California", "Colorado", "Connecticut", "Delaware", "Florida", "Georgia", "Hawaii", "Idaho", "Illinois", "Indiana", "Iowa", "Kansas", "Kentucky", "Louisiana", "Maine", "Maryland", "Massachusetts", "Michigan", "Minnesota", "Mississippi", "Missouri", "Montana", "Nebraska", "Nevada", "New Hampshire", "New Jersey", "New Mexico", "New York", "North Carolina", "North Dakota", "Ohio", "Oklahoma", "Oregon", "Pennsylvania", "Rhode Island", "South Carolina", "South Dakota", "Tennessee", "Texas", "Utah", "Vermont", "Virginia", "Washington", "West Virginia", "Wisconsin", "Wyoming", "Custom..." } }, { "Canada", new[] { "Alberta", "British Columbia", "Manitoba", "New Brunswick", "Newfoundland and Labrador", "Nova Scotia", "Ontario", "Prince Edward Island", "Quebec", "Saskatchewan", "Northwest Territories", "Nunavut", "Yukon", "Custom..." } }, { "Australia", new[] { "New South Wales", "Victoria", "Queensland", "Western Australia", "South Australia", "Tasmania", "Australian Capital Territory", "Northern Territory", "Custom..." } }, { "United Kingdom", new[] { "England", "Scotland", "Wales", "Northern Ireland", "Custom..." } } };
        private static readonly Dictionary<string, string[]> _citiesMap = new() { { "Alabama", new[] { "Birmingham", "Montgomery", "Huntsville", "Mobile", "Custom..." } }, { "Alaska", new[] { "Anchorage", "Fairbanks", "Juneau", "Custom..." } }, { "Arizona", new[] { "Phoenix", "Tucson", "Mesa", "Custom..." } }, { "Arkansas", new[] { "Little Rock", "Fayetteville", "Fort Smith", "Custom..." } }, { "California", new[] { "Los Angeles", "San Francisco", "San Diego", "Sacramento", "San Jose", "Custom..." } }, { "Colorado", new[] { "Denver", "Colorado Springs", "Aurora", "Custom..." } }, { "Connecticut", new[] { "Bridgeport", "New Haven", "Hartford", "Custom..." } }, { "Delaware", new[] { "Wilmington", "Dover", "Newark", "Custom..." } }, { "Florida", new[] { "Miami", "Orlando", "Tampa", "Jacksonville", "Tallahassee", "Custom..." } }, { "Georgia", new[] { "Atlanta", "Augusta", "Savannah", "Custom..." } }, { "Hawaii", new[] { "Honolulu", "Hilo", "Kailua", "Custom..." } }, { "Idaho", new[] { "Boise", "Meridian", "Nampa", "Custom..." } }, { "Illinois", new[] { "Chicago", "Aurora", "Springfield", "Custom..." } }, { "Indiana", new[] { "Indianapolis", "Fort Wayne", "Evansville", "Custom..." } }, { "Iowa", new[] { "Des Moines", "Cedar Rapids", "Davenport", "Custom..." } }, { "Kansas", new[] { "Wichita", "Overland Park", "Kansas City", "Custom..." } }, { "Kentucky", new[] { "Louisville", "Lexington", "Bowling Green", "Custom..." } }, { "Louisiana", new[] { "New Orleans", "Baton Rouge", "Shreveport", "Custom..." } }, { "Maine", new[] { "Portland", "Lewiston", "Bangor", "Custom..." } }, { "Maryland", new[] { "Baltimore", "Annapolis", "Frederick", "Custom..." } }, { "Massachusetts", new[] { "Boston", "Worcester", "Springfield", "Custom..." } }, { "Michigan", new[] { "Detroit", "Grand Rapids", "Lansing", "Custom..." } }, { "Minnesota", new[] { "Minneapolis", "St. Paul", "Rochester", "Custom..." } }, { "Mississippi", new[] { "Jackson", "Gulfport", "Southaven", "Custom..." } }, { "Missouri", new[] { "Kansas City", "St. Louis", "Springfield", "Custom..." } }, { "Montana", new[] { "Billings", "Missoula", "Great Falls", "Custom..." } }, { "Nebraska", new[] { "Omaha", "Lincoln", "Bellevue", "Custom..." } }, { "Nevada", new[] { "Las Vegas", "Henderson", "Reno", "Custom..." } }, { "New Hampshire", new[] { "Manchester", "Nashua", "Concord", "Custom..." } }, { "New Jersey", new[] { "Newark", "Jersey City", "Paterson", "Custom..." } }, { "New Mexico", new[] { "Albuquerque", "Las Cruces", "Rio Rancho", "Custom..." } }, { "New York", new[] { "New York City", "Buffalo", "Rochester", "Albany", "Syracuse", "Custom..." } }, { "North Carolina", new[] { "Charlotte", "Raleigh", "Greensboro", "Custom..." } }, { "North Dakota", new[] { "Fargo", "Bismarck", "Grand Forks", "Custom..." } }, { "Ohio", new[] { "Columbus", "Cleveland", "Cincinnati", "Custom..." } }, { "Oklahoma", new[] { "Oklahoma City", "Tulsa", "Norman", "Custom..." } }, { "Oregon", new[] { "Portland", "Salem", "Eugene", "Custom..." } }, { "Pennsylvania", new[] { "Philadelphia", "Pittsburgh", "Allentown", "Custom..." } }, { "Rhode Island", new[] { "Providence", "Warwick", "Cranston", "Custom..." } }, { "South Carolina", new[] { "Charleston", "Columbia", "North Charleston", "Custom..." } }, { "South Dakota", new[] { "Sioux Falls", "Rapid City", "Aberdeen", "Custom..." } }, { "Tennessee", new[] { "Nashville", "Memphis", "Knoxville", "Custom..." } }, { "Texas", new[] { "Houston", "San Antonio", "Dallas", "Austin", "Fort Worth", "Custom..." } }, { "Utah", new[] { "Salt Lake City", "West Valley City", "Provo", "Custom..." } }, { "Vermont", new[] { "Burlington", "South Burlington", "Rutland", "Custom..." } }, { "Virginia", new[] { "Virginia Beach", "Norfolk", "Chesapeake", "Richmond", "Custom..." } }, { "Washington", new[] { "Seattle", "Spokane", "Tacoma", "Custom..." } }, { "West Virginia", new[] { "Charleston", "Huntington", "Morgantown", "Custom..." } }, { "Wisconsin", new[] { "Milwaukee", "Madison", "Green Bay", "Custom..." } }, { "Wyoming", new[] { "Cheyenne", "Casper", "Laramie", "Custom..." } }, { "Alberta", new[] { "Calgary", "Edmonton", "Red Deer", "Custom..." } }, { "British Columbia", new[] { "Vancouver", "Victoria", "Kelowna", "Custom..." } }, { "Manitoba", new[] { "Winnipeg", "Brandon", "Steinbach", "Custom..." } }, { "New Brunswick", new[] { "Moncton", "Saint John", "Fredericton", "Custom..." } }, { "Newfoundland and Labrador", new[] { "St. John's", "Corner Brook", "Mount Pearl", "Custom..." } }, { "Nova Scotia", new[] { "Halifax", "Sydney", "Truro", "Custom..." } }, { "Ontario", new[] { "Toronto", "Ottawa", "Mississauga", "Hamilton", "Custom..." } }, { "Prince Edward Island", new[] { "Charlottetown", "Summerside", "Stratford", "Custom..." } }, { "Quebec", new[] { "Montreal", "Quebec City", "Laval", "Custom..." } }, { "Saskatchewan", new[] { "Saskatoon", "Regina", "Prince Albert", "Custom..." } }, { "Northwest Territories", new[] { "Yellowknife", "Hay River", "Inuvik", "Custom..." } }, { "Nunavut", new[] { "Iqaluit", "Rankin Inlet", "Arviat", "Custom..." } }, { "Yukon", new[] { "Whitehorse", "Dawson City", "Watson Lake", "Custom..." } }, { "New South Wales", new[] { "Sydney", "Newcastle", "Wollongong", "Custom..." } }, { "Victoria", new[] { "Melbourne", "Geelong", "Ballarat", "Custom..." } }, { "Queensland", new[] { "Brisbane", "Gold Coast", "Sunshine Coast", "Custom..." } }, { "Western Australia", new[] { "Perth", "Mandurah", "Bunbury", "Custom..." } }, { "South Australia", new[] { "Adelaide", "Mount Gambier", "Gawler", "Custom..." } }, { "Tasmania", new[] { "Hobart", "Launceston", "Devonport", "Custom..." } }, { "Australian Capital Territory", new[] { "Canberra", "Custom..." } }, { "Northern Territory", new[] { "Darwin", "Alice Springs", "Katherine", "Custom..." } }, { "England", new[] { "London", "Birmingham", "Manchester", "Liverpool", "Leeds", "Custom..." } }, { "Scotland", new[] { "Glasgow", "Edinburgh", "Aberdeen", "Dundee", "Custom..." } }, { "Wales", new[] { "Cardiff", "Swansea", "Newport", "Custom..." } }, { "Northern Ireland", new[] { "Belfast", "Derry", "Lisburn", "Custom..." } } };
        
        static Vector4 V4(float[] c) => new(c[0], c[1], c[2], 1f);

        public static void Main() { 
            LoadConfig(); 
            ColAccent = V4(Config.AccentColor); ColBg = V4(Config.BgColor); ColSidebar = V4(Config.SidebarColor); ColCard = V4(Config.CardColor); ColText = DeriveText(V4(Config.BgColor)); ColSubText = DeriveSubText(V4(Config.BgColor)); 
            _mtx = new Mutex(true, "XOSC_VRC_Unique_Runner", out bool fresh); 
            if (!fresh) Environment.Exit(0); 
            Directory.CreateDirectory(Path.GetDirectoryName(_path)); 
            if (Config.SavedVersion != AppVersion) { Config.SavedVersion = AppVersion; SaveConfig(); } 
            MusicChatEngine.Init(); 
            Raylib.InitWindow(960, 640, "XOSC"); 
            Raylib.SetWindowState(ConfigFlags.ResizableWindow); 
            rlImGui.Setup(true); 
            Raylib.SetTargetFPS(60); 
            ApplyTheme(); 
            while (!Raylib.WindowShouldClose()) { 
                Raylib.BeginDrawing(); 
                Raylib.ClearBackground(new Color((byte)(Config.BgColor[0] * 255f), (byte)(Config.BgColor[1] * 255f), (byte)(Config.BgColor[2] * 255f), (byte)255)); 
                rlImGui.Begin(); 
                DrawUI(); 
                rlImGui.End(); 
                Raylib.EndDrawing(); 
            } 
            SaveConfig(); 
            Raylib.CloseWindow(); 
        }

        public static string FindVrcLog() { if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { string winPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "VRChat", "VRChat"); if (Directory.Exists(winPath)) return Directory.GetFiles(winPath, "output_log_*.txt").OrderByDescending(File.GetLastWriteTime).FirstOrDefault(); return null; } string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); string[] stems = { Path.Combine(home, ".local/share/Steam"), Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam") }; foreach (var b in stems) { if (!Directory.Exists(b)) continue; string vdf = Path.Combine(b, "steamapps", "libraryfolders.vdf"); List<string> libs = new() { b }; if (File.Exists(vdf)) { var ms = Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"(.+?)\""); foreach (Match m in ms) libs.Add(m.Groups[1].Value.Replace("\\\\", "/")); } foreach (var lib in libs) { string p = Path.Combine(lib, "steamapps/compatdata/438100/pfx/drive_c/users/steamuser/AppData/LocalLow/VRChat/VRChat"); if (Directory.Exists(p)) return Directory.GetFiles(p, "output_log_*.txt").OrderByDescending(File.GetLastWriteTime).FirstOrDefault(); } } return null; }
        public static string GetGpuPath() { if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return null; return Directory.GetDirectories("/sys/class/drm/").Where(d => d.Contains("card")).OrderByDescending(d => File.Exists($"{d}/device/mem_info_vram_total") ? long.Parse(File.ReadAllText($"{d}/device/mem_info_vram_total").Trim()) : 0).FirstOrDefault(); }
        
        // Derive readable text colours from the background brightness
        static Vector4 DeriveText(Vector4 bg)
        {
            bool light = (bg.X + bg.Y + bg.Z) / 3f > 0.6f;
            return light ? new Vector4(0.12f, 0.12f, 0.16f, 1f)
                         : new Vector4(0.88f, 0.88f, 0.92f, 1f);
        }
        static Vector4 DeriveSubText(Vector4 bg)
        {
            bool light = (bg.X + bg.Y + bg.Z) / 3f > 0.6f;
            return light ? new Vector4(0.35f, 0.35f, 0.42f, 1f)
                         : new Vector4(0.52f, 0.52f, 0.60f, 1f);
        }
        static void ApplyTheme()
        {
            var s = ImGui.GetStyle();
            s.WindowRounding    = Config.WindowRounding;
            s.ChildRounding     = Config.ChildRounding;
            s.FrameRounding     = Config.FrameRounding;
            s.PopupRounding     = Config.FrameRounding;
            s.ScrollbarRounding = Config.FrameRounding;
            s.GrabRounding      = Config.FrameRounding;
            s.TabRounding       = Config.TabRounding;
            s.WindowPadding     = new Vector2(12, 12);
            s.FramePadding      = new Vector2(8, 4);
            s.ItemSpacing       = new Vector2(8, 6);
            ImGui.GetIO().FontGlobalScale = Config.FontScale;

            // Keep cached Vector4 fields in sync — used by Card(), sidebar, alert banner
            ColAccent  = V4(Config.AccentColor);
            ColBg      = V4(Config.BgColor);
            ColSidebar = V4(Config.SidebarColor);
            ColCard    = V4(Config.CardColor);
            ColText    = DeriveText(ColBg);
            ColSubText = DeriveSubText(ColBg);

            var colors = ImGui.GetStyle().Colors;
            var a  = ColAccent;   // accent
            var bg = ColBg;       // main background
            var c  = ColCard;     // card / panel background

            // Derive input/frame bg as slightly brighter than card so it reads as a distinct surface
            float b = 0.07f; // brightness bump
            var frame  = new Vector4(Math.Min(c.X+b, 1f), Math.Min(c.Y+b, 1f), Math.Min(c.Z+b, 1f), 1f);
            var frameH = new Vector4(Math.Min(c.X+b+0.04f, 1f), Math.Min(c.Y+b+0.04f, 1f), Math.Min(c.Z+b+0.04f, 1f), 1f);
            var frameA = new Vector4(Math.Min(c.X+b+0.08f, 1f), Math.Min(c.Y+b+0.08f, 1f), Math.Min(c.Z+b+0.08f, 1f), 1f);
            var popup  = new Vector4(Math.Min(c.X+0.04f, 1f), Math.Min(c.Y+0.04f, 1f), Math.Min(c.Z+0.06f, 1f), 1f);

            // ── Backgrounds ──────────────────────────────────────────────────
            colors[(int)ImGuiCol.WindowBg]  = bg;
            colors[(int)ImGuiCol.ChildBg]   = c;
            colors[(int)ImGuiCol.PopupBg]   = popup;
            colors[(int)ImGuiCol.MenuBarBg] = c;

            // ── Frames (text inputs, sliders, combos, colour pickers) ────────
            colors[(int)ImGuiCol.FrameBg]        = frame;
            colors[(int)ImGuiCol.FrameBgHovered] = frameH;
            colors[(int)ImGuiCol.FrameBgActive]  = frameA;

            // ── Buttons ──────────────────────────────────────────────────────
            colors[(int)ImGuiCol.Button]        = new Vector4(a.X, a.Y, a.Z, 0.20f);
            colors[(int)ImGuiCol.ButtonHovered] = new Vector4(a.X, a.Y, a.Z, 0.40f);
            colors[(int)ImGuiCol.ButtonActive]  = new Vector4(a.X, a.Y, a.Z, 0.65f);

            // ── Checkmarks / sliders / scrollbar ─────────────────────────────
            colors[(int)ImGuiCol.CheckMark]            = a;
            colors[(int)ImGuiCol.SliderGrab]           = a;
            colors[(int)ImGuiCol.SliderGrabActive]     = new Vector4(a.X, a.Y, a.Z, 0.85f);
            colors[(int)ImGuiCol.ScrollbarBg]          = new Vector4(bg.X, bg.Y, bg.Z, 1f);
            colors[(int)ImGuiCol.ScrollbarGrab]        = new Vector4(a.X, a.Y, a.Z, 0.35f);
            colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(a.X, a.Y, a.Z, 0.65f);
            colors[(int)ImGuiCol.ScrollbarGrabActive]  = a;

            // ── Combo / list-box row highlight (Header = selected row) ────────
            colors[(int)ImGuiCol.Header]        = new Vector4(a.X, a.Y, a.Z, 0.22f);
            colors[(int)ImGuiCol.HeaderHovered] = new Vector4(a.X, a.Y, a.Z, 0.38f);
            colors[(int)ImGuiCol.HeaderActive]  = new Vector4(a.X, a.Y, a.Z, 0.55f);

            // ── Title bars ───────────────────────────────────────────────────
            colors[(int)ImGuiCol.TitleBg]          = c;
            colors[(int)ImGuiCol.TitleBgActive]    = popup;
            colors[(int)ImGuiCol.TitleBgCollapsed] = c;

            // ── Tabs (corrected enum names for ImGui.NET 1.91.6) ─────────────
            colors[(int)ImGuiCol.Tab]                        = c;
            colors[(int)ImGuiCol.TabHovered]                 = new Vector4(a.X, a.Y, a.Z, 0.35f);
            colors[(int)ImGuiCol.TabSelected]                = new Vector4(a.X, a.Y, a.Z, 0.25f);
            colors[(int)ImGuiCol.TabSelectedOverline]        = a;
            colors[(int)ImGuiCol.TabDimmed]                  = c;
            colors[(int)ImGuiCol.TabDimmedSelected]          = new Vector4(a.X, a.Y, a.Z, 0.14f);
            colors[(int)ImGuiCol.TabDimmedSelectedOverline]  = new Vector4(a.X, a.Y, a.Z, 0.40f);

            // ── Separators ───────────────────────────────────────────────────
            colors[(int)ImGuiCol.Separator]        = new Vector4(a.X, a.Y, a.Z, 0.22f);
            colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(a.X, a.Y, a.Z, 0.55f);
            colors[(int)ImGuiCol.SeparatorActive]  = a;

            // ── Resize grip ──────────────────────────────────────────────────
            colors[(int)ImGuiCol.ResizeGrip]        = new Vector4(a.X, a.Y, a.Z, 0.18f);
            colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(a.X, a.Y, a.Z, 0.45f);
            colors[(int)ImGuiCol.ResizeGripActive]  = a;

            // ── Borders ──────────────────────────────────────────────────────
            colors[(int)ImGuiCol.Border]       = new Vector4(a.X, a.Y, a.Z, 0.18f);
            colors[(int)ImGuiCol.BorderShadow] = new Vector4(0f, 0f, 0f, 0f);

            // ── Text ─────────────────────────────────────────────────────────
            // Use dark text on light themes (Ice White), light text on dark themes
            bool lightTheme = (bg.X + bg.Y + bg.Z) / 3f > 0.6f;
            colors[(int)ImGuiCol.Text]         = lightTheme
                ? new Vector4(0.10f, 0.10f, 0.13f, 1f)
                : new Vector4(0.92f, 0.92f, 0.95f, 1f);
            colors[(int)ImGuiCol.TextDisabled] = lightTheme
                ? new Vector4(0.40f, 0.40f, 0.45f, 1f)
                : new Vector4(0.50f, 0.50f, 0.55f, 1f);
            colors[(int)ImGuiCol.TextLink]     = a;

            // ── Nav / drag-drop ──────────────────────────────────────────────
            colors[(int)ImGuiCol.NavCursor]             = a;
            colors[(int)ImGuiCol.DragDropTarget]        = a;
            colors[(int)ImGuiCol.TextSelectedBg]        = new Vector4(a.X, a.Y, a.Z, 0.35f);
            colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(a.X, a.Y, a.Z, 0.70f);
            colors[(int)ImGuiCol.NavWindowingDimBg]     = new Vector4(0f, 0f, 0f, 0.45f);
            colors[(int)ImGuiCol.ModalWindowDimBg]      = new Vector4(0f, 0f, 0f, 0.45f);
        }

        static void DrawUI() {
            ApplyTheme(); // re-apply every frame so colors are always current
            int w = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight(); float sw = Config.SidebarWidth;
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(w, sh)); ImGui.Begin("##root", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar); 
            string alert = MusicChatEngine.ActiveAlert;
            if (!string.IsNullOrWhiteSpace(alert)) { ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.55f, 0.08f, 0.08f, 1f)); ImGui.BeginChild("##alertbanner", new Vector2(w, 30), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar); ImGui.SetCursorPos(new Vector2(10, 6)); ImGui.TextColored(new Vector4(1f, 0.85f, 0.85f, 1f), alert); ImGui.EndChild(); ImGui.PopStyleColor(); }
            ImGui.PushStyleColor(ImGuiCol.ChildBg, ColSidebar); ImGui.BeginChild("##sidebar", new Vector2(sw, sh), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar); ImGui.Dummy(new Vector2(0, 20)); ImGui.SetCursorPosX(20); ImGui.TextColored(ColAccent, "XOSC"); ImGui.SetCursorPosX(20); ImGui.TextColored(ColSubText, $"v{AppVersion}"); ImGui.Dummy(new Vector2(0, 20)); 
            for (int i = 0; i < _navLabels.Length; i++) { bool active = _navPage == i; ImGui.PushStyleColor(ImGuiCol.Button, active ? new Vector4(ColAccent.X, ColAccent.Y, ColAccent.Z, 0.15f) : Vector4.Zero); ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(ColAccent.X, ColAccent.Y, ColAccent.Z, 0.08f)); ImGui.PushStyleColor(ImGuiCol.Text, active ? ColAccent : ColText); ImGui.SetCursorPosX(10); if (ImGui.Button(_navLabels[i], new Vector2(sw - 20, 36))) _navPage = i; ImGui.PopStyleColor(3); } 
            ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.SameLine(); ImGui.PushStyleColor(ImGuiCol.ChildBg, ColBg); ImGui.BeginChild("##content", new Vector2(w - sw, sh), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar); ImGui.Dummy(new Vector2(0, 24)); 
            switch (_navPage) { 
                case 0: Card("Dashboard", () => { Toggle("Enable Chatbox", ref Config.ChatboxEnabled); ImGui.Text($"Engine State: {MusicChatEngine.EngineState}"); ImGui.Text($"Packets Sent: {MusicChatEngine.PacketsSent}"); ImGui.Text($"Weather Alert: {(string.IsNullOrEmpty(MusicChatEngine.ActiveAlert) ? "None" : "ACTIVE")}"); ImGui.Dummy(new Vector2(0, 6)); ImGui.InputText("Manual Message", ref _chatIn, 128); if (ImGui.Button("Send Manual")) { MusicChatEngine.SetManual(_chatIn); _chatIn = ""; } ImGui.Dummy(new Vector2(0, 10)); if (ImGui.InputText("OSC IP", ref Config.OscIP, 64)) SaveConfig(); if (ImGui.InputInt("OSC Port", ref Config.OscPort)) SaveConfig(); }); break; 
                case 1: Card("Statuses", () => { lock (MusicChatEngine.ListLock) { for (int i = 0; i < Config.StatusList.Count; i++) { ImGui.PushID(i); var item = Config.StatusList[i]; bool isSelected = _selectedStatusIndices.Contains(i); if (ImGui.Checkbox("##select", ref isSelected)) { if (isSelected) _selectedStatusIndices.Add(i); else _selectedStatusIndices.Remove(i); } ImGui.SameLine(); if (ImGui.Button(item.IsFavorited ? "[*]" : "[ ]", new Vector2(32, 24))) { item.IsFavorited = !item.IsFavorited; Config.StatusList = Config.StatusList.OrderByDescending(s => s.IsFavorited).ToList(); SaveConfig(); ImGui.PopID(); break; } ImGui.SameLine(); if (ImGui.Button("Up", new Vector2(32, 24)) && i > 0) { (Config.StatusList[i], Config.StatusList[i - 1]) = (Config.StatusList[i - 1], Config.StatusList[i]); SaveConfig(); } ImGui.SameLine(); if (ImGui.Button("Dn", new Vector2(32, 24)) && i < Config.StatusList.Count - 1) { (Config.StatusList[i], Config.StatusList[i + 1]) = (Config.StatusList[i + 1], Config.StatusList[i]); SaveConfig(); } ImGui.SameLine(); string statusText = item.Text; if (ImGui.InputText("##s", ref statusText, 100)) { item.Text = statusText; SaveConfig(); } ImGui.PopID(); } } ImGui.Dummy(new Vector2(0, 10)); if (ImGui.Button("+ Add New Status")) Config.StatusList.Add(new StatusItem()); ImGui.SameLine(); if (_selectedStatusIndices.Any() && ImGui.Button("Remove Selected")) { var sorted = _selectedStatusIndices.ToList(); sorted.Sort(); for (int i = sorted.Count - 1; i >= 0; i--) Config.StatusList.RemoveAt(sorted[i]); _selectedStatusIndices.Clear(); SaveConfig(); } }); break; 
                case 2: Card("Chatbox", () => { Toggle("Status Text", ref Config.StatusTextMode); Toggle("Pronouns##Toggle", ref Config.PronounsMode); Toggle("Song Mode", ref Config.SongMode); Toggle("Song Progress Bar", ref Config.SongProgressMode); Toggle("Audio Visualizer", ref Config.AudioVisualizerMode); Toggle("Time", ref Config.TimeMode); Toggle("Military Time", ref Config.MilitaryTime); Toggle("Distro", ref Config.DistroMode); Toggle("Thin Mode", ref Config.ThinMode); Toggle("Auto-Cycle", ref Config.AutoCycleStatus); Toggle("Stylize All Text", ref Config.StylizeTextMode); DrawCombo("Pronouns", _pronounsList, ref Config.Pronouns, ref Config.CustomPronouns); DrawCombo("Country", _countriesList, ref Config.Country, ref Config.CustomCountry); string[] states = _statesMap.ContainsKey(Config.Country) ? _statesMap[Config.Country] : new[] { "Custom..." }; DrawCombo("State", states, ref Config.State, ref Config.CustomState); string[] cities = _citiesMap.ContainsKey(Config.State) ? _citiesMap[Config.State] : new[] { "Custom..." }; DrawCombo("City", cities, ref Config.City, ref Config.CustomCity); ImGui.SliderInt("Interval##slider", ref Config.Interval, 1, 60); }); Card("Weather", () => { Toggle("Enable Weather", ref Config.WeatherMode); Toggle("Show Temperature", ref Config.WeatherTempMode); Toggle("Emergency Alerts", ref Config.WeatherAlertMode); int tempIdx = Array.IndexOf(_tempUnits, Config.WeatherTempUnit); if (tempIdx < 0) tempIdx = 0; if (ImGui.Combo("Temp Unit##tempunit", ref tempIdx, _tempUnits, _tempUnits.Length)) { Config.WeatherTempUnit = _tempUnits[tempIdx]; SaveConfig(); } }); break; 
                case 3: Card("Hardware", () => { Toggle("Show Stats", ref Config.PcMode); Toggle("Show RAM", ref Config.ShowRam); Toggle("Show VRAM", ref Config.ShowVram); if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) Toggle("VR Headset Battery", ref Config.VrBatteryMode); Toggle("Stylized Names", ref Config.HwNameMode); Toggle("CPU Temp", ref Config.CpuTempOn); Toggle("GPU Temp", ref Config.GpuTempOn); Toggle("Custom CPU Name", ref Config.CustomCpuNameOn); if (Config.CustomCpuNameOn) ImGui.InputText("##c_cpu", ref Config.CustomCpuName, 32); Toggle("Custom GPU Name", ref Config.CustomGpuNameOn); if (Config.CustomGpuNameOn) ImGui.InputText("##c_gpu", ref Config.CustomGpuName, 32); }); break; 
                case 4: Card("Network", () => { Toggle("Internet Ping", ref Config.NetMode); ImGui.Dummy(new Vector2(0, 8)); ImGui.Text($"Avg: {NetworkStats.AvgPing}ms"); ImGui.Text($"Loss: {NetworkStats.PacketLoss}%"); ImGui.Text($"Jitter: {NetworkStats.Jitter}ms"); ImGui.Text($"Status: {NetworkStats.Status}"); }); break; 
                case 5: Card("Appearance", () => { var presets = ThemePresets.All; for (int i = 0; i < presets.Length; i++) { if (i > 0 && i % 4 != 0) ImGui.SameLine(0, 6); var p = presets[i]; var ac = new Vector4(p.Accent[0], p.Accent[1], p.Accent[2], 1f); ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(p.Bg[0], p.Bg[1], p.Bg[2], 1f)); ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(p.Card[0], p.Card[1], p.Card[2], 1f)); ImGui.PushStyleColor(ImGuiCol.Text, ac); if (ImGui.Button(p.Name, new Vector2(140, 40))) { Config.AccentColor = (float[])p.Accent.Clone(); Config.BgColor = (float[])p.Bg.Clone(); Config.SidebarColor = (float[])p.Sidebar.Clone(); Config.CardColor = (float[])p.Card.Clone(); ColAccent = V4(Config.AccentColor); ColBg = V4(Config.BgColor); ColSidebar = V4(Config.SidebarColor); ColCard = V4(Config.CardColor); ApplyTheme(); SaveConfig(); } ImGui.PopStyleColor(3); } }); Card("Colors", () => { ColorPicker3("Accent Color", Config.AccentColor, ref ColAccent, c => Config.AccentColor = c); ColorPicker3("Background", Config.BgColor, ref ColBg, c => Config.BgColor = c); ColorPicker3("Sidebar", Config.SidebarColor, ref ColSidebar, c => Config.SidebarColor = c); ColorPicker3("Card", Config.CardColor, ref ColCard, c => Config.CardColor = c); }); Card("Layout & Shape", () => { bool changed = false; float sw = Config.SidebarWidth; if (ImGui.SliderFloat("Sidebar Width", ref sw, 120, 280)) { Config.SidebarWidth = sw; changed = true; } float wr = Config.WindowRounding; if (ImGui.SliderFloat("Window Rounding", ref wr, 0, 14)) { Config.WindowRounding = wr; changed = true; } float cr = Config.ChildRounding; if (ImGui.SliderFloat("Card Rounding", ref cr, 0, 14)) { Config.ChildRounding = cr; changed = true; } float fr = Config.FrameRounding; if (ImGui.SliderFloat("Frame Rounding", ref fr, 0, 14)) { Config.FrameRounding = fr; changed = true; } float tr = Config.TabRounding; if (ImGui.SliderFloat("Tab Rounding", ref tr, 0, 14)) { Config.TabRounding = tr; changed = true; } float fs = Config.FontScale; if (ImGui.SliderFloat("Font Scale", ref fs, 0.7f, 1.8f)) { Config.FontScale = fs; ImGui.GetIO().FontGlobalScale = fs; changed = true; } if (changed) { ApplyTheme(); SaveConfig(); } }); break; 
                case 6: Card("Misc", () => { Toggle("EAS Alert Mode", ref Config.EasMode); Toggle("AFK Detection", ref Config.AfkDetectionMode); }); break; 
                case 7: Card("Updater", () => { if (ImGui.Button("Check for Update")) Task.Run(() => Updater.CheckForUpdates()); if (Updater.NewVersionFound && ImGui.Button("Apply Update")) Updater.ApplyUpdate(); ImGui.Text($"Status: {Updater.Status}"); ImGui.Text($"Version: {AppVersion}"); }); break; 
            } ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.End(); 
        }
        static void ColorPicker3(string label, float[] src, ref Vector4 col, Action<float[]> onChange) { var v = new Vector3(src[0], src[1], src[2]); if (ImGui.ColorEdit3(label, ref v)) { onChange(new[] { v.X, v.Y, v.Z }); col = new Vector4(v.X, v.Y, v.Z, 1f); ApplyTheme(); SaveConfig(); } }
        static void DrawCombo(string label, string[] items, ref string selected, ref string customVal) { int idx = Array.IndexOf(items, selected); if (idx == -1) { if (!string.IsNullOrEmpty(selected) && selected != "Custom...") customVal = selected; idx = items.Length - 1; selected = "Custom..."; } if (ImGui.Combo(label, ref idx, items, items.Length)) { selected = items[idx]; SaveConfig(); } if (selected == "Custom..." && ImGui.InputText("Custom " + label, ref customVal, 64)) SaveConfig(); }
        static void Card(string t, Action d) {
            ImGui.SetCursorPosX(24); ImGui.TextColored(ColAccent, t); ImGui.Dummy(new Vector2(0, 8)); ImGui.SetCursorPosX(24);
            // Push both card background AND text colour derived from that surface
            ImGui.PushStyleColor(ImGuiCol.ChildBg, ColCard);
            ImGui.PushStyleColor(ImGuiCol.Text, DeriveText(ColCard));
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, DeriveSubText(ColCard));
            ImGui.BeginChild($"##c{t}", new Vector2(ImGui.GetContentRegionAvail().X - 48, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY);
            ImGui.Dummy(new Vector2(0, 10)); d(); ImGui.Dummy(new Vector2(0, 10));
            ImGui.EndChild();
            ImGui.PopStyleColor(3); // ChildBg + Text + TextDisabled
            ImGui.Dummy(new Vector2(0, 10));
        }
        static void Toggle(string l, ref bool v) { if (ImGui.Checkbox(l, ref v)) SaveConfig(); }
        public static void SaveConfig() { try { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); var options = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }; File.WriteAllText(_path, JsonSerializer.Serialize(Config, options)); } catch { } }
        static void LoadConfig() { if (!File.Exists(_path)) return; try { var rawJson = File.ReadAllText(_path); var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(rawJson); if (jsonNode["StatusList"] is System.Text.Json.Nodes.JsonArray statusList && statusList.All(node => node is System.Text.Json.Nodes.JsonValue)) { var stringList = JsonSerializer.Deserialize<List<string>>(statusList.ToJsonString()); var tempConfig = JsonSerializer.Deserialize<AppConfig>(rawJson); if (tempConfig != null && stringList != null) { tempConfig.StatusList = stringList.Select(s => new StatusItem { Text = s }).ToList(); Config = tempConfig; SaveConfig(); return; } } var options = new JsonSerializerOptions { IncludeFields = true, Converters = { new StatusItemConverter() } }; var loaded = JsonSerializer.Deserialize<AppConfig>(rawJson, options); if (loaded != null) Config = loaded; } catch { } }
    }
    public class StatusItemConverter : JsonConverter<List<StatusItem>> { public override List<StatusItem> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) { if (reader.TokenType == JsonTokenType.StartArray) { var list = new List<StatusItem>(); while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) { if (reader.TokenType == JsonTokenType.String) list.Add(new StatusItem { Text = reader.GetString() }); else if (reader.TokenType == JsonTokenType.StartObject) list.Add(JsonSerializer.Deserialize<StatusItem>(ref reader, options)); } return list; } return new List<StatusItem>(); } public override void Write(Utf8JsonWriter writer, List<StatusItem> value, JsonSerializerOptions options) => JsonSerializer.Serialize(writer, value, options); }
}