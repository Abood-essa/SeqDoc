namespace WindowsDependency;

public sealed class WindowsDependencyMarker;

#if NET10_0_WINDOWS
public sealed class DependencyEvaluatedAsWindows;
#else
public sealed class DependencyEvaluatedAsPortable;
#endif
