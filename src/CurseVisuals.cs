using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace CurseForSale;

/// <summary>
/// Builds the sprites that go on the card removal coin, all shader-driven copies of the coin's
/// own texture:
///
/// - the aura, behind the coin: a dark purple glow with rising smoke tongues;
/// - the flare, between the aura and the coin: pink/green light reaching out from behind the
///   coin (optional, in one of <see cref="FlareStyles"/>);
/// - the shadow, drawn over the coin and clipped to it: dark wisps, darkness creeping in from
///   the rim, and a light purple wash.
///
/// The aura and shadow read a blurred copy of the coin's silhouette that is computed once on the CPU (see
/// <see cref="BuildMask"/>), so the glow is smooth at one texture tap per pixel.
///
/// No custom Node subclass (the Godot source generators do not run over a mod assembly), so
/// all motion comes from TIME in the shaders.
/// </summary>
internal static class CurseVisuals
{
    public const string AuraName = "CurseForSaleAura";

    public const string ShadowName = "CurseForSaleShadow";

    // Shared by both shaders. `fade` is ours; the node's inherited modulate arrives in the vertex
    // COLOR, which writing COLOR in fragment() would otherwise discard, so the shop's own fades
    // still apply.
    private const string Common = @"
shader_type canvas_item;

uniform float fade = 0.0;
uniform float speed = 0.3;
// Coin texture height / width, so lengths are round on screen.
uniform float aspect = 1.26;
// How much larger than the texture the mask is (and the aura's quad).
uniform float grow = 1.6;
uniform vec2 centre = vec2(0.0);

varying vec4 inherited;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 5; i++) {
        v += a * noise(p);
        p *= 2.02;
        a *= 0.5;
    }
    return v;
}
";

    private const string AuraCode = Common + @"
render_mode blend_mix;

uniform vec4 glow_color : source_color = vec4(0.12, 0.02, 0.20, 1.0);
uniform vec4 core_color : source_color = vec4(0.17, 0.04, 0.24, 1.0);
uniform float intensity = 0.91;
// How far smoke tongues lift the glow upwards, as a fraction of the coin's width.
uniform float lift = 0.07;
uniform float pulse = 0.07;
uniform sampler2D glow_mask : filter_linear, repeat_disable, hint_default_transparent;

void vertex() {
    inherited = COLOR;
    // Grow the quad so the glow can reach past the texture (the coin touches its sides).
    VERTEX = centre + (VERTEX - centre) * grow;
    UV = (UV - vec2(0.5)) * grow + vec2(0.5);
}

void fragment() {
    float t = TIME * speed;
    // Width units: x runs 0..1 across the texture, y 0..aspect down it.
    vec2 p = UV * vec2(1.0, aspect);

    // Smoke tongues: sample the coin lower down where the noise is high, pushing the glow up.
    float tongue = fbm(vec2(p.x * 5.0, p.y * 2.5 + t * 1.6));
    vec2 wobble = vec2(fbm(p * 3.0 + vec2(t, 0.0)), fbm(p * 3.0 + vec2(3.1, t))) - vec2(0.5);
    vec2 q = p + wobble * 0.05 + vec2(0.0, lift * tongue);

    // The blurred silhouette is 0.5 on the coin's edge and fades to 0 at about `reach` outside it.
    vec2 mask_uv = (q / vec2(1.0, aspect) - vec2(0.5)) / grow + vec2(0.5);
    float glow = clamp(texture(glow_mask, mask_uv).r * 2.4, 0.0, 1.0);

    // Break the glow into drifting wisps rather than a flat ring.
    float smoke = fbm(p * vec2(4.0, 3.0) + vec2(0.0, t * 1.2) + wobble);
    float breath = 1.0 - pulse + pulse * sin(TIME * 1.7);
    float a = pow(glow, 1.2) * (0.5 + 0.75 * smoke) * breath * intensity;

    vec3 col = mix(glow_color.rgb, core_color.rgb, smoothstep(0.55, 1.0, glow));
    COLOR = vec4(col, clamp(a, 0.0, 1.0) * fade) * inherited;
}
";

    private const string ShadowCode = Common + @"
