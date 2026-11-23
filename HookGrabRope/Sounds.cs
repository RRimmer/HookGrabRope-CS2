using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.UserMessages;

public sealed partial class HookGrabRope
{
    void Play(Session session, string key)
    {
        if (!Setting("Sounds")) return;
        string path = Token(Property(Section("Defaults"), session.Mode.ToString()), key, "");
        if (string.IsNullOrWhiteSpace(path) || path == "false") return;
        var pawn = new CHandle<CCSPlayerPawn>(session.Pawn).Value;
        if (pawn == null) return;
        string name = "HGR." + Path.GetFileNameWithoutExtension(path);
        if (resources?.Events is { } events && !events.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            Warn("Event missing from soundevents/hgr.vsndevts: " + name);
            return;
        }
        uint guid = pawn.EmitSound(name);
        if (guid is 0 or uint.MaxValue) { Warn("Sound event unavailable: " + name); return; }
        if (key != "SoundRopeOut") session.Sounds.Add(guid);
    }
    static void StopSounds(Session session)
    {
        foreach (uint guid in session.Sounds)
        {
            using var message = UserMessage.FromId((int)EBaseGameEvents.GE_SosStopSoundEvent);
            message.SetUInt("soundevent_guid", guid);
            message.Recipients.AddAllPlayers();
            message.Send();
        }
        session.Sounds.Clear();
    }
}
