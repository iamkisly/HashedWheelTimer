namespace HashedWheelTimer.Contract;

public interface ITimerFactory
{
    HashedWheelTimer Create();
}