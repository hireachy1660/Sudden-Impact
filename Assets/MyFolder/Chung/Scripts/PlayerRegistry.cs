using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

[CreateAssetMenu(fileName = "PlayerRegistry", menuName = "SuddenImpact/PlayerRegistry")]
public class PlayerRegistry : ScriptableObject
{
    [SerializeField] private List<PlayerController> teamA = new List<PlayerController>(); // 팀별 플레이어가 담기는 곳
    [SerializeField] private List<PlayerController> teamB = new List<PlayerController>();

    [Header("LocalData")]
    [SerializeField] private PlayerController localPlayer; // 로컬 플레이어를 담는 곳
    [SerializeField] private int myTeam; 

    public List<PlayerController> TeamA { get { return teamA; } }
    public List<PlayerController> TeamB { get{ return teamB; } }
    public PlayerController LocalPlayer { get { return localPlayer; } }
    public int MyTeam { get { return myTeam; } }

    public event Action<PlayerController> OnPlayerRegistered;

    public void RegisterLocalPlayer(PlayerController player)
    {
        localPlayer = player;
        OnPlayerRegistered?.Invoke(player);
    }

    public void RegisterMyTeam(int _team)
    {
        myTeam = _team;
    }

    public void RegisterPlayerTeam(PlayerController player, int team)
    {
        if (team == 1) teamA.Add(player);
        if (team == 2) teamB.Add(player);
    }

    public bool TryGetPlayerByActorNumber(int _actorNumber, out PlayerController _player)
    {
        foreach (PlayerController player in teamA)
        {
            if(player.photonView.Owner.ActorNumber == _actorNumber)
            {
                _player = player;
                return true;
            }
        }

        foreach (PlayerController player in teamB)
        {
            if (player.photonView.Owner.ActorNumber == _actorNumber)
            {
                _player = player;
                return true;
            }
        }

        _player = null;
        return false;
    }

    public void RemoveFromTeam(int actorNumber)
    {
        teamA.RemoveAll(p => p == null || (p.photonView != null && p.photonView.Owner.ActorNumber == actorNumber));
        teamB.RemoveAll(p => p == null || (p.photonView != null && p.photonView.Owner.ActorNumber == actorNumber));
    }

    // 게임 종료 시 반드시 초기화
    public void Clear()
    {
        localPlayer = null;
        teamA.Clear();
        teamB.Clear();
        myTeam = int.MaxValue;
    }
}