using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// B1 후보 ① — 3D 메시 스프링 소프트바디.
///
/// 정점마다 속도를 들고, 두 종류의 스프링으로 되돌린다.
///   1) 형태 기억: 각 정점을 자기 원래 자리로 당긴다 (전체 모양 유지)
///   2) 모서리:   이웃 정점과의 거리를 유지한다 (국소 뻣뻣함, 찌그러짐 전파)
///
/// 둘의 비율이 질감을 만든다. 형태 기억이 세면 탱탱볼, 모서리가 세고
/// 형태 기억이 약하면 찹쌀떡처럼 늘어난 채 천천히 돌아온다.
/// B5(질감 파라미터)에서 쓸 손잡이가 여기 다 있다.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class MeshSpringBlob : MonoBehaviour, IBlob
{
    [Header("메시")]
    [Tooltip("0=12정점, 1=42, 2=162, 3=642. 실험은 2면 충분하다.")]
    [SerializeField, Range(0, 4)] int subdivisions = 2;
    [SerializeField] float radius = 1f;

    [Header("탄성 — 이 둘의 비율이 질감을 만든다")]
    [Tooltip("원래 자리로 당기는 힘. 높을수록 탱탱하고 빨리 돌아온다. "
           + "이게 모서리보다 세면 이웃이 제자리로 돌아가버려 잡은 데만 뾰족하게 뽑힌다.")]
    [SerializeField] float shapeStiffness = 70f;

    [Tooltip("이웃과의 거리를 유지하는 힘. 높을수록 변형이 넓게 번진다. "
           + "뾰족함을 없애는 가장 큰 손잡이 — 형태 기억보다 세게 두는 것이 기본.")]
    [SerializeField] float edgeStiffness = 150f;

    [Tooltip("감쇠. 낮으면 오래 출렁이고, 높으면 묵직하게 멎는다.")]
    [SerializeField] float damping = 4f;

    [Tooltip("놓은 뒤 원래 모양으로 수렴하는 속도. 0이면 꺼진다. "
           + "모서리 스프링은 거리만 맞추므로 찌그러진 채 균형이 잡히는 자세가 "
           + "생길 수 있다(버클링). 형태 기억만으로는 못 빠져나와서 이게 필요하다.")]
    [SerializeField, Range(0f, 8f)] float restoreRate = 2f;

    [Tooltip("원래 반지름 대비 최대 늘어남. 무한정 늘어나면 손맛이 아니라 고무줄이 된다.")]
    [SerializeField, Range(1.1f, 4f)] float maxStretch = 2.2f;

    [Header("변형 공간")]
    [Tooltip("1이면 변형이 화면 평면 안에서만 일어난다. 0이면 3D 공간 전체. "
           + "카메라가 고정이라 플레이어는 사실상 평면을 본다. 깊이 방향으로 변형하면 "
           + "3D 물체를 3D에서 잡아당기는 느낌이 들어 손맛과 어긋난다.")]
    [SerializeField, Range(0f, 1f)] float planarDeform = 1f;

    [Tooltip("앞면만 잡히는 정도. 정점이 카메라를 향한 각도가 이 값을 넘으면 "
           + "온전히 잡힌다. 작을수록 실루엣 가까이까지 잡히고, 크면 정면만 잡힌다.")]
    [SerializeField, Range(0.05f, 1f)] float frontFalloff = 0.3f;

    [Header("잡기")]
    [Tooltip("잡는 점 주변 이 반경 안의 정점이 끌려온다. 좁으면 점으로 꼬집힌다.")]
    [SerializeField, Range(0.1f, 3f)] float grabRadius = 1.1f;

    [Tooltip("잡은 정점이 커서를 따라가는 세기. 너무 세면 스프링이 중재할 틈 없이 "
           + "커서에 못 박혀서 이웃과 끊긴 모양이 된다.")]
    [SerializeField, Range(0.05f, 2f)] float grabStrength = 0.45f;

    [Tooltip("몸 전체가 커서 쪽으로 쏠리는 비율. 0이면 잡은 데만 뽑혀 나오고, "
           + "올리면 몸통째 늘어나는 느낌이 된다. 뾰족함을 없애는 두 번째 손잡이.")]
    [SerializeField, Range(0f, 1f)] float bodyFollow = 0.35f;

    [Header("찌르기")]
    [SerializeField] float pokeImpulse = 6f;
    [SerializeField, Range(0.1f, 2f)] float pokeRadius = 0.45f;

    // ── 내부 상태 ─────────────────────────────────────────────────────────
    Mesh _mesh;
    Vector3[] _rest;        // 원래 자리 (로컬)
    Vector3[] _pos;         // 현재 자리 (로컬)
    Vector3[] _vel;
    Vector3[] _accum;       // 프레임당 힘 누적
    List<(int a, int b)> _edges;
    float[] _restLen;

    // 잡기 상태
    bool _grabbed;
    Vector3 _grabTargetLocal;
    Vector3 _grabAnchorLocal;   // 처음 잡은 자리. 여기서 얼마나 끌려났는지로 몸 쏠림을 정한다.
    int[] _grabbedVerts;
    float[] _grabWeight;

    float _maxDisplacement;  // HUD용
    Camera _cam;

    public string DisplayName => "3D Mesh Spring";
    public bool IsGrabbed => _grabbed;
    public string DebugLine =>
        $"verts {_pos?.Length ?? 0}  edges {_edges?.Count ?? 0}  maxDisp {_maxDisplacement:F2}";

    void Awake()
    {
        _cam = Camera.main;
        Build();
    }

    /// <summary>카메라가 보는 방향을 이 오브젝트의 로컬 공간으로 가져온다.</summary>
    Vector3 LocalViewAxis()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return Vector3.forward;
        return transform.InverseTransformDirection(_cam.transform.forward).normalized;
    }

    void Build()
    {
        _mesh = IcoSphere.Create(subdivisions, radius);
        _mesh.MarkDynamic();
        GetComponent<MeshFilter>().sharedMesh = _mesh;

        _rest = _mesh.vertices;
        _pos = (Vector3[])_rest.Clone();
        _vel = new Vector3[_rest.Length];
        _accum = new Vector3[_rest.Length];

        _edges = IcoSphere.BuildEdges(_mesh.triangles);
        _restLen = new float[_edges.Count];
        for (int i = 0; i < _edges.Count; i++)
            _restLen[i] = Vector3.Distance(_rest[_edges[i].a], _rest[_edges[i].b]);

        // 잡을 때 쓸 콜라이더. 변형하면 실제 모양과 어긋나지만
        // 레이캐스트로 "대충 어디를 잡았나"를 알기엔 충분하다.
        var col = GetComponent<SphereCollider>();
        if (col == null) col = gameObject.AddComponent<SphereCollider>();
        col.radius = radius;
    }

    void Update()
    {
        // 고정 스텝으로 여러 번 돌린다. 프레임레이트가 흔들려도
        // 손맛이 달라지지 않게 하려는 것 — 튜닝값이 기기마다 달라지면 비교가 안 된다.
        const float step = 1f / 120f;
        int steps = Mathf.Clamp(Mathf.CeilToInt(Time.deltaTime / step), 1, 8);
        float dt = Mathf.Min(Time.deltaTime, step * steps) / steps;

        for (int s = 0; s < steps; s++) Simulate(dt);

        _mesh.vertices = _pos;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
    }

    void Simulate(float dt)
    {
        System.Array.Clear(_accum, 0, _accum.Length);

        // 1) 형태 기억 — 원래 자리로
        for (int i = 0; i < _pos.Length; i++)
            _accum[i] += (_rest[i] - _pos[i]) * shapeStiffness;

        // 2) 모서리 — 이웃과의 거리 유지
        for (int e = 0; e < _edges.Count; e++)
        {
            var (a, b) = _edges[e];
            var delta = _pos[b] - _pos[a];
            float len = delta.magnitude;
            if (len < 1e-6f) continue;

            float diff = len - _restLen[e];
            var force = (delta / len) * (diff * edgeStiffness);
            _accum[a] += force;
            _accum[b] -= force;
        }

        // 3) 잡기
        if (_grabbed && _grabbedVerts != null)
        {
            // 3a) 잡은 정점은 커서 쪽으로. 못 박지 않고 스프링으로 끈다 —
            //     그래야 모서리 스프링이 이웃을 끌고 올 틈이 생긴다.
            for (int k = 0; k < _grabbedVerts.Length; k++)
            {
                int i = _grabbedVerts[k];
                var pull = (_grabTargetLocal - _pos[i]) * (grabStrength * 400f * _grabWeight[k]);
                _accum[i] += pull;
            }

            // 3b) 몸 전체도 그쪽으로 쏠린다. 이게 없으면 잡은 데만 스파이크처럼
            //     뽑혀 나온다. 실제로 말랑한 걸 늘리면 몸통도 따라 쏠린다.
            if (bodyFollow > 0f)
            {
                var offset = _grabTargetLocal - _grabAnchorLocal;
                float perVertex = shapeStiffness * bodyFollow;
                for (int i = 0; i < _pos.Length; i++)
                    _accum[i] += (_rest[i] + offset - _pos[i]) * perVertex;
            }
        }

        // 4) 적분 + 감쇠 + 늘어남 제한
        float maxR = radius * maxStretch;
        _maxDisplacement = 0f;

        var viewAxis = LocalViewAxis();

        for (int i = 0; i < _pos.Length; i++)
        {
            _vel[i] += _accum[i] * dt;
            _vel[i] *= Mathf.Exp(-damping * dt);   // 지수 감쇠 — dt가 변해도 결과가 같다

            // 깊이 방향 속도를 깎는다. 각 정점은 자기 원래 깊이에 머문 채
            // 화면 평면 안에서만 움직인다 → 음영은 3D인데 변형은 평면으로 읽힌다.
            if (planarDeform > 0f)
                _vel[i] -= viewAxis * (Vector3.Dot(_vel[i], viewAxis) * planarDeform);

            _pos[i] += _vel[i] * dt;

            // 중심에서 너무 멀어지면 잘라낸다. 고무줄처럼 늘어나는 걸 막는다.
            float d = _pos[i].magnitude;
            if (d > maxR)
            {
                _pos[i] = _pos[i] * (maxR / d);
                _vel[i] *= 0.5f;
            }

            // 깊이를 원래 값으로 되돌린다. 위 클램프가 위치를 반지름 방향으로
            // 당기면서 깊이 성분까지 건드리는데, 깊이 속도를 깎아놨으니
            // 스스로 복원하지 못하고 찌그러진 채 쌓인다.
            if (planarDeform > 0f)
            {
                float drift = Vector3.Dot(_pos[i] - _rest[i], viewAxis);
                _pos[i] -= viewAxis * (drift * planarDeform);
            }

            // 잡고 있지 않으면 원래 모양으로 천천히 수렴시킨다.
            // 모서리 스프링만으로는 버클링에서 빠져나오지 못한다.
            if (!_grabbed && restoreRate > 0f)
                _pos[i] = Vector3.Lerp(_pos[i], _rest[i], Mathf.Min(1f, restoreRate * dt));

            float disp = Vector3.Distance(_pos[i], _rest[i]);
            if (disp > _maxDisplacement) _maxDisplacement = disp;
        }
    }

    // ── IBlob ─────────────────────────────────────────────────────────────
    public void Grab(Vector3 worldPoint)
    {
        var local = transform.InverseTransformPoint(worldPoint);
        _grabTargetLocal = local;
        _grabAnchorLocal = local;

        // 잡는 점 주변 정점을 거리에 따라 가중치를 주고 모은다.
        var idx = new List<int>();
        var w = new List<float>();
        float r2 = grabRadius * grabRadius;
        var axis = LocalViewAxis();

        for (int i = 0; i < _pos.Length; i++)
        {
            // 거리를 화면 평면에서 잰다. 3D 거리로 재면 깊이 방향으로 뭉쳐서
            // 3D 스트레칭처럼 보인다.
            var toVert = _pos[i] - local;
            var planar = toVert - axis * Vector3.Dot(toVert, axis);
            float d2 = Vector3.Lerp(toVert, planar, planarDeform).sqrMagnitude;

            if (d2 > r2) continue;

            // 다만 평면 거리만 쓰면 구의 앞면과 뒷면이 화면상 같은 자리로 읽혀
            // 둘 다 잡힌다 — 앞뒤로 다리가 두 개 생기는 원인. 카메라를 향한
            // 면만 잡히도록 가중치를 준다. (구 중심이 원점이라 정점 방향이 곧 법선)
            float facing = Vector3.Dot(_rest[i].normalized, -axis);
            float facingW = Mathf.Clamp01(Mathf.InverseLerp(0f, frontFalloff, facing));
            facingW = Mathf.Lerp(1f, facingW, planarDeform);
            if (facingW <= 0f) continue;

            float t = 1f - Mathf.Sqrt(d2) / grabRadius;
            // smoothstep. t*t는 가운데로 급격히 몰려서 점으로 꼬집히는 모양이 된다.
            // 이쪽은 가운데가 평평하고 가장자리에서만 떨어져 넓게 끌린다.
            idx.Add(i);
            w.Add(t * t * (3f - 2f * t) * facingW);
        }

        // 아무것도 안 잡혔으면 제일 가까운 정점 하나라도 잡는다.
        if (idx.Count == 0)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < _pos.Length; i++)
            {
                float d2 = (_pos[i] - local).sqrMagnitude;
                if (d2 < bestD) { bestD = d2; best = i; }
            }
            idx.Add(best);
            w.Add(1f);
        }

        _grabbedVerts = idx.ToArray();
        _grabWeight = w.ToArray();
        _grabbed = true;
    }

    public void Drag(Vector3 worldPoint)
    {
        if (!_grabbed) return;
        _grabTargetLocal = transform.InverseTransformPoint(worldPoint);
    }

    public void Release(Vector3 velocity)
    {
        if (!_grabbed) return;
        _grabbed = false;

        // 놓는 순간의 커서 속도를 잡고 있던 정점에 실어준다 — 휙 놓으면 출렁인다.
        var localVel = transform.InverseTransformVector(velocity);
        if (_grabbedVerts != null)
            for (int k = 0; k < _grabbedVerts.Length; k++)
                _vel[_grabbedVerts[k]] += localVel * _grabWeight[k];

        _grabbedVerts = null;
        _grabWeight = null;
    }

    public void Poke(Vector3 worldPoint, Vector3 direction)
    {
        var local = transform.InverseTransformPoint(worldPoint);
        var dir = transform.InverseTransformDirection(direction.normalized);
        float r2 = pokeRadius * pokeRadius;

        for (int i = 0; i < _pos.Length; i++)
        {
            float d2 = (_pos[i] - local).sqrMagnitude;
            if (d2 > r2) continue;
            float t = 1f - Mathf.Sqrt(d2) / pokeRadius;
            _vel[i] += dir * (pokeImpulse * t * t);
        }
    }

    void OnValidate()
    {
        // 인스펙터에서 분할 수를 바꾸면 메시를 다시 만든다.
        if (!Application.isPlaying || _mesh == null) return;
        if (_rest != null && _mesh.vertexCount == _rest.Length) return;
        Build();
    }
}
