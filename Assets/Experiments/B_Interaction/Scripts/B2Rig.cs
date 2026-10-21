using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// B2 — 늘리기 손맛 수치를 정하는 장치.
///
/// 통과 기준이 "30초 동안 계속 만지고 싶음"이라 숫자로는 끝나지 않는다.
/// 그래서 이 스크립트는 판정을 하지 않는다. 비교가 성립하게만 해준다.
///
/// B1에서 배운 것: 따로 띄워놓고 번갈아 보면 "아까 그게 더 나았나"가 되어
/// 비교가 안 된다. 여기서는 후보를 나란히 놓을 수 없으므로(같은 몸이니까)
/// **잡고 있는 중에 즉시 갈아끼우는** 쪽으로 푼다. 손이 기억을 못 하기 전에
/// 바꿔야 차이가 보인다.
/// </summary>
public class B2Rig : MonoBehaviour
{
    [SerializeField] ShaderDeformBlob blob;
    [SerializeField] BlobInputDriver driver;

    /// <summary>
    /// 손맛 한 벌. 이름은 B5(질감 파라미터)에서 쓸 이름과 맞춰 뒀다 —
    /// B2에서 "어느 쪽이 기본인가"가 정해지면 B5는 그 주변을 넓히는 일이 된다.
    /// </summary>
    [System.Serializable]
    public struct Preset
    {
        public string name;
        public float returnStiffness;   // 되돌아오는 힘
        public float returnDamping;     // 감쇠 — 낮으면 지나쳤다 와서 뽀잉
        public float maxStretch;        // 최대 늘어남 (반지름 배수)
        public float wobbleFromRelease; // 놓을 때 속도가 출렁임으로 얼마나
        public float wobbleDecay;       // 출렁임이 잦아드는 속도
    }

    [SerializeField]
    Preset[] presets = new Preset[]
    {
        // B1에서 쓰던 값. 기준선이다.
        new Preset { name = "Base",   returnStiffness =  90f, returnDamping =  7f,
                     maxStretch = 2.2f, wobbleFromRelease = 0.16f, wobbleDecay = 2.6f },

        // 세게 되돌아오고 오래 떤다. 감쇠비가 낮아 뽀잉이 가장 크다.
        new Preset { name = "Bouncy", returnStiffness = 180f, returnDamping =  6f,
                     maxStretch = 1.7f, wobbleFromRelease = 0.26f, wobbleDecay = 1.8f },

        // 많이 늘어나고 천천히 돌아온다. 거의 지나치지 않는다.
        new Preset { name = "Mochi",  returnStiffness =  40f, returnDamping = 11f,
                     maxStretch = 3.0f, wobbleFromRelease = 0.08f, wobbleDecay = 3.5f },

        // 가운데. 적당히 늘어나고 적당히 떤다.
        new Preset { name = "Jelly",  returnStiffness = 110f, returnDamping =  5f,
                     maxStretch = 2.4f, wobbleFromRelease = 0.22f, wobbleDecay = 2.2f },

        // 아주 많이 늘어나고 출렁임이 거의 없다.
        new Preset { name = "Balloon",returnStiffness =  55f, returnDamping = 14f,
                     maxStretch = 3.4f, wobbleFromRelease = 0.05f, wobbleDecay = 4.0f },
    };

    [Tooltip("손을 뗀 뒤 이 시간이 지나면 '계속 만지는 중'이 끊긴 것으로 본다.")]
    [SerializeField] float idleBreak = 2f;

    int _current;
    bool _show = true;

    // 통과 기준을 화면에 띄운다. "30초 동안 계속 만지고 싶음"이 기준인데
    // 만지는 사람은 시간 가는 걸 모른다 — 세어주면 기준이 눈에 보인다.
    float _streakStart;
    float _lastTouch = -999f;
    float _bestStreak;
    string _bestStreakPreset = "-";

    // 고른 것을 화면에도 남긴다. 빌드로 남에게 보내면 콘솔을 볼 수 없고,
    // 플레이어 로그가 어디 있는지 찾아달라고 할 수도 없다.
    readonly System.Collections.Generic.List<string> _picks = new();

    void Start() => Apply(0);

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        for (int i = 0; i < presets.Length && i < 9; i++)
            if (kb[Key.Digit1 + i].wasPressedThisFrame) Apply(i);

