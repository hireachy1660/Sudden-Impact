using Photon.Pun;
using System;
using UnityEngine;

/// <summary>
/// 무기 부품: 장착한 총과 칼의 상태, 장착·전환·공격·던지기.
/// 모든 클라이언트에 남는다(IOwnerOnlyPart가 아니다) — 무기 RPC를 받는 쪽이고, 원격의 애니메이터도 UseGun·MyEquippedGun을 읽는다.
/// 자격 가드(CanAct·달리기·구르기 중 금지)는 Core 중계가 가진다. Core에서 읽는 것은 조준점(AimPosition)만.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerWeapon : MonoBehaviourPun, IPlayerPart
{
    [Header("Weapon Reference")]
    [SerializeField] private Weapon myKnife;

    [Header("Pre-Allocated Weapons")]
    [Tooltip("미리 모든 종류의 총을 등록")]
    [SerializeField] private Gun[] allGuns;

    [Header("ForDebug")]
    [SerializeField] private Gun myEquippedGun;
    [SerializeField] private bool useGun;

    private PlayerController core;

    public bool UseGun => useGun;
    public Gun MyEquippedGun { get { return myEquippedGun; } }

    // 애니메이터에게 행동을 알리는 이벤트
    public event Action OnAttackEvent;
    public event Action OnThrowEvent;
    // 장착이 도착한 직후 Q 잠금을 Core가 걸도록 알리는 이벤트(소유자에서만 발화)
    public event Action OnEquipped;

    public void Bind(PlayerController _core) => core = _core;

    // Core.OnEnable에 있던 무기 초기화. 직렬화된 참조만 읽어서 Core와 실행 순서가 달라도 된다.
    private void OnEnable()
    {
        GameEvents.WeaponChanged(myKnife.WeaponType.ToString(), false);

        if (PhotonNetwork.IsMasterClient)
        {
            WeaponSpawnManager.Instance.RegisterGunCatalog(allGuns);
        }
    }

    // 소유자 정보는 Core가 넣어 준다 — 팀 값을 아는 쪽이 Core다(칼은 Core.Awake에서, 총은 스폰 때 Core.Init에서)
    /// <param name="_actorNumber">소유자의 Photon ActorNumber</param>
    /// <param name="_team">팀 번호(1 = A, 그 외 = B)</param>
    public void SetKnifeOwner(int _actorNumber, int _team)
    {
        myKnife.SetOwner(_actorNumber, _team);
    }

    /// <param name="_actorNumber">소유자의 Photon ActorNumber</param>
    /// <param name="_team">팀 번호(1 = A, 그 외 = B)</param>
    public void SetGunsOwner(int _actorNumber, int _team)
    {
        if (allGuns != null)
        {
            foreach (var gun in allGuns)
                if (gun != null) gun.SetOwner(_actorNumber, _team);
        }
    }

    public void OnRespawn() { }

    // 라운드 종료 시 들고 있던 총을 치우고 칼로 돌아간다(전에는 Core.OnRoundEndReset이 직접 부르던 것)
    public void OnRoundEndReset()
    {
        CleanupWeapon();
    }

    #region 무기 전환
    public void TrySwapWeapon()
    {
        // useGun은 여기서 쓰지 않는다 — 보낼 값만 정하고, 실제 갱신은 모든 클라이언트의 SwapWeapon이 한다
        bool nextUseGun = myEquippedGun != null && !useGun;
        photonView.RPC(nameof(SwapWeapon), RpcTarget.All, nextUseGun);
    }

    [PunRPC]
    private void SwapWeapon(bool _useGun)
    {
        // 전환 상태를 받는 쪽 모두가 갱신한다 — 원격의 애니메이터도 이 값으로 무기 자세를 정한다.
        // 총이 없는데 true가 오면 칼로 둔다.
        useGun = _useGun && myEquippedGun != null;

        if (myEquippedGun != null)
        {
            myEquippedGun.gameObject.SetActive(useGun);
        }
        myKnife.gameObject.SetActive(!useGun);

        if (photonView.IsMine)
        {
            string curWeaponName = useGun ? myEquippedGun.WeaponType.ToString() : myKnife.WeaponType.ToString();
            GameEvents.WeaponChanged(curWeaponName, useGun);
        }
    }
    #endregion

    public void TryAttack(Vector3 _aimPos, bool _isHeld = false)
    {
        if (useGun)
        {
            myEquippedGun.Attack(_aimPos, _isHeld);
        }
        else
        {
            // 쿨다운은 칼이 가진다(총의 fireRate처럼). 실제로 휘둘렀을 때만 애니를 보낸다 —
            // 근접 공격 애니는 원격에도 보여야 하므로 RPC 안에서 발화한다(총 연출은 Gun이 자체 RPC로 처리)
            if (myKnife.Attack(_aimPos, _isHeld))
            {
                photonView.RPC(nameof(AttackAnimRPC), RpcTarget.All);
            }
        }
    }

    [PunRPC]
    private void AttackAnimRPC()
    {
        OnAttackEvent?.Invoke();
    }

    #region 무기 장착·던지기

    // 사망처럼 총을 잃고 끝나는 경로 — 총을 놓은 뒤 칼을 켠다. 사망 순서를 가진 Core의 Die가 부른다.
    public void DropWeapon()
    {
        if (myEquippedGun == null) return;

        ReleaseGun();

        // 장착한 총이 없으면 칼이 켜져 있어야 한다 — 총이 없어지는 경로는 모두 칼을 켠다
        SwapWeapon(false);
    }

    // 장착한 총을 놓는다: 소유자가 드롭 노드를 요청하고 총 오브젝트를 끈다.
    // 칼은 켜지 않는다 — 곧바로 다른 총을 장착하는 교체 경로에서 무기 변경 이벤트(HUD·전환음)가 두 번 나가지 않게 따로 뗐다.
    private void ReleaseGun()
    {
        if (photonView.IsMine)
        {
            int type = (int)myEquippedGun.WeaponType;
            int ammo = myEquippedGun.CurrentAmmo;
            Vector3 dropPos = transform.position;

            WeaponSpawnManager.Instance?.photonView?.RPC(
                nameof(WeaponSpawnManager.RPC_CreateDropNode),
                RpcTarget.MasterClient,
                dropPos, type, ammo);
        }

        myEquippedGun.gameObject.SetActive(false);
        myEquippedGun = null;
        useGun = false;
    }

    /// <param name="typeInt">장착할 총의 Weapon.EWeaponType을 int로</param>
    /// <param name="ammo">노드에 남아 있던 잔탄 — 총의 현재 탄약으로</param>
    [PunRPC]
    public void RPC_ForceEquipWeapon(int typeInt, int ammo)
    {
        Weapon.EWeaponType type = (Weapon.EWeaponType)typeInt;

        // 교체: 들고 있던 총만 놓는다. 칼은 켜지 않고 바로 새 총을 장착한다
        if (myEquippedGun != null) ReleaseGun();

        Gun newGun = FindGunByType(type);
        if (newGun != null)
        {
            myEquippedGun = newGun;
            newGun.SetAmmo(ammo);
            // 켜기·useGun 갱신·HUD 이벤트는 SwapWeapon이 한다.
            // 이 RPC는 이미 모든 클라이언트에서 실행 중이다 — 여기서 다시 방송하면 전환이 클라이언트 수만큼 반복된다
            SwapWeapon(true);
        }

        // 장착 직후 Q 연타로 발밑 노드를 즉시 줍는 루프 방지 — Q 잠금은 Core가 이 알림을 받아 건다
        if (photonView.IsMine) OnEquipped?.Invoke();
    }

    // Q 키의 던지기 입구(소유자). 노드 줍기가 먼저인지는 Core가 가린 뒤 여기로 온다.
    public void TryThrow()
    {
        if (!photonView.IsMine) return;
        if (myEquippedGun == null || !useGun) return;

        photonView.RPC(nameof(RPC_TryThrow), RpcTarget.All);
    }

    [PunRPC]
    public void RPC_TryThrow()
    {
        if (myEquippedGun == null) return;

        if (photonView.IsMine)
        {
            myEquippedGun.ThrowWeapon(core.AimPosition);
        }
        OnThrowEvent?.Invoke();

        CleanupWeapon();
    }

    private void CleanupWeapon()
    {
        if (myEquippedGun == null) return;
        myEquippedGun.gameObject.SetActive(false);
        myEquippedGun = null;
        useGun = false;

        // DropWeapon과 같은 이유 — 총을 치운 뒤에는 칼을 켠다
        SwapWeapon(false);
    }

    private Gun FindGunByType(Weapon.EWeaponType type)
    {
        if (allGuns == null) return null;
        foreach (var gun in allGuns)
            if (gun != null && gun.WeaponType == type) return gun;
        return null;
    }

    #endregion
}
