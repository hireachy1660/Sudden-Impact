using UnityEngine;
using Photon.Pun;

public class Projectile : MonoBehaviourPun, IPunInstantiateMagicCallback
{
    [SerializeField] protected float speed = 100f;
    protected float damage;
    protected int attackActorNum;
    protected int team;
    bool hasHit = false;

    protected Rigidbody rb;

    [SerializeField]
    protected DamageType damageType;
    [SerializeField]
    private LayerMask hitMask;
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

    private void FixedUpdate()
    {
        // 중복 호출 방지
        if (hasHit) return;

        if (Physics.SphereCast(transform.position,0.2f, transform.forward, out RaycastHit hit, speed*Time.fixedDeltaTime, hitMask))
        {
            hasHit = true;
            rb.MovePosition(hit.point);
            HandleHit(hit);
            return;
        }

        rb.MovePosition(transform.position + transform.forward * speed * Time.fixedDeltaTime);
    }

    private void HandleHit(RaycastHit hitInfo)
    {
        // 맞았는지의 검사는 공격을 한 클라이언트에서 실행
        if (!photonView.IsMine) return;


        // 이 대상이 내가 부딪혀야 할 대상(리시버 or 장애물)인지 확인
        bool isReceiver = hitInfo.transform.TryGetComponent<IAttackReceiver>(out var receiver);
        bool isObstacle = !isReceiver;



        HitEffectManager.Instance.SpawnEffect(hitInfo.point, hitInfo.normal);


        // 데미지 처리는 리시버인 경우에만 수행
        if (isReceiver)
        {
            ImpactData data = new ImpactData
            {
                damage = damage,
                attackerActorNumber = attackActorNum,
                attackerTeam = team,
                type = damageType,
                hitPoint = hitInfo.point,
                hitNormal = hitInfo.normal
            };
            receiver.OnReceiveImpact(data);
        }

        // 최종적으로 총알 파괴 (벽이든 사람이든 무조건 부서짐)
        PhotonNetwork.Destroy(gameObject);
    }

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