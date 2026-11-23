using CounterStrikeSharp.API.Modules.Extensions;
using System.Drawing;
using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;

public sealed partial class HookGrabRope
{
    sealed class Preference
    {
        public Dictionary<string, Dictionary<string, string>> Values { get; set; } = new();
        public bool? Chat { get; set; }
        public bool? Toast { get; set; }
        public string? Priority { get; set; }
    }
    sealed class View(bool admin)
    {
        public bool Admin = admin;
        public Mode Mode = Mode.Grab;
        public readonly string[] TintClasses = new string[7];
        public string Page = "home", Field = ""; public int Offset; public List<(string Label, string Value, Action Action)> Rows = new();
    }
    JsonElement rules;
    Dictionary<ulong, Preference> prefs = new(), vipPrefs = new();
    readonly Dictionary<(ulong, Mode), int> used = new();
    readonly Dictionary<int, View> views = new();
    readonly Dictionary<bool, CCSCustomHudLayout> layouts = new();



    string DataPath(string name) => name == "rules" ? Path.GetFullPath(Path.Combine(ModuleDirectory, "..", "..", "configs", "plugins", "HookGrabRope", "hgr.jsonc")) : Path.Combine(ModuleDirectory, name + ".json");
    void Save(string name, Dictionary<ulong, Preference> data)
    {
        var path = DataPath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(EncodePreferences(data), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
    bool Admin(CCSPlayerController p) => Token(Section("Admin"), "Enabled", "true") == "true" && AdminManager.PlayerHasPermissions(p, Token(Section("Admin"), "Flag", "@css/root"));
    // Resolve optional shared APIs at use time, including after provider reloads.
    static object? Optional(string typeName, string capability, string method, params object[] args)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName)).FirstOrDefault(t => t != null);
        if (type == null) return null;
        var cap = typeof(PluginCapability<>).MakeGenericType(type);
        try
        {
            var api = cap.GetMethod("Get")!.Invoke(Activator.CreateInstance(cap, capability), null);
            var mi = type.GetMethod(method);
            if (mi?.IsGenericMethodDefinition == true) mi = mi.MakeGenericMethod(typeof(JsonElement));
            return api == null ? null : mi?.Invoke(api, args);
        }
        catch (TargetInvocationException) { return null; }
    }
    static bool Vip(CCSPlayerController p) => Optional("VipCoreApi.IVipCoreApi", "vipcore:core", "IsPlayerVip", p) is true ||
        Optional("VipCoreApi.IVipCoreApi", "vipcore:core", "IsClientVip", p) is true;
    int Allowance(CCSPlayerController p, Mode m)
    {
        var policy = Policy(p, m); if (!policy.Allowed) return 0;
        var n = (int)Numeric(policy, m, "Amount").Value; return n == -1 ? int.MaxValue : n;
    }
    int Remaining(CCSPlayerController p, Mode mode)
    {
        int limit = Allowance(p, mode);
        return limit == int.MaxValue ? limit : Math.Max(0, limit - used.GetValueOrDefault((p.SteamID, mode)));
    }
    static string Count(int n) => n is -1 or int.MaxValue ? "∞" : n.ToString();
    void Charge(CCSPlayerController p, Mode m)
    {
        var key = (p.SteamID, m);
        used[key] = used.GetValueOrDefault(key) + 1;
        Tell(p, "used", m, Count(Remaining(p, m)));
    }
    void Tell(CCSPlayerController p, string key, params object[] args)
    {
        var msg = Localizer.ForPlayer(p, key, args);
        if (NotificationPrefs(p).Chat ?? Setting("Chat")) p.PrintToChat(" [HGR] " + msg);
        if (NotificationPrefs(p).Toast ?? Setting("Toast")) Optional("PmmNotificationApi.IBhopNotifications", "pmm_vipcore:notification", "Toast", p, msg, 0);
    }
    void SetupMenu()
    {
        LoadRules();
        vipPrefs = ReadPreferences("vip-preferences");
        prefs = ReadPreferences("preferences");
        AddCommand("css_hgr_reload", "Reload HGR settings", (p, cmd) =>
        {
            if (p != null && !Admin(p)) return;
            LoadRules(); Clear(); CloseAllMenus(); cmd.ReplyToCommand("[HGR] Reloaded");
        });
        AddCommand("css_hgr_vip", "VIP HGR", (p, _) => { if (p != null && HasVipGrant(p)) Open(p, false); });
        AddCommand("css_hgr_admin", "Manage HGR", (p, _) => { if (p != null && Admin(p)) Open(p, true); });
        RegisterListener<Listeners.OnCustomHudClicked>(Click);
        RegisterListener<Listeners.OnClientDisconnect>(slot => views.Remove(slot));
        RegisterListener<Listeners.OnMapEnd>(() => { views.Clear(); layouts.Clear(); used.Clear(); });
        RegisterEventHandler<EventRoundStart>((e, i) => { Clear(); used.Clear(); foreach (var p in Utilities.GetPlayers()) if (views.ContainsKey(p.Slot)) Render(p); return HookResult.Continue; });
    }
    void Binds(CCSPlayerController p, Mode mode)
    {
        var m = mode.ToString().ToLowerInvariant();
        if (Choice(Policy(p, mode), mode, "Sticking").Value == "true")
            p.PrintToChat($"[HGR · {mode}] bind V css_{m}");
        else
        {
            p.PrintToChat($"alias +hgr_{m} css_{m}_on");
            p.PrintToChat($"alias -hgr_{m} css_{m}_off");
            p.PrintToChat($"bind V +hgr_{m}");
        }
        p.PrintToChat("bind mwheelup \"invprev; css_grab_farther; css_hook_closer; css_rope_closer\"");
        p.PrintToChat("bind mwheeldown \"invnext; css_grab_closer; css_hook_farther; css_rope_farther\"");
    }
}
