using UnityEngine;

public class Furniture : MonoBehaviour, IAttackReceiver
{
    [Header("Stats")]
    [SerializeField] protected float maxHp = 50f;
    protected float curHp;
    protected bool isDestroyed = false;
    protected Vector3 lastHitNormal = Vector3.up;

    // FurnitureNetworkManager가 부여하는 씬 고유 인덱스
    private int _index = -1;
    public bool IsDestroyed => isDestroyed;
    public void SetIndex(int index) => _index = index;

    protected AudioSource audioSource;
    protected virtual void Awake()
    {
        curHp = maxHp;
        audioSource = GetComponent<AudioSource>();
    }

    // [CH 수정] 기존 OnReceiveImpact + RPC_ApplyDamage → 매니저 위임 방식으로 교체
    // ── 기존 코드 ──────────────────────────────────────────────
    // public virtual void OnReceiveImpact(ImpactData _data)
    // {
    //     Debug.Log($" {gameObject.name}이 맞았습니다! 받은 데미지: {_data.damage}");
    //     if (isDestroyed) return;
    //     if (photonView == null)
    //     {
    //         Debug.LogError("가구에 PhotonView 컴포넌트가 없습니다!");
    //         return;
    //     }
    //     // 네트워크 모든 인스턴스에 데미지 동기화
    //     photonView.RPC(nameof(RPC_ApplyDamage), RpcTarget.All, _data.damage, _data.hitNormal);
    // }
    //
    // [PunRPC]
    // protected void RPC_ApplyDamage(float _damage, Vector3 _hitNormal)
    // {
    //     lastHitNormal = _hitNormal;
    //     curHp -= _damage;
    //     Debug.Log($"RPC 데미지 적용! 현재 체력: {curHp}");
    //     if (curHp <= 0 && !isDestroyed)
    //     {
    //         Debug.Log($"{gameObject.name} 체력 0 이하! 파괴됩니다!");
    //         OnBroken();
    //     }
    // }
    // ────────────────────────────────────────────────────────────

    // IAttackReceiver 구현 → 매니저에 위임
    public virtual void OnReceiveImpact(ImpactData _data)
    {
        if (isDestroyed) return;
        if (FurnitureNetworkManager.Instance == null) return;
        FurnitureNetworkManager.Instance.ReportHit(_index, _data.damage, _data.hitNormal);
    }

    // 매니저 RPC가 모든 클라이언트에서 호출
    public void LocalApplyDamage(float damage, Vector3 hitNormal)
    {
        if (isDestroyed) return;
        lastHitNormal = hitNormal;
        curHp -= damage;
        if (curHp <= 0)
        {
            OnBroken();
            // MasterClient가 RoomProperties 업데이트 (늦참 동기화용)
            FurnitureNetworkManager.Instance?.ReportDestroyed(_index);
        }
    }

    // 늦참 클라이언트 상태 복원용 (비주얼/사운드 없이 상태만)
    public void ForceBreak()
    {
        if (isDestroyed) return;
        lastHitNormal = Vector3.up;
        OnBroken();
    }

    protected virtual void OnBroken()
    {
        isDestroyed = true;

        

        if (audioSource != null)
            audioSource.Play();

        Item item = GetComponent<Item>();
        if (item != null)
        {
            Debug.Log("SetGhostFurniture");
            item.SetBrokenState(isDestroyed);
        }
        Obstacle obstacle = GetComponent<Obstacle>();
        if(obstacle != null)
        {
            obstacle.Broken();
        }
        // 부서진 후 물리 판정 제거
        //if (TryGetComponent<Collider>(out var col)) col.enabled = false;
        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
        {
            //if (!col.isTrigger)
            //{
            //    col.enabled = false;
            //}
            col.enabled = false;
        }

        // 고스트 콜라이더만 다시 활성화 (FOW가 고스트 감지에 사용)
        Item item2 = GetComponent<Item>();
        if (item2 != null && item2.ghostObject != null)
        {
            Collider ghostCol = item2.ghostObject.GetComponentInChildren<Collider>();
            if (ghostCol != null)
            {
                ghostCol.enabled = true;
                Debug.Log($"[Furniture] ghost collider 재활성화: {ghostCol.name}");
            }
        }
    }
}