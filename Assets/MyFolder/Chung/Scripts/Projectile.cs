using UnityEngine;
using Photon.Pun;

public class Projectile : MonoBehaviourPun, IPunInstantiateMagicCallback
{
    [SerializeField] protected float speed = 100f;
    protected float damage;
    protected int attackActorNum;
    protected int team;

    protected Rigidbody rb;

    [SerializeField]
    protected DamageType damageType;
    [SerializeField]
    private LayerMask obstacleLayer;
    [SerializeField]
    private BulletTrail bulletTrail;

    protected virtual void Awake()
    {
        
        rb = GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        bulletTrail.OnSpawnFromPool();
    }

    private void OnDisable()
    {
        bulletTrail.OnReturnToPool();
    }

    protected virtual void Update()
    {
        rb.MovePosition(transform.position + transform.forward * speed * Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if(Physics.SphereCast(transform.position,0.2f, transform.forward, out RaycastHit hit, 100f*Time.fixedDeltaTime))
        {
            HandleHit(hit.collider);
        }
    }

    private void HandleHit(Collider other)
    {
        // 맞았는지의 검사는 공격을 한 클라이언트에서 실행
        if (!photonView.IsMine) return;

        // 이 대상이 내가 부딪혀야 할 대상(리시버 or 장애물)인지 확인
        bool isReceiver = other.TryGetComponent<IAttackReceiver>(out var receiver);
        bool isObstacle = ((1 << other.gameObject.layer) & obstacleLayer) != 0;

        // 부딪히는 대상이 아니면 무시 (트리거 통과)
        if (!isReceiver && !isObstacle) return;

        // 충돌 히트포인트 계산 (non-convex MeshCollider는 ClosestPoint 미지원 → transform.position 폴백)
        // Vector3 hitPoint = other.ClosestPoint(transform.position);
        MeshCollider mc = other as MeshCollider;
        Vector3 hitPoint = (mc != null && !mc.convex)
            ? transform.position
            : other.ClosestPoint(transform.position);
        Vector3 hitNormal = -transform.forward;

        HitEffectManager.Instance.SpawnEffect(hitPoint, hitNormal);


        // 데미지 처리는 리시버인 경우에만 수행
        if (isReceiver)
        {
            ImpactData data = new ImpactData
            {
                damage = damage,
                attackerActorNumber = attackActorNum,
                attackerTeam = team,
                type = damageType,
                hitPoint = hitPoint,
                hitNormal = hitNormal
            };
            receiver.OnReceiveImpact(data);
        }

        // 최종적으로 총알 파괴 (벽이든 사람이든 무조건 부서짐)
        PhotonNetwork.Destroy(gameObject);
    }

    //protected virtual void OnTriggerEnter(Collider other)
    //{
    //    // 맞았는지의 검사는 공격을 한 클라이언트에서 실행
    //    if (!photonView.IsMine) return;

    //    // 이 대상이 내가 부딪혀야 할 대상(리시버 or 장애물)인지 확인
    //    bool isReceiver = other.TryGetComponent<IAttackReceiver>(out var receiver);
    //    bool isObstacle = ((1 << other.gameObject.layer) & obstacleLayer) != 0;

    //    // 부딪히는 대상이 아니면 무시 (트리거 통과)
    //    if (!isReceiver && !isObstacle) return;

    //    // 충돌 히트포인트 계산 (non-convex MeshCollider는 ClosestPoint 미지원 → transform.position 폴백)
    //    // Vector3 hitPoint = other.ClosestPoint(transform.position);
    //    MeshCollider mc = other as MeshCollider;
    //    Vector3 hitPoint = (mc != null && !mc.convex)
    //        ? transform.position
    //        : other.ClosestPoint(transform.position);
    //    Vector3 hitNormal = -transform.forward;

    //    HitEffectManager.Instance.SpawnEffect(hitPoint, hitNormal);


    //    // 데미지 처리는 리시버인 경우에만 수행
    //    if (isReceiver)
    //    {
    //        ImpactData data = new ImpactData
    //        {
    //            damage = damage,
    //            attackerActorNumber = attackActorNum,
    //            attackerTeam = team,
    //            type = damageType,
    //            hitPoint = hitPoint,
    //            hitNormal = hitNormal
    //        };
    //        receiver.OnReceiveImpact(data);
    //    }

    //    // 최종적으로 총알 파괴 (벽이든 사람이든 무조건 부서짐)
    //    PhotonNetwork.Destroy(gameObject);
    //}

    public void OnPhotonInstantiate(PhotonMessageInfo info)
    {
        object[] data = info.photonView.InstantiationData;

        // 데이터가 제대로 들어왔는지 방어 코드
        if (data != null && data.Length >= 3)
        {
            // object로 넘어오므로 원래 타입으로 캐스팅(Unboxing)
            attackActorNum = (int)data[0];
            team = (int)data[1];
            damage = (float)data[2];

            Debug.Log($"[Projectile] 스폰 동기화 완료! 공격자: {attackActorNum}, 팀: {team}, 데미지: {damage}");
        }
    }

    // 모든 사람의 화면에서 이펙트를 띄우기 위한 RPC
    [PunRPC]
    public void RPC_PlayHitEffect(Vector3 pos, Vector3 normal)
    {
        if (HitEffectManager.Instance != null)
        {
            HitEffectManager.Instance.PlayHitEffect(pos, normal); 
        }
    }
}