render_mode blend_mix;

uniform vec4 shadow_color : source_color = vec4(0.12, 0.02, 0.20, 1.0);
uniform vec4 tint_color : source_color = vec4(0.42, 0.14, 0.62, 1.0);
uniform float intensity = 1.0;
uniform float wisp_strength = 0.4;
uniform float rim_strength = 0.25;
uniform float tint_strength = 0.18;
uniform sampler2D glow_mask : filter_linear, repeat_disable, hint_default_white;

void vertex() {
    inherited = COLOR;
}

void fragment() {
    float t = TIME * speed;
    float alpha = texture(TEXTURE, UV).a;
    vec2 p = UV * vec2(1.0, aspect);

    // Rim: the blurred silhouette is 0.5 on the edge and reaches 1 well inside the coin.
    float m = texture(glow_mask, (UV - vec2(0.5)) / grow + vec2(0.5)).r;
    float rim = 1.0 - smoothstep(0.5, 0.95, m);

    vec2 warp = vec2(fbm(p * 2.5 + vec2(0.0, t)), fbm(p * 2.5 + vec2(t * 0.6, 1.7)));
    float smoke = fbm(p * 3.2 + warp * 1.5 + vec2(0.0, t * 1.1));
    smoke = smoothstep(0.45, 0.85, smoke);

    float dark = (smoke * wisp_strength + rim * rim_strength) * intensity;
    float tint = tint_strength * intensity;
    float a = clamp(dark + tint, 0.0, 0.92);
    vec3 col = mix(tint_color.rgb, shadow_color.rgb, dark / max(dark + tint, 0.0001));
    COLOR = vec4(col, a * alpha * fade) * inherited;
}
";

    // Light reaching out from behind the coin, over the aura so it starts at the coin's edge and
    // shines out of the dark smoke. Added rather than mixed, like a lens flare. Lengths are in coin widths; `coin_centre`
    // and `coin_radius` come from the coin's own silhouette (it sits low in its texture).
    private const string FlareCode = Common + @"
render_mode blend_add;

uniform vec4 flare_color : source_color = vec4(0.63, 0.32, 0.43, 1.0);
uniform vec4 flare_color2 : source_color = vec4(0.88, 0.27, 0.60, 1.0);
uniform float intensity = 0.35;
// How far past the coin's rim the light reaches.
uniform float reach = 0.5;
// 0 rays, 1 duotone, 2 tendrils, 3 streak.
uniform int style = 0;
uniform vec2 coin_centre = vec2(0.5, 0.73);
uniform float coin_radius = 0.5;

void vertex() {
    inherited = COLOR;
    VERTEX = centre + (VERTEX - centre) * grow;
    UV = (UV - vec2(0.5)) * grow + vec2(0.5);
}

// Three octaves only: broad, soft shapes rather than fine needles.
float soft(vec2 p) {
    return 0.57 * noise(p) + 0.29 * noise(p * 2.03) + 0.14 * noise(p * 4.1);
}

// Soft shafts around the coin. The angle is sampled on a circle so there is no seam, and is
// bent a little with distance so the shafts waver like smoke.
float rays(float ang, float r, float t, float freq, float seed, float spin) {
    float a = ang + spin * t + 0.3 * (noise(vec2(r * 2.5 - t * 0.7, seed)) - 0.5);
    float n = soft(vec2(cos(a), sin(a)) * freq + vec2(seed, t * 0.12));
    return smoothstep(0.38, 0.72, n);
}

