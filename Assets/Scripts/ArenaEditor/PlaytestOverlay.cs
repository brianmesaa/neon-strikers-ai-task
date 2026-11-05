using NeonStrikers.Arenas;
using UnityEngine;

/// <summary>
/// Small bar shown while playtesting a custom arena: back to the editor (also Esc) or restart the match.
/// Added at runtime by <see cref="CustomArenaBuilder"/> only in playtest mode.
/// </summary>
public class PlaytestOverlay : MonoBehaviour
{
    public string arenaName = "";

    GUIStyle boxStyle;
    GUIStyle labelStyle;
    GUIStyle buttonStyle;
    Texture2D boxTexture;
    Texture2D buttonTexture;
    Texture2D buttonHoverTexture;

    void OnGUI()
    {
        if (!CustomArenaSession.IsPlaytesting)
            return;

        EnsureStyles();

        float scale = Mathf.Clamp(Screen.height / 820f, 0.7f, 3f);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
        float width = Screen.width / scale;
        float height = Screen.height / scale;

        var area = new Rect(width * 0.5f - 330f, height - 58f, 660f, 48f);
        GUI.Box(area, GUIContent.none, boxStyle);
        GUILayout.BeginArea(new Rect(area.x + 10f, area.y + 8f, area.width - 20f, area.height - 16f));
        GUILayout.BeginHorizontal();
        GUILayout.Label("PLAYTEST: " + arenaName + "   Blue: WASD + Space   Red: Arrows + Enter", labelStyle, GUILayout.ExpandWidth(true));
        bool back = GUILayout.Button("Back to Editor (Esc)", buttonStyle, GUILayout.Width(160f));
        bool restart = GUILayout.Button("Restart", buttonStyle, GUILayout.Width(80f));
        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        GUI.matrix = Matrix4x4.identity;

        if (back)
            ArenaEditorNavigation.ReturnToEditor("Back from playtest. Your arena is exactly as you left it.");
        else if (restart)
            ArenaEditorNavigation.RestartPlaytest();
    }

    void EnsureStyles()
    {
        if (boxStyle != null)
            return;

        boxTexture = Solid(new Color(0.03f, 0.04f, 0.1f, 0.85f));
        buttonTexture = Solid(new Color(0.15f, 0.2f, 0.42f, 1f));
        buttonHoverTexture = Solid(new Color(0.25f, 0.33f, 0.62f, 1f));

        boxStyle = new GUIStyle();
        boxStyle.normal.background = boxTexture;

        labelStyle = new GUIStyle { fontSize = 13, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        labelStyle.normal.textColor = new Color(0.85f, 0.95f, 1f);

        buttonStyle = new GUIStyle { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        buttonStyle.normal.background = buttonTexture;
        buttonStyle.hover.background = buttonHoverTexture;
        buttonStyle.active.background = buttonHoverTexture;
        buttonStyle.normal.textColor = Color.white;
        buttonStyle.hover.textColor = Color.white;
        buttonStyle.active.textColor = Color.white;
        buttonStyle.margin = new RectOffset(4, 4, 0, 0);
        buttonStyle.fixedHeight = 32f;
    }

    static Texture2D Solid(Color color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    void OnDestroy()
    {
        if (boxTexture != null) Destroy(boxTexture);
        if (buttonTexture != null) Destroy(buttonTexture);
        if (buttonHoverTexture != null) Destroy(buttonHoverTexture);
    }
}
