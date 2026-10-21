using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A3-1 확인용 화면 표시 + 조작 키.
///
/// 이 실험이 답해야 할 질문은 하나다 —
/// "벽에 눌린 모양이 셰이더 수식만으로 납득할 만하게 나오는가."
/// 숫자로는 안 나오므로 HUD는 상태만 보여주고, 판단은 만져보는 사람이 한다.
/// 대신 비교를 위해 퍼짐 정도를 런타임에 바꿀 수 있게 해 둔다.
/// </summary>
public class A3Hud : MonoBehaviour
{
    [SerializeField] BlobBody body;
    [SerializeField] BlobInputDriver driver;
    [SerializeField] ShaderDeformBlob blob;

    bool _show = true;

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb[Key.R].wasPressedThisFrame && body != null) body.ResetToCenter();
        if (kb[Key.G].wasPressedThisFrame && body != null)
            body.GravityEnabled = !body.GravityEnabled;
        if (kb[Key.F6].wasPressedThisFrame) _show = !_show;
        if (kb[Key.Escape].wasPressedThisFrame)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    void OnGUI()
    {
        if (!_show) return;

        var box = new Rect(16, 16, 520, 170);
        GUI.color = new Color(0.04f, 0.04f, 0.06f, 0.92f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        var st = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        st.normal.textColor = Color.white;
        var head = new GUIStyle(st) { fontSize = 17, fontStyle = FontStyle.Bold };

        GUILayout.BeginArea(new Rect(box.x + 12, box.y + 10, box.width - 24, box.height - 20));

        GUILayout.Label("A3-1 - Wall contact deformation", head);
        GUILayout.Space(4);
        GUILayout.Label("Drag the blob into a wall or the taskbar line.", st);
        GUILayout.Label("R reset    G gravity    F6 hide HUD    ESC stop", st);
        GUILayout.Space(8);

        if (body != null)
        {
            var s = new GUIStyle(st);
            s.normal.textColor = body.DebugLine.StartsWith("contact none")
                ? new Color(0.7f, 0.7f, 0.75f)
                : new Color(0.5f, 1f, 0.6f);
            GUILayout.Label($"gravity {(body.GravityEnabled ? "on " : "off")}   {body.DebugLine}", s);
        }
        if (driver != null) GUILayout.Label(driver.DebugLine, st);

        GUILayout.EndArea();
    }
}
