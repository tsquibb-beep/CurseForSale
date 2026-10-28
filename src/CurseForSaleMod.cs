using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace CurseForSale;

/// <summary>
/// Entry point. The loader finds this via <see cref="ModInitializerAttribute"/> and calls
/// <see cref="Init"/> once at startup.
/// </summary>
[ModInitializer(nameof(Init))]
internal static class CurseForSaleMod
{
    private const string HarmonyId = "tomasapan.CurseForSale";

    private static void Init()
    {
        try
        {
            _ = SaleConfig.Current;
            new Harmony(HarmonyId).PatchAll(Assembly.GetExecutingAssembly());
            Log.Info("[CurseForSale] Initialised.");
        }
        catch (Exception ex)
        {
            Log.Error($"[CurseForSale] Failed to initialise: {ex}");
        }
    }
}
