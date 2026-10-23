using Photon.Pun;
using UnityEngine;

public class PistolGun : Gun
{
    protected override void FireProjectile()
    {
        Vector3 direction = attackPoint.forward;
        direction.y = 0; // 총알이 바닥에 박히거나 하늘로 날아가지 않게 수평(y축) 고정

        // 2. 방향이 0이 아닐 때만 회전값 적용 (제자리 클릭 에러 방지)
        Quaternion bulletRotation = attackPoint.rotation;
        if (direction.sqrMagnitude > 0.001f)
        {
            bulletRotation = Quaternion.LookRotation(direction);
        }

        // 3. 넘겨줄 데이터를 object 배열로 포장합니다. (순서가 매우 중요!)
        object[] bulletData = new object[] { ownerActorNumber, ownerTeam, damage };

        // 4. Instantiate의 5번째 매개변수에 이 배열을 꽂아 넣습니다.
        PhotonNetwork.Instantiate(
            projectilePrefab.name,
            attackPoint.position,
            bulletRotation,
            0,
            bulletData 
        );

        GameEvents.SpreadUpdate(1f, fireRate, 1f);
    }
}
