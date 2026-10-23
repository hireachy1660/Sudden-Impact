using Photon.Pun;
using TMPro;
using UnityEngine;

/// <summary>
/// 무기 픽업 노드 - 데이터 보관함.
///
/// 역할:
///   - (EWeaponType + ammo) 데이터만 보유
///   - 라운드 시작 시 WeaponSpawnManager가 SetupAndActivate()로 데이터를 주입.
///   - 플레이어가 버린 총도 이 노드 형태로 필드에 남겨짐 (RPC_CreateDropNode 경로).
///   - 플레이어가 Q키를 누르면: PlayerController → RequestPickup() → RPC_RequestPickup(MasterClient) → RPC_ForceEquipWeapon(All).
///
/// 씬 설정:
///   1. PhotonView 컴포넌트 필수 (씬 배치 오브젝트).
///   2. Collider(isTrigger=true) 추가 — 플레이어의 OnTriggerEnter로 감지.
///   3. weaponVisuals: EWeaponType별 비주얼 오브젝트 배열 (Inspector 설정).
///   4. labelText: 월드 스페이스 TextMeshPro — 가장 가까운 플레이어에게만 표시.
///   5. defaultWeaponType / defaultAmmo: spawnNode 전용 (WeaponSpawnManager가 덮어씀).
/// </summary>
public class WeaponPickupNode : MonoBehaviourPun
{
    [System.Serializable]
    public struct WeaponVisualEntry
    {
        public Weapon.EWeaponType weaponType;
        public GameObject visualObject;
    }

    [Header("Spawn Node Defaults (Inspector)")]
    [Tooltip("스폰 노드 기본 무기 타입 (WeaponSpawnManager가 라운드마다 랜덤 덮어씀)")]
    [SerializeField] private Weapon.EWeaponType defaultWeaponType = Weapon.EWeaponType.Pistol;

    [Tooltip("스폰 노드 기본 탄약 수 (WeaponSpawnManager가 설정한 값으로 덮어씀)")]
    [SerializeField] private int defaultAmmo = 30;

    [Header("Visuals")]
    [Tooltip("EWeaponType별 비주얼 오브젝트 (타입에 맞는 것만 활성화)")]
    [SerializeField] private WeaponVisualEntry[] weaponVisuals;
    [SerializeField] private GameObject nodeIndicator;

    [Tooltip("노드 위 월드 스페이스 텍스트 (가장 가까운 플레이어에게만 표시)")]
    [SerializeField] private TextMeshPro labelText;

    [Header("런타임 데이터")]
    [Tooltip("디버그용 값 넣지 말 것")]
    [SerializeField] private Weapon.EWeaponType weaponType;
    [SerializeField] private int currentAmmo;
    [SerializeField] private bool isAvailable;

    /// <summary>WeaponSpawnManager가 풀에서 자유 노드를 판별할 때 사용.</summary>
    public bool IsAvailable => isAvailable;
    /// <summary>노드가 담고있는 무기의 타입.</summary>
    public Weapon.EWeaponType WeaponType => weaponType;


    // WeaponSpawnManager에서 호출

    /// <summary>
    /// 스폰 노드 활성화 (라운드 시작).
    /// spawnNodes는 위치를 절대 이동하지 않으므로 transform.position 그대로 사용.
    /// </summary>
    public void ActivateAsSpawnNode(Weapon.EWeaponType _type, int _ammo)
    {
        SetupAndActivate(transform.position, _type, _ammo);
    }

    /// <summary>
    /// 드롭 노드 활성화 (플레이어 무기 버리기): 위치 + 타입 + 잔탄수를 외부에서 주입.
    /// dropNodePool 노드는 position이 변경될 수 있음.
    /// </summary>
    public void SetupAndActivate(Vector3 _position, Weapon.EWeaponType _type, int _ammo)
    {
        photonView.RPC(nameof(RPC_Setup), RpcTarget.All,
            _position, (int)_type, _ammo, true);
    }

    /// <summary>노드 비활성화 (픽업 완료 or 라운드 종료).</summary>
    public void Deactivate()
    {
        photonView.RPC(nameof(RPC_Setup), RpcTarget.All,
            transform.position, (int)weaponType, 0, false);
    }

    [PunRPC]
    private void RPC_Setup(Vector3 _position, int _typeInt, int _ammo, bool _visible)
    {
        transform.position = _position;
        weaponType = (Weapon.EWeaponType)_typeInt;
        currentAmmo = _ammo;
        isAvailable = _visible;

        UpdateVisual(weaponType, _visible);

        if (!_visible)
            HideLabel();
    }

    // 라벨 표시 (PlayerController.CheckClosestNode에서 호출)

    public void ShowLabel()
    {
        if (labelText == null) return;
        labelText.text = weaponType.ToString() + " \u25bc";
        labelText.gameObject.SetActive(true);
    }

    public void HideLabel()
    {
        if (labelText == null) return;
        labelText.gameObject.SetActive(false);
    }

    // 픽업 요청 (PlayerController.PickUpAndDrop에서 로컬 호출)

    /// <summary>
    /// 로컬 플레이어가 Q키를 눌렀을 때 PlayerController에서 직접 호출.
    /// MasterClient에게 장착 요청을 전달하고, 낙관적으로 노드를 즉시 숨김.
    /// </summary>
    public void RequestPickup(int _playerViewID)
    {
        if (!isAvailable) return;

        int ammoToTransfer = currentAmmo;

        // 모든 클라이언트에서 즉시 비활성화
        photonView.RPC(nameof(RPC_Setup), RpcTarget.All,
            transform.position, (int)weaponType, 0, false);

        // MasterClient에게 무기 장착 요청 (type + ammo 직접 전달)
        photonView.RPC(nameof(RPC_RequestPickup), RpcTarget.MasterClient,
            _playerViewID, (int)weaponType, ammoToTransfer);
    }

    /// <summary>
    /// MasterClient 전용: 해당 플레이어에게 RPC_ForceEquipWeapon을 방송하고, 드롭 풀로 반환.
    /// </summary>
    [PunRPC]
    public void RPC_RequestPickup(int _playerViewID, int _typeInt, int _ammo)
    {
        PhotonView pv = PhotonView.Find(_playerViewID);
        if (pv == null) return;

        // All 클라이언트의 PlayerController에서 무기를 활성화
        pv.RPC(nameof(PlayerController.RPC_ForceEquipWeapon),
            RpcTarget.All, _typeInt, _ammo);

        // 드롭 풀 노드라면 추적 목록에서 제거 (스폰 노드는 no-op)
        WeaponSpawnManager.Instance?.ReturnNodeToDropPool(this);
    }

    // 비주얼 업데이트

    private void UpdateVisual(Weapon.EWeaponType _type, bool _visible)
    {
        if (weaponVisuals == null) return;
        foreach (var entry in weaponVisuals)
        {
            if (entry.visualObject != null)
                entry.visualObject.SetActive(_visible && entry.weaponType == _type);
        }
        nodeIndicator?.SetActive(_visible);
    }
}
