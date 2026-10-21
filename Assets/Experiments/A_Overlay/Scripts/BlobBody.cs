using UnityEngine;

/// <summary>
/// A3 — 말랑이 몸을 화면 안에서 움직이고, 벽에 눌렸을 때 눌린 평면을 변형 쪽에 넘긴다.
///
/// 여기서 확인하려는 전제는 하나다.
/// B1에서 셰이더 정점 변형을 고른 이유가 "벽에 눌려 납작해지는 건 수식으로 표현된다"는
/// 판단이었는데, 그걸 해보지 않고 결정했다. 안 되면 B1을 다시 봐야 한다.
///
/// 몸이 벽을 파고드는 걸 막지 않는 것이 요점이다. 중심이 벽 안으로 들어간 만큼
/// 스프링이 밀어내고, 들어간 깊이를 평면으로 넘겨 셰이더가 그 너머를 눌러 편다.
/// 중심을 반지름만큼 떨어뜨려 고정하면 딱딱한 공이 되어 버린다.
/// </summary>
[RequireComponent(typeof(ShaderDeformBlob))]
public class BlobBody : MonoBehaviour
{
    [SerializeField] ScreenWalls walls;

    [Header("몸 움직임")]
    [SerializeField] float gravity = 9f;
    [SerializeField] bool gravityEnabled = true;

    [Tooltip("벽에 닿아 있는 동안 벽을 따라 미끄러지는 속도가 줄어드는 정도.")]
    [SerializeField, Range(0f, 20f)] float wallFriction = 3f;

    [Tooltip("공중에서의 감쇠. 던진 게 영원히 날아다니지 않게 한다.")]
    [SerializeField, Range(0f, 5f)] float airDrag = 0.2f;

    [Header("벽에 눌리기")]
    [Tooltip("벽 안으로 파고든 깊이를 되미는 힘. 낮으면 푹 눌리고 높으면 단단하다. "
           + "가만히 놓였을 때 눌리는 깊이는 중력 / 이 값이다 — 9 / 60 = 0.15.")]
    [SerializeField] float contactStiffness = 60f;

    [Tooltip("눌림 감쇠. 낮으면 벽에서 몇 번 되튀고 높으면 한 번에 멎는다. "
           + "되튀김을 따로 반사로 처리하지 않는 이유는 ResolveWalls 주석 참고.")]
    [SerializeField] float contactDamping = 5f;

    [Tooltip("반지름 대비 최대로 파고들 수 있는 깊이. 1이면 중심까지 들어간다.")]
    [SerializeField, Range(0f, 0.9f)] float maxPenetration = 0.55f;

    [Tooltip("이 속도보다 빠르게 벽에 들어올 때만 표면이 출렁인다. "
           + "0이면 살짝 닿아도 떨어서 벽에 붙어 떠는 모양이 된다.")]
    [SerializeField] float impactSpeed = 1.2f;

    [SerializeField] float radius = 1f;

    ShaderDeformBlob _blob;
    Vector3 _velocity;
    Vector3 _lastPos;
    bool _wasGrabbed;
    bool[] _wasTouching;   // 벽마다 직전 프레임에 닿아 있었나

    public string DebugLine { get; private set; } = "";
    public bool GravityEnabled { get => gravityEnabled; set => gravityEnabled = value; }

