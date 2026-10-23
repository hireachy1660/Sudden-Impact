using UnityEngine;

public class TeamGoal : MonoBehaviour
{
    public int myTeam;

    private void OnTriggerEnter(Collider other)
    {
        // "저 거점입니다! 누가 도착했어요!"
        RoundManager.Instance.OnLocalPlayerReachedGoal(myTeam);
    }
}