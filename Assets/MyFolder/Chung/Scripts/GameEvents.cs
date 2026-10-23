using System;
using UnityEngine;

// 게임 전역 이벤트 버스 (static 클래스).
public static class GameEvents
{
    public static event Action<float> OnHpChanged;

    //구르기 시작 시 쿨다운 지속 시간을 전달(PlayerWorldUI 링 애니메이션용)
    public static event Action<float> OnRollStarted;
    public static event Action<string, bool> OnWeaponChanged;
    public static event Action<int, int> OnAmmoChanged;
    public static event Action<int, int> OnScoreChanged;
    public static Action<float, float, float> OnSpreadUpdated;
    // 킬로그 (공격자 이름, 사망자 이름)
    public static event Action<string, string> OnKillLog;

    // 플레이어 UI 초기화 (액터번호, 이름, 팀번호)
    public static event Action<int, string, int> OnPlayerUIInit;

    // 플레이어 사망 UI (액터번호)
    public static event Action<int> OnPlayerUIDead;

    // 라운드 시작 / 종료
    public static event Action OnRoundStart;
    public static event Action<int> OnRoundEnd;
    public static event Action<int> OnMatchEnd; // 승리 팀 번호를 전달

    // 무기노드에 닿았을 떄
    public static event Action<bool, Vector3, string> OnPickupUIUpdate; // (켜기/끄기, 월드좌표, 무기이름)

    // 메뉴열기
    public static event Action<bool> OnMenuUIUpdate;

    // 플로팅 텍스트 용
    public static event Action<Vector3> OnKillFloat;

    public static event Action OnLocalPlayerDeath;



    #region 호출 메서드
    //  호출 메서드
    public static void HpChanged(float _hp)                             
        => OnHpChanged?.Invoke(_hp);

    //구르기 쿨다운 이벤트 호출 메서드
    public static void RollStarted(float _cooldownDuration)
        => OnRollStarted?.Invoke(_cooldownDuration);

    public static void WeaponChanged(string _name, bool _isGun)         
        => OnWeaponChanged?.Invoke(_name, _isGun);

    public static void AmmoChanged(int _curAmmo, int _maxAmmo)          
        => OnAmmoChanged?.Invoke(_curAmmo, _maxAmmo);

    public static void SpreadUpdate(float _curAimSpread, float _RecoveryRate, float baseSpread)                       
        => OnSpreadUpdated?.Invoke(_curAimSpread, _RecoveryRate, baseSpread);

    public static void ScoreChanged(int _aTeam, int _bTeam)             
        => OnScoreChanged?.Invoke(_aTeam, _bTeam);

    public static void Kill(string _killName, string _dieName)          
        => OnKillLog?.Invoke(_killName, _dieName);

    public static void PlayerUIInit(int _no, string _name, int _team)   
        => OnPlayerUIInit?.Invoke(_no, _name, _team);

    public static void PlayerUIDead(int _actorNumber)                   
        => OnPlayerUIDead?.Invoke(_actorNumber);

    public static void RoundStart()                                     
        => OnRoundStart?.Invoke();

    public static void RoundEnd(int _winTeam)                                       
        => OnRoundEnd?.Invoke(_winTeam);


    public static void MatchEnd(int _winTeam)                           
        => OnMatchEnd?.Invoke(_winTeam);

    public static void PickupUIUpdate(bool _isShow, Vector3 _pos, string _text = "")
    => OnPickupUIUpdate?.Invoke(_isShow, _pos, _text);

    public static void MenuUIUpdate(bool _isShow)
        => OnMenuUIUpdate?.Invoke(_isShow);

    public static void KillFloat(Vector3 position)
    => OnKillFloat?.Invoke(position);

    public static void LocalPlayerDeath()
    => OnLocalPlayerDeath?.Invoke();

    #endregion

}