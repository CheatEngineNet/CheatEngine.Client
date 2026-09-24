using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using CheatEngine.Client.SourceGenerators.Lua.Tests.Infrastructure;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.Architecture;

/// <summary>
///     C0 ratchet of the CheatEngine.SDK surface of generated Client Lua module code: the exact allowlist of the SDK
///     members the generated registration adapter uses. It is the Lua registration API that CheatEngine.SDK 2.0.0
///     imposes (an admitted operation whose state the SDK-generated <c>TryRegisterLuaFunctions</c> and the lease release
///     take, and the getters of the results they return), not raw stack access, and it may only shrink.
/// </summary>
/// <remarks>
///     The members are read from the emitted image with System.Reflection.Metadata (member references whose declaring
///     type is in a <c>CheatEngine.SDK.Lua</c> namespace); no generated code runs. A second check proves, on the
///     semantic model, that only the generated adapter calls CheatEngine.SDK: the module and the registrar name SDK
///     enum values and the bindings type's registration method only.
/// </remarks>
public sealed class GeneratedLuaSurfaceRatchetTests
{
	private const string Guidance =
		"Generated Client Lua module code may use only the SDK-imposed registration API listed in " +
		"SdkImposedRegistrationSurface; a new member is a reviewed addition with its reason, never raw Lua stack access.";

	private const string Admission =
		"SDK-imposed admission: the SDK registration and the lease release take the state of an admitted operation";

	private const string Registration = "SDK-imposed registration API: a getter of the result the SDK registration returns";

	private const string Release = "SDK-imposed registration API: the lease release and the getters of its outcome";

	/// <summary>The exact SDK surface of generated module code, one reason per member; it may only shrink.</summary>
	private static readonly SdkImposedMember[] SdkImposedRegistrationSurface =
	[
		new("CheatEngine.SDK.Lua.Registration.LuaRegistrationFailure::get_LuaStatus()->CheatEngine.SDK.Lua.Calls.LuaStatus",
			Registration + ": the failed protected operation's status, copied as text into the failure message"),
		new("CheatEngine.SDK.Lua.Registration.LuaRegistrationFailure::get_Name()->string", Registration),
		new("CheatEngine.SDK.Lua.Registration.LuaRegistrationLease::ReleaseWithOutcome()->CheatEngine.SDK.Lua.Registration.LuaRegistrationReleaseOutcome",
			Release + ": consumes a lease whose Lua universe is gone (Detached, ExternalStateReset) without any Lua call"),
		new("CheatEngine.SDK.Lua.Registration.LuaRegistrationLease::ReleaseWithOutcome(CheatEngine.SDK.Lua.State.LuaState)->CheatEngine.SDK.Lua.Registration.LuaRegistrationReleaseOutcome",
			Release),
		new("CheatEngine.SDK.Lua.Registration.LuaRegistrationReleaseFailure::get_Name()->string",
			Release + ": the failed global names of LuaModuleReleaseOutcome.FailedExports"),
		new("CheatEngine.SDK.Lua.Registration.LuaRegistrationReleaseOutcome::get_Failures()->System.Collections.Generic.IReadOnlyList`1<CheatEngine.SDK.Lua.Registration.LuaRegistrationReleaseFailure>",
			Release),
		new("CheatEngine.SDK.Lua.Registration.LuaRegistrationReleaseOutcome::get_Kind()->CheatEngine.SDK.Lua.Registration.LuaRegistrationReleaseKind",
			Release),
		new("CheatEngine.SDK.Lua.Registration.LuaRegistrationReleaseOutcome::get_RemainingCount()->int32", Release),
		new("CheatEngine.SDK.Lua.Registration.LuaRegistrationReleaseOutcome::get_RemovedCount()->int32", Release),
		new("CheatEngine.SDK.Lua.Registration.LuaRegistrationReleaseOutcome::get_ReplacementCount()->int32", Release),
		new("CheatEngine.SDK.Lua.Registration.LuaRegistrationReleaseOutcome::get_RestoredCount()->int32", Release),
		new("CheatEngine.SDK.Lua.Registration.LuaRegistrationResult::get_Failure()->System.Nullable`1<CheatEngine.SDK.Lua.Registration.LuaRegistrationFailure>",
			Registration),
		new("CheatEngine.SDK.Lua.Registration.LuaRegistrationResult::get_Kind()->CheatEngine.SDK.Lua.Registration.LuaRegistrationResultKind",
			Registration),
		new("CheatEngine.SDK.Lua.Registration.LuaRegistrationResult::get_Lease()->CheatEngine.SDK.Lua.Registration.LuaRegistrationLease",
			Registration),
		new("CheatEngine.SDK.Lua.Registration.LuaRegistrationResult::get_Rollback()->CheatEngine.SDK.Lua.Registration.LuaRegistrationReleaseOutcome",
			Registration + ": the compensation outcome of a failed publication"),
		new("CheatEngine.SDK.Lua.Runtime.LuaRuntime::TryAcquireOperationWithOutcome(CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation&)->CheatEngine.SDK.Lua.Runtime.LuaAdmissionStatus",
			Admission + "; the factual LuaAdmissionStatus is classified by the registrar, never an exception message"),
		new("CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation::Dispose()->void",
			Admission + "; ends the admission before control returns to Cheat Engine"),
		new("CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation::get_State()->CheatEngine.SDK.Lua.State.LuaState",
			Admission + "; passed to the SDK only, never read or written by generated code")
	];

