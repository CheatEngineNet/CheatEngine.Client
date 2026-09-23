using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;

using CheatEngine.Client.Tables;

using ReflectionAssembly = System.Reflection.Assembly;

namespace CheatEngine.Client.Tests.Architecture;

/// <summary>
///     C0 architecture ratchet for ADR-01 ("the SDK is the only native authority") and ADR-06 ("ownership is explicit"):
///     the Client never declares native imports, never references the SDK ABI or Lua interop assemblies, and keeps its
///     remaining direct Lua and owner usages frozen to a reviewed list that may only shrink.
/// </summary>
/// <remarks>
///     Everything is read from the compiled Client assemblies with System.Reflection.Metadata; no Client code runs. The
///     frozen lists below are the registered ADR-01 debt of the Client on CheatEngine.SDK 1.0.0; each entry is removed by
///     the SDK 2.0 migration (docs/migration/sdk-2.0.md). Shrinking a list is always allowed; growing it requires a
///     registered exception with a removal entry in that migration guide.
/// </remarks>
public sealed class ArchitectureRatchetTests
{
	private const string Adr01Guidance =
		"ADR-01 exception: register it in the ratchet (tests/CheatEngine.Client.Tests/Architecture) and in " +
		"docs/migration/sdk-2.0.md with its SDK 2.0 replacement, or route the work through the SDK.";

	private const string SdkRemoval = "SDK 2.0 (docs/migration/sdk-2.0.md)";

	private const string ClientLuaGlobalsType = "CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals";

	/// <summary>The only Lua globals the Client may bind itself, each a registered ADR-01 exception.</summary>
	private static readonly FrozenLuaGlobal[] FrozenLuaGlobals =
	[
		new("getOpenedProcessID", "Process selection reads the opened PID; SDK 1.0.0 has no process observation.",
			false),
		new("openProcess", "Process attach; SDK 1.0.0 has no attach service.", false),
		new("getCEVersion", "Runtime version fact for the capability snapshot.", false),
		new("getSystemArchitecture", "Host architecture fact for the capability snapshot.", false),
		new("getABI", "Target ABI fact for the capability snapshot.", false),
		new("targetIs64Bit", "Target bitness for codec pointer width and the runtime snapshot.", false),
		new("loadTable", "Trusted table import behind the Client path policy.", false),
		new("saveTable", "Trusted table export behind the Client path policy.", false),
		new("getNameFromAddress", "Symbol name resolution; SDK 1.0.0 has no name lookup service.", false),
		new("registerSymbol", "Activation-owned symbol registration lease.", false),
		new("unregisterSymbol", "Release of an activation-owned symbol registration.", false),
		new("getPointerSize", "Reserved for C-CORE-B: configured pointer size (spike C3 D3).", true),
		new("targetIsX86", "Reserved for C-CORE-B: x86-family ISA fact (spike C3 D2).", true),
		new("targetIsArm", "Reserved for C-CORE-B: ARM-family ISA fact (spike C3 D2).", true)
	];

	/// <summary>
	///     Every direct use of Lua-stack or SDK-owner primitives in Client code, by outermost declaring type. Regenerated
	///     from the compiled assemblies; it may only shrink.
	/// </summary>
	private static readonly string[] FrozenLuaUsage =
	[
		"CheatEngine.Client.Core.Domains.SdkAobScanPort -> CheatEngine.SDK.Engine.Objects.Owned`1::Dispose()->void",
		"CheatEngine.Client.Core.Domains.SdkAobScanPort -> CheatEngine.SDK.Engine.Objects.Owned`1::get_Value()->!0",
		"CheatEngine.Client.Core.Domains.SdkAobScanPort -> CheatEngine.SDK.Engine.Objects.StringList::TryGetCount(int32&)->boolean",
		"CheatEngine.Client.Core.Domains.SdkAobScanPort -> CheatEngine.SDK.Engine.Objects.StringList::TryGetItem(int32,string&)->boolean",
		"CheatEngine.Client.Core.Domains.SdkTableRecordMutationPort -> CheatEngine.SDK.Engine.AddressList.MemoryRecord::TryRead(CheatEngine.SDK.Lua.State.LuaState,int32,CheatEngine.SDK.Engine.AddressList.MemoryRecord&)->boolean",
		"CheatEngine.Client.Core.Domains.SdkTableRecordMutationPort -> CheatEngine.SDK.Engine.Objects.CEObject::TryCallMethod(System.ReadOnlySpan`1<byte>)->boolean",
		"CheatEngine.Client.Core.Domains.SdkTableRecordMutationPort -> CheatEngine.SDK.Engine.Objects.CEObject::TryGetProperty(CheatEngine.SDK.Lua.State.LuaState,System.ReadOnlySpan`1<byte>)->CheatEngine.SDK.Lua.Calls.LuaStatus",
		"CheatEngine.Client.Core.Domains.SdkTableRecordMutationPort -> CheatEngine.SDK.Engine.Objects.CEObject::TrySetProperty``2(System.ReadOnlySpan`1<byte>,!!1)->boolean",
		"CheatEngine.Client.Core.Domains.SdkTableRecordMutationPort -> CheatEngine.SDK.Lua.Runtime.LuaRuntime::AcquireOperation()->CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation",
		"CheatEngine.Client.Core.Domains.SdkTableRecordMutationPort -> CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation::Dispose()->void",
		"CheatEngine.Client.Core.Domains.SdkTableRecordMutationPort -> CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation::get_State()->CheatEngine.SDK.Lua.State.LuaState",
		"CheatEngine.Client.Core.Domains.SdkTableRecordMutationPort -> CheatEngine.SDK.Lua.State.LuaFrame::.ctor(CheatEngine.SDK.Lua.State.LuaState)->void",
		"CheatEngine.Client.Core.Domains.SdkTableRecordMutationPort -> CheatEngine.SDK.Lua.State.LuaFrame::Dispose()->void",
		"CheatEngine.Client.Core.Domains.SdkTableRecordMutationPort -> CheatEngine.SDK.Lua.State.LuaState::IsNil(int32)->boolean",
		"CheatEngine.Client.Core.Domains.TableClient -> CheatEngine.SDK.Engine.Objects.CEObject::TryGetProperty``2(System.ReadOnlySpan`1<byte>,!!1&)->boolean",
		"CheatEngine.Client.Core.Domains.TableClient -> CheatEngine.SDK.Engine.Objects.CEObject::TrySetProperty``2(System.ReadOnlySpan`1<byte>,!!1)->boolean",
		"CheatEngine.Client.Core.Domains.UnsafeLuaClient -> CheatEngine.SDK.Lua.Calls.LuaError::FromStack(CheatEngine.SDK.Lua.State.LuaState,CheatEngine.SDK.Lua.Calls.LuaStatus)->CheatEngine.SDK.Lua.Calls.LuaError",
		"CheatEngine.Client.Core.Domains.UnsafeLuaClient -> CheatEngine.SDK.Lua.Calls.LuaError::get_Message()->string",
		"CheatEngine.Client.Core.Domains.UnsafeLuaClient -> CheatEngine.SDK.Lua.Runtime.LuaRuntime::AcquireOperation()->CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation",
		"CheatEngine.Client.Core.Domains.UnsafeLuaClient -> CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation::Dispose()->void",
		"CheatEngine.Client.Core.Domains.UnsafeLuaClient -> CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation::get_State()->CheatEngine.SDK.Lua.State.LuaState",
		"CheatEngine.Client.Core.Domains.UnsafeLuaClient -> CheatEngine.SDK.Lua.State.LuaFrame::.ctor(CheatEngine.SDK.Lua.State.LuaState)->void",
		"CheatEngine.Client.Core.Domains.UnsafeLuaClient -> CheatEngine.SDK.Lua.State.LuaFrame::Dispose()->void",
		"CheatEngine.Client.Core.Domains.UnsafeLuaClient -> CheatEngine.SDK.Lua.State.LuaState::TryExecute(System.ReadOnlySpan`1<byte>,int32,System.ReadOnlySpan`1<byte>)->CheatEngine.SDK.Lua.Calls.LuaStatus",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.CompilerServices.LuaCallSupport::Fail``1(CheatEngine.SDK.Lua.State.LuaState,int32,!!0&)->boolean",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.CompilerServices.LuaCallSupport::Throw(CheatEngine.SDK.Lua.State.LuaState,int32,CheatEngine.SDK.Lua.Calls.LuaStatus)->void",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.CompilerServices.LuaCallSupport::ThrowUnexpectedResult(CheatEngine.SDK.Lua.State.LuaState,int32,int32,string,string)->void",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.CompilerServices.LuaCallSupport::ThrowUnresolvedGlobal(CheatEngine.SDK.Lua.State.LuaState,int32,string)->void",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.CompilerServices.LuaGlobalFunctions::TryPush(CheatEngine.SDK.Lua.State.LuaState,CheatEngine.SDK.Lua.References.LuaRef,System.ReadOnlySpan`1<byte>)->boolean",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.Marshalling.AddressMarshaller::Push(CheatEngine.SDK.Lua.State.LuaState,uintptr)->void",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.Marshalling.BooleanMarshaller::Push(CheatEngine.SDK.Lua.State.LuaState,boolean)->void",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.Marshalling.BooleanMarshaller::TryRead(CheatEngine.SDK.Lua.State.LuaState,int32,boolean&)->boolean",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.Marshalling.DoubleMarshaller::TryRead(CheatEngine.SDK.Lua.State.LuaState,int32,double&)->boolean",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.Marshalling.Int32Marshaller::TryRead(CheatEngine.SDK.Lua.State.LuaState,int32,int32&)->boolean",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.Marshalling.Int64Marshaller::Push(CheatEngine.SDK.Lua.State.LuaState,int64)->void",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.Marshalling.Int64Marshaller::TryRead(CheatEngine.SDK.Lua.State.LuaState,int32,int64&)->boolean",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.Marshalling.StringMarshaller::Push(CheatEngine.SDK.Lua.State.LuaState,string)->void",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.Marshalling.StringMarshaller::TryRead(CheatEngine.SDK.Lua.State.LuaState,int32,string&)->boolean",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.References.LuaRef::.ctor()->void",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.Runtime.LuaRuntime::AcquireOperation()->CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation::Dispose()->void",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation::get_State()->CheatEngine.SDK.Lua.State.LuaState",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.State.LuaState::SetTop(int32)->void",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.State.LuaState::TryCall(int32,int32)->CheatEngine.SDK.Lua.Calls.LuaStatus",
		"CheatEngine.Client.Core.Infrastructure.ClientLuaGlobals -> CheatEngine.SDK.Lua.State.LuaState::get_Top()->int32"
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

	[Fact]
	public void LuaGlobalBindingsAreFrozenToTheRegisteredAdr01Exceptions()
	{
		Dictionary<string, FrozenLuaGlobal> frozen = FrozenLuaGlobals.ToDictionary(static entry => entry.Name,
			StringComparer.Ordinal);
		List<(string Name, string DeclaringType)> actual = [];
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
						if (attribute.AttributeType.FullName == "CheatEngine.SDK.Annotations.Lua.LuaGlobalAttribute")
						{
							actual.Add(((string) attribute.ConstructorArguments[0].Value!, type.FullName!));
						}
					}
				}
			}
		}

		List<string> violations = [];
		foreach ((string name, string declaringType) in actual)
		{
			if (!frozen.ContainsKey(name))
			{
				violations.Add($"{declaringType} declares [LuaGlobal(\"{name}\")], which is not a registered exception. " +
							   Adr01Guidance);
			}

			if (declaringType != ClientLuaGlobalsType)
			{
				violations.Add($"{declaringType} declares [LuaGlobal(\"{name}\")]; only {ClientLuaGlobalsType} may. " +
							   Adr01Guidance);
			}
		}

		HashSet<string> actualNames = new(actual.Select(static binding => binding.Name), StringComparer.Ordinal);
		foreach (FrozenLuaGlobal entry in FrozenLuaGlobals.Where(static entry => !entry.Reserved))
		{
			if (!actualNames.Contains(entry.Name))
			{
				violations.Add($"[LuaGlobal(\"{entry.Name}\")] was removed: shrink the frozen list (ratchet) and the SDK " +
							   "2.0 migration entry.");
			}
		}

		Assert.Equal(actual.Count, actualNames.Count);
		Assert.All(FrozenLuaGlobals, static entry =>
		{
			Assert.False(string.IsNullOrWhiteSpace(entry.Reason));
			Assert.Equal(SdkRemoval, entry.Removal);
		});
		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
	}

	[Fact]
	public void DirectLuaStateUsageIsLimitedToTheFrozenAllowlist()
	{
		SortedSet<string> actual = new(StringComparer.Ordinal);
		foreach (string assembly in ClientAssemblyCatalog.Names)
		{
			actual.UnionWith(LuaUsageScanner.Scan(assembly));
		}

		string[] added = actual.Except(FrozenLuaUsage, StringComparer.Ordinal).ToArray();
		string[] removed = FrozenLuaUsage.Except(actual, StringComparer.Ordinal).ToArray();

		Assert.True(added.Length == 0,
			"New direct Lua-stack or SDK-owner usage in Client code:" + Environment.NewLine +
			string.Join(Environment.NewLine, added) + Environment.NewLine + Adr01Guidance);
		Assert.True(removed.Length == 0,
			"These frozen ADR-01 usages no longer exist; shrink FrozenLuaUsage (ratchet):" + Environment.NewLine +
			string.Join(Environment.NewLine, removed));
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
				(member.Contains("::Transfer(", StringComparison.Ordinal) ||
				 member.Contains("::Abandon(", StringComparison.Ordinal))) ||
			   member.Contains("::PushUncheckedFunction(", StringComparison.Ordinal);
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
				string attributeType = GetAttributeType(reader, reader.GetCustomAttribute(attributeHandle));
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

	private static string GetAttributeType(MetadataReader reader, CustomAttribute attribute)
	{
		return attribute.Constructor.Kind switch
		{
			HandleKind.MemberReference => MetadataSurface.ResolveType(reader,
				reader.GetMemberReference((MemberReferenceHandle) attribute.Constructor).Parent).FullName,
			HandleKind.MethodDefinition => MetadataSurface.ResolveTypeDefinition(reader,
				reader.GetMethodDefinition((MethodDefinitionHandle) attribute.Constructor).GetDeclaringType()).FullName,
			_ => string.Empty
		};
	}

	private static string DescribeSignature(MetadataReader reader, MethodDefinition method)
	{
		MethodSignature<MetadataSurface.SignatureName> signature =
			method.DecodeSignature(new MetadataSurface.SignatureNameProvider(), null);
		return string.Join(",", signature.ParameterTypes.Select(static parameter => parameter.Display)) + "->" +
			   signature.ReturnType.Display;
	}

	private sealed record FrozenLuaGlobal(string Name, string Reason, bool Reserved, string Removal = SdkRemoval);

	/// <summary>Deliberate native-interop forms proving that the scan is not vacuous. Never called.</summary>
	private static unsafe class NativeImportFixture
	{
		[DllImport("kernel32.dll")]
		internal static extern uint GetCurrentThreadId();

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
