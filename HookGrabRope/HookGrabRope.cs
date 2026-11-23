using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using V = System.Numerics.Vector3;
using System.Drawing;

public sealed partial class HookGrabRope : BasePlugin
{
    public override string ModuleName => "Hook Grab Rope";
    public override string ModuleVersion => "0.1.0";
    string Particle(string line)
    {
        string fallback = Token(Property(template, "Particles"), line);
        string path = Token(Section("Particles"), line, fallback);
        bool beam = line.Equals("Beam", StringComparison.OrdinalIgnoreCase);
        if (path.StartsWith(beam ? "materials/" : "particles/", StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(beam ? ".vmat" : ".vpcf", StringComparison.OrdinalIgnoreCase) && !path.Contains("..")) return path;
        Warn("Invalid particle resource: " + path); return fallback;
    }
    string? debugLine;
    enum Mode { Grab, Hook, Rope }
    sealed class Session(Mode mode, uint pawn)
    {
        public readonly Mode Mode = mode;
        public readonly uint Pawn = pawn;
        public float Up, Down, Width, Range, MaxDistance, Strength, InitialLength;
        public string Pull = "false", Line = "Beam";
        public bool Cycling; public Color BeamColor; public uint? Target;
        public V HookEnd;
        public readonly List<uint> Sounds = [];
        public CHandle<CParticleSystem>? Wire;
        public float GrabDistance = 150;
        public V Anchor;
        public float Length;

        public readonly List<CHandle<CBeam>> Beams = [];
    }
    readonly Dictionary<int, Session> sessions = [];
    static V Read(Vector v) => new(v.X, v.Y, v.Z);
    static Vector Write(V v) => new(v.X, v.Y, v.Z);

    public override void Load(bool hotReload)
    {
        SetupMenu();
        AddCommand("css_hgr_visual", "Grab debug override: beam|laser|lasso|chain|default", (player, cmd) =>
        {
            if (player != null) return;
            if (cmd.ArgCount > 1)
            {
                string arg = cmd.GetArg(1);
                if (arg.Equals("default", StringComparison.OrdinalIgnoreCase)) debugLine = null;
                else if (Lines.FirstOrDefault(x => x.Equals(arg, StringComparison.OrdinalIgnoreCase)) is string line) debugLine = line;
            }
            cmd.ReplyToCommand($"[HGR] Grab: {debugLine ?? "default"}");
        });
        if (!HookHandleDrop()) AddCommandListener("drop", (p, _) => ThrowHeld(p) ? HookResult.Handled : HookResult.Continue, HookMode.Pre);
        foreach (var mode in Enum.GetValues<Mode>())
        {
            var name = mode.ToString().ToLowerInvariant();
            AddCommand("css_" + name, "Toggle " + name, (p, _) =>
            {
                if (p == null) return;
                if (sessions.TryGetValue(p.Slot, out var s) && s.Mode == mode) Stop(p.Slot);
                else if (!Policy(p, mode).Allowed) return;
                else if (Choice(Policy(p, mode), mode, "Sticking").Value == "false") { Tell(p, "hold-bind", mode); Binds(p, mode); }
                else Start(p, mode);
            });
            AddCommand("css_" + name + "_on", "Start " + name, (p, _) => Start(p, mode));
            AddCommand("css_" + name + "_off", "Stop " + name, (p, _) =>
            {
                if (p != null && sessions.TryGetValue(p.Slot, out var s) && s.Mode == mode) Stop(p.Slot);
            });
            AddCommand($"css_{name}_farther", "Lengthen", (p, _) => Wheel(p, mode, false));
            AddCommand($"css_{name}_closer", "Shorten", (p, _) => Wheel(p, mode, true));
        }
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            foreach (var line in Lines) manifest.AddResource(Particle(line));
            manifest.AddResource("soundevents/hgr.vsndevts");
        });
        RegisterListener<Listeners.OnClientDisconnect>(Stop);
        RegisterListener<Listeners.OnMapEnd>(Clear);
        RegisterListener<Listeners.OnTick>(Tick);
    }

    bool ThrowHeld(CCSPlayerController? p)
    {
        if (p?.PlayerPawn.Value is not { } pawn || !p.PawnIsAlive || !sessions.TryGetValue(p.Slot, out var s)
           || s.Pawn != pawn.EntityHandle.Raw || s.Mode != Mode.Grab || s.Target is not uint handle) return false;
        var target = new CHandle<CBaseEntity>(handle).Value;
        if (target?.AbsOrigin == null || (target.DesignerName == "player" ? target.LifeState != 0 : target.OwnerEntity.IsValid))
        {
            Stop(p.Slot); return false;
        }
        var angles = pawn.EyeAngles;
        float pitch = angles.X * MathF.PI / 180, yaw = angles.Y * MathF.PI / 180;
        var forward = new V(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), -MathF.Sin(pitch));
        target.Teleport(null, null, Write(forward * s.Strength));
        Stop(p.Slot);
        return true;
    }

    void Wheel(CCSPlayerController? p, Mode mode, bool closer)
    {
        if (p == null || !sessions.TryGetValue(p.Slot, out var s) || s.Mode != mode) return;
        if (mode == Mode.Grab)
        {
            if (closer) s.GrabDistance = Math.Max(50, s.GrabDistance - s.Down);
            else if (s.GrabDistance < s.MaxDistance) s.GrabDistance = Math.Min(s.MaxDistance, s.GrabDistance + s.Up);
        }
        else if (mode == Mode.Rope ? s.Pull == "true" : s.Pull == "Self")
        {
            float min = mode == Mode.Rope ? Math.Min(32, s.InitialLength) : Math.Max(Math.Min(32, s.InitialLength), s.InitialLength - s.MaxDistance);
            s.Length = Math.Clamp(s.Length + (closer ? -s.Up : s.Down), min, Math.Max(min, mode == Mode.Rope ? s.Range : s.InitialLength));
        }
    }
    void Start(CCSPlayerController? p, Mode mode)
    {
        if (p?.PlayerPawn.Value is not { AbsOrigin: not null } pawn || !p.PawnIsAlive) return;
        if (sessions.TryGetValue(p.Slot, out var active) && active.Mode == mode) return;
        var policy = Policy(p, mode);
        if (!policy.Allowed) return;
        if (Remaining(p, mode) == 0) { Tell(p, "limit", mode); return; }
        float Num(string f) => (float)Numeric(policy, mode, f).Value;
        string ChoiceOf(string f) => Choice(policy, mode, f).Value;
        string color = ChoiceOf("Color");
        var s = new Session(mode, pawn.EntityHandle.Raw)
        {
            Up = Num("Step up"),
            Down = Num("Step down"),
            Width = Num("Width"),
            Range = Num("Range"),
            MaxDistance = mode == Mode.Rope ? Num("Range") : Num("Distance"),
            Pull = ChoiceOf("Pull"),
            Strength = mode == Mode.Grab ? Num("Strength") : 0,
            Line = mode == Mode.Grab && debugLine != null ? debugLine : ChoiceOf("EffectLine"),
            Cycling = Tint(color, mode).Equals("rainbow", StringComparison.OrdinalIgnoreCase),
            BeamColor = ResolvedColor(color, mode, p.TeamNum)
        };
        var origin = Read(pawn.AbsOrigin);
        var eye = origin + new V(pawn.ViewOffset.X, pawn.ViewOffset.Y, pawn.ViewOffset.Z);
        if (mode == Mode.Grab) Play(s, "SoundGrabCapture");
        var hit = Trace.TraceShape(Write(eye), pawn.EyeAngles, pawn);
        if (!hit.DidHit() || hit.IsAllSolid || V.Distance(eye, Read(hit.EndPos)) > s.Range) return;
        if (mode == Mode.Grab)
        {
            var ent = hit.HitEntity();
            if (ent.Handle == 0 || !ent.IsValid || sessions.Values.Any(other => other.Target == ent.EntityHandle.Raw)) return;
            var target = ent.As<CBaseEntity>();
            if (target.AbsOrigin == null) return;
            if (ent.DesignerName == "player") { if (target.LifeState != 0) return; }
            else if (!ent.DesignerName.StartsWith("weapon_") || target.OwnerEntity.IsValid) return;
            var center = Read(target.AbsOrigin);
            if (ent.DesignerName == "player") center.Z += ent.As<CCSPlayerPawn>().Collision.Maxs.Z / 2;
            s.GrabDistance = s.Pull == "true" ? Math.Clamp(Num("Start.Distance"), 50, Math.Max(50, s.MaxDistance)) : V.Distance(eye, center);
            s.Target = ent.EntityHandle.Raw;
        }
        else
        {
            s.Anchor = Read(hit.EndPos);
            s.Length = s.InitialLength = V.Distance(origin, s.Anchor);
            var toward = s.Anchor - origin;
            s.HookEnd = origin + toward / Math.Max(.001f, s.Length) * Math.Min(s.MaxDistance, Math.Max(0, s.Length - 32));
        }
        Stop(p.Slot); sessions[p.Slot] = s; Charge(p, mode);
        if (mode == Mode.Grab)
        {
            var ent = new CHandle<CBaseEntity>(s.Target!.Value).Value!;
            var victim = ent.DesignerName == "player" ? ent.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>() : null;
            Tell(p, "caught", victim?.PlayerName ?? ent.DesignerName);
            if (victim is { IsBot: false }) Tell(victim, "held", p.PlayerName);
            Play(s, "SoundGrabCaught"); Play(s, "SoundGrabHold");
        }
        else if (mode == Mode.Hook) { Play(s, "SoundHook"); if (s.Pull == "true") Play(s, "SoundHookPull"); }
        else Play(s, "SoundRope");
    }

    void Tick()
    {
        foreach (var (slot, s) in sessions.ToArray())
        {
            var pawn = new CHandle<CCSPlayerPawn>(s.Pawn).Value;
            if (pawn?.AbsOrigin == null || pawn.LifeState != 0) { Stop(slot); continue; }
            var origin = Read(pawn.AbsOrigin);
            var eye = origin + new V(pawn.ViewOffset.X, pawn.ViewOffset.Y, pawn.ViewOffset.Z);
            if (s.Mode == Mode.Grab)
            {
                var target = new CHandle<CBaseEntity>(s.Target!.Value).Value;
                if (target?.AbsOrigin == null || (target.DesignerName == "player" ? target.LifeState != 0 : target.OwnerEntity.IsValid))
                { Stop(slot); continue; }
                var center = Read(target.AbsOrigin);
                if (target.DesignerName == "player") center.Z += target.As<CCSPlayerPawn>().Collision.Maxs.Z / 2;
                var angles = pawn.EyeAngles;
                float pitch = angles.X * MathF.PI / 180, yaw = angles.Y * MathF.PI / 180;
                var forward = new V(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), -MathF.Sin(pitch));
                var control = eye + forward * s.GrabDistance;
                target.Teleport(null, null, Write((control - center) * 5));
                DrawLine(s, eye + forward * 12 - new V(0, 0, 20), control - new V(0, 0, 20), center, 11);
            }
            else
            {
                var delta = s.Anchor - origin;
                float distance = delta.Length();
                if (s.Mode == Mode.Hook && s.Pull == "true")
                {
                    var error = s.HookEnd - origin;
                    pawn.Teleport(null, null, Write(error * Math.Min(6, 800 / Math.Max(.001f, error.Length()))));
                }
                else if (distance >= s.Length && distance > .001f)
                {
                    var direction = delta / distance;
                    var velocity = Read(pawn.AbsVelocity);
                    float correction = Math.Max(0, (distance - s.Length) * 8 - V.Dot(velocity, direction));
                    if (correction > 0) pawn.Teleport(null, null, Write(velocity + direction * correction));
                }
                var start = eye - new V(0, 0, 20);
                DrawLine(s, start, start, s.Anchor, 1);
            }
        }
    }
    void DrawLine(Session s, V start, V control, V end, int segments)
    {
        if (s.Line.Equals("Beam", StringComparison.OrdinalIgnoreCase)) { Draw(s, start, control, end, segments); return; }
        var wire = s.Wire?.Value; bool spawn = wire == null;
        if (spawn)
        {
            wire = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
            if (wire == null) { Draw(s, start, control, end, segments); return; }
            wire.EffectName = Particle(s.Line); wire.StartActive = false; wire.Teleport(Write(start), null, null); wire.DispatchSpawn();
            s.Wire = new(wire.EntityHandle.Raw); ClearBeams(s);
        }
        else wire!.Teleport(Write(start), null, null);
        wire!.AcceptInput("SetControlPoint", value: FormattableString.Invariant($"1: {end.X} {end.Y} {end.Z}"));
        if (spawn || s.Cycling)
        {
            var tint = s.Cycling ? Rainbow() : s.BeamColor;
            wire.AcceptInput("SetControlPoint", value: $"2: {tint.R} {tint.G} {tint.B}");
        }
        if (spawn)
        {
            float scale = s.Width / 10f;
            if (s.Line.Equals("Lasso", StringComparison.OrdinalIgnoreCase)) scale = Math.Min(scale, 1.2f);
            wire.AcceptInput("SetControlPoint", value: FormattableString.Invariant($"3: {scale} 0 0"));
            wire.AcceptInput("Start");
        }
    }
    static void ClearBeams(Session s)
    {
        foreach (var beam in s.Beams) beam.Value?.Remove();
        s.Beams.Clear();
    }

    static void ClearVisual(Session s)
    {
        ClearBeams(s);
        if (s.Wire is { Value: { } wire } handle)
        {
            wire.AcceptInput("StopPlayEndCap");
            wire.Active = false;
            Utilities.SetStateChanged(wire, "CParticleSystem", "m_bActive");
            Utilities.SetStateChanged(wire, "CParticleSystem", "m_nStopType");
            Server.RunOnTick(Server.TickCount + 8, () =>
            {
                if (handle.Value is not { IsValid: true } stopped) return;
                stopped.AcceptInput("DestroyImmediately");
                stopped.Remove();
            });
        }
        s.Wire = null;
    }

    void Draw(Session s, V start, V control, V end, int count)
    {
        var previous = start;
        for (int i = 0; i < count; i++)
        {
            if (s.Beams.Count == i)
            {
                var created = Utilities.CreateEntityByName<CBeam>("beam");
                if (created == null) return;
                created.Width = created.EndWidth = s.Width;
                created.Render = s.BeamColor;
                created.DispatchSpawn();
                created.SetModel(Particle("Beam"));
                s.Beams.Add(new(created.EntityHandle.Raw));
            }
            var beam = s.Beams[i].Value;
            float t = (i + 1f) / count;
            var point = V.Lerp(V.Lerp(start, control, t), V.Lerp(control, end, t), t);
            if (beam != null)
            {
                if (s.Cycling && Server.TickCount % 4 == 0) { beam.Render = Rainbow(); Utilities.SetStateChanged(beam, "CBaseModelEntity", "m_clrRender"); }
                beam.Teleport(Write(previous), null, null);
                beam.EndPos.X = point.X; beam.EndPos.Y = point.Y; beam.EndPos.Z = point.Z;
                Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");
            }
            previous = point;
        }
    }

    void Stop(int slot)
    {
        if (sessions.Remove(slot, out var s))
        {
            ClearVisual(s);
            StopSounds(s);
            if (s.Mode == Mode.Rope) Play(s, "SoundRopeOut");
        }
    }
    void Clear() { foreach (var slot in sessions.Keys.ToArray()) Stop(slot); }
    public override void Unload(bool hotReload) { _handleDrop?.Unhook(OnHandleDrop, HookMode.Pre); CloseAllMenus(); Clear(); }
}
