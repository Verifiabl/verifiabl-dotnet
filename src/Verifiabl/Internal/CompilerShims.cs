#if !NET7_0_OR_GREATER
// Attributes the C# compiler emits for language features newer than net472's
// BCL. Roslyn binds them by full name, so internal copies satisfy both this
// assembly and downstream compilations reading its metadata.
namespace System.Runtime.CompilerServices;

// Enables C# init-only setters.
internal static class IsExternalInit;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property,
    AllowMultiple = false,
    Inherited = false)]
internal sealed class RequiredMemberAttribute : Attribute;

[AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
internal sealed class CompilerFeatureRequiredAttribute(string featureName) : Attribute
{
    public string FeatureName { get; } = featureName;

    public bool IsOptional { get; init; }
}
#endif
