using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 킬 발생 시 월드 공간에 팝핑되는 텍스트 (핫라인 마이애미 스타일)
///
/// 버그 수정 (v2):
///   [빌보드]  LookRotation(camPos-textPos) → LookRotation(-cam.forward, cam.up)
///             탑뷰에서 forward가 거의 수직이 되면 up 기준이 무너지는 문제 해결
///   [상승방향] worldY 상승 → cam.transform.up 방향 상승
///             탑뷰에서 worldY = 카메라 쪽(화면 밖)으로 사라지던 문제 해결
///   [궤도축]  world XZ → camRight/camUp 축 (화면 평면 위 타원 궤도)
///   [가려짐]  Distance Field Overlay 셰이더로 교체 (ZTest Always)
///   [잔상]    Underlay 키워드 강제 ON + Dilate/Softness 설정 + brightness 상향
/// </summary>
public class KillFloatText : MonoBehaviour
{
    [SerializeField] private TextMeshPro tmp;

    [Header("Motion")]
    [SerializeField] private float radiusX = 0.6f;
    [SerializeField] private float radiusY = 0.35f;   // 화면 Y(cam.up) 방향 반경
    [SerializeField] private float orbitSpeed = 2.5f;
    [SerializeField] private float riseSpeed = 1.8f;    // 화면 위쪽(cam.up) 상승 속도

    [Header("Timing")]
    [SerializeField] private float popInDuration = 0.18f;
    [SerializeField] private float idleDuration = 0.75f;
    [SerializeField] private float popOutDuration = 0.12f;
    // idleDuration 대기 객체 — 직렬화 값이라 Awake에서 1회 생성해 재사용
    private WaitForSeconds _idleWait;

    [Header("Underlay Color")]
    // [CH 추가] false로 끄면 UNDERLAY_ON 키워드 자체를 비활성화 → 잔상 완전 제거 (비교 촬영용)
    [SerializeField] private bool useUnderlay = true;
    [SerializeField] private float hueSpeed = 1.2f;
    [SerializeField][Range(0f, 1f)] private float underlayBrightness = 0.55f;   // ↑ 기존 0.28
    [SerializeField] private float underlayOffsetStrength = 1.0f;        // ↑ 기존 0.35

    // ── 내부 상태 ──────────────────────────────────────────
    private enum State { Inactive, PopIn, Idle, PopOut }
    private State _state = State.Inactive;
    private Vector3 _basePos;   // 상승 누적용 기준점 (cam.up 방향으로 매 프레임 이동)
    private float _orbitT;    // 궤도 위상(phase)
    private Material _mat;       // TMP 인스턴스 머티리얼 (캐싱)
    private Camera _cam;
    private Coroutine _routine;

