using UnityEngine;

/// <summary>
/// 말랑이가 부딪히는 벽이 어디인지 정한다.
///
/// A3-1(전제 확인)에서는 카메라에 보이는 영역의 네 변을 벽으로 쓴다.
/// A3-2에서 이걸 모니터 작업 영역(작업표시줄을 뺀 범위)으로 바꾼다.
/// 벽을 "어디서 가져오는가"만 갈아끼우면 되도록 떼어 둔 것 —
/// 충돌 변형 쪽은 평면 네 개를 받을 뿐 출처를 모른다.
///
/// 평면 표현: (normal.xyz, w). 안쪽이 양수다. 즉 dot(p, n) - w >= 0 이면 벽 안.
/// </summary>
public class ScreenWalls : MonoBehaviour
{
    [SerializeField] Camera cam;

    [Tooltip("말랑이가 노는 평면의 z. 카메라에 보이는 영역을 이 깊이에서 잰다.")]
    [SerializeField] float playPlaneZ = 0f;

    [Tooltip("아래쪽 벽을 이만큼 올린다. 작업표시줄 자리를 흉내내는 값 — "
           + "A3-2에서는 실제 작업 영역에서 가져온다.")]
    [SerializeField] float taskbarHeight = 1.2f;

    public float TaskbarHeight => taskbarHeight;

    readonly Vector4[] _planes = new Vector4[4];
    public Vector4[] Planes => _planes;

    void Awake()
    {
        if (cam == null) cam = Camera.main;
        // 첫 프레임에 BlobBody.Update가 LateUpdate보다 먼저 돌아서
        // 0으로 채워진 평면을 읽는다 — 네 벽에 동시에 끼인 것처럼 보였다.
        Recompute();
    }

    void LateUpdate() => Recompute();

    void Recompute()
    {
        if (cam == null) return;

        float dist = Mathf.Abs(playPlaneZ - cam.transform.position.z);
        float halfH = dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfW = halfH * cam.aspect;

        float cx = cam.transform.position.x;
        float cy = cam.transform.position.y;

        // 안쪽이 양수가 되도록 법선을 안쪽으로 향하게 둔다.
        _planes[0] = new Vector4( 1f, 0f, 0f, cx - halfW);                  // 왼쪽
        _planes[1] = new Vector4(-1f, 0f, 0f, -(cx + halfW));               // 오른쪽
        _planes[2] = new Vector4(0f,  1f, 0f, cy - halfH + taskbarHeight);  // 바닥 (작업표시줄)
        _planes[3] = new Vector4(0f, -1f, 0f, -(cy + halfH));               // 천장
    }

    // ── 화면에 벽을 그린다 ────────────────────────────────────────────────
    // 기즈모는 게임 뷰에 안 나오고, 벽이 안 보이면 눌리는 모양을 판단할 수 없다.
    Material _lineMat;

    void OnRenderObject()
    {
        // URP에서는 이 콜백이 돌 때 Camera.current가 우리 카메라가 아닐 수 있어
        // 그걸로 거르지 않는다. 씬 뷰에도 같이 그려지는 편이 오히려 낫다.
        if (cam == null) return;
        if (_lineMat == null)
        {
            var sh = Shader.Find("Hidden/Internal-Colored");
            _lineMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _lineMat.SetInt("_ZWrite", 0);
        }

        float dist = Mathf.Abs(playPlaneZ - cam.transform.position.z);
        float halfH = dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfW = halfH * cam.aspect;
        float cx = cam.transform.position.x, cy = cam.transform.position.y;

        float l = cx - halfW, r = cx + halfW;
        float b = cy - halfH + taskbarHeight, t = cy + halfH;

        _lineMat.SetPass(0);
        GL.PushMatrix();
        GL.Begin(GL.LINES);
        GL.Color(new Color(1f, 1f, 1f, 0.35f));
        Line(l, b, r, b); Line(l, t, r, t);
        Line(l, b, l, t); Line(r, b, r, t);

        // 작업표시줄 자리는 다른 색으로 — 바닥이 화면 끝이 아니라는 걸 보이게.
        GL.Color(new Color(0.4f, 0.8f, 1f, 0.5f));
        float screenBottom = cy - halfH;
        Line(l, screenBottom, r, screenBottom);
        for (float x = l; x < r; x += 0.4f) Line(x, screenBottom, x + 0.2f, b);

        GL.End();
        GL.PopMatrix();
    }

    void Line(float x0, float y0, float x1, float y1)
    {
        GL.Vertex3(x0, y0, playPlaneZ);
        GL.Vertex3(x1, y1, playPlaneZ);
    }

    void OnDestroy()
    {
        if (_lineMat != null) DestroyImmediate(_lineMat);
    }
}
