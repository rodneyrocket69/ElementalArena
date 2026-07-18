using UnityEngine;

// Central place for the game's Flat Kit look. Every runtime-created primitive
// (tiles, pieces, ball, VFX) gets its material from here so nothing falls back
// to the default URP Lit material. Base materials live in Assets/Game/Resources
// so the shader is always included in builds.
public static class FlatKitMaterials
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorDimId  = Shader.PropertyToID("_ColorDim");

    // How much darker the cel-shaded side is, relative to the tint
    const float ShadeMul = 0.72f;

    static Material _base;
    static Material _outlined;

    static Material BaseMat     => _base     != null ? _base     : (_base     = Load("FlatKitBase"));
    static Material OutlinedMat => _outlined != null ? _outlined : (_outlined = Load("FlatKitOutlined"));

    static Material Load(string name)
    {
        var mat = Resources.Load<Material>(name);
        if (mat == null) Debug.LogWarning($"FlatKitMaterials: missing Resources material '{name}'");
        return mat;
    }

    // Puts a Flat Kit material on the renderer (if it isn't carrying one yet)
    // and tints it: _BaseColor is the lit side, _ColorDim the shaded side.
    public static void Tint(Renderer rend, Color col, bool outlined = false)
    {
        if (rend == null) return;

        var source = outlined ? OutlinedMat : BaseMat;
        if (source != null && (rend.sharedMaterial == null || rend.sharedMaterial.shader != source.shader ||
                               IsOutlined(rend.sharedMaterial) != outlined))
            rend.material = new Material(source);

        var m = rend.material; // per-renderer instance, same as the old .material.color usage
        if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, col);
        else m.color = col; // shader missing — at least keep the tint
        if (m.HasProperty(ColorDimId))
            m.SetColor(ColorDimId, new Color(col.r * ShadeMul, col.g * ShadeMul, col.b * ShadeMul, 1f));
    }

    public static void Tint(GameObject go, Color col, bool outlined = false) =>
        Tint(go.GetComponent<Renderer>(), col, outlined);

    static bool IsOutlined(Material m) => m.IsKeywordEnabled("DR_OUTLINE_ON");

    static Material _transparent;

    // Alpha-blended unlit material for ghosted effects (shield bubbles).
    // Alpha comes from the color you pass in.
    public static void TintTransparent(GameObject go, Color col)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;

        if (_transparent == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default"); // non-URP fallback
            _transparent = new Material(sh);
            if (_transparent.HasProperty("_Surface"))
            {
                _transparent.SetFloat("_Surface", 1f); // URP: transparent surface
                _transparent.SetFloat("_Blend",   0f); // alpha blend
                _transparent.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _transparent.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _transparent.SetInt("_ZWrite", 0);
                _transparent.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                _transparent.SetOverrideTag("RenderType", "Transparent");
            }
            _transparent.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        if (rend.sharedMaterial == null || rend.sharedMaterial.shader != _transparent.shader)
        {
            rend.material = new Material(_transparent);
            rend.material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        var m = rend.material;
        if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, col);
        else m.color = col;
    }
}
