/// <summary>
/// PlayerController(Core)와 같은 오브젝트에 붙는 부품이 구현하는 인터페이스.
/// 리스폰·라운드 종료는 Core가 한 번 받아 모든 부품에 차례로 알린다.
/// 부품의 규칙(책임·이벤트·네트워크 분리)은 PlayerController의 summary에 있다.
/// </summary>
public interface IPlayerPart
{
    /// <summary>Core가 Awake(CollectParts)에서 자기 참조를 넣어 준다. 컴포넌트 Awake 순서가 정해져 있지 않으므로 부품은 자기 Awake·OnEnable에서 core를 쓰지 않는다.</summary>
    void Bind(PlayerController _core);
    void OnRespawn();
    void OnRoundEndReset();
}

/// <summary>
/// 소유자(IsMine) 클라이언트에서만 의미가 있는 부품의 표지. 멤버는 없다.
/// 원격 복제본에서는 Core가 Awake에서 끄고 지운다. 그래서 이 표지를 단 부품은
/// [PunRPC]를 갖지 않고, 원격이 읽어야 하는 값도 갖지 않는다.
/// </summary>
public interface IOwnerOnlyPart : IPlayerPart
{
}