    void Awake()
    {
        _blob = GetComponent<ShaderDeformBlob>();
        if (walls == null) walls = FindFirstObjectByType<ScreenWalls>();
        _lastPos = transform.position;
    }

    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 1f / 30f);

        if (_blob.IsGrabbed)
        {
            // 잡고 있는 동안은 입력이 몸을 끌고 간다. 중력은 쉬고, 대신 실제로
            // 얼마나 움직였는지를 기록해 둔다 — 놓는 순간 그게 던진 속도가 된다.
            var moved = (transform.position - _lastPos) / Mathf.Max(dt, 1e-4f);
            _velocity = Vector3.Lerp(_velocity, moved, 0.5f);
            _wasGrabbed = true;
        }
        else
        {
            if (_wasGrabbed) _wasGrabbed = false;

            if (gravityEnabled) _velocity.y -= gravity * dt;
            _velocity *= Mathf.Exp(-airDrag * dt);
            transform.position += _velocity * dt;
        }

        ResolveWalls(dt);
        _lastPos = transform.position;
    }

    /// <summary>
    /// 벽마다 중심이 얼마나 파고들었는지 보고, 되미는 힘을 주면서
    /// 가장 깊이 눌린 평면 두 개를 변형 쪽에 넘긴다 (모서리에 몰렸을 때를 위해 둘).
    ///
    /// 감쇠 스프링 하나로 처리한다. 처음에는 되튀김을 속도 반사로 따로 넣었는데,
    /// 그러면 벽으로 들어오는 속도가 매 프레임 지워져서 **눌림이 쌓이질 못한다.**
    /// 가만히 놓아둬도 깊이가 0.08에 머물렀다. 반사는 딱딱한 물체의 처리다.
    /// 말랑한 물체는 스프링이 밀어내고, 되튀김은 감쇠가 낮을 때 저절로 나온다.
    /// </summary>
    void ResolveWalls(float dt)
    {
        if (walls == null) { _blob.ClearContacts(); return; }

        var planes = walls.Planes;
        var pos = transform.position;
        if (_wasTouching == null || _wasTouching.Length != planes.Length)
            _wasTouching = new bool[planes.Length];

        Vector4 best0 = default, best1 = default;
        float depth0 = 0f, depth1 = 0f;
        int touching = 0;

        for (int i = 0; i < planes.Length; i++)
        {
            var n = new Vector3(planes[i].x, planes[i].y, planes[i].z);
            float signed = Vector3.Dot(pos, n) - planes[i].w;   // 벽면에서 중심까지
            float pen = radius - signed;                        // 양수면 파고든 것
            if (pen <= 0f) { _wasTouching[i] = false; continue; }

            touching++;

            // 너무 깊이 들어가지 않게 — 여기서만 위치를 직접 건드린다.
            float maxPen = radius * maxPenetration;
            if (pen > maxPen)
            {
                pos += n * (pen - maxPen);
                pen = maxPen;
            }

            // 되미는 스프링 + 감쇠. 벽에 수직인 성분만 다룬다.
            float vn = Vector3.Dot(_velocity, n);

            // 막 닿은 순간에만 표면을 출렁이게 한다. 닿아 있는 내내 주면
            // 벽에 붙어 떠는 모양이 된다. 천천히 밀어붙이면 아무 일도 없어야 한다.
            if (!_wasTouching[i] && vn < -impactSpeed) _blob.Impact(n, -vn);
            _wasTouching[i] = true;

            _velocity += n * (pen * contactStiffness * dt);
            _velocity -= n * (vn * Mathf.Min(1f, contactDamping * dt));

            // 벽을 따라 미끄러지는 성분은 마찰로 깎는다.
            var tangent = _velocity - n * Vector3.Dot(_velocity, n);
            _velocity -= tangent * Mathf.Min(1f, wallFriction * dt);

            if (pen > depth0) { depth1 = depth0; best1 = best0; depth0 = pen; best0 = planes[i]; }
            else if (pen > depth1) { depth1 = pen; best1 = planes[i]; }
        }

        transform.position = pos;

        if (touching == 0) _blob.ClearContacts();
        else _blob.SetContacts(best0, depth0, depth1 > 0f ? best1 : (Vector4?)null, depth1);

        DebugLine = touching == 0
            ? "contact none"
            : $"contact {touching}  depth {depth0:F2}  vel {_velocity.magnitude:F2}";
    }

    public void ResetToCenter()
    {
        var cam = Camera.main;
        transform.position = cam != null
            ? new Vector3(cam.transform.position.x, cam.transform.position.y, 0f)
            : Vector3.zero;
        _velocity = Vector3.zero;
        _lastPos = transform.position;
    }
}
