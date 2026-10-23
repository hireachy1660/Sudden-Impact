using UnityEngine;

public class ThrownWeapon : Projectile
{
    [Header("Throw Visual Settings")]
    [Tooltip("회전할 총기 모델링 (자식 오브젝트)")]
    [SerializeField] private Transform visualMesh;

    [Tooltip("회전 속도 (기본값: 1초에 약 3바퀴)")]
    [SerializeField] private float rotateSpeed = 1080f;

    protected override void Awake()
    {
        base.Awake();
        damageType = DamageType.Throw;
    }

    protected override void Update()
    {
        // 1. 부모(Projectile)의 이동 로직을 그대로 실행 (총알처럼 앞으로 날아감)
        base.Update();

        // 2. 날아가는 동안 자식 메쉬를 빙글빙글 회전시킴
        if (visualMesh != null)
        {
            // 모델링의 기준 축에 따라 Vector3.up / right / forward 중 하나로 맞추시면 됩니다.
            visualMesh.Rotate(Vector3.up * rotateSpeed * Time.deltaTime);
        }
    }

    // OnTriggerEnter나 OnPhotonInstantiate 등 충돌/동기화 로직은 
    // 부모(Projectile)에 있는 코드를 100% 그대로 물려받아 사용합니다. (코드 중복 0%)
}