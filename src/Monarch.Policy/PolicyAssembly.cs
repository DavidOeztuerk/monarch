using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Monarch.Policy.Tests")]

namespace Monarch.Policy;

/// <summary>Marker type that identifies the Monarch policy assembly.</summary>
public static class PolicyAssembly
{
    /// <summary>Gets the logical name of this assembly.</summary>
    public const string Name = "Monarch.Policy";
}
