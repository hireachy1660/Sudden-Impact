using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public class PlayerSpawner : MonoBehaviourPunCallbacks
{
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private GameObject FOVPrefab;
    [SerializeField] private PlayerRegistry playerRegistry;
    [SerializeField] private int layerNumber;

    private void Start()
    {
        // 1. 이미 방에 들어와 있는 상태인가? (로비에서 정상적으로 넘어온 경우)
        if (PhotonNetwork.InRoom)
        {
            Debug.Log("[PlayerSpawner] 이미 방에 접속되어 있습니다. 즉시 스폰을 진행합니다.");
            SpawnProcess();
        }
        // 2. 아직 접속 전인가? (DebugJoiner 등을 통해 접속 중인 경우)
        else
        {
            Debug.Log("[PlayerSpawner] 아직 접속 전입니다. OnJoinedRoom 콜백을 기다립니다.");
        }
    }

    // 기존 OnJoinedRoom 콜백도 유지 (디버그 조이너용)
    public override void OnJoinedRoom()
    {
        Debug.Log("[PlayerSpawner] 방 입장이 완료되었습니다. 스폰을 진행합니다.");
        SpawnProcess();
    }

    // 실제 스폰 로직을 별도 함수로 분리
    private void SpawnProcess()
    {
        // 1. 팀 정보 확인 (기본값 설정)
        int team = 1;
        if (PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey("Team"))
        {
            team = (int)PhotonNetwork.LocalPlayer.CustomProperties["Team"];
        }
        else
        {
            Debug.LogWarning("[PlayerSpawner] 팀 정보가 없어 엑터 넘버로 설정");
            team = (PhotonNetwork.LocalPlayer.ActorNumber % 2) + 1;
        }

        // 2. 캐릭터 생성
        var go = PhotonNetwork.Instantiate(playerPrefab.name, Vector3.zero, Quaternion.identity);

        // 3. 레지스트리 등록 RPC
        photonView.RPC(nameof(RegisterToRegistry), RpcTarget.AllBuffered,
                       go.GetComponent<PhotonView>().ViewID, team);
    }

    [PunRPC]
    private void RegisterToRegistry(int _viewID, int _team)
    {
        PhotonView pv = PhotonView.Find(_viewID);
        if (pv == null) return;

        PlayerController player = pv.GetComponent<PlayerController>();
        playerRegistry.RegisterPlayerTeam(player, _team);
        Debug.Log("playerRegistery 등록된 팀 : " + _team);

        if (player.photonView.IsMine)
        {
            playerRegistry.RegisterLocalPlayer(player); // 로컬 플레이어 주입
            playerRegistry.RegisterMyTeam(_team);
            player.gameObject.layer = layerNumber;

            ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable();

            props["Team"] = _team;
            props["viewID"] = _viewID;

            PhotonNetwork.LocalPlayer.SetCustomProperties(props);

        }
        SetPlayerInfo(player,_team);
        int actualLocalTeam = 1;
        actualLocalTeam = playerRegistry.MyTeam;

        if (_team == actualLocalTeam)
        {
            // FOV는 네트워크 동기화가 필요 없는 순수 로컬 시각 효과이므로 일반 Instantiate 사용
            // 단, 이미 FOV가 달려있는지 중복 생성 방지 체크를 권장합니다.
            if (player.transform.Find(FOVPrefab.name) == null)
            {
                GameObject fov = Instantiate(FOVPrefab, player.transform);
                fov.name = FOVPrefab.name; // 이름 맞춰주기 (중복 체크용)
            }
        }
    }

    private void SetPlayerInfo(PlayerController _player, int _team)
    {
        _player.Init(_team);
    }
}
