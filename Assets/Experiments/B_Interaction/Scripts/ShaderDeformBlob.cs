using UnityEngine;

/// <summary>
/// B1 후보 ② — 셰이더 정점 변형.
///
/// CPU는 값 몇 개(잡은 지점, 당긴 벡터, 출렁임 진폭·위상)만 스프링처럼 굴리고,
/// 실제 모양은 정점 셰이더가 만든다. 정점 수가 늘어도 CPU 비용이 안 늘어난다.
///
/// 메시 스프링과 같은 입력을 받되, 상태 공간이 훨씬 작다. 그게 장점이자 한계다 —
/// 가볍지만 "수식으로 표현되는 모양"만 나온다.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ShaderDeformBlob : MonoBehaviour, IBlob
{
    [Header("메시")]
    [SerializeField, Range(1, 4)] int subdivisions = 3;   // 셰이더 변형은 정점이 많아도 싸다
    [SerializeField] float radius = 1f;

    [Header("탄성")]
    [Tooltip("당긴 양이 0으로 돌아오는 속도.")]
    [SerializeField] float returnStiffness = 90f;
    [SerializeField] float returnDamping = 7f;

    [Tooltip("원래 반지름 대비 최대 늘어남.")]
    [SerializeField, Range(1.1f, 4f)] float maxStretch = 2.2f;

    [Header("출렁임")]
    [Tooltip("놓는 순간 속도가 출렁임으로 얼마나 바뀌나.")]
    [SerializeField] float wobbleFromRelease = 0.16f;
    [SerializeField] float wobbleDecay = 2.6f;

    [Header("잡기 추종 — 커서를 스프링으로 따라간다")]
    [Tooltip("커서를 그대로 받아쓰면 마우스 폴링 간격과 픽셀 양자화가 그대로 드러나 "
           + "프레임이 떨어지는 것처럼 보인다. 스프링으로 따라가면 그 계단이 걸러진다.")]
    [SerializeField] float grabStiffness = 600f;

    [Tooltip("추종 감쇠. 대략 2*sqrt(강성)이 임계값이다. 낮추면 출렁이며 따라온다.")]
    [SerializeField] float grabDamping = 48f;

    [Header("잡기")]
    [Tooltip("잡는 점 주변 이 반경이 끌려온다. 좁으면 점으로 꼬집힌다.")]
    [SerializeField, Range(0.1f, 3f)] float grabRadius = 1.1f;

    [Tooltip("몸 전체가 커서 쪽으로 따라가는 비율. 0이면 잡은 데만 뽑혀 나온다.")]
    [SerializeField, Range(0f, 1f)] float bodyFollow = 0.35f;

    [Header("찌르기")]
    [Tooltip("찌를 때 주는 충격. 눌림 위치가 아니라 속도에 더한다 — "
           + "위치를 직접 바꾸면 실루엣이 통째로 튀어서 클릭마다 끊겨 보인다.")]
    [SerializeField] float pokeImpulse = 2.2f;

    [Tooltip("눌림이 돌아오는 강성.")]
    [SerializeField] float squashStiffness = 180f;

    [Tooltip("눌림 감쇠. 낮으면 몇 번 출렁이고, 높으면 한 번에 멎는다.")]
    [SerializeField] float squashDamping = 11f;

    // ── 내부 상태 ─────────────────────────────────────────────────────────
    Material _mat;
    Vector3 _grabPointOS;      // 잡은 지점 (오브젝트 공간, 고정)
    Vector3 _grabVector;       // 거기서 얼마나 당겨졌나 (실제 변형에 쓰이는 값)
    Vector3 _grabTargetVector; // 커서가 지시하는 목표. _grabVector가 스프링으로 쫓아간다
    Vector3 _grabVelocity;
    bool _grabbed;

    // 직전 잡기의 잔여. 새로 잡을 때 현재 변형을 여기로 넘기고 따로 잦아들게 한다.
    // 슬롯이 하나뿐이면 변형이 남은 채로 새로 잡을 때 이전 걸 버려야 해서 툭 끊긴다.
    Vector3 _residualPointOS;
    Vector3 _residualVector;
    Vector3 _residualVelocity;

    Vector3 _wobbleAxis = Vector3.up;
    float _wobbleAmount;
    float _wobblePhase;

    float _squash;
    float _squashVelocity;
    float _wobbleTarget;

    static readonly int IdGrabPoint = Shader.PropertyToID("_GrabPoint");
    static readonly int IdGrabVector = Shader.PropertyToID("_GrabVector");
    static readonly int IdGrabRadius = Shader.PropertyToID("_GrabRadius");
    static readonly int IdGrabAmount = Shader.PropertyToID("_GrabAmount");
    static readonly int IdBodyFollow = Shader.PropertyToID("_BodyFollow");
    static readonly int IdGrabPoint2 = Shader.PropertyToID("_GrabPoint2");
    static readonly int IdGrabVector2 = Shader.PropertyToID("_GrabVector2");
    static readonly int IdWobbleAxis = Shader.PropertyToID("_WobbleAxis");
    static readonly int IdWobbleAmount = Shader.PropertyToID("_WobbleAmount");
    static readonly int IdWobblePhase = Shader.PropertyToID("_WobblePhase");
    static readonly int IdSquash = Shader.PropertyToID("_SquashAmount");

    public string DisplayName => "Shader Deform";
    public bool IsGrabbed => _grabbed;
    public string DebugLine =>
        $"pull {_grabVector.magnitude:F2}  residual {_residualVector.magnitude:F2}  wobble {_wobbleAmount:F3}";

    void Awake()
    {
        var mf = GetComponent<MeshFilter>();
        mf.sharedMesh = IcoSphere.Create(subdivisions, radius);

        // 머티리얼 인스턴스를 만든다. 공유하면 두 후보가 서로의 값을 덮어쓴다.
        var mr = GetComponent<MeshRenderer>();
        _mat = mr.material;

        var col = GetComponent<SphereCollider>();
        if (col == null) col = gameObject.AddComponent<SphereCollider>();
        col.radius = radius;
    }

    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 1f / 30f);

        if (_grabbed)
        {
            // 커서를 직접 대입하지 않고 스프링으로 쫓아간다.
            // 메시 스프링은 물리가 커서의 계단을 걸러주는데, 여기엔 그 과정이
            // 없어서 입력 양자화가 그대로 드러났다. 같은 조건으로 맞춘다.
            _grabVelocity += (_grabTargetVector - _grabVector) * (grabStiffness * dt);
            _grabVelocity *= Mathf.Exp(-grabDamping * dt);
            _grabVector += _grabVelocity * dt;
        }
        else
        {
            // 당긴 양을 스프링으로 0까지 되돌린다.
            _grabVelocity += (-_grabVector) * (returnStiffness * dt);
            _grabVelocity *= Mathf.Exp(-returnDamping * dt);
            _grabVector += _grabVelocity * dt;
        }

        // 잔여는 잡혀 있든 아니든 계속 0으로 잦아든다.
        _residualVelocity += (-_residualVector) * (returnStiffness * dt);
        _residualVelocity *= Mathf.Exp(-returnDamping * dt);
        _residualVector += _residualVelocity * dt;
        if (_residualVector.sqrMagnitude < 1e-6f)
        {
            _residualVector = Vector3.zero;
            _residualVelocity = Vector3.zero;
        }

        float maxPull = radius * (maxStretch - 1f);
        if (_grabVector.magnitude > maxPull)
            _grabVector = _grabVector.normalized * maxPull;

        _wobblePhase += dt;

        // 진폭도 즉시 올리지 않는다. sin이 이미 0이 아닌 지점에 있으면
        // 진폭이 튀는 순간 변위도 같이 튄다.
        _wobbleTarget = Mathf.MoveTowards(_wobbleTarget, 0f, _wobbleTarget * wobbleDecay * dt + 1e-4f);
        _wobbleAmount = Mathf.MoveTowards(_wobbleAmount, _wobbleTarget, dt / 0.05f);

        // 눌림은 스프링으로 돌아온다. 위치가 아니라 속도에 충격을 받으므로
        // 찌르는 순간에도 모양이 연속이다.
        _squashVelocity += (-_squash) * (squashStiffness * dt);
        _squashVelocity *= Mathf.Exp(-squashDamping * dt);
        _squash += _squashVelocity * dt;

        Push();
    }

    void Push()
    {
        _mat.SetVector(IdGrabPoint, _grabPointOS);
        _mat.SetVector(IdGrabVector, _grabVector);
        _mat.SetFloat(IdGrabRadius, grabRadius);
        _mat.SetFloat(IdGrabAmount, 1f);
        _mat.SetFloat(IdBodyFollow, bodyFollow);
        _mat.SetVector(IdGrabPoint2, _residualPointOS);
        _mat.SetVector(IdGrabVector2, _residualVector);
        _mat.SetVector(IdWobbleAxis, _wobbleAxis);
        _mat.SetFloat(IdWobbleAmount, _wobbleAmount);
        _mat.SetFloat(IdWobblePhase, _wobblePhase);
        _mat.SetFloat(IdSquash, _squash);
    }

    // ── IBlob ─────────────────────────────────────────────────────────────
    public void Grab(Vector3 worldPoint)
    {
        // 지금 남아 있는 변형을 잔여 슬롯으로 넘긴다. 그래야 새 잡기가
        // 0에서 깨끗하게 시작하면서도 화면에서 변형이 튀지 않는다.
        // (예전에는 앵커를 _grabVector만큼 어긋나게 잡아서, 부푸는 지점이
        //  실제 클릭한 곳과 달라지고 첫 드래그에 잔여가 0으로 튀었다.)
        // 슬롯이 하나뿐이라 연타하면 이전 잔여를 버리게 된다. 버릴 때는
        // 작은 쪽을 버려서 눈에 띄는 변화가 최소가 되게 한다.
        if (_grabVector.sqrMagnitude > _residualVector.sqrMagnitude)
        {
            _residualPointOS = _grabPointOS;
            _residualVector = _grabVector;
            _residualVelocity = _grabVelocity;
        }

        _grabbed = true;
        // 콜라이더는 변형하지 않으므로 레이캐스트 지점이 곧 원래 자리에 가깝다.
        _grabPointOS = transform.InverseTransformPoint(worldPoint);
        _grabVector = Vector3.zero;
        _grabTargetVector = Vector3.zero;
        _grabVelocity = Vector3.zero;
    }

    public void Drag(Vector3 worldPoint)
    {
        if (!_grabbed) return;
        var local = transform.InverseTransformPoint(worldPoint);
        _grabTargetVector = local - _grabPointOS;
    }

    public void Release(Vector3 velocity)
    {
        if (!_grabbed) return;
        _grabbed = false;

        var localVel = transform.InverseTransformVector(velocity);
        _grabVelocity = localVel;

        // 놓는 속도를 출렁임으로 바꾼다.
        // 위상은 절대 리셋하지 않는다 — sin 인자가 튀면 파형 전체가 순간이동해서
        // 클릭할 때마다 화면이 새로고침되는 것처럼 끊긴다. 진폭만 바꾼다.
        // 축도 이미 출렁이는 중이면 그대로 둔다. 바꾸면 같은 이유로 튄다.
        if (_wobbleTarget < 0.02f && _grabVector.sqrMagnitude > 1e-6f)
            _wobbleAxis = _grabVector.normalized;
        _wobbleTarget = Mathf.Max(_wobbleTarget, Mathf.Min(0.35f, localVel.magnitude * wobbleFromRelease));
    }

    public void Poke(Vector3 worldPoint, Vector3 direction)
    {
        _squashVelocity += pokeImpulse;
        if (_wobbleTarget < 0.02f)
            _wobbleAxis = transform.InverseTransformDirection(direction.normalized);
        _wobbleTarget = Mathf.Max(_wobbleTarget, 0.12f);
    }

    void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
    }
}
