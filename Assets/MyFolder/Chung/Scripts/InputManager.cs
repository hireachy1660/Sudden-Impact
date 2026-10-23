using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    [SerializeField] private InputActionAsset myInputAction;
    [SerializeField] private PlayerRegistry myPlayerRegistry;

    [Header("Cinemachine Aim Settings")]
    [Tooltip("씨네머신 타겟용 오브젝트")]
    [SerializeField] private Transform mouseAimTarget; 
    [Tooltip("씨네머신 타겟 그룹의 가중치")]
    [SerializeField] private float aimExpansionWeight = 0.5f;
    [SerializeField] private CinemachineTargetGroup targetGroup;

    [Header("ForDebug")]
    [SerializeField] private PlayerController player;

    private InputActionMap playerMap;

    private InputAction onSprintAction;
    private InputAction onMoveAction;
    private InputAction onMousePosAction;
    private InputAction onRollAction;
    private InputAction onDropAction;
    private InputAction onFireAction;
    private InputAction onSwapAction;
    private InputAction onInteractAction;
    private InputAction onZoomAction;
    private InputAction onMenuAction;

    private Camera myMainCamera;
    private Plane aimPlane;
    private Vector3 worldAimPosition;
    
    private bool isFireHeld = false;
    private bool isAiming = false;
    private bool isOnMenu = false;


    private void Awake()
    {
        playerMap = myInputAction.FindActionMap("Player");
        playerMap.Enable();

        onSprintAction = myInputAction.FindAction("Sprint");
        onMousePosAction = myInputAction.FindAction("MousePosition");
        onMoveAction = myInputAction.FindAction("Move");
        onRollAction = myInputAction.FindAction("Roll");
        onDropAction = myInputAction.FindAction("Drop");
        onFireAction = myInputAction.FindAction("Fire");
        onSwapAction = myInputAction.FindAction("Swap");
        onInteractAction = myInputAction.FindAction("Interact");
        onZoomAction = myInputAction.FindAction("Zoom");
        onMenuAction = myInputAction.FindAction("Menu");

        SetConnectActionMap(false);

        myPlayerRegistry.OnPlayerRegistered += GetmyPlayer;
        myMainCamera = Camera.main;
    }

    private void OnDestroy()
    {
        myPlayerRegistry.OnPlayerRegistered -= GetmyPlayer;
        playerMap.Disable();
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
        if (player == null ) return;

        Ray ray = myMainCamera.ScreenPointToRay(onMousePosAction.ReadValue<Vector2>());
        if (aimPlane.Raycast(ray, out float enter))
        {
            worldAimPosition = ray.GetPoint(enter);
            if (mouseAimTarget != null)
                mouseAimTarget.position = worldAimPosition;
        }
    }

    private void HandlePlayerDeath(int deadActorNumber)
    {
        if (player != null && player.photonView.Owner.ActorNumber == deadActorNumber)
        {
            // 1. 캐릭터 조작 끄기
            SetConnectActionMap(false);

            Debug.Log("[InputManager] 로컬 플레이어 사망: 조작 차단");
        }
    }

    private void HandlePlayerRespawn()
    {
        if (player != null)
        {
            // 1. 캐릭터 조작 켜기
            SetConnectActionMap(true);

            Debug.Log("[InputManager] 로컬 플레이어 리스폰: 조작 차단 해제");
        }
    }

    private void SetConnectActionMap(bool _isConnect)
    {
        if (_isConnect)
        {
            playerMap.Enable();
        }
        else
        {
            playerMap.Disable(); 
            onMenuAction.Enable(); // 메뉴 끄기 버튼(ESC)만 다시 살리기
        }
    }

    private IEnumerator GetInputValue()
    {
        while (player != null)
        {
            yield return new WaitForFixedUpdate();

            if (!playerMap.enabled) continue;

            OnMove(onMoveAction.ReadValue<Vector2>());
            //OnRotate(onMousePosAction.ReadValue<Vector2>());
            player.RotatePlayer(worldAimPosition);

            isAiming = onZoomAction.IsPressed();
            // 우클릭(조준) 버튼을 누르고 있다면 가중치를 올림
            UpdateTargetGroupWeight(isAiming);


            if (isFireHeld)
            {
                player.TryAttack(worldAimPosition, true);
            }
        }
    }

    private void OnMove(Vector2 _moveAxis)
    {
        player.MovePlayer(new Vector3(_moveAxis.x, 0f, _moveAxis.y));
    }

    //private void OnRotate(Vector2 _mouseScreenPos)
    //{
    //    Ray ray = myMainCamera.ScreenPointToRay(_mouseScreenPos);

    //    if (aimPlane.Raycast(ray, out float enter))
    //    {
    //        worldAimPosition = ray.GetPoint(enter);

    //        smoothedAimPosition = Vector3.Lerp(smoothedAimPosition, worldAimPosition, Time.deltaTime * 20f);

    //        if (mouseAimTarget != null)
    //        {
    //            mouseAimTarget.position = smoothedAimPosition;
    //        }
    //    }

    //    player.RotatePlayer(worldAimPosition);
    //}

    private void UpdateTargetGroupWeight(bool isAiming)
    {
        if (targetGroup == null || targetGroup.Targets.Count < 2) return;

        float targetWeight = isAiming ? 0.8f : 0f;

        var target = targetGroup.Targets[1];

        // 부드럽게 보간(Lerp)하며 가중치 변경
        target.Weight = Mathf.Lerp(target.Weight, targetWeight, Time.fixedDeltaTime * 5f);

        // 수정된 구조체를 다시 리스트에 할당 (덮어쓰기)
        targetGroup.Targets[1] = target;
    }

    private void OnFireStart(InputAction.CallbackContext ctx)
    {
        isFireHeld = true;
        player.TryAttack(worldAimPosition, false);
    }

    private void OnFireEnd(InputAction.CallbackContext ctx)
    {
        isFireHeld = false;
    }

    private void OnMenuToggle(InputAction.CallbackContext ctx)
    {
        isOnMenu = !isOnMenu;

        // 캐릭터 조작 끄기/켜기
        SetConnectActionMap(!isOnMenu);

        // 마우스 커서 상태 변경
        if (isOnMenu)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
        else
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Confined;
        }

        // UI 매니저로 이벤트 발송
        GameEvents.MenuUIUpdate(isOnMenu);
    }

    private void SetPlayerAction()
    {
        onSprintAction.performed += player.SprintStart;
        onSprintAction.canceled += player.SprintEnd;
        onRollAction.performed += player.TryRoll;
        onDropAction.performed += player.PickUpAndDrop;
        onSwapAction.performed += player.TrySwapWeapon;
        onInteractAction.performed += player.TryInteract;

        onMenuAction.performed += OnMenuToggle;

        onFireAction.performed += OnFireStart;
        onFireAction.canceled += OnFireEnd;

    }

    private void GetmyPlayer(PlayerController _player)
    {
        player = _player;
        SetPlayerAction();

        player.StunCallback = SetConnectActionMap;

        aimPlane = new Plane(Vector3.up, new Vector3(0, player.transform.position.y, 0));

        StartCoroutine(GetInputValue());
    }
}
