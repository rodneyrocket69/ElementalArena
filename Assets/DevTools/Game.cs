using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Global access point for the one tuning object. Read numbers through
// Game.Params (or the ArenaConfig / CombatConfig facades). The asset is loaded
// once from any Resources folder and cached. In the editor it is auto-created at
// Assets/Resources/GameParameters.asset the first time it's needed, so the tool
// works with zero manual setup; at runtime a missing asset falls back to
// transient defaults with a warning.
public static class Game
{
    static GameParameters _params;

    public static GameParameters Params
    {
        get
        {
            if (_params != null) return _params;

            _params = Resources.Load<GameParameters>("GameParameters");

#if UNITY_EDITOR
            if (_params == null && !Application.isPlaying)
                _params = CreateAssetInEditor();
#endif
            if (_params == null)
            {
                Debug.LogWarning(
                    "[Game] No GameParameters asset found in a Resources folder — using transient defaults. " +
                    "Create one via  Elemental Arena ▸ Game Parameters (Create or Select)  to tune it in the Inspector.");
                _params = ScriptableObject.CreateInstance<GameParameters>();
                _params.ResetToDefaults();
            }

            _params.EnsureSeeded();
            return _params;
        }
    }

    // Drop the cache (e.g. after swapping the asset). Next access reloads.
    public static void Invalidate() => _params = null;

#if UNITY_EDITOR
    static GameParameters CreateAssetInEditor()
    {
        var so = ScriptableObject.CreateInstance<GameParameters>();
        so.ResetToDefaults();
        System.IO.Directory.CreateDirectory(Application.dataPath + "/Resources");
        AssetDatabase.CreateAsset(so, "Assets/Resources/GameParameters.asset");
        AssetDatabase.SaveAssets();
        Debug.Log("[Game] Created Assets/Resources/GameParameters.asset — select it to tune the game.");
        return so;
    }
#endif
}
