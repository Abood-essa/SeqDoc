namespace MultiTargetProfiles;

public sealed class CommonService;

#if WINDOWS_PROFILE
public sealed class WindowsSymbolOnly;
#elif PORTABLE_PROFILE
public sealed class PortableSymbolOnly;
#endif
