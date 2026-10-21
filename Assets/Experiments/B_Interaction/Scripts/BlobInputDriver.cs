using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// B1 공통 입력. 후보 전부가 이 한 벌을 쓴다.
///
/// 구현마다 입력을 따로 만들면 "어느 쪽이 더 나은지"가 아니라
/// "어느 쪽 입력이 더 나은지"를 비교하게 된다. 그러면 실험이 성립하지 않는다.
///
/// 조작 (기획서의 입력 설계):
///   클릭      = 찌르기
///   드래그    = 늘리기
///   휙 놓기   = 던지기 (놓는 순간의 커서 속도를 실어준다)
/// </summary>
public class BlobInputDriver : MonoBehaviour
{
    [SerializeField] Camera cam;

    [Tooltip("이 거리 이상 끌면 찌르기가 아니라 늘리기로 친다.")]
    [SerializeField] float dragThreshold = 0.06f;

    [Tooltip("찌르기를 켤지. 끄면 클릭해도 아무 일 없고 드래그만 동작한다. "
           + "찌르기가 체감을 해치는지 A/B로 보기 위한 스위치.")]
    [SerializeField] bool enablePoke = true;

    [Tooltip("놓는 속도를 낼 때 최근 몇 초를 볼지. 짧으면 민감하고 길면 뭉툭하다.")]
    [SerializeField] float velocityWindow = 0.08f;

    [Header("끌고 가기")]
    [Tooltip("이보다 멀리 끌면 몸이 커서를 따라 움직이기 시작한다. "
           + "그 전까지는 제자리에서 늘어나기만 한다. 0이면 항상 따라온다.")]
    [SerializeField] float followThreshold = 0.9f;

    [Tooltip("몸이 커서를 따라잡는 속도. 낮으면 무겁게 끌려온다.")]
    [SerializeField, Range(0f, 20f)] float followRate = 6f;

    IBlob _active;
    Transform _activeTf;
    Vector3 _grabPlanePoint;
    Vector3 _grabNormal;
    bool _dragging;
    float _dragDistance;
    Vector3 _grabAnchorLocal;   // 잡은 지점을 몸 기준으로 기억한다 — 몸이 움직여도 따라간다

    readonly Queue<(float t, Vector3 p)> _trail = new();
    Vector3 _lastWorld;

    public IBlob Active => _active;
    public string DebugLine { get; private set; } = "";

    void Awake()
    {
        if (cam == null) cam = Camera.main;
    }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null || cam == null) return;

        var screen = mouse.position.ReadValue();

        if (mouse.leftButton.wasPressedThisFrame) BeginGrab(screen);
        else if (mouse.leftButton.isPressed && _active != null) ContinueGrab(screen);
        else if (mouse.leftButton.wasReleasedThisFrame && _active != null) EndGrab(screen);

        DebugLine = _active == null
            ? "no blob grabbed"
            : $"{_active.DisplayName}  drag {_dragDistance:F2}  {_active.DebugLine}";
    }

    void BeginGrab(Vector2 screen)
    {
        var ray = cam.ScreenPointToRay(screen);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f)) return;

        var blob = hit.collider.GetComponentInParent<IBlob>();
        if (blob == null) return;

        _active = blob;
        _activeTf = hit.collider.transform;
        _grabPlanePoint = hit.point;
        // 카메라를 바라보는 평면 위에서 커서를 추적한다. 깊이 방향으로는
        // 끌 수 없지만, 마우스 한 개로는 그게 자연스럽다.
        _grabNormal = -cam.transform.forward;

        _grabAnchorLocal = _activeTf.InverseTransformPoint(hit.point);
        _dragging = false;
        _dragDistance = 0f;
        _lastWorld = hit.point;

        _trail.Clear();
        _trail.Enqueue((Time.time, hit.point));

        blob.Grab(hit.point);
    }

    void ContinueGrab(Vector2 screen)
    {
        if (!ProjectToGrabPlane(screen, out var world)) return;

        _dragDistance += Vector3.Distance(world, _lastWorld);
        _lastWorld = world;
        if (_dragDistance > dragThreshold) _dragging = true;

        _trail.Enqueue((Time.time, world));
        while (_trail.Count > 2 && Time.time - _trail.Peek().t > velocityWindow)
            _trail.Dequeue();

        ApplyBodyFollow(world);
        _active.Drag(world);
    }

    void EndGrab(Vector2 screen)
    {
        ProjectToGrabPlane(screen, out var world);

        if (!_dragging)
        {
            // 끌지 않고 뗐으면 찌르기. 방향은 카메라에서 안쪽으로.
            _active.Release(Vector3.zero);
            if (enablePoke) _active.Poke(_grabPlanePoint, cam.transform.forward);
        }
        else
        {
            _active.Release(ComputeReleaseVelocity(world));
        }

        _active = null;
        _activeTf = null;
        _dragging = false;
        _trail.Clear();
    }

    /// <summary>
    /// 한계 이상 끌면 몸이 커서를 따라 움직인다.
    ///
    /// 이게 없으면 아무리 끌어도 제자리로 돌아와서 어딘가에 묶여 있는 느낌이 든다.
    /// 한계까지는 제자리에서 늘어나고, 그 너머로만 몸이 옮겨간다 —
    /// 늘어나는 맛과 끌고 다니는 맛을 둘 다 남기려는 것.
    ///
    /// 두 후보가 똑같이 동작하도록 구현 쪽이 아니라 여기에 둔다.
    /// </summary>
    void ApplyBodyFollow(Vector3 cursorWorld)
    {
        if (_activeTf == null || followRate <= 0f) return;

        var anchorWorld = _activeTf.TransformPoint(_grabAnchorLocal);
        var offset = cursorWorld - anchorWorld;
        float dist = offset.magnitude;
        if (dist <= followThreshold) return;

        var excess = offset.normalized * (dist - followThreshold);
        var step = excess * Mathf.Clamp01(followRate * Time.deltaTime);

        _activeTf.position += step;
        // 커서를 투영하는 평면도 같이 옮긴다. 안 그러면 몸만 가고 기준이 남는다.
        _grabPlanePoint += step;
        _lastWorld += step;
    }

    bool ProjectToGrabPlane(Vector2 screen, out Vector3 world)
    {
        var plane = new Plane(_grabNormal, _grabPlanePoint);
        var ray = cam.ScreenPointToRay(screen);
        if (plane.Raycast(ray, out float enter))
        {
            world = ray.GetPoint(enter);
            return true;
        }
        world = _lastWorld;
        return false;
    }

    /// <summary>
    /// 최근 몇 프레임의 이동을 평균 낸다. 마지막 한 프레임만 쓰면
    /// 손이 멈춘 상태로 떼도 미세한 떨림이 던지기로 잡힌다.
    /// </summary>
    Vector3 ComputeReleaseVelocity(Vector3 endPoint)
    {
        if (_trail.Count < 2) return Vector3.zero;

        var oldest = _trail.Peek();
        float dt = Time.time - oldest.t;
        if (dt < 1e-4f) return Vector3.zero;

        return (endPoint - oldest.p) / dt;
    }
}
