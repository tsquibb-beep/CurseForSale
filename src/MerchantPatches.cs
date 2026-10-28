using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace CurseForSale;

/// <summary>
/// The removal slot's UpdateVisual runs when the slot is filled, on every gold change
/// (NMerchantSlot subscribes it to Player.GoldChanged) and after a removal, which covers
/// everything except a curse arriving without gold changing; the deck subscription made in
/// FillSlot covers that.
/// </summary>
[HarmonyPatch(typeof(NMerchantCardRemoval))]
internal static class MerchantPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(NMerchantCardRemoval.FillSlot))]
    private static void AfterFillSlot(NMerchantCardRemoval __instance)
    {
        CurseAura.Attach(__instance);
        CurseAura.Refresh(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch("UpdateVisual")]
    private static void AfterUpdateVisual(NMerchantCardRemoval __instance) => CurseAura.Refresh(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(nameof(NMerchantCardRemoval._ExitTree))]
    private static void AfterExitTree(NMerchantCardRemoval __instance) => CurseAura.Detach(__instance);
}
