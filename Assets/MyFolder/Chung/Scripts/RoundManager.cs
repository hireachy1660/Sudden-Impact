using Photon.Pun;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 디버그용 경기 흐름 관리
/// 실제 GameManager로 재활용 예정
/// </summary>
public class RoundManager : MonoBehaviourPunCallbacks
{
    public static RoundManager Instance { get; private set; }

    [Header("Registry")]
    [SerializeField] private PlayerRegistry playerRegistry;

    [Header("경기 설정")]
    [SerializeField] private int winScore = 5;          // 5선승
    [SerializeField] private float roundStartDelay = 3f;
    public float RoundStartDelay
    {
        get { return roundStartDelay; }
    }

    [Header("스폰 설정")]
    [SerializeField] private Transform[] teamASpawnPoints;
    [SerializeField] private Transform[] teamBSpawnPoints;

    [Header("Flags")]
    [SerializeField] private Flag[] mapFlags;
    [SerializeField] private FlagPointer flagPointer;

    [Header("ForDebug")]
    [SerializeField] private int teamAScore = 0;
    [SerializeField] private int teamBScore = 0;

    [Header("StartButton")]
    [SerializeField] private Button startButton;

    [Header("SceneLoad")]
    [SerializeField] private string lobbySceneName;

    // -----------------------------------------------

    private void Awake()
    {
        Instance = this;
        startButton?.onClick.AddListener(StartRound);
    }

    private void Start()
    {
        if(PhotonNetwork.IsMasterClient)
        {
            StartCoroutine(CountDownCoroutine());
        }
    }

    public override void OnDisable()
    {
        base.OnDisable();
        playerRegistry.Clear();
    }

    #region 외부 호출

    // -----------------------------------------------
    // 외부 호출 - 플레이어 사망 시 PlayerController에서 호출
    // -----------------------------------------------

    public void OnPlayerDied(PlayerController player, int _attackerNum)
    {
        GameEvents.PlayerUIDead(player.photonView.Owner.ActorNumber);

        if (playerRegistry.TryGetPlayerByActorNumber(_attackerNum, out var attacker))
            GameEvents.Kill(attacker.photonView.Owner.NickName, player.photonView.Owner.NickName);

        if (_attackerNum == PhotonNetwork.LocalPlayer.ActorNumber)
            GameEvents.KillFloat(player.transform.position);

        if (!PhotonNetwork.IsMasterClient) return; // 마스터 클라이언트만 승패 판단

        // 죽은 플레이어가 적 깃발을 들고 있다면?
        if (player.HasEnemyFlag)
        {
            int droppedFlagIndex = -1;
            for(int i = 0; i< mapFlags.Length; ++i)
            {
                if(player.MyTeam != mapFlags[i].myTeam)
                {
                    droppedFlagIndex = i;
                    break;
                }
            }

            // 모든 클라이언트에게 깃발을 이 위치에 떨어뜨리라고 명령!
            if (droppedFlagIndex != -1)
                photonView.RPC(nameof(DropFlagRPC), RpcTarget.All, droppedFlagIndex, player.transform.position);

        }


        CheckRoundEnd();
    }

    [PunRPC]
    private void DropFlagRPC(int _flagEnemyIndex, Vector3 _dropPos)
    {
        mapFlags[_flagEnemyIndex].DropFlag(_dropPos);
        if(mapFlags[_flagEnemyIndex].myTeam != playerRegistry.MyTeam)
        {
            flagPointer.UpdateEnemyObject(mapFlags[_flagEnemyIndex].gameObject);
            flagPointer.HasEnemyFlag = false;
        }
        if (mapFlags[_flagEnemyIndex].myTeam == playerRegistry.MyTeam)
        {
            flagPointer.UpdateAllyObject(mapFlags[_flagEnemyIndex].gameObject);
            flagPointer.HasAllyFlag = false;
        }
    }

    #endregion

    #region 라운드 체크

    // -----------------------------------------------
    // 라운드 체크
    // -----------------------------------------------


    // 상황 A: 빈손으로 적 깃발을 만짐 -> 획득!
    // 여기서 마스터 클라이언트에게 "나 이거 먹었어!" 라고 RPC를 쏴서 동기화
    public void OnLocalPlayerTouchedFlag(int enemyflagTeam)
    {
        // 1. 레지스트리에서 '나'를 찾는다. 
        int myActorNumber = PhotonNetwork.LocalPlayer.ActorNumber;

        if (!playerRegistry.TryGetPlayerByActorNumber(myActorNumber, out PlayerController myPlayer)) return;

        if (myPlayer == null || myPlayer.GetPlayerState == PlayerController.PlayerState.Dead) return;

        if (!myPlayer.HasEnemyFlag && myPlayer.MyTeam != enemyflagTeam)
        {
            photonView.RPC(nameof(ProcessFlagPickupRPC), RpcTarget.All, myActorNumber, enemyflagTeam);
        }
    }

