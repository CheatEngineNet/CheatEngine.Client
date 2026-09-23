using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ReflectionAssembly = System.Reflection.Assembly;

namespace CheatEngine.Client.Tests.Architecture;

/// <summary>Locates the shipped Client runtime assemblies that the architecture ratchet inspects.</summary>
/// <remarks>
///     The list is explicit so that a new shipped assembly is a reviewed change. Every assembly is loaded from the test
///     output (the same binaries the packages contain) and read as metadata; no Client code runs.
/// </remarks>
internal static class ClientAssemblyCatalog
{
	/// <summary>The Client runtime libraries in dependency order, followed by the umbrella facade.</summary>
	internal static readonly string[] Names =
	[
		"CheatEngine.Client.Abstractions",
		"CheatEngine.Client.Core",
		"CheatEngine.Client.Fluent",
		"CheatEngine.Client.Extensions.DependencyInjection",
		"CheatEngine.Client.Hosting",
		"CheatEngine.Client"
	];

	/// <summary>Loads every Client runtime assembly by name.</summary>
	internal static IEnumerable<ReflectionAssembly> LoadAll()
	{
		foreach (string name in Names)
		{
			yield return Load(name);
		}
	}

	/// <summary>Loads one Client runtime assembly by simple name.</summary>
	internal static ReflectionAssembly Load(string name)
	{
		return ReflectionAssembly.Load(name);
	}

	/// <summary>Opens the metadata of one Client runtime assembly without executing it.</summary>
	/// <param name="name">The simple assembly name.</param>
	/// <param name="action">Reads the metadata; the reader is valid only during the call.</param>
	internal static void ReadMetadata(string name, Action<MetadataReader, PEReader> action)
	{
		ArgumentNullException.ThrowIfNull(action);
		string path = Load(name).Location;
		using FileStream stream = File.OpenRead(path);
		using PEReader reader = new(stream);
		action(reader.GetMetadataReader(), reader);
	}
}
