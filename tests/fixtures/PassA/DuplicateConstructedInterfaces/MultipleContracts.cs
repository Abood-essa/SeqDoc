namespace DuplicateConstructedInterfaces;

public interface IContract<T>;

public sealed class First;

public sealed class Second;

public sealed class MultipleContracts : IContract<First>, IContract<Second>;