	private const string ModuleSource =
		"""
		using CheatEngine.Client.Lua;
		using CheatEngine.SDK.Annotations.Lua;
		using CheatEngine.SDK.Lua.Registration;
		using CheatEngine.SDK.Lua.State;
		namespace TestPlugin;
		internal static partial class PluginLuaBindings
		{
			[LuaFunction("status")]
			public static string Status() => "ok";

			[LuaFunction("ping")]
			public static int Ping() => 1;

			// Stands for the SDK LuaBindings output; it adds no SDK member reference of its own.
			public static LuaRegistrationResult TryRegisterLuaFunctions(LuaState state,
				LuaRegistrationCollisionPolicy collisionPolicy = LuaRegistrationCollisionPolicy.RejectExisting) => default;
		}

		[CheatEngineLuaModule(typeof(PluginLuaBindings), "plugin")]
		internal sealed partial class PluginLuaModule : ILuaModule;
		""";

	private const string OperationSource =
		"""
		using CheatEngine.Client.Lua;
		using CheatEngine.SDK.Annotations.Lua;
		namespace TestPlugin;
		internal sealed class SdkSnapshot { }
		internal readonly record struct Snapshot(int Value);
		internal readonly struct SnapshotMapper : ILuaResultMapper<SdkSnapshot, Snapshot>
		{
			public static Snapshot Map(SdkSnapshot source) => new(0);
		}

		internal static partial class Globals
		{
			[CheatEngineLuaOperation]
			[LuaGlobal("getVersion")]
			public static partial int ReadVersion(int address);

			[CheatEngineLuaOperation]
			[LuaGlobal("tryGetVersion")]
			public static partial bool TryReadVersion(int address, out int version);

			[CheatEngineLuaOperation(typeof(SnapshotMapper))]
			[LuaGlobal("getSnapshot")]
			public static partial SdkSnapshot ReadSnapshot();
		}
		""";

	// Stands for the SDK LuaBindings implementation of the [LuaGlobal] partial methods.
	private const string OperationImplementations =
		"""
		namespace TestPlugin;
		internal static partial class Globals
		{
			public static partial int ReadVersion(int address) => 0;

			public static partial bool TryReadVersion(int address, out int version)
			{
				version = 0;
				return true;
			}

			public static partial SdkSnapshot ReadSnapshot() => new();
		}
		""";

