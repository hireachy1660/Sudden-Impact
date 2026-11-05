using UnityEngine;
using Photon.Pun;
using System.Collections;

[RequireComponent(typeof(Animator))]
public class PlayerAnimator : MonoBehaviourPun
{
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerController playerController;
    private PlayerHealth health;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip[] footstepClips;
    [SerializeField] private AudioClip[] runFootstepClips;
    [SerializeField] private AudioClip rollClip;
    [SerializeField] private AudioClip stunClip;
    [SerializeField] private AudioClip deathClip;
    [SerializeField] private AudioClip weaponSwapClip;

    [Header("Audio Volumes")]
    [SerializeField][Range(0f, 1f)] private float footstepVolume = 0.6f;
    [SerializeField][Range(0f, 1f)] private float runFootstepVolume = 0.8f;
    [SerializeField] private float runThreshold = 1.2f;
    [SerializeField][Range(0f, 1f)] private float rollVolume = 0.8f;
    [SerializeField][Range(0f, 1f)] private float stunVolume = 1.0f;
    [SerializeField][Range(0f, 1f)] private float deathVolume = 1.0f;
    [SerializeField][Range(0f, 1f)] private float weaponSwapVolume = 0.7f;

    [Header("Stun Visual")]
    [SerializeField] private Renderer[] bodyRenderers;
    [Tooltip("히트 플래시/스턴에서 제외할 렌더러 (완장 등)")]
    [SerializeField] private Renderer[] excludeFromFlash;
    [SerializeField] private Color stunColor = new Color(0.4f, 0.6f, 1f);
    [SerializeField][Range(0.05f, 0.3f)] private float blinkInterval = 0.12f;

    [Header("Hit Flash Visual")]
    [SerializeField][Range(0.05f, 0.3f)] private float hitFlashDuration = 0.1f;

    private Color _originalColor;
    private MaterialPropertyBlock _mpb;
    private Coroutine _stunBlinkCoroutine;
    private Coroutine _hitFlashCoroutine;
    private static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int s_Color = Shader.PropertyToID("_Color");
    private static readonly int s_FlashIntensity = Shader.PropertyToID("_FlashIntensity");

    [Header("Animator Hashes")]
    private readonly int hashVelocityX = Animator.StringToHash("VelocityX");
    private readonly int hashVelocityZ = Animator.StringToHash("VelocityZ");
    private readonly int hashWeaponType = Animator.StringToHash("WeaponType");

    private readonly int hashAction = Animator.StringToHash("Action");
    private readonly int hashThrow = Animator.StringToHash("Throw");
    private readonly int hashInteract = Animator.StringToHash("Interact");
    private readonly int hashRoll = Animator.StringToHash("Roll");
    private readonly int hashStun = Animator.StringToHash("Stun");
    private readonly int hashDead = Animator.StringToHash("Dead");
    private readonly int hashSpeed = Animator.StringToHash("Speed");
    private readonly int hashIsDead = Animator.StringToHash("IsDead");

    private float lastFootstepTime = 0f;
    private bool _wasDead; // 사망 사운드 1회 재생용 플래그

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (playerController == null) playerController = GetComponent<PlayerController>();
        // 이 컴포넌트는 모델(자식) 오브젝트에 붙어 있어 컨트롤러가 있는 오브젝트에서 가져온다
        health = playerController.GetComponent<PlayerHealth>();

