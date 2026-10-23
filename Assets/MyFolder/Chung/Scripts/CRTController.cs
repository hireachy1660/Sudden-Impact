using UnityEngine;
using System.Collections;

/// <summary>
/// RetroEffect.shader 의 _CRTIntensity 를 런타임에 제어.
/// Shader.SetGlobalFloat 을 사용하므로 머티리얼 참조 불필요.
/// </summary>
public class CRTController : MonoBehaviour
{
    [Header("Death Effect (로컬 플레이어 사망)")]
    [SerializeField][Range(0f, 1f)] private float deathMaxIntensity = 0.85f;
    [SerializeField] private float deathRampUp = 0.3f;
    // 다음 라운드 시작 전까지 켜두려면 충분히 큰 값, 자동 종료는 원하는 초로 설정
    // [CH 추가] 홀드 후 서서히 꺼지는 시간 (0이면 즉시 꺼짐)
    [SerializeField] private float deathRampDown = 1f;
    // 라운드 종료 이벤트가 덮어쓰므로 사실상 무한 유지
    [SerializeField] private float deathHold = 5f;

    [Header("Round End Effect (라운드 종료)")]
    [SerializeField][Range(0f, 1f)] private float roundEndMaxIntensity = 1.0f;
    [SerializeField] private float roundEndRampUp = 0.5f;
    [SerializeField] private float roundEndHold = 2.5f;
    [SerializeField] private float roundEndRampDown = 1.5f;

    // ── 내부 ──────────────────────────────────────────────────────────
    private static readonly int k_CRTIntensity = Shader.PropertyToID("_CRTIntensity");
    private float _current;
    private Coroutine _active;

    // ── 이벤트 연결 ────────────────────────────────────────────────────
    private void OnEnable()
    {
        GameEvents.OnLocalPlayerDeath += HandleLocalDeath;
        GameEvents.OnRoundEnd += HandleRoundEnd;
        GameEvents.OnRoundStart += HandleRoundStart;
    }

    private void OnDisable()
    {
        GameEvents.OnLocalPlayerDeath -= HandleLocalDeath;
        GameEvents.OnRoundEnd -= HandleRoundEnd;
        GameEvents.OnRoundStart -= HandleRoundStart;

        // 라운드 종료시에 CRT효과 제거
        if (_active != null) StopCoroutine(_active);
        SetIntensity(0f);
    }

    // ── 핸들러 ────────────────────────────────────────────────────────
    private void HandleLocalDeath()
    {
        // 사망 후 강도 유지, 라운드 종료 이벤트가 Override
        // [CH 수정] 0f(즉시 종료) → deathRampDown(인스펙터 조절 가능)
        Trigger(deathMaxIntensity, deathRampUp, deathHold, deathRampDown);
    }

    private void HandleRoundEnd(int winTeam)
    {
        // 라운드 종료: 더 강하게 올렸다가 서서히 복구
        Trigger(roundEndMaxIntensity, roundEndRampUp, roundEndHold, roundEndRampDown);
    }

    private void HandleRoundStart()
    {
        // 새 라운드 시작 시 즉시 초기화
        if (_active != null) StopCoroutine(_active);
        SetIntensity(0f);
    }

    // ── 코루틴 ────────────────────────────────────────────────────────
    private void Trigger(float maxVal, float rampUp, float hold, float rampDown)
    {
        if (_active != null) StopCoroutine(_active);
        _active = StartCoroutine(CRTCoroutine(maxVal, rampUp, hold, rampDown));
    }

    private IEnumerator CRTCoroutine(float maxVal, float rampUp, float hold, float rampDown)
    {
        // Ramp Up
        float start = _current;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / rampUp;
            SetIntensity(Mathf.Lerp(start, maxVal, t));
            yield return null;
        }
        SetIntensity(maxVal);

        // Hold
        yield return new WaitForSecondsRealtime(hold);

        // Ramp Down (0이면 스킵)
        if (rampDown > 0.001f)
        {
            t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / rampDown;
                SetIntensity(Mathf.Lerp(maxVal, 0f, t));
                yield return null;
            }
            SetIntensity(0f);
        }

        _active = null;
    }

    private void SetIntensity(float value)
    {
        _current = Mathf.Clamp01(value);
        Shader.SetGlobalFloat(k_CRTIntensity, _current);
    }
}
