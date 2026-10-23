/// <summary>
/// 데미지를 송신 받는 객체가 상속 받아야 하는 인터페이스
/// </summary>
public interface IAttackReceiver
{
    void OnReceiveImpact(ImpactData data);
}
