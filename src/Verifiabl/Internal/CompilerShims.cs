#if !NET5_0_OR_GREATER
// Enables C# init-only setters when compiling the net472 target.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;
#endif
