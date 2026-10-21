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

    [Header("벽에 눌리기 (A3)")]
    [Tooltip("벽에 눌린 만큼 옆으로 퍼지는 정도. 부피 보존을 흉내낸다. "
           + "0이면 그냥 잘린 것처럼 납작해지기만 한다.")]
    [SerializeField, Range(0f, 1.5f)] float contactBulge = 0.9f;

    [Tooltip("퍼짐이 벽에서 얼마나 멀리까지 번지는지. 좁으면 접촉 테두리가 각지고, "
           + "넓으면 몸 전체가 부푼다.")]
    [SerializeField, Range(0.05f, 2f)] float contactFalloff = 0.9f;

    [Tooltip("접촉면 모서리가 얼마나 둥근지. 0에 가까우면 칼로 자른 것처럼 되고 "
           + "테두리에 꺾임이 생겨 노멀이 튄다. 크면 접촉면이 둥글어진다.")]
    [SerializeField, Range(0.001f, 0.5f)] float contactSoftness = 0.12f;

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

    // 실제로 적용 중인 늘어남 한계. maxStretch를 그대로 쓰지 않는 이유는 Update 참고.
    float _maxPullSmooth = -1f;

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

    // 눌린 벽 (오브젝트 공간). w가 아주 작으면 "닿은 데 없음"이 된다.
    static readonly Vector4 NoContact = new Vector4(0f, 1f, 0f, -1e9f);
    Vector4 _plane0 = NoContact;
    Vector4 _plane1 = NoContact;
    Vector2 _contactDepth;   // 각 평면에 몸이 얼마나 파고들었나 (오브젝트 공간)

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
    static readonly int IdWobbleFreq = Shader.PropertyToID("_WobbleFreq");
    static readonly int IdSquash = Shader.PropertyToID("_SquashAmount");
    static readonly int IdPlane0 = Shader.PropertyToID("_ContactPlane0");
    static readonly int IdPlane1 = Shader.PropertyToID("_ContactPlane1");
    static readonly int IdContactBulge = Shader.PropertyToID("_ContactBulge");
    static readonly int IdContactDepth = Shader.PropertyToID("_ContactDepth");
    static readonly int IdContactFalloff = Shader.PropertyToID("_ContactFalloff");
    static readonly int IdContactSoft = Shader.PropertyToID("_ContactSoft");

    /// <summary>붙이는 쪽이 표면 위치를 구할 때 필요하다.</summary>
    public float Radius => radius;

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

        // 늘어남 한계를 그대로 적용하면, 한계가 갑자기 줄 때 당겨져 있던 양이
        // 뚝 잘려 튄다(B2에서 Balloon 3.4 -> Bouncy 1.7로 바꾸니 2.279가 한 프레임에
        // 0.700이 됐다). 한계만 부드럽게 옮기면 잡은 채로 수치를 바꿔도 비교가 된다.
        // 한계가 그대로일 때는 아무 차이가 없다.
        const float limitRate = 4f;   // 초당 이동량
        float maxPull = radius * (maxStretch - 1f);
        if (_maxPullSmooth < 0f) _maxPullSmooth = maxPull;
        _maxPullSmooth = Mathf.MoveTowards(_maxPullSmooth, maxPull, limitRate * dt);

        if (_grabVector.magnitude > _maxPullSmooth)
            _grabVector = _grabVector.normalized * _maxPullSmooth;

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
        // 주파수를 셰이더 기본값에 맡기지 않고 C#에서 밀어넣는다.
        // 그래야 CPU와 GPU가 같은 값을 쓴다.
        _mat.SetFloat(IdWobbleFreq, wobbleFrequency);
        _mat.SetVector(IdPlane0, _plane0);
        _mat.SetVector(IdPlane1, _plane1);
        _mat.SetFloat(IdContactBulge, contactBulge);
        _mat.SetVector(IdContactDepth, _contactDepth);
        _mat.SetFloat(IdContactFalloff, contactFalloff);
        _mat.SetFloat(IdContactSoft, contactSoftness);
    }

    // ── B2 손맛 손잡이 ─────────────────────────────────────────────────────
    //
    // B2는 이 값들을 만지면서 "계속 만지고 싶은" 지점을 찾는 실험이다.
    // 프리셋을 잡는 중에 즉시 갈아끼울 수 있어야 비교가 되므로 밖으로 연다.
    // 잡기 추종(grabStiffness/Damping)은 B1에서 정해진 입력 처리라 열지 않는다.

    public float ReturnStiffness { get => returnStiffness; set => returnStiffness = value; }
    public float ReturnDamping { get => returnDamping; set => returnDamping = value; }
    public float MaxStretch { get => maxStretch; set => maxStretch = value; }
    public float WobbleFromRelease { get => wobbleFromRelease; set => wobbleFromRelease = value; }
    public float WobbleDecay { get => wobbleDecay; set => wobbleDecay = value; }
    public float GrabRadius { get => grabRadius; set => grabRadius = value; }
    public float BodyFollow { get => bodyFollow; set => bodyFollow = value; }

    /// <summary>
    /// 감쇠비. 1보다 작으면 되돌아올 때 지나쳤다 와서 "뽀잉"이 생기고,
    /// 1을 넘으면 지나치지 않고 스르륵 멎는다. 손맛을 가르는 숫자라 HUD에 띄운다.
    /// </summary>
    public float DampingRatio => returnDamping / (2f * Mathf.Sqrt(Mathf.Max(returnStiffness, 1e-4f)));

    // ── 변형을 CPU에서도 계산하기 (B3 얼굴 / B4 장신구) ────────────────────
    //
    // 셰이더의 Deform()과 **같은 식**을 C#으로 옮겨 둔 것이다. 표면에 뭘 붙이려면
    // "그 점이 변형 후 어디로 갔나"를 CPU가 알아야 한다. 정점 수천 개가 아니라
    // 부착점 몇 개만 계산하므로 비용은 사실상 없다.
    //
    // 같은 식이 두 벌이라는 게 이 방식의 대가다. 한쪽만 고치면 장신구가 표면에서
    // 뜬다. 고칠 때는 반드시 양쪽을 같이 고칠 것. (셰이더: ShaderDeformBlob.shader)

    [Tooltip("출렁임 주파수. 셰이더의 _WobbleFreq와 같은 값이어야 한다.")]
    [SerializeField] float wobbleFrequency = 12f;

    static float SmoothStep01(float t) => t * t * (3f - 2f * t);

    static Vector3 ApplyPull(Vector3 p, Vector3 anchor, Vector3 vec, float amount,
                             float grabRadius, float bodyFollow)
    {
        float d = Vector3.Distance(p, anchor);
        float t = Mathf.Clamp01(1f - d / Mathf.Max(grabRadius, 1e-4f));
        float weight = Mathf.Lerp(bodyFollow, 1f, SmoothStep01(t));
        return p + vec * (weight * amount);
    }

    static float SoftAbove(float sd, float k) => 0.5f * (sd + Mathf.Sqrt(sd * sd + k * k));

    Vector3 ApplyContactCpu(Vector3 p, Vector4 plane, float depth)
    {
        var n = new Vector3(plane.x, plane.y, plane.z);
        float sd = Vector3.Dot(p, n) - plane.w;
        if (sd >= contactFalloff) return p;

        float t = Mathf.Clamp01(1f - sd / contactFalloff);
        var lateral = p - n * Vector3.Dot(p, n);
        p += lateral * (depth * contactBulge * SmoothStep01(t));

        sd = Vector3.Dot(p, n) - plane.w + contactSoftness * 0.5f;
        return p + n * (SoftAbove(sd, contactSoftness) - sd);
    }

    /// <summary>오브젝트 공간의 한 점이 변형 후 어디로 가는지.</summary>
    public Vector3 DeformPoint(Vector3 p)
    {
        p = ApplyPull(p, _grabPointOS, _grabVector, 1f, grabRadius, bodyFollow);
        p = ApplyPull(p, _residualPointOS, _residualVector, 1f, grabRadius, bodyFollow);

        var axis = (_wobbleAxis + Vector3.one * 1e-6f).normalized;
        float wave = Mathf.Sin(Vector3.Dot(p, axis) * 2.2f + _wobblePhase * wobbleFrequency);
        p += axis * (wave * _wobbleAmount);

        float s = _squash;
        p = new Vector3(p.x * (1f + s * 0.5f), p.y * (1f - s), p.z * (1f + s * 0.5f));

        p = ApplyContactCpu(p, _plane0, _contactDepth.x);
        p = ApplyContactCpu(p, _plane1, _contactDepth.y);
        return p;
    }

    /// <summary>
    /// 변형 후 그 자리의 바깥 방향. 셰이더가 하는 것과 같게 이웃 두 점을 같이
    /// 변형해 외적으로 구한다. 장신구를 표면에 눕히려면 이게 필요하다.
    /// </summary>
    public Vector3 DeformNormal(Vector3 p, Vector3 restNormal)
    {
        var t1 = Vector3.Normalize(Mathf.Abs(restNormal.y) < 0.99f
            ? Vector3.Cross(restNormal, Vector3.up)
            : Vector3.Cross(restNormal, Vector3.right));
        var t2 = Vector3.Cross(restNormal, t1);

        const float eps = 0.01f;
        var p0 = DeformPoint(p);
        var pa = DeformPoint(p + t1 * eps);
        var pb = DeformPoint(p + t2 * eps);
        var n = Vector3.Cross(pa - p0, pb - p0).normalized;
        return Vector3.Dot(n, restNormal) < 0f ? -n : n;
    }

    // ── 벽 접촉 (A3) ───────────────────────────────────────────────────────

    /// <summary>
    /// 눌린 벽을 월드 평면 (normal.xyz, w) 으로 받아 오브젝트 공간으로 옮겨 둔다.
    /// 안쪽이 양수인 표현을 그대로 유지한다 — 셰이더는 음수인 정점만 민다.
    ///
    /// 평면이 몇 개든 셰이더가 하는 일은 같다. 벽이 화면 끝인지 작업표시줄인지
    /// 다른 창인지는 여기서 모르고, 그래서 A3-2에서 출처만 갈아끼울 수 있다.
    /// </summary>
    public void SetContacts(Vector4 world0, float depth0, Vector4? world1, float depth1)
    {
        float scale = Mathf.Max(transform.lossyScale.x, 1e-4f);
        _plane0 = ToObjectSpace(world0);
        _plane1 = world1.HasValue ? ToObjectSpace(world1.Value) : NoContact;
        _contactDepth = new Vector2(depth0 / scale, world1.HasValue ? depth1 / scale : 0f);
    }

    public void ClearContacts()
    {
        _plane0 = NoContact;
        _plane1 = NoContact;
        _contactDepth = Vector2.zero;
    }

    Vector4 ToObjectSpace(Vector4 world)
    {
        var n = new Vector3(world.x, world.y, world.z);
        var nOS = transform.InverseTransformDirection(n).normalized;
        // 월드 점 = R*(p_os * s) + t 이므로 dot(p_os, nOS) >= (w - dot(t, n)) / s
        float scale = Mathf.Max(transform.lossyScale.x, 1e-4f);
        float wOS = (world.w - Vector3.Dot(transform.position, n)) / scale;
        return new Vector4(nOS.x, nOS.y, nOS.z, wOS);
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

    /// <summary>
    /// 벽에 부딪힌 순간의 반응. 놓을 때와 같은 출렁임 기계를 쓰되 축을 벽의 법선으로 둔다.
    ///
    /// 눌리는 깊이 자체는 BlobBody의 접촉 스프링이 이미 출렁이게 하고 있다.
    /// 여기서 더하는 것은 표면의 물결 — 그게 없으면 닿는 순간 눌린 모양으로
    /// 바뀌고 그대로 멎어서 생기가 없다.
    ///
    /// 위상은 절대 리셋하지 않고 축도 이미 출렁이는 중이면 그대로 둔다.
    /// B1에서 그걸 리셋했다가 클릭할 때마다 화면이 새로고침되듯 끊겼다.
    /// </summary>
    public void Impact(Vector3 worldNormal, float speed)
    {
        if (_wobbleTarget < 0.02f)
            _wobbleAxis = transform.InverseTransformDirection(worldNormal).normalized;
        _wobbleTarget = Mathf.Max(_wobbleTarget, Mathf.Min(0.35f, speed * wobbleFromRelease));
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
