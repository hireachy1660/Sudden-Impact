using UnityEngine;
using Photon.Pun;
using Unity.Cinemachine;
using System.Collections;

public abstract class Gun : Weapon
{
    [Header("Gun References")]
    [SerializeField] protected GameObject projectilePrefab;     // 쏘는 총알 프리팹
    [SerializeField] protected GameObject thrownWeaponPrefab; // 던지는 총(무기) 프리팹
    [SerializeField] private CinemachineImpulseSource impulseSource;
    [SerializeField] private GameObject ripple;

    [Header("Parameter")]
    [SerializeField] protected int maxAmmo = 30;
    [SerializeField] protected float fireRate = 0.1f;         // 연사 속도 

    [Header("MuzzleFlash")]
    [SerializeField] private Light flashLight;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip fireClip;
    [SerializeField][Range(0f, 1f)] private float fireVolume = 1f;

    [Header("Ripple Setting")]
    [Tooltip("리플 생성 시간 최소 간격")]
    [SerializeField] private float rippleCooldown = 0.3f;
    [Tooltip("해당 거리 이상일 때 리플 생성")]
    [SerializeField] private float rippleMinDistance = 8f;

    [Tooltip("체크 시 꾹 누르면 연사, 해제 시 클릭마다 단발")]
    public bool isAutomatic = true;                           // 단발/연사 구분용 스위치 (PlayerController에서 읽음)

    protected int currentAmmo;
    protected float lastFireTime;                             // 마지막으로 총을 쏜 시간을 기억하는 변수
    private float lastRippleTime;
    private Coroutine flashCoroutine;

    public int CurrentAmmo => currentAmmo;
    public int MaxAmmo => maxAmmo;

    protected virtual void OnEnable()
    {
        //currentAmmo = maxAmmo; 총을 꺼낼 때 장탄수 채우기
        lastFireTime = 0f;     // 무기를 꺼내자마자 바로 쏠 수 있도록 타이머 초기화
    }

    public override void SetOwner(int _actorNumber, int _team)
    {
        base.SetOwner(_actorNumber, _team);

        GameEvents.AmmoChanged(currentAmmo, maxAmmo);
    }
    public void SetAmmo(int ammo)
    {
        Debug.Log($"[Gun]{gameObject.name}'s SetAmmo Called, Ammo Is {ammo}");
        currentAmmo = Mathf.Clamp(ammo, 0, maxAmmo);
        if (ownerActorNumber == PhotonNetwork.LocalPlayer.ActorNumber)
            GameEvents.AmmoChanged(currentAmmo, maxAmmo);
    }

    public override void Attack(Vector3 _aimPos, bool _isHeld = false)
    {
        // 단발 무기 && 마우스를 꾹 누르고 있는(Hold) 호출이면 무시
        if (!isAutomatic && _isHeld) return;

        // 쿨다운 및 잔탄 확인 공통로직
        if (Time.time < lastFireTime + fireRate) return;

        if (currentAmmo <= 0)
        {
            Debug.Log("장탄수가 부족합니다! (재장전 필요)");
            return;
        }

        // 검사 통과 시간 갱신 및 탄약 소모
        lastFireTime = Time.time;
        currentAmmo--;

        if(ownerActorNumber == PhotonNetwork.LocalPlayer.ActorNumber)
        {
            GameEvents.AmmoChanged(currentAmmo, maxAmmo);
        }

        // 실제 탄환 생성 로직 — 방향은 쏘는 순간의 총구 위치와 조준점으로 여기서 정해 넘긴다
        FireProjectile(GetAimRotation(_aimPos));
        photonView.RPC(nameof(RPC_MuzzleFlash), RpcTarget.All);
        

        if (photonView.IsMine && impulseSource != null)
        {
            photonView.RPC(nameof(SpawnRipple), RpcTarget.All, transform.position, ownerTeam);
            if (impulseSource != null)
            {
                Vector3 baseVelocity = impulseSource.DefaultVelocity;
                Vector3 rotatedVelocity = transform.rotation * baseVelocity;
                impulseSource.GenerateImpulse(rotatedVelocity);
            }
        }
    }

