namespace HashedWheelTimer.Contract;

public interface ITimeoutFactory<TTimeout> where TTimeout : class, ITimeout
{
    TTimeout Create();
    void Return(TTimeout timeout);
}