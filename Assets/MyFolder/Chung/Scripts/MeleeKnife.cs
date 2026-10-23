using Photon.Pun;
using System.Collections.Generic;
using UnityEngine;

public class MeleeKnife : Weapon
{
    [Header("Melee Settings")]
    [SerializeField] private Vector3 boxSize = new Vector3(2f, 1f, 1.5f); // 전체 크기 (가로, 세로, 깊이)
    [SerializeField] private float attackOffset = 1.0f; // 캐릭터 중심으로부터 얼마나 앞에서 판정이 생길지
    [SerializeField] private LayerMask targetLayer;       // 대상 레이어 (Player, Furniture 등)


    [Header("Cooldown")]
    [SerializeField] private float attackCooldown = 0.5f;
    private float _cooldownEnd = 0f;

    [Header("ForDebug")]
    [SerializeField] private bool isDebugMode;
    
    public override void Attack(bool _isHeld = true)
    {
        if (_isHeld) { return; }
        if (Time.time < _cooldownEnd) return;
        _cooldownEnd = Time.time + attackCooldown;

        // 1. 판정 중심점 계산 (에임 방향으로 attackOffset만큼 떨어진 곳)
        Vector3 attackPos = attackPoint.position;
        Quaternion orientation = attackPoint.rotation;
        Vector3 halfExtents = boxSize / 2f;

        // 2. 해당 영역 내의 모든 콜라이더 검출
        Collider[] hitColliders = Physics.OverlapBox(attackPos, halfExtents, orientation, targetLayer);

        // 3. 중복 타격 방지를 위한 셋 (한 번 휘둘러서 동일 객체 여러 번 타격 방지)
        HashSet<IAttackReceiver> hitTargets = new HashSet<IAttackReceiver>();

        foreach (var hit in hitColliders)
        {
            // 나 자신(또는 내 캐릭터 최상단)은 제외
            if (hit.transform.root == transform.root) continue;

            if (isDebugMode)
                damage = 50;

            // 인터페이스 추출
            IAttackReceiver receiver = hit.GetComponent<IAttackReceiver>();

            if (receiver != null && !hitTargets.Contains(receiver))
            {
                hitTargets.Add(receiver);

                Vector3 impactPoint = hit.ClosestPoint(attackPos);

                ImpactData date = new ImpactData
                {
                    damage = this.damage,
                    attackerActorNumber = PhotonNetwork.LocalPlayer.ActorNumber,
                    attackerTeam = ownerTeam,
                    type = DamageType.Melee,
                    hitPoint = impactPoint,
                    hitNormal = attackPoint.forward,
                };

                // 4. 데미지 전송 (모든 클라이언트에서 동일하게 처리되도록 RPC 호출)
                // 피격자가 PhotonView를 가지고 있다면 해당 객체의 TakeDamage를 호출합니다.
                receiver.OnReceiveImpact(date);

                Debug.Log($"[Knife] <color=yellow>Hit!</color> Target: {hit.name} / Damage: {damage}");
            }
        }
    }

    // 에디터에서 공격 범위를 시각적으로 확인하기 위함
    private void OnDrawGizmosSelected()
    {
        if(!isDebugMode) return;

        Gizmos.color = Color.red;

        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(attackPoint.position, attackPoint.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, boxSize);
        Gizmos.matrix = oldMatrix;
    }


}
