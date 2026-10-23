using UnityEngine;
using Photon.Pun;

public abstract class Weapon : MonoBehaviourPun
{
    public enum EWeaponType
    {
        Knife = 0,
        Pistol = 1,
        Uzi = 2,
        Shotgun = 3,
        Rifle = 4
    }

    [Header("Weapon Settings")]
    [SerializeField] protected EWeaponType weaponType;

    [Header("Weapon Status")]
    [SerializeField] protected float damage = 10f;

    [Tooltip("총구 또는 칼의 공격 지점")]
    [SerializeField] protected Transform attackPoint;

    // 소유자 정보
    protected int ownerActorNumber;
    protected int ownerTeam;

    public Transform AttackPoint { get { return attackPoint; } }

    // 외부(PlayerAnimator)에서 읽어갈 수 있도록 프로퍼티 개방
    public EWeaponType WeaponType => weaponType;


    // Initialize (PlayerController의 SetOwner와 연결됨)
    public virtual void SetOwner(int _actorNumber, int _team)
    {
        ownerActorNumber = _actorNumber;
        ownerTeam = _team;
    }

    // PlayerController에서 호출하는 공격 명령 (추상 메서드로 강제)
    public abstract void Attack(bool isHeld = false);
}