    // 상황 B: 적 깃발을 들고 우리 팀 깃발(베이스)을 만짐 -> 득점!
    // 득점 RPC 호출 (섬멸전 때 짰던 OnRoundEndRPC 재활용)
    public void OnLocalPlayerReachedGoal(int goalTeam)
    {
        int myActorNumber = PhotonNetwork.LocalPlayer.ActorNumber;

        if (!playerRegistry.TryGetPlayerByActorNumber(myActorNumber, out PlayerController myPlayer)) return;

        if (myPlayer == null || myPlayer.GetPlayerState == PlayerController.PlayerState.Dead) return;

        if (myPlayer.HasEnemyFlag && myPlayer.MyTeam == goalTeam)
        {
            photonView.RPC(nameof(OnRoundEndRPC), RpcTarget.All, myPlayer.MyTeam);
        }
    }

    [PunRPC]
    private void ProcessFlagPickupRPC(int _myActorNumber, int _flagEnemyTeam)
    {
        if (!playerRegistry.TryGetPlayerByActorNumber(_myActorNumber, out PlayerController myPlayer)) return;

        myPlayer.GetFlag();
        Flag stolenFlag = null;
        for (int i = 0; i < mapFlags.Length; ++i)
        {
            if (mapFlags[i].myTeam == _flagEnemyTeam)
            {
                stolenFlag = mapFlags[i];
                break;
            }
        }
        if (stolenFlag == null) return;
        if (stolenFlag.myTeam == playerRegistry.MyTeam) // 아군 깃발을 집으면 깃발과 깃발 쥔 사람이 표시되어야 함
        {
            flagPointer.UpdateAllyObject(myPlayer.gameObject);
            flagPointer.HasAllyFlag = true;
        }
        else  // 적 깃발을 집으면 깃발과 깃발 쥔 사람이 표시되어야 함
        {
            flagPointer.UpdateEnemyObject(myPlayer.gameObject);
            flagPointer.HasEnemyFlag = true;
            if(PhotonNetwork.LocalPlayer.ActorNumber == _myActorNumber)
            {
                flagPointer.SetBaseCamp((int)PhotonNetwork.LocalPlayer.CustomProperties["Team"]);
                flagPointer.HasEnemyFlag = false;
            }
        }
        stolenFlag.HideFlag();

        
    }

    private void CheckRoundEnd()
    {
        bool teamAAlive = IsTeamAlive(playerRegistry.TeamA);
        bool teamBAlive = IsTeamAlive(playerRegistry.TeamB);

        Debug.Log($"[GameManager] Team A Alive : {teamAAlive}, Team B Alive : {teamBAlive}");

        if (!teamAAlive)
        {
            // B팀 승리
            photonView.RPC(nameof(OnRoundEndRPC), RpcTarget.All, 2);
        }
        else if (!teamBAlive)
        {
            // A팀 승리
            photonView.RPC(nameof(OnRoundEndRPC), RpcTarget.All, 1);
        }
    }

    private bool IsTeamAlive(List<PlayerController> team)
    {
        if (team == null) return false;

        foreach (var player in team)
        {
            // 1단계: 메모리 상에 객체가 존재하는지 (MissingReference 방지)
            if (player == null) continue;

            // 2단계: 유니티 객체로서 유효한지 확인
            if (!player.gameObject) continue;

            // 3단계: 사망 확인
            // 내부 상태 값을 검사
            if (player.GetPlayerState != PlayerController.PlayerState.Dead)
            {
                return true; // 한 명이라도 켜져 있으면 팀은 살아있음
            }
        }
        return false;
    }

    #endregion

    #region 라운드 종료

    // -----------------------------------------------
    // 라운드 종료
    // -----------------------------------------------

    [PunRPC]
    private void OnRoundEndRPC(int winTeam)
    {
        if (winTeam == 1) teamAScore++;
        else teamBScore++;

        Debug.Log($"[GameManager] 라운드 종료 | A팀: {teamAScore} / B팀: {teamBScore}");

        if (teamAScore >= winScore || teamBScore >= winScore)
        {
            OnMatchEnd(winTeam);
            return;
        }

        GameEvents.RoundEnd(winTeam);

        StartCoroutine(NextRoundCoroutine());
    }

