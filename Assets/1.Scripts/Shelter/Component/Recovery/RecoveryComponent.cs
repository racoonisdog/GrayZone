public interface IRecoveryComponent
{
    void CompleteShelterRecovery(NPCRuntimeData target);
}

public class RecoveryComponent : IRecoveryComponent
{
    public void CompleteShelterRecovery(NPCRuntimeData target)
    {
        if (target == null)
            return;

        target.CompleteRecovery();
    }
}
