using Photon.Pun;
using UnityEngine;

public class PistolGun : Gun
{
    protected override void FireProjectile(Quaternion _aimRotation)
    {
        // 방향은 Gun이 조준점으로 계산해 넘긴 회전을 그대로 쓴다(권총은 퍼짐 없음)

        // 넘겨줄 데이터를 object 배열로 포장합니다. (순서가 매우 중요!)
        object[] bulletData = new object[] { ownerActorNumber, ownerTeam, damage };

        // Instantiate의 5번째 매개변수에 이 배열을 꽂아 넣습니다.
        PhotonNetwork.Instantiate(
            projectilePrefab.name,
            attackPoint.position,
            _aimRotation,
            0,
            bulletData 
        );

        GameEvents.SpreadUpdate(1f, fireRate, 1f);
    }
}