    private IEnumerator NextRoundCoroutine()
    {
        foreach(var player in playerRegistry.TeamA)
        {
            player.OnRoundEndReset();
        }
        foreach(var player in playerRegistry.TeamB)
        {
            player.OnRoundEndReset();
        }

        Debug.Log($"[GameManager] {roundStartDelay}초 후 다음 라운드 시작");
        yield return new WaitForSeconds(roundStartDelay);
        FogOfWar.Instance.ResetFogOfWar();
        StartRound();
    }



    #endregion

    #region 라운드 시작

    // -----------------------------------------------
    // 라운드 시작
    // -----------------------------------------------

    public void StartRound()
    {

        if (!PhotonNetwork.IsMasterClient) return;
        photonView.RPC(nameof(StartRoundRPC), RpcTarget.All);
    }

    [PunRPC]
    private void StartRoundRPC()
    {
        GameEvents.ScoreChanged(teamAScore, teamBScore);
        GameEvents.RoundStart();

        for (int i = 0; i < mapFlags.Length; i++)
        {
            mapFlags[i].RespawnFlag();
        }

        for (int i = 0; i < mapFlags.Length; i++)   // 나중에 리펙토링
        {
            if (mapFlags[i].myTeam != playerRegistry.MyTeam)
            {
                flagPointer.UpdateEnemyObject(mapFlags[i].gameObject);
                flagPointer.HasEnemyFlag = false;
                
            }
            else
            {
                // 같은 팀 플래그 넣는 메소드 호출
                flagPointer.UpdateAllyObject(null);
                flagPointer.HasAllyFlag = false;
            }
        }

        RespawnTeam(playerRegistry.TeamA, teamASpawnPoints);
        RespawnTeam(playerRegistry.TeamB, teamBSpawnPoints);
        Debug.Log("[GameManager] 라운드 시작");
    }

    private void RespawnTeam(List<PlayerController> team, Transform[] spawnPoints)
    {
        for (int i = 0; i < team.Count; i++)
        {
            if (team[i] == null) continue;

            Vector3 spawnPos = spawnPoints[i % spawnPoints.Length].position;
            Debug.Log($"[GameManager] {team[i].photonView.Owner.NickName}'s SpawnPoint Is : {spawnPos}"); 
            //team[i].gameObject.SetActive(true);
            team[i].Respawn(spawnPos);

            // 매니저가 플레이어를 스폰시키면서 UI 매니저에게 슬롯 등록/갱신 지시!
            int actorNum = team[i].photonView.Owner.ActorNumber;
            string nickName = team[i].photonView.Owner.NickName;
            int teamNum = team[i].MyTeam;

            GameEvents.PlayerUIInit(actorNum, nickName, teamNum);
        }
    }

    #endregion

    #region 매치 종료

    // -----------------------------------------------
    // 매치 종료
    // -----------------------------------------------
    private void OnMatchEnd(int winTeam)
    {
        Debug.Log($"[GameManager] 매치 종료 | 승리팀: {(winTeam == 1 ? "A팀" : "B팀")}");

        // 이벤트 버스를 통해 UI 매니저 등에게 알림
        GameEvents.MatchEnd(winTeam);

        // 기존의 로비 복귀 코루틴은 그대로 유지
        StartCoroutine(ReturnToLobbyCoroutine());
    }

    private IEnumerator ReturnToLobbyCoroutine()
    {
        yield return new WaitForSeconds(5f);
        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.LoadLevel(lobbySceneName); // 실제 로비 씬 이름으로 변경
        }
    }

    #endregion

    private IEnumerator CountDownCoroutine()
    {
        yield return new WaitForSeconds(3f);
        
        StartRound();
    }

    #region 게임 나가기

    // UIManager에서 버튼을 누르면 호출됨
    public void LeaveGame()
    {
        Debug.Log("[GameManager] 플레이어가 방 나가기를 요청했습니다.");

        // 포톤 룸에서 퇴장 요청
        PhotonNetwork.LeaveRoom();
    }

    // 포톤 서버가 "방 퇴장 완료"를 확정 지어주면 자동으로 실행되는 콜백
    public override void OnLeftRoom()
    {
        base.OnLeftRoom();
        Debug.Log("[GameManager] 방 퇴장 완료. 로비 씬으로 이동합니다.");

        // 방을 완전히 나갔으므로 씬을 로드함
        SceneManager.LoadScene(lobbySceneName);
    }

    // 상대방이 중간에 나갔을 떄 - 레지스트리 정리, 라운드 종료 체크
    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        base.OnPlayerLeftRoom(otherPlayer);
        playerRegistry.RemoveFromTeam(otherPlayer.ActorNumber);

        if (PhotonNetwork.IsMasterClient)
            CheckRoundEnd();
    }


    #endregion
}

