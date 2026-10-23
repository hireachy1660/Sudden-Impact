using UnityEngine;

/// <summary>
/// [기능 2] 오브젝트 풀 환경에서 TrailRenderer의 잔상을 방지하는 컴포넌트.
///
/// 사용법:
///   - 총알 프리팹(또는 그 자식)에 TrailRenderer와 함께 이 컴포넌트를 추가하세요.
///   - 풀에서 꺼낼 때  → OnSpawnFromPool() 호출
///   - 풀에 반납할 때  → OnReturnToPool() 호출
/// </summary>
[RequireComponent(typeof(TrailRenderer))]
public class BulletTrail : MonoBehaviour
{
    [SerializeField]
    private TrailRenderer _trail;


    /// <summary>
    /// 풀에서 꺼낼 때 호출.
    /// 이전 프레임의 궤적 데이터를 완전히 지우고 방출을 재개합니다.
    /// </summary>
    public void OnSpawnFromPool()
    {
        // Clear() 만으로는 한 프레임 뒤에 잔상이 남을 수 있으므로
        // emitting을 false → Clear → true 순서로 처리합니다.
        _trail.emitting = false;
        _trail.Clear();
        _trail.emitting = true;
    }

    /// <summary>
    /// 풀에 반납할 때 호출.
    /// 방출을 멈추어 반납 직전 마지막 위치에서 새 궤적이 그려지지 않게 합니다.
    /// </summary>
    public void OnReturnToPool()
    {
        _trail.emitting = false;
    }
}
