using System.Reflection.Metadata;

namespace CheatEngine.Client.Tests.Architecture;

/// <summary>Finds direct uses of SDK Lua-stack and ownership primitives in a Client assembly's method bodies.</summary>
/// <remarks>
///     ADR-01: the SDK is the only native authority, so Client code must not touch the Lua state, frames, references,
///     marshallers, generator helpers, raw object calls, or SDK owners. Each use is attributed to the outermost declaring
///     type of the method body that makes it (closures and state machines fold into their container).
/// </remarks>
internal static class LuaUsageScanner
{
	/// <summary>Any SDK member that takes or returns a Lua state works on the Lua stack directly.</summary>
	private const string LuaStateTypeName = "CheatEngine.SDK.Lua.State.LuaState";

	private static readonly string[] ForbiddenNamespaces =
	[
		"CheatEngine.SDK.Lua.State.",
		"CheatEngine.SDK.Lua.Runtime.",
		"CheatEngine.SDK.Lua.References.",
		"CheatEngine.SDK.Lua.Marshalling.",
		"CheatEngine.SDK.Lua.CompilerServices."
	];

	private static readonly string[] ForbiddenTypes =
	[
		"CheatEngine.SDK.Lua.Calls.LuaError",
		"CheatEngine.SDK.Engine.Objects.Owned`1",
		"CheatEngine.SDK.Engine.Objects.StringList"
	];

	private static readonly string[] ForbiddenObjectMembers =
	[
		"CheatEngine.SDK.Engine.Objects.CEObject::TryCallMethod",
		"CheatEngine.SDK.Engine.Objects.CEObject::TryGetProperty",
		"CheatEngine.SDK.Engine.Objects.CEObject::TrySetProperty",
		"CheatEngine.SDK.Engine.Objects.CEObject::TryDestroy"
	];

	/// <summary>Returns sorted <c>OuterType -&gt; symbol</c> lines for every forbidden use in the assembly.</summary>
	internal static string[] Scan(string assemblyName)
	{
		SortedSet<string> uses = new(StringComparer.Ordinal);
		ClientAssemblyCatalog.ReadMetadata(assemblyName, (reader, peReader) =>
		{
			foreach (MetadataSurface.IlReference reference in MetadataSurface.ReadIlReferences(reader, peReader))
			{
				if (Describe(reader, reference.Token) is { } symbol && IsForbidden(symbol))
				{
					uses.Add($"{reference.OuterType} -> {symbol}");
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

	private static bool IsForbidden(string symbol)
	{
		string typeName = symbol.Split("::", 2)[0];
		return ForbiddenNamespaces.Any(ns => typeName.StartsWith(ns, StringComparison.Ordinal)) ||
			   ForbiddenTypes.Contains(typeName, StringComparer.Ordinal) ||
			   ForbiddenObjectMembers.Any(member => symbol.StartsWith(member, StringComparison.Ordinal)) ||
			   symbol.Contains(LuaStateTypeName, StringComparison.Ordinal);
	}
}
