namespace SmartX.Desktop.Client.Services;

// the screen can only be touched from its own thread, so work done in the background hands its result over through here
public interface IUiDispatcher
{
    void Invoke(Action action);
}

public sealed class ImmediateDispatcher : IUiDispatcher
{
    public static readonly ImmediateDispatcher Instance = new();

    public void Invoke(Action action) => action();
}
