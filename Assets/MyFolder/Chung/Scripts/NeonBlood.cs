using UnityEngine;
using System.Collections;

public class NeonBlood : MonoBehaviour
{
    [Header("BloodReference")]
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private Sprite[] bloodSprites; // 3~4종 혈흔 스프라이트

    [Header("Parameter")]
    [SerializeField, Range(0f, 1f)] private float minScale = 0.1f;
    [SerializeField, Range(0f, 1f)] private float maxScale = 0.5f;

    // HDR을 지원하는 컬러 배열 (인스펙터에서 형광 핑크, 크림슨 네온 색상 세팅)
    [ColorUsage(true, true)]
    [SerializeField] private Color[] neonColors;

    private void OnEnable()
    {
        // 라운드 종료 이벤트 구독 (알아서 청소 됨)
        GameEvents.OnRoundStart += ReturnToPool;

        // 랜덤 스프라이트 및 네온 색상 적용
        if (bloodSprites.Length > 0) sr.sprite = bloodSprites[Random.Range(0, bloodSprites.Length)];
        if (neonColors.Length > 0) sr.color = neonColors[Random.Range(0, neonColors.Length)];

        // 랜덤 회전 (바닥에 누운 상태에서 Y축(또는 2D면 Z축) 랜덤 회전)
        transform.localRotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);

        // 스플래터 애니메이션 실행 (피가 확 퍼지는 연출)
        StartCoroutine(SplatterAnimation());
    }

    private void OnDisable()
    {
        GameEvents.OnRoundStart -= ReturnToPool;
    }

    private IEnumerator SplatterAnimation()
    {
        // 스케일: 0.3 ~ 0.8
        float targetScale = Random.Range(0.3f, 0.8f);
        float time = 0f;
        float duration = 0.1f; // 0.1초 만에 퍼짐 

        Vector3 startScale = Vector3.one * 0.05f; // 아주 작은 점에서 시작
        Vector3 endScale = Vector3.one * targetScale;

        while (time < duration)
        {
            time += Time.deltaTime;
            // Lerp를 통해 부드럽고 빠르게 크기 팽창
            transform.localScale = Vector3.Lerp(startScale, endScale, time / duration);
            yield return null;
        }
        transform.localScale = endScale;
    }

    // 라운드 종료 시 알아서 비활성화 (오브젝트 풀링용)
    private void ReturnToPool()
    {
        Destroy(gameObject);
        //gameObject.SetActive(false);
    }
}