using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace CurseForSale;

/// <summary>
/// Settings, read from CurseForSale.config.jsonc. Missing or malformed files fall back to these
/// defaults, so the mod never fails to load because of a bad config. `cfs reload` re-reads it.
///
/// Lengths are fractions of the coin texture's width, so they scale with the shop.
/// </summary>
internal sealed class SaleConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Only show the effect when you have the gold to pay for the removal.</summary>
    [JsonPropertyName("onlyWhenAffordable")]
    public bool OnlyWhenAffordable { get; set; } = true;

    /// <summary>Outer colour of the glow behind the coin.</summary>
    [JsonPropertyName("auraColor")]
    public string AuraColorHex { get; set; } = "#1f0533";

    /// <summary>Colour of the glow right next to the coin.</summary>
    [JsonPropertyName("auraCoreColor")]
    public string AuraCoreColorHex { get; set; } = "#2b0a3d";

    /// <summary>Opacity of the glow (0 hides it).</summary>
    [JsonPropertyName("auraStrength")]
    public float AuraStrength { get; set; } = 0.91f;

    /// <summary>How far the glow reaches beyond the coin.</summary>
    [JsonPropertyName("auraReach")]
    public float AuraReach { get; set; } = 0.46f;

    /// <summary>How far smoke tongues lift the glow upwards.</summary>
    [JsonPropertyName("auraLift")]
    public float AuraLift { get; set; } = 0.07f;

    /// <summary>How much the glow breathes in and out (0 = steady).</summary>
    [JsonPropertyName("auraPulse")]
    public float AuraPulse { get; set; } = 0.07f;

    /// <summary>Colour of the shadowy wisps drifting over the coin.</summary>
    [JsonPropertyName("shadowColor")]
    public string ShadowColorHex { get; set; } = "#1f0533";

    /// <summary>Colour of the light wash over the whole coin.</summary>
    [JsonPropertyName("tintColor")]
    public string TintColorHex { get; set; } = "#6b249e";

    /// <summary>Opacity of the overlay on the coin as a whole (0 hides it).</summary>
    [JsonPropertyName("shadowStrength")]
    public float ShadowStrength { get; set; } = 1f;

    /// <summary>Darkness of the drifting wisps.</summary>
    [JsonPropertyName("wispStrength")]
    public float WispStrength { get; set; } = 0.4f;

    /// <summary>Darkness creeping in from the coin's rim.</summary>
    [JsonPropertyName("rimStrength")]
    public float RimStrength { get; set; } = 0.25f;

    /// <summary>Strength of the even purple wash.</summary>
    [JsonPropertyName("tintStrength")]
    public float TintStrength { get; set; } = 0.18f;

    /// <summary>Light reaching out from behind the coin: "rays", "duotone", "tendrils", "streak" or "none".</summary>
    [JsonPropertyName("flare")]
    public string Flare { get; set; } = "rays";

    /// <summary>The flare's main colour (dusky pink); the only one rays uses.</summary>
    [JsonPropertyName("flareColor")]
    public string FlareColorHex { get; set; } = "#a0526e";

    /// <summary>The flare's second colour (pink), used by duotone, tendrils and streak.</summary>
    [JsonPropertyName("flareColor2")]
    public string FlareColor2Hex { get; set; } = "#e0459a";

    /// <summary>Brightness of the flare (0 hides it).</summary>
    [JsonPropertyName("flareStrength")]
    public float FlareStrength { get; set; } = 0.35f;

    /// <summary>How far the flare reaches past the coin's rim.</summary>
    [JsonPropertyName("flareReach")]
    public float FlareReach { get; set; } = 0.5f;

    /// <summary>How fast the smoke moves. 0 freezes it.</summary>
    [JsonPropertyName("speed")]
    public float Speed { get; set; } = 0.3f;

    [JsonPropertyName("fadeSeconds")]
    public float FadeSeconds { get; set; } = 0.6f;

    public Color AuraColor => ParseColor(AuraColorHex, "#1f0533");

    public Color AuraCoreColor => ParseColor(AuraCoreColorHex, "#2b0a3d");

    public Color ShadowColor => ParseColor(ShadowColorHex, "#1f0533");

    public Color TintColor => ParseColor(TintColorHex, "#6b249e");

    public Color FlareColor => ParseColor(FlareColorHex, "#a0526e");

    public Color FlareColor2 => ParseColor(FlareColor2Hex, "#e0459a");

    private static Color ParseColor(string value, string fallback)
    {
        try
        {
            return new Color(value);
        }
        catch (Exception)
        {
            Log.Warn($"[CurseForSale] '{value}' is not a valid colour, falling back to {fallback}.");
            return new Color(fallback);
        }
    }

    private static SaleConfig? _current;

    public static SaleConfig Current => _current ??= Load();

    public static void Reload() => _current = Load();

    private const string FileName = "CurseForSale.config.jsonc";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Looks for the config next to the game's save data first, then beside the mod DLL.
    ///
    /// The save-data copy wins so that settings survive a Vortex update, which replaces the
    /// whole mod folder. The file is .jsonc rather than .json deliberately: the game scans
    /// mods/ recursively for *.json and tries to parse every one as a mod manifest.
    /// </summary>
    private static SaleConfig Load()
    {
        foreach (string path in CandidatePaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                SaleConfig? loaded = JsonSerializer.Deserialize<SaleConfig>(File.ReadAllText(path), _jsonOptions);
                if (loaded != null)
                {
                    Log.Info($"[CurseForSale] Loaded config from {path}.");
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[CurseForSale] Could not read {path} ({ex.Message}), trying the next location.");
            }
        }

        Log.Info("[CurseForSale] No config file found, using defaults.");
        return new SaleConfig();
    }

    private static IEnumerable<string> CandidatePaths()
    {
        string? userDir = null;
        try
        {
            userDir = OS.GetUserDataDir();
        }
        catch (Exception)
        {
            // Not fatal: fall back to the mod folder.
        }

        if (!string.IsNullOrEmpty(userDir))
        {
            yield return Path.Combine(userDir, FileName);
        }

        string? modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrEmpty(modDir))
        {
            yield return Path.Combine(modDir, FileName);
        }
    }
}
