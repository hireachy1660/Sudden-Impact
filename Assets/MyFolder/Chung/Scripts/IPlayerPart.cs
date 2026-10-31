/// <summary>
/// PlayerController(Core)와 같은 오브젝트에 붙는 부품이 구현하는 인터페이스.
/// 리스폰·라운드 종료는 Core가 한 번 받아 모든 부품에 차례로 알린다.
/// </summary>
public interface IPlayerPart
{
    void OnRespawn();
    void OnRoundEndReset();
}
