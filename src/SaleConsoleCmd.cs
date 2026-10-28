using System;
using System.Linq;
using System.Text;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;

namespace CurseForSale;

/// <summary>
/// `cfs diag` says whether the card removal coin should wear the curse effect and why (also
/// written to the game log); `cfs reload` re-reads the config and rebuilds the effect; `cfs flare
/// <style>` switches the light behind the coin until the next reload.
///
/// The dev console discovers commands in loaded mods by reflection, so shipping this public
/// class with a parameterless constructor is enough to register it.
/// </summary>
public class SaleConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "cfs";

    public override string Args => "[diag|reload|flare <rays|duotone|tendrils|streak|none>]";

    public override string Description => "CurseForSale: explain the card removal effect, or reload the config.";

    /// <summary>The effect is local to this client — nothing to synchronise.</summary>
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        switch (args.FirstOrDefault()?.ToLowerInvariant() ?? "diag")
        {
            case "diag":
                return Diag();

            case "reload":
                SaleConfig.Reload();
                if (CurseAura.LastSlot is { } slot)
                {
                    CurseAura.Restyle(slot);
                }

                return new CmdResult(success: true, "Config reloaded.");

            case "flare":
                string? style = args.Skip(1).FirstOrDefault()?.ToLowerInvariant();
                if (style == null || (style != CurseVisuals.NoFlare && !CurseVisuals.FlareStyles.Contains(style)))
                {
                    return new CmdResult(success: false, $"Use: cfs flare {string.Join(", ", CurseVisuals.FlareStyles)} or none.");
                }

                // Until the next reload or restart; the config file is not changed.
                SaleConfig.Current.Flare = style;
                if (CurseAura.LastSlot is { } shown)
                {
                    CurseAura.Restyle(shown);
                }

                return new CmdResult(success: true, $"Flare: {style}.");

            default:
                return new CmdResult(success: false, "Use: cfs diag, cfs reload or cfs flare <style>.");
        }
    }

    private static CmdResult Diag()
    {
        if (CurseAura.LastSlot is not { } slot || CurseAura.PlayerOf(slot) is not { } player)
        {
            return new CmdResult(success: false, "Open a shop first (dev console: room Shop).");
        }

        var (show, reason) = CurseAura.Evaluate(slot);
        var (removable, eternal) = CurseAura.Curses(player);
        var text = new StringBuilder()
            .Append("Effect: ").Append(show ? "on" : "off").Append(" (").Append(reason).AppendLine(")")
            .Append("Removal: ").Append(slot.Entry.IsStocked ? $"available, {slot.Entry.Cost} gold" : "not available")
            .Append(", you have ").Append(player.Gold).AppendLine(" gold")
            .Append("Removable curses: ").AppendLine(removable.Count > 0 ? string.Join(", ", removable.Select(c => c.Title)) : "none")
            .Append("Eternal curses: ").Append(eternal.Count > 0 ? string.Join(", ", eternal.Select(c => c.Title)) : "none");

        string result = text.ToString();
        Log.Info($"[CurseForSale] cfs diag:\n{result}");
        return new CmdResult(success: true, result);
    }

    public override CompletionResult GetArgumentCompletions(Player? player, string[] args)
    {
        if (args.Length <= 1)
        {
            return CompleteArgument(new[] { "diag", "reload", "flare" }, Array.Empty<string>(), args.FirstOrDefault() ?? "");
        }

        if (args.Length == 2 && args[0].Equals("flare", StringComparison.OrdinalIgnoreCase))
        {
            return CompleteArgument(CurseVisuals.FlareStyles.Append(CurseVisuals.NoFlare).ToArray(), Array.Empty<string>(), args[1]);
        }

        return new CompletionResult { Type = CompletionType.Argument, ArgumentContext = CmdName };
    }
}
