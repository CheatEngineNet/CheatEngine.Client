using System.Reflection.Metadata;

using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;

namespace CheatEngine.Client.Tests.Architecture;

/// <summary>
///     C0 capability ratchets (audit ADR-09, ADR-09a, A17-19, A17-20, SRC02-08): runtime probes call only read-only
///     Cheat Engine globals, and contract-only domains stay unavailable while the Client consumes CheatEngine.SDK 1.x.
/// </summary>
/// <remarks>Everything is read from the compiled Client assemblies; no Client code runs.</remarks>
public sealed class CapabilityRatchetTests
{
	private const string CoreAssembly = "CheatEngine.Client.Core";

	private const string ClientLuaGlobalsType = "CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals";

	/// <summary>The only generated bindings a runtime or target-fact probe may call: observations, never effects.</summary>
	private static readonly string[] ReadOnlyGlobals =
	[
		"GetCheatEngineVersion", "GetConfiguredPointerSize", "GetOpenedProcessId", "GetSystemArchitecture",
		"GetTargetAbi", "TargetIs64Bit", "TargetIsArm", "TargetIsX86"
	];

	/// <summary>Bindings with a host effect: attach, table import/export and symbol registration.</summary>
	private static readonly string[] MutatingGlobals =
		["LoadTable", "OpenProcess", "RegisterSymbol", "SaveTable", "UnregisterSymbol"];

	/// <summary>The probe types and, when a type also owns effects, the probe members that are scanned.</summary>
	private static readonly Dictionary<string, string[]?> ProbeMembers = new(StringComparer.Ordinal)
	{
		["CheatEngine.Client.Core.Domains.LuaRuntimeProbe"] = null,
		["CheatEngine.Client.Core.Domains.SdkMemoryCodecContextPort"] = null,
		["CheatEngine.Client.Core.Domains.LocalProcessHost"] =
		[
			"GetOpenedProcessId", "TargetIs64Bit", "TargetIsX86", "TargetIsArm", "GetConfiguredPointerSize"
		]
	};

	/// <summary>Types that observe the runtime and must never reach a binding with a host effect.</summary>
	private static readonly string[] ObservationOnlyTypes =
	[
		"CheatEngine.Client.Core.Domains.RuntimeClient",
		"CheatEngine.Client.Core.Domains.TargetArchitectureObserver",
		"CheatEngine.Client.Core.Domains.ProbeClassifier",
		"CheatEngine.Client.Core.Domains.LuaRuntimeProbe",
		"CheatEngine.Client.Core.Infrastructure.ConsumedSdkIdentity"
	];

	[Fact]
	[Trait("Qualification", "Q45")]
	public void RuntimeProbeCallsOnlyReadOnlyGlobals()
	{
		List<(string Type, string Method, string Global)> calls = ReadClientLuaGlobalsCalls();
		string[] probeCalls = calls
			.Where(static call => ProbeMembers.TryGetValue(call.Type, out string[]? members) &&
								  (members is null || members.Contains(call.Method, StringComparer.Ordinal)))
			.Select(static call => $"{call.Type}.{call.Method} -> {call.Global}")
			.ToArray();
		string[] forbiddenProbeCalls = probeCalls
			.Where(static call => !ReadOnlyGlobals.Any(global => call.EndsWith("-> " + global, StringComparison.Ordinal)))
			.ToArray();
		string[] effectsFromObservers = calls
			.Where(static call => ObservationOnlyTypes.Contains(call.Type, StringComparer.Ordinal) &&
								  MutatingGlobals.Contains(call.Global, StringComparer.Ordinal))
			.Select(static call => $"{call.Type}.{call.Method} -> {call.Global}")
			.ToArray();

		// Every production probe is scanned: LuaRuntimeProbe alone calls the eight read-only observations.
		Assert.True(probeCalls.Length >= ReadOnlyGlobals.Length,
			"The probe scan found too few binding calls; it would pass vacuously." + Environment.NewLine +
			string.Join(Environment.NewLine, probeCalls));
		Assert.True(forbiddenProbeCalls.Length == 0,
			"A runtime or target-fact probe calls a binding outside the read-only allowlist (Q45):" +
			Environment.NewLine + string.Join(Environment.NewLine, forbiddenProbeCalls));
		Assert.True(effectsFromObservers.Length == 0,
			"An observation-only type references a binding with a host effect (Q45):" + Environment.NewLine +
			string.Join(Environment.NewLine, effectsFromObservers));
		Assert.Contains(calls, static call => call.Global == "OpenProcess" &&
											  call.Type == "CheatEngine.Client.Core.Domains.LocalProcessHost");
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void ContractOnlyDomainsHaveNoOperationalImplementationWhileTheSdkMajorIsOne()
	{
		// SRC02-08: an Allocation or Assembly folder never activates the capability; adopting CheatEngine.SDK 2.0 must
		// update this test deliberately, together with the capability gates and docs/migration/sdk-2.0.md.
		Type[] implementations = ClientAssemblyCatalog.LoadAll()
			.SelectMany(static assembly => assembly.GetTypes())
			.Where(static type => type is { IsInterface: false, IsAbstract: false } &&
								  (typeof(IAllocationClient).IsAssignableFrom(type) ||
								   typeof(IAssemblyClient).IsAssignableFrom(type)))
			.ToArray();
		int referencedSdkMajor = ClientAssemblyCatalog.Load(CoreAssembly).GetReferencedAssemblies()
			.Single(static name => name.Name == "CheatEngine.SDK.Engine").Version!.Major;

		Assert.Equal(1, referencedSdkMajor);
		Assert.Equal(
			[
				"CheatEngine.Client.Core.Domains.Allocations.UnavailableAllocationClient",
				"CheatEngine.Client.Core.Domains.Assembly.UnavailableAssemblyClient"
			],
			implementations.Select(static type => type.FullName!).Order(StringComparer.Ordinal));
	}

	/// <summary>Returns every call from a Core method body to a generated <c>ClientLuaGlobals</c> binding.</summary>
	private static List<(string Type, string Method, string Global)> ReadClientLuaGlobalsCalls()
	{
		List<(string Type, string Method, string Global)> calls = [];
		ClientAssemblyCatalog.ReadMetadata(CoreAssembly, (reader, peReader) =>
		{
			foreach (MetadataSurface.IlReference reference in MetadataSurface.ReadIlReferences(reader, peReader))
			{
				if (reference.Token.Kind != HandleKind.MethodDefinition)
				{
					continue;
				}

				MethodDefinition target = reader.GetMethodDefinition((MethodDefinitionHandle) reference.Token);
				string declaringType = MetadataSurface.ResolveTypeDefinition(reader, target.GetDeclaringType()).FullName;
				if (declaringType == ClientLuaGlobalsType && reference.OuterType != ClientLuaGlobalsType)
				{
					calls.Add((reference.OuterType, reference.Method, reader.GetString(target.Name)));
				}
			}
		});

		return calls;
	}
}
