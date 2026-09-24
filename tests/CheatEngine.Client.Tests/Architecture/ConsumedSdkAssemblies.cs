using ReflectionAssembly = System.Reflection.Assembly;

namespace CheatEngine.Client.Tests.Architecture;

/// <summary>The CheatEngine.SDK runtime assemblies that the shipped Client assemblies reference.</summary>
/// <remarks>
///     They are the assemblies of the consumed package (the pin of <c>eng/CheatEngineSdk.props</c>), loaded from the test
///     output. The ratchets read their public surface and metadata only; no SDK code runs.
/// </remarks>
internal static class ConsumedSdkAssemblies
{
	private static readonly Lazy<ReflectionAssembly[]> LazyAll = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

	/// <summary>Every referenced SDK runtime assembly, ordered by simple name.</summary>
	internal static IReadOnlyList<ReflectionAssembly> All => LazyAll.Value;

	/// <summary>Finds a public SDK type by its full metadata name.</summary>
	/// <returns>The type, or <see langword="null" /> when no referenced SDK assembly exports it.</returns>
	internal static Type? FindPublicType(string fullName)
	{
		return All.Select(assembly => assembly.GetType(fullName, throwOnError: false))
			.FirstOrDefault(static type => type is { IsPublic: true } or { IsNestedPublic: true });
	}

	private static ReflectionAssembly[] Load()
	{
		return
		[
			.. ClientAssemblyCatalog.LoadAll()
				.SelectMany(static assembly => assembly.GetReferencedAssemblies())
				.Where(static name => name.Name is not null && MetadataSurface.IsSdkAssembly(name.Name))
				.DistinctBy(static name => name.Name, StringComparer.Ordinal)
				.OrderBy(static name => name.Name, StringComparer.Ordinal)
				.Select(ReflectionAssembly.Load)
		];
	}
}