	[Fact]
	public void GeneratedModuleSdkLuaSurfaceIsTheExactSdkImposedRegistrationApi()
	{
		GeneratorRun run = GeneratorRun.Execute(ModuleSource);
		Assert.Empty(run.Diagnostics);

		string[] actual = [.. SdkLuaMemberReferences(Emit(run.OutputCompilation)).Order(StringComparer.Ordinal)];
		string[] allowed = [.. SdkImposedRegistrationSurface.Select(static member => member.Member)];

		string[] added = [.. actual.Except(allowed, StringComparer.Ordinal)];
		string[] removed = [.. allowed.Except(actual, StringComparer.Ordinal)];
		Assert.True(added.Length == 0,
			"New SDK Lua members in generated module code:" + Environment.NewLine + string.Join(Environment.NewLine, added) +
			Environment.NewLine + Guidance);
		Assert.True(removed.Length == 0,
			"These SDK Lua members are no longer used; shrink SdkImposedRegistrationSurface:" + Environment.NewLine +
			string.Join(Environment.NewLine, removed));
		Assert.Equal(allowed.Order(StringComparer.Ordinal), allowed);
		Assert.All(SdkImposedRegistrationSurface, static member =>
			Assert.False(string.IsNullOrWhiteSpace(member.Reason), member.Member + " needs a reason."));
	}

	[Fact]
	public void OnlyTheGeneratedAdapterCallsCheatEngineSdk()
	{
		GeneratorRun run = GeneratorRun.Execute(ModuleSource);
		List<string> violations = [];
		int adapterCalls = 0;
		foreach (SyntaxTree generated in run.OutputCompilation.SyntaxTrees.Where(static tree =>
					 tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal)))
		{
			bool isAdapter = generated.FilePath.EndsWith(RegistrarEmitter.AdapterHintName, StringComparison.Ordinal);
			SemanticModel model = run.OutputCompilation.GetSemanticModel(generated);
			foreach (SimpleNameSyntax name in generated.GetRoot(TestContext.Current.CancellationToken).DescendantNodes()
						 .OfType<SimpleNameSyntax>())
			{
				ISymbol? symbol = model.GetSymbolInfo(name, TestContext.Current.CancellationToken).Symbol;
				if (symbol is null or ITypeSymbol or INamespaceSymbol || symbol.ContainingType is not { } owner ||
					owner.ContainingAssembly?.Name.StartsWith("CheatEngine.SDK", StringComparison.Ordinal) != true)
				{
					continue;
				}

				if (isAdapter)
				{
					adapterCalls++;
				}
				else if (owner.TypeKind != TypeKind.Enum)
				{
					// The module and the registrar may name SDK enum values (constants), never call an SDK member.
					violations.Add($"{Path.GetFileName(generated.FilePath)}: {owner.ToDisplayString()}.{symbol.Name}");
				}
			}
		}

