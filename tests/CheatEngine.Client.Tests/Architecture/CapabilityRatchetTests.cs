using System.Reflection;
using System.Reflection.Metadata;

using CheatEngine.Client.Runtime;
using CheatEngine.Client.Tests.Infrastructure;

namespace CheatEngine.Client.Tests.Architecture;

/// <summary>
///     C0 capability ratchets (audit ADR-09, ADR-09a, A17-19, A17-20, SRC02-08): runtime and target observations call
///     only read-only CheatEngine.SDK operations, the one selection call is reachable only from
///     <c>ProcessClient.TryAttach</c>, and every capability has one operational adapter on the CheatEngine.SDK major the
///     Client supports (<c>_CheatEngineClientSupportedSdkMajor</c> in <c>eng/CheatEngineSdk.props</c>).
/// </summary>
/// <remarks>Everything is read from the compiled Client assemblies; no Client code runs.</remarks>
public sealed class CapabilityRatchetTests
{
	private const string CoreAssembly = "CheatEngine.Client.Core";

	private const string ObservationPortType = "CheatEngine.Client.Core.Domains.SdkRuntimeObservationPort";

	private const string SelectionPortType = "CheatEngine.Client.Core.Domains.SdkProcessSelectionPort";

	private const string SelectionPortInterface = "CheatEngine.Client.Core.Domains.IProcessSelectionPort";

	private const string ProcessClientType = "CheatEngine.Client.Core.Domains.ProcessClient";

	private const string SelectAndObserve = "RuntimeProcessOperations::SelectAndObserve";

	/// <summary>The CheatEngine.SDK types whose operations load or save a table or mutate its records, a host effect.</summary>
	private static readonly string[] TableEffectTypes =
	[
		"CheatEngine.SDK.Engine.AddressList.AddressListMutations",
		"CheatEngine.SDK.Engine.Tables.CheatTableFiles"
	];

	/// <summary>The CheatEngine.SDK types whose operations observe or select Cheat Engine's host and target.</summary>
	private static readonly string[] RuntimeOperationTypes =
	[
		"CheatEngine.SDK.Engine.Processes.RuntimeHostOperations",
		"CheatEngine.SDK.Engine.Processes.RuntimeObservations",
		"CheatEngine.SDK.Engine.Processes.RuntimeProcessOperations",
		"CheatEngine.SDK.Engine.Targets.TargetSelection"
	];

	/// <summary>
	///     The exact read-only CheatEngine.SDK operations the observation port calls: observations, never effects. Each one
	///     is used, so the allowlist cannot pass vacuously.
	/// </summary>
	private static readonly string[] ReadOnlySdkOperations =
	[
		"RuntimeHostOperations::ObserveHost",
		"RuntimeHostOperations::TryGetCheatEngineFileVersion",
		"RuntimeHostOperations::TryGetOperatingSystem",
		"RuntimeHostOperations::TryGetSystemArchitecture",
		"RuntimeHostOperations::TryIsCheatEngine64Bit",
		"RuntimeObservations::TryObserveRuntimeInfo",
		"RuntimeProcessOperations::ObserveCurrent",
		"RuntimeProcessOperations::ObserveTargetArchitecture",
		"RuntimeProcessOperations::TryGetConfiguredPointerSize",
		"TargetSelection::ObserveCurrent",
		"TargetSelection::ValidateCurrent"
	];

	/// <summary>The CheatEngine.SDK types that register and unregister symbols, a host effect.</summary>
	private static readonly string[] SymbolRegistrationTypes =
	[
		"CheatEngine.SDK.Engine.Inspection.SymbolRegistrationLease",
		"CheatEngine.SDK.Engine.Inspection.SymbolRegistry"
	];

	/// <summary>Types that observe the runtime and must never reach a binding or an SDK operation with a host effect.</summary>
	private static readonly string[] ObservationOnlyTypes =
	[
		"CheatEngine.Client.Core.Domains.RuntimeClient",
		"CheatEngine.Client.Core.Domains.RuntimeObservationMapping",
		"CheatEngine.Client.Core.Domains.RuntimeObserver",
		ObservationPortType,
		"CheatEngine.Client.Core.Domains.TargetArchitectureObserver",
		"CheatEngine.Client.Core.Infrastructure.ConsumedSdkIdentity"
	];

