using Photon.Pun;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [Header("CameraRference")]
    [SerializeField] private PlayerRegistry playerRegistry;
    [SerializeField] private CinemachineCamera virtualCam;
    [SerializeField] private CinemachineTargetGroup targetGroup; 
    [SerializeField] private Transform mouseAimTarget;

    [Header("Camera Tilt")]
    [SerializeField] private float maxTiltAngle = 2.0f;
    [SerializeField] private float tiltSpeed = 5.0f;

    [Header("Observer Mode")]
    [SerializeField] private float observerMoveSpeed = 15f;

    [Header("ForDebug")]
    [SerializeField] private PlayerController player;

    private GameObject observerTarget; // 가짜 타겟
    private bool isObserverMode = false;

    private void Awake()
    {
        playerRegistry.OnPlayerRegistered += SetCameraTarget;
        observerTarget = new GameObject("ObserverTarget");
    }

    private void OnEnable()
    {
        GameEvents.OnPlayerUIDead += HandlePlayerDeath;
        GameEvents.OnRoundStart += HandlePlayerRespawn;
    }

    private void OnDisable()
    {
        GameEvents.OnPlayerUIDead -= HandlePlayerDeath;
        GameEvents.OnRoundStart -= HandlePlayerRespawn;
    }

    private void LateUpdate()
    {
        if (virtualCam == null || Mouse.current == null) return;


        Vector2 mousePos = Mouse.current.position.ReadValue();

        float mouseXRatio = mousePos.x / Screen.width;
        float target = (mouseXRatio - 0.5f) * 2f * -maxTiltAngle;

        virtualCam.Lens.Dutch = Mathf.LerpAngle(virtualCam.Lens.Dutch, target, Time.deltaTime * tiltSpeed);

        if (isObserverMode && observerTarget != null && Keyboard.current != null)
        {
            float h = (Keyboard.current.dKey.isPressed ? 1f : 0f) - (Keyboard.current.aKey.isPressed ? 1f : 0f);
            float v = (Keyboard.current.wKey.isPressed ? 1f : 0f) - (Keyboard.current.sKey.isPressed ? 1f : 0f);
            observerTarget.transform.position += new Vector3(h, 0f, v).normalized * observerMoveSpeed * Time.deltaTime;
        }
    }

    private void HandlePlayerDeath(int deadActorNumber)
    {
        if (player != null && player.photonView.Owner.ActorNumber == deadActorNumber)
        {
            isObserverMode = true; // 옵저버 모드 ON

            // 가짜 타겟을 내 시체 위치로 가져옴
            observerTarget.transform.position = player.transform.position;

            // 카메라에게 타겟을 가짜로 변경
            virtualCam.Follow = observerTarget.transform;
        }
    }

    private void HandlePlayerRespawn()
    {
        if (player != null)
        {
            isObserverMode = false; 

            virtualCam.Follow = targetGroup.transform;

            Debug.Log("[Camera] 옵저버 모드 해제");
        }
    }



    private void SetCameraTarget(PlayerController _player)
    {
        if (_player == null || targetGroup == null) return;

        player = _player;

        //  플레이어를 그룹 멤버 0번으로 추가 (가중치 1.0)
        targetGroup.AddMember(_player.transform, 1f, 0f);

        // 에임 타겟을 그룹 멤버 1번으로 추가 (가중치 0.3 ~ 0.5)
        // 이 가중치가 높을수록 카메라가 마우스 쪽으로 더 많이 쏠림
        targetGroup.AddMember(mouseAimTarget, 0.3f, 0f);

        // 카메라가 그룹 전체를 추적하게 설정
        virtualCam.Follow = targetGroup.transform;

        Debug.Log($"[Camera] {_player.photonView.Owner.NickName}를 카메라 타겟으로 설정 완료!");
    }

}
