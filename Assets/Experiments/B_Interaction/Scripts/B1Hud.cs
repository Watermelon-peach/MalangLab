using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// B1 비교용 화면 표시.
///
/// B1은 "손맛"이 판정 기준이라 숫자로 결론이 나지 않는다. 그래서 HUD는
/// 판정을 대신하지 않고, 지금 뭘 만지고 있고 파라미터가 얼마인지만 보여준다.
/// 판단은 만져보는 사람이 한다.
/// </summary>
public class B1Hud : MonoBehaviour
{
    [SerializeField] BlobInputDriver driver;
    [SerializeField] MonoBehaviour[] blobs;   // IBlob 구현들 (인스펙터에서 끌어다 넣는다)

    bool _show = true;

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
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

        var box = new Rect(16, 16, 520, 190);
        GUI.color = new Color(0.04f, 0.04f, 0.06f, 0.92f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        var st = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        st.normal.textColor = Color.white;
        var head = new GUIStyle(st) { fontSize = 17, fontStyle = FontStyle.Bold };

        GUILayout.BeginArea(new Rect(box.x + 12, box.y + 10, box.width - 24, box.height - 20));

        GUILayout.Label("B1 - Deformation comparison", head);
        GUILayout.Space(4);
        GUILayout.Label("Click = poke    Drag = stretch    Flick = throw", st);
        GUILayout.Label("F6 hide HUD     ESC stop", st);
        GUILayout.Space(8);

        GUILayout.Label(driver != null ? driver.DebugLine : "no driver", st);
        GUILayout.Space(4);

        if (blobs != null)
        {
            foreach (var mb in blobs)
            {
                if (mb is not IBlob b) continue;
                var s = new GUIStyle(st);
                s.normal.textColor = b.IsGrabbed
                    ? new Color(0.5f, 1f, 0.6f)
                    : new Color(0.7f, 0.7f, 0.75f);
                GUILayout.Label($"{b.DisplayName,-18} {b.DebugLine}", s);
            }
        }

        GUILayout.EndArea();
    }
}