        _mpb = new MaterialPropertyBlock();
        if (bodyRenderers == null || bodyRenderers.Length == 0)
        {
            // 자동 수집 후 excludeFromFlash에 있는 렌더러 제외
            var all = GetComponentsInChildren<Renderer>();
            if (excludeFromFlash != null && excludeFromFlash.Length > 0)
            {
                var excludeSet = new System.Collections.Generic.HashSet<Renderer>(excludeFromFlash);
                var filtered = new System.Collections.Generic.List<Renderer>();
                foreach (var r in all)
                    if (!excludeSet.Contains(r)) filtered.Add(r);
                bodyRenderers = filtered.ToArray();
            }
            else
            {
                bodyRenderers = all;
            }
        }
        if (bodyRenderers.Length > 0)
        {
            var mat = bodyRenderers[0].sharedMaterial;
            if (mat.HasProperty(s_BaseColor)) _originalColor = mat.GetColor(s_BaseColor);
            else if (mat.HasProperty(s_Color)) _originalColor = mat.GetColor(s_Color);
            else _originalColor = Color.white;
        }
    }

    private void OnEnable()
    {
        // 이벤트 구독 (컨트롤러에서 행동이 발생하면 자동으로 트리거 함수 호출)
        playerController.OnAttackEvent += TriggerAction;
        playerController.OnRollEvent += TriggerRoll;
        playerController.OnThrowEvent += TriggerThrow;
        playerController.OnInteractEvent += TriggerInteract;
        health.OnStunnedEvent += TriggerStun;
        health.OnHitEvent += TriggerHitFlash;

        // 초기화
        animator.SetLayerWeight(1, 1f);
        GameEvents.OnWeaponChanged += HandleWeaponChanged;
        GameEvents.OnRoundStart += HandleRoundStart;
    }

    private void OnDisable()
    {
        // 메모리 누수 방지 및 이벤트 구독 해제
        playerController.OnAttackEvent -= TriggerAction;
        playerController.OnRollEvent -= TriggerRoll;
        playerController.OnThrowEvent -= TriggerThrow;
        playerController.OnInteractEvent -= TriggerInteract;
        health.OnStunnedEvent -= TriggerStun;
        health.OnHitEvent -= TriggerHitFlash;
        GameEvents.OnWeaponChanged -= HandleWeaponChanged;
        GameEvents.OnRoundStart -= HandleRoundStart;
    }

    private void HandleRoundStart()
    {
        _wasDead = false;
        animator.SetBool(hashIsDead, false);
        animator.SetLayerWeight(1, 1f);
        animator.CrossFade("Locomotion", 0.1f, 0);
    }

    private void LateUpdate()
    {
        bool isDead = playerController.GetPlayerState == PlayerController.PlayerState.Dead;

        animator.SetBool(hashIsDead, isDead);

        if (isDead && !_wasDead)
        {
            //animator.SetTrigger(hashDead);
            //animator.SetLayerWeight(1, 0f);
            animator.CrossFade("Die", 0.1f, 0);
            PlayClip(deathClip);
            //StartCoroutine(SafeDisableUpperBodyLayer());
        }
        _wasDead = isDead;

        if (isDead) return;

        UpdateMovementAnimations();
        UpdateWeaponState();
    }

    private void UpdateMovementAnimations()
    {
        //  플레이어 벨로시티(방향) 기준으로 로컬 속도 변환
        Vector3 localVelocity = transform.InverseTransformDirection(playerController.CurrentVelocity)
                                / playerController.MoveSpeed;

        // Blend Tree
        animator.SetFloat(hashVelocityX, localVelocity.x);
        animator.SetFloat(hashVelocityZ, localVelocity.z);
        animator.SetFloat(hashSpeed, playerController.NormalizedSpeed);
    }

    private void UpdateWeaponState()
    {
        // 기본 무기 0 (칼/맨손)
        int currentWeaponType = 0;

        // 총을 들고 있고, 장착된 무기 오브젝트가 확실히 존재한다면
        if (playerController.UseGun && playerController.MyEquippedGun != null)
        {
            // 무기 Enum 타입을 int로 형변환
            currentWeaponType = (int)playerController.MyEquippedGun.WeaponType;
        }

        // 애니메이터로 파라미터 전송 (0, 1, 2, 3, 4가 자동으로 들어감)
        animator.SetInteger(hashWeaponType, currentWeaponType);
    }

    private IEnumerator RollCoroutine()
    {
        animator.SetLayerWeight(1, 0f);
        animator.SetTrigger(hashRoll);
        yield return new WaitForSeconds(0.2f);
        animator.SetLayerWeight(1, 1f);
    }

    // --- 애니메이션 트리거 전송 ---
    private void TriggerAction() => animator.SetTrigger(hashAction);
    private void TriggerRoll()
    {
        StartCoroutine(RollCoroutine());
        PlayClip(rollClip, rollVolume);
    }
    private void TriggerThrow() => animator.SetTrigger(hashThrow);
    private void TriggerInteract() => animator.SetTrigger(hashInteract);

    // 애니메이션 이벤트 -- 애니메이션 클립에서 호출
    public void PlayFootstep()
    {
        // 방금 소리가 났다면(0.2초 이내) 무시 (탭댄스 방어)
        if (Time.time - lastFootstepTime < 0.2f) return;

        AudioClip[] clips;
        float vol;

        float speed = playerController.NormalizedSpeed;
        if (speed >= runThreshold && runFootstepClips != null && runFootstepClips.Length > 0)
        {
            clips = runFootstepClips;
            vol = runFootstepVolume;
        }
        else
        {
            clips = footstepClips;
            vol = footstepVolume;
        }
        if (clips == null || clips.Length == 0) return;
        PlayClip(clips[Random.Range(0, clips.Length)], vol);

        lastFootstepTime = Time.time;
    }

    private void HandleWeaponChanged(string _name, bool _isGun)
    {
        if (!photonView.IsMine) return;
        PlayClip(weaponSwapClip, weaponSwapVolume);
    }

    private void PlayClip(AudioClip clip, float volume = 1f)
    {
        if (clip == null || audioSource == null) return;
        audioSource.PlayOneShot(clip, volume);
    }
    private void TriggerStun()
    {
        animator.SetTrigger(hashStun);
        PlayClip(stunClip, stunVolume);
        if (_stunBlinkCoroutine != null) StopCoroutine(_stunBlinkCoroutine);
        _stunBlinkCoroutine = StartCoroutine(StunBlinkCoroutine());
    }

    private void TriggerHitFlash()
    {
        if (_hitFlashCoroutine != null) StopCoroutine(_hitFlashCoroutine);
        _hitFlashCoroutine = StartCoroutine(HitFlashCoroutine());
    }

    private IEnumerator HitFlashCoroutine()
    {
        // _FlashIntensity 1 → 텍스처를 흰색으로 lerp (EnemyShader 전용 프로퍼티)
        SetFlash(1f);
        yield return new WaitForSeconds(hitFlashDuration);
        SetFlash(0f);
        _hitFlashCoroutine = null;
    }

    private void SetFlash(float intensity)
    {
        foreach (var r in bodyRenderers)
        {
            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(s_FlashIntensity, intensity);
            r.SetPropertyBlock(_mpb);
        }
    }

    private IEnumerator StunBlinkCoroutine()
    {
        Debug.Log("[PlayerAnimator]StartBlink");
        SetRenderersColor(stunColor);
        bool visible = true;
        while (playerController.GetPlayerState == PlayerController.PlayerState.Stunned)
        {
            visible = !visible;
            SetRenderersVisible(visible);
            yield return new WaitForSeconds(blinkInterval);
        }
        SetRenderersVisible(true);
        SetRenderersColor(_originalColor);
        _stunBlinkCoroutine = null;
    }

    private void SetRenderersColor(Color color)
    {
        foreach (var r in bodyRenderers)
        {
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(s_BaseColor, color); // URP Lit (_BaseColor)
            _mpb.SetColor(s_Color, color);     // Standard / 커스텀 셰이더 (_Color)
            r.SetPropertyBlock(_mpb);
        }
    }

    private void SetRenderersVisible(bool visible)
    {
        foreach (var r in bodyRenderers) r.enabled = visible;
    }
}