    private void Awake()
    {
        if (tmp == null) tmp = GetComponent<TextMeshPro>();
        _idleWait = new WaitForSeconds(idleDuration);

        // 머티리얼 인스턴스 분리 후 캐싱 (첫 접근 시 자동 분리)
        _mat = tmp.fontMaterial;

        // [CH 수정] useUnderlay 토글로 잔상 키워드 활성 여부 결정 (비교 촬영용)
        if (useUnderlay)
        {
            _mat.EnableKeyword("UNDERLAY_ON");
            _mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.5f);
            _mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.05f);
        }
        else
        {
            _mat.DisableKeyword("UNDERLAY_ON");
        }

        // ── 항상 전면 렌더링 — Distance Field Overlay 셰이더 교체 ──
        // Overlay 셰이더는 ZTest Always → 벽/바닥에 가려지지 않음
        // Material Variant는 셰이더 교체 불가 → 프리팹에 Distance Field Overlay 머티리얼 직접 연결할 것
        // Shader overlay = Shader.Find("TextMeshPro/Distance Field Overlay");
        // if (overlay != null) _mat.shader = overlay;
    }

    // ─────────────────────────────────────────────────────────
    // KillFloatTextManager가 풀에서 꺼낸 뒤 호출
    // ─────────────────────────────────────────────────────────
    public void Play(Vector3 worldPos, string text, Camera cam)
    {
        _basePos = worldPos;
        _orbitT = 0f;
        _cam = cam;

        tmp.text = text;
        tmp.color = Color.white;   // 전경은 항상 흰색 (가독성)

        transform.position = worldPos;
        transform.localScale = Vector3.zero;

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(PlayRoutine());
    }

    // ─────────────────────────────────────────────────────────
    // FSM 코루틴
    // ─────────────────────────────────────────────────────────
    private IEnumerator PlayRoutine()
    {
        // ── Pop-In : EaseOutBack 스케일 0 → 1 (+오버슈트) ──
        _state = State.PopIn;
        float elapsed = 0f;
        while (elapsed < popInDuration)
        {
            elapsed += Time.deltaTime;
            transform.localScale = MirrorScale(EaseOutBack(Mathf.Clamp01(elapsed / popInDuration)));
            yield return null;
        }
        transform.localScale = MirrorScale(1f);

        // ── Idle : 궤도 운동 + 상승 (LateUpdate에서 처리) ──
        _state = State.Idle;
        yield return _idleWait;

        // ── Pop-Out : 스케일 1 → 0 ────────────────────────
        _state = State.PopOut;
        elapsed = 0f;
        while (elapsed < popOutDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / popOutDuration);
            transform.localScale = MirrorScale(1f - t * t);
            yield return null;
        }

        _state = State.Inactive;
        gameObject.SetActive(false);  // 풀 반환
    }

    // ─────────────────────────────────────────────────────────
    // 위치·빌보드·언더레이 매 프레임 갱신
    // ─────────────────────────────────────────────────────────
    private void LateUpdate()
    {
        if (_state == State.Inactive || _cam == null) return;

        // ── 스크린 정렬 빌보드 ─────────────────────────────
        // -cam.forward 방향으로 면이 향하게 → 카메라 뷰 평면과 완전 평행
        // LookRotation(camPos - textPos) 는 탑뷰에서 forward ≈ 수직이라 UP 참조가 망가짐
        transform.rotation = Quaternion.LookRotation(-_cam.transform.forward, _cam.transform.up);

        if (_state == State.Idle || _state == State.PopIn)
        {
            // ── Lissajous 달걀형 궤도 (화면 평면 기준 camRight/camUp 축) ──
            _orbitT += orbitSpeed * Time.deltaTime;
            float ox = radiusX * Mathf.Sin(_orbitT + Mathf.Sin(2f * _orbitT) * 0.3f);
            float oy = radiusY * Mathf.Cos(_orbitT);

            // ── 상승 : cam.up 방향 (탑뷰에서 화면 위쪽 = +Z 방향) ────
            _basePos += _cam.transform.up * riseSpeed * Time.deltaTime;
            transform.position = _basePos
                                + _cam.transform.right * ox
                                + _cam.transform.up * oy;

            // ── 배경 언더레이 : HSV 색상 순환 ────────────────
            Color underlay = Color.HSVToRGB(Time.time * hueSpeed % 1f, 1f, underlayBrightness);
            _mat.SetColor(ShaderUtilities.ID_UnderlayColor, underlay);

            // ── 언더레이 오프셋 = 이동 방향 반대 → GPU 잔상 ──
            // ox 도함수 (연쇄법칙)
            float dxdt = radiusX * Mathf.Cos(_orbitT + Mathf.Sin(2f * _orbitT) * 0.3f)
                       * (1f + 0.6f * Mathf.Cos(2f * _orbitT));
            float dydt = -radiusY * Mathf.Sin(_orbitT);
            var vel = new Vector2(dxdt, dydt);
            if (vel.sqrMagnitude > 0.001f) vel.Normalize();
            _mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, -vel.x * underlayOffsetStrength);
            _mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -vel.y * underlayOffsetStrength);
        }
    }

    // ── EaseOutBack : ~1.3 오버슈트 후 1.0 안착 ───────────
    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float tm1 = t - 1f;
        return 1f + c3 * tm1 * tm1 * tm1 + c1 * tm1 * tm1;
    }

    // ── Scale X = -1 : 빌보드 공식이 만드는 좌우 반전 보정 ─
    // LookRotation(-cam.forward, cam.up) → text +X = -cam.right (거울 방향)
    // Scale.x 를 음수로 유지하면 렌더 시 다시 한 번 뒤집혀서 정상 방향이 됨
    private static Vector3 MirrorScale(float s) => new Vector3(-s, s, s);
}
