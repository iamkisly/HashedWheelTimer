namespace HashedWheelTimer.Contract;

public interface ITimeout
{
    /// <summary>
    /// Returns the <see cref="ITimerTask"/> which is associated with this handle.
    /// </summary>
    ITimerTask TimerTask { get; }

    /// <summary>
    /// Returns <c>true</c> if and only if the <see cref="ITimerTask"/> associated
    /// with this handle has been expired.
    /// </summary>
    bool Expired { get; }

    /// <summary>
    /// Returns <c>true</c> if and only if the <see cref="ITimerTask"/> associated
    /// with this handle has been canceled.
    /// </summary>
    bool Canceled { get; }

    /// <summary>
    /// Attempts to cancel the <see cref="ITimerTask"/> associated with this handle.
    /// If the task has been executed or canceled already, it will return with
    /// no side effect.
    /// </summary>
    /// <returns>A <see cref="TimerTask"/> representing the asynchronous operation. Returns <c>true</c> if the cancellation completed successfully, otherwise <c>false</c>.</returns>
    bool Cancel();
}