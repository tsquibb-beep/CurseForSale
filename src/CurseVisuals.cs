using System;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace CurseForSale;

/// <summary>
/// Builds the two sprites that go on the card removal coin, both shader-driven copies of the
/// coin's own texture:
///
/// - the aura, drawn behind the coin: a dark purple glow with rising smoke tongues;
/// - the shadow, drawn over the coin and clipped to it: dark wisps, darkness creeping in from
///   the rim, and a light purple wash.
///
/// Both read a blurred copy of the coin's silhouette that is computed once on the CPU (see
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
render_mode blend_mix;

uniform float fade = 0.0;
uniform float speed = 0.45;
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
uniform vec4 glow_color : source_color = vec4(0.24, 0.05, 0.38, 1.0);
uniform vec4 core_color : source_color = vec4(0.50, 0.18, 0.80, 1.0);
uniform float intensity = 1.0;
// How far smoke tongues lift the glow upwards, as a fraction of the coin's width.
uniform float lift = 0.07;
uniform float pulse = 0.12;
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
uniform vec4 shadow_color : source_color = vec4(0.12, 0.02, 0.20, 1.0);
uniform vec4 tint_color : source_color = vec4(0.42, 0.14, 0.62, 1.0);
uniform float intensity = 1.0;
uniform float wisp_strength = 0.7;
uniform float rim_strength = 0.5;
uniform float tint_strength = 0.1;
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

    private static Shader? _auraShader;

    private static Shader? _shadowShader;

    private static Shader AuraShader => _auraShader ??= new Shader { Code = AuraCode };

    private static Shader ShadowShader => _shadowShader ??= new Shader { Code = ShadowCode };

    // The mask depends only on the texture and the reach, so one is enough for the whole game.
    private static Texture2D? _maskSource;

    private static float _maskReach = -1f;

    private static ImageTexture? _mask;

    /// <summary>Margin around the texture the aura may draw into: reach, lift and wobble.</summary>
    public static float Grow(SaleConfig config) =>
        Mathf.Clamp(1f + 2f * (Reach(config) + Mathf.Max(config.AuraLift, 0f) + 0.03f), 1.2f, 3f);

    private static float Reach(SaleConfig config) => Mathf.Clamp(config.AuraReach, 0.02f, 0.6f);

    /// <summary>Creates the aura and shadow sprites as children of the coin, hidden (fade 0).</summary>
    public static (Sprite2D Aura, Sprite2D Shadow) Create(Sprite2D coin, SaleConfig config)
    {
        Texture2D texture = coin.Texture;
        float grow = Grow(config);
        ImageTexture? mask = Mask(texture, Reach(config), grow);

        var aura = MakeSprite(coin, AuraName, AuraShader);
        aura.ShowBehindParent = true;
        var shadow = MakeSprite(coin, ShadowName, ShadowShader);

        foreach (Sprite2D sprite in new[] { aura, shadow })
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

        var auraMaterial = (ShaderMaterial)aura.Material;
        auraMaterial.SetShaderParameter("centre", coin.Centered ? coin.Offset : coin.Offset + texture.GetSize() / 2f);
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

        // The aura goes first so the game's own drop shadow (also behind the coin) lies over it.
        coin.AddChild(aura);
        coin.MoveChild(aura, 0);
        coin.AddChild(shadow);
        return (aura, shadow);
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
        if (_mask != null && _maskSource == texture && Mathf.IsEqualApprox(_maskReach, reach))
        {
            return _mask;
        }

        try
        {
            _mask = BuildMask(texture, reach, grow);
            _maskSource = texture;
            _maskReach = reach;
            return _mask;
        }
        catch (Exception ex)
        {
            Log.Warn($"[CurseForSale] Could not build the glow mask ({ex.Message}); the aura will not show.");
            return null;
        }
    }

    private const int Downscale = 4;

    /// <summary>
    /// The coin's alpha at quarter resolution, centred in a canvas <paramref name="grow"/> times
    /// the texture's size, blurred by three box passes each way (close to a Gaussian with a
    /// sigma of half the reach). Values: 1 inside, 0.5 on the edge, 0 well outside.
    /// </summary>
    private static ImageTexture BuildMask(Texture2D texture, float reach, float grow)
    {
        Image image = texture.GetImage() ?? throw new InvalidOperationException("texture has no image");
        image = (Image)image.Duplicate();
        if (image.IsCompressed())
        {
            image.Decompress();
        }

        int sw = Math.Max(image.GetWidth() / Downscale, 1);
        int sh = Math.Max(image.GetHeight() / Downscale, 1);
        image.Resize(sw, sh, Image.Interpolation.Bilinear);
        image.Convert(Image.Format.Rgba8);
        byte[] rgba = image.GetData();

        int w = (int)Math.Ceiling(sw * grow);
        int h = (int)Math.Ceiling(sh * grow);
        int ox = (w - sw) / 2;
        int oy = (h - sh) / 2;
        var a = new float[w * h];
        for (int y = 0; y < sh; y++)
        {
            for (int x = 0; x < sw; x++)
            {
                a[(y + oy) * w + x + ox] = rgba[(y * sw + x) * 4 + 3] / 255f;
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

    /// <summary>Fades both sprites to <paramref name="target"/> (0 or 1) through the `fade` uniform.</summary>
    public static Tween? FadeTo(Sprite2D aura, Sprite2D shadow, float target, float seconds)
    {
        var materials = new[] { (ShaderMaterial)aura.Material, (ShaderMaterial)shadow.Material };

        void SetFade(float value)
        {
            foreach (ShaderMaterial material in materials)
            {
                material.SetShaderParameter("fade", value);
            }
        }

        if (seconds <= 0f || !aura.IsInsideTree())
        {
            SetFade(target);
            aura.Visible = shadow.Visible = target > 0f;
            return null;
        }

        // One method tween drives both materials, starting from wherever a cut-short fade left off.
        Variant current = materials[0].GetShaderParameter("fade");
        float from = current.VariantType == Variant.Type.Float ? current.AsSingle() : 0f;
        aura.Visible = shadow.Visible = true;
        Tween tween = aura.CreateTween();
        tween.TweenMethod(Callable.From<float>(SetFade), from, target, seconds)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);

        if (target <= 0f)
        {
            tween.TweenCallback(Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(aura) && GodotObject.IsInstanceValid(shadow))
                {
                    aura.Visible = shadow.Visible = false;
                }
            }));
        }

        return tween;
    }
}
