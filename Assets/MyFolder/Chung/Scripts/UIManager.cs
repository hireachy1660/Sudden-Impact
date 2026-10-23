using TMPro;
using UnityEngine;
using System.Collections;
using UnityEngine.UI;
/// <summary>
/// 전체 HUD 관리 매니저.
/// </summary>
public class UIManager : MonoBehaviour
{
    //
    [Header("HUD")]
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private TextMeshProUGUI weaponNameText;
    [SerializeField] private TextMeshProUGUI ammoText;
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private GameObject ammoPanel;

    // 팀 생존 슬롯 UI 
    [Header("Team Status UI")]
    [Tooltip("화면 좌측 최상단에 배치된 A팀 슬롯들 (4개)")]
    [SerializeField] private PlayerSlotUI[] teamASlots;

    [Tooltip("화면 우측 최상단에 배치된 B팀 슬롯들 (4개)")]
    [SerializeField] private PlayerSlotUI[] teamBSlots;

    // 킬 로그 UI
    [Header("Kill Log UI")]
    [Tooltip("Vertical Layout Group 부모 Transform")]
    [SerializeField] private Transform killLogParent;

    [Tooltip("KillLogEntry 컴포넌트가 붙은 UI 텍스트 프리팹")]
    [SerializeField] private KillLogEntry killLogEntryPrefab;

    [Header("Crosshair Settings")]
    [SerializeField] private float baseSize = 60f;        // 기본 에임 크기 (픽셀)
    [SerializeField] private float spreadSensitivity = 5f; // 1도당 벌어질 픽셀 거리 (중요!)
    [SerializeField] private RectTransform crosshairUI;
    [SerializeField] private Transform targetTransform; // InputManager의 mouseAimTarget

    [Header("Result UI")]
    [SerializeField] private GameObject resultPanel; // 승리/패배 패널
    [SerializeField] private TextMeshProUGUI resultText;

    [Header("Pickup UI")]
    [SerializeField] private RectTransform pickupUIRect; // 픽업 텍스트의 부모 RectTransform
    [SerializeField] private TextMeshProUGUI pickupUIText;

    [Header("Pause UI")]
    [SerializeField] private GameObject menuPanel; // ESC 메뉴 패널 연결
    [SerializeField] private CRTController crtController;
    private bool usingCRT = true;

    [Header("Round UI")]
    [SerializeField] private AudioClip endRoundAudio;
    private AudioSource audiosource;
    [SerializeField] private TextMeshProUGUI roundWinner;
    [SerializeField] private TextMeshProUGUI roundLeftTime;

    //  플레이어 발밑 추적 UI (Screen Space, WorldToScreenPoint로 위치 이동)
    [Header("Player Status UI (발밑 추적)")]
    [Tooltip("PlayerRegistry ScriptableObject - registry.LocalPlayer.transform으로 위치 추적")]
    [SerializeField] private PlayerRegistry registry;
    [Tooltip("HP링/구르기링/무기텍스트를 묶은 부모 RectTransform")]
    [SerializeField] private RectTransform playerStatusPanel;
    [Tooltip("체력 원형 링 Image (Filled, Radial 360)")]
    [SerializeField] private Image hpRingImage;
    [SerializeField] private Color hpFullColor = Color.green;
    [SerializeField] private Color hpLowColor = Color.red;
    [SerializeField][Range(0f, 1f)] private float lowHpThreshold = 0.3f;
    [Tooltip("구르기 쿨다운 원형 링 Image (Filled, Radial 360)")]
    [SerializeField] private Image rollRingImage;
    [SerializeField] private Color rollReadyColor = Color.cyan;
    [SerializeField] private Color rollCooldownColor = Color.gray;

    // [CH 추가] 라운드 시작 전 전체 화면을 가리는 커버 패널
    // 인스펙터에서 Canvas 가장 아래(최상위 렌더) 전체 사이즈 Panel을 연결
    // 기본값: 활성(SetActive true), 라운드 시작 시 SetActive(false)로 제거
    [Header("Initial Cover")]
    [SerializeField] private GameObject initialCoverPanel;

    private Coroutine _rollCooldownCoroutine;

    private Camera mainCam;
    private Coroutine crosshairCoroutine;
    private float currentSpread = 1f;   // UI가 현재 보여주는 부드러운 값
    private float lastRecRate = 15f;    // 가장 최근 총기에서 받은 회복 속도
    private float lastBaseSpread = 1f;  // 가장 최근 총기에서 받은 기본 크기

    private Vector3 targetPickupPos;
    private bool isPickupUIActive = false;

    

