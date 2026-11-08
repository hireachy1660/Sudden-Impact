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


    // Initialize (PlayerWeapon의 SetKnifeOwner·SetGunsOwner가 부른다)
    public virtual void SetOwner(int _actorNumber, int _team)
    {
        ownerActorNumber = _actorNumber;
        ownerTeam = _team;
    }

    // PlayerWeapon에서 호출하는 공격 명령 (추상 메서드로 강제). _aimPos = 발사 순간의 조준점(월드 좌표)
    // 반환 = 실제로 공격을 수행했는가. 쿨다운·탄약·단발 조건에 걸려 아무것도 안 했으면 false — 호출자는 이 값으로 연출(애니)을 보낸다
    public abstract bool Attack(Vector3 _aimPos, bool _isHeld = false);
}