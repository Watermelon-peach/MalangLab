using UnityEngine;

/// <summary>
/// 말랑이 변형 방식들이 공유하는 입력 인터페이스.
///
/// B1은 방식 비교가 목적이므로 입력은 한 벌만 만들고 구현만 갈아끼운다.
/// 입력이 미묘하게 다르면 "어느 쪽이 더 나은지"가 아니라
/// "어느 쪽 입력이 더 나은지"를 비교하게 된다.
/// </summary>
public interface IBlob
{
    /// <summary>표시 이름 — HUD에 찍는다.</summary>
    string DisplayName { get; }

    /// <summary>잡는다. worldPoint는 표면의 레이캐스트 지점.</summary>
    void Grab(Vector3 worldPoint);

    /// <summary>잡은 채로 끈다.</summary>
    void Drag(Vector3 worldPoint);

    /// <summary>놓는다. velocity는 놓는 순간의 커서 속도 — 던지기에 쓴다.</summary>
    void Release(Vector3 velocity);

    /// <summary>찌른다. 한 번 눌렀다 뗀 경우.</summary>
    void Poke(Vector3 worldPoint, Vector3 direction);

    /// <summary>지금 잡혀 있나.</summary>
    bool IsGrabbed { get; }

    /// <summary>HUD에 찍을 한 줄 상태 (변형량 등).</summary>
    string DebugLine { get; }
}
