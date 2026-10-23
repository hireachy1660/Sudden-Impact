using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 킬 로그 UI의 단일 항목 프리팹 컴포넌트.
///
/// 씬 설정:
///   - TextMeshProUGUI : 킬 메시지 텍스트
///   - CanvasGroup     : 페이드 아웃에 사용 (알파 제어)
///
/// 사용법:
///   UIManager에서 Instantiate 후 entry.Show(killer, victim) 호출.
/// </summary>
public class KillLogEntry : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI logText;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Timing")]
    [SerializeField] private float displayDuration = 3f;   // 표시 지속 시간
    [SerializeField] private float fadeDuration = 1f;   // 페이드 아웃 시간

    // ─── 외부 호출 ────────────────────────────────────────────────────

    /// <summary>
    /// UIManager가 GameEvents.OnKillLog 수신 시 호출합니다.
    /// </summary>
    public void Show(string killer, string victim)
    {
        if (logText != null)
            logText.text = $"{killer}  Kill!  {victim}";

        if (canvasGroup != null)
            canvasGroup.alpha = 1f;

        gameObject.SetActive(true);
        StartCoroutine(FadeOutAndDestroy());
    }

    // ─── 페이드 아웃 코루틴 ──────────────────────────────────────────

    private IEnumerator FadeOutAndDestroy()
    {
        // displayDuration 동안 완전히 표시
        yield return new WaitForSeconds(displayDuration);

        // fadeDuration 동안 서서히 투명해짐
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            if (canvasGroup != null)
                canvasGroup.alpha = 1f - (elapsed / fadeDuration);
            yield return null;
        }

        // 완전히 사라진 뒤 오브젝트 제거
        Destroy(gameObject);
    }
}
