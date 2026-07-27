namespace ThemeStudio.Core.Runtime;

public enum BrokerObservation
{
    NoCodex,
    ManagedCodex,
    UnmanagedCodex,
    CdpReady,
    ApplySucceeded,
    ApplyFailed
}
public enum BrokerAction
{
    Wait,
    LaunchManaged,
    RestartManagedOnce,
    ApplyTheme,
    LeaveNative,
    ReportFailure
}

public sealed class BrokerStateMachine(bool restartUnmanagedCodex)
{
    private bool _restartSpent;

    public BrokerAction Observe(BrokerObservation observation) => observation switch
    {
        BrokerObservation.NoCodex => BrokerAction.Wait,
        BrokerObservation.ManagedCodex => BrokerAction.Wait,
        BrokerObservation.CdpReady => BrokerAction.ApplyTheme,
        BrokerObservation.ApplySucceeded => BrokerAction.Wait,
        BrokerObservation.ApplyFailed => BrokerAction.ReportFailure,
        BrokerObservation.UnmanagedCodex when restartUnmanagedCodex && !_restartSpent => SpendRestart(),
        BrokerObservation.UnmanagedCodex => BrokerAction.LeaveNative,
        _ => BrokerAction.Wait
    };

    public void ResetSession() => _restartSpent = false;

    private BrokerAction SpendRestart()
    {
        _restartSpent = true;
        return BrokerAction.RestartManagedOnce;
    }
}