    // 라이프사이클
    private void Awake()
    {
        // 1. A팀 슬롯 전원 비활성화
        if (teamASlots != null)
        {
            foreach (var slot in teamASlots)
            {
                if (slot != null) slot.gameObject.SetActive(false);
            }
        }

        // 2. B팀 슬롯 전원 비활성화
        if (teamBSlots != null)
        {
            foreach (var slot in teamBSlots)
            {
                if (slot != null) slot.gameObject.SetActive(false);
            }
        }

        mainCam = Camera.main;
        // 이벤트 버스 구독: 무기가 발사될 때 크로스헤어 키우기

        //마우스의 비주얼을 끔
        Cursor.visible = false;

        // 마우스가 게임 창 밖으로 나가지 못하게 
        Cursor.lockState = CursorLockMode.Confined;

        pickupUIRect.gameObject.SetActive(false);
        if (playerStatusPanel != null) playerStatusPanel.gameObject.SetActive(false);

        audiosource = GetComponent<AudioSource>();
    }

    #region 이벤트 관리
    /// <summary>
    /// 이벤트 구독
    /// </summary>
    private void OnEnable()
    {

        GameEvents.OnHpChanged += HandleHpChanged;
        GameEvents.OnWeaponChanged += HandleWeaponChanged;
        GameEvents.OnRollStarted += HandleRollStarted;

        GameEvents.OnAmmoChanged += HandleAmmoChanged;
        GameEvents.OnScoreChanged += HandleScoreChanged;
        GameEvents.OnSpreadUpdated += HandleSpreadUpdate;
        GameEvents.OnPlayerUIInit += HandlePlayerUIInit;
        GameEvents.OnPlayerUIDead += HandlePlayerUIDead;
        GameEvents.OnKillLog += HandleKillLog;
        GameEvents.OnMatchEnd += HandleMatchEnd;
        GameEvents.OnPickupUIUpdate += HandlePickupUIUpdate;
        GameEvents.OnMenuUIUpdate += HandleMenuUI;
        GameEvents.OnRoundStart += HandleRoundStart;
        GameEvents.OnRoundEnd += HandleRoundEnd;
        GameEvents.OnLocalPlayerDeath += HandleLocalPlayerDead;

    }

    /// <summary>
    /// 이벤트 구독 해제
    /// </summary>
    private void OnDisable()
    {
        GameEvents.OnHpChanged -= HandleHpChanged;
        GameEvents.OnWeaponChanged -= HandleWeaponChanged;
        GameEvents.OnRollStarted += HandleRollStarted;

        GameEvents.OnAmmoChanged -= HandleAmmoChanged;
        GameEvents.OnScoreChanged -= HandleScoreChanged;

        GameEvents.OnPlayerUIInit -= HandlePlayerUIInit;
        GameEvents.OnPlayerUIDead -= HandlePlayerUIDead;
        GameEvents.OnKillLog -= HandleKillLog;
        GameEvents.OnMatchEnd -= HandleMatchEnd;
        GameEvents.OnPickupUIUpdate -= HandlePickupUIUpdate;
        GameEvents.OnMenuUIUpdate -= HandleMenuUI;
        GameEvents.OnRoundStart -= HandleRoundStart;
        GameEvents.OnRoundEnd -= HandleRoundEnd;
        GameEvents.OnLocalPlayerDeath -= HandleLocalPlayerDead;

    }
    #endregion

    private void Start()
    {
        StartCoroutine(StartCountDown());
    }
    private void Update()
    {
        // 회복 로직: 총기(AutomaticGun)의 MoveTowards와 동일한 수식 사용
        if (currentSpread > lastBaseSpread)
        {
            currentSpread = Mathf.MoveTowards(currentSpread, lastBaseSpread, lastRecRate * Time.deltaTime);
        }

        // 9-슬라이스 스케일 적용
        if (crosshairUI != null)
        {
            // 수정: 배율이 아니라 '더하기' 방식으로 거리 조절
            // 예: 탄퍼짐이 5도 늘어나면 에임 크기는 5 * 5 = 25픽셀만 더 커짐
            float addedSize = (currentSpread - lastBaseSpread) * spreadSensitivity;
            float targetUISize = baseSize + addedSize;

            // 9슬라이스가 작동하도록 sizeDelta를 수정
            crosshairUI.sizeDelta = new Vector2(targetUISize, targetUISize);

            // localScale은 무조건 (1,1,1)이어야 선이 안 굵어집니다!
            crosshairUI.localScale = Vector3.one;
        }
    }

