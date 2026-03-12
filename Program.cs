using System.Diagnostics;
using System.Net;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ImGuiNET;
using Microsoft.Data.Sqlite;
using Raylib_cs;
using rlImGui_cs;
using VRChat.API.Api;
using VRChat.API.Client;
using File = System.IO.File;
using Color = Raylib_cs.Color;

namespace Unfriendmaxxing
{
    public static class Paths
    {
        public static readonly string AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRChatUnfriendManager");
        public static readonly string CookieFile = Path.Combine(AppDataFolder, "session.cookie");
        public static readonly string ConfigFile = Path.Combine(AppDataFolder, "user.config");

        public static string VrcxBase => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX");

        public static string VrcxStartup => Path.Combine(VrcxBase, "startup");

        public static void EnsureExists() => Directory.CreateDirectory(AppDataFolder);
    }

    public class SafeLimitedUserFriend
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string LastLogin { get; set; } = "";
        /// <summary>Total time spent together in milliseconds (from VRCX).</summary>
        public long TimeSpentMs { get; set; } = 0;
        /// <summary>Profile picture thumbnail URL from VRChat API (~256px).</summary>
        public string ThumbnailUrl { get; set; } = "";
    }

    public class AppConfig
    {
        public string Username { get; set; } = "";
        public string EncodedPassword { get; set; } = "";
        public string Cookie { get; set; } = "";
        public bool RememberMe { get; set; } = true;
        public bool ExcludeFavorites { get; set; } = true;
        public bool InactiveEnabled { get; set; } = false;
        public int InactiveValue { get; set; } = 3;
        public int InactiveUnitIndex { get; set; } = 1;
        /// <summary>Filter: only show friends with less than this many minutes together (0 = disabled).</summary>
        public bool TogetherFilterEnabled { get; set; } = false;
        /// <summary>Max together time value for the filter.</summary>
        public int TogetherFilterValue { get; set; } = 60;
        /// <summary>Unit: 0=Minutes, 1=Hours, 2=Days</summary>
        public int TogetherFilterUnit { get; set; } = 1;
        public int SortOptionIndex { get; set; } = 0;
        public bool AutoUnfriendEnabled { get; set; } = false;
        public int AutoUnfriendHour { get; set; } = 3;
        public int AutoUnfriendMinute { get; set; } = 0;
        public int AutoUnfriendMode { get; set; } = 0;
        public bool RunOnStartup { get; set; } = false;
        public bool VrcxStartupDesktop { get; set; } = false;
        public bool VrcxStartupVr { get; set; } = false;
        public List<string> ExcludedFavGroups { get; set; } = new();
    }

    // --- API Service ---
    public class VRChatApiService
    {
        private const string UA = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";
        private static readonly Uri BaseUri = new("https://api.vrchat.cloud/api/1/");
        private readonly HttpClient client;
        private readonly CookieContainer cookies = new();
        private Configuration? cfg;
        private TaskCompletionSource<string?>? tfaTcs;
        private string tfaCode = "";
        private bool show2FADialog = false;

        public VRChatApiService()
        {
            var handler = new HttpClientHandler { CookieContainer = cookies, UseCookies = true, AllowAutoRedirect = true };
            client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UA);
        }

        private void SaveCookies()
        {
            Paths.EnsureExists();
            var authCookie = cookies.GetCookies(BaseUri)["auth"];
            var tfaCookie = cookies.GetCookies(BaseUri)["twoFactorAuth"];
            if (authCookie == null) return;

            var fullCookie = $"auth={authCookie.Value}";
            if (tfaCookie != null && !string.IsNullOrEmpty(tfaCookie.Value))
                fullCookie += $"; twoFactorAuth={tfaCookie.Value}";

            try { File.WriteAllText(Paths.CookieFile, fullCookie); } catch { }
            Program.config.Cookie = fullCookie;
            Program.SaveConfig();

            // Keep the image downloader authenticated
            TextureCache.SetCookie(fullCookie);
        }

        private async Task<bool> TestSessionAsync()
        {
            if (cfg == null) return false;
            try
            {
                using var test = new HttpClient();
                test.DefaultRequestHeaders.UserAgent.ParseAdd(UA);
                if (cfg.DefaultHeaders.TryGetValue("Cookie", out var c))
                    test.DefaultRequestHeaders.Add("Cookie", c);
                var r = await test.GetAsync("https://api.vrchat.cloud/api/1/auth/user");
                if (!r.IsSuccessStatusCode) return false;
                var body = await r.Content.ReadAsStringAsync();
                if (!body.Contains("\"id\"", StringComparison.OrdinalIgnoreCase)) return false;
                // Opportunistically grab the user ID so GetFavoriteGroupNamesAsync works immediately
                var (_, userId) = ParseUserFromJson(body);
                if (!string.IsNullOrWhiteSpace(userId)) CurrentUserId = userId;
                return true;
            }
            catch { return false; }
        }

        private async Task<string?> GetCurrentDisplayNameAsync()
        {
            if (cfg == null) return null;
            try
            {
                var user = await new AuthenticationApi(cfg).GetCurrentUserAsync();
                CurrentUserId = user?.Id;
                var name = user?.DisplayName;
                if (!string.IsNullOrWhiteSpace(name)) return name;
                return user?.Username;
            }
            catch { return null; }
        }

        public async Task<(bool success, string? displayName)> RestoreSessionFromDiskOrConfigAsync()
        {
            // 1. Try saved cookie from config
            if (!string.IsNullOrWhiteSpace(Program.config.Cookie) && Program.config.Cookie.Contains("auth="))
            {
                cfg = new Configuration { UserAgent = UA };
                cfg.DefaultHeaders["Cookie"] = Program.config.Cookie.Trim();
                if (await TestSessionAsync())
                {
                    TextureCache.SetCookie(Program.config.Cookie.Trim());
                    var name = await GetCurrentDisplayNameAsync();
                    if (!string.IsNullOrWhiteSpace(name)) return (true, name);
                }
            }

            // 2. Try cookie file on disk
            if (File.Exists(Paths.CookieFile))
            {
                var cookie = await File.ReadAllTextAsync(Paths.CookieFile);
                if (!string.IsNullOrWhiteSpace(cookie) && cookie.Contains("auth="))
                {
                    cfg = new Configuration { UserAgent = UA };
                    cfg.DefaultHeaders["Cookie"] = cookie.Trim();
                    if (await TestSessionAsync())
                    {
                        Program.config.Cookie = cookie.Trim();
                        Program.SaveConfig();
                        TextureCache.SetCookie(cookie.Trim());
                        var name = await GetCurrentDisplayNameAsync();
                        if (!string.IsNullOrWhiteSpace(name)) return (true, name);
                    }
                }
            }

            // 3. Always attempt credential login if we have saved credentials —
            //    regardless of RememberMe. This will surface the 2FA dialog if needed.
            if (!string.IsNullOrEmpty(Program.config.Username) && !string.IsNullOrEmpty(Program.config.EncodedPassword))
            {
                var p = Encoding.UTF8.GetString(Convert.FromBase64String(Program.config.EncodedPassword));
                var (success, name, error) = await LoginWithCredentialsAsync(Program.config.Username, p);
                if (success && !string.IsNullOrWhiteSpace(name)) return (true, name);
            }

            return (false, null);
        }

        public async Task<(bool success, string? displayName, string? error)> LoginWithCredentialsAsync(string username, string password)
        {
            try
            {
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("User-Agent", UA);

                var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);

                var response = await client.GetAsync("https://api.vrchat.cloud/api/1/auth/user");
                var body = await response.Content.ReadAsStringAsync();

                if (body.Contains("requiresTwoFactorAuth"))
                {
                    show2FADialog = true;
                    tfaTcs = new TaskCompletionSource<string?>();

                    var code = await tfaTcs.Task;

                    if (string.IsNullOrEmpty(code))
                    {
                        client.DefaultRequestHeaders.Authorization = null;
                        return (false, null, "2FA Cancelled");
                    }

                    client.DefaultRequestHeaders.Authorization = null;

                    var verifyJson = JsonSerializer.Serialize(new { code = code });
                    var verifyContent = new StringContent(verifyJson, Encoding.UTF8, "application/json");

                    var verifyResp = await client.PostAsync("https://api.vrchat.cloud/api/1/auth/twofactorauth/totp/verify", verifyContent);

                    if (!verifyResp.IsSuccessStatusCode)
                        return (false, null, "2FA Verification Failed");

                    // After 2FA, re-fetch user to get display name
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);
                    var reResp = await client.GetAsync("https://api.vrchat.cloud/api/1/auth/user");
                    body = await reResp.Content.ReadAsStringAsync();
                    client.DefaultRequestHeaders.Authorization = null;
                }
                else if (!response.IsSuccessStatusCode)
                {
                    client.DefaultRequestHeaders.Authorization = null;
                    return (false, null, $"Login failed: {response.StatusCode}");
                }

                client.DefaultRequestHeaders.Authorization = null;

                // Extract auth cookie
                var cookieCollection = cookies.GetCookies(BaseUri);
                Cookie? authCookie = null;
                foreach (Cookie c in cookieCollection) if (c.Name == "auth") authCookie = c;

                if (authCookie == null)
                    return (false, null, "Login succeeded but 'auth' cookie was not set. Check your credentials.");

                string fullCookie = $"auth={authCookie.Value}";
                var tfaCookie = cookies.GetCookies(BaseUri)["twoFactorAuth"];
                if (tfaCookie != null) fullCookie += $"; twoFactorAuth={tfaCookie.Value}";

                cfg = new Configuration();
                cfg.UserAgent = UA;
                cfg.DefaultHeaders ??= new Dictionary<string, string>();
                cfg.DefaultHeaders["Cookie"] = fullCookie;

                SaveCookies();

                // Parse displayName + userId directly from the response body we already have —
                // the /auth/user endpoint returns the full CurrentUser object on success.
                var (displayName, userId) = ParseUserFromJson(body);
                if (!string.IsNullOrWhiteSpace(userId)) CurrentUserId = userId;

                // If JSON parsing failed for some reason, fall back to the SDK call
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    try
                    {
                        var authApi = new AuthenticationApi(cfg);
                        var sdkUser = await authApi.GetCurrentUserAsync();
                        displayName = sdkUser?.DisplayName ?? sdkUser?.Username;
                        CurrentUserId = sdkUser?.Id ?? CurrentUserId;
                    }
                    catch { }
                }

                if (string.IsNullOrWhiteSpace(displayName))
                    return (false, null, "Logged in but could not read display name. Try again.");

                return (true, displayName, null);
            }
            catch (Exception ex)
            {
                client.DefaultRequestHeaders.Authorization = null;
                return (false, null, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Parses displayName and id from VRChat's /auth/user response JSON.
        /// Returns (displayName, userId) — either may be null if parsing fails.
        /// </summary>
        private static (string? displayName, string? userId) ParseUserFromJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string? name = null, id = null;

                if (root.TryGetProperty("displayName", out var dn) && dn.ValueKind == JsonValueKind.String)
                    name = dn.GetString();
                else if (root.TryGetProperty("username", out var un) && un.ValueKind == JsonValueKind.String)
                    name = un.GetString();

                if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    id = idProp.GetString();

                return (name, id);
            }
            catch { }
            return (null, null);
        }

        public void Draw2FADialog()
        {
            if (!show2FADialog || tfaTcs == null) return;

            ImGui.OpenPopup("2FA Required");
            ImGui.SetNextWindowPos(new Vector2(Raylib.GetScreenWidth() / 2f, Raylib.GetScreenHeight() / 2f), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

            if (ImGui.BeginPopupModal("2FA Required", ref show2FADialog, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove))
            {
                ImGui.Text("Two-Factor Authentication Required");
                ImGui.Separator();
                ImGui.TextWrapped("Enter your 2FA code:");
                ImGui.SetNextItemWidth(200);
                ImGui.InputText("##2fa", ref tfaCode, 10, ImGuiInputTextFlags.CharsDecimal);

                if ((ImGui.IsItemFocused() && Raylib.IsKeyPressed(KeyboardKey.Enter)) || ImGui.Button("Submit"))
                {
                    tfaTcs.SetResult(tfaCode.Trim());
                    tfaTcs = null;
                    show2FADialog = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    tfaTcs.SetResult(null);
                    tfaTcs = null;
                    show2FADialog = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
        }

        public FriendsApi Friends => cfg != null ? new FriendsApi(cfg) : throw new InvalidOperationException("Not logged in");
        public FavoritesApi Favorites => cfg != null ? new FavoritesApi(cfg) : throw new InvalidOperationException("Not logged in");

        /// <summary>The logged-in user's VRChat user ID (e.g. usr_xxxx). Set after login.</summary>
        public string? CurrentUserId { get; private set; }

        public async Task UnfriendAsync(string id)
        {
            await Friends.UnfriendAsync(id);
        }

        public async Task<List<SafeLimitedUserFriend>> GetAllFriendsAsync()
        {
            var list = new List<SafeLimitedUserFriend>();
            for (int offset = 0; ; offset += 100)
            {
                var page = await Friends.GetFriendsAsync(offset: offset, n: 100, offline: false);
                list.AddRange(page.Select(u => new SafeLimitedUserFriend
                {
                    Id = u.Id,
                    DisplayName = u.DisplayName ?? "Unknown",
                    LastLogin = u.LastLogin?.ToString("o") ?? "",
                    // Prefer the small thumbnail; fall back to the full image URL
                    ThumbnailUrl = u.CurrentAvatarThumbnailImageUrl
                              ?? u.CurrentAvatarImageUrl
                              ?? u.ProfilePicOverrideThumbnail
                              ?? u.ProfilePicOverride
                              ?? "",
                }));
                if (page.Count < 100) break;
            }
            for (int offset = 0; ; offset += 100)
            {
                var page = await Friends.GetFriendsAsync(offset: offset, n: 100, offline: true);
                list.AddRange(page.Select(u => new SafeLimitedUserFriend
                {
                    Id = u.Id,
                    DisplayName = u.DisplayName ?? "Unknown",
                    LastLogin = u.LastLogin?.ToString("o") ?? "",
                    ThumbnailUrl = u.CurrentAvatarThumbnailImageUrl
                              ?? u.CurrentAvatarImageUrl
                              ?? u.ProfilePicOverrideThumbnail
                              ?? u.ProfilePicOverride
                              ?? "",
                }));
                if (page.Count < 100) break;
            }
            return list;
        }

        /// <summary>
        /// Returns all favorited friend IDs (flat set) AND a mapping of
        /// group tag (e.g. "group_0") -> set of favorited user IDs in that group.
        /// </summary>
        public async Task<(HashSet<string> allIds, Dictionary<string, HashSet<string>> byGroup)> GetFavoritesDetailedAsync()
        {
            var allIds  = new HashSet<string>();
            var byGroup = new Dictionary<string, HashSet<string>>();

            // Pre-seed all four slots so empty groups (0 members) still show in the UI
            for (int i = 0; i < 4; i++) byGroup[$"group_{i}"] = new HashSet<string>();

            for (int offset = 0; ; offset += 100)
            {
                var page = await Favorites.GetFavoritesAsync(type: "friend", n: 100, offset: offset);
                Console.WriteLine($"[FAV] offset={offset} count={page.Count}");
                foreach (var f in page)
                {
                    allIds.Add(f.FavoriteId);
                    var tag = f.Tags?.FirstOrDefault() ?? "group_0";
                    Console.WriteLine($"[FAV]   id={f.FavoriteId} tag={tag}");
                    if (!byGroup.ContainsKey(tag)) byGroup[tag] = new HashSet<string>();
                    byGroup[tag].Add(f.FavoriteId);
                }
                if (page.Count < 100) break;
            }
            Console.WriteLine($"[FAV] Total: {allIds.Count} favorites across {byGroup.Count} groups: {string.Join(", ", byGroup.Keys)}");
            return (allIds, byGroup);
        }

        /// <summary>
        /// Fetches VRChat native favorite group names directly via the authenticated
        /// HTTP client (bypasses SDK which requires ownerId and is unreliable).
        /// Endpoint: GET /api/1/favorite/group?type=friend&n=10&ownerId={userId}
        /// </summary>
        public async Task<Dictionary<string, string>> GetFavoriteGroupNamesAsync()
        {
            var result = new Dictionary<string, string>();

            // Try raw HTTP first — most reliable since client already has auth cookie
            if (!string.IsNullOrEmpty(CurrentUserId))
            {
                try
                {
                    var url = $"https://api.vrchat.cloud/api/1/favorite/group?type=friend&n=10&offset=0&ownerId={CurrentUserId}";
                    Console.WriteLine($"[FAV_GROUPS] GET {url}");
                    var resp = await client.GetAsync(url);
                    var body = await resp.Content.ReadAsStringAsync();
                    Console.WriteLine($"[FAV_GROUPS] HTTP {(int)resp.StatusCode}: {body}");

                    if (resp.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(body);
                        foreach (var g in doc.RootElement.EnumerateArray())
                        {
                            // Each element has: name (tag like "group_0"), displayName (user label), type, ownerId
                            var tag         = g.TryGetProperty("name",        out var n)  ? n.GetString()  : null;
                            var displayName = g.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
                            Console.WriteLine($"[FAV_GROUPS]   tag={tag} displayName={displayName}");
                            if (!string.IsNullOrEmpty(tag))
                                result[tag] = !string.IsNullOrWhiteSpace(displayName) ? displayName : tag;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FAV_GROUPS] Raw HTTP failed: {ex.Message}");
                }
            }

            // Fallback: SDK call
            if (result.Count == 0)
            {
                try
                {
                    Console.WriteLine($"[FAV_GROUPS] Falling back to SDK (ownerId={CurrentUserId})");
                    var favGroups = await Favorites.GetFavoriteGroupsAsync(n: 10, offset: 0, ownerId: CurrentUserId);
                    Console.WriteLine($"[FAV_GROUPS] SDK returned {favGroups.Count} groups");
                    foreach (var g in favGroups)
                    {
                        var tag = g.Tags?.FirstOrDefault() ?? g.Name ?? "";
                        Console.WriteLine($"[FAV_GROUPS]   SDK: name={g.Name} displayName={g.DisplayName} tag={tag}");
                        if (!string.IsNullOrEmpty(tag))
                            result[tag] = !string.IsNullOrWhiteSpace(g.DisplayName) ? g.DisplayName : tag;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FAV_GROUPS] SDK also failed: {ex.Message}");
                }
            }

            // Always ensure all four slots exist — VRChat has group_0 through group_3
            for (int i = 0; i < 4; i++)
            {
                var key = $"group_{i}";
                if (!result.ContainsKey(key)) result[key] = $"Group {i + 1}";
            }

            Console.WriteLine($"[FAV_GROUPS] Final: {string.Join(", ", result.Select(kv => $"{kv.Key}={kv.Value}"))}");
            return result;
        }
    }

    /// <summary>
    /// Thread-safe async texture cache for VRChat profile images.
    /// Call SetCookie() once after login, then RequestTexture() each frame.
    /// Call UnloadAll() on shutdown to free GPU memory.
    /// </summary>
    public static class TextureCache
    {
        private enum State { Downloading, Ready, Failed }

        private sealed class Entry
        {
            public State State;
            public Texture2D Texture;
        }

        private static readonly Dictionary<string, Entry> _cache = new();

        // Separate HttpClient that carries the VRChat auth cookie
        private static readonly CookieContainer _cookieContainer = new();
        private static readonly HttpClient _http = new(
            new HttpClientHandler { CookieContainer = _cookieContainer, UseCookies = true })
        {
            Timeout = TimeSpan.FromSeconds(20),
            DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36" } }
        };

        private static readonly Uri VrcApiBase = new("https://api.vrchat.cloud/");

        /// <summary>
        /// Set (or update) the VRChat auth cookie. Call this right after login / session restore.
        /// </summary>
        public static void SetCookie(string cookieHeader)
        {
            // cookieHeader looks like "auth=xxx" or "auth=xxx; twoFactorAuth=yyy"
            foreach (var part in cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Trim().Split('=', 2);
                if (kv.Length == 2)
                    _cookieContainer.Add(VrcApiBase, new Cookie(kv[0].Trim(), kv[1].Trim()));
            }
        }

        /// <summary>
        /// Returns a loaded Texture2D for the given URL, or null if not yet ready.
        /// Must be called from the main (render) thread.
        /// </summary>
        public static Texture2D? RequestTexture(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            lock (_cache)
            {
                if (_cache.TryGetValue(url, out var entry))
                    return entry.State == State.Ready ? entry.Texture : null;

                _cache[url] = new Entry { State = State.Downloading };
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var bytes = await _http.GetByteArrayAsync(url);
                    if (bytes.Length < 4)
                    {
                        MarkFailed(url);
                        return;
                    }

                    // Detect format from magic bytes rather than URL extension
                    // (VRChat image URLs have no extension and serve JPEG data)
                    string fmt;
                    if (bytes[0] == 0xFF && bytes[1] == 0xD8)
                        fmt = ".jpg";
                    else if (bytes[0] == 0x89 && bytes[1] == 0x50)
                        fmt = ".png";
                    else if (bytes[0] == 0x47 && bytes[1] == 0x49)
                        fmt = ".gif";
                    else
                        fmt = ".jpg"; // VRChat default

                    var img = Raylib.LoadImageFromMemory(fmt, bytes);
                    if (img.Width == 0 || img.Height == 0) { MarkFailed(url); return; }

                    Raylib.ImageResize(ref img, 32, 32);

                    lock (_pendingLoad)
                        _pendingLoad.Add((url, img));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[IMG] Failed {url}: {ex.Message}");
                    MarkFailed(url);
                }
            });

            return null;
        }

        private static void MarkFailed(string url)
        {
            lock (_cache)
                if (_cache.TryGetValue(url, out var e)) e.State = State.Failed;
        }

        private static readonly List<(string url, Image img)> _pendingLoad = new();

        /// <summary>Upload pending textures to GPU. Call once per frame on the render thread.</summary>
        public static void FlushPending()
        {
            List<(string url, Image img)> batch;
            lock (_pendingLoad)
            {
                if (_pendingLoad.Count == 0) return;
                batch = new List<(string, Image)>(_pendingLoad);
                _pendingLoad.Clear();
            }

            foreach (var (url, img) in batch)
            {
                try
                {
                    var tex = Raylib.LoadTextureFromImage(img);
                    Raylib.UnloadImage(img);
                    lock (_cache)
                    {
                        if (_cache.TryGetValue(url, out var entry))
                        {
                            entry.Texture = tex;
                            entry.State = State.Ready;
                        }
                        else Raylib.UnloadTexture(tex);
                    }
                }
                catch
                {
                    lock (_cache)
                        if (_cache.TryGetValue(url, out var e)) e.State = State.Failed;
                }
            }
        }

        /// <summary>Unload all GPU textures. Call on application shutdown.</summary>
        public static void UnloadAll()
        {
            lock (_cache)
            {
                foreach (var entry in _cache.Values)
                    if (entry.State == State.Ready)
                        Raylib.UnloadTexture(entry.Texture);
                _cache.Clear();
            }
        }
    }

    /// <summary>
    /// Reads time-together data from VRCX's local SQLite database.
    /// Requires the NuGet package: Microsoft.Data.Sqlite
    /// Add to your .csproj: &lt;PackageReference Include="Microsoft.Data.Sqlite" Version="8.*" /&gt;
    /// </summary>
    public static class VrcxDataService
    {
        private static string DbPath => Path.Combine(Paths.VrcxBase, "VRCX.sqlite3");
        public static bool IsAvailable => File.Exists(DbPath);

        /// <summary>
        /// Returns userId -> total seconds spent together, computed from VRCX's
        /// gamelog_join_leave table by pairing OnPlayerJoined / OnPlayerLeft events.
        /// Each contiguous session (join→leave) per user is summed across all time.
        /// </summary>
        public static Dictionary<string, long> LoadTimeSpentSeconds()
        {
            var result = new Dictionary<string, long>();
            if (!IsAvailable) return result;

            try
            {
                using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                    $"Data Source={DbPath};Mode=ReadOnly;Cache=Shared");
                connection.Open();

                // gamelog_join_leave columns: id, created_at, type, display_name, user_id, location
                // 'type' is 'OnPlayerJoined' or 'OnPlayerLeft'
                // We order by (user_id, created_at) so we can walk each user's events in order.
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT user_id, created_at, type
                    FROM   gamelog_join_leave
                    WHERE  user_id IS NOT NULL AND user_id != ''
                    ORDER  BY user_id ASC, created_at ASC";

                using var reader = cmd.ExecuteReader();

                // Track the most recent join time per user within the current scan
                var pendingJoin = new Dictionary<string, DateTime>();

                while (reader.Read())
                {
                    var userId = reader.GetString(0);
                    // created_at is stored as ISO-8601 text in VRCX
                    if (!DateTime.TryParse(reader.GetString(1), out var ts)) continue;
                    var type = reader.GetString(2);

                    if (type == "OnPlayerJoined")
                    {
                        // If there's already a pending join (e.g. duplicate), overwrite with latest
                        pendingJoin[userId] = ts;
                    }
                    else if (type == "OnPlayerLeft" && pendingJoin.TryGetValue(userId, out var joinTime))
                    {
                        var secs = (long)(ts - joinTime).TotalSeconds;
                        if (secs > 0)
                        {
                            result.TryGetValue(userId, out var existing);
                            result[userId] = existing + secs;
                        }
                        pendingJoin.Remove(userId);
                    }
                }

                // Any still-pending joins (no matching leave) are ignored — they'd be ongoing sessions
                // or truncated log entries; don't count unfinished time.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VRCX] DB read failed: {ex.Message}");
            }

            return result;
        }
    }

    class Program
    {
        static VRChatApiService api = new();
        static List<SafeLimitedUserFriend> friends = new();
        static HashSet<string> favorites = new();
        // byGroup: tag ("group_0") -> set of user IDs in that VRChat favorite group
        static Dictionary<string, HashSet<string>> favByGroup = new();
        // favGroupNames: tag -> display name as set by the user in VRChat
        static Dictionary<string, string> favGroupNames = new();
        static List<SafeLimitedUserFriend> shown = new();
        static HashSet<int> selected = new();
        static string user = "", pass = "";
        static string loggedInAs = "";
        static bool remember = true;
        static bool hideFavs = true;
        static bool inactiveOn = false;
        static int inactiveVal = 3;
        static int inactiveUnit = 1;
        static bool togetherOn = false;
        static int togetherVal = 60;
        static int togetherUnit = 1; // 0=min 1=hr 2=day
        static string searchText = "";
        static int searchField = 0; // 0=Name, 1=Group
        static int sort = 0;
        static string status = "Starting up...";
        static bool working = false;
        static bool isUnfriending = false;
        static bool isPaused = false;
        static int unfriendTotal = 0;
        static int unfriendDone = 0;
        static CancellationTokenSource? unfriendCts;
        public static AppConfig config = new();
        static CancellationTokenSource? autoCts;
        static readonly string[] units = { "Days", "Months", "Years" };
        static readonly string[] sorts = { "Oldest", "Newest", "A-Z", "Z-A", "Most Time", "Least Time" };
        static readonly string[] autoModes = { "Inactive Only (3+ mo)", "All Shown", "Marked Only" };
        static bool isLoggedIn = false;
        static bool sessionRestored = false;
        static bool shouldExit = false;

        // Windows Specific Imports
        [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_HIDE = 0;

        public static async Task Main(string[] args)
        {
            bool isAutostart = args.Contains("--autostart");

            Console.WriteLine("VRChat Unfriend Manager Starting...");
            Paths.EnsureExists();
            LoadConfig();

            if (Directory.Exists(Paths.VrcxStartup))
            {
                UpdateVrcxShortcut("desktop", config.VrcxStartupDesktop);
                UpdateVrcxShortcut("vr", config.VrcxStartupVr);
            }

            if (config.RunOnStartup) UpdateStartup(true);

            // Standard window (no custom title bar)
            ConfigFlags flags = ConfigFlags.ResizableWindow | ConfigFlags.HighDpiWindow;
            if (isAutostart) flags |= ConfigFlags.HiddenWindow;

            Raylib.SetConfigFlags(flags);
            Raylib.InitWindow(1280, 800, "VRChat Unfriend Manager");

            try
            {
                string iconPath = "icon.png";
                if (!File.Exists(iconPath)) iconPath = "icon.ico";
                if (File.Exists(iconPath))
                {
                    var img = Raylib.LoadImage(iconPath);
                    Raylib.SetWindowIcon(img);
                    Raylib.UnloadImage(img);
                }
            }
            catch { }

            Raylib.SetTargetFPS(60);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#if !DEBUG
                try {
                    var consoleHwnd = GetConsoleWindow();
                    if (consoleHwnd != IntPtr.Zero) ShowWindow(consoleHwnd, SW_HIDE);
                } catch {}
#endif
            }

            rlImGui.Setup(true);
            ApplyTheme();

            user = config.Username;
            remember = config.RememberMe;
            hideFavs = config.ExcludeFavorites;
            inactiveOn = config.InactiveEnabled;
            inactiveVal = config.InactiveValue;
            inactiveUnit = config.InactiveUnitIndex;
            togetherOn   = config.TogetherFilterEnabled;
            togetherVal  = config.TogetherFilterValue;
            togetherUnit = config.TogetherFilterUnit;
            sort = config.SortOptionIndex;

            bool firstFrame = true;

            while (!shouldExit)
            {
                // Background / autostart mode
                if (isAutostart)
                {
                    if (firstFrame)
                    {
                        firstFrame = false;
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(1000);
                            var (restored, name) = await api.RestoreSessionFromDiskOrConfigAsync();
                            if (restored && name != null)
                            {
                                loggedInAs = name;
                                isLoggedIn = true;
                                sessionRestored = true;
                                status = $"Background Login: {name}";
                                if (config.AutoUnfriendEnabled) StartAutoScheduler();
                            }
                        });
                    }

                    Raylib.PollInputEvents();
                    Thread.Sleep(100);
                    continue;
                }

                if (Raylib.WindowShouldClose()) { shouldExit = true; continue; }

                // Resize-aware: get current window size each frame
                int screenW = Raylib.GetScreenWidth();
                int screenH = Raylib.GetScreenHeight();

                rlImGui.Begin();
                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Color(15, 15, 20, 255));

                // Upload any newly-downloaded textures to the GPU before rendering
                TextureCache.FlushPending();

                // Full-window ImGui panel, always tracks window size
                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(new Vector2(screenW, screenH));
                ImGui.Begin("##main", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoTitleBar |
                    ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBringToFrontOnFocus);

                if (firstFrame)
                {
                    firstFrame = false;
                    // Show "Signing in..." immediately if we have credentials to try
                    bool hasCredentials = !string.IsNullOrEmpty(config.Username) &&
                                         !string.IsNullOrEmpty(config.EncodedPassword);
                    bool hasCookie = (!string.IsNullOrEmpty(config.Cookie) && config.Cookie.Contains("auth="))
                                     || File.Exists(Paths.CookieFile);
                    if (hasCredentials || hasCookie)
                        status = "Signing in...";

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(200); // let the window paint once before blocking on network
                        var (restored, name) = await api.RestoreSessionFromDiskOrConfigAsync();
                        if (restored && name != null)
                        {
                            loggedInAs = name;
                            isLoggedIn = true;
                            sessionRestored = true;
                            status = $"Welcome back, {name}";
                            await Refresh();
                            if (config.AutoUnfriendEnabled) StartAutoScheduler();
                        }
                        else
                        {
                            // Clear any stale status — show clean login screen
                            status = string.IsNullOrEmpty(config.Username)
                                ? "Please log in"
                                : "Session expired — please log in again";
                        }
                    });
                }

                if (sessionRestored || isLoggedIn) DrawMainUI();
                else DrawLoginScreen();

                api.Draw2FADialog();
                ImGui.End();
                rlImGui.End();
                Raylib.EndDrawing();
            }

            SaveConfig();
            TextureCache.UnloadAll();
            rlImGui.Shutdown();
            Raylib.CloseWindow();
        }

        // ─── Theme ─────────────────────────────────────────────────────────────────
        static void ApplyTheme()
        {
            var style = ImGui.GetStyle();
            style.WindowRounding = 6f;
            style.FrameRounding = 4f;
            style.ScrollbarRounding = 4f;
            style.GrabRounding = 4f;
            style.TabRounding = 4f;
            style.WindowPadding = new Vector2(12, 12);
            style.FramePadding = new Vector2(6, 4);
            style.ItemSpacing = new Vector2(8, 6);

            var colors = style.Colors;
            colors[(int)ImGuiCol.WindowBg]          = new Vector4(0.10f, 0.10f, 0.14f, 1f);
            colors[(int)ImGuiCol.ChildBg]            = new Vector4(0.08f, 0.08f, 0.12f, 1f);
            colors[(int)ImGuiCol.PopupBg]            = new Vector4(0.12f, 0.12f, 0.16f, 1f);
            colors[(int)ImGuiCol.Border]             = new Vector4(0.25f, 0.25f, 0.35f, 1f);
            colors[(int)ImGuiCol.FrameBg]            = new Vector4(0.16f, 0.16f, 0.22f, 1f);
            colors[(int)ImGuiCol.FrameBgHovered]     = new Vector4(0.22f, 0.22f, 0.30f, 1f);
            colors[(int)ImGuiCol.FrameBgActive]      = new Vector4(0.28f, 0.28f, 0.38f, 1f);
            colors[(int)ImGuiCol.TitleBg]            = new Vector4(0.08f, 0.08f, 0.12f, 1f);
            colors[(int)ImGuiCol.TitleBgActive]      = new Vector4(0.12f, 0.12f, 0.18f, 1f);
            colors[(int)ImGuiCol.Tab]                = new Vector4(0.14f, 0.14f, 0.20f, 1f);
            colors[(int)ImGuiCol.TabHovered]         = new Vector4(0.35f, 0.25f, 0.55f, 1f);
            colors[(int)ImGuiCol.TabSelected]        = new Vector4(0.45f, 0.30f, 0.70f, 1f);
            colors[(int)ImGuiCol.Header]             = new Vector4(0.30f, 0.20f, 0.50f, 0.6f);
            colors[(int)ImGuiCol.HeaderHovered]      = new Vector4(0.40f, 0.27f, 0.65f, 0.8f);
            colors[(int)ImGuiCol.HeaderActive]       = new Vector4(0.50f, 0.35f, 0.75f, 1f);
            colors[(int)ImGuiCol.Button]             = new Vector4(0.30f, 0.20f, 0.50f, 1f);
            colors[(int)ImGuiCol.ButtonHovered]      = new Vector4(0.45f, 0.30f, 0.70f, 1f);
            colors[(int)ImGuiCol.ButtonActive]       = new Vector4(0.55f, 0.40f, 0.80f, 1f);
            colors[(int)ImGuiCol.SliderGrab]         = new Vector4(0.55f, 0.40f, 0.80f, 1f);
            colors[(int)ImGuiCol.SliderGrabActive]   = new Vector4(0.70f, 0.55f, 0.90f, 1f);
            colors[(int)ImGuiCol.CheckMark]          = new Vector4(0.70f, 0.55f, 0.90f, 1f);
            colors[(int)ImGuiCol.ScrollbarBg]        = new Vector4(0.08f, 0.08f, 0.12f, 1f);
            colors[(int)ImGuiCol.ScrollbarGrab]      = new Vector4(0.30f, 0.20f, 0.50f, 1f);
            colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.45f, 0.30f, 0.65f, 1f);
            colors[(int)ImGuiCol.ScrollbarGrabActive]  = new Vector4(0.55f, 0.40f, 0.75f, 1f);
            colors[(int)ImGuiCol.Separator]          = new Vector4(0.25f, 0.25f, 0.35f, 1f);
            colors[(int)ImGuiCol.Text]               = new Vector4(0.90f, 0.88f, 0.95f, 1f);
            colors[(int)ImGuiCol.TextDisabled]       = new Vector4(0.50f, 0.48f, 0.55f, 1f);
        }

        // ─── Login Screen ──────────────────────────────────────────────────────────
        static void DrawLoginScreen()
        {
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            float formW = Math.Min(360f, sw * 0.85f);
            float formH = 310f;
            float ox = (sw - formW) * 0.5f;
            float oy = (sh - formH) * 0.5f;
            float pad = 16f;
            float fieldW = formW - pad * 2;

            ImGui.SetCursorPos(new Vector2(ox, oy));
            ImGui.BeginChild("##login_card", new Vector2(formW, formH), ImGuiChildFlags.Borders);

            // ── Title ─────────────────────────────────────────────────────────────
            ImGui.Spacing();
            var title = "VRChat Unfriend Manager";
            ImGui.SetCursorPosX((formW - ImGui.CalcTextSize(title).X) * 0.5f);
            ImGui.TextColored(new Vector4(0.75f, 0.55f, 1f, 1f), title);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // ── Status line ───────────────────────────────────────────────────────
            bool isSigningIn = status == "Signing in...";
            bool isErr = !isSigningIn &&
                         (status.Contains("fail",    StringComparison.OrdinalIgnoreCase) ||
                          status.Contains("wrong",   StringComparison.OrdinalIgnoreCase) ||
                          status.Contains("error",   StringComparison.OrdinalIgnoreCase) ||
                          status.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
                          status.Contains("cookie",  StringComparison.OrdinalIgnoreCase));

            var statusColor = isSigningIn ? new Vector4(0.7f, 0.7f, 0.3f, 1f)
                            : isErr       ? new Vector4(1f,   0.3f, 0.3f, 1f)
                            :               new Vector4(0.5f, 0.5f, 0.6f, 1f);

            ImGui.SetCursorPosX(pad);
            if (isSigningIn)
            {
                int dots = (int)(ImGui.GetTime() * 2) % 4;
                ImGui.TextColored(statusColor, "Signing in" + new string('.', dots));
            }
            else ImGui.TextColored(statusColor, status);

            ImGui.Spacing();

            // Pre-fill username if blank
            if (string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(config.Username))
                user = config.Username;

            // ── Username ──────────────────────────────────────────────────────────
            ImGui.SetCursorPosX(pad);
            ImGui.TextDisabled("Username");
            ImGui.SetCursorPosX(pad);
            ImGui.SetNextItemWidth(fieldW);
            ImGui.InputText("##user", ref user, 100);

            ImGui.Spacing();

            // ── Password ──────────────────────────────────────────────────────────
            ImGui.SetCursorPosX(pad);
            ImGui.TextDisabled("Password");
            ImGui.SetCursorPosX(pad);
            ImGui.SetNextItemWidth(fieldW);
            ImGui.InputText("##pass", ref pass, 100, ImGuiInputTextFlags.Password);

            ImGui.Spacing();

            // ── Remember me ───────────────────────────────────────────────────────
            ImGui.SetCursorPosX(pad);
            ImGui.Checkbox("Remember me", ref remember);
            ImGui.Spacing();

            // ── Login button ──────────────────────────────────────────────────────
            bool canLogin = !working && !isSigningIn &&
                            !string.IsNullOrWhiteSpace(user) &&
                            !string.IsNullOrWhiteSpace(pass);

            ImGui.SetCursorPosX(pad);
            if (!canLogin) ImGui.BeginDisabled();
            if (ImGui.Button(working || isSigningIn ? "Signing in..." : "Login",
                new Vector2(fieldW, 34)))
            {
                working = true;
                status = "Logging in...";
                _ = Task.Run(async () =>
                {
                    var (success, name, error) = await api.LoginWithCredentialsAsync(user, pass);
                    if (success && name != null)
                    {
                        loggedInAs = name;
                        isLoggedIn = true;
                        sessionRestored = true;
                        if (remember)
                        {
                            config.Username = user;
                            config.EncodedPassword = Convert.ToBase64String(Encoding.UTF8.GetBytes(pass));
                            config.RememberMe = true;
                            SaveConfig();
                        }
                        await Refresh();
                        status = $"Logged in as {name}";
                    }
                    else status = error ?? "Login failed";
                    working = false;
                });
            }
            if (!canLogin) ImGui.EndDisabled();

            ImGui.EndChild();
        }

        // ─── Main UI ───────────────────────────────────────────────────────────────
        static void DrawMainUI()
        {
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();

            // Top bar: app name + logged-in info + logout
            ImGui.TextColored(new Vector4(0.75f, 0.55f, 1f, 1f), "VRChat Unfriend Manager");
            if (isLoggedIn)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), $"  •  {loggedInAs}");
                ImGui.SameLine();
                float logoutW = ImGui.CalcTextSize("Logout").X + 16;
                ImGui.SetCursorPosX(sw - logoutW - ImGui.GetStyle().WindowPadding.X);
                if (ImGui.Button("Logout"))
                {
                    File.Delete(Paths.CookieFile);
                    config.Cookie = "";
                    SaveConfig();
                    api = new VRChatApiService();
                    friends.Clear(); favorites.Clear(); selected.Clear();
                    loggedInAs = ""; isLoggedIn = false; sessionRestored = false;
                    status = "Logged out";
                }
            }
            ImGui.Separator();

            if (ImGui.BeginTabBar("##tabs"))
            {
                if (ImGui.BeginTabItem("Friends"))
                {
                    DrawFriendsTab(sw, sh);
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Groups"))
                {
                    DrawGroupsTab(sw, sh);
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Settings"))
                {
                    DrawSettingsTab();
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }

        // ─── Friends Tab ───────────────────────────────────────────────────────────
        static readonly string[] togetherUnits = { "min", "hr", "days" };
        static readonly string[] searchFields  = { "Name", "Group" };

        static void DrawFriendsTab(int sw, int sh)
        {
            ImGui.Spacing();

            // ── Row 1: Hide Favorites | Inactive filter ──────────────────────────
            if (ImGui.Checkbox("Hide Favorites", ref hideFavs))
            { config.ExcludeFavorites = hideFavs; SaveConfig(); }

            ImGui.SameLine(0, 20);
            if (ImGui.Checkbox("Inactive >=", ref inactiveOn))
            { config.InactiveEnabled = inactiveOn; SaveConfig(); }
            if (inactiveOn)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(70f);
                if (ImGui.InputInt("##iv", ref inactiveVal, 1, 0))
                { if (inactiveVal < 1) inactiveVal = 1; config.InactiveValue = inactiveVal; SaveConfig(); }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(80f);
                if (ImGui.Combo("##iu", ref inactiveUnit, units, units.Length))
                { config.InactiveUnitIndex = inactiveUnit; SaveConfig(); }
                ImGui.SameLine();
                var inCutoff = inactiveUnit switch
                {
                    0 => DateTime.UtcNow.AddDays(-inactiveVal),
                    1 => DateTime.UtcNow.AddMonths(-inactiveVal),
                    _ => DateTime.UtcNow.AddYears(-inactiveVal)
                };
                int inMatch = friends.Count(f =>
                    (!hideFavs || !favorites.Contains(f.Id)) &&
                    (string.IsNullOrEmpty(f.LastLogin) || DateTime.Parse(f.LastLogin) < inCutoff));
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), $"({inMatch} match)");
            }

            // ── Row 2: Together time filter ───────────────────────────────────────
            if (ImGui.Checkbox("Together <", ref togetherOn))
            { config.TogetherFilterEnabled = togetherOn; SaveConfig(); }
            if (togetherOn)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(70f);
                if (ImGui.InputInt("##tv", ref togetherVal, 1, 0))
                { if (togetherVal < 0) togetherVal = 0; config.TogetherFilterValue = togetherVal; SaveConfig(); }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(70f);
                if (ImGui.Combo("##tu", ref togetherUnit, togetherUnits, togetherUnits.Length))
                { config.TogetherFilterUnit = togetherUnit; SaveConfig(); }
                ImGui.SameLine();
                long tThreshMs = togetherUnit switch
                {
                    0 => togetherVal * 60_000L,
                    1 => togetherVal * 3_600_000L,
                    _ => togetherVal * 86_400_000L
                };
                int tMatch = friends.Count(f =>
                    (!hideFavs || !favorites.Contains(f.Id)) && f.TimeSpentMs < tThreshMs);
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), $"({tMatch} match)");
            }

            // ── Row 3: Search bar ─────────────────────────────────────────────────
            ImGui.Spacing();
            ImGui.SetNextItemWidth(100f);
            ImGui.Combo("##sf", ref searchField, searchFields, searchFields.Length);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(sw * 0.45f);
            ImGui.InputText("Search##sq", ref searchText, 128);
            if (!string.IsNullOrEmpty(searchText))
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("x##clr")) searchText = "";
            }

            // ── Row 4: Exclude groups ─────────────────────────────────────────────
            if (favByGroup.Any(kv => kv.Value.Count > 0 || favGroupNames.ContainsKey(kv.Key)))
            {
                ImGui.Spacing();
                ImGui.TextDisabled("Exclude groups:");
                ImGui.SameLine();
                foreach (var tag in favByGroup.Keys.OrderBy(t => t))
                {
                    bool excl = config.ExcludedFavGroups.Contains(tag);
                    string lbl = favGroupNames.TryGetValue(tag, out var gn) ? gn : tag;
                    int cnt = favByGroup[tag].Count;
                    if (ImGui.Checkbox($"##{tag}_excl", ref excl))
                    {
                        if (excl) { if (!config.ExcludedFavGroups.Contains(tag)) config.ExcludedFavGroups.Add(tag); }
                        else config.ExcludedFavGroups.Remove(tag);
                        SaveConfig();
                    }
                    ImGui.SameLine();
                    ImGui.Text($"{lbl} ({cnt})");
                    ImGui.SameLine(0, 14);
                }
                ImGui.NewLine();
            }

            // ── Sort ──────────────────────────────────────────────────────────────
            ImGui.Spacing();
            ImGui.SetNextItemWidth(160f);
            if (ImGui.Combo("Sort", ref sort, sorts, sorts.Length))
            { config.SortOptionIndex = sort; SaveConfig(); }

            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.7f, 1f), status);
            if (working && !isUnfriending)
                ImGui.ProgressBar(-1f * (float)(ImGui.GetTime() % 1.0), new Vector2(-1, 6), "");

            // ── Build shown list ──────────────────────────────────────────────────
            var excludedIds = new HashSet<string>();
            foreach (var tag in config.ExcludedFavGroups)
                if (favByGroup.TryGetValue(tag, out var eids))
                    foreach (var id in eids) excludedIds.Add(id);

            shown.Clear();
            var temp = friends.ToList();
            if (hideFavs)              temp = temp.Where(f => !favorites.Contains(f.Id)).ToList();
            if (excludedIds.Count > 0) temp = temp.Where(f => !excludedIds.Contains(f.Id)).ToList();
            if (inactiveOn && inactiveVal > 0)
            {
                var cutoff = inactiveUnit switch
                {
                    0 => DateTime.UtcNow.AddDays(-inactiveVal),
                    1 => DateTime.UtcNow.AddMonths(-inactiveVal),
                    _ => DateTime.UtcNow.AddYears(-inactiveVal)
                };
                temp = temp.Where(f => string.IsNullOrEmpty(f.LastLogin) || DateTime.Parse(f.LastLogin) < cutoff).ToList();
            }
            if (togetherOn && togetherVal >= 0)
            {
                long thMs = togetherUnit switch
                {
                    0 => togetherVal * 60_000L,
                    1 => togetherVal * 3_600_000L,
                    _ => togetherVal * 86_400_000L
                };
                temp = temp.Where(f => f.TimeSpentMs < thMs).ToList();
            }
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var q = searchText.Trim().ToLowerInvariant();
                if (searchField == 1)
                {
                    temp = temp.Where(f =>
                    {
                        foreach (var (tag, ids) in favByGroup.OrderBy(kv => kv.Key))
                            if (ids.Contains(f.Id))
                            {
                                var gn2 = favGroupNames.TryGetValue(tag, out var g) ? g : tag;
                                return gn2.ToLowerInvariant().Contains(q);
                            }
                        return false;
                    }).ToList();
                }
                else temp = temp.Where(f => f.DisplayName.ToLowerInvariant().Contains(q)).ToList();
            }
            temp = sort switch
            {
                0 => temp.OrderBy(f => string.IsNullOrEmpty(f.LastLogin) ? DateTime.MinValue : DateTime.Parse(f.LastLogin)).ToList(),
                1 => temp.OrderByDescending(f => string.IsNullOrEmpty(f.LastLogin) ? DateTime.MinValue : DateTime.Parse(f.LastLogin)).ToList(),
                2 => temp.OrderBy(f => f.DisplayName).ToList(),
                3 => temp.OrderByDescending(f => f.DisplayName).ToList(),
                4 => temp.OrderByDescending(f => f.TimeSpentMs).ToList(),
                _ => temp.OrderBy(f => f.TimeSpentMs).ToList()
            };
            shown = temp;

            // ── List ──────────────────────────────────────────────────────────────
            float bottomBarH = isUnfriending ? 90f : 50f;
            float listH = sh - ImGui.GetCursorPosY() - bottomBarH - ImGui.GetStyle().WindowPadding.Y * 2 - 60;
            if (listH < 80) listH = 80;

            if (ImGui.BeginChild("##list", new Vector2(-1, listH), ImGuiChildFlags.Borders))
            {
                ImGui.TextDisabled($"{"  ",-5}{"Name",-36} {"Last seen",8}  {"Together",9}  Group");
                ImGui.Separator();

                const float IMG_SIZE = 32f;
                const float ROW_H    = IMG_SIZE + 4f;

                for (int i = 0; i < shown.Count; i++)
                {
                    var f        = shown[i];
                    var ago      = string.IsNullOrEmpty(f.LastLogin) ? "never" : Ago(DateTime.Parse(f.LastLogin));
                    var together = FormatTimeSpent(f.TimeSpentMs);
                    bool sel     = selected.Contains(i);

                    string groupLabel = "";
                    foreach (var (tag, ids) in favByGroup.OrderBy(kv => kv.Key))
                        if (ids.Contains(f.Id))
                        { groupLabel = favGroupNames.TryGetValue(tag, out var gn3) ? gn3 : tag; break; }

                    ImGui.PushID(i);
                    var rowStart = ImGui.GetCursorScreenPos();

                    if (ImGui.Selectable($"##s{i}", sel,
                        ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap,
                        new Vector2(0, ROW_H)))
                    {
                        if (Raylib.IsKeyDown(KeyboardKey.LeftControl))
                            _ = sel ? selected.Remove(i) : selected.Add(i);
                        else { selected.Clear(); selected.Add(i); }
                    }

                    ImGui.SetCursorScreenPos(rowStart);
                    var tex = TextureCache.RequestTexture(f.ThumbnailUrl);
                    if (tex.HasValue && tex.Value.Id != 0)
                        ImGui.Image((nint)tex.Value.Id, new Vector2(IMG_SIZE, IMG_SIZE));
                    else
                    {
                        var dl = ImGui.GetWindowDrawList();
                        dl.AddRectFilled(rowStart, rowStart + new Vector2(IMG_SIZE, IMG_SIZE),
                            ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.3f, 1f)));
                        dl.AddText(rowStart + new Vector2(8, 8),
                            ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.6f, 1f)), "?");
                    }

                    ImGui.SameLine();
                    float textY = rowStart.Y + (ROW_H - ImGui.GetTextLineHeight()) * 0.5f;
                    ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, textY));
                    ImGui.Text($"{f.DisplayName,-34} {ago,8}  {together,9}  {groupLabel}");

                    ImGui.PopID();
                }
                ImGui.EndChild();
            }

            // Bottom action bar
            ImGui.Spacing();
            if (ImGui.Button("Mark All")) { for (int i = 0; i < shown.Count; i++) selected.Add(i); }
            ImGui.SameLine();
            if (ImGui.Button("Unmark All")) selected.Clear();
            ImGui.SameLine();
            if (ImGui.Button("Refresh")) _ = Refresh();
            ImGui.SameLine();
            if (ImGui.Button("Backup JSON"))
                File.WriteAllText($"backup_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                    JsonSerializer.Serialize(shown, new JsonSerializerOptions { WriteIndented = true }));

            ImGui.SameLine();
            string btnLabel = isUnfriending ? (isPaused ? "Resume" : "Pause") : $"Unfriend ({selected.Count})";
            bool canUnfriend = selected.Count > 0 || isUnfriending;
            if (!canUnfriend) ImGui.BeginDisabled();
            if (ImGui.Button(btnLabel))
            {
                if (isUnfriending) isPaused = !isPaused;
                else if (selected.Count > 0) ImGui.OpenPopup("##confirm_unfriend");
            }
            if (!canUnfriend) ImGui.EndDisabled();

            if (ImGui.BeginPopupModal("##confirm_unfriend", ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text($"Permanently unfriend {selected.Count} user(s)?");
                ImGui.Spacing();
                if (ImGui.Button("Yes, do it", new Vector2(120, 0)))
                {
                    _ = Task.Run(StartUnfriendProcess);
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(80, 0))) ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }

            if (isUnfriending)
            {
                ImGui.Spacing();
                float p = unfriendTotal > 0 ? unfriendDone / (float)unfriendTotal : 0f;
                ImGui.ProgressBar(p, new Vector2(-1, 20), $"{unfriendDone}/{unfriendTotal}");
                ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), isPaused ? "PAUSED" : "Unfriending...");
                ImGui.SameLine();
                if (ImGui.Button("Cancel")) { unfriendCts?.Cancel(); isUnfriending = false; isPaused = false; }
            }
        }

        // ─── Groups Tab ────────────────────────────────────────────────────────────
        static void DrawGroupsTab(int sw, int sh)
        {
            ImGui.Spacing();
            ImGui.TextWrapped("These are your VRChat native favorite groups. Membership is managed inside VRChat. Use the toggles to exclude a group from the Friends list.");
            ImGui.Spacing();

            if (ImGui.Button("Refresh Groups")) _ = Refresh();
            ImGui.SameLine();
            // Show debug info about what tags came back
            ImGui.TextDisabled($"  {favByGroup.Count} group(s) detected, {favGroupNames.Count} named");
            ImGui.Separator();
            ImGui.Spacing();

            if (favByGroup.Count == 0)
            {
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "No favorite groups found.");
                ImGui.Spacing();
                ImGui.TextDisabled("This could mean:");
                ImGui.TextDisabled("  • You have no friends added to favorites in VRChat");
                ImGui.TextDisabled("  • The API didn't return group tags (check console for [FAV] logs)");
                ImGui.TextDisabled("  • CurrentUserId was not set before the groups call");
                ImGui.Spacing();
                ImGui.TextDisabled($"CurrentUserId: {(string.IsNullOrEmpty(api.CurrentUserId) ? "(not set!)" : api.CurrentUserId)}");
                return;
            }

            float colW = Math.Max((sw - 50f) / Math.Max(favByGroup.Count, 1), 180f);

            foreach (var tag in favByGroup.Keys.OrderBy(t => t))
            {
                var ids = favByGroup[tag];
                string displayName = favGroupNames.TryGetValue(tag, out var n) ? n : tag;
                bool excluded = config.ExcludedFavGroups.Contains(tag);

                ImGui.BeginGroup();

                // Show both display name and raw tag so we can verify the mapping
                ImGui.TextColored(new Vector4(0.75f, 0.55f, 1f, 1f), displayName);
                ImGui.SameLine();
                ImGui.TextDisabled($"[{tag}] ({ids.Count})");
                ImGui.SameLine();
                if (ImGui.Checkbox($"Exclude##{tag}", ref excluded))
                {
                    if (excluded) { if (!config.ExcludedFavGroups.Contains(tag)) config.ExcludedFavGroups.Add(tag); }
                    else config.ExcludedFavGroups.Remove(tag);
                    SaveConfig();
                }

                float cardH = Math.Min(ids.Count * (ImGui.GetTextLineHeightWithSpacing() + 6) + 12, sh * 0.5f);
                if (ImGui.BeginChild($"##grp_{tag}", new Vector2(colW, cardH), ImGuiChildFlags.Borders))
                {
                    foreach (var id in ids)
                    {
                        var f = friends.FirstOrDefault(x => x.Id == id);
                        if (f != null)
                        {
                            var tex = TextureCache.RequestTexture(f.ThumbnailUrl);
                            if (tex.HasValue && tex.Value.Id != 0)
                                ImGui.Image((nint)tex.Value.Id, new Vector2(24, 24));
                            else
                                ImGui.Dummy(new Vector2(24, 24));
                            ImGui.SameLine();
                            ImGui.Text(f.DisplayName);
                            ImGui.SameLine();
                            ImGui.TextDisabled($"  {FormatTimeSpent(f.TimeSpentMs)}");
                        }
                        else
                        {
                            ImGui.TextDisabled(id);
                        }
                    }
                    ImGui.EndChild();
                }

                ImGui.EndGroup();
                ImGui.SameLine(0, 12);
            }
            ImGui.NewLine();
        }

        // ─── Settings Tab ──────────────────────────────────────────────────────────
        static void DrawSettingsTab()
        {
            ImGui.Spacing();
            ImGui.Text("Startup Options");
            ImGui.Separator();

            bool runOnStartup = config.RunOnStartup;
            if (ImGui.Checkbox("Run on system startup", ref runOnStartup))
            {
                config.RunOnStartup = runOnStartup;
                SaveConfig();
                UpdateStartup(runOnStartup);
            }

            if (Directory.Exists(Paths.VrcxStartup))
            {
                ImGui.Spacing();
                ImGui.Text("VRCX Integration");
                if (VrcxDataService.IsAvailable)
                    ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), "✓ VRCX database found — time together data enabled");
                else
                    ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "VRCX.sqlite3 not found — time together will show as '-'");
                ImGui.TextDisabled("  Requires NuGet: Microsoft.Data.Sqlite");

                bool vrcxDesktop = config.VrcxStartupDesktop;
                if (ImGui.Checkbox("Launch with VRCX (Desktop)", ref vrcxDesktop))
                {
                    config.VrcxStartupDesktop = vrcxDesktop;
                    UpdateVrcxShortcut("desktop", vrcxDesktop);
                    SaveConfig();
                }

                bool vrcxVr = config.VrcxStartupVr;
                if (ImGui.Checkbox("Launch with VRCX (VR)", ref vrcxVr))
                {
                    config.VrcxStartupVr = vrcxVr;
                    UpdateVrcxShortcut("vr", vrcxVr);
                    SaveConfig();
                }
            }

            ImGui.Spacing();
            ImGui.Text("Auto-Unfriend Scheduler");
            ImGui.Separator();

            bool autoEnabled = config.AutoUnfriendEnabled;
            if (ImGui.Checkbox("Enable Auto-Unfriend", ref autoEnabled))
            {
                config.AutoUnfriendEnabled = autoEnabled;
                SaveConfig();
                if (autoEnabled) StartAutoScheduler();
                else { autoCts?.Cancel(); autoCts = null; }
            }

            if (config.AutoUnfriendEnabled)
            {
                ImGui.Spacing();
                ImGui.Text("Schedule time:");
                ImGui.SameLine();

                int h = config.AutoUnfriendHour;
                int m = config.AutoUnfriendMinute;
                ImGui.SetNextItemWidth(55);
                if (ImGui.InputInt("##ah", ref h)) { h = Math.Clamp(h, 0, 23); config.AutoUnfriendHour = h; SaveConfig(); StartAutoScheduler(); }
                ImGui.SameLine(); ImGui.Text(":");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(55);
                if (ImGui.InputInt("##am", ref m)) { m = Math.Clamp(m, 0, 59); config.AutoUnfriendMinute = m; SaveConfig(); StartAutoScheduler(); }

                ImGui.Spacing();
                ImGui.Text("Mode:");
                ImGui.SameLine();
                int mode = config.AutoUnfriendMode;
                ImGui.SetNextItemWidth(230);
                if (ImGui.Combo("##automode", ref mode, autoModes, autoModes.Length))
                {
                    config.AutoUnfriendMode = mode;
                    SaveConfig();
                }

                // Show next scheduled run
                var now = DateTime.Now;
                var target = new DateTime(now.Year, now.Month, now.Day, config.AutoUnfriendHour, config.AutoUnfriendMinute, 0);
                if (target <= now) target = target.AddDays(1);
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), $"Next run: {target:ddd dd MMM yyyy HH:mm}");
            }
        }

        // ─── Auto Scheduler ────────────────────────────────────────────────────────
        static void StartAutoScheduler()
        {
            autoCts?.Cancel();
            autoCts = new CancellationTokenSource();
            var token = autoCts.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && config.AutoUnfriendEnabled)
                {
                    var now = DateTime.Now;
                    var target = new DateTime(now.Year, now.Month, now.Day, config.AutoUnfriendHour, config.AutoUnfriendMinute, 0);
                    if (target <= now) target = target.AddDays(1);

                    try { await Task.Delay(target - now, token); }
                    catch (OperationCanceledException) { break; }

                    if (token.IsCancellationRequested) break;

                    // Refresh data then run auto-unfriend
                    try
                    {
                        await Refresh();

                        List<SafeLimitedUserFriend> toUnfriend = config.AutoUnfriendMode switch
                        {
                            // Inactive Only: 3+ months
                            0 => shown.Where(f =>
                                string.IsNullOrEmpty(f.LastLogin) ||
                                DateTime.Parse(f.LastLogin) < DateTime.UtcNow.AddMonths(-3)).ToList(),
                            // All Shown
                            1 => shown.ToList(),
                            // Marked Only (use current selection)
                            2 => selected.Select(i => shown[i]).ToList(),
                            _ => new List<SafeLimitedUserFriend>()
                        };

                        foreach (var u in toUnfriend)
                        {
                            if (token.IsCancellationRequested) break;
                            try
                            {
                                await api.UnfriendAsync(u.Id);
                                ShowUnfriendToast(u.DisplayName);
                                await Task.Delay(Random.Shared.Next(7000, 13000), token);
                            }
                            catch { }
                        }

                        ShowToast("Auto-Unfriend", $"Removed {toUnfriend.Count} friends");
                        await Refresh();
                    }
                    catch { }
                }
            }, token);
        }

        // ─── Unfriend Process ──────────────────────────────────────────────────────
        static async Task StartUnfriendProcess()
        {
            isUnfriending = true; isPaused = false;
            unfriendTotal = selected.Count; unfriendDone = 0;
            unfriendCts = new CancellationTokenSource();
            var list = selected.Select(i => shown[i]).ToList();

            try
            {
                for (int i = 0; i < list.Count; i++)
                {
                    while (isPaused && !unfriendCts.Token.IsCancellationRequested)
                        await Task.Delay(200, unfriendCts.Token);

                    if (unfriendCts.Token.IsCancellationRequested) break;

                    var u = list[i];
                    status = $"Unfriending {u.DisplayName}...";
                    try
                    {
                        await api.UnfriendAsync(u.Id);
                        unfriendDone++;
                        ShowUnfriendToast(u.DisplayName);
                    }
                    catch (Exception ex) { Console.WriteLine(ex.Message); }

                    if (i < list.Count - 1)
                        await Task.Delay(Random.Shared.Next(7000, 13000), unfriendCts.Token);
                }
            }
            finally
            {
                isUnfriending = false; isPaused = false;
                status = unfriendDone == unfriendTotal ? "All done!" : "Cancelled";
                ShowToast("Unfriend Complete", $"{unfriendDone} users removed");
                selected.Clear();
                await Refresh();
            }
        }

        // ─── Refresh ───────────────────────────────────────────────────────────────
        static async Task Refresh()
        {
            working = true; status = "Loading friends...";
            // Clear stale image cache so new friend list gets fresh thumbnails
            TextureCache.UnloadAll();
            try
            {
                var (allIds, byGroup) = await api.GetFavoritesDetailedAsync();
                favorites = allIds;
                favByGroup = byGroup;
                favGroupNames = await api.GetFavoriteGroupNamesAsync();
                friends = await api.GetAllFriendsAsync();

                // Enrich with VRCX time-together data if available
                if (VrcxDataService.IsAvailable)
                {
                    status = "Loading VRCX time data...";
                    var timeMap = await Task.Run(() => VrcxDataService.LoadTimeSpentSeconds());
                    foreach (var f in friends)
                        if (timeMap.TryGetValue(f.Id, out var secs))
                            f.TimeSpentMs = secs * 1000L;
                }

                status = $"Loaded {friends.Count} friends" +
                         (VrcxDataService.IsAvailable ? " (with time data)" : "");
            }
            catch (Exception ex)
            {
                status = "Session expired — please re-login";
                isLoggedIn = false;
                sessionRestored = false;
                Console.WriteLine(ex.Message);
            }
            selected.Clear();
            working = false;
        }

        // ─── Helpers ───────────────────────────────────────────────────────────────
        static string Ago(DateTime dt)
        {
            var span = DateTime.UtcNow - dt.ToUniversalTime();
            if (span.TotalDays < 1) return "today";
            if (span.TotalDays < 30) return $"{(int)span.TotalDays}d";
            if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30.4)}mo";
            return $"{(int)(span.TotalDays / 365.25)}y";
        }

        /// <summary>Formats milliseconds into a compact human-readable string, e.g. "9h 51m" or "2d 3h".</summary>
        static string FormatTimeSpent(long ms)
        {
            if (ms <= 0) return "-";
            var ts = TimeSpan.FromMilliseconds(ms);
            if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h";
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            return $"{ts.Minutes}m";
        }

        static void ShowUnfriendToast(string displayName) =>
            ShowToast("Unfriended", $"{displayName} has been removed.");

        static void ShowToast(string title, string msg)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows toast notification (add win32 notification library if desired)
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try { Process.Start("notify-send", $"\"{title}\" \"{msg}\""); } catch { }
            }
        }

        static void UpdateStartup(bool enable)
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return;
            string cmdArgs = $"\"{exePath}\" --autostart";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                    if (enable) key?.SetValue("VRChatUnfriendManager", cmdArgs);
                    else key?.DeleteValue("VRChatUnfriendManager", false);
                }
                catch (Exception ex) { Console.WriteLine($"[STARTUP] Windows: {ex.Message}"); }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    string autostartDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "autostart");
                    Directory.CreateDirectory(autostartDir);
                    string desktopFile = Path.Combine(autostartDir, "VRChatUnfriendManager.desktop");
                    if (enable)
                        File.WriteAllText(desktopFile, $"[Desktop Entry]\nType=Application\nName=VRChat Unfriend Manager\nExec={cmdArgs}\nTerminal=false\n");
                    else if (File.Exists(desktopFile))
                        File.Delete(desktopFile);
                }
                catch (Exception ex) { Console.WriteLine($"[STARTUP] Linux: {ex.Message}"); }
            }
        }

        static void UpdateVrcxShortcut(string subfolder, bool enable)
        {
            try
            {
                var targetDir = Path.Combine(Paths.VrcxStartup, subfolder);
                Directory.CreateDirectory(targetDir);
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    string linkPath = Path.Combine(targetDir, "VRChatUnfriendManager");
                    if (File.Exists(linkPath)) File.Delete(linkPath);
                    if (enable) File.CreateSymbolicLink(linkPath, exePath);
                }
                // Windows: add .lnk shortcut via PowerShell if needed
            }
            catch (Exception ex) { Console.WriteLine($"[VRCX] {ex.Message}"); }
        }

        static void LoadConfig()
        {
            Paths.EnsureExists();
            if (!File.Exists(Paths.ConfigFile)) return;
            try
            {
                var json = File.ReadAllText(Paths.ConfigFile);
                var c = JsonSerializer.Deserialize<AppConfig>(json);
                if (c != null) config = c;
            }
            catch { }
        }

        public static void SaveConfig()
        {
            Paths.EnsureExists();

            // Only update credentials fields if we actually have values — never blank them on shutdown
            // (pass is empty after a session-restore login since we never store it in the field)
            if (!string.IsNullOrEmpty(user))
                config.Username = user;

            if (remember && !string.IsNullOrEmpty(pass))
                config.EncodedPassword = Convert.ToBase64String(Encoding.UTF8.GetBytes(pass));
            else if (!remember)
                config.EncodedPassword = ""; // explicit opt-out

            config.RememberMe = remember;
            config.ExcludeFavorites = hideFavs;
            config.InactiveEnabled = inactiveOn;
            config.InactiveValue = inactiveVal;
            config.InactiveUnitIndex = inactiveUnit;
            config.TogetherFilterEnabled = togetherOn;
            config.TogetherFilterValue = togetherVal;
            config.TogetherFilterUnit = togetherUnit;
            config.SortOptionIndex = sort;
            try
            {
                File.WriteAllText(Paths.ConfigFile,
                    JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}