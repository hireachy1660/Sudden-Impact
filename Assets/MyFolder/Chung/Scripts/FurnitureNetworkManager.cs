using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;

/// <summary>
/// 씬의 모든 Furniture를 단일 PhotonView로 관리.
/// RPC: 피격/파괴 동기화 | RoomProperties: 늦참 동기화
/// </summary>
public class FurnitureNetworkManager : MonoBehaviourPunCallbacks
{
    public static FurnitureNetworkManager Instance { get; private set; }

    private const string STATE_KEY = "FS";

    private Furniture[] _furnitures;
    private byte[] _stateBytes;

    // ─────────────────────────────────────────────────────────
    private void Awake()
    {
        Instance = this;

        // 위치 기준 오름차순 정렬 → 모든 클라이언트에서 동일한 인덱스 보장
        _furnitures = FindObjectsOfType<Furniture>()
            .OrderBy(f => f.transform.position.x * 10000f + f.transform.position.z)
            .ToArray();

        int byteCount = Mathf.CeilToInt(_furnitures.Length / 8f);
        _stateBytes = new byte[byteCount];

        for (int i = 0; i < _furnitures.Length; i++)
            _furnitures[i].SetIndex(i);

        Debug.Log($"[FurnitureNetworkManager] 가구 {_furnitures.Length}개 등록 ({byteCount} bytes)");
    }

    private void Start()
    {
        // 늦참 동기화: 방에 이미 있던 파괴 상태 복원
        if (PhotonNetwork.InRoom &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(STATE_KEY, out object val))
        {
            ApplyState((byte[])val);
            Debug.Log("[FurnitureNetworkManager] 늦참 동기화 완료");
        }
    }

    // ─────────────────────────────────────────────────────────
    // 피격 → 매니저 PhotonView로 단일 RPC 전송
    public void ReportHit(int index, float damage, Vector3 hitNormal)
    {
        if (!PhotonNetwork.IsConnected)
        {
            // 오프라인 테스트용
            _furnitures[index].LocalApplyDamage(damage, hitNormal);
            return;
        }
        photonView.RPC(nameof(RPC_ApplyDamage), RpcTarget.All, index, damage, hitNormal);
    }

    [PunRPC]
    private void RPC_ApplyDamage(int index, float damage, Vector3 hitNormal)
    {
        if (index < 0 || index >= _furnitures.Length) return;
        _furnitures[index].LocalApplyDamage(damage, hitNormal);
    }

    // ─────────────────────────────────────────────────────────
    // 파괴 확정 → MasterClient가 RoomProperties 업데이트
    public void ReportDestroyed(int index)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        _stateBytes[index / 8] |= (byte)(1 << (index % 8));
        PhotonNetwork.CurrentRoom.SetCustomProperties(
            new Hashtable { { STATE_KEY, (byte[])_stateBytes.Clone() } });
    }

    // 룸 프로퍼티 변경 콜백 (MasterClient 포함 모든 클라이언트)
    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        if (!propertiesThatChanged.ContainsKey(STATE_KEY)) return;
        ApplyState((byte[])propertiesThatChanged[STATE_KEY]);
    }

    private void ApplyState(byte[] state)
    {
        _stateBytes = (byte[])state.Clone();
        for (int i = 0; i < _furnitures.Length; i++)
        {
            bool destroyed = (state[i / 8] & (1 << (i % 8))) != 0;
            if (destroyed && !_furnitures[i].IsDestroyed)
                _furnitures[i].ForceBreak();
        }
    }
}
