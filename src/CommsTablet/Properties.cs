using System.Runtime.CompilerServices;

// The contract check (tools/CommsTablet.Check) reads the game members and patch targets.
[assembly: InternalsVisibleTo("CommsTablet.Check")]
#if !PUBLIC_RELEASE
[assembly: InternalsVisibleTo("CommsTabletSelfTest")]
#endif
