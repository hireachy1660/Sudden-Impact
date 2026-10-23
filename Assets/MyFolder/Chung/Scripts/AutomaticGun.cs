using Photon.Pun;
using UnityEngine;

public class AutomaticGun : Gun
{
    [Header("Dynamic Spread Settings")]
    [Tooltip("기본 탄퍼짐 각도 (초탄 명중률 - 작을수록 정교함)")]
    [SerializeField] private float baseSpread = 1f;

    [Tooltip("최대 탄퍼짐 각도 (완전 난사 시 최대치)")]
    [SerializeField] private float maxSpread = 15f;

    [Tooltip("한 발 쏠 때마다 누적해서 증가하는 각도")]
    [SerializeField] private float spreadIncreasePerShot = 2f;

    [Tooltip("사격을 멈췄을 때 초당 에임이 회복되는(좁혀지는) 속도")]
    [SerializeField] private float spreadRecoveryRate = 15f;

    // 현재 발사에 적용될 실시간 탄퍼짐 각도
    private float currentSpread;

    protected override void OnEnable()
    {
        base.OnEnable();
        currentSpread = baseSpread; // 무기를 꺼낼 때는 초탄 명중률로 초기화
    }

    private void Update()
    {
        // 총을 쏘지 않는 동안(혹은 쏘는 와중에도 프레임마다) 
        // 누적된 탄퍼짐을 기본값(baseSpread)으로 서서히 되돌립니다.
        if (currentSpread > baseSpread)
        {
            currentSpread = Mathf.MoveTowards(currentSpread, baseSpread, spreadRecoveryRate * Time.deltaTime);
        }
    }

    protected override void FireProjectile()
    {
        // 1. 기본 방향 계산
        Vector3 direction = attackPoint.forward;
        direction.y = 0;

        Quaternion baseRotation = attackPoint.rotation;
        if (direction.sqrMagnitude > 0.001f)
        {
            baseRotation = Quaternion.LookRotation(direction);
        }

        // 누적된 탄퍼짐(currentSpread) 적용
        float randomSpread = Random.Range(-currentSpread, currentSpread);
        Quaternion finalSpreadRotation = baseRotation * Quaternion.Euler(0f, randomSpread, 0f);

        // 탄퍼짐 누적시킴 (최대치까지만)
        currentSpread = Mathf.Min(currentSpread + spreadIncreasePerShot, maxSpread);

        // 초기화 데이터 전달
        object[] bulletData = new object[] { ownerActorNumber, ownerTeam, damage };

        // 꺾인 각도(finalSpreadRotation)로 인스턴스
        PhotonNetwork.Instantiate(
            projectilePrefab.name,
            attackPoint.position,
            finalSpreadRotation,
            0,
            bulletData
        );

        // 현재 총기의 반동/퍼짐 정도를 UI에 전달 (예: 1.0 ~ 5.0 사이의 값)
        GameEvents.SpreadUpdate(currentSpread, spreadRecoveryRate, baseSpread);
    }
}