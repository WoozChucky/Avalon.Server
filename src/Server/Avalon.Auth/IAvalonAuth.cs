namespace Avalon.Auth;

public interface IAvalonAuth
{
    void Start();
    void Stop();
    void Update(TimeSpan deltaTime);
    
    long GetLoopCounter();
    void IncrementLoopCounter();
    bool IsRunning();
}
