using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using System.Collections;

/// <summary>
/// 플레이어 발밑에 표시되는 World Space UI
/// - 원형 체력 링 (Radial Fill Image)
/// - 무기 이름 / 남은 탄약 텍스트
///
/// [프리팹 설정]
/// 1. Player 프리팹 하위에 빈 오브젝트 "WorldUI" 추가
///    - Position: (0, 0.05, 0)  ← 바닥 위에 살짝 떠 있게
///    - Rotation: (-90, 0, 0)   ← Canvas가 위를 향하도록 눕힘
/// 2. WorldUI 하위에 Canvas 추가
///    - Render Mode: World Space
///    - RectTransform: W=1, H=1  (= 1미터 크기)
///    - Scale: (1, 1, 1)
/// 3. Canvas 하위에 Image 추가 → hpRingImage 슬롯에 연결
///    - Image Type: Filled
///    - Fill Method: Radial 360
///    - Fill Origin: Top (12시 방향 시작)
///    - 원형 테두리 스프라이트 사용 권장 (없으면 기본 흰 원도 OK)
/// 4. Canvas 하위에 TextMeshProUGUI 2개 추가 → weaponText / ammoText 슬롯 연결
/// </summary>
public class PlayerWorldUI : MonoBehaviourPun
{
    [Header("체력 링")]
    [SerializeField] private Image hpRingImage;
    [SerializeField] private Color hpFullColor = Color.green;
    [SerializeField] private Color hpLowColor = Color.red;
    [Tooltip("이 비율 이하로 떨어지면 빨간색으로 변함 (0~1)")]
    [SerializeField][Range(0f, 1f)] private float lowHpThreshold = 0.3f;

    [Header("구르기 쿨다운 링")]
    [SerializeField] private Image rollRingImage;
    [SerializeField] private Color rollReadyColor = Color.cyan;
    [SerializeField] private Color rollCooldownColor = Color.gray;

    private Coroutine _rollCooldownCoroutine;

    [Header("무기 / 탄약 텍스트")]
    [SerializeField] private TextMeshProUGUI weaponText;
    [SerializeField] private TextMeshProUGUI ammoText;
    [SerializeField] private GameObject ammoPanel; // 근접무기일 때 숨길 패널

    private float maxHp = 100f; // PlayerController와 맞춰서 수정

    private void Awake()
    {
        // 내 캐릭터가 아니면 WorldUI 전체를 숨김
        // (다른 플레이어 발밑에 링/텍스트가 보이지 않도록)
        if (!photonView.IsMine)
        {
            gameObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        // 이 플레이어가 내 캐릭터일 때만 이벤트 구독
        // (다른 플레이어 프리팹의 WorldUI는 구독하지 않음)
        if (!photonView.IsMine) return;

        GameEvents.OnHpChanged += HandleHpChanged;
        GameEvents.OnWeaponChanged += HandleWeaponChanged;
        GameEvents.OnAmmoChanged += HandleAmmoChanged;
        GameEvents.OnRoundStart += HandleRoundStart;
        GameEvents.OnRollStarted += HandleRollStarted;
    }

    private void OnDisable()
    {
        if (!photonView.IsMine) return;

        GameEvents.OnHpChanged -= HandleHpChanged;
        GameEvents.OnWeaponChanged -= HandleWeaponChanged;
        GameEvents.OnAmmoChanged -= HandleAmmoChanged;
        GameEvents.OnRoundStart -= HandleRoundStart;
        GameEvents.OnRollStarted -= HandleRollStarted;
    }

    private void LateUpdate()
    {
        // 캐릭터가 어떤 방향으로 돌아가도 WorldUI는 항상 바닥에 평평하게 고정
        // Euler(-90, 0, 0) = 캔버스 정면(+Z)이 위(+Y)를 향하도록 눕힘 → 탑다운 카메라에서 정상 표시
        transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
    }

    private void HandleHpChanged(float hp)
    {
        if (hpRingImage == null) return;

        float ratio = Mathf.Clamp01(hp / maxHp);
        hpRingImage.fillAmount = ratio;

        // 체력 비율에 따라 색상 변경: lowHpThreshold 이하면 빨강, 이상이면 초록
        hpRingImage.color = ratio <= lowHpThreshold ? hpLowColor : hpFullColor;
    }

    private void HandleWeaponChanged(string weaponName, bool isGun)
    {
        if (weaponText != null) weaponText.text = weaponName;
        if (ammoPanel != null) ammoPanel.SetActive(isGun);
    }

    private void HandleAmmoChanged(int cur, int max)
    {
        if (ammoText != null) ammoText.text = $"{cur}/{max}";
    }

    // 라운드 시작 시 체력/구르기 링 초기화
    private void HandleRoundStart()
    {
        if (hpRingImage != null)
        {
            hpRingImage.fillAmount = 1f;
            hpRingImage.color = hpFullColor;
        }

        // 라운드 시작 시 쿨다운 코루틴 중단 후 즉시 사용 가능 상태로
        if (_rollCooldownCoroutine != null)
        {
            StopCoroutine(_rollCooldownCoroutine);
            _rollCooldownCoroutine = null;
        }
        if (rollRingImage != null)
        {
            rollRingImage.fillAmount = 1f;
            rollRingImage.color = rollReadyColor;
        }
    }

    // 구르기 시작 → 쿨다운 링을 0에서 1까지 서서히 채움
    private void HandleRollStarted(float cooldownDuration)
    {
        if (rollRingImage == null) return;
        if (_rollCooldownCoroutine != null) StopCoroutine(_rollCooldownCoroutine);
        _rollCooldownCoroutine = StartCoroutine(RollCooldownCoroutine(cooldownDuration));
    }

    private IEnumerator RollCooldownCoroutine(float duration)
    {
        rollRingImage.fillAmount = 0f;
        rollRingImage.color = rollCooldownColor;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            rollRingImage.fillAmount = elapsed / duration;
            yield return null;
        }

        // 쿨다운 완료
        rollRingImage.fillAmount = 1f;
        rollRingImage.color = rollReadyColor;
        _rollCooldownCoroutine = null;
    }
}
