namespace CheatEngine.Client.Tests.Architecture;

/// <summary>
///     The public surface of 1.x keeps out of the namespaces that the Client removed or never shipped, and Core, the
///     CheatEngine.SDK-facing implementation, exports nothing.
/// </summary>
/// <remarks>
///     A list of forbidden namespaces, not an exact pin of the surface: the PublicAPI files and their analyzers pin every
///     symbol, and a 1.x minor release may add types anywhere else.
/// </remarks>
public sealed class PublicSurfaceInventoryTests
{
	/// <summary>The namespaces no shipped Client assembly exports a type in, nested namespaces included.</summary>
	private static readonly string[] ForbiddenNamespaces =
	[
		"CheatEngine.Client.Abstractions",
		"CheatEngine.Client.Timers",
		"CheatEngine.Client.Hotkeys",
		"CheatEngine.Client.Debugger",
		"CheatEngine.Client.Speed",
		"CheatEngine.Client.Hashing",
		"CheatEngine.Client.Dbvm",
		"CheatEngine.Client.RemoteExecution",
		"CheatEngine.Client.Events"
	];

	[Fact]
	public void NoShippedAssemblyExportsATypeInAForbiddenNamespace()
	{
		string[] offenders =
		[
			.. ClientAssemblyCatalog.LoadAll()
				.SelectMany(static assembly => assembly.GetExportedTypes())
				.Where(static type => type.Namespace is { } ns && ForbiddenNamespaces.Any(forbidden =>
					ns == forbidden || ns.StartsWith(forbidden + ".", StringComparison.Ordinal)))
				.Select(static type => $"{type.Assembly.GetName().Name}: {type.FullName}")
		];

		Assert.True(offenders.Length == 0,
			"These types are exported in a namespace the 1.x surface does not have: " + string.Join(", ", offenders));
	}

	[Fact]
	public void CoreExportsNoType()
	{
		Assert.Empty(ClientAssemblyCatalog.Load("CheatEngine.Client.Core").GetExportedTypes());
	}

	[Fact]
	public void TheInventoryInspectsEveryShippedAssembly()
	{
		Assert.Equal(
			[
				"CheatEngine.Client", "CheatEngine.Client.Abstractions", "CheatEngine.Client.Core",
				"CheatEngine.Client.Extensions.DependencyInjection", "CheatEngine.Client.Fluent",
				"CheatEngine.Client.Hosting"
			],
			ClientAssemblyCatalog.LoadAll().Select(static assembly => assembly.GetName().Name!)
				.Order(StringComparer.Ordinal));
	}
}