	/// <summary>
	///     The public client interface of every <c>ClientCapabilityId</c> and the one Core adapter that serves it (plan
	///     L18: no capability is contract-only). Names, not types, so the experimental interfaces need no suppression.
	/// </summary>
	private static readonly (string Capability, string Contract, string Adapter)[] CapabilityAdapters =
	[
		("ProcessSelection", "CheatEngine.Client.Processes.IProcessClient",
			"CheatEngine.Client.Core.Domains.ProcessClient"),
		("TypedMemory", "CheatEngine.Client.Memory.IMemoryClient", "CheatEngine.Client.Core.Domains.MemoryClient"),
		("PatternScanning", "CheatEngine.Client.Scanning.IPatternScanner",
			"CheatEngine.Client.Core.Domains.PatternScanner"),
		("ValueScanning", "CheatEngine.Client.Scanning.IValueScanner",
			"CheatEngine.Client.Core.Domains.ValueScanning.ValueScanner"),
		("Inspection", "CheatEngine.Client.Inspection.IInspectionClient",
			"CheatEngine.Client.Core.Domains.InspectionClient"),
		("Tables", "CheatEngine.Client.Tables.ITableClient", "CheatEngine.Client.Core.Domains.TableClient"),
		("ProtectedLua", "CheatEngine.Client.Lua.ILuaClient", "CheatEngine.Client.Core.Domains.LuaClient"),
		("UnsafeLuaExecution", "CheatEngine.Client.Lua.IUnsafeLuaClient",
			"CheatEngine.Client.Core.Domains.UnsafeLuaClient"),
		("Allocations", "CheatEngine.Client.Allocations.IAllocationClient",
			"CheatEngine.Client.Core.Domains.Allocations.AllocationClient"),
		("Assembly", "CheatEngine.Client.Assembly.IAssemblyClient",
			"CheatEngine.Client.Core.Domains.Assembly.AssemblyClient"),
		("AutoAssemblerPatches", "CheatEngine.Client.Assembly.IAutoAssemblerClient",
			"CheatEngine.Client.Core.Domains.Assembly.AutoAssemblerClient")
	];

	[Fact]
	[Trait("Qualification", "Q45")]
	public void RuntimeProbeCallsOnlyReadOnlySdkOperations()
	{
		List<SdkCall> sdkCalls = ReadSdkCalls();
		string[] runtimeCalls =
		[
			.. sdkCalls.Where(static call => RuntimeOperationTypes.Contains(call.DeclaringType, StringComparer.Ordinal))
				.Select(static call => call.ToString())
		];
		string[] portOperations =
		[
			.. sdkCalls.Where(static call => call.OuterType == ObservationPortType &&
											 RuntimeOperationTypes.Contains(call.DeclaringType, StringComparer.Ordinal))
				.Select(static call => call.Operation)
				.Distinct(StringComparer.Ordinal)
				.Order(StringComparer.Ordinal)
		];
		string[] outsideThePorts =
		[
			.. sdkCalls.Where(static call => RuntimeOperationTypes.Contains(call.DeclaringType, StringComparer.Ordinal) &&
											 call.OuterType != ObservationPortType &&
											 !(call.OuterType == SelectionPortType && call.Operation == SelectAndObserve))
				.Select(static call => call.ToString())
		];
		string[] selectionPortOperations =
		[
			.. sdkCalls.Where(static call => call.OuterType == SelectionPortType)
				.Select(static call => call.Operation)
				.Distinct(StringComparer.Ordinal)
		];
		string[] tableCalls =
		[
			.. sdkCalls.Where(static call => ObservationOnlyTypes.Contains(call.OuterType, StringComparer.Ordinal) &&
											 TableEffectTypes.Contains(call.DeclaringType, StringComparer.Ordinal))
				.Select(static call => call.ToString())
		];
		string[] symbolCalls =
		[
			.. sdkCalls.Where(static call => ObservationOnlyTypes.Contains(call.OuterType, StringComparer.Ordinal) &&
											 SymbolRegistrationTypes.Contains(call.DeclaringType, StringComparer.Ordinal))
				.Select(static call => call.ToString())
		];

		// The allowlist is exact and non-empty: every read-only operation is used, and nothing else is.
		Assert.NotEmpty(ReadOnlySdkOperations);
		Assert.True(ReadOnlySdkOperations.SequenceEqual(portOperations, StringComparer.Ordinal),
			"The observation port calls SDK operations outside the read-only allowlist, or no longer calls one of " +
			"them (Q45):" + Environment.NewLine + string.Join(Environment.NewLine, portOperations));
		Assert.True(outsideThePorts.Length == 0,
			"Only the observation port, and the selection port for SelectAndObserve, may call a CheatEngine.SDK runtime " +
			"or process operation:" + Environment.NewLine + string.Join(Environment.NewLine, outsideThePorts));
		Assert.Equal([SelectAndObserve], selectionPortOperations);
		Assert.DoesNotContain(runtimeCalls, static call => call.Contains("::SelectAndObserve(", StringComparison.Ordinal) &&
														   !call.StartsWith(SelectionPortType + ".", StringComparison.Ordinal));
		Assert.True(tableCalls.Length == 0,
			"An observation-only type references CheatTableFiles or AddressListMutations (Q45):" + Environment.NewLine +
			string.Join(Environment.NewLine, tableCalls));
		Assert.True(symbolCalls.Length == 0,
			"An observation-only type references the CheatEngine.SDK symbol registry (Q45):" + Environment.NewLine +
			string.Join(Environment.NewLine, symbolCalls));
	}

