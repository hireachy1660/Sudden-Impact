/// <summary>
/// PlayerController(Core)와 같은 오브젝트에 붙는 부품이 구현하는 인터페이스.
/// 리스폰·라운드 종료는 Core가 한 번 받아 모든 부품에 차례로 알린다.
/// </summary>
public interface IPlayerPart
{
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
