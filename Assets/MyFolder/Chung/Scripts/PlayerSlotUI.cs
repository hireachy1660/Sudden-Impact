using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// [기능 4] 상단 팀 생존 UI의 개별 플레이어 슬롯.
///
/// 씬 설정 (Horizontal Layout Group 자식 오브젝트):
///   - nameText     : 플레이어 이름을 표시하는 TextMeshProUGUI
///   - portraitImage: 캐릭터 초상화 Image (흑백 처리 대상)
///   - teamBadge    : 팀 색상을 표시하는 Image (선택사항)
///   - deadOverlay  : 사망 표시용 반투명 오버레이 Image (선택사항)
/// </summary>
public class PlayerSlotUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Image portraitImage;
    [SerializeField] private Image teamBadge;
    [SerializeField] private GameObject deadOverlay;    // 사망 X 표시 등

    [Header("Team Colors")]
    [SerializeField] private Color teamAColor = new Color(0.2f, 0.5f, 1f);   // 파랑
    [SerializeField] private Color teamBColor = new Color(1f, 0.3f, 0.3f);   // 빨강

    [Header("Dead State")]
    [SerializeField] private Color deadTint = new Color(0.25f, 0.25f, 0.25f, 1f);

    public int ActorNumber { get; private set; }

    // ─── 초기화 ────────────────────────────────────────────────────────

    /// <summary>
    /// UIManager가 GameEvents.OnPlayerUIInit 수신 시 호출합니다.
    /// </summary>
    public void Init(int actorNumber, string playerName, int team)
    {
        ActorNumber = actorNumber;

        if (nameText != null)
            nameText.text = playerName;

        // 팀 배지 색상 설정
        if (teamBadge != null)
            teamBadge.color = (team == 0) ? teamAColor : teamBColor;

        // 초상화 원래 색으로 복원 (라운드 재시작 대비)
        if (portraitImage != null)
            portraitImage.color = Color.white;

        if (deadOverlay != null)
            deadOverlay.SetActive(false);

        gameObject.SetActive(true);
    }

    // ─── 사망 처리 ────────────────────────────────────────────────────

    /// <summary>
    /// UIManager가 GameEvents.OnPlayerUIDead 수신 시 호출합니다.
    /// 슬롯을 비활성화하지 않고 흑백/어둡게 처리해 사망을 시각화합니다.
    /// </summary>
    public void SetDead()
    {
        // 초상화를 어두운 단색으로 덮어 흑백 느낌 연출
        if (portraitImage != null)
            portraitImage.color = deadTint;

        if (nameText != null)
            nameText.color = deadTint;

        // 선택사항: 별도 오버레이(예: X 아이콘) 활성화
        if (deadOverlay != null)
            deadOverlay.SetActive(true);
    }
}
