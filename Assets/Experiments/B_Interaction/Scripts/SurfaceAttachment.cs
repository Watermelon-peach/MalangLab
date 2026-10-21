using UnityEngine;

/// <summary>
/// 말랑이 표면의 한 점에 물건을 붙인다. 몸이 변형되면 붙은 것도 따라 움직인다.
///
/// 눈알·입 같은 입체 얼굴 요소(B3)와 장신구 토핑(B4)이 둘 다 이걸로 풀린다.
/// 차이는 "붙은 자리를 바꿀 수 있는가"뿐이다 —
/// 얼굴 토핑은 고정 위치, 장신구 토핑은 자유 이동(기획서의 토핑 2분류).
///
/// 몸 메시에 눈 모양을 섞어 넣는 방법도 있지만, 그러면 눈알도 몸처럼 늘어나고
/// 토핑을 바꿀 때마다 메시를 다시 만들어야 한다. 딱딱한 구슬 눈이라면 이쪽이다.
/// </summary>
[ExecuteAlways]
public class SurfaceAttachment : MonoBehaviour
{
    [SerializeField] ShaderDeformBlob blob;

    [Tooltip("붙을 자리. 말랑이 오브젝트 공간에서의 방향이다. 길이는 무시하고 "
           + "방향만 쓴다 — 표면 위로 올려붙이기 때문.")]
    [SerializeField] Vector3 direction = new Vector3(-0.34f, 0.18f, -0.92f);

    [Tooltip("표면에서 얼마나 띄울지. 0이면 표면에 딱 붙고, 키우면 튀어나온다.")]
    [SerializeField] float lift = 0.04f;

    [Tooltip("몸이 변형돼도 크기는 그대로 둘지. 눈알처럼 단단한 것은 켠다.")]
    [SerializeField] bool rigid = true;

    [Tooltip("표면을 향해 눕힐지. 끄면 회전을 건드리지 않는다.")]
    [SerializeField] bool alignToSurface = true;

    Vector3 _baseScale = Vector3.one;
    bool _captured;

    void OnEnable()
    {
        if (blob == null) blob = GetComponentInParent<ShaderDeformBlob>();
        if (!_captured) { _baseScale = transform.localScale; _captured = true; }
    }

    // 몸의 변형이 다 끝난 뒤에 붙어야 한 프레임 늦지 않는다.
    void LateUpdate()
    {
        if (blob == null) return;

        var restDir = direction.sqrMagnitude < 1e-8f ? Vector3.forward : direction.normalized;
        float radius = blob.Radius;

        var restPoint = restDir * radius;
        var deformed = blob.DeformPoint(restPoint);
        var normal = blob.DeformNormal(restPoint, restDir);

        transform.position = blob.transform.TransformPoint(deformed + normal * lift);

        if (alignToSurface)
            transform.rotation = blob.transform.rotation *
                                 Quaternion.LookRotation(normal, Vector3.up);

        if (rigid) transform.localScale = _baseScale;
    }
}
