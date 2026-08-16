namespace BrokenCompilation;

public sealed class BrokenService
{
    public MissingResult Execute(UnknownRequest request) => request.CreateResult();
}
