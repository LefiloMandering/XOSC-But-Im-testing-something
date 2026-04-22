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
        public string City = "Los Angeles";
        public string CustomCity = "";
        public string State = "California";
        public string CustomState = "";
        public string Country = "United States";
        public string CustomCountry = "";
        public string Pronouns = "They/Them";
        public string CustomPronouns = "";
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
        public string PublishPath = "https://github.com/hollyntt/XOSC/raw/refs/heads/master/publish/XOSC.zip";
        public bool BetaOptIn = false;
        public string Cookie = "";
        public string SavedVersion = "";
    }

    public static class HardwareService
    {
        private static long _lastTotal, _lastIdle;
        private static bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        public static string GetCpuLoad()
        {
            if (!IsLinux) return "??%";
            try {
                if (!File.Exists("/proc/stat")) return "--";
                var parts = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                long idle = long.Parse(parts[4]), total = parts.Skip(1).Select(long.Parse).Sum();
                long dIdle = idle - _lastIdle, dTotal = total - _lastTotal;
                _lastIdle = idle; _lastTotal = total;
                return dTotal == 0 ? "0%" : Math.Round(100.0 * (1.0 - (double)dIdle / dTotal), 0) + "%";
            } catch { return "--"; }
        }

        public static string GetCpuTemp(string unit)
        {
            if (!IsLinux) return "--";
            try {
                string path = "";
                if (Directory.Exists("/sys/class/hwmon/")) {
                    foreach (var dir in Directory.GetDirectories("/sys/class/hwmon/")) {
                        if (!File.Exists($"{dir}/name")) continue;
                        string n = File.ReadAllText($"{dir}/name");
                        if (n.Contains("k10temp") || n.Contains("coretemp")) {
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
            string smi = IsLinux ? "/usr/bin/nvidia-smi" : "C:\\Windows\\System32\\nvidia-smi.exe";
            if (File.Exists(smi)) {
                try {
                    var psi = new ProcessStartInfo(smi, "--query-gpu=utilization.gpu,memory.used,memory.total,temperature.gpu --format=csv,noheader,nounits") { RedirectStandardOutput = true, UseShellExecute = false };
                    using var p = Process.Start(psi);
                    var v = p?.StandardOutput.ReadToEnd().Trim().Split(',');
                    if (v?.Length >= 4) {
                        string l = v[0].Trim() + "%";
                        long u = long.Parse(v[1].Trim()), t = long.Parse(v[2].Trim());
                        string vr = vUnit == "GB" ? $"{Math.Round(u / 1024.0, 1)}/{Math.Round(t / 1024.0, 1)}GB" : $"{(u * 100 / t)}%";
                        string tmp = tUnit == "°C" ? $"{v[3].Trim()}°C" : $"{(int.Parse(v[3].Trim()) * 9 / 5) + 32}°F";
                        return (l, vr, tmp);
                    }
                } catch { }
            }
            if (!IsLinux) return ("--", "--", "--");
            try {
                string gpu = Program.GetGpuPath();
                if (string.IsNullOrEmpty(gpu)) return ("--", "--", "--");
                string l = File.Exists($"{gpu}/device/gpu_busy_percent") ? File.ReadAllText($"{gpu}/device/gpu_busy_percent").Trim() + "%" : "--%";
                long used = long.Parse(File.ReadAllText($"{gpu}/device/mem_info_vram_used").Trim()), total = long.Parse(File.ReadAllText($"{gpu}/device/mem_info_vram_total").Trim());
                string vr = vUnit == "GB" ? $"{Math.Round(used / 1073741824.0, 1)}/{Math.Round(total / 1073741824.0, 1)}GB" : $"{Math.Round(100.0 * used / total, 0)}%";
                string tmp = "--", hw = $"{gpu}/device/hwmon/";
                if (Directory.Exists(hw)) {
                    var d = Directory.GetDirectories(hw).FirstOrDefault();
                    if (d != null && File.Exists($"{d}/temp1_input")) { int c = int.Parse(File.ReadAllText($"{d}/temp1_input").Trim()) / 1000; tmp = tUnit == "°C" ? $"{c}°C" : $"{(c * 9 / 5) + 32}°F"; }
                }
                return (l, vr, tmp);
            } catch { return ("--", "--", "--"); }
        }

        public static string GetRamUsage(string unit)
        {
            if (!IsLinux) return "??GB";
            try {
                var m = File.ReadLines("/proc/meminfo").ToList();
                long t = long.Parse(m.FirstOrDefault(x => x.StartsWith("MemTotal:"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1] ?? "0");
                long a = long.Parse(m.FirstOrDefault(x => x.StartsWith("MemAvailable:"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1] ?? "0");
                return unit == "GB" ? $"{Math.Round((t - a) / 1048576.0, 1)}/{Math.Round(t / 1048576.0, 1)}GB" : $"{Math.Round(100.0 * (t - a) / t, 0)}%";
            } catch { return "--"; }
        }
    }

    public static class Updater
    {
        public static string Status = "idle";
        public static bool NewVersionFound = false;
        private static byte[]? _pData;
        private const string StableApiUrl = "https://api.github.com/repos/hollyntt/XOSC/releases/latest";
        public static async Task CheckForUpdates()
        {
            Status = "checking GitHub..."; NewVersionFound = false;
            try {
                using var http = new HttpClient(); http.DefaultRequestHeaders.Add("User-Agent", "XOSC-Updater");
                var r = await http.GetStringAsync(StableApiUrl); using var doc = JsonDocument.Parse(r);
                string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                if (tag == Program.AppVersion) { Status = "already up to date"; return; }
                var asset = doc.RootElement.GetProperty("assets").EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString() == "XOSC.zip");
                string dUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                var z = await http.GetByteArrayAsync(dUrl); using var ms = new MemoryStream(z); using var arch = new ZipArchive(ms);
                var entry = arch.GetEntry("linux-x64/XOSC") ?? arch.GetEntry("XOSC");
                if (entry == null) { Status = "binary not found in zip"; return; }
                Status = "update found!"; NewVersionFound = true;
                using var es = entry.Open(); using var msw = new MemoryStream(); await es.CopyToAsync(msw); _pData = msw.ToArray();
            } catch (Exception e) { Status = $"error: {e.Message}"; }
        }
        public static void ApplyUpdate()
        {
            if (_pData == null) return;
            try {
                string self = Environment.ProcessPath!; Program.SaveConfig();
                File.Move(self, self + ".bak", true); File.WriteAllBytes(self, _pData);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) Process.Start("chmod", $"+x \"{self}\"").WaitForExit();
                Thread.Sleep(500); Process.Start(new ProcessStartInfo(self) { UseShellExecute = true }); Environment.Exit(0);
            } catch (Exception e) { Status = $"apply error: {e.Message}"; }
        }
    }

    public static class MusicChatEngine
    {
        private static UdpClient _client = new();
        private static CancellationTokenSource? _cts;
        private static int _statusIdx = 0;
        private static string _cpu = "CPU", _gpu = "GPU", _music = "Chilling", _weather = "...";
        private static DateTime _lastR = DateTime.MinValue, _lastS = DateTime.MinValue, _manualE = DateTime.MinValue;
        private static string _manualM = "";
        public static int PacketsSent = 0;
        public static string EngineState = "Idle";
        public static readonly object ListLock = new();
        public static void Init() { _client = new UdpClient(); _cts?.Cancel(); _cts = new CancellationTokenSource(); ScrapeHardwareNames(); Task.Run(() => Loop(_cts.Token)); }
        public static void SetManual(string m) { _manualM = m; _manualE = DateTime.Now.AddSeconds(20); }
        private static async Task Loop(CancellationToken t) { while (!t.IsCancellationRequested) { if (Program.Config.ChatboxEnabled) try { await Update(); } catch { } await Task.Delay(1000, t); } }
        private static async Task Update()
        {
            var cfg = Program.Config;
            if (DateTime.Now < _manualE) { EngineState = "Manual"; SendOsc("/chatbox/input", $"💬 {_manualM}"); return; }
            if ((DateTime.Now - _lastR).TotalSeconds >= cfg.Interval) {
                _music = FetchMusic();
                string actualCity = cfg.City == "Custom..." ? cfg.CustomCity : cfg.City;
                if (cfg.WeatherMode && !string.IsNullOrEmpty(actualCity)) _weather = (await new HttpClient().GetStringAsync($"https://wttr.in/{Uri.EscapeDataString(actualCity)}?format=%C+%t")).Trim();
                lock (ListLock) { if (cfg.AutoCycleStatus && cfg.StatusList.Count > 0) _statusIdx = (_statusIdx + 1) % cfg.StatusList.Count; }
                _lastR = DateTime.Now;
            }
            if ((DateTime.Now - _lastS).TotalSeconds < Math.Max(cfg.Interval, 1.5)) return;
            var lines = new List<string>();
            lock (ListLock) { if (cfg.StatusTextMode && cfg.StatusList.Count > _statusIdx) { if (_statusIdx >= cfg.StatusList.Count) _statusIdx = 0; lines.Add(cfg.StatusList[_statusIdx]); } }
            string actualPronouns = cfg.Pronouns == "Custom..." ? cfg.CustomPronouns : cfg.Pronouns;
            if (cfg.PronounsMode && !string.IsNullOrEmpty(actualPronouns)) lines.Add($"{cfg.StatusIcon} {actualPronouns}");
            var env = new List<string>();
            if (cfg.TimeMode) env.Add($"🕒 {DateTime.Now:hh:mm tt}");
            if (cfg.DistroMode) env.Add("| " + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" : "Fedora"));
            if (cfg.WeatherMode) env.Add($"| 🌤️ {_weather}");
            if (env.Count > 0) lines.Add(string.Join(" ", env));
            if (cfg.PcMode) {
                var g = HardwareService.GetGpuStats(cfg.GpuUnit, cfg.VramUnit, cfg.TempUnit);
                string c = cfg.CpuUnit == "Watt" ? "--W" : HardwareService.GetCpuLoad();
                if (cfg.CpuTempOn) c += $" ({HardwareService.GetCpuTemp(cfg.TempUnit)})";
                string cpuL = cfg.CustomCpuNameOn ? cfg.CustomCpuName : (cfg.HwNameMode ? Stylize(_cpu) : "CPU");
                string gpuL = cfg.CustomGpuNameOn ? cfg.CustomGpuName : (cfg.HwNameMode ? Stylize(_gpu) : "GPU");
                lines.Add($"🖥️ {cpuL}: {c} | 🎮 {gpuL}: {g.Load} ({g.Temp})");
                var mem = new List<string>();
                if (cfg.ShowRam) mem.Add($"🐏 ʳᵃᵐ: {HardwareService.GetRamUsage(cfg.RamUnit)}");
                if (cfg.ShowVram) mem.Add($"🎞️ ᵛʳᵃᵐ: {g.Vram}");
                if (mem.Count > 0) lines.Add(string.Join(" | ", mem));
            }
            if (cfg.NetMode) lines.Add($"🌐 {new System.Net.NetworkInformation.Ping().Send("1.1.1.1", 300).RoundtripTime}ms");
            if (cfg.SongMode && _music != "Chilling") lines.Add($"♪ {_music}");
            string output = string.Join("\n", lines);
            if (cfg.ThinMode) { if (output.Length > 138) output = output.Substring(0, 138); output += "\u0003\u001f"; }
            SendOsc("/chatbox/input", output); _lastS = DateTime.Now; PacketsSent++; EngineState = "Idle";
        }
        private static void SendOsc(string addr, string text) {
            try { List<byte> p = new(); void Add(string s) { byte[] b = Encoding.UTF8.GetBytes(s); p.AddRange(b); p.Add(0); while (p.Count % 4 != 0) p.Add(0); }
                Add(addr); Add(",sTT"); Add(text); _client.Send(p.ToArray(), p.Count, "127.0.0.1", 9000);
            } catch { }
        }
        private static string FetchMusic() {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                StringBuilder buff = new StringBuilder(256); IntPtr handle = GetForegroundWindow();
                if (GetWindowText(handle, buff, 256) > 0) {
                    string t = buff.ToString(); if (t.Contains("Spotify") || t.Contains("YouTube") || t.Contains("SoundCloud"))
                        return Regex.Replace(t, @" - (Spotify|YouTube|SoundCloud).*", "").Trim();
                }
                return "Chilling";
            }
            try {
                var psi = new ProcessStartInfo("playerctl", "metadata --format \"{{artist}} - {{title}}\"") { RedirectStandardOutput = true, UseShellExecute = false };
                using var p = Process.Start(psi); string r = p?.StandardOutput.ReadToEnd().Trim() ?? "";
                if (!string.IsNullOrEmpty(r) && r != " - ") return r;
                return "Chilling";
            } catch { return "Chilling"; }
        }
        private static void ScrapeHardwareNames() {
            string log = Program.FindVrcLog();
            if (log != null) foreach (var l in File.ReadLines(log).Take(1500)) {
                if (l.Contains("Processor Type:")) _cpu = Regex.Replace(l.Split(": ")[1], @"(\s\d+-Core| Processor| @.*|AMD |Intel |Core |Ryzen )", "").Trim();
                if (l.Contains("Graphics Device Name:")) _gpu = Regex.Replace(l.Split(": ")[1], @"(\(RADV.*| Graphics|AMD |NVIDIA |GeForce |Radeon |\sRX\s|\sXT)", "").Trim();
            }
        }
        private static string Stylize(string t) {
            string n = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", s = "ᵃᵇᶜᵈᵉᶠᵍʰᶦʲᵏˡᵐⁿᵒᵖᵠʳˢᵗᵘᵛʷˣʸᶻᵃᵇᶜᵈᵉᶠᵍʰᶦʲᵏˡᵐⁿᵒᵖᵠʳˢᵗᵘᵛʷˣʸᶻ⁰¹²³⁴⁵⁶⁷⁸⁹";
            StringBuilder sb = new(); foreach (char c in t) { int i = n.IndexOf(c); sb.Append(i != -1 ? s[i] : c); }
            return sb.ToString();
        }[DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();[DllImport("user32.dll")] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    }

    class Program
    {
        public const string AppVersion = "dev";
        public static AppConfig Config = new();
        private static string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "xosc", "config.json");
        private static string _chatIn = "";
        private static Mutex? _mtx; private static int _navPage = 0;
        private static readonly string[] _navLabels = { "Dashboard", "Statuses", "Chatbox", "Hardware", "Network", "Updater" };
        private static readonly Vector4 ColAccent = new(0.38f, 0.73f, 1.00f, 1f), ColBg = new(0.10f, 0.10f, 0.13f, 1f), ColSidebar = new(0.07f, 0.07f, 0.09f, 1f), ColCard = new(0.14f, 0.14f, 0.18f, 1f);

        private static readonly string[] _pronounsList = { "He/Him", "She/Her", "They/Them", "He/They", "She/They", "It/Its", "Any", "Custom..." };
        
        // All 195 Recognized Countries
        private static readonly string[] _countriesList = {
            "Afghanistan", "Albania", "Algeria", "Andorra", "Angola", "Antigua and Barbuda", "Argentina", "Armenia", "Australia", "Austria",
            "Azerbaijan", "Bahamas", "Bahrain", "Bangladesh", "Barbados", "Belarus", "Belgium", "Belize", "Benin", "Bhutan", "Bolivia",
            "Bosnia and Herzegovina", "Botswana", "Brazil", "Brunei", "Bulgaria", "Burkina Faso", "Burundi", "Cabo Verde", "Cambodia",
            "Cameroon", "Canada", "Central African Republic", "Chad", "Chile", "China", "Colombia", "Comoros", "Congo", "Costa Rica",
            "Croatia", "Cuba", "Cyprus", "Czechia", "Democratic Republic of the Congo", "Denmark", "Djibouti", "Dominica", "Dominican Republic",
            "Ecuador", "Egypt", "El Salvador", "Equatorial Guinea", "Eritrea", "Estonia", "Eswatini", "Ethiopia", "Fiji", "Finland", "France",
            "Gabon", "Gambia", "Georgia", "Germany", "Ghana", "Greece", "Grenada", "Guatemala", "Guinea", "Guinea-Bissau", "Guyana", "Haiti",
            "Honduras", "Hungary", "Iceland", "India", "Indonesia", "Iran", "Iraq", "Ireland", "Israel", "Italy", "Jamaica", "Japan", "Jordan",
            "Kazakhstan", "Kenya", "Kiribati", "Kuwait", "Kyrgyzstan", "Laos", "Latvia", "Lebanon", "Lesotho", "Liberia", "Libya", "Liechtenstein",
            "Lithuania", "Luxembourg", "Madagascar", "Malawi", "Malaysia", "Maldives", "Mali", "Malta", "Marshall Islands", "Mauritania",
            "Mauritius", "Mexico", "Micronesia", "Moldova", "Monaco", "Mongolia", "Montenegro", "Morocco", "Mozambique", "Myanmar", "Namibia",
            "Nauru", "Nepal", "Netherlands", "New Zealand", "Nicaragua", "Niger", "Nigeria", "North Korea", "North Macedonia", "Norway", "Oman",
            "Pakistan", "Palau", "Palestine", "Panama", "Papua New Guinea", "Paraguay", "Peru", "Philippines", "Poland", "Portugal", "Qatar",
            "Romania", "Russia", "Rwanda", "Saint Kitts and Nevis", "Saint Lucia", "Saint Vincent and the Grenadines", "Samoa", "San Marino",
            "Sao Tome and Principe", "Saudi Arabia", "Senegal", "Serbia", "Seychelles", "Sierra Leone", "Singapore", "Slovakia", "Slovenia",
            "Solomon Islands", "Somalia", "South Africa", "South Korea", "South Sudan", "Spain", "Sri Lanka", "Sudan", "Suriname", "Sweden",
            "Switzerland", "Syria", "Tajikistan", "Tanzania", "Thailand", "Timor-Leste", "Togo", "Tonga", "Trinidad and Tobago", "Tunisia",
            "Turkey", "Turkmenistan", "Tuvalu", "Uganda", "Ukraine", "United Arab Emirates", "United Kingdom", "United States", "Uruguay",
            "Uzbekistan", "Vanuatu", "Vatican City", "Venezuela", "Vietnam", "Yemen", "Zambia", "Zimbabwe", "Custom..."
        };

        // Complete States/Provinces for Major Countries
        private static readonly Dictionary<string, string[]> _statesMap = new() {
            { "United States", new[] { "Alabama", "Alaska", "Arizona", "Arkansas", "California", "Colorado", "Connecticut", "Delaware", "Florida", "Georgia", "Hawaii", "Idaho", "Illinois", "Indiana", "Iowa", "Kansas", "Kentucky", "Louisiana", "Maine", "Maryland", "Massachusetts", "Michigan", "Minnesota", "Mississippi", "Missouri", "Montana", "Nebraska", "Nevada", "New Hampshire", "New Jersey", "New Mexico", "New York", "North Carolina", "North Dakota", "Ohio", "Oklahoma", "Oregon", "Pennsylvania", "Rhode Island", "South Carolina", "South Dakota", "Tennessee", "Texas", "Utah", "Vermont", "Virginia", "Washington", "West Virginia", "Wisconsin", "Wyoming", "Custom..." } },
            { "Canada", new[] { "Alberta", "British Columbia", "Manitoba", "New Brunswick", "Newfoundland and Labrador", "Nova Scotia", "Ontario", "Prince Edward Island", "Quebec", "Saskatchewan", "Northwest Territories", "Nunavut", "Yukon", "Custom..." } },
            { "Australia", new[] { "New South Wales", "Victoria", "Queensland", "Western Australia", "South Australia", "Tasmania", "Australian Capital Territory", "Northern Territory", "Custom..." } },
            { "United Kingdom", new[] { "England", "Scotland", "Wales", "Northern Ireland", "Custom..." } }
        };

        // Massive list of major cities for every state
        private static readonly Dictionary<string, string[]> _citiesMap = new() {
            // US Cities
            { "Alabama", new[] { "Birmingham", "Montgomery", "Huntsville", "Mobile", "Custom..." } },
            { "Alaska", new[] { "Anchorage", "Fairbanks", "Juneau", "Custom..." } },
            { "Arizona", new[] { "Phoenix", "Tucson", "Mesa", "Custom..." } },
            { "Arkansas", new[] { "Little Rock", "Fayetteville", "Fort Smith", "Custom..." } },
            { "California", new[] { "Los Angeles", "San Francisco", "San Diego", "Sacramento", "San Jose", "Custom..." } },
            { "Colorado", new[] { "Denver", "Colorado Springs", "Aurora", "Custom..." } },
            { "Connecticut", new[] { "Bridgeport", "New Haven", "Hartford", "Custom..." } },
            { "Delaware", new[] { "Wilmington", "Dover", "Newark", "Custom..." } },
            { "Florida", new[] { "Miami", "Orlando", "Tampa", "Jacksonville", "Tallahassee", "Custom..." } },
            { "Georgia", new[] { "Atlanta", "Augusta", "Savannah", "Custom..." } },
            { "Hawaii", new[] { "Honolulu", "Hilo", "Kailua", "Custom..." } },
            { "Idaho", new[] { "Boise", "Meridian", "Nampa", "Custom..." } },
            { "Illinois", new[] { "Chicago", "Aurora", "Springfield", "Custom..." } },
            { "Indiana", new[] { "Indianapolis", "Fort Wayne", "Evansville", "Custom..." } },
            { "Iowa", new[] { "Des Moines", "Cedar Rapids", "Davenport", "Custom..." } },
            { "Kansas", new[] { "Wichita", "Overland Park", "Kansas City", "Custom..." } },
            { "Kentucky", new[] { "Louisville", "Lexington", "Bowling Green", "Custom..." } },
            { "Louisiana", new[] { "New Orleans", "Baton Rouge", "Shreveport", "Custom..." } },
            { "Maine", new[] { "Portland", "Lewiston", "Bangor", "Custom..." } },
            { "Maryland", new[] { "Baltimore", "Annapolis", "Frederick", "Custom..." } },
            { "Massachusetts", new[] { "Boston", "Worcester", "Springfield", "Custom..." } },
            { "Michigan", new[] { "Detroit", "Grand Rapids", "Lansing", "Custom..." } },
            { "Minnesota", new[] { "Minneapolis", "St. Paul", "Rochester", "Custom..." } },
            { "Mississippi", new[] { "Jackson", "Gulfport", "Southaven", "Custom..." } },
            { "Missouri", new[] { "Kansas City", "St. Louis", "Springfield", "Custom..." } },
            { "Montana", new[] { "Billings", "Missoula", "Great Falls", "Custom..." } },
            { "Nebraska", new[] { "Omaha", "Lincoln", "Bellevue", "Custom..." } },
            { "Nevada", new[] { "Las Vegas", "Henderson", "Reno", "Custom..." } },
            { "New Hampshire", new[] { "Manchester", "Nashua", "Concord", "Custom..." } },
            { "New Jersey", new[] { "Newark", "Jersey City", "Paterson", "Custom..." } },
            { "New Mexico", new[] { "Albuquerque", "Las Cruces", "Rio Rancho", "Custom..." } },
            { "New York", new[] { "New York City", "Buffalo", "Rochester", "Albany", "Syracuse", "Custom..." } },
            { "North Carolina", new[] { "Charlotte", "Raleigh", "Greensboro", "Custom..." } },
            { "North Dakota", new[] { "Fargo", "Bismarck", "Grand Forks", "Custom..." } },
            { "Ohio", new[] { "Columbus", "Cleveland", "Cincinnati", "Custom..." } },
            { "Oklahoma", new[] { "Oklahoma City", "Tulsa", "Norman", "Custom..." } },
            { "Oregon", new[] { "Portland", "Salem", "Eugene", "Custom..." } },
            { "Pennsylvania", new[] { "Philadelphia", "Pittsburgh", "Allentown", "Custom..." } },
            { "Rhode Island", new[] { "Providence", "Warwick", "Cranston", "Custom..." } },
            { "South Carolina", new[] { "Charleston", "Columbia", "North Charleston", "Custom..." } },
            { "South Dakota", new[] { "Sioux Falls", "Rapid City", "Aberdeen", "Custom..." } },
            { "Tennessee", new[] { "Nashville", "Memphis", "Knoxville", "Custom..." } },
            { "Texas", new[] { "Houston", "San Antonio", "Dallas", "Austin", "Fort Worth", "Custom..." } },
            { "Utah", new[] { "Salt Lake City", "West Valley City", "Provo", "Custom..." } },
            { "Vermont", new[] { "Burlington", "South Burlington", "Rutland", "Custom..." } },
            { "Virginia", new[] { "Virginia Beach", "Norfolk", "Chesapeake", "Richmond", "Custom..." } },
            { "Washington", new[] { "Seattle", "Spokane", "Tacoma", "Custom..." } },
            { "West Virginia", new[] { "Charleston", "Huntington", "Morgantown", "Custom..." } },
            { "Wisconsin", new[] { "Milwaukee", "Madison", "Green Bay", "Custom..." } },
            { "Wyoming", new[] { "Cheyenne", "Casper", "Laramie", "Custom..." } },

            // Canada Cities
            { "Alberta", new[] { "Calgary", "Edmonton", "Red Deer", "Custom..." } },
            { "British Columbia", new[] { "Vancouver", "Victoria", "Kelowna", "Custom..." } },
            { "Manitoba", new[] { "Winnipeg", "Brandon", "Steinbach", "Custom..." } },
            { "New Brunswick", new[] { "Moncton", "Saint John", "Fredericton", "Custom..." } },
            { "Newfoundland and Labrador", new[] { "St. John's", "Corner Brook", "Mount Pearl", "Custom..." } },
            { "Nova Scotia", new[] { "Halifax", "Sydney", "Truro", "Custom..." } },
            { "Ontario", new[] { "Toronto", "Ottawa", "Mississauga", "Hamilton", "Custom..." } },
            { "Prince Edward Island", new[] { "Charlottetown", "Summerside", "Stratford", "Custom..." } },
            { "Quebec", new[] { "Montreal", "Quebec City", "Laval", "Custom..." } },
            { "Saskatchewan", new[] { "Saskatoon", "Regina", "Prince Albert", "Custom..." } },
            { "Northwest Territories", new[] { "Yellowknife", "Hay River", "Inuvik", "Custom..." } },
            { "Nunavut", new[] { "Iqaluit", "Rankin Inlet", "Arviat", "Custom..." } },
            { "Yukon", new[] { "Whitehorse", "Dawson City", "Watson Lake", "Custom..." } },

            // Australia Cities
            { "New South Wales", new[] { "Sydney", "Newcastle", "Wollongong", "Custom..." } },
            { "Victoria", new[] { "Melbourne", "Geelong", "Ballarat", "Custom..." } },
            { "Queensland", new[] { "Brisbane", "Gold Coast", "Sunshine Coast", "Custom..." } },
            { "Western Australia", new[] { "Perth", "Mandurah", "Bunbury", "Custom..." } },
            { "South Australia", new[] { "Adelaide", "Mount Gambier", "Gawler", "Custom..." } },
            { "Tasmania", new[] { "Hobart", "Launceston", "Devonport", "Custom..." } },
            { "Australian Capital Territory", new[] { "Canberra", "Custom..." } },
            { "Northern Territory", new[] { "Darwin", "Alice Springs", "Katherine", "Custom..." } },

            // UK Cities
            { "England", new[] { "London", "Birmingham", "Manchester", "Liverpool", "Leeds", "Custom..." } },
            { "Scotland", new[] { "Glasgow", "Edinburgh", "Aberdeen", "Dundee", "Custom..." } },
            { "Wales", new[] { "Cardiff", "Swansea", "Newport", "Custom..." } },
            { "Northern Ireland", new[] { "Belfast", "Derry", "Lisburn", "Custom..." } }
        };

        public static void Main() {
            _mtx = new Mutex(true, "XOSC_VRC_Unique_Runner", out bool fresh); if (!fresh) Environment.Exit(0);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)); LoadConfig();
            if (Config.SavedVersion != AppVersion) { Config.SavedVersion = AppVersion; SaveConfig(); }
            MusicChatEngine.Init(); Raylib.InitWindow(960, 640, "XOSC"); Raylib.SetWindowState(ConfigFlags.ResizableWindow); rlImGui.Setup(true); Raylib.SetTargetFPS(60); ApplyTheme();
            while (!Raylib.WindowShouldClose()) { Raylib.BeginDrawing(); Raylib.ClearBackground(new Color(26, 26, 33, 255)); rlImGui.Begin(); DrawUI(); rlImGui.End(); Raylib.EndDrawing(); }
            SaveConfig(); Raylib.CloseWindow();
        }

        public static string FindVrcLog() {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] stems = { Path.Combine(home, ".local/share/Steam"), Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam") };
            foreach (var b in stems) {
                if (!Directory.Exists(b)) continue;
                string vdf = Path.Combine(b, "steamapps", "libraryfolders.vdf");
                List<string> libs = new() { b };
                if (File.Exists(vdf)) {
                    var ms = Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"(.+?)\"");
                    foreach (Match m in ms) libs.Add(m.Groups[1].Value.Replace("\\\\", "/"));
                }
                foreach (var lib in libs) {
                    string p = Path.Combine(lib, "steamapps/compatdata/438100/pfx/drive_c/users/steamuser/AppData/LocalLow/VRChat/VRChat");
                    if (Directory.Exists(p)) return Directory.GetFiles(p, "output_log_*.txt").OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
                }
            }
            return null;
        }

        public static string GetGpuPath() {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return null;
            return Directory.GetDirectories("/sys/class/drm/").Where(d => d.Contains("card")).OrderByDescending(d => File.Exists($"{d}/device/mem_info_vram_total") ? long.Parse(File.ReadAllText($"{d}/device/mem_info_vram_total").Trim()) : 0).FirstOrDefault();
        }

        static void ApplyTheme() { var s = ImGui.GetStyle(); s.WindowRounding = 0; s.ChildRounding = 6; s.FrameRounding = 5; s.PopupRounding = 5; s.ScrollbarRounding = 5; s.GrabRounding = 4; s.TabRounding = 5; s.WindowPadding = new Vector2(12, 12); }
        static void DrawUI() {
            int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(sw, sh));
            ImGui.Begin("##root", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, ColSidebar); ImGui.BeginChild("##sidebar", new Vector2(172, sh), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
            ImGui.Dummy(new Vector2(0, 20)); ImGui.SetCursorPosX(20); ImGui.TextColored(ColAccent, "XOSC"); ImGui.SetCursorPosX(20); ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.6f, 1f), $"v{AppVersion}"); ImGui.Dummy(new Vector2(0, 20));
            for (int i = 0; i < _navLabels.Length; i++) { bool active = _navPage == i; ImGui.PushStyleColor(ImGuiCol.Button, active ? new Vector4(0.38f, 0.73f, 1.00f, 0.13f) : Vector4.Zero); ImGui.PushStyleColor(ImGuiCol.Text, active ? ColAccent : new Vector4(0.72f, 0.72f, 0.80f, 1f));
                ImGui.SetCursorPosX(10); if (ImGui.Button(_navLabels[i], new Vector2(152, 36))) _navPage = i; ImGui.PopStyleColor(2); }
            ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.ChildBg, ColBg);
            ImGui.BeginChild("##content", new Vector2(sw - 172, sh), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar); ImGui.Dummy(new Vector2(0, 24));
            switch (_navPage) {
                case 0: Card("Dashboard", () => { ImGui.Text($"Engine State: {MusicChatEngine.PacketsSent}"); ImGui.InputText("Override##field", ref _chatIn, 128); if (ImGui.Button("Send")) { MusicChatEngine.SetManual(_chatIn); _chatIn = ""; } }); break;
                case 1: Card("Statuses", () => { int toRem = -1; lock (MusicChatEngine.ListLock) { for (int i = 0; i < Config.StatusList.Count; i++) { string s = Config.StatusList[i]; ImGui.PushID(i); if (ImGui.InputText("##s", ref s, 100)) Config.StatusList[i] = s; ImGui.SameLine(); if (ImGui.Button("X")) toRem = i; ImGui.PopID(); } if (toRem != -1) { Config.StatusList.RemoveAt(toRem); SaveConfig(); } } if (ImGui.Button("+ Add")) Config.StatusList.Add("New Status"); }); break;
                case 2: Card("Chatbox", () => { 
                    Toggle("Status Text", ref Config.StatusTextMode); 
                    Toggle("Pronouns##Toggle", ref Config.PronounsMode); 
                    Toggle("Song Mode", ref Config.SongMode); 
                    Toggle("Time", ref Config.TimeMode); 
                    Toggle("Distro", ref Config.DistroMode); 
                    Toggle("Weather", ref Config.WeatherMode); 
                    Toggle("Thin Mode", ref Config.ThinMode); 
                    Toggle("Auto-Cycle", ref Config.AutoCycleStatus); 
                    
                    DrawCombo("Pronouns", _pronounsList, ref Config.Pronouns, ref Config.CustomPronouns);
                    DrawCombo("Country", _countriesList, ref Config.Country, ref Config.CustomCountry);
                    
                    string[] states = _statesMap.ContainsKey(Config.Country) ? _statesMap[Config.Country] : new[] { "Custom..." };
                    DrawCombo("State", states, ref Config.State, ref Config.CustomState);
                    
                    string[] cities = _citiesMap.ContainsKey(Config.State) ? _citiesMap[Config.State] : new[] { "Custom..." };
                    DrawCombo("City", cities, ref Config.City, ref Config.CustomCity);

                    ImGui.SliderInt("Interval##slider", ref Config.Interval, 1, 60); 
                }); break;
                case 3: Card("Hardware", () => { Toggle("Show Stats", ref Config.PcMode); Toggle("Show RAM", ref Config.ShowRam); Toggle("Show VRAM", ref Config.ShowVram); Toggle("Stylized Names", ref Config.HwNameMode); Toggle("CPU Temp", ref Config.CpuTempOn); Toggle("GPU Temp", ref Config.GpuTempOn); Toggle("Custom CPU Name", ref Config.CustomCpuNameOn); if (Config.CustomCpuNameOn) ImGui.InputText("##c_cpu", ref Config.CustomCpuName, 32); Toggle("Custom GPU Name", ref Config.CustomGpuNameOn); if (Config.CustomGpuNameOn) ImGui.InputText("##c_gpu", ref Config.CustomGpuName, 32); }); break;
                case 4: Card("Network", () => { Toggle("Internet Ping", ref Config.NetMode); }); break;
                case 5: Card("Updater", () => { if (ImGui.Button("Check for Update")) Task.Run(() => Updater.CheckForUpdates()); if (Updater.NewVersionFound && ImGui.Button("Apply Update")) Updater.ApplyUpdate(); ImGui.Text($"Status: {Updater.Status}"); }); break;
            }
            ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.End();
        }

        static void DrawCombo(string label, string[] items, ref string selected, ref string customVal) {
            int idx = Array.IndexOf(items, selected);
            if (idx == -1) { 
                // If a user manually edited the JSON and loaded a value not in the list, 
                // automatically move it to the custom text box so it doesn't get deleted!
                if (!string.IsNullOrEmpty(selected) && selected != "Custom...") customVal = selected;
                idx = items.Length - 1; 
                selected = "Custom..."; 
            }
            
            if (ImGui.Combo(label, ref idx, items, items.Length)) { 
                selected = items[idx]; 
                SaveConfig(); 
            }
            
            if (selected == "Custom...") { 
                if (ImGui.InputText("Custom " + label, ref customVal, 64)) {
                    SaveConfig();
                }
            }
        }
        
        static void Card(string t, Action d) { ImGui.SetCursorPosX(24); ImGui.TextColored(new Vector4(0.92f, 0.92f, 0.97f, 1f), t); ImGui.Dummy(new Vector2(0, 8)); ImGui.SetCursorPosX(24); ImGui.PushStyleColor(ImGuiCol.ChildBg, ColCard); ImGui.BeginChild($"##c{t}", new Vector2(ImGui.GetContentRegionAvail().X - 48, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY); ImGui.Dummy(new Vector2(0, 10)); d(); ImGui.Dummy(new Vector2(0, 10)); ImGui.EndChild(); ImGui.PopStyleColor(); }
        static void Toggle(string l, ref bool v) { if (ImGui.Checkbox(l, ref v)) SaveConfig(); }
        public static void SaveConfig() { try { Directory.CreateDirectory(Path.GetDirectoryName(_path)); var options = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }; File.WriteAllText(_path, JsonSerializer.Serialize(Config, options)); } catch { } }
        static void LoadConfig() { if (!File.Exists(_path)) return; try { var options = new JsonSerializerOptions { IncludeFields = true }; var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_path), options); if (loaded != null) Config = loaded; } catch { } }
    }
}