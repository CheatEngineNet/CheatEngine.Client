using System.Reflection.Metadata;

namespace CheatEngine.Client.Tests.Architecture;

/// <summary>
///     Finds the uses of the CheatEngine.SDK Lua runtime, Lua stack and ownership surface in a Client assembly's method
///     bodies, so that each one is either sanctioned typed SDK API or registered ADR-01 debt.
/// </summary>
/// <remarks>
///     <para>
///         ADR-01: the SDK is the only native authority. The scanner no longer decides what is forbidden: it reports every
///         use of the scoped surface, and <see cref="ArchitectureRatchetTests" /> classifies each one against two exact
///         inventories. A member of <c>SanctionedSdkLuaSurface</c> is typed SDK API that CheatEngine.SDK imposes (its
///         admission and its owners); every other use is raw Lua or ownership work and must be a <c>FrozenLuaUsage</c>
///         entry.
///     </para>
///     <para>
///         Each use is attributed to the outermost declaring type of the method body that makes it (closures and state
///         machines fold into their container).
///     </para>
/// </remarks>
internal static class LuaUsageScanner
{
	/// <summary>Any SDK member that takes or returns a Lua state works on the Lua stack directly.</summary>
	private const string LuaStateTypeName = "CheatEngine.SDK.Lua.State.LuaState";

	/// <summary>The Lua runtime, stack, reference, marshalling and generator-helper namespaces of the SDK.</summary>
	private static readonly string[] ScopedNamespaces =
	[
		"CheatEngine.SDK.Lua.State.",
		"CheatEngine.SDK.Lua.Runtime.",
		"CheatEngine.SDK.Lua.References.",
		"CheatEngine.SDK.Lua.Marshalling.",
		"CheatEngine.SDK.Lua.CompilerServices."
	];

	/// <summary>SDK types outside those namespaces that expose raw Lua errors or SDK ownership.</summary>
	private static readonly string[] ScopedTypes =
	[
		"CheatEngine.SDK.Lua.Calls.LuaError",
		"CheatEngine.SDK.Engine.Objects.Owned`1",
		"CheatEngine.SDK.Engine.Objects.StringList"
	];

	/// <summary>The raw property, method and destroy calls of an SDK object handle.</summary>
	private static readonly string[] ScopedObjectMembers =
	[
		"CheatEngine.SDK.Engine.Objects.CEObject::TryCallMethod",
		"CheatEngine.SDK.Engine.Objects.CEObject::TryGetProperty",
		"CheatEngine.SDK.Engine.Objects.CEObject::TrySetProperty",
		"CheatEngine.SDK.Engine.Objects.CEObject::TryDestroy"
	];

	/// <summary>Returns every use of the scoped SDK surface in the assembly, sorted and without duplicates.</summary>
	internal static LuaSurfaceUse[] Scan(string assemblyName)
	{
		SortedSet<LuaSurfaceUse> uses = new(Comparer<LuaSurfaceUse>.Create(static (left, right) =>
			string.CompareOrdinal(left.ToString(), right.ToString())));
		ClientAssemblyCatalog.ReadMetadata(assemblyName, (reader, peReader) =>
		{
			foreach (MetadataSurface.IlReference reference in MetadataSurface.ReadIlReferences(reader, peReader))
			{
				if (Describe(reader, reference.Token) is { } symbol && IsInScope(symbol))
				{
					uses.Add(new LuaSurfaceUse(reference.OuterType, symbol));
				}
			}
		});
		return [.. uses];
	}

	private static string? Describe(MetadataReader reader, EntityHandle token)
	{
		if (MetadataSurface.AsMemberReference(reader, token) is { } member)
		{
			string description = MetadataSurface.DescribeMember(reader, member,
				out MetadataSurface.TypeIdentity declaringType);
			return declaringType.IsSdk ? description : null;
		}

		if (token.Kind is HandleKind.TypeReference or HandleKind.TypeSpecification)
		{
			MetadataSurface.TypeIdentity type = MetadataSurface.ResolveType(reader, token);
			return type.IsSdk ? type.FullName : null;
		}

		return null;
	}

	private static bool IsInScope(string symbol)
	{
		string typeName = symbol.Split("::", 2)[0];
		return ScopedNamespaces.Any(ns => typeName.StartsWith(ns, StringComparison.Ordinal)) ||
			   ScopedTypes.Contains(typeName, StringComparer.Ordinal) ||
			   ScopedObjectMembers.Any(member => symbol.StartsWith(member, StringComparison.Ordinal)) ||
			   symbol.Contains(LuaStateTypeName, StringComparison.Ordinal);
	}
}

/// <summary>One use of the scoped SDK surface: the outermost Client type and the SDK symbol it references.</summary>
/// <param name="OuterType">The outermost declaring type of the method body that makes the use.</param>
/// <param name="Symbol">The SDK member (<c>Type::Name(parameters)-&gt;return</c>) or type.</param>
internal readonly record struct LuaSurfaceUse(string OuterType, string Symbol)
{
	/// <summary>The inventory key: <c>OuterType -&gt; Symbol</c>.</summary>
	public override string ToString()
	{
		return $"{OuterType} -> {Symbol}";
	}
}
