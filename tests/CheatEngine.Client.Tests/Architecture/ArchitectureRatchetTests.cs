using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using CheatEngine.Client.Tables;

using ReflectionAssembly = System.Reflection.Assembly;

namespace CheatEngine.Client.Tests.Architecture;

/// <summary>
///     C0 architecture ratchet for ADR-01 ("the SDK is the only native authority") and ADR-06 ("ownership is explicit"):
///     the Client never declares native imports, never references the SDK ABI or Lua interop assemblies, reaches the SDK
///     Lua runtime only through reviewed typed SDK API, and keeps its remaining direct Lua and owner usages frozen to a
///     reviewed list that may only shrink.
/// </summary>
/// <remarks>
///     <para>
///         Everything is read from the compiled Client assemblies with System.Reflection.Metadata; no Client code runs.
///     </para>
///     <para>
///         <see cref="FrozenLuaGlobals" /> and <see cref="FrozenLuaUsage" /> are the registered ADR-01 debt of the Client.
///         <see cref="FrozenLuaGlobals" /> is empty and stays empty: the Client binds no Cheat Engine global itself. Each
///         <see cref="FrozenLuaUsage" /> entry states why it exists and how it ends: the CheatEngine.SDK primitive that
///         replaces it and the plan lot that removes it, the SDK primitive that is still missing, or a permanent reason.
///         The replacing members are resolved in the consumed SDK, so a stale name fails here. Shrinking a list is always
///         allowed; growing it requires a registered exception with its own reason and its replacing or missing SDK
///         primitive, here, not in an external document.
///     </para>
///     <para>
///         <see cref="SanctionedSdkLuaSurface" /> is the exact inventory of the typed SDK Lua API the Client is expected to
///         use (admission, the external reset fact, SDK owners), one reason per member. It is not debt, but it is exact
///         too: an unused member leaves it, and a new one is a reviewed addition.
///     </para>
/// </remarks>
public sealed partial class ArchitectureRatchetTests
{
	private const string Adr01Guidance =
		"ADR-01 exception: register it in the ratchet (tests/CheatEngine.Client.Tests/Architecture) with its reason and " +
		"the CheatEngine.SDK primitive that replaces it or is missing, or route the work through the SDK.";

	private const string TableClientType = "CheatEngine.Client.Core.Domains.TableClient";

	private const string UnsafeLuaClientType = "CheatEngine.Client.Core.Domains.UnsafeLuaClient";

	private const string UnsafeLuaReason =
		"CheatEngine.SDK 2.0.0 exposes no protected chunk-execution service; caller-supplied Lua runs only behind " +
		"EnableUnsafeLuaExecution";

	/// <summary>
	///     The Lua globals the Client may bind itself with <c>[LuaGlobal]</c>. Empty since the table files moved to
	///     CheatEngine.SDK's <c>CheatTableFiles</c>: every Cheat Engine global the Client calls goes through a typed SDK
	///     service. <see cref="FrozenLuaGlobalsStaysEmpty" /> keeps it empty; a new binding is not a registrable exception.
	/// </summary>
	private static readonly string[] FrozenLuaGlobals = [];

	/// <summary>
	///     Every direct use of the SDK Lua stack or of SDK ownership in Client code that is not sanctioned typed SDK API, by
	///     outermost declaring type. Ordered ordinally; it may only shrink.
	/// </summary>
	private static readonly FrozenLuaUse[] FrozenLuaUsage =
	[
		new(TableClientType,
			"CheatEngine.SDK.Engine.Objects.CEObject::TryGetProperty``2(System.ReadOnlySpan`1<byte>,!!1&)->boolean",
			"Record snapshots read Count, which ChildCount keeps for ADR-08 precision (A3): CheatEngine.SDK 2.0.0 only " +
			"offers MemoryRecord.TryGetChild(int), which conflates out-of-range with failure; every other field is " +
			"read through the typed MemoryRecord getters",
			new LuaDebtKind.AwaitingSdkPrimitive("no MemoryRecord child-count getter in CheatEngine.SDK 2.0.0")),
		new(UnsafeLuaClientType,
			"CheatEngine.SDK.Lua.Calls.LuaError::FromStack(CheatEngine.SDK.Lua.State.LuaState,CheatEngine.SDK.Lua.Calls.LuaStatus)->CheatEngine.SDK.Lua.Calls.LuaError",
			UnsafeLuaReason, new LuaDebtKind.Permanent()),
		new(UnsafeLuaClientType, "CheatEngine.SDK.Lua.Calls.LuaError::get_Message()->string", UnsafeLuaReason,
			new LuaDebtKind.Permanent()),
		new(UnsafeLuaClientType, "CheatEngine.SDK.Lua.State.LuaFrame::.ctor(CheatEngine.SDK.Lua.State.LuaState)->void",
			UnsafeLuaReason, new LuaDebtKind.Permanent()),
		new(UnsafeLuaClientType, "CheatEngine.SDK.Lua.State.LuaFrame::Dispose()->void", UnsafeLuaReason,
			new LuaDebtKind.Permanent()),
		new(UnsafeLuaClientType,
			"CheatEngine.SDK.Lua.State.LuaState::TryExecute(System.ReadOnlySpan`1<byte>,int32,System.ReadOnlySpan`1<byte>)->CheatEngine.SDK.Lua.Calls.LuaStatus",
			UnsafeLuaReason, new LuaDebtKind.Permanent())
	];

	/// <summary>
	///     The typed CheatEngine.SDK Lua API the Client uses, member by member, each with its reason. Exact: every member
	///     is used, and every use of the scanned SDK surface is either one of these members or a
	///     <see cref="FrozenLuaUsage" /> entry.
	/// </summary>
	private static readonly SanctionedSdkLuaMember[] SanctionedSdkLuaSurface =
	[
		new("CheatEngine.SDK.Engine.Objects.Owned`1::ReleaseWithOutcome()->CheatEngine.SDK.Engine.Targets.TargetReleaseOutcome",
			"Releases the AOB result-list owner that AobScanner.TryScanOutcome hands out, once and without throwing, " +
			"and reports the outcome; OwnershipHandoff and then the match list are the only release authority (F13). " +
			"The 1:1 replacement of Owned`1::Dispose (L11)"),
		new("CheatEngine.SDK.Engine.Objects.Owned`1::get_Value()->!0",
			"Reads the StringList of the AOB result-list owner, the only result shape AobScanner.TryScanOutcome returns"),
		new("CheatEngine.SDK.Engine.Objects.StringList::TryGetCount(int32&)->boolean",
			"Counts the AOB result rows through the SDK's typed StringList"),
		new("CheatEngine.SDK.Engine.Objects.StringList::TryGetItem(int32,string&)->boolean",
			"Reads one AOB result row through the SDK's typed StringList"),
		new("CheatEngine.SDK.Lua.Runtime.LuaRuntime::TryAcquireOperationWithOutcome(CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation&)->CheatEngine.SDK.Lua.Runtime.LuaAdmissionStatus",
			"The SDK's non-throwing Lua admission with its factual LuaAdmissionStatus; LuaAdmission is its only caller " +
			"and classifies every refusal", "CheatEngine.Client.Core.Infrastructure.LuaAdmission"),
		new("CheatEngine.SDK.Lua.Runtime.LuaRuntime::get_ExternalStateResetDetected()->boolean",
			"The SDK's sticky external Lua state reset fact; SdkBoundary reads it to report a plain " +
			"InvalidOperationException as RuntimeChanged", "CheatEngine.Client.Core.Infrastructure.SdkBoundary"),
		new("CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation::Dispose()->void",
			"Ends an admission the SDK granted, on the acquiring thread, before control returns to Cheat Engine"),
		new("CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation::get_State()->CheatEngine.SDK.Lua.State.LuaState",
			"The Lua state of an admitted operation; every raw use of that state is a FrozenLuaUsage entry of its own")
	];

	private static readonly Dictionary<string, string[]> AllowedSdkAssemblyReferences = new(StringComparer.Ordinal)
	{
		["CheatEngine.Client.Abstractions"] = ["CheatEngine.SDK.Engine"],
		["CheatEngine.Client.Fluent"] = ["CheatEngine.SDK.Engine"],
		["CheatEngine.Client.Extensions.DependencyInjection"] = ["CheatEngine.SDK.Engine"],
		["CheatEngine.Client.Core"] =
			["CheatEngine.SDK.Engine", "CheatEngine.SDK.Lua", "CheatEngine.SDK.Annotations", "CheatEngine.SDK.Hosting"],
		["CheatEngine.Client.Hosting"] = ["CheatEngine.SDK.Hosting"],
		["CheatEngine.Client"] = []
	};

	private static readonly string[] ForbiddenRuntimeReferences =
	[
		"CheatEngine.SDK.Abi",
		"CheatEngine.SDK.Lua.Interop",
		"CheatEngine.Client.SourceGenerators.Lua"
	];

	private static readonly string[] ForbiddenInteropAttributes =
	[
		"System.Runtime.InteropServices.DllImportAttribute",
		"System.Runtime.InteropServices.LibraryImportAttribute",
		"System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute"
	];

	[Fact]
	public void ClientAssembliesDeclareNoNativeImports()
	{
		List<string> violations = [];
		foreach (string assembly in ClientAssemblyCatalog.Names)
		{
			violations.AddRange(FindNativeImports(ClientAssemblyCatalog.Load(assembly).Location));
		}

		Assert.True(violations.Count == 0,
			string.Join(Environment.NewLine, violations.Select(static violation => $"{violation} {Adr01Guidance}")));
	}

	[Fact]
	public void NativeImportScanDetectsEveryForbiddenForm()
	{
		string[] violations = FindNativeImports(typeof(NativeImportFixture).Assembly.Location)
			.Where(static violation => violation.Contains(nameof(NativeImportFixture), StringComparison.Ordinal) ||
									   violation.Contains("NativeLibrary", StringComparison.Ordinal))
			.ToArray();

		Assert.Contains(violations, static violation => violation.Contains("P/Invoke", StringComparison.Ordinal));
		Assert.Contains(violations, static violation => violation.Contains("UnmanagedCallersOnly", StringComparison.Ordinal));
		Assert.Contains(violations, static violation => violation.Contains("function pointer", StringComparison.Ordinal));
		Assert.Contains(violations, static violation => violation.Contains("NativeLibrary", StringComparison.Ordinal));
	}

	[Fact]
	public void OnlyCoreAndHostingReferenceSdkRuntimeAssemblies()
	{
		List<string> violations = [];
		foreach (string assembly in ClientAssemblyCatalog.Names)
		{
			HashSet<string> allowed = new(AllowedSdkAssemblyReferences[assembly], StringComparer.Ordinal);
			ClientAssemblyCatalog.ReadMetadata(assembly, (reader, _) =>
			{
				foreach (AssemblyReferenceHandle handle in reader.AssemblyReferences)
				{
					string name = reader.GetString(reader.GetAssemblyReference(handle).Name);
					if (ForbiddenRuntimeReferences.Contains(name, StringComparer.Ordinal))
					{
						violations.Add($"{assembly} references {name} at runtime. {Adr01Guidance}");
					}
					else if (MetadataSurface.IsSdkAssembly(name) && !allowed.Contains(name))
					{
						violations.Add($"{assembly} references {name}; only Core (and Hosting for the plugin base) may " +
									   $"reference SDK runtime assemblies beyond descriptive Engine values. {Adr01Guidance}");
					}
				}
			});
		}

		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
	}

	/// <summary>
	///     Core stays logger-free (audit ch.24, open issue O3): its diagnostic events go through an internal sink that the
	///     dependency-injection package implements, so Core never references a logging assembly or package.
	/// </summary>
	[Fact]
	public void CoreReferencesNoLoggingAssembly()
	{
		List<string> references = [];
		ClientAssemblyCatalog.ReadMetadata("CheatEngine.Client.Core", (reader, _) =>
		{
			foreach (AssemblyReferenceHandle handle in reader.AssemblyReferences)
			{
				references.Add(reader.GetString(reader.GetAssemblyReference(handle).Name));
			}
		});

		Assert.NotEmpty(references);
		Assert.DoesNotContain(references, static name =>
			name.StartsWith("Microsoft.Extensions.Logging", StringComparison.Ordinal) ||
			name.StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal) ||
			name.Equals("Serilog", StringComparison.Ordinal) || name.StartsWith("NLog", StringComparison.Ordinal));
	}

	[Fact]
	public void LuaGlobalBindingsAreFrozenToTheRegisteredAdr01Exceptions()
	{
		List<string> violations = [];
		foreach (ReflectionAssembly assembly in ClientAssemblyCatalog.LoadAll())
		{
			foreach (Type type in assembly.GetTypes())
			{
				foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
															   BindingFlags.Static | BindingFlags.Instance |
															   BindingFlags.DeclaredOnly))
				{
					foreach (CustomAttributeData attribute in method.GetCustomAttributesData())
					{
						if (attribute.AttributeType.FullName == "CheatEngine.SDK.Annotations.Lua.LuaGlobalAttribute" &&
							attribute.ConstructorArguments[0].Value is string name &&
							!FrozenLuaGlobals.Contains(name, StringComparer.Ordinal))
						{
							violations.Add($"{type.FullName}.{method.Name} declares [LuaGlobal(\"{name}\")]; the Client " +
										   "binds no Cheat Engine global itself. Call the typed CheatEngine.SDK service.");
						}
					}
				}
			}
		}

		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
	}

	[Fact]
	public void FrozenLuaGlobalsStaysEmpty()
	{
		// The ratchet only shrinks, and it reached zero: a Cheat Engine global that CheatEngine.SDK does not wrap is an
		// SDK issue, never a Client binding.
		Assert.Empty(FrozenLuaGlobals);
	}

	[Fact]
	public void SdkLuaSurfaceUseIsSanctionedTypedApiOrRegisteredDebt()
	{
		List<LuaSurfaceUse> uses = [.. ClientAssemblyCatalog.Names.SelectMany(LuaUsageScanner.Scan)];
		Dictionary<string, SanctionedSdkLuaMember> sanctioned =
			SanctionedSdkLuaSurface.ToDictionary(static member => member.Member, StringComparer.Ordinal);

		string[] debt = [.. uses.Where(use => !sanctioned.ContainsKey(use.Symbol)).Select(static use => use.ToString())];
		string[] frozen = [.. FrozenLuaUsage.Select(static entry => entry.Usage)];
		string[] added = [.. debt.Except(frozen, StringComparer.Ordinal)];
		string[] removed = [.. frozen.Except(debt, StringComparer.Ordinal)];
		string[] unusedSanctioned =
			[.. sanctioned.Keys.Where(member => !uses.Exists(use => use.Symbol == member)).Order(StringComparer.Ordinal)];
		string[] misplaced =
		[
			.. uses.Where(use => sanctioned.TryGetValue(use.Symbol, out SanctionedSdkLuaMember? member) &&
								 member.OnlyIn is { } owner && owner != use.OuterType)
				.Select(static use => use.ToString())
		];

		Assert.True(added.Length == 0,
			"New direct Lua-stack or SDK-owner usage in Client code:" + Environment.NewLine +
			string.Join(Environment.NewLine, added) + Environment.NewLine + Adr01Guidance);
		Assert.True(removed.Length == 0,
			"These frozen ADR-01 usages no longer exist; shrink FrozenLuaUsage (ratchet):" + Environment.NewLine +
			string.Join(Environment.NewLine, removed));
		Assert.True(unusedSanctioned.Length == 0,
			"These sanctioned SDK Lua members are no longer used; shrink SanctionedSdkLuaSurface:" + Environment.NewLine +
			string.Join(Environment.NewLine, unusedSanctioned));
		Assert.True(misplaced.Length == 0,
			"These sanctioned SDK Lua members are used outside their single owner:" + Environment.NewLine +
			string.Join(Environment.NewLine, misplaced));
	}

	[Fact]
	public void LuaInventoriesAreOrderedAndEveryEntryStatesHowItEnds()
	{
		string[] usages = [.. FrozenLuaUsage.Select(static entry => entry.Usage)];
		string[] members = [.. SanctionedSdkLuaSurface.Select(static member => member.Member)];
		List<string> violations = [];
		foreach (FrozenLuaUse entry in FrozenLuaUsage)
		{
			if (string.IsNullOrWhiteSpace(entry.Reason))
			{
				violations.Add($"{entry.Usage} has no reason.");
			}

			switch (entry.Kind)
			{
				case LuaDebtKind.Permanent
					when !entry.Usage.StartsWith(UnsafeLuaClientType + " -> ", StringComparison.Ordinal):
					violations.Add($"{entry.Usage} is Permanent; only UnsafeLuaClient may be.");
					break;
				case LuaDebtKind.Transitional transitional:
					violations.AddRange(FindUnresolvedReplacement(entry.Usage, transitional.Replacement));
					break;
				case LuaDebtKind.AwaitingSdkPrimitive awaiting when string.IsNullOrWhiteSpace(awaiting.Primitive):
					violations.Add($"{entry.Usage} does not name the missing SDK primitive.");
					break;
			}

			if (members.Contains(entry.Usage.Split(" -> ", 2)[1], StringComparer.Ordinal))
			{
				violations.Add($"{entry.Usage} is sanctioned SDK API; it cannot also be debt.");
			}
		}

		violations.AddRange(SanctionedSdkLuaSurface.Where(static member => string.IsNullOrWhiteSpace(member.Reason))
			.Select(static member => $"{member.Member} has no reason."));
		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
		Assert.Equal(usages.Order(StringComparer.Ordinal), usages);
		Assert.Equal(usages.Length, usages.Distinct(StringComparer.Ordinal).Count());
		Assert.Equal(members.Order(StringComparer.Ordinal), members);
		Assert.Equal(members.Length, members.Distinct(StringComparer.Ordinal).Count());
		Assert.Equal(UnsafeLuaReason,
			Assert.Single(FrozenLuaUsage.Where(static entry => entry.Kind is LuaDebtKind.Permanent)
				.Select(static entry => entry.Reason).Distinct(StringComparer.Ordinal)));
	}

	[Fact]
	public void ClientCodeNeverConstructsSdkOwnersOrAdoptsHandles()
	{
		List<string> violations = [];
		foreach (string assembly in ClientAssemblyCatalog.Names)
		{
			ClientAssemblyCatalog.ReadMetadata(assembly, (reader, _) =>
			{
				foreach (MemberReferenceHandle handle in reader.MemberReferences)
				{
					string member = MetadataSurface.DescribeMember(reader, handle,
						out MetadataSurface.TypeIdentity declaringType);
					if (declaringType.IsSdk && IsOwnershipAdoption(member, declaringType.FullName))
					{
						violations.Add($"{assembly} references {member}: only qualified SDK factories may create " +
									   $"owners or adopt handles (ADR-06). {Adr01Guidance}");
					}
				}
			});
		}

		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
	}

	[Fact]
	public void EveryPublicSdkAbandonMemberIsAForbiddenOwnershipBypass()
	{
		string[] abandonMembers =
		[
			.. ConsumedSdkAssemblies.All.SelectMany(static assembly => assembly.GetExportedTypes())
				.SelectMany(static type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance |
														   BindingFlags.Static | BindingFlags.DeclaredOnly)
					.Where(static method => method.Name.StartsWith("Abandon", StringComparison.Ordinal))
					.Select(method => $"{type.FullName}::{method.Name}()"))
				.Distinct(StringComparer.Ordinal)
				.Order(StringComparer.Ordinal)
		];

		// ADR-06: abandoning an owner leaks its Cheat Engine object on purpose. The Client never does it, including the
		// session-level MemoryScanSession.Abandon of CheatEngine.SDK 2.0.0.
		Assert.Contains("CheatEngine.SDK.Engine.Scanning.Values.MemoryScanSession::Abandon()", abandonMembers);
		Assert.Contains("CheatEngine.SDK.Engine.Objects.Owned`1::Abandon()", abandonMembers);
		Assert.All(abandonMembers, static member =>
			Assert.True(IsOwnershipAdoption(member, member.Split("::", 2)[0]), $"{member} is not forbidden."));
	}

	[Fact]
	public void ClientNeverReinvokesPluginEnable()
	{
		List<string> violations = [];
		foreach (string assembly in ClientAssemblyCatalog.Names)
		{
			ClientAssemblyCatalog.ReadMetadata(assembly, (reader, _) =>
			{
				foreach (MemberReferenceHandle handle in reader.MemberReferences)
				{
					string member = MetadataSurface.DescribeMember(reader, handle,
						out MetadataSurface.TypeIdentity declaringType);
					bool pluginHostMember = declaringType.FullName == "CheatEngine.SDK.Hosting.Bootstrap.PluginHost" &&
											!member.Contains("::get_Context()", StringComparison.Ordinal);
					bool pluginLifecycleCall = declaringType.FullName == "CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin" &&
											   (member.Contains("::OnEnable(", StringComparison.Ordinal) ||
												member.Contains("::OnDisable(", StringComparison.Ordinal));
					if (pluginHostMember || pluginLifecycleCall)
					{
						violations.Add($"{assembly} references {member}; the Client never re-invokes or drives the SDK " +
									   "plugin lifecycle.");
					}
				}
			});
		}

		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
	}

	[Fact]
	public void ClientAssembliesDeclareNoFinalizers()
	{
		List<string> violations = [];
		foreach (string assembly in ClientAssemblyCatalog.Names)
		{
			ClientAssemblyCatalog.ReadMetadata(assembly, (reader, _) =>
			{
				foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
				{
					MethodDefinition method = reader.GetMethodDefinition(handle);
					if (reader.GetString(method.Name) == "Finalize" && method.GetParameters().Count == 0 &&
						(method.Attributes & MethodAttributes.Virtual) != 0)
					{
						violations.Add($"{MetadataSurface.ResolveTypeDefinition(reader, method.GetDeclaringType()).FullName} " +
									   "declares a finalizer; a finalizer must never repair a forgotten cleanup by touching " +
									   "Lua or the GUI.");
					}
				}
			});
		}

		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
	}

	[Fact]
	public void DomainTypesDoNotHoldAServiceProvider()
	{
		const BindingFlags AllMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
										BindingFlags.Instance | BindingFlags.DeclaredOnly;
		List<string> violations = [];
		foreach (Type type in ClientAssemblyCatalog.Load("CheatEngine.Client.Core").GetTypes())
		{
			foreach (FieldInfo field in type.GetFields(AllMembers).Where(static field =>
						 typeof(IServiceProvider).IsAssignableFrom(field.FieldType)))
			{
				violations.Add($"{type.FullName} field {field.Name}");
			}

			foreach (PropertyInfo property in type.GetProperties(AllMembers).Where(static property =>
						 typeof(IServiceProvider).IsAssignableFrom(property.PropertyType)))
			{
				violations.Add($"{type.FullName} property {property.Name}");
			}

			foreach (ConstructorInfo constructor in type.GetConstructors(AllMembers))
			{
				violations.AddRange(constructor.GetParameters()
					.Where(static parameter => typeof(IServiceProvider).IsAssignableFrom(parameter.ParameterType))
					.Select(parameter => $"{type.FullName} constructor parameter {parameter.Name}"));
			}
		}

		Assert.True(violations.Count == 0,
			"Core domain types must receive their dependencies explicitly; no service locator inside a domain:" +
			Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[Fact]
	public void TableClientExposesNoStreamOrByteLoadPath()
	{
		Type[] forbidden =
		[
			typeof(Stream), typeof(byte[]), typeof(ReadOnlySpan<byte>), typeof(Span<byte>), typeof(ReadOnlyMemory<byte>),
			typeof(Memory<byte>), typeof(IEnumerable<byte>)
		];
		MethodInfo[] methods = typeof(ITableClient).GetMethods();
		List<string> violations = [];
		foreach (MethodInfo method in methods)
		{
			foreach (ParameterInfo parameter in method.GetParameters())
			{
				Type type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
				if (forbidden.Any(candidate => candidate.IsAssignableFrom(type)))
				{
					violations.Add($"{method.Name} parameter {parameter.Name} accepts a raw table payload ({type}).");
				}
			}
		}

		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
		Assert.All(methods.Where(static method => method.Name.Contains("LoadTrustedTable", StringComparison.Ordinal)),
			static method => Assert.Equal(typeof(TableLoadRequest), method.GetParameters()[0].ParameterType));
		Assert.All(methods.Where(static method => method.Name.Contains("SaveTable", StringComparison.Ordinal)),
			static method => Assert.Equal(typeof(TableSaveRequest), method.GetParameters()[0].ParameterType));
	}

	private static bool IsOwnershipAdoption(string member, string declaringType)
	{
		return (declaringType == "CheatEngine.SDK.Engine.Objects.CEObject" &&
				member.Contains("::.ctor(", StringComparison.Ordinal)) ||
			   (member.Contains("::.ctor(", StringComparison.Ordinal) &&
				member.Contains("CheatEngine.SDK.Engine.Objects.CEObject", StringComparison.Ordinal)) ||
			   member.Contains("::FromHandle(", StringComparison.Ordinal) ||
			   declaringType == "CheatEngine.SDK.Engine.Objects.ICEObject`1" ||
			   (declaringType == "CheatEngine.SDK.Engine.Objects.Owned`1" &&
				member.Contains("::Transfer(", StringComparison.Ordinal)) ||
			   member.Contains("::Abandon", StringComparison.Ordinal) ||
			   member.Contains("::PushUncheckedFunction(", StringComparison.Ordinal);
	}

	/// <summary>Resolves a replacement in the consumed SDK and returns the violations of one ratchet entry.</summary>
	private static IEnumerable<string> FindUnresolvedReplacement(string entry, SdkReplacement replacement)
	{
		if (!LotPattern().IsMatch(replacement.Lot))
		{
			yield return $"{entry}: '{replacement.Lot}' is not a plan lot (L<number>).";
		}

		if (ConsumedSdkAssemblies.FindPublicType(replacement.Type) is not { } type)
		{
			yield return $"{entry}: {replacement.Type} is not a public type of the consumed CheatEngine.SDK.";
			yield break;
		}

		foreach (string member in replacement.Members)
		{
			if (type.GetMember(member, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance).Length == 0)
			{
				yield return $"{entry}: {replacement} names no public member {type.Name}.{member} in the consumed SDK.";
			}
		}
	}

	private static List<string> FindNativeImports(string assemblyPath)
	{
		List<string> violations = [];
		using FileStream stream = File.OpenRead(assemblyPath);
		using System.Reflection.PortableExecutable.PEReader peReader = new(stream);
		MetadataReader reader = peReader.GetMetadataReader();
		string assembly = reader.GetString(reader.GetAssemblyDefinition().Name);

		for (int row = 1; row <= reader.GetTableRowCount(TableIndex.ModuleRef); row++)
		{
			ModuleReference module = reader.GetModuleReference(MetadataTokens.ModuleReferenceHandle(row));
			violations.Add($"{assembly} imports native module '{reader.GetString(module.Name)}'.");
		}

		foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
		{
			MethodDefinition method = reader.GetMethodDefinition(handle);
			string owner = $"{MetadataSurface.ResolveTypeDefinition(reader, method.GetDeclaringType()).FullName}." +
						   reader.GetString(method.Name);
			if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
			{
				violations.Add($"{assembly}: {owner} is a P/Invoke declaration.");
			}

			foreach (CustomAttributeHandle attributeHandle in method.GetCustomAttributes())
			{
				string attributeType = MetadataSurface.GetAttributeTypeName(reader, reader.GetCustomAttribute(attributeHandle));
				if (ForbiddenInteropAttributes.Contains(attributeType, StringComparer.Ordinal))
				{
					violations.Add($"{assembly}: {owner} carries {attributeType.Split('.')[^1]}.");
				}
			}

			if (DescribeSignature(reader, method).Contains("fnptr(", StringComparison.Ordinal))
			{
				violations.Add($"{assembly}: {owner} exposes a function pointer in its signature.");
			}
		}

		foreach (MemberReferenceHandle handle in reader.MemberReferences)
		{
			string member = MetadataSurface.DescribeMember(reader, handle, out MetadataSurface.TypeIdentity type);
			if (type.FullName == "System.Runtime.InteropServices.NativeLibrary")
			{
				violations.Add($"{assembly} references {member}.");
			}
		}

		return violations;
	}

	private static string DescribeSignature(MetadataReader reader, MethodDefinition method)
	{
		MethodSignature<MetadataSurface.SignatureName> signature =
			method.DecodeSignature(new MetadataSurface.SignatureNameProvider(), null);
		return string.Join(",", signature.ParameterTypes.Select(static parameter => parameter.Display)) + "->" +
			   signature.ReturnType.Display;
	}

	[GeneratedRegex("^L[1-9][0-9]*$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex LotPattern();

	/// <summary>One registered ADR-01 debt entry: a direct SDK Lua-stack or ownership use, why, and how it ends.</summary>
	private sealed record FrozenLuaUse(string Usage, string Reason, LuaDebtKind Kind)
	{
		internal FrozenLuaUse(string outerType, string symbol, string reason, LuaDebtKind kind)
			: this($"{outerType} -> {symbol}", reason, kind)
		{
		}
	}

	/// <summary>A typed SDK Lua member the Client uses, why, and the single Client type allowed to use it, if any.</summary>
	private sealed record SanctionedSdkLuaMember(string Member, string Reason, string? OnlyIn = null);

	/// <summary>The CheatEngine.SDK members that replace a registered exception, and the plan lot that adopts them.</summary>
	private sealed record SdkReplacement(string Type, string Lot, string[] Members)
	{
		/// <summary>Formats the replacement as <c>Type.Member (Lot)</c>, for example
		///     <c>CheatTableFiles.TryLoad (L13)</c>.</summary>
		public override string ToString()
		{
			return $"{Type[(Type.LastIndexOf('.') + 1)..]}.{string.Join(" and ", Members)} ({Lot})";
		}
	}

	/// <summary>How a registered ADR-01 debt entry ends.</summary>
	private abstract record LuaDebtKind
	{
		/// <summary>Kept by design: the SDK offers no replacement and the Client deliberately exposes the capability.</summary>
		internal sealed record Permanent : LuaDebtKind;

		/// <summary>Removed by a plan lot that adopts the named SDK replacement.</summary>
		internal sealed record Transitional(SdkReplacement Replacement) : LuaDebtKind;

		/// <summary>Kept until the consumed SDK offers the named primitive.</summary>
		internal sealed record AwaitingSdkPrimitive(string Primitive) : LuaDebtKind;
	}

	/// <summary>Deliberate native-interop forms proving that the scan is not vacuous. Never called.</summary>
	private static unsafe class NativeImportFixture
	{
		// The ratchet must see a real DllImport; LibraryImport would hide it behind generated code.
#pragma warning disable SYSLIB1054
		[DllImport("kernel32.dll")]
		internal static extern uint GetCurrentThreadId();
#pragma warning restore SYSLIB1054

		[UnmanagedCallersOnly]
		internal static int Callback(int value)
		{
			return value;
		}

		internal static delegate* unmanaged<int, int> GetCallback()
		{
			return &Callback;
		}

		internal static bool Load()
		{
			return NativeLibrary.TryLoad("kernel32.dll", out _);
		}
	}
}
