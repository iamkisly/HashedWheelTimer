namespace HashedWheelTimer;

public class RejectedExecutionException : Exception
{
    public RejectedExecutionException()
    {
    }

    public RejectedExecutionException(string message)
        : base(message)
    {
    }
}