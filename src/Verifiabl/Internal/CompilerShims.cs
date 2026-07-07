#if !NET5_0_OR_GREATER
// Enables C# init-only setters when compiling the netstandard2.0 target.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;
#endif