	[Fact]
	[Trait("Qualification", "Q45")]
	public void SelectAndObserveIsReachableOnlyFromTryAttach()
	{
		// The one Cheat Engine call of the Processes domain that changes the selected target: every call to the
		// selection port, through its interface or its production type, is in ProcessClient.TryAttach (its lambda or
		// local function), so no observation can select a process.
		List<(string Type, string Method)> callers = [];
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
				if (reader.GetString(target.Name) == "SelectAndObserve" &&
					declaringType is SelectionPortInterface or SelectionPortType)
				{
					callers.Add((reference.OuterType, reference.Method));
				}
			}
		});

		Assert.NotEmpty(callers);
		Assert.All(callers, static caller =>
		{
			Assert.Equal(ProcessClientType, caller.Type);
			Assert.True(caller.Method == "TryAttach" || caller.Method.StartsWith("<TryAttach>", StringComparison.Ordinal),
				$"{caller.Type}.{caller.Method} calls SelectAndObserve outside TryAttach.");
		});
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void EveryCapabilityHasAnOperationalAdapter()
	{
		// SRC02-08, plan L18: on the supported CheatEngine.SDK major every capability's client interface has exactly
		// one implementation, its operational Core adapter, and no Client assembly keeps an Unavailable* placeholder
		// that refuses every operation. Making a domain contract-only again must update this test deliberately,
		// together with the catalog's implementation gate and the capability tables.
		Type[] types = [.. ClientAssemblyCatalog.LoadAll().SelectMany(static assembly => assembly.GetTypes())];
		Type[] concrete = [.. types.Where(static type => type is { IsInterface: false, IsAbstract: false })];
		string[] capabilities =
		[
			.. typeof(ClientCapabilityId).GetProperties(BindingFlags.Public | BindingFlags.Static)
				.Where(static property => property.PropertyType == typeof(ClientCapabilityId))
				.Select(static property => property.Name)
				.Order(StringComparer.Ordinal)
		];
		int referencedSdkMajor = ClientAssemblyCatalog.Load(CoreAssembly).GetReferencedAssemblies()
			.Single(static name => name.Name == "CheatEngine.SDK.Engine").Version!.Major;

		Assert.Equal(SdkPin.SupportedMajor, referencedSdkMajor);
		Assert.Equal(capabilities,
			CapabilityAdapters.Select(static adapter => adapter.Capability).Order(StringComparer.Ordinal));
		foreach ((string capability, string contract, string adapter) in CapabilityAdapters)
		{
			Type contractType = Assert.Single(types, type => type.FullName == contract);
			string[] implementations =
			[
				.. concrete.Where(type => contractType.IsAssignableFrom(type))
					.Select(static type => type.FullName!)
					.Order(StringComparer.Ordinal)
			];
			Assert.True(implementations.SequenceEqual([adapter], StringComparer.Ordinal),
				$"{capability}: {contract} must be implemented only by its operational adapter {adapter}, not by " +
				$"[{string.Join(", ", implementations)}].");
		}

		Assert.DoesNotContain(concrete, static type => type.Name.StartsWith("Unavailable", StringComparison.Ordinal));
	}

	/// <summary>Returns every reference from a Core method body to a CheatEngine.SDK member.</summary>
	private static List<SdkCall> ReadSdkCalls()
	{
		List<SdkCall> calls = [];
		ClientAssemblyCatalog.ReadMetadata(CoreAssembly, (reader, peReader) =>
		{
			foreach (MetadataSurface.IlReference reference in MetadataSurface.ReadIlReferences(reader, peReader))
			{
				if (MetadataSurface.AsMemberReference(reader, reference.Token) is not { } handle)
				{
					continue;
				}

				string member = MetadataSurface.DescribeMember(reader, handle,
					out MetadataSurface.TypeIdentity declaringType);
				if (declaringType.IsSdk)
				{
					string name = reader.GetString(reader.GetMemberReference(handle).Name);
					calls.Add(new SdkCall(reference.OuterType, reference.Method, declaringType.FullName, name, member));
				}
			}
		});

		return calls;
	}

	/// <summary>One reference from a Core method body (by outermost type) to a CheatEngine.SDK member.</summary>
	private sealed record SdkCall(string OuterType, string Method, string DeclaringType, string Name, string Member)
	{
		/// <summary>Gets the operation as <c>ShortType::Name</c>, for example <c>RuntimeHostOperations::ObserveHost</c>.</summary>
		internal string Operation => $"{DeclaringType[(DeclaringType.LastIndexOf('.') + 1)..]}::{Name}";

		public override string ToString()
		{
			return $"{OuterType}.{Method} -> {Member}";
		}
	}
}
