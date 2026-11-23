using System.Globalization;
using System.Text.Json;
using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

public sealed partial class HookGrabRope
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    static readonly JsonDocumentOptions Loose = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    static readonly string[] Lines = ["Beam", "Laser", "Lasso", "Chain"];
    readonly HashSet<string> warnings = new();
    JsonElement template;
    void Warn(string message) { if (warnings.Add(message)) Logger.LogWarning("{Issue}", message); }
    static JsonElement Property(JsonElement obj, string key)
    {
        if (obj.ValueKind == JsonValueKind.Array && obj.GetArrayLength() > 0) obj = obj[0];
        if (obj.ValueKind == JsonValueKind.Object)
            foreach (var entry in obj.EnumerateObject()) if (entry.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) return entry.Value;
        return default;
    }
    static string Token(JsonElement obj, string key, string fallback = "default")
    {
        var value = Property(obj, key);
        if (value.ValueKind == JsonValueKind.Undefined) return fallback;
        string token = value.ToString().Trim();
        return token.ToLowerInvariant() switch { "true" => "true", "false" => "false", "default" or "defualt" => "default", _ => token };
    }
    JsonElement Section(string key) => Property(rules, key);
    string Core(Mode m, string field) => Token(Property(Section("Defaults"), m.ToString()), field,
        Token(Property(Property(template, "Defaults"), m.ToString()), field));
    bool Setting(string field) => Token(Section("Settings"), field, "true") == "true";
    static bool Number(string s, out double n) => double.TryParse(s, NumberStyles.Float, Invariant, out n) && double.IsFinite(n);
    double Limit(string field)
    {
        var fallback = Token(Property(template, "Settings"), field);
        if (Number(Token(Section("Settings"), field, fallback), out var n) && n >= 1 && n <= int.MaxValue) return n;
        Warn("Invalid Settings." + field); return double.Parse(fallback, Invariant);
    }
    sealed class ResourceIndex
    {
        public string[] Particles { get; set; } = [];
        public string[] Events { get; set; } = [];
    }
    ResourceIndex? resources;
    void ReadResources()
    {
        string path = Path.Combine(ModuleDirectory, "addon-resources.json");
        resources = null;
        if (!File.Exists(path)) return;
        try { resources = JsonSerializer.Deserialize<ResourceIndex>(File.ReadAllText(path), Json); }
        catch (JsonException ex) { Warn("Invalid addon resource index: " + ex.Message); }
    }
    void LoadRules()
    {
        warnings.Clear();
        using var reader = new StreamReader(typeof(HookGrabRope).Assembly.GetManifestResourceStream("hgr.jsonc")!);
        string defaults = reader.ReadToEnd(), path = DataPath("rules");
        template = JsonSerializer.Deserialize<JsonElement>(defaults, new JsonSerializerOptions { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip });
        rules = template;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path), Loose);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Expected object");
                if (Property(doc.RootElement, "Everyone").ValueKind == JsonValueKind.Undefined && Property(doc.RootElement, "Vip").ValueKind == JsonValueKind.Undefined) rules = doc.RootElement.Clone();
                else
                {
                    string backup = Path.Combine(Path.GetDirectoryName(path)!, "hgr.old.jsonc");
                    if (File.Exists(backup)) backup += "." + Guid.NewGuid().ToString("N");
                    File.Move(path, backup); Warn("Legacy HGR configuration saved to " + backup);
                }
            }
            if (!File.Exists(path)) File.WriteAllText(path, defaults);
        }
        catch (Exception ex) when (ex is IOException or JsonException) { Logger.LogError("HGR defaults used: {Reason}", ex.Message); }
        ReadResources();
        LoadPalette();
        foreach (var line in Lines.Skip(1))
        {
            string resource = Particle(line);
            if (resources?.Particles is { } known && !known.Contains(resource, StringComparer.OrdinalIgnoreCase))
                Warn("Particle missing from packaged addon; external mount required: " + resource);
        }
        foreach (var line in Lines) Particle(line);
        string priority = Token(Section("Settings"), "Priority", "Admin");
        if (!new[] { "Admin", "Vip" }.Contains(priority, StringComparer.OrdinalIgnoreCase)) Warn("Invalid priority: " + priority);
        foreach (var mode in Enum.GetValues<Mode>())
        {
            var policy = new Access(true, false, default, new Preference());
            foreach (var field in Fields(mode)) if (IsChoice(field)) Choice(policy, mode, field); else Numeric(policy, mode, field);
        }
    }
    static readonly Dictionary<string, string> PreferenceKeys = new()
    {
        ["Color"] = "Colors",
        ["EffectLine"] = "EffectLines",
        ["Width"] = "Widths",
        ["Amount"] = "Amounts",
        ["Range"] = "Ranges",
        ["Distance"] = "Distances",
        ["Start.Distance"] = "StartDistances",
        ["Strength"] = "Strengths",
        ["Step up"] = "StepUps",
        ["Step down"] = "StepDowns",
        ["Pull"] = "Pulls",
        ["Sticking"] = "Stickings"
    };
    static Dictionary<ulong, Dictionary<string, object?>> EncodePreferences(Dictionary<ulong, Preference> stored)
    {
        var result = new Dictionary<ulong, Dictionary<string, object?>>();
        foreach (var (id, pref) in stored)
        {
            var record = new Dictionary<string, object?> { { "Chat", pref.Chat }, { "Toast", pref.Toast }, { "Priority", pref.Priority } };
            foreach (var (field, group) in PreferenceKeys)
            {
                var values = new Dictionary<string, object>();
                foreach (var (mode, fields) in pref.Values)
                {
                    if (!fields.TryGetValue(field, out var val)) continue;
                    values[mode] = !IsChoice(field) && Number(val, out var n) ? n :
                        field is "Pull" or "Sticking" && bool.TryParse(val, out var flag) ? flag : val;
                }
                record[group] = values;
            }
            result[id] = record;
        }
        return result;
    }
    Dictionary<ulong, Preference> ReadPreferences(string name)
    {
        var result = new Dictionary<ulong, Preference>();
        if (!File.Exists(DataPath(name))) return result;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(DataPath(name)), Loose);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Expected player dictionary");
            foreach (var row in doc.RootElement.EnumerateObject())
            {
                if (!ulong.TryParse(row.Name, out var id) || row.Value.ValueKind != JsonValueKind.Object) continue;
                var pref = new Preference();
                bool? Flag(string key) => Property(row.Value, key).ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
                pref.Chat = Flag("Chat"); pref.Toast = Flag("Toast"); pref.Priority = Property(row.Value, "Priority").ValueKind == JsonValueKind.String ? Token(row.Value, "Priority") : null;
                result[id] = pref;
                bool legacy = Property(row.Value, "Ranges").ValueKind == JsonValueKind.Undefined;
                pref.Toast ??= Property(row.Value, "Side").ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
                foreach (var m in Enum.GetValues<Mode>())
                {
                    var fields = new Dictionary<string, string>(); pref.Values[m.ToString()] = fields;
                    if (!legacy)
                    {
                        foreach (var (field, group) in PreferenceKeys)
                        {
                            string saved = Token(Property(row.Value, group), m.ToString(), "");
                            if (saved != "") fields[field] = saved;
                        }
                        continue;
                    }
                    foreach (var (old, key) in new[] { ("Sizes", "Width"), ("Amounts", "Amount"), ("Colors", "Color"), ("Lengths", m == Mode.Grab ? "Distance" : "Range") })
                    {
                        var v = Token(Property(row.Value, old), m.ToString(), "");
                        if (v != "")
                        {
                            if (key == "Color" && v.StartsWith("#")) v = colors.FirstOrDefault(pair => pair.Value.Equals(v, StringComparison.OrdinalIgnoreCase)).Key ?? "default";
                            fields[key] = key == "Amount" && v == "0" ? "-1" : v;
                        }
                    }
                    if (m == Mode.Grab) foreach (var (old, key) in new[] { ("ThrowForce", "Strength"), ("Capture", "Range"), ("Distance", "Start.Distance"), ("PullOnGrab", "Pull"), ("StepCloser", "Step down"), ("StepFarther", "Step up") })
                    {
                        var v = Token(row.Value, old, ""); if (v != "") fields[key] = v;
                    }
                }
            }
        }
        catch (JsonException ex) { Warn(name + ": " + ex.Message); }
        return result;
    }
    static IEnumerable<string> Fields(Mode m)
    {
        foreach (string f in new[] { "Color", "EffectLine", "Width", "Amount", "Range" }) yield return f;
        if (m != Mode.Rope) yield return "Distance";
        if (m == Mode.Grab) { yield return "Start.Distance"; yield return "Strength"; }
        foreach (string f in new[] { "Step up", "Step down", "Pull", "Sticking" }) yield return f;
    }
    record Access(bool Allowed, bool Vip, JsonElement Grant, Preference Pref);
    JsonElement VipGrant(CCSPlayerController p, Mode m)
    {
        if (!Vip(p)) return default;
        object? Get(string key) => Optional("VipCoreApi.IVipCoreApi", "vipcore:core", "GetFeatureValue", p, key);
        var grant = Get("HGR") is JsonElement { ValueKind: JsonValueKind.Object } group ? Property(group, m.ToString()) : Get(m.ToString()) is JsonElement direct ? direct : default;
        if (grant.ValueKind != JsonValueKind.Object) return default;
        foreach (var e in grant.EnumerateObject())
        {
            if (new[] { "Size", "Length", "ThrowForce", "CaptureDistance", "StartDistance", "PullOnGrab", "StepCloser", "StepFarther" }.Contains(e.Name, StringComparer.OrdinalIgnoreCase) || e.Name.Equals("Enabled", StringComparison.OrdinalIgnoreCase) && e.Value.ToString().Equals("On", StringComparison.OrdinalIgnoreCase))
            {
                Warn("Legacy VIP grant: " + m); return default;
            }
            if (!Fields(m).Append("Enabled").Contains(e.Name, StringComparer.OrdinalIgnoreCase)) Warn($"Ignored VIP key {m}.{e.Name}");
        }
        return Token(grant, "Enabled").ToLowerInvariant() is "false" or "off" ? default : grant;
    }
    bool HasVipGrant(CCSPlayerController p) => Enum.GetValues<Mode>().Any(m => VipGrant(p, m).ValueKind == JsonValueKind.Object);
    string Priority(CCSPlayerController p)
    {
        var selected = prefs.GetValueOrDefault(p.SteamID)?.Priority ?? Token(Section("Settings"), "Priority", "Admin");
        if (selected.Equals("Vip", StringComparison.OrdinalIgnoreCase)) return "Vip";
        if (!selected.Equals("Admin", StringComparison.OrdinalIgnoreCase)) Warn("Invalid priority: " + selected);
        return "Admin";
    }
    bool UsesVip(CCSPlayerController p) => !Admin(p) || Priority(p) == "Vip" && HasVipGrant(p);
    Preference NotificationPrefs(CCSPlayerController p) => (UsesVip(p) ? vipPrefs : prefs).GetValueOrDefault(p.SteamID) ?? new();
    Access Policy(CCSPlayerController p, Mode m)
    {
        bool vip = UsesVip(p); var grant = vip ? VipGrant(p, m) : default;
        return new(Core(m, "Enabled") == "true" && (vip ? grant.ValueKind == JsonValueKind.Object : Admin(p)), vip, grant, (vip ? vipPrefs : prefs).GetValueOrDefault(p.SteamID) ?? new());
    }
    record NumericRule(double Value, double Min, double Max, bool Editable, bool Unlimited);
    NumericRule Numeric(Access e, Mode m, string f)
    {
        bool amount = f == "Amount";
        double min = f switch { "Width" => .5, "Range" or "Strength" => 50, "Distance" or "Start.Distance" => 0, _ => 1 };
        double max = amount ? int.MaxValue - 1 : Limit("Max" + (f switch { "Start.Distance" => "StartDistance", "Step up" or "Step down" => "Step", _ => f }));
        max = Math.Max(min, max);
        string basis = Core(m, f), grant = Token(e.Grant, f);
        if (!Number(basis, out var val)) { Warn($"Invalid {m}.{f}: {basis}"); Number(Token(Property(Property(template, "Defaults"), m.ToString()), f), out val); }
        bool unlimited = amount, edit = true;
        if (!(amount && val == -1) && (val < min || val > max)) Warn($"Clamped {m}.{f}: {val}");
        if (grant == "false") edit = false;
        else if (grant != "default")
        {
            if (Number(grant, out var fixedValue)) { val = fixedValue; edit = false; }
            else
            {
                var sides = grant.Split('-', 2, StringSplitOptions.TrimEntries);
                if (sides.Length == 2 && Number(sides[0], out var lo) && Number(sides[1], out var hi) && lo <= hi)
                {
                    if (lo < min || hi > max) Warn($"Clamped VIP {m}.{f}: {grant}");
                    min = Math.Clamp(lo, min, max); max = Math.Clamp(hi, min, max); unlimited = false;
                }
                else Warn($"Invalid VIP {m}.{f}: {grant}");
            }
        }
        if (amount && edit && !unlimited)
        {
            min = Math.Ceiling(min); max = Math.Floor(max);
            if (min > max) { Warn($"No integral amount in {grant}"); max = min; }
        }
        if (!edit && !(amount && val == -1) && (val < min || val > max)) Warn($"Clamped fixed {m}.{f}: {val}");
        if (edit && Number(e.Pref.Values.GetValueOrDefault(m.ToString())?.GetValueOrDefault(f) ?? "", out var personal)) val = personal;
        if (amount && val == -1) val = unlimited ? -1 : max;
        else val = Math.Clamp(amount ? Math.Truncate(val) : val, min, max);
        return new(val, min, max, edit && min != max, unlimited);
    }
    readonly Dictionary<string, string> colors = new(StringComparer.OrdinalIgnoreCase);
    string[] palette = [];
    void LoadPalette()
    {
        colors.Clear();
        var hex = Section("PaletteHex");
        if (hex.ValueKind == JsonValueKind.Undefined) hex = Property(template, "PaletteHex");
        if (hex.ValueKind == JsonValueKind.Array && hex.GetArrayLength() > 0) hex = hex[0];
        if (hex.ValueKind == JsonValueKind.Object) foreach (var c in hex.EnumerateObject())
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(c.Value.ToString(), "^#[a-fA-F0-9]{6}$")) colors[c.Name] = c.Value.ToString(); else Warn("Invalid HEX: " + c.Name);
        }
        var special = Section("PaletteDef");
        palette = (special.ValueKind == JsonValueKind.Array ? special.EnumerateArray().Select(x => x.ToString()).Where(x => new[] { "default", "rainbow", "team" }.Contains(x, StringComparer.OrdinalIgnoreCase)) : new[] { "default", "rainbow", "team" }).Concat(colors.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (palette.Length == 0) palette = ["default"];
    }
    static bool IsChoice(string f) => f is "Color" or "EffectLine" or "Pull" or "Sticking";
    record ChoiceRule(string Value, string[] Options, bool Editable);
    ChoiceRule Choice(Access e, Mode m, string f)
    {
        bool toggle = f is "Pull" or "Sticking";
        string[] options = f switch { "Color" => palette, "EffectLine" => Lines, "Pull" when m == Mode.Hook => ["true", "Self", "false"], _ => ["true", "false"] };
        string val = Core(m, f), grant = Token(e.Grant, f);
        if (f == "Sticking" && val.Equals("Self", StringComparison.OrdinalIgnoreCase)) val = "true";
        if (!options.Contains(val, StringComparer.OrdinalIgnoreCase))
        {
            Warn($"Invalid {m}.{f}: {val}");
            var original = Token(Property(Property(template, "Defaults"), m.ToString()), f);
            val = options.FirstOrDefault(x => x.Equals(original, StringComparison.OrdinalIgnoreCase)) ?? options[0];
        }
        if (grant == "false") return new(toggle ? "false" : val, [], false);
        if (toggle && grant == "true") val = "true";
        if (toggle && grant is not ("default" or "true") && !(m == Mode.Hook && f == "Pull" && grant.Equals("Self", StringComparison.OrdinalIgnoreCase))) Warn($"Invalid VIP {m}.{f}: {grant}");
        if (!toggle && grant != "default" && !grant.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            string[] requested = grant.Split(',', StringSplitOptions.TrimEntries);
            foreach (var item in requested.Except(options, StringComparer.OrdinalIgnoreCase)) Warn($"Unknown VIP {m}.{f}: {item}");
            var allowed = options.Intersect(requested, StringComparer.OrdinalIgnoreCase).ToArray();
            if (allowed.Length == 0) { Warn($"Invalid VIP {m}.{f}: {grant}"); return new(val, [], false); }
            options = allowed;
        }
        string? personal = e.Pref.Values.GetValueOrDefault(m.ToString())?.GetValueOrDefault(f);
        string? Match(string? wanted) => options.FirstOrDefault(x => x.Equals(wanted, StringComparison.OrdinalIgnoreCase));
        return new(Match(personal) ?? Match(val) ?? options[0], options, options.Length > 1);
    }
    string Tint(string name, Mode m) => name.Equals("default", StringComparison.OrdinalIgnoreCase) ? Core(m, "Color") : name;
    Color ResolvedColor(string name, Mode m, int team)
    {
        name = Tint(name, m);
        if (name.Equals("team", StringComparison.OrdinalIgnoreCase)) return team == 2 ? Color.Goldenrod : Color.CornflowerBlue;
        return colors.TryGetValue(name, out var hex) ? ColorTranslator.FromHtml(hex) : m == Mode.Grab ? Color.Yellow : m == Mode.Hook ? Color.Red : Color.Teal;
    }
    static Color Rainbow()
    {
        double t = Server.CurrentTime * 2;
        int Channel(double phase) => (int)(128 + 127 * Math.Sin(t + phase));
        return Color.FromArgb(Channel(0), Channel(2.094), Channel(4.189));
    }
}
