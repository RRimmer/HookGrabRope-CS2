using System.Drawing;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;

public sealed partial class HookGrabRope
{
    void Open(CCSPlayerController p, bool admin)
    {
        if (views.ContainsKey(p.Slot)) Close(p);
        if (!layouts.TryGetValue(admin, out var hud) || !hud.IsValid)
        {
            hud = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout"); if (hud == null) return;
            hud.StrLayout = "panorama/layout/custom_game/" + (admin ? "hgr_admin" : "hgr_vip") + ".xml"; hud.DispatchSpawn(); layouts[admin] = hud;
        }
        views[p.Slot] = new(admin); hud.SetHasClassForPlayer(p, "hgr-root", "hidden", false); hud.SetInputCaptureEnabled(p, true); Render(p);
    }
    void Close(CCSPlayerController p)
    {
        if (!views.Remove(p.Slot, out var view) || !layouts.TryGetValue(view.Admin, out var hud) || !hud.IsValid) return;
        hud.SetHasClassForPlayer(p, "hgr-root", "hidden", true); hud.SetInputCaptureEnabled(p, false);
    }
    void CloseAllMenus() { foreach (var p in Utilities.GetPlayers()) Close(p); foreach (var h in layouts.Values) if (h.IsValid) h.Remove(); layouts.Clear(); }
    void Click(CCSPlayerController p, CCSCustomHudLayout hud, string id)
    {
        if (!views.TryGetValue(p.Slot, out var v) || layouts[v.Admin].Handle != hud.Handle) return;
        if (id == "close" || (v.Admin ? !Admin(p) : !HasVipGrant(p))) { Close(p); return; }
        if (id == "back")
        {
            if (v.Page == "home") { Close(p); p.ExecuteClientCommandFromServer(v.Admin ? "css_admin" : "css_vip"); return; }
            else v.Page = v.Page == "field" ? "ability" : "home";
            v.Offset = 0;
        }
        else if (id == "next") v.Offset = Math.Min(v.Offset + 7, Math.Max(0, (v.Rows.Count - 1) / 7 * 7));
        else if (id == "prev") v.Offset = Math.Max(0, v.Offset - 7);
        else if (id.StartsWith("row") && int.TryParse(id.AsSpan(3), out int n) && n >= 0 && n < 7 && v.Offset + n < v.Rows.Count)
        {
            // Rebuild from current rights and rules before executing a selection.
            var old = v.Rows[v.Offset + n]; Render(p);
            if (v.Offset + n >= v.Rows.Count || v.Rows[v.Offset + n].Label != old.Label) return;
            v.Rows[v.Offset + n].Action();
        }
        Render(p);
    }
    void Render(CCSPlayerController p)
    {
        var v = views[p.Slot]; var hud = layouts[v.Admin]; v.Rows.Clear();
        var m = v.Mode; string key = m.ToString(); var e = Policy(p, m);

        bool vipSettings = e.Vip;
        var storage = vipSettings ? vipPrefs : prefs;
        if (!storage.TryGetValue(p.SteamID, out var pref)) pref = new();
        void Write() { storage[p.SteamID] = pref; Save(vipSettings ? "vip-preferences" : "preferences", storage); }
        void Row(string text, string detail, Action action) => v.Rows.Add((text, detail, action));
        void Go(string page) { v.Page = page; v.Offset = 0; }
        void Option(string label, string value, bool edit, Action action)
        {
            if (v.Page == "ability") Row(label, value + (edit ? "  ›" : "  · из группы"), () => { if (edit) { v.Field = label; Go("field"); } });
            else if (v.Field == label) action();
        }
        void Toggle(string label, bool value, Action<bool> set) => Row(label, value ? "Вкл" : "Откл", () => { set(!value); Write(); });
        if (v.Page == "home")
        {
            foreach (var mode in Enum.GetValues<Mode>()) if (Allowance(p, mode) > 0) Row(mode.ToString(), "›", () => { v.Mode = mode; Go("ability"); });
            Row("Все бинды в чат", "V", () => { foreach (var item in Enum.GetValues<Mode>()) if (Allowance(p, item) > 0) Binds(p, item); });
            Row("Настройка уведомлений", "›", () => Go("notifications"));
            if (Admin(p) && HasVipGrant(p))
                Row("Приоритет настроек", Priority(p), () =>
                {
                    if (!prefs.TryGetValue(p.SteamID, out var owner)) prefs[p.SteamID] = owner = new();
                    owner.Priority = Priority(p).Equals("VIP", StringComparison.OrdinalIgnoreCase) ? "Admin" : "VIP";
                    Save("preferences", prefs);
                });
        }
        else if (v.Page == "notifications")
        {
            Toggle("Сообщения в чате", pref.Chat ?? Setting("Chat"), b => { pref.Chat = b; });
            Toggle("Уведомления сбоку", pref.Toast ?? Setting("Toast"), b => { pref.Toast = b; });
        }
        else if (Allowance(p, m) > 0)
        {
            foreach (var field in Fields(m))
            {
                string label = field switch
                {
                    "Color" => "Цвет луча",
                    "EffectLine" => "Линия",
                    "Width" => "Ширина луча",
                    "Amount" => "Кол-во за раунд",
                    "Range" => m == Mode.Grab ? "Дальность захвата" : "Дальность зацепа",
                    "Distance" => m == Mode.Grab ? "Дистанция удержания" : "Дистанция подтягивания",
                    "Start.Distance" => "Стартовая дистанция",
                    "Strength" => "Сила броска",
                    "Step up" => "Шаг колеса вверх",
                    "Step down" => "Шаг колеса вниз",
                    "Pull" => "Подтягивание",
                    _ => "Залипание"
                };
                void Set(string value)
                {
                    if (!pref.Values.TryGetValue(key, out var values)) pref.Values[key] = values = new();
                    values[field] = value; Write();
                }
                if (IsChoice(field))
                {
                    var rule = Choice(e, m, field);
                    string Display(string value) => value switch { "true" => field == "Pull" && m == Mode.Hook ? "Авто" : "Вкл", "false" => "Откл", "Self" => "Сам", _ => value };
                    Option(label, Display(rule.Value), rule.Editable, () =>
                    {
                        if (rule.Editable) foreach (var option in rule.Options) Row(Display(option), option == rule.Value ? "Выбрано" : "", () => { Set(option); Go("ability"); });
                    });
                }
                else
                {
                    var rule = Numeric(e, m, field);
                    double step = field switch { "Width" or "Range" => .5, "Amount" => 1, "Step up" or "Step down" => m == Mode.Grab ? 25 : 50, _ => Limit("Step") };
                    string display = rule.Value == -1 ? "∞" : rule.Value.ToString("0.##");
                    Option(label, display, rule.Editable, () =>
                    {
                        if (!rule.Editable) return;
                        foreach (int dir in new[] { -1, 1 }) Row(dir < 0 ? "Уменьшить" : "Увеличить", display, () =>
                        {
                            double next = rule.Unlimited && rule.Value == -1 ? rule.Min : rule.Unlimited && rule.Value == rule.Min && dir < 0 ? -1 : Math.Clamp(rule.Value + dir * step, rule.Min, rule.Max);
                            Set(next.ToString(Invariant));
                        });
                    });
                }
            }
            if (v.Page == "ability") Row("Бинды в чат", "V", () => Binds(p, m));
        }
        void Var(string k, string val) => hud.SetDialogVariableStringForPlayer(p, "hgr-root", k, val + " ");
        Var("nick", p.PlayerName); Var("group", v.Admin ? "PMM · SIMPLEADMIN" : (Optional("VipCoreApi.IVipCoreApi", "vipcore:core", "GetClientVipGroup", p) as string ?? "VIP"));
        Var("title", v.Page switch { "home" => "Управление HGR", "notifications" => "Настройка уведомлений", "field" => v.Field, _ => key });
        v.Offset = Math.Min(v.Offset, Math.Max(0, (v.Rows.Count - 1) / 7 * 7)); Var("page", $"{v.Offset / 7 + 1} / {Math.Max(1, (v.Rows.Count + 6) / 7)}");
        for (int i = 0; i < 7; i++)
        {
            bool show = v.Offset + i < v.Rows.Count; string id = "row" + i; hud.SetHasClassForPlayer(p, id, "hidden", !show); if (!show) continue;
            var row = v.Rows[v.Offset + i]; Var("label" + i, row.Label); Var("value" + i, row.Value);
            var tint = row.Label == "Цвет луча" ? row.Value.Split(' ')[0] : v.Field == "Цвет луча" && v.Page == "field" && row.Value == "Выбрано" ? row.Label : "";
            tint = Tint(tint, m).ToLowerInvariant();
            if (colors.TryGetValue(tint, out var hex)) tint = hex;
            if (tint == "team") tint = p.TeamNum == 2 ? "goldenrod" : "cornflowerblue";
            string tone = tint.StartsWith("#") ? "hex-" + tint.Substring(1).ToLowerInvariant() : tint == "rainbow" ? "tone-rainbow" : tint is "goldenrod" or "cornflowerblue" ? "tone-" + tint : "";
            if (v.TintClasses[i] == null)
            {
                foreach (var c in colors.Values.Distinct()) hud.SetHasClassForPlayer(p, id, "hex-" + c.Substring(1).ToLowerInvariant(), false);
                foreach (var c in new[] { "rainbow", "goldenrod", "cornflowerblue" }) hud.SetHasClassForPlayer(p, id, "tone-" + c, false);
            }
            if (v.TintClasses[i] != tone)
            {
                if (!string.IsNullOrEmpty(v.TintClasses[i])) hud.SetHasClassForPlayer(p, id, v.TintClasses[i], false);
                if (tone != "") hud.SetHasClassForPlayer(p, id, tone, true);
                v.TintClasses[i] = tone;
            }
        }
        foreach (var id in new[] { "prev", "next", "page-count" }) hud.SetHasClassForPlayer(p, id, "hidden", v.Rows.Count <= 7);
    }
}

