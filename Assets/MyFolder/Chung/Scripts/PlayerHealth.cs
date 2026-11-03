using Photon.Pun;
using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

// 체력·피격·기절 부품. 모든 클라이언트에서 돌며 TakeDamage·StunRPC의 입구다.
// 사망의 "때"(hp가 0)만 알고 OnDied로 알린다. 후속 조치는 이를 구독한 Core가 순서를 가진다.
public class PlayerHealth : MonoBehaviourPun, IAttackReceiver, IPlayerPart
{
    [Tooltip("혈흔 프리팹")]
    [SerializeField] private GameObject neonBloodPrefab;

    [Header("Parameters")]
    [SerializeField] private float maxHp = 100f;
    [SerializeField] private float stunDuration = 1.5f;

    [Header("Camera Shake (Damage)")]
    [SerializeField] private CinemachineImpulseSource damageImpulseSource;
    [SerializeField] private float damageShakeForce = 1.5f;

    [Header("ForDebug")]
    [SerializeField] private float curHp;

    private PlayerController core;

    // 애니메이터에게 피격·기절을 알리는 이벤트
    public event Action OnHitEvent;
    public event Action OnStunnedEvent;
    // hp가 0이 된 순간을 알린다(인자 = 공격자 ActorNumber). 발화는 이 부품만, 밖에서는 듣기만.
    public event Action<int> OnDied;

    private void Awake()
    {
        core = GetComponent<PlayerController>();
    }

    private void OnEnable()
    {
        curHp = maxHp;
    }

    public void OnRespawn()
    {
        curHp = maxHp;
        GameEvents.HpChanged(curHp);
    }

    public void OnRoundEndReset()
    {
        // 라운드 종료 순간 기절 중이었다면 코루틴이 다음 라운드로 넘어가지 않게 멈춘다
        StopAllCoroutines();
    }

    public void OnReceiveImpact(ImpactData _data)
    {
        // 상태 검사
        if (
            curHp <= 0
            || core.GetPlayerState == PlayerController.PlayerState.NotReady
            || core.GetPlayerState == PlayerController.PlayerState.Dead
            || core.GetPlayerState == PlayerController.PlayerState.Rolling
            || _data.attackerTeam == core.MyTeam
            ) { return; }

        if (_data.type == DamageType.Throw)
        {
            StunPlayer();
            return;
        }

        //내가 쏜 총알이 아니면 데미지 RPC호출
        if (_data.attackerActorNumber != photonView.Owner.ActorNumber)
        {
            photonView.RPC(nameof(TakeDamage), RpcTarget.All, _data.damage, _data.hitNormal, _data.attackerActorNumber);
        }
    }

    [PunRPC]
    private void TakeDamage(float _damage, Vector3 _hitNormal, int _attackerNum)
    {
        curHp -= _damage;
        OnHitEvent?.Invoke();

        // 피격 카메라 쉐이크
        if (photonView.IsMine && damageImpulseSource != null)
        {
            GameEvents.HpChanged(curHp);
            if (damageImpulseSource != null)
            {
                Vector3 shakeDir = -_hitNormal;
                shakeDir.y = 0f;
                if (shakeDir.sqrMagnitude < 0.01f) shakeDir = Vector3.back;
                { damageImpulseSource.GenerateImpulseWithVelocity(shakeDir.normalized * damageShakeForce); }
            }
        }

        Vector3 bloodPos = new Vector3(transform.position.x, -1.35f, transform.position.z);
        Instantiate(neonBloodPrefab, bloodPos, Quaternion.identity);

        if (curHp <= 0)
        {
            OnDied?.Invoke(_attackerNum);
        }
    }

    private void StunPlayer()
    {
        photonView.RPC(nameof(StunRPC), RpcTarget.All);
    }

    [PunRPC]
    private void StunRPC()
    {
        if (!gameObject.activeSelf) return;
        StartCoroutine(StunCoroutine());
        OnStunnedEvent?.Invoke();
    }

    private IEnumerator StunCoroutine()
    {
        core.SetPlayerState(PlayerController.PlayerState.Stunned);
        yield return new WaitForSeconds(stunDuration);
        core.SetPlayerState(PlayerController.PlayerState.Idle);
    }
}
