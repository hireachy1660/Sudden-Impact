using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// 디버그용 빠른 접속 스크립트
/// 씬에 배치하면 자동으로 Photon 접속 및 방 입장
/// </summary>
public class DebugJoiner : MonoBehaviourPunCallbacks
{
    [Header("방 설정")]
    [SerializeField] private string roomName = "DebugRoom";
    [SerializeField] private int maxPlayers = 4;

    [Header("스폰 설정")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private Transform[] spawnPoints;

    [Header("디버그")]
    [SerializeField] private bool autoConnect = true;
    [SerializeField] private bool showGUI = true;

    private string log = "";

    // -----------------------------------------------

    private void Start()
    {
        if (autoConnect)
            Connect();
    }

    public void Connect()
    {
        if (PhotonNetwork.IsConnected)
        {
            JoinOrCreateRoom();
            return;
        }

        Log("마스터 서버 접속 중...");
        PhotonNetwork.ConnectUsingSettings();
    }

    private void JoinOrCreateRoom()
    {
        Log($"방 입장 시도: {roomName}");
        RoomOptions options = new RoomOptions { MaxPlayers = (byte)maxPlayers };
        PhotonNetwork.JoinOrCreateRoom(roomName, options, TypedLobby.Default);
    }

    // -----------------------------------------------
    // PUN 콜백
    // -----------------------------------------------

    public override void OnConnectedToMaster()
    {
        Log("마스터 서버 접속 완료");
        JoinOrCreateRoom();
    }

    public override void OnJoinedRoom()
    {

        string myNickName = $"Player_{PhotonNetwork.LocalPlayer.ActorNumber}";
        PhotonNetwork.LocalPlayer.NickName = myNickName;

        // 디버그용 팀 설정 (홀수 번호는 1팀, 짝수 번호는 0팀 등 임의 배정)
        int debugTeam = (PhotonNetwork.LocalPlayer.ActorNumber % 2 == 0) ? 0 : 1;

        //프로퍼티 설정
        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable();
        props["Team"] = debugTeam; 
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);

        Log($"방 입장 완료 | 닉네임: {myNickName} | 디버그 팀: {debugTeam}");
        Log($"방 입장 완료 | 닉네임: {myNickName} | 플레이어 수: {PhotonNetwork.CurrentRoom.PlayerCount}");

        // SpawnPlayer(); 
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Log($"방 입장 실패: {message}");
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Log($"접속 끊김: {cause}");
    }

    // -----------------------------------------------
    // 플레이어 스폰
    // -----------------------------------------------

    private void SpawnPlayer()
    {
        if (playerPrefab == null)
        {
            Log("playerPrefab이 없습니다");
            return;
        }

        Vector3 spawnPos = GetSpawnPosition();
        PhotonNetwork.Instantiate(playerPrefab.name, spawnPos, Quaternion.identity);
        Log($"플레이어 스폰: {spawnPos}");
    }

    private Vector3 GetSpawnPosition()
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            int index = (PhotonNetwork.CurrentRoom.PlayerCount - 1) % spawnPoints.Length;
            return spawnPoints[index].position;
        }

        // 스폰 포인트 없으면 랜덤 위치
        return new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));
    }

    // -----------------------------------------------
    // 디버그 GUI
    // -----------------------------------------------

    private void Log(string message)
    {
        Debug.Log($"[DebugJoiner] {message}");
        log = message;
    }

    private void OnGUI()
    {
        if (!showGUI) return;

        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        GUILayout.Label($"상태: {PhotonNetwork.NetworkClientState}");
        GUILayout.Label($"핑: {PhotonNetwork.GetPing()}ms");

        if (PhotonNetwork.InRoom)
        {
            GUILayout.Label($"방: {PhotonNetwork.CurrentRoom.Name}");
            GUILayout.Label($"플레이어: {PhotonNetwork.CurrentRoom.PlayerCount} / {maxPlayers}");
            GUILayout.Label($"호스트 여부: {PhotonNetwork.IsMasterClient}");
        }

        GUILayout.Label($"로그: {log}");

        if (!PhotonNetwork.IsConnected)
        {
            if (GUILayout.Button("접속")) Connect();
        }
        else if (!PhotonNetwork.InRoom)
        {
            if (GUILayout.Button("방 입장")) JoinOrCreateRoom();
        }
        else
        {
            if (GUILayout.Button("방 나가기")) PhotonNetwork.LeaveRoom();
        }

        GUILayout.EndArea();
    }
}