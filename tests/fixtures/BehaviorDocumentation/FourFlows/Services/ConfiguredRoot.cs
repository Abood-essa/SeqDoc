namespace BehaviorDocumentation.FourFlows.Services;

public sealed class ConfiguredRoot(GuardedChild child)
{
    public void Execute(bool enabled)
    {
        child.Execute(enabled);
    }
}

public sealed class GuardedChild(GuardedLeaf leaf)
{
    public void Execute(bool enabled)
    {
        if (enabled)
        {
            leaf.Emit();
        }

        for (var index = 0; index < 1; index++)
        {
            leaf.Noise();
        }
    }
}

public sealed class GuardedLeaf
{
    public void Emit() { }

    public void Noise() { Tail(); }

    public void Tail() { }
}
