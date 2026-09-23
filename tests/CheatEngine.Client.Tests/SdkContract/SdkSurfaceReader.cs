using System.Reflection.Metadata;

using CheatEngine.Client.Tests.Architecture;

namespace CheatEngine.Client.Tests.SdkContract;

/// <summary>Computes the CheatEngine.SDK surface that the compiled Client assemblies actually consume.</summary>
internal static class SdkSurfaceReader
{
	/// <summary>
	///     Returns one sorted line per SDK type reference (<c>T</c>) and member reference (<c>M</c>) of each shipped Client
	///     assembly, prefixed with the consuming assembly name.
	/// </summary>
	internal static string[] ReadConsumedSurface()
	{
		SortedSet<string> lines = new(StringComparer.Ordinal);
		foreach (string consumer in ClientAssemblyCatalog.Names)
		{
			ClientAssemblyCatalog.ReadMetadata(consumer, (reader, _) =>
			{
				foreach (TypeReferenceHandle handle in reader.TypeReferences)
				{
					MetadataSurface.TypeIdentity type = MetadataSurface.ResolveType(reader, handle);
					if (type.IsSdk)
					{
						lines.Add($"{consumer} T {type.FullName}");
					}
				}

				foreach (MemberReferenceHandle handle in reader.MemberReferences)
				{
					string member = MetadataSurface.DescribeMember(reader, handle,
						out MetadataSurface.TypeIdentity declaringType);
					if (declaringType.IsSdk)
					{
						lines.Add($"{consumer} M {member}");
					}
				}
			});
		}

		return [.. lines];
	}

	/// <summary>Returns the SDK member references made from method bodies of one type of an assembly.</summary>
	internal static string[] ReadMemberReferencesFrom(string assemblyPath, string outermostTypeFullName)
	{
		SortedSet<string> members = new(StringComparer.Ordinal);
		using FileStream stream = File.OpenRead(assemblyPath);
		using System.Reflection.PortableExecutable.PEReader peReader = new(stream);
		MetadataReader reader = peReader.GetMetadataReader();
		foreach (MetadataSurface.IlReference reference in MetadataSurface.ReadIlReferences(reader, peReader))
		{
			if (!string.Equals(reference.OuterType, outermostTypeFullName, StringComparison.Ordinal) ||
				MetadataSurface.AsMemberReference(reader, reference.Token) is not { } memberHandle)
			{
				continue;
			}

			string member = MetadataSurface.DescribeMember(reader, memberHandle,
				out MetadataSurface.TypeIdentity declaringType);
			if (declaringType.IsSdk)
			{
				members.Add(member);
			}
		}

		return [.. members];
	}
}