		Assert.True(violations.Count == 0,
			"Only the generated CheatEngine.SDK adapter may call CheatEngine.SDK:" + Environment.NewLine +
			string.Join(Environment.NewLine, violations) + Environment.NewLine + Guidance);
		Assert.True(adapterCalls > 0, "The generated adapter was expected to call CheatEngine.SDK.");
	}

	[Fact]
	public void OperationAdaptersStillUseNoLuaStateOrLuaRef()
	{
		GeneratorRun run = GeneratorRun.Execute(OperationSource);
		Assert.Empty(run.Diagnostics);
		foreach (GeneratedSourceResult source in run.GeneratedSources)
		{
			string text = source.SourceText.ToString();
			Assert.DoesNotContain("LuaState", text, StringComparison.Ordinal);
			Assert.DoesNotContain("LuaRef", text, StringComparison.Ordinal);
			Assert.DoesNotContain("LuaRuntime", text, StringComparison.Ordinal);
		}

		Compilation compilation = run.OutputCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(OperationImplementations,
			new CSharpParseOptions(LanguageVersion.CSharp14), cancellationToken: TestContext.Current.CancellationToken));
		Assert.Empty(SdkLuaMemberReferences(Emit(compilation)));
	}

	private static byte[] Emit(Compilation compilation)
	{
		using MemoryStream image = new();
		EmitResult result = compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
		return image.ToArray();
	}

	private static SortedSet<string> SdkLuaMemberReferences(byte[] image)
	{
		using PEReader reader = new(new MemoryStream(image));
		MetadataReader metadata = reader.GetMetadataReader();
		SortedSet<string> members = new(StringComparer.Ordinal);
		foreach (MemberReferenceHandle handle in metadata.MemberReferences)
		{
			MemberReference member = metadata.GetMemberReference(handle);
			string declaringType = member.Parent.Kind switch
			{
				HandleKind.TypeReference => TypeReferenceName(metadata, (TypeReferenceHandle) member.Parent),
				HandleKind.TypeSpecification => metadata.GetTypeSpecification((TypeSpecificationHandle) member.Parent)
					.DecodeSignature(SignatureNames.Instance, null),
				_ => string.Empty
			};
			if (!declaringType.StartsWith("CheatEngine.SDK.Lua", StringComparison.Ordinal))
			{
				continue;
			}

			string name = metadata.GetString(member.Name);
			MethodSignature<string> signature = member.DecodeMethodSignature(SignatureNames.Instance, null);
			members.Add($"{declaringType}::{name}({string.Join(",", signature.ParameterTypes)})->{signature.ReturnType}");
		}

		return members;
	}

	private static string TypeReferenceName(MetadataReader metadata, TypeReferenceHandle handle)
	{
		TypeReference reference = metadata.GetTypeReference(handle);
		string name = metadata.GetString(reference.Name);
		if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
		{
			return TypeReferenceName(metadata, (TypeReferenceHandle) reference.ResolutionScope) + "+" + name;
		}

		string ns = metadata.GetString(reference.Namespace);
		return ns.Length == 0 ? name : ns + "." + name;
	}

	private sealed record SdkImposedMember(string Member, string Reason);

	/// <summary>Type names in the display convention of the Client architecture ratchet (primitives lower-case).</summary>
	private sealed class SignatureNames : ISignatureTypeProvider<string, object?>
	{
		public static readonly SignatureNames Instance = new();

		public string GetArrayType(string elementType, ArrayShape shape)
		{
			return elementType + "[" + new string(',', shape.Rank - 1) + "]";
		}

		public string GetByReferenceType(string elementType)
		{
			return elementType + "&";
		}

		public string GetFunctionPointerType(MethodSignature<string> signature)
		{
			return "fnptr(" + string.Join(",", signature.ParameterTypes) + ")->" + signature.ReturnType;
		}

		public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
		{
			return genericType + "<" + string.Join(",", typeArguments) + ">";
		}

		public string GetGenericMethodParameter(object? genericContext, int index)
		{
			return "!!" + index;
		}

		public string GetGenericTypeParameter(object? genericContext, int index)
		{
			return "!" + index;
		}

		public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
		{
			return unmodifiedType;
		}

		public string GetPinnedType(string elementType)
		{
			return elementType;
		}

		public string GetPointerType(string elementType)
		{
			return elementType + "*";
		}

		public string GetPrimitiveType(PrimitiveTypeCode typeCode)
		{
			return typeCode.ToString().ToLowerInvariant();
		}

		public string GetSZArrayType(string elementType)
		{
			return elementType + "[]";
		}

		public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
		{
			TypeDefinition definition = reader.GetTypeDefinition(handle);
			string ns = reader.GetString(definition.Namespace);
			string name = reader.GetString(definition.Name);
			return ns.Length == 0 ? name : ns + "." + name;
		}

		public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
		{
			return TypeReferenceName(reader, handle);
		}

		public string GetTypeFromSpecification(MetadataReader reader, object? genericContext,
			TypeSpecificationHandle handle, byte rawTypeKind)
		{
			return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
		}
	}
}