void fragment() {
    float t = TIME * speed;
    vec2 p = UV * vec2(1.0, aspect);
    vec2 q = p - coin_centre;
    float r = length(q);
    float ang = atan(q.y, q.x);
    vec2 dir = q / max(r, 0.0001);
    // Distance past the rim; the coin hides everything inside it anyway.
    float d = max(r - coin_radius, 0.0);
    float fall = exp(-3.0 * d / reach);
    vec3 light;

    if (style == 0) {
        // Rays: pink shafts of uneven length that waver and turn slowly.
        float len = reach * (0.5 + 0.8 * soft(dir * 1.5 + vec2(7.0, t * 0.15)));
        light = flare_color.rgb * rays(ang, r, t, 3.0, 0.0, 0.05) * exp(-3.0 * d / len);
    } else if (style == 1) {
        // Duotone: pink rays turning one way, shorter green rays the other.
        float pink = rays(ang, r, t, 3.0, 0.0, 0.05) * fall;
        float green = rays(ang, r, t, 4.0, 11.0, -0.07) * exp(-3.0 * d / (reach * 0.7));
        light = flare_color.rgb * pink + flare_color2.rgb * green * 0.8;
    } else if (style == 2) {
        // Tendrils: wisps stretched along the radius drifting outwards (two crossfaded phases of
        // a flow, so it never jumps), green at the coin turning pink at the tips.
        float s1 = fract(t * 0.2);
        float s2 = fract(t * 0.2 + 0.5);
        vec2 swirl = vec2(-dir.y, dir.x) * 0.15 * sin(r * 5.0 - t);
        float n1 = soft((dir + swirl) * (2.5 + 1.4 * r - 1.2 * s1) + vec2(1.7, 9.2));
        float n2 = soft((dir + swirl) * (2.5 + 1.4 * r - 1.2 * s2) + vec2(5.3, 2.8));
        float n = mix(n1, n2, abs(1.0 - 2.0 * s1));
        float wisp = smoothstep(0.42, 0.68, n) * fall;
        light = mix(flare_color2.rgb, flare_color.rgb, smoothstep(0.0, reach * 0.6, d)) * wisp;
    } else {
        // Streak: a lens flare. A shimmering horizontal streak and six faint spikes in pink, and a
        // broken green ring that drifts round.
        float shimmer = 0.6 + 0.4 * noise(vec2(q.x * 5.0 - t * 2.0, t));
        float streak = exp(-abs(q.y) / 0.03) * exp(-abs(q.x) / (reach * 1.1)) * shimmer * 1.4;
        float spikes = pow(abs(cos((ang + t * 0.05) * 3.0)), 40.0) * exp(-3.0 * d / (reach * 0.8));
        float ring_r = coin_radius + reach * 0.3;
        float ring = exp(-pow((r - ring_r) / 0.035, 2.0))
            * smoothstep(0.35, 0.65, soft(dir * 2.5 + vec2(t * 0.25, 3.0)));
        light = flare_color.rgb * (streak + spikes) + flare_color2.rgb * ring * 0.8;
    }

    // Fade out before the quad's edge so nothing is cut off hard.
    vec2 quad = (UV - vec2(0.5)) / grow + vec2(0.5);
    float edge = smoothstep(0.0, 0.08, min(quad.x, 1.0 - quad.x)) * smoothstep(0.0, 0.08, min(quad.y, 1.0 - quad.y));
    COLOR = vec4(light * intensity * fade * edge, 1.0) * inherited;
}
";

    private static Shader? _auraShader;

    private static Shader? _shadowShader;

    private static Shader AuraShader => _auraShader ??= new Shader { Code = AuraCode };

    private static Shader ShadowShader => _shadowShader ??= new Shader { Code = ShadowCode };

    private static Shader? _flareShader;

    private static Shader FlareShader => _flareShader ??= new Shader { Code = FlareCode };

    public const string FlareName = "CurseForSaleFlare";

    /// <summary>Flare styles, in the order of the shader's `style` uniform.</summary>
    public static readonly string[] FlareStyles = { "rays", "duotone", "tendrils", "streak" };

    public const string NoFlare = "none";

    // The mask depends only on the texture, the reach and the grow, so one is enough for the
    // whole game.
    private static Texture2D? _maskSource;

    private static float _maskReach = -1f;

    private static float _maskGrow = -1f;

    private static ImageTexture? _mask;

    // Where the coin sits in its texture, in coin-width units (y measured down from the top).
    private static Texture2D? _shapeSource;

    private static Vector2 _coinCentre = new(0.5f, 0.73f);

    private static float _coinRadius = 0.5f;

    /// <summary>Margin around the texture the aura may draw into: reach, lift and wobble.</summary>
    public static float Grow(SaleConfig config) =>
        Mathf.Clamp(1f + 2f * (Reach(config) + Mathf.Max(config.AuraLift, 0f) + 0.03f), 1.2f, 3f);

    private static float Reach(SaleConfig config) => Mathf.Clamp(config.AuraReach, 0.02f, 0.6f);

    private static float FlareReach(SaleConfig config) => Mathf.Clamp(config.FlareReach, 0.05f, 1.5f);

    /// <summary>The flare's quad: the coin plus its reach on each side, plus room for the streak.</summary>
    private static float FlareGrow(SaleConfig config) => 1f + 2.4f * FlareReach(config);

    /// <summary>The `style` uniform for the configured flare, or -1 for none.</summary>
    public static int FlareStyle(SaleConfig config) =>
        Array.IndexOf(FlareStyles, (config.Flare ?? NoFlare).Trim().ToLowerInvariant());

    /// <summary>
    /// Creates the effect's sprites as children of the coin, hidden (fade 0). The flare is null
    /// when the config has none.
    /// </summary>
    public static (Sprite2D Aura, Sprite2D Shadow, Sprite2D? Flare) Create(Sprite2D coin, SaleConfig config)
    {
        Texture2D texture = coin.Texture;
        float grow = Grow(config);
        ImageTexture? mask = Mask(texture, Reach(config), grow);
        int style = FlareStyle(config);

        var aura = MakeSprite(coin, AuraName, AuraShader);
        aura.ShowBehindParent = true;
        var shadow = MakeSprite(coin, ShadowName, ShadowShader);
        Sprite2D? flare = style >= 0 ? MakeSprite(coin, FlareName, FlareShader) : null;

        foreach (Sprite2D sprite in flare != null ? new[] { aura, shadow, flare } : new[] { aura, shadow })
        {
            var material = (ShaderMaterial)sprite.Material;
            // Set explicitly: an unset shader parameter reads back as Nil, which a tween cannot
            // start from ("Type mismatch between initial and final value: Nil and float").
            material.SetShaderParameter("fade", 0f);
            material.SetShaderParameter("speed", Mathf.Max(config.Speed, 0f));
            material.SetShaderParameter("aspect", (float)texture.GetHeight() / Mathf.Max(texture.GetWidth(), 1));
            material.SetShaderParameter("grow", grow);
            if (mask != null)
            {
                material.SetShaderParameter("glow_mask", mask);
            }
        }

        Vector2 centre = coin.Centered ? coin.Offset : coin.Offset + texture.GetSize() / 2f;
        var auraMaterial = (ShaderMaterial)aura.Material;
        auraMaterial.SetShaderParameter("centre", centre);
        auraMaterial.SetShaderParameter("glow_color", config.AuraColor);
        auraMaterial.SetShaderParameter("core_color", config.AuraCoreColor);
        auraMaterial.SetShaderParameter("intensity", Mathf.Max(config.AuraStrength, 0f));
        auraMaterial.SetShaderParameter("lift", Mathf.Max(config.AuraLift, 0f));
        auraMaterial.SetShaderParameter("pulse", Mathf.Clamp(config.AuraPulse, 0f, 1f));

        var shadowMaterial = (ShaderMaterial)shadow.Material;
        shadowMaterial.SetShaderParameter("shadow_color", config.ShadowColor);
        shadowMaterial.SetShaderParameter("tint_color", config.TintColor);
        shadowMaterial.SetShaderParameter("intensity", Mathf.Max(config.ShadowStrength, 0f));
        shadowMaterial.SetShaderParameter("wisp_strength", Mathf.Max(config.WispStrength, 0f));
        shadowMaterial.SetShaderParameter("rim_strength", Mathf.Max(config.RimStrength, 0f));
        shadowMaterial.SetShaderParameter("tint_strength", Mathf.Max(config.TintStrength, 0f));

        // The aura goes first so the game's own drop shadow (also behind the coin) lies over it,
        // then the flare, so its light starts at the coin's edge instead of behind the dark smoke.
        coin.AddChild(aura);
        coin.MoveChild(aura, 0);
        coin.AddChild(shadow);

        if (flare != null)
        {
            CoinShape(texture);
            flare.ShowBehindParent = true;
            var flareMaterial = (ShaderMaterial)flare.Material;
            flareMaterial.SetShaderParameter("grow", FlareGrow(config));
            flareMaterial.SetShaderParameter("centre", centre);
            flareMaterial.SetShaderParameter("style", style);
            flareMaterial.SetShaderParameter("flare_color", config.FlareColor);
            flareMaterial.SetShaderParameter("flare_color2", config.FlareColor2);
            flareMaterial.SetShaderParameter("intensity", Mathf.Max(config.FlareStrength, 0f));
            flareMaterial.SetShaderParameter("reach", FlareReach(config));
            flareMaterial.SetShaderParameter("coin_centre", _coinCentre);
            flareMaterial.SetShaderParameter("coin_radius", _coinRadius);
            coin.AddChild(flare);
            coin.MoveChild(flare, 1);
        }

        return (aura, shadow, flare);
    }

    private static Sprite2D MakeSprite(Sprite2D coin, string name, Shader shader) => new()
    {
        Name = name,
        Texture = coin.Texture,
        Centered = coin.Centered,
        Offset = coin.Offset,
        Material = new ShaderMaterial { Shader = shader },
    };

    private static ImageTexture? Mask(Texture2D texture, float reach, float grow)
    {
        if (_mask != null && _maskSource == texture && Mathf.IsEqualApprox(_maskReach, reach) && Mathf.IsEqualApprox(_maskGrow, grow))
        {
            return _mask;
        }

        try
        {
            _mask = BuildMask(texture, reach, grow);
            _maskSource = texture;
            _maskReach = reach;
            _maskGrow = grow;
            return _mask;
        }
        catch (Exception ex)
        {
            Log.Warn($"[CurseForSale] Could not build the glow mask ({ex.Message}); the aura will not show.");
            return null;
        }
    }

    private const int Downscale = 4;

    /// <summary>The coin's alpha at quarter resolution, row by row.</summary>
    private static float[] SmallAlpha(Texture2D texture, out int sw, out int sh)
    {
        Image image = texture.GetImage() ?? throw new InvalidOperationException("texture has no image");
        image = (Image)image.Duplicate();
        if (image.IsCompressed())
        {
            image.Decompress();
        }

        sw = Math.Max(image.GetWidth() / Downscale, 1);
        sh = Math.Max(image.GetHeight() / Downscale, 1);
        image.Resize(sw, sh, Image.Interpolation.Bilinear);
        image.Convert(Image.Format.Rgba8);
        byte[] rgba = image.GetData();
        var alpha = new float[sw * sh];
        for (int i = 0; i < alpha.Length; i++)
        {
            alpha[i] = rgba[i * 4 + 3] / 255f;
        }

        return alpha;
    }

    /// <summary>Measures the coin from its silhouette's bounding box; keeps the defaults on failure.</summary>
    private static void CoinShape(Texture2D texture)
    {
        if (_shapeSource == texture)
        {
            return;
        }

        try
        {
            float[] alpha = SmallAlpha(texture, out int sw, out int sh);
            int minX = sw, maxX = -1, minY = sh, maxY = -1;
            for (int y = 0; y < sh; y++)
            {
                for (int x = 0; x < sw; x++)
                {
                    if (alpha[y * sw + x] > 0.5f)
                    {
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }

            if (maxX >= minX && maxY >= minY)
            {
                _coinCentre = new Vector2((minX + maxX + 1) * 0.5f / sw, (minY + maxY + 1) * 0.5f / sw);
                _coinRadius = (maxX - minX + 1) * 0.5f / sw;
            }

            _shapeSource = texture;
        }
        catch (Exception ex)
        {
            Log.Warn($"[CurseForSale] Could not measure the coin ({ex.Message}); the flare may sit off centre.");
        }
    }

    /// <summary>
    /// The coin's alpha at quarter resolution, centred in a canvas <paramref name="grow"/> times
    /// the texture's size, blurred by three box passes each way (close to a Gaussian with a
    /// sigma of half the reach). Values: 1 inside, 0.5 on the edge, 0 well outside.
    /// </summary>
    private static ImageTexture BuildMask(Texture2D texture, float reach, float grow)
    {
        float[] alpha = SmallAlpha(texture, out int sw, out int sh);
        int w = (int)Math.Ceiling(sw * grow);
        int h = (int)Math.Ceiling(sh * grow);
        int ox = (w - sw) / 2;
        int oy = (h - sh) / 2;
        var a = new float[w * h];
        for (int y = 0; y < sh; y++)
        {
            for (int x = 0; x < sw; x++)
            {
                a[(y + oy) * w + x + ox] = alpha[y * sw + x];
            }
        }

        // Three box passes of radius r give a variance of 3((2r+1)^2 - 1)/12 = r(r+1).
        float sigma = reach * 0.5f * sw;
        int r = Math.Max((int)Math.Round((Math.Sqrt(4 * sigma * sigma + 1) - 1) / 2), 1);
        var scratch = new float[w * h];
        for (int pass = 0; pass < 3; pass++)
        {
            BoxBlur(a, scratch, w, h, r, horizontal: true);
            BoxBlur(scratch, a, w, h, r, horizontal: false);
        }

        var bytes = new byte[w * h];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(Math.Clamp(a[i], 0f, 1f) * 255f + 0.5f);
        }

        return ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, Image.Format.L8, bytes));
    }

    /// <summary>One running-sum box blur along rows or columns, clamping at the edges.</summary>
    private static void BoxBlur(float[] src, float[] dst, int w, int h, int r, bool horizontal)
    {
        int length = horizontal ? w : h;
        int lines = horizontal ? h : w;
        float norm = 1f / (2 * r + 1);
        for (int line = 0; line < lines; line++)
        {
            int Index(int i) => horizontal ? line * w + Math.Clamp(i, 0, length - 1) : Math.Clamp(i, 0, length - 1) * w + line;

            float sum = 0f;
            for (int i = -r; i <= r; i++)
            {
                sum += src[Index(i)];
            }

            for (int i = 0; i < length; i++)
            {
                dst[Index(i)] = sum * norm;
                sum += src[Index(i + r + 1)] - src[Index(i - r)];
            }
        }
    }

    /// <summary>Fades the sprites to <paramref name="target"/> (0 or 1) through their `fade` uniform.</summary>
    public static Tween? FadeTo(IReadOnlyList<Sprite2D> sprites, float target, float seconds)
    {
        var materials = sprites.Select(sprite => (ShaderMaterial)sprite.Material).ToArray();

        void SetFade(float value)
        {
            foreach (ShaderMaterial material in materials)
            {
                material.SetShaderParameter("fade", value);
            }
        }

        void SetVisible(bool visible)
        {
            foreach (Sprite2D sprite in sprites)
            {
                if (GodotObject.IsInstanceValid(sprite))
                {
                    sprite.Visible = visible;
                }
            }
        }

        Sprite2D owner = sprites[0];
        if (seconds <= 0f || !owner.IsInsideTree())
        {
            SetFade(target);
            SetVisible(target > 0f);
            return null;
        }

        // One method tween drives every material, starting from wherever a cut-short fade left off.
        Variant current = materials[0].GetShaderParameter("fade");
        float from = current.VariantType == Variant.Type.Float ? current.AsSingle() : 0f;
        SetVisible(true);
        Tween tween = owner.CreateTween();
        tween.TweenMethod(Callable.From<float>(SetFade), from, target, seconds)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);

        if (target <= 0f)
        {
            tween.TweenCallback(Callable.From(() => SetVisible(false)));
        }

        return tween;
    }
}
