using UnityEngine;
using Photon.Pun;

public class PlayerMovement : MonoBehaviourPun, IPlayerPart
{

    private void Awake()
    {
        this.enabled = photonView.IsMine;
    }

    public void OnRespawn() { }
    public void OnRoundEndReset() { }
}