    private void LateUpdate()
    {
        if (isPickupUIActive && pickupUIRect != null && mainCam != null)
        {
            // 총기 바로 위쪽에 띄우기 위해 Y축으로 살짝 올림 (+1.5f 등 조정)
            Vector3 offsetPos = targetPickupPos + new Vector3(0, 1.0f, 0);
            pickupUIRect.position = mainCam.WorldToScreenPoint(offsetPos);
        }

        // [CH 추가] playerStatusPanel을 로컬 플레이어 발밑 위치로 이동
        // registry.LocalPlayer는 PlayerController.Awake에서 등록됨 (스폰 전엔 null)
        if (registry != null && registry.LocalPlayer != null && playerStatusPanel != null)
        {
            playerStatusPanel.position = mainCam.WorldToScreenPoint(registry.LocalPlayer.transform.position);
        }

        if (targetTransform == null || crosshairUI == null) return;

        // 월드 좌표 -> 스크린 좌표 변환 후 UI 배치
        crosshairUI.position = mainCam.WorldToScreenPoint(targetTransform.position);


    }



    private void HandleSpreadUpdate(float _cur, float _rec, float _baseVal)
    {
        // 총을 쏘는 순간, 현재 UI의 퍼짐 정도를 총기의 실제 탄퍼짐 값으로 점프시킴
        currentSpread = _cur;
        lastRecRate = _rec;
        lastBaseSpread = _baseVal;
    }

    public void SetAimTarget(Transform _target)
    {
        targetTransform = _target;
    }
    private void HandleHpChanged(float _hp)
    {
        hpText.text = $"HP: {_hp} / 100";

        // [CH 추가] 체력 링 업데이트
        if (hpRingImage != null)
        {
            float ratio = Mathf.Clamp01(_hp / 100f);
            hpRingImage.fillAmount = ratio;
            hpRingImage.color = ratio <= lowHpThreshold ? hpLowColor : hpFullColor;
        }
    }

    // 구르기 쿨다운 링 애니메이션
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
        rollRingImage.fillAmount = 1f;
        rollRingImage.color = rollReadyColor;
        _rollCooldownCoroutine = null;
    }

    private void HandleAmmoChanged(int _cur, int _max)
        => ammoText.text = $"{_cur} / {_max}";

    private void HandleWeaponChanged(string _name, bool _isGun)
    {
        weaponNameText.text = _name;
        ammoPanel.SetActive(_isGun);
    }

    private void HandleScoreChanged(int _aTeam, int _bTeam)
        => scoreText.text = $"{_aTeam} : {_bTeam}";

    // 팀 슬롯 핸들러
    /// <summary>
    /// 플레이어 입장 시 빈 슬롯에 이름·팀 정보를 채우고 활성화
    /// </summary>
    private void HandlePlayerUIInit(int actorNumber, string playerName, int team)
    {
        // 이미 들어온 사람인지 확인 (재접속 방지)
        PlayerSlotUI slot = FindSlotByActor(actorNumber);

        // 처음 들어온 사람이면 팀에 맞는 빈 슬롯 찾기
        if (slot == null)
        {
            slot = FindEmptySlot(team);
        }

        if (slot == null)
        {
            Debug.LogWarning($"[UIManager] {team}팀의 남은 플레이어 슬롯이 없습니다!");
            return;
        }

        slot.gameObject.SetActive(true);
        slot.Init(actorNumber, playerName, team);
    }

    /// <summary>
    /// 플레이어 사망 시 해당 슬롯을 흑백/어둡게 처리 (SetActive false 아님)
    /// </summary>
    private void HandlePlayerUIDead(int actorNumber)
    {
        PlayerSlotUI slot = FindSlotByActor(actorNumber);
        slot?.SetDead();
    }
    // 킬 로그 핸들러
    /// <summary>
    /// 킬 발생 시 killLogParent 아래에 새 엔트리를 Instantiate.
    /// KillLogEntry 자체가 3초 표시 후 페이드 아웃 → 자기 자신을 Destroy.
    /// </summary>
    private void HandleKillLog(string killer, string victim)
    {
        if (killLogEntryPrefab == null || killLogParent == null) return;

        KillLogEntry entry = Instantiate(killLogEntryPrefab, killLogParent);
        entry.Show(killer, victim);
    }

    private void HandleMatchEnd(int winTeam)
    {
        if (resultPanel != null)
        {
            resultPanel.SetActive(true);
            // 내 팀 정보를 레지스트리나 로컬 속성에서 가져와 승패 판단
            resultText.text = (winTeam == 1) ? "TEAM A WIN" : "TEAM B WIN";
        }
    }

    private void HandleMenuUI(bool isShow)
    {
        if (menuPanel != null)
            menuPanel.SetActive(isShow);
    }

    // 나가기 버튼의 OnClick 이벤트에 연결할 함수
    public void OnClickLeaveGame()
    {
        RoundManager.Instance.LeaveGame();
    }

    private void HandlePickupUIUpdate(bool isShow, Vector3 pos, string text)
    {
        isPickupUIActive = isShow;

        if (pickupUIRect != null)
        {
            pickupUIRect.gameObject.SetActive(isShow);
            if (isShow)
            {
                targetPickupPos = pos;
                pickupUIText.text = text;
            }
        }
    }

    // 유틸리티
    private PlayerSlotUI FindSlotByActor(int actorNumber)
    {
        // A팀 슬롯
        foreach (var slot in teamASlots)
        {
            if (slot.gameObject.activeSelf && slot.ActorNumber == actorNumber)
                return slot;
        }
        // 없으면 B팀 슬롯 
        foreach (var slot in teamBSlots)
        {
            if (slot.gameObject.activeSelf && slot.ActorNumber == actorNumber)
                return slot;
        }
        return null;
    }

    // 팀 번호에 따라 올바른 배열에서 빈자리를 찾아주는 함수
    private PlayerSlotUI FindEmptySlot(int team)
    {
        PlayerSlotUI[] targetSlots = (team == 1) ? teamASlots : teamBSlots; // 0이면 A팀, 아니면 B팀

        foreach (var slot in targetSlots)
        {
            if (!slot.gameObject.activeSelf)
                return slot;
        }
        return null;
    }

    private void HandleRoundStart()
    {
        if (audiosource != null && usingCRT)
            audiosource.Play();

        // [CH 추가] 라운드 시작 시 커버 패널 제거 (이미 비활성이면 no-op)
        if (initialCoverPanel != null) initialCoverPanel.SetActive(false);

        // [CH 추가] 라운드 시작 시 링 초기화
        if (hpRingImage != null) { hpRingImage.fillAmount = 1f; hpRingImage.color = hpFullColor; }
        if (_rollCooldownCoroutine != null) { StopCoroutine(_rollCooldownCoroutine); _rollCooldownCoroutine = null; }
        if (rollRingImage != null) { rollRingImage.fillAmount = 1f; rollRingImage.color = rollReadyColor; }
        if (playerStatusPanel != null) playerStatusPanel.gameObject.SetActive(true);
    }

    private void HandleRoundEnd(int winTeam)
    {
        if (audiosource != null && usingCRT)
            audiosource.PlayOneShot(endRoundAudio);
        roundWinner.gameObject.SetActive(true);
        roundLeftTime.gameObject.SetActive(true);
        if (playerStatusPanel != null) playerStatusPanel.gameObject.SetActive(false);

        StartCoroutine(EndCountDown(winTeam));
    }

    private void HandleLocalPlayerDead()
    {
        if (audiosource != null && usingCRT)
            audiosource.PlayOneShot(endRoundAudio);
        if (playerStatusPanel != null) playerStatusPanel.gameObject.SetActive(false);

    }

    private IEnumerator EndCountDown(int winTeam)
    {
        float time = 0;
        while(time < RoundManager.Instance.RoundStartDelay)
        {
            time += Time.deltaTime;
            int timeToInt = Mathf.RoundToInt(RoundManager.Instance.RoundStartDelay - time);
            string textColor = (winTeam == 1) ? "red" : "blue";
            roundWinner.text = string.Format("This round winner is team <color={0}><size=60>{1}", textColor, winTeam);
            roundLeftTime.text = string.Format("The round starts in <color={0}><size=60>{1}</color></size> seconds.", textColor, timeToInt);

            yield return null;
        }
        roundWinner.gameObject.SetActive(false);
        roundLeftTime.gameObject.SetActive(false);

    }

    private IEnumerator StartCountDown()
    {
        roundLeftTime.gameObject.SetActive(true);

        float time = 0;
        while (time < 3f)
        {
            time += Time.deltaTime;
            int timeToInt = Mathf.RoundToInt(3f - time);
            roundLeftTime.text = string.Format("The round starts in <color=yellow><size=60>{0}</color></size> seconds.", timeToInt);

            yield return null;
        }
        roundLeftTime.gameObject.SetActive(false);

    }

    public void SetCRT(bool _isActive)
    {
        usingCRT = _isActive;
        if (crtController != null)
        {
            crtController.gameObject.SetActive(_isActive);
        }
    }

    public void SetBloorWhite(bool _isActive)
    {

    }

    public void SetGameSound(float _soundValue)
    {
        AudioListener.volume = _soundValue;
    }
}