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
/// written to the game log); `cfs reload` re-reads the config and rebuilds the effect.
///
/// The dev console discovers commands in loaded mods by reflection, so shipping this public
/// class with a parameterless constructor is enough to register it.
/// </summary>
public class SaleConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "cfs";

    public override string Args => "[diag|reload]";

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

            default:
                return new CmdResult(success: false, "Use: cfs diag or cfs reload.");
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
            return CompleteArgument(new[] { "diag", "reload" }, Array.Empty<string>(), args.FirstOrDefault() ?? "");
        }

        return new CompletionResult { Type = CompletionType.Argument, ArgumentContext = CmdName };
    }
}
