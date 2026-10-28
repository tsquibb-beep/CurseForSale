using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace CurseForSale;

/// <summary>
/// Decides when the card removal coin wears the curse effect and keeps it in step.
///
/// The effect shows while the removal service is still in stock and the deck holds a curse
/// the removal screen would offer, i.e. one without Eternal (the game's own
/// <see cref="CardModel.IsRemovable"/>). Optionally only while you can afford it.
/// </summary>
internal static class CurseAura
{
    private sealed class State
    {
        public Sprite2D? Aura;

        public Sprite2D? Shadow;

        public Tween? Fade;

        public bool Shown;

        public CardPile? Deck;

        public Action? OnDeckChanged;
    }

    private static readonly ConditionalWeakTable<NMerchantCardRemoval, State> _states = new();

    // NMerchantSlot.Player is protected; it is the shop's owner, i.e. the local player.
    private static readonly Func<NMerchantSlot, Player?> _player =
        AccessTools.MethodDelegate<Func<NMerchantSlot, Player?>>(AccessTools.PropertyGetter(typeof(NMerchantSlot), "Player"));

    private static WeakReference<NMerchantCardRemoval>? _last;

    /// <summary>The slot of the shop open now (or last), for the console command.</summary>
    public static NMerchantCardRemoval? LastSlot =>
        _last != null && _last.TryGetTarget(out var slot) && GodotObject.IsInstanceValid(slot) ? slot : null;

    public static Player? PlayerOf(NMerchantSlot slot) => _player(slot);

    /// <summary>Curses in the deck, split by whether the removal screen would offer them.</summary>
    public static (List<CardModel> Removable, List<CardModel> Eternal) Curses(Player player)
    {
        var curses = player.Deck.Cards.Where(c => c.Type == CardType.Curse).ToList();
        return (curses.Where(c => c.IsRemovable).ToList(), curses.Where(c => !c.IsRemovable).ToList());
    }

    /// <summary>Whether the effect should show, with the reason when it should not.</summary>
    public static (bool Show, string Reason) Evaluate(NMerchantCardRemoval slot)
    {
        SaleConfig config = SaleConfig.Current;
        if (!config.Enabled)
        {
            return (false, "the mod is turned off in the config");
        }

        if (slot.Entry is not { } entry || PlayerOf(slot) is not { } player)
        {
            return (false, "the shop is not set up yet");
        }

        if (!entry.IsStocked)
        {
            return (false, "card removal is not available (used, or not offered)");
        }

        if (config.OnlyWhenAffordable && !entry.EnoughGold)
        {
            return (false, $"not enough gold ({player.Gold} of {entry.Cost})");
        }

        var (removable, eternal) = Curses(player);
        if (removable.Count == 0)
        {
            return (false, eternal.Count > 0 ? "every curse in the deck is Eternal" : "no curses in the deck");
        }

        return (true, $"{removable.Count} removable curse(s)");
    }

    /// <summary>Called once the slot has its entry: watch the deck for curses arriving or leaving.</summary>
    public static void Attach(NMerchantCardRemoval slot)
    {
        _last = new WeakReference<NMerchantCardRemoval>(slot);
        State state = _states.GetOrCreateValue(slot);
        Unsubscribe(state);
        if (PlayerOf(slot) is { } player)
        {
            state.Deck = player.Deck;
            state.OnDeckChanged = () =>
            {
                if (GodotObject.IsInstanceValid(slot))
                {
                    Refresh(slot);
                }
            };
            state.Deck.ContentsChanged += state.OnDeckChanged;
        }
    }

    public static void Detach(NMerchantCardRemoval slot)
    {
        if (_states.TryGetValue(slot, out State? state))
        {
            Unsubscribe(state);
            state.Fade?.Kill();
        }
    }

    private static void Unsubscribe(State state)
    {
        if (state.Deck != null && state.OnDeckChanged != null)
        {
            state.Deck.ContentsChanged -= state.OnDeckChanged;
        }

        state.Deck = null;
        state.OnDeckChanged = null;
    }

    public static void Refresh(NMerchantCardRemoval slot)
    {
        try
        {
            bool show = Evaluate(slot).Show;
            State state = _states.GetOrCreateValue(slot);
            if (show == state.Shown && (state.Aura != null || !show))
            {
                return;
            }

            if (show && (state.Aura == null || !GodotObject.IsInstanceValid(state.Aura)))
            {
                if (slot.GetNodeOrNull<Sprite2D>("%Visual") is not { Texture: not null } coin)
                {
                    return;
                }

                (state.Aura, state.Shadow) = CurseVisuals.Create(coin, SaleConfig.Current);
            }

            state.Shown = show;
            if (state.Aura == null || state.Shadow == null)
            {
                return;
            }

            state.Fade?.Kill();
            state.Fade = CurseVisuals.FadeTo(state.Aura, state.Shadow, show ? 1f : 0f, Mathf.Max(SaleConfig.Current.FadeSeconds, 0f));
        }
        catch (Exception ex)
        {
            Log.Error($"[CurseForSale] Refresh failed: {ex}");
        }
    }

    /// <summary>After a config reload: drop the sprites so the next refresh rebuilds them.</summary>
    public static void Restyle(NMerchantCardRemoval slot)
    {
        if (_states.TryGetValue(slot, out State? state))
        {
            state.Fade?.Kill();
            state.Aura?.QueueFree();
            state.Shadow?.QueueFree();
            state.Aura = state.Shadow = null;
            state.Shown = false;
        }

        Refresh(slot);
    }
}
