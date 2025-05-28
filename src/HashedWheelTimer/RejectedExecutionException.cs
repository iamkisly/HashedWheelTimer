namespace HashedWheelTimer;

public class RejectedExecutionException : Exception
{
    public RejectedExecutionException() : base() { }
    public RejectedExecutionException(string message) : base(message) { }
}