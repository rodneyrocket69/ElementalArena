using UnityEditor;
using UnityEngine;

// Adds a dev toolbar to the top of the GameParameters Inspector:
// Reset to Defaults · Save/Load JSON presets · Rebuild Board (apply piece stats).
[CustomEditor(typeof(GameParameters))]
public class GameParametersEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var gp = (GameParameters)target;

        EditorGUILayout.HelpBox(
            "The single tuning object. Combat / Arena / AI values are LIVE. " +
            "Piece stats apply on the next board build — press Rebuild Board (Play mode) or start a new game.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Reset to Defaults"))
            {
                if (EditorUtility.DisplayDialog("Reset Game Parameters",
                        "Reset ALL parameters to their defaults?", "Reset", "Cancel"))
                {
                    Undo.RecordObject(gp, "Reset Game Parameters");
                    gp.ResetToDefaults();
                    EditorUtility.SetDirty(gp);
                }
            }
            if (GUILayout.Button("Save Preset…")) SavePreset(gp);
            if (GUILayout.Button("Load Preset…")) LoadPreset(gp);
        }

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Rebuild Board (apply piece stats now)"))
                RebuildBoard();
        }

        EditorGUILayout.Space();
        DrawDefaultInspector();
    }

    void SavePreset(GameParameters gp)
    {
        string path = EditorUtility.SaveFilePanel("Save tuning preset", Application.dataPath, "tuning", "json");
        if (string.IsNullOrEmpty(path)) return;
        System.IO.File.WriteAllText(path, JsonUtility.ToJson(gp, true));
        Debug.Log($"[GameParameters] Saved preset → {path}");
    }

    void LoadPreset(GameParameters gp)
    {
        string path = EditorUtility.OpenFilePanel("Load tuning preset", Application.dataPath, "json");
        if (string.IsNullOrEmpty(path)) return;
        Undo.RecordObject(gp, "Load Game Parameters Preset");
        JsonUtility.FromJsonOverwrite(System.IO.File.ReadAllText(path), gp);
        gp.InvalidateLookup();
        EditorUtility.SetDirty(gp);
        Debug.Log($"[GameParameters] Loaded preset ← {path}");
    }

    void RebuildBoard()
    {
        var bm = Object.FindFirstObjectByType<BoardManager>();
        if (bm == null) { Debug.LogWarning("[GameParameters] No BoardManager in the scene."); return; }
        bm.InitBoard();
        var bv = Object.FindFirstObjectByType<BoardVisual>();
        if (bv != null) bv.RefreshAll();
        Debug.Log("[GameParameters] Board rebuilt with current piece stats.");
    }

    // Create the asset in a Resources folder (or select it if it already exists).
    [MenuItem("Elemental Arena/Game Parameters (Create or Select)")]
    static void CreateOrSelect()
    {
        var gp = LoadOrCreate();
        Selection.activeObject = gp;
        EditorGUIUtility.PingObject(gp);
    }

    // Self-heal: on every editor load / recompile, ensure the asset exists so it's
    // always present in the Inspector without needing Play mode or a menu click.
    [InitializeOnLoadMethod]
    static void EnsureAssetExists()
    {
        // Defer to next editor tick — the AssetDatabase may still be importing on load.
        EditorApplication.delayCall += () => LoadOrCreate();
    }

    static GameParameters LoadOrCreate()
    {
        const string path = "Assets/Resources/GameParameters.asset";
        var gp = AssetDatabase.LoadAssetAtPath<GameParameters>(path);
        if (gp != null) return gp;

        gp = CreateInstance<GameParameters>();
        gp.ResetToDefaults();
        System.IO.Directory.CreateDirectory(Application.dataPath + "/Resources");
        AssetDatabase.CreateAsset(gp, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[GameParameters] Created " + path);
        return gp;
    }
}