    // 3. 자식 클래스들이 반드시 구현해야 하는 '순수 발사 로직'. 기본 회전은 Gun이 계산해 넘기고 파생은 탄퍼짐만 더한다
    protected abstract void FireProjectile(Quaternion _aimRotation);

    // PlayerWeapon에서 호출하는 무기 던지기 기능. _aimPos = 던지는 순간의 조준점(월드 좌표)
    public virtual void ThrowWeapon(Vector3 _aimPos)
    {
        if (thrownWeaponPrefab == null) return;

        // 포톤 콜백 사용
        object[] info = new object[]
        {
            ownerActorNumber, ownerTeam, 0f
        };

        // 총알과 같은 규칙 — 던지는 순간의 총구 위치에서 조준점을 향한 회전
        Quaternion throwRotation = GetAimRotation(_aimPos);

        PhotonNetwork.Instantiate(
            thrownWeaponPrefab.name,
            attackPoint.position,
            throwRotation,
            0,
            info
        );
    }

    // 조준점이 총구 바로 옆·뒤이거나 이 거리 안이면 몸 정면으로 쏜다(총구 → 조준점 벡터가 뒤집히는 것 방지)
    private const float MinAimDistance = 0.5f;

    // 발사 순간의 총구 위치에서 조준점을 향한 수평 회전.
    // 총구 Transform을 미리 돌려 두는 대신(전에는 PlayerMovement의 LookAt — 애니메이션이 손을 움직이면 틀어짐) 쏘는 순간의 데이터로 계산한다.
    private Quaternion GetAimRotation(Vector3 _aimPos)
    {
        // 플레이어 루트의 정면 — 총은 플레이어 프리팹의 자식이라 root가 플레이어다(MeleeKnife도 transform.root로 자기 몸을 가린다)
        Vector3 bodyForward = transform.root.forward;
        bodyForward.y = 0f;

        Vector3 dir = _aimPos - attackPoint.position;
        dir.y = 0f;

        if (dir.sqrMagnitude < MinAimDistance * MinAimDistance || Vector3.Dot(dir, bodyForward) <= 0f)
        {
            dir = bodyForward;
        }

        return Quaternion.LookRotation(dir);
    }

    [PunRPC]
    public void RPC_MuzzleFlash()
    {
        if (audioSource != null && fireClip != null)
            audioSource.PlayOneShot(fireClip, fireVolume);
        if(flashCoroutine == null)
        flashCoroutine = StartCoroutine(FlashCoroutine());
    }

    private IEnumerator FlashCoroutine()
    {
        //  매번 빛의 세기(Intensity)와 크기를 랜덤으로
        if (flashLight == null) yield break; 
            
        flashLight.intensity = Random.Range(10f, 20f); // 라이트 밝기 변화
        flashLight.range = Random.Range(7f, 13f);

        // 켜기
        if (flashLight != null) flashLight.enabled = true;

        // 타이밍은 0.02초 ~ 0.04초 동안 켜지게
        yield return new WaitForSeconds(0.03f);

        // 끄기
        if (flashLight != null) flashLight.enabled = false;
    }

    [PunRPC]
    public void SpawnRipple(Vector3 _pos, int _spawnTeam)
    {
        // 같은 팀이면 스킵
        if (_spawnTeam == (int)PhotonNetwork.LocalPlayer.CustomProperties["Team"]) return;

        // 쿨다운: 같은 총에서 너무 자주 생성 방지
        if (Time.time - lastRippleTime < rippleCooldown) return;

        // 거리 체크: 너무 가까우면 직접 보이는 상황이므로 스킵 (XZ 평면 기준)
        Vector3 camPos = Camera.main.transform.position;
        float distSqr = (_pos.x - camPos.x) * (_pos.x - camPos.x)
                      + (_pos.z - camPos.z) * (_pos.z - camPos.z);
        if (distSqr < rippleMinDistance * rippleMinDistance) return;

        lastRippleTime = Time.time;
        Instantiate(ripple, _pos, Quaternion.identity);
    }
}