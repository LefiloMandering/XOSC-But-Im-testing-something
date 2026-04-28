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
    public class StatusItem
    {
        public string Text { get; set; } = "New Status";
        public bool IsFavorited { get; set; } = false;
    }

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
            new("Ice White", new[]{ 0.30f, 0.55f, 0.90f }, new[]{ 0.88f, 0.90f, 0.94f }, new[]{ 0.78f, 0.80f, 0.85f }, new[]{ 0.95f, 0.96f, 0.98f }),
            new("Deep Red", new[]{ 1.00f, 0.25f, 0.25f }, new[]{ 0.10f, 0.06f, 0.06f }, new[]{ 0.07f, 0.04f, 0.04f }, new[]{ 0.15f, 0.09f, 0.09f }),
            new("Cyberpunk", new[]{ 1.00f, 0.95f, 0.00f }, new[]{ 0.05f, 0.05f, 0.07f }, new[]{ 0.03f, 0.03f, 0.05f }, new[]{ 0.09f, 0.09f, 0.12f })
        };
    }

    public class AppConfig
    {
        public string SavedVersion { get; set; } = "";
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
        public bool FpsMode = false;
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
        public List<StatusItem> StatusList { get; set; } = new();
        public bool AutoCycleStatus = false;
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
        public float TabRounding = 5f;
    }

    public static class Logger
    {
        private static readonly string LogPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "xosc", "xosc.log")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "xosc", "xosc.log");

        public static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }
    }

    public static class FpsMonitor
    {
        private static double _lastTime = 0;
        private static int _frameCount = 0;
        public static int CurrentFps { get; private set; } = 0;

        public static void Update()
        {
            _frameCount++;
            double currentTime = Raylib.GetTime();
            if (currentTime - _lastTime >= 1.0)
            {
                CurrentFps = _frameCount;
                _frameCount = 0;
                _lastTime = currentTime;
            }
        }
    }

    public static class HardwareService
    {
        private static long _lastTotal, _lastIdle;
        private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        public static string GetCpuLoad()
        {
            if (!IsLinux) return "??%";
            try
            {
                if (!File.Exists("/proc/stat")) return "--";
                var parts = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                long idle = long.Parse(parts[4]);
                long total = parts.Skip(1).Select(long.Parse).Sum();
                long dIdle = idle - _lastIdle;
                long dTotal = total - _lastTotal;
                _lastIdle = idle;
                _lastTotal = total;
                return dTotal == 0 ? "0%" : Math.Round(100.0 * (1.0 - (double)dIdle / dTotal), 0) + "%";
            }
            catch { return "--"; }
        }

        public static string GetCpuTemp(string unit)
        {
            if (!IsLinux) return "--";
            try
            {
                string path = "";
                if (Directory.Exists("/sys/class/hwmon/"))
                    foreach (var dir in Directory.GetDirectories("/sys/class/hwmon/"))
                    {
                        if (!File.Exists($"{dir}/name")) continue;
                        string n = File.ReadAllText($"{dir}/name").Trim();
                        if ((n.Contains("k10temp") || n.Contains("coretemp")) && File.Exists($"{dir}/temp1_input"))
                        {
                            path = $"{dir}/temp1_input";
                            break;
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
            string smi = IsLinux ? "/usr/bin/nvidia-smi" :
                (File.Exists(@"C:\Windows\System32\nvidia-smi.exe") ? @"C:\Windows\System32\nvidia-smi.exe" :
                 @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe");

            if (File.Exists(smi))
            {
                try
                {
                    var psi = new ProcessStartInfo(smi, "--query-gpu=utilization.gpu,memory.used,memory.total,temperature.gpu --format=csv,noheader,nounits")
                    { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                    using var p = Process.Start(psi);
                    var v = p?.StandardOutput.ReadToEnd().Trim().Split(',');
                    if (v?.Length >= 4)
                    {
                        string l = v[0].Trim() + "%";
                        long u = long.Parse(v[1].Trim()), t = long.Parse(v[2].Trim());
                        string vr = vUnit == "GB" ? $"{Math.Round(u / 1024.0, 1)}/{Math.Round(t / 1024.0, 1)}GB" : $"{Math.Round((double)u * 100 / t, 0)}%";
                        string tmp = tUnit == "°C" ? $"{v[3].Trim()}°C" : $"{(int.Parse(v[3].Trim()) * 9 / 5) + 32}°F";
                        return (l, vr, tmp);
                    }
                }
                catch { }
            }

            if (!IsLinux) return ("--", "--", "--");

            try
            {
                string gpu = Program.GetGpuPath()!;
                if (string.IsNullOrEmpty(gpu)) return ("--", "--", "--");
                string l = File.Exists($"{gpu}/device/gpu_busy_percent") ? File.ReadAllText($"{gpu}/device/gpu_busy_percent").Trim() + "%" : "--%";
                long used = long.Parse(File.ReadAllText($"{gpu}/device/mem_info_vram_used").Trim());
                long total = long.Parse(File.ReadAllText($"{gpu}/device/mem_info_vram_total").Trim());
                string vr = vUnit == "GB" ? $"{Math.Round(used / 1073741824.0, 1)}/{Math.Round(total / 1073741824.0, 1)}GB" : $"{Math.Round(100.0 * used / total, 0)}%";
                string tmp = "--";
                string hwmon = $"{gpu}/device/hwmon/";
                if (Directory.Exists(hwmon))
                {
                    var dir = Directory.GetDirectories(hwmon).FirstOrDefault();
                    if (dir != null && File.Exists($"{dir}/temp1_input"))
                    {
                        int c = int.Parse(File.ReadAllText($"{dir}/temp1_input").Trim()) / 1000;
                        tmp = tUnit == "°C" ? $"{c}°C" : $"{(c * 9 / 5) + 32}°F";
                    }
                }
                return (l, vr, tmp);
            }
            catch { return ("--", "--", "--"); }
        }

        public static string GetRamUsage(string unit)
        {
            if (!IsLinux) return "??GB";
            try
            {
                var lines = File.ReadLines("/proc/meminfo").ToList();
                long total = long.Parse(lines.FirstOrDefault(x => x.StartsWith("MemTotal:"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1] ?? "0");
                long available = long.Parse(lines.FirstOrDefault(x => x.StartsWith("MemAvailable:"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1] ?? "0");
                return unit == "GB"
                    ? $"{Math.Round((total - available) / 1048576.0, 1)}/{Math.Round(total / 1048576.0, 1)}GB"
                    : $"{Math.Round(100.0 * (total - available) / total, 0)}%";
            }
            catch { return "--"; }
        }
    }

    public static class Updater
    {
        public static string Status = "idle";
        public static bool NewVersionFound = false;
        private static byte[]? _updateData;
        private const string StableApiUrl = "https://api.github.com/repos/hollyntt/XOSC/releases/latest";

        public static async Task CheckForUpdates()
        {
            Status = "checking GitHub...";
            NewVersionFound = false;
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", "XOSC-Updater");
                var response = await http.GetStringAsync(StableApiUrl);
                using var doc = JsonDocument.Parse(response);
                string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                if (tag == Program.AppVersion) { Status = "already up to date"; return; }

                var asset = doc.RootElement.GetProperty("assets").EnumerateArray()
                    .FirstOrDefault(a => a.GetProperty("name").GetString() == "XOSC.zip");
                string downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                var zipData = await http.GetByteArrayAsync(downloadUrl);

                using var ms = new MemoryStream(zipData);
                using var archive = new ZipArchive(ms);
                var entry = archive.GetEntry("linux-x64/XOSC") ?? archive.GetEntry("XOSC");
                if (entry == null) { Status = "binary not found in zip"; return; }

                Status = "update found!";
                NewVersionFound = true;
                using var entryStream = entry.Open();
                using var memory = new MemoryStream();
                await entryStream.CopyToAsync(memory);
                _updateData = memory.ToArray();
            }
            catch { Status = "error checking updates"; }
        }

        public static void ApplyUpdate()
        {
            if (_updateData == null) return;
            try
            {
                string currentPath = Environment.ProcessPath!;
                Program.SaveConfig();
                File.Move(currentPath, currentPath + ".bak", true);
                File.WriteAllBytes(currentPath, _updateData);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    Process.Start("chmod", $"+x \"{currentPath}\"")?.WaitForExit();
                Thread.Sleep(500);
                Process.Start(new ProcessStartInfo(currentPath) { UseShellExecute = true });
                Environment.Exit(0);
            }
            catch { Status = "apply error"; }
        }
    }

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
                else
                {
                    PacketLoss = Math.Min(100, PacketLoss + 12);
                    Status = "Timeout";
                }
            }
            catch
            {
                PacketLoss = Math.Min(100, PacketLoss + 15);
                Status = "Error";
            }
        }
    }

    public static class MusicChatEngine
    {
        private static UdpClient _client = new();
        private static CancellationTokenSource? _cts;
        private static int _statusIdx = 0;
        private static bool _showHardwareTick = false;
        private static string _cpu = "CPU", _gpu = "GPU";
        private static bool _isAfk = false;
        private static int _weatherCode = 0;
        private static double _weatherTempC = 0;
        private static string _activeAlert = string.Empty;
        private static DateTime _alertExpire = DateTime.MinValue;
        private static (string Title, double Position, double Length) _musicData = ("Chilling", 0, 0);
        private static DateTime _manualExpire = DateTime.MinValue;
        private static string _manualMessage = "";
        private static DateTime _lastRefresh = DateTime.MinValue;
        private static DateTime _lastSend = DateTime.MinValue;

        public static readonly object ListLock = new();
        public static string ActiveAlert => _activeAlert;

        private static Process? _mediaProcess;
        private static string _mediaData = "Chilling|0|0";
        private static readonly Random _random = new();
        private static readonly string[] _visualizerBars = { " ", "▂", "", "▄", "", "▆", "", "█" };

        public static int PacketsSent = 0;
        public static string EngineState = "Idle";

        public static void Init()
        {
            _client = new UdpClient();
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            ScrapeHardwareNames();
            StartWindowsMediaScraper();
            Task.Run(() => MainLoop(_cts.Token));
        }

        public static void SetManual(string message)
        {
            _manualMessage = message;
            _manualExpire = DateTime.Now.AddSeconds(20);
        }

        private static async Task MainLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (Program.Config.ChatboxEnabled)
                    try { await UpdateAsync(); } catch { }
                await Task.Delay(1000, token);
            }
        }

        private static async Task<(double lat, double lon)> GetCoordinatesAsync()
        {
            var cfg = Program.Config;
            string search = !string.IsNullOrWhiteSpace(cfg.CustomCity) ? cfg.CustomCity : cfg.City;

            if (!string.IsNullOrWhiteSpace(search))
            {
                try
                {
                    using var http = new HttpClient();
                    var query = Uri.EscapeDataString(search);
                    var json = await http.GetStringAsync($"https://geocoding-api.open-meteo.com/v1/search?name={query}&count=1");
                    using var doc = JsonDocument.Parse(json);
                    var results = doc.RootElement.GetProperty("results");
                    if (results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0)
                    {
                        var first = results[0];
                        return (first.GetProperty("latitude").GetDouble(), first.GetProperty("longitude").GetDouble());
                    }
                }
                catch { }
            }

            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", "XOSC-Weather");
                var json = await http.GetStringAsync("https://ipapi.co/json/");
                using var doc = JsonDocument.Parse(json);
                return (doc.RootElement.GetProperty("latitude").GetDouble(), doc.RootElement.GetProperty("longitude").GetDouble());
            }
            catch { }

            return (39.78, -89.65);
        }

        private static string WeatherCodeToString(int code, double tempC, string unit)
        {
            string condition = code switch
            {
                0 => "☀️ Clear",
                1 => "🌤️ Mostly Clear",
                2 => "⛅ Partly Cloudy",
                3 => "☁️ Overcast",
                45 or 48 => "🌫️ Foggy",
                51 or 53 or 55 => "🌦️ Drizzle",
                56 or 57 => "🌨️ Freezing Drizzle",
                61 or 63 or 65 => "🌧️ Rain",
                66 or 67 => "🌨️ Freezing Rain",
                71 or 73 or 75 => "❄️ Snow",
                77 => "🌨️ Snow Grains",
                80 or 81 or 82 => "🌧️ Showers",
                85 or 86 => "❄️ Snow Showers",
                95 => "⛈️ Thunderstorm",
                96 or 99 => "⛈️ Thunderstorm w/ Hail",
                _ => $"🌡️ Code {code}"
            };

            if (!Program.Config.WeatherTempMode) return condition;

            string tempStr = unit == "°F"
                ? $"{Math.Round(tempC * 9.0 / 5.0 + 32, 0)}°F"
                : $"{Math.Round(tempC, 0)}°C";

            return $"{condition} {tempStr}";
        }

        private static string AlertEventToEmoji(string evt)
        {
            string e = evt.ToLowerInvariant();
            if (e.Contains("tornado")) return "🌪️ TORNADO";
            if (e.Contains("hurricane")) return "🌀 HURRICANE";
            if (e.Contains("flood")) return "🌊 FLOOD";
            if (e.Contains("thunderstorm")) return "⛈️ THUNDERSTORM";
            if (e.Contains("winter storm")) return "❄️ WINTER STORM";
            if (e.Contains("blizzard")) return "🌨️ BLIZZARD";
            if (e.Contains("heat")) return "🌡️ HEAT";
            if (e.Contains("fire")) return "🔥 FIRE";
            if (e.Contains("earthquake")) return "🌍 EARTHQUAKE";
            if (e.Contains("tsunami")) return "🌊 TSUNAMI";
            return $"⚠️ {evt.ToUpperInvariant()}";
        }

        private static async Task FetchWeatherAsync(double lat, double lon)
        {
            try
            {
                using var http = new HttpClient();
                var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=weather_code,temperature_2m";
                var response = await http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(response);
                var current = doc.RootElement.GetProperty("current");
                _weatherCode = current.GetProperty("weather_code").GetInt32();
                _weatherTempC = current.GetProperty("temperature_2m").GetDouble();
            }
            catch { }

            if (Program.Config.WeatherAlertMode)
            {
                try
                {
                    using var http = new HttpClient();
                    http.DefaultRequestHeaders.Add("User-Agent", "XOSC-Alerts");
                    var json = await http.GetStringAsync($"https://api.weather.gov/alerts/active?point={lat:F4},{lon:F4}");
                    using var doc = JsonDocument.Parse(json);
                    var features = doc.RootElement.GetProperty("features");
                    if (features.GetArrayLength() > 0)
                    {
                        var props = features[0].GetProperty("properties");
                        string evt = props.GetProperty("event").GetString() ?? "Alert";
                        string head = props.TryGetProperty("headline", out var hl) ? (hl.GetString() ?? evt) : evt;
                        if (head.Length > 100) head = head[..100] + "…";
                        _activeAlert = $"{AlertEventToEmoji(evt)}: {head}";
                        _alertExpire = DateTime.Now.AddMinutes(5);
                    }
                    else if (DateTime.Now > _alertExpire)
                    {
                        _activeAlert = string.Empty;
                    }
                }
                catch
                {
                    if (DateTime.Now > _alertExpire) _activeAlert = string.Empty;
                }
            }
        }

        private static async Task UpdateAsync()
        {
            var cfg = Program.Config;

            if (DateTime.Now < _manualExpire)
            {
                EngineState = "Manual";
                SendOsc("/chatbox/input", $"💬 {_manualMessage}");
                return;
            }

            if ((DateTime.Now - _lastRefresh).TotalSeconds >= cfg.Interval)
            {
                _musicData = FetchMusicData();
                if (cfg.WeatherMode)
                {
                    var (lat, lon) = await GetCoordinatesAsync();
                    await FetchWeatherAsync(lat, lon);
                }
                if (cfg.NetMode) await NetworkStats.UpdateAsync();
                FpsMonitor.Update();
                _lastRefresh = DateTime.Now;
            }

            if (cfg.AfkDetectionMode) CheckAfk();
            if ((DateTime.Now - _lastSend).TotalSeconds < Math.Max(cfg.Interval, 1.5)) return;

            if (cfg.WeatherAlertMode && !string.IsNullOrEmpty(_activeAlert) && DateTime.Now < _alertExpire)
            {
                EngineState = "Alert";
                string alertText = _activeAlert;
                if (cfg.ThinMode) alertText += "\u0003\u001f";
                SendOsc("/chatbox/input", alertText);
                _lastSend = DateTime.Now;
                PacketsSent++;
                return;
            }

            var page1 = new List<string>();
            bool statusAdded = false;
            string? currentStatus = null;

            lock (ListLock)
            {
                if (cfg.StatusTextMode && cfg.StatusList.Count > 0)
                {
                    if (_statusIdx >= cfg.StatusList.Count) _statusIdx = 0;
                    currentStatus = cfg.StatusList[_statusIdx].Text;
                    page1.Add(_isAfk ? "AFK" : currentStatus);
                    statusAdded = true;
                }
            }

            string pronouns = cfg.Pronouns == "Custom..." ? cfg.CustomPronouns : cfg.Pronouns;
            if (cfg.PronounsMode && !string.IsNullOrEmpty(pronouns))
                page1.Add($"{cfg.StatusIcon} {pronouns}");

            var env = new List<string>();
            if (cfg.TimeMode) env.Add($"🕒 {DateTime.Now.ToString(cfg.MilitaryTime ? "HH:mm" : "hh:mm tt")}");
            if (cfg.DistroMode) env.Add(GetDistroName());
            if (cfg.WeatherMode) env.Add(WeatherCodeToString(_weatherCode, _weatherTempC, cfg.WeatherTempUnit));
            if (cfg.FpsMode) env.Add($"🎮 {FpsMonitor.CurrentFps} FPS");
            if (env.Count > 0) page1.Add(string.Join(" | ", env));

            if (cfg.SongMode)
            {
                string songLine = _musicData.Title == "Chilling" ? "♪ Chilling" : $"♪ {_musicData.Title}";
                string extra = cfg.AudioVisualizerMode ? MakeVisualizer() : cfg.SongProgressMode ? MakeProgressBar(_musicData.Position, _musicData.Length) : "";
                if (!string.IsNullOrEmpty(extra)) songLine = $"{extra}\n{songLine}";
                page1.Add(songLine);
            }

            if (cfg.NetMode) page1.Add($"🌐 {NetworkStats.AvgPing}ms ({NetworkStats.PacketLoss}% loss)");

            var page2 = new List<string>();
            if (cfg.PcMode)
            {
                if (currentStatus != null) page2.Add(_isAfk ? "AFK" : currentStatus);
                var env2 = new List<string>();
                if (cfg.TimeMode) env2.Add($"🕒 {DateTime.Now.ToString(cfg.MilitaryTime ? "HH:mm" : "hh:mm tt")}");
                if (cfg.DistroMode) env2.Add(GetDistroName());
                if (cfg.FpsMode) env2.Add($"🎮 {FpsMonitor.CurrentFps} FPS");
                if (env2.Count > 0) page2.Add(string.Join(" | ", env2));

                var gpuStats = HardwareService.GetGpuStats(cfg.GpuUnit, cfg.VramUnit, "°C");
                string cpuLoad = HardwareService.GetCpuLoad();
                if (cfg.CpuTempOn) cpuLoad += $" ({HardwareService.GetCpuTemp("°C")})";

                string cpuName = cfg.CustomCpuNameOn ? cfg.CustomCpuName : (cfg.HwNameMode ? Stylize(_cpu) : "CPU");
                string gpuName = cfg.CustomGpuNameOn ? cfg.CustomGpuName : (cfg.HwNameMode ? Stylize(_gpu) : "GPU");
                string gpuTemp = cfg.GpuTempOn ? $" ({gpuStats.Temp})" : "";
                page2.Add($"🖥️ {cpuName}: {cpuLoad} | 🎮 {gpuName}: {gpuStats.Load}{gpuTemp}");

                var mem = new List<string>();
                if (cfg.ShowRam) mem.Add($"RAM: {HardwareService.GetRamUsage(cfg.RamUnit)}");
                if (cfg.ShowVram) mem.Add($"VRAM: {gpuStats.Vram}");
                if (cfg.VrBatteryMode && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    mem.Add($"VR: {GetVrBattery()}");
                if (mem.Count > 0) page2.Add(string.Join(" | ", mem));
            }

            var activePage = (page2.Count > 0 && (_showHardwareTick || page1.Count == 0)) ? page2 : page1;
            _showHardwareTick = !_showHardwareTick;

            string finalText = string.Join("\n", activePage);
            if (finalText.Length > 140) finalText = finalText[..140];
            if (cfg.ThinMode) finalText += "\u0003\u001f";

            SendOsc("/chatbox/input", finalText);
            _lastSend = DateTime.Now;
            PacketsSent++;
            EngineState = "Running";

            if (activePage == page1 && statusAdded && cfg.AutoCycleStatus)
                lock (ListLock) _statusIdx = (_statusIdx + 1) % cfg.StatusList.Count;
        }

        private static string GetDistroName()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "Windows";

            try
            {
                if (File.Exists("/etc/os-release"))
                {
                    var lines = File.ReadAllLines("/etc/os-release");
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("PRETTY_NAME="))
                        {
                            string name = line.Substring(12).Trim('"', '\'');
                            return name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                        }
                    }
                }
            }
            catch { }

            return "Linux";
        }

        private static void SendOsc(string address, string text)
        {
            try
            {
                var packet = new List<byte>();
                void Add(string s)
                {
                    var bytes = Encoding.UTF8.GetBytes(s);
                    packet.AddRange(bytes);
                    packet.Add(0);
                    while (packet.Count % 4 != 0) packet.Add(0);
                }
                Add(address);
                Add(",s");
                Add(text);
                string ip = Program.Config.OscIP.Trim();
                _client.Send(packet.ToArray(), packet.Count, ip, Program.Config.OscPort);
            }
            catch { }
        }

        private static string GetVrBattery()
        {
            try
            {
                var psi = new ProcessStartInfo("powershell",
                    "-Command \"if (Get-Process vrserver -ErrorAction SilentlyContinue) { '85%' } else { '0%' }\"")
                { RedirectStandardOutput = true, CreateNoWindow = true, UseShellExecute = false };
                using var p = Process.Start(psi);
                return p?.StandardOutput.ReadToEnd().Trim() ?? "0%";
            }
            catch { return "0%"; }
        }

        private static void CheckAfk()
        {
            string? logPath = Program.FindVrcLog();
            if (string.IsNullOrEmpty(logPath)) return;
            try
            {
                string lastLine = File.ReadLines(logPath).LastOrDefault() ?? "";
                if (lastLine.Contains("OnPlayerResting")) _isAfk = true;
                else if (lastLine.Contains("OnPlayerActive")) _isAfk = false;
            }
            catch { }
        }

        private static string MakeProgressBar(double position, double length)
        {
            if (length <= 0) return "";
            int width = 8;
            int filled = Math.Clamp((int)Math.Round((position / length) * width), 0, width);
            return $"[{new string('■', filled)}{new string('□', width - filled)}] {TimeSpan.FromSeconds(position):m\\:ss}/{TimeSpan.FromSeconds(length):m\\:ss}";
        }

        private static string MakeVisualizer()
        {
            var sb = new StringBuilder("♪ ");
            for (int i = 0; i < 12; i++) sb.Append(_visualizerBars[_random.Next(_visualizerBars.Length)]);
            sb.Append(" ♪");
            return sb.ToString();
        }

        private static void StartWindowsMediaScraper()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            try
            {
                string script = @"[Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media, ContentType=WindowsRuntime] | Out-Null
$m = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync().GetAwaiter().GetResult()
while($true) {
try {
$s = $m.GetCurrentSession()
if ($s) {
$t = $s.GetTimelineProperties()
$i = $s.TryGetMediaPropertiesAsync().GetAwaiter().GetResult()
$art = $i.Artist; $tit = $i.Title
$name = if ([string]::IsNullOrWhiteSpace($art)) { $tit } else { ""$art - $tit"" }
if ([string]::IsNullOrWhiteSpace($name)) { $name = ""Chilling"" }
$pos = if ($t) { [math]::Round($t.Position.TotalSeconds) } else { 0 }
$end = if ($t) { [math]::Round($t.EndTime.TotalSeconds) } else { 0 }
[Console]::WriteLine(""$name|$pos|$end"")
} else { [Console]::WriteLine(""Chilling|0|0"") }
} catch { [Console]::WriteLine(""Chilling|0|0"") }
Start-Sleep -Seconds 1
}";
                string b64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                var psi = new ProcessStartInfo("powershell", $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {b64}")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                _mediaProcess = Process.Start(psi);
                AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { _mediaProcess?.Kill(); } catch { } };
                Task.Run(() =>
                {
                    if (_mediaProcess == null) return;
                    using var sr = _mediaProcess.StandardOutput;
                    while (!sr.EndOfStream)
                    {
                        string? line = sr.ReadLine();
                        if (!string.IsNullOrWhiteSpace(line)) _mediaData = line;
                    }
                });
            }
            catch { }
        }

        private static (string Title, double Position, double Length) FetchMusicData()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!string.IsNullOrEmpty(_mediaData))
                {
                    var parts = _mediaData.Split('|');
                    if (parts.Length == 3)
                    {
                        double.TryParse(parts[1], out double pos);
                        double.TryParse(parts[2], out double len);
                        return (parts[0], pos, len);
                    }
                }
                return ("Chilling", 0, 0);
            }
            try
            {
                var psi = new ProcessStartInfo("playerctl", "metadata --format \"{{artist}} - {{title}}\"")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                string r = p?.StandardOutput.ReadToEnd().Trim() ?? "";
                if (!string.IsNullOrEmpty(r) && r != " - ")
                {
                    double pos = 0, len = 0;
                    try
                    {
                        var pPos = Process.Start(new ProcessStartInfo("playerctl", "position") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                        double.TryParse(pPos?.StandardOutput.ReadToEnd().Trim(), out pos);
                        var pLen = Process.Start(new ProcessStartInfo("playerctl", "metadata mpris:length") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                        if (long.TryParse(pLen?.StandardOutput.ReadToEnd().Trim(), out long lMicro))
                            len = lMicro / 1000000.0;
                    }
                    catch { }
                    return (r, pos, len);
                }
            }
            catch { }
            return ("Chilling", 0, 0);
        }

        private static void ScrapeHardwareNames()
        {
            string log = Program.FindVrcLog();
            if (log != null)
                foreach (var l in File.ReadLines(log).Take(1500))
                {
                    if (l.Contains("Processor Type:"))
                        _cpu = Regex.Replace(l.Split(": ")[1], @"(\s\d+-Core| Processor| @.*|AMD |Intel |Core |Ryzen )", "").Trim();
                    if (l.Contains("Graphics Device Name:"))
                        _gpu = Regex.Replace(l.Split(": ")[1], @"(\(RADV.*| Graphics|AMD |NVIDIA |GeForce |Radeon |\sRX\s|\sXT)", "").Trim();
                }
        }

        private static string Stylize(string t)
        {
            const string n = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            const string s = "ᵃᶜᵈᵉᶠʰᶦʲᵏˡⁿᵒᵖᵠʳˢᵘᵛʷˣʸᵃᵇᶜᵈᵉᶠʰᶦᵏˡⁿᵒᵖᵠʳˢᵘᵛʷˣʸ⁰¹²³⁵⁶⁷⁸⁹";
            var sb = new StringBuilder();
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
        public const string AppVersion = "dev-fixed-2026";
        public static AppConfig Config = new();

        private static readonly string ConfigPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "xosc", "config.json")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "xosc", "config.json");

        private static string _manualInput = "";
        private static Mutex? _mutex;
        private static int _currentPage = 0;

        private static readonly string[] NavLabels = { "Dashboard", "Statuses", "Chatbox", "Hardware", "Network", "Appearance", "Misc", "Updater" };

        private static Vector4 ColAccent, ColBg, ColSidebar, ColCard;
        private static readonly HashSet<int> SelectedStatuses = new();

        private static readonly string[] _pronounsList = { "He/Him", "She/Her", "They/Them", "He/They", "She/They", "It/Its", "Any", "Custom..." };
        private static readonly string[] _tempUnits = { "°F", "°C" };
        private static readonly string[] _countriesList = {
            "Afghanistan", "Albania", "Algeria", "Andorra", "Angola", "Antigua and Barbuda", "Argentina", "Armenia",
            "Australia", "Austria", "Azerbaijan", "Bahamas", "Bahrain", "Bangladesh", "Barbados", "Belarus", "Belgium",
            "Belize", "Benin", "Bhutan", "Bolivia", "Bosnia and Herzegovina", "Botswana", "Brazil", "Brunei", "Bulgaria",
            "Burkina Faso", "Burundi", "Cabo Verde", "Cambodia", "Cameroon", "Canada", "Central African Republic", "Chad",
            "Chile", "China", "Colombia", "Comoros", "Congo", "Costa Rica", "Croatia", "Cuba", "Cyprus", "Czechia",
            "Democratic Republic of the Congo", "Denmark", "Djibouti", "Dominica", "Dominican Republic", "Ecuador",
            "Egypt", "El Salvador", "Equatorial Guinea", "Eritrea", "Estonia", "Eswatini", "Ethiopia", "Fiji", "Finland",
            "France", "Gabon", "Gambia", "Georgia", "Germany", "Ghana", "Greece", "Grenada", "Guatemala", "Guinea",
            "Guinea-Bissau", "Guyana", "Haiti", "Honduras", "Hungary", "Iceland", "India", "Indonesia", "Iran", "Iraq",
            "Ireland", "Israel", "Italy", "Jamaica", "Japan", "Jordan", "Kazakhstan", "Kenya", "Kiribati", "Kuwait",
            "Kyrgyzstan", "Laos", "Latvia", "Lebanon", "Lesotho", "Liberia", "Libya", "Liechtenstein", "Lithuania",
            "Luxembourg", "Madagascar", "Malawi", "Malaysia", "Maldives", "Mali", "Malta", "Marshall Islands",
            "Mauritania", "Mauritius", "Mexico", "Micronesia", "Moldova", "Monaco", "Mongolia", "Montenegro", "Morocco",
            "Mozambique", "Myanmar", "Namibia", "Nauru", "Nepal", "Netherlands", "New Zealand", "Nicaragua", "Niger",
            "Nigeria", "North Korea", "North Macedonia", "Norway", "Oman", "Pakistan", "Palau", "Palestine", "Panama",
            "Papua New Guinea", "Paraguay", "Peru", "Philippines", "Poland", "Portugal", "Qatar", "Romania", "Russia",
            "Rwanda", "Saint Kitts and Nevis", "Saint Lucia", "Saint Vincent and the Grenadines", "Samoa",
            "San Marino", "Sao Tome and Principe", "Saudi Arabia", "Senegal", "Serbia", "Seychelles", "Sierra Leone",
            "Singapore", "Slovakia", "Slovenia", "Solomon Islands", "Somalia", "South Africa", "South Korea",
            "South Sudan", "Spain", "Sri Lanka", "Sudan", "Suriname", "Sweden", "Switzerland", "Syria", "Tajikistan",
            "Tanzania", "Thailand", "Timor-Leste", "Togo", "Tonga", "Trinidad and Tobago", "Tunisia", "Turkey",
            "Turkmenistan", "Tuvalu", "Uganda", "Ukraine", "United Arab Emirates", "United Kingdom", "United States",
            "Uruguay", "Uzbekistan", "Vanuatu", "Vatican City", "Venezuela", "Vietnam", "Yemen", "Zambia", "Zimbabwe",
            "Custom..."
        };

        public static void Main()
        {
            LoadConfig();

            ColAccent = V4(Config.AccentColor);
            ColBg = V4(Config.BgColor);
            ColSidebar = V4(Config.SidebarColor);
            ColCard = V4(Config.CardColor);

            _mutex = new Mutex(true, "XOSC_VRC_Unique_Runner", out bool isNew);
            if (!isNew) Environment.Exit(0);

            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

            if (Config.SavedVersion != AppVersion)
            {
                Config.SavedVersion = AppVersion;
                SaveConfig();
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
                Raylib.ClearBackground(new Color(
                    (byte)(Config.BgColor[0] * 255f),
                    (byte)(Config.BgColor[1] * 255f),
                    (byte)(Config.BgColor[2] * 255f),
                    (byte)255));

                rlImGui.Begin();
                DrawUI();
                rlImGui.End();
                Raylib.EndDrawing();
            }

            SaveConfig();
            Raylib.CloseWindow();
        }

        static Vector4 V4(float[] c) => new(c[0], c[1], c[2], 1f);

        static void ApplyTheme()
        {
            var style = ImGui.GetStyle();
            style.WindowRounding = Config.WindowRounding;
            style.ChildRounding = Config.ChildRounding;
            style.FrameRounding = Config.FrameRounding;
            style.PopupRounding = Config.FrameRounding;
            style.ScrollbarRounding = Config.FrameRounding;
            style.GrabRounding = Config.FrameRounding;
            style.TabRounding = Config.TabRounding;
            style.WindowPadding = new Vector2(12, 12);
            ImGui.GetIO().FontGlobalScale = Config.FontScale;
        }

        static void DrawUI()
        {
            int w = Raylib.GetScreenWidth();
            int h = Raylib.GetScreenHeight();
            float sw = Config.SidebarWidth;

            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(new Vector2(w, h));
            ImGui.Begin("##root", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar);

            string alert = MusicChatEngine.ActiveAlert;
            if (!string.IsNullOrWhiteSpace(alert))
            {
                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.55f, 0.08f, 0.08f, 1f));
                ImGui.BeginChild("##alertbanner", new Vector2(w, 30), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
                ImGui.SetCursorPos(new Vector2(10, 6));
                ImGui.TextColored(new Vector4(1f, 0.85f, 0.85f, 1f), alert);
                ImGui.EndChild();
                ImGui.PopStyleColor();
            }

            ImGui.PushStyleColor(ImGuiCol.ChildBg, ColSidebar);
            ImGui.BeginChild("##sidebar", new Vector2(sw, h), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
            ImGui.Dummy(new Vector2(0, 20));
            ImGui.SetCursorPosX(20); ImGui.TextColored(ColAccent, "XOSC");
            ImGui.SetCursorPosX(20); ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.6f, 1f), $"v{AppVersion}");
            ImGui.Dummy(new Vector2(0, 20));

            for (int i = 0; i < NavLabels.Length; i++)
            {
                bool active = _currentPage == i;
                ImGui.PushStyleColor(ImGuiCol.Button, active ? new Vector4(ColAccent.X, ColAccent.Y, ColAccent.Z, 0.15f) : Vector4.Zero);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(ColAccent.X, ColAccent.Y, ColAccent.Z, 0.08f));
                ImGui.PushStyleColor(ImGuiCol.Text, active ? ColAccent : new Vector4(0.72f, 0.72f, 0.80f, 1f));
                ImGui.SetCursorPosX(10);
                if (ImGui.Button(NavLabels[i], new Vector2(sw - 20, 36))) _currentPage = i;
                ImGui.PopStyleColor(3);
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.ChildBg, ColBg);
            ImGui.BeginChild("##content", new Vector2(w - sw, h), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
            ImGui.Dummy(new Vector2(0, 24));

            switch (_currentPage)
            {
                case 0: DrawDashboard(); break;
                case 1: DrawStatuses(); break;
                case 2: DrawChatbox(); break;
                case 3: DrawHardware(); break;
                case 4: DrawNetwork(); break;
                case 5: DrawAppearance(); break;
                case 6: DrawMisc(); break;
                case 7: DrawUpdater(); break;
            }

            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.End();
        }

        static void DrawDashboard()
        {
            Card("Dashboard", () =>
            {
                Toggle("Enable Chatbox", ref Config.ChatboxEnabled);
                ImGui.Text($"Engine State: {MusicChatEngine.EngineState}");
                ImGui.Text($"Packets Sent: {MusicChatEngine.PacketsSent}");
                ImGui.Text($"Current FPS: {FpsMonitor.CurrentFps}");
                ImGui.Text($"Weather Alert: {(string.IsNullOrEmpty(MusicChatEngine.ActiveAlert) ? "None" : "ACTIVE")}");
                ImGui.Dummy(new Vector2(0, 6));
                ImGui.InputText("Manual Message", ref _manualInput, 128);
                if (ImGui.Button("Send Manual")) { MusicChatEngine.SetManual(_manualInput); _manualInput = ""; }
                ImGui.Dummy(new Vector2(0, 10));
                if (ImGui.InputText("OSC IP", ref Config.OscIP, 64)) SaveConfig();
                if (ImGui.InputInt("OSC Port", ref Config.OscPort)) SaveConfig();
            });
        }

        static void DrawStatuses()
        {
            Card("Statuses", () =>
            {
                lock (MusicChatEngine.ListLock)
                {
                    for (int i = 0; i < Config.StatusList.Count; i++)
                    {
                        ImGui.PushID(i);
                        var item = Config.StatusList[i];

                        bool selected = SelectedStatuses.Contains(i);
                        if (ImGui.Checkbox("##select", ref selected))
                        {
                            if (selected) SelectedStatuses.Add(i);
                            else SelectedStatuses.Remove(i);
                        }

                        ImGui.SameLine();
                        if (ImGui.Button(item.IsFavorited ? "[*]" : "[ ]", new Vector2(32, 24)))
                        {
                            item.IsFavorited = !item.IsFavorited;
                            Config.StatusList = Config.StatusList.OrderByDescending(s => s.IsFavorited).ToList();
                            SaveConfig();
                        }

                        ImGui.SameLine();
                        if (ImGui.Button("Up", new Vector2(32, 24)) && i > 0)
                        {
                            (Config.StatusList[i], Config.StatusList[i - 1]) = (Config.StatusList[i - 1], Config.StatusList[i]);
                            SaveConfig();
                        }

                        ImGui.SameLine();
                        if (ImGui.Button("Dn", new Vector2(32, 24)) && i < Config.StatusList.Count - 1)
                        {
                            (Config.StatusList[i], Config.StatusList[i + 1]) = (Config.StatusList[i + 1], Config.StatusList[i]);
                            SaveConfig();
                        }

                        ImGui.SameLine();
                        string text = item.Text;
                        if (ImGui.InputText("##text", ref text, 120))
                        {
                            item.Text = text;
                            SaveConfig();
                        }

                        ImGui.PopID();
                    }
                }

                if (ImGui.Button("+ Add")) Config.StatusList.Add(new StatusItem());
                ImGui.SameLine();
                if (SelectedStatuses.Count > 0 && ImGui.Button("Remove Selected"))
                {
                    foreach (var idx in SelectedStatuses.OrderByDescending(x => x).ToList())
                        Config.StatusList.RemoveAt(idx);
                    SelectedStatuses.Clear();
                    SaveConfig();
                }
            });
        }

        static void DrawChatbox()
        {
            Card("General", () =>
            {
                Toggle("Status Text", ref Config.StatusTextMode);
                Toggle("Pronouns##pronounstoggle", ref Config.PronounsMode);
                Toggle("Song Mode", ref Config.SongMode);
                Toggle("Song Progress", ref Config.SongProgressMode);
                Toggle("Audio Visualizer", ref Config.AudioVisualizerMode);
                Toggle("Time", ref Config.TimeMode);
                Toggle("Military Time", ref Config.MilitaryTime);
                Toggle("Distro", ref Config.DistroMode);
                Toggle("Thin Mode", ref Config.ThinMode);
                Toggle("Auto Cycle Status", ref Config.AutoCycleStatus);
                Toggle("Show FPS", ref Config.FpsMode);
                DrawCombo("Pronouns", _pronounsList, ref Config.Pronouns, ref Config.CustomPronouns);
                DrawCombo("Country", _countriesList, ref Config.Country, ref Config.CustomCountry);
                ImGui.SliderInt("Update Interval (s)", ref Config.Interval, 1, 30);
            });

            Card("Weather", () =>
            {
                Toggle("Enable Weather", ref Config.WeatherMode);
                Toggle("Show Temperature", ref Config.WeatherTempMode);
                Toggle("Emergency Alerts", ref Config.WeatherAlertMode);
                int tempIdx = Array.IndexOf(_tempUnits, Config.WeatherTempUnit);
                if (tempIdx < 0) tempIdx = 0;
                if (ImGui.Combo("Temp Unit##tempunit", ref tempIdx, _tempUnits, _tempUnits.Length))
                {
                    Config.WeatherTempUnit = _tempUnits[tempIdx];
                    SaveConfig();
                }
            });
        }

        static void DrawHardware()
        {
            Card("Hardware", () =>
            {
                Toggle("Show Hardware Stats", ref Config.PcMode);
                Toggle("Show RAM", ref Config.ShowRam);
                Toggle("Show VRAM", ref Config.ShowVram);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    Toggle("VR Battery", ref Config.VrBatteryMode);
                Toggle("Stylized Names", ref Config.HwNameMode);
                Toggle("CPU Temp", ref Config.CpuTempOn);
                Toggle("GPU Temp", ref Config.GpuTempOn);
                Toggle("Custom CPU Name", ref Config.CustomCpuNameOn);
                if (Config.CustomCpuNameOn) ImGui.InputText("CPU Name", ref Config.CustomCpuName, 32);
                Toggle("Custom GPU Name", ref Config.CustomGpuNameOn);
                if (Config.CustomGpuNameOn) ImGui.InputText("GPU Name", ref Config.CustomGpuName, 32);
            });
        }

        static void DrawNetwork()
        {
            Card("Network", () => 
            { 
                Toggle("Internet Ping", ref Config.NetMode);
                ImGui.Dummy(new Vector2(0, 8));
                ImGui.Text($"📊 Avg: {NetworkStats.AvgPing}ms");
                ImGui.Text($"📉 Loss: {NetworkStats.PacketLoss}%");
                ImGui.Text($"📈 Jitter: {NetworkStats.Jitter}ms");
                ImGui.Text($"🟢 Status: {NetworkStats.Status}");
            });
        }

        static void DrawAppearance()
        {
            Card("Theme Presets", () =>
            {
                var presets = ThemePresets.All;
                for (int i = 0; i < presets.Length; i++)
                {
                    if (i > 0 && i % 4 != 0) ImGui.SameLine(0, 6);
                    var p = presets[i];
                    var ac = new Vector4(p.Accent[0], p.Accent[1], p.Accent[2], 1f);
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(p.Bg[0], p.Bg[1], p.Bg[2], 1f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(p.Card[0], p.Card[1], p.Card[2], 1f));
                    ImGui.PushStyleColor(ImGuiCol.Text, ac);
                    if (ImGui.Button(p.Name, new Vector2(140, 40)))
                    {
                        Config.AccentColor = (float[])p.Accent.Clone();
                        Config.BgColor = (float[])p.Bg.Clone();
                        Config.SidebarColor = (float[])p.Sidebar.Clone();
                        Config.CardColor = (float[])p.Card.Clone();
                        ColAccent = V4(Config.AccentColor);
                        ColBg = V4(Config.BgColor);
                        ColSidebar = V4(Config.SidebarColor);
                        ColCard = V4(Config.CardColor);
                        ApplyTheme();
                        SaveConfig();
                    }
                    ImGui.PopStyleColor(3);
                }
            });

            Card("Colors", () =>
            {
                ColorPicker3("Accent Color", Config.AccentColor, ref ColAccent, c => Config.AccentColor = c);
                ColorPicker3("Background", Config.BgColor, ref ColBg, c => Config.BgColor = c);
                ColorPicker3("Sidebar", Config.SidebarColor, ref ColSidebar, c => Config.SidebarColor = c);
                ColorPicker3("Card", Config.CardColor, ref ColCard, c => Config.CardColor = c);
            });

            Card("Layout & Shape", () =>
            {
                bool changed = false;
                float sw = Config.SidebarWidth;
                if (ImGui.SliderFloat("Sidebar Width", ref sw, 120, 280)) { Config.SidebarWidth = sw; changed = true; }
                float wr = Config.WindowRounding;
                if (ImGui.SliderFloat("Window Rounding", ref wr, 0, 14)) { Config.WindowRounding = wr; changed = true; }
                float cr = Config.ChildRounding;
                if (ImGui.SliderFloat("Card Rounding", ref cr, 0, 14)) { Config.ChildRounding = cr; changed = true; }
                float fr = Config.FrameRounding;
                if (ImGui.SliderFloat("Frame Rounding", ref fr, 0, 14)) { Config.FrameRounding = fr; changed = true; }
                float tr = Config.TabRounding;
                if (ImGui.SliderFloat("Tab Rounding", ref tr, 0, 14)) { Config.TabRounding = tr; changed = true; }
                float fs = Config.FontScale;
                if (ImGui.SliderFloat("Font Scale", ref fs, 0.7f, 1.8f))
                {
                    Config.FontScale = fs;
                    ImGui.GetIO().FontGlobalScale = fs;
                    changed = true;
                }
                if (changed) { ApplyTheme(); SaveConfig(); }
            });
        }

        static void DrawMisc()
        {
            Card("Misc", () => { Toggle("EAS Alert Mode", ref Config.EasMode); });
        }

        static void DrawUpdater()
        {
            Card("Updater", () =>
            {
                if (ImGui.Button("Check for Updates")) Task.Run(() => Updater.CheckForUpdates());
                if (Updater.NewVersionFound && ImGui.Button("Apply Update")) Updater.ApplyUpdate();
                ImGui.Text($"Status: {Updater.Status}");
                ImGui.Text($"Version: {AppVersion}");
            });
        }

        static void ColorPicker3(string label, float[] src, ref Vector4 col, Action<float[]> onChange)
        {
            var v = new Vector3(src[0], src[1], src[2]);
            if (ImGui.ColorEdit3(label, ref v))
            {
                onChange(new[] { v.X, v.Y, v.Z });
                col = new Vector4(v.X, v.Y, v.Z, 1f);
                ApplyTheme();
                SaveConfig();
            }
        }

        static void Card(string title, Action content)
        {
            ImGui.SetCursorPosX(24);
            ImGui.TextColored(ColAccent, title);
            ImGui.Dummy(new Vector2(0, 4));
            ImGui.SetCursorPosX(24);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, ColCard);
            ImGui.BeginChild($"##card_{title}", new Vector2(ImGui.GetContentRegionAvail().X - 48, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY);
            ImGui.Dummy(new Vector2(0, 8));
            content();
            ImGui.Dummy(new Vector2(0, 8));
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(0, 10));
        }

        static void Toggle(string label, ref bool value)
        {
            if (ImGui.Checkbox(label + "##toggle", ref value)) SaveConfig();
        }

        static void DrawCombo(string label, string[] items, ref string selected, ref string custom)
        {
            int idx = Array.IndexOf(items, selected);
            if (idx < 0)
            {
                if (!string.IsNullOrEmpty(selected) && selected != "Custom...") custom = selected;
                idx = items.Length - 1;
                selected = "Custom...";
            }
            if (ImGui.Combo(label + "##combo", ref idx, items, items.Length))
            {
                selected = items[idx];
                SaveConfig();
            }
            if (selected == "Custom..." && ImGui.InputText("Custom " + label, ref custom, 64)) SaveConfig();
        }

        public static void SaveConfig()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                var opt = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Config, opt));
            }
            catch { }
        }

        static void LoadConfig()
        {
            if (!File.Exists(ConfigPath)) return;
            try
            {
                var opt = new JsonSerializerOptions { IncludeFields = true, Converters = { new StatusItemConverter() } };
                var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), opt);
                if (loaded != null) Config = loaded;
            }
            catch { }
        }

        public static string? FindVrcLog()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "VRChat", "VRChat");
            return Directory.Exists(path)
                ? Directory.GetFiles(path, "output_log_*.txt").OrderByDescending(File.GetLastWriteTime).FirstOrDefault()
                : null;
        }

        public static string? GetGpuPath()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return null;
            return Directory.GetDirectories("/sys/class/drm/")
                .Where(d => d.Contains("card"))
                .OrderByDescending(d => File.Exists($"{d}/device/mem_info_vram_total")
                    ? long.Parse(File.ReadAllText($"{d}/device/mem_info_vram_total").Trim()) : 0)
                .FirstOrDefault();
        }
    }

    public class StatusItemConverter : JsonConverter<List<StatusItem>>
    {
        public override List<StatusItem> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var list = new List<StatusItem>();
            if (reader.TokenType == JsonTokenType.StartArray)
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.String && reader.GetString() is string t)
                        list.Add(new StatusItem { Text = t });
                    else if (reader.TokenType == JsonTokenType.StartObject)
                        list.Add(JsonSerializer.Deserialize<StatusItem>(ref reader, options)!);
                }
            return list;
        }

        public override void Write(Utf8JsonWriter writer, List<StatusItem> value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value, options);
    }
}