using System.Text.Json;
using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;

public sealed partial class HookGrabRope
{
    private const string GameDataKey = "PMM_BuyMenu_HandleDropWeapon";
    private MemoryFunctionVoid<nint, nint>? _handleDrop;

    private string? ReadSignature()
    {
        string path = Path.Combine(ModuleDirectory, "gamedata", "HookGrabRope.gamedata.json");
        if (!File.Exists(path))
        {
            Logger.LogError("no gamedata file {Path}: G falls back to the \"drop\" command", path);
            return null;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!doc.RootElement.TryGetProperty(GameDataKey, out var entry) ||
            !entry.TryGetProperty("signatures", out var sigs) ||
            !sigs.TryGetProperty(OperatingSystem.IsWindows() ? "windows" : "linux", out var sig) ||
            sig.ValueKind != JsonValueKind.String)
        {
            Logger.LogError("{Key}: no signature for this OS in {Path}", GameDataKey, path);
            return null;
        }
        return sig.GetString();
    }

    private bool HookHandleDrop()
    {
        try
        {
            string? sig = ReadSignature();
            if (string.IsNullOrWhiteSpace(sig)) return false;
            _handleDrop = new MemoryFunctionVoid<nint, nint>(sig);
            _handleDrop.Hook(OnHandleDrop, HookMode.Pre);
            Logger.LogInformation("G: {Key} hooked", GameDataKey);
            return true;
        }
        catch (Exception ex)
        {
            _handleDrop = null;
            Logger.LogError("G: {Key} hook failed ({Msg}): falling back to the \"drop\" command", GameDataKey, ex.Message);
            return false;
        }
    }

    private HookResult OnHandleDrop(DynamicHook hook)
    {
        nint self;
        try
        {
            self = hook.GetParam<nint>(0);
        }
        catch
        {
            return HookResult.Continue;
        }
        CCSPlayerController? player = null;
        foreach (var p in Utilities.GetPlayers())
        {
            if (!p.IsValid) continue;
            var pawn = p.PlayerPawn.Value;
            if ((pawn != null && pawn.Handle == self) || p.Handle == self)
            {
                player = p;
                break;
            }
        }

        return ThrowHeld(player) ? HookResult.Handled : HookResult.Continue;
    }


}