        if (kb[Key.Tab].wasPressedThisFrame) Apply((_current + 1) % presets.Length);
        if (kb[Key.Enter].wasPressedThisFrame) LogCurrent();
        if (kb[Key.F6].wasPressedThisFrame) _show = !_show;
        if (kb[Key.Escape].wasPressedThisFrame)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        TrackStreak();
    }

    void TrackStreak()
    {
        bool touching = blob != null && blob.IsGrabbed;
        if (touching)
        {
            // 끊긴 지 오래됐으면 새로 시작, 아니면 이어붙인다.
            if (Time.time - _lastTouch > idleBreak) _streakStart = Time.time;
            _lastTouch = Time.time;
        }

        float streak = CurrentStreak;
        if (streak > _bestStreak)
        {
            _bestStreak = streak;
            _bestStreakPreset = presets[_current].name;
        }
    }

    float CurrentStreak =>
        Time.time - _lastTouch > idleBreak ? 0f : Time.time - _streakStart;

    void Apply(int index)
    {
        if (blob == null || presets == null || presets.Length == 0) return;
        _current = Mathf.Clamp(index, 0, presets.Length - 1);
        var p = presets[_current];

        // 잡고 있는 중에 바꿔도 변형이 튀지 않는다. 탄성·감쇠·출렁임은 "돌아오는
        // 방식"이라 지금 당겨진 양을 건드리지 않고, 늘어남 한계만 당겨진 양을
        // 직접 자르는데 그쪽은 ShaderDeformBlob이 부드럽게 옮긴다.
        // (처음엔 안 튄다고 적어놨다가 재보니 2.279 -> 0.700으로 잘려서 고쳤다.)
        blob.ReturnStiffness = p.returnStiffness;
        blob.ReturnDamping = p.returnDamping;
        blob.MaxStretch = p.maxStretch;
        blob.WobbleFromRelease = p.wobbleFromRelease;
        blob.WobbleDecay = p.wobbleDecay;
    }

    /// <summary>
    /// 마음에 든 설정을 남긴다. 안 그러면 "세 번째 게 좋았는데"로 끝나고
    /// 그 숫자가 뭐였는지 되살리지 못한다.
    /// </summary>
    void LogCurrent()
    {
        var p = presets[_current];
        _picks.Add($"{_current + 1} {p.name}  (after {CurrentStreak:F0}s)");
        if (_picks.Count > 3) _picks.RemoveAt(0);
        var sb = new StringBuilder();
        sb.AppendLine($"[B2 기록] {p.name}");
        sb.AppendLine($"  returnStiffness   {p.returnStiffness}");
        sb.AppendLine($"  returnDamping     {p.returnDamping}   (감쇠비 {blob.DampingRatio:F2})");
        sb.AppendLine($"  maxStretch        {p.maxStretch}");
        sb.AppendLine($"  wobbleFromRelease {p.wobbleFromRelease}");
        sb.AppendLine($"  wobbleDecay       {p.wobbleDecay}");
        sb.AppendLine($"  이 프리셋 최장 연속 {_bestStreak:F0}초");
        Debug.Log(sb.ToString());
    }

    void OnGUI()
    {
        if (!_show || blob == null) return;

        var box = new Rect(16, 16, 560, 282);
        GUI.color = new Color(0.04f, 0.04f, 0.06f, 0.92f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        var st = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        st.normal.textColor = Color.white;
        var head = new GUIStyle(st) { fontSize = 17, fontStyle = FontStyle.Bold };
        var big = new GUIStyle(st) { fontSize = 20, fontStyle = FontStyle.Bold };

        GUILayout.BeginArea(new Rect(box.x + 12, box.y + 10, box.width - 24, box.height - 20));

        GUILayout.Label("B2 - Stretch feel", head);
        GUILayout.Space(2);
        GUILayout.Label("Drag = stretch   Flick = throw", st);

        var keys = new StringBuilder();
        for (int i = 0; i < presets.Length; i++) keys.Append($"{i + 1} {presets[i].name}   ");
        GUILayout.Label(keys.ToString(), st);
        GUILayout.Label("Tab next   Enter log it   F6 hide   ESC stop", st);
        GUILayout.Space(6);

        var p = presets[_current];
        big.normal.textColor = new Color(0.5f, 1f, 0.6f);
        GUILayout.Label($"{_current + 1}. {p.name}", big);
        GUILayout.Label($"stiffness {p.returnStiffness,-6} damping {p.returnDamping,-5} "
                      + $"ratio {blob.DampingRatio:F2}", st);
        GUILayout.Label($"maxStretch {p.maxStretch,-6} wobble {p.wobbleFromRelease} "
                      + $"/ decay {p.wobbleDecay}", st);
        GUILayout.Space(6);

        // 통과 기준을 눈에 보이게. 30초를 넘기면 색이 바뀐다.
        float streak = CurrentStreak;
        var s = new GUIStyle(st) { fontSize = 16, fontStyle = FontStyle.Bold };
        s.normal.textColor = streak >= 30f
            ? new Color(0.5f, 1f, 0.6f)
            : new Color(0.85f, 0.85f, 0.9f);
        GUILayout.Label($"playing for {streak:F0}s   (goal 30s)   best {_bestStreak:F0}s "
                      + $"on {_bestStreakPreset}", s);

        if (_picks.Count > 0)
        {
            GUILayout.Space(4);
            var pick = new GUIStyle(st);
            pick.normal.textColor = new Color(1f, 0.85f, 0.5f);
            GUILayout.Label("picked:  " + string.Join("   |   ", _picks), pick);
        }

        GUILayout.EndArea();
    }
}
