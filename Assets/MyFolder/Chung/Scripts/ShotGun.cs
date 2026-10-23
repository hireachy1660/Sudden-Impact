using Photon.Pun;
using UnityEngine;

public class Shotgun : Gun
{
    [Header("Shotgun Settings")]
    [Tooltip("한 번 쏠 때 나가는 파편(Pellet)의 개수")]
    [SerializeField] private int pelletCount = 5;

    [Tooltip("산탄의 시작 각도 (가장 왼쪽 펠릿)")]
    [SerializeField] private float startAngle = -10f;

    [Tooltip("각 펠릿 사이의 기본 간격(각도)")]
    [SerializeField] private float angleStep = 5f;

    [Tooltip("자연스러움을 위한 랜덤 노이즈 범위")]
    [SerializeField] private float randomNoise = 2f;

    protected override void FireProjectile()
    {
        Vector3 direction = attackPoint.forward;
        direction.y = 0;

        Quaternion baseRotation = attackPoint.rotation;
        if (direction.sqrMagnitude > 0.001f)
        {
            baseRotation = Quaternion.LookRotation(direction);
        }

        // 산탄 공식 
        for (int i = 0; i < pelletCount; i++)
        {
            // -10도부터 시작해 5도씩 더하고, 거기에 -2 ~ 2도의 랜덤 노이즈
            float currentAngle = startAngle + (angleStep * i) + Random.Range(-randomNoise, randomNoise);

            // 베이스 회전값에 계산된 각도를 Y축을(좌우) 기준으로
            Quaternion pelletRotation = baseRotation * Quaternion.Euler(0f, currentAngle, 0f);

            // 데이터 포장
            object[] bulletData = new object[] { ownerActorNumber, ownerTeam, damage };

            // 펠릿 1발 생성 (루트를 돌며 총 5발이 거의 동시에 생성)
            PhotonNetwork.Instantiate(
                projectilePrefab.name,
                attackPoint.position,
                pelletRotation,
                0,
                bulletData
            );

            GameEvents.SpreadUpdate(2f, fireRate, 2f);
        }
    }
}