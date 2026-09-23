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
///     C0 ratchet of the single registered ADR-01 exception of generated Client code (Q16 on CheatEngine.SDK 1.0.0): the
///     SDK Lua members a generated <c>[CheatEngineLuaModule]</c> adapter may use are frozen, and only its SDK port touches
///     the Lua state. Removal: SDK 2.0 registration leases (docs/migration/sdk-2.0.md).
/// </summary>
/// <remarks>
///     The members are read from the emitted image with System.Reflection.Metadata (member references whose declaring
///     type is in a <c>CheatEngine.SDK.Lua</c> namespace); no generated code runs.
/// </remarks>
public sealed class GeneratedLuaSurfaceRatchetTests
{
	private const string Adr01Guidance =
		"ADR-01 exception: generated Client Lua module code may only use the frozen SDK Lua members; register any change in the ratchet and in docs/migration/sdk-2.0.md.";

	private const string SdkPortName = "__CheatEngineLuaSdkPort";

	/// <summary>The frozen SDK Lua surface of generated module code; it may only shrink (SDK 2.0 leases remove it).</summary>
	private static readonly string[] FrozenModuleSdkLuaMembers =
	[
		"CheatEngine.SDK.Lua.Calls.LuaError::FromStack(CheatEngine.SDK.Lua.State.LuaState,CheatEngine.SDK.Lua.Calls.LuaStatus)->CheatEngine.SDK.Lua.Calls.LuaError",
		"CheatEngine.SDK.Lua.Calls.LuaException::.ctor(CheatEngine.SDK.Lua.Calls.LuaError)->void",
		"CheatEngine.SDK.Lua.Calls.LuaException::.ctor(string,System.Exception)->void",
		"CheatEngine.SDK.Lua.Calls.LuaStatus::get_IsOk()->boolean",
		"CheatEngine.SDK.Lua.References.LuaRef::Release(CheatEngine.SDK.Lua.State.LuaState)->void",
		"CheatEngine.SDK.Lua.References.LuaRef::get_IsCurrent()->boolean",
		"CheatEngine.SDK.Lua.Runtime.LuaRuntime::AcquireOperation()->CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation",
		"CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation::Dispose()->void",
		"CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation::get_State()->CheatEngine.SDK.Lua.State.LuaState",
		"CheatEngine.SDK.Lua.State.LuaState::CreateRef()->CheatEngine.SDK.Lua.References.LuaRef",
		"CheatEngine.SDK.Lua.State.LuaState::IsNil(int32)->boolean",
		"CheatEngine.SDK.Lua.State.LuaState::PushNil()->void",
		"CheatEngine.SDK.Lua.State.LuaState::RawEquals(int32,int32)->boolean",
		"CheatEngine.SDK.Lua.State.LuaState::SetTop(int32)->void",
		"CheatEngine.SDK.Lua.State.LuaState::TryGetGlobal(System.ReadOnlySpan`1<byte>)->CheatEngine.SDK.Lua.Calls.LuaStatus",
		"CheatEngine.SDK.Lua.State.LuaState::TryPushRef(CheatEngine.SDK.Lua.References.LuaRef)->boolean",
		"CheatEngine.SDK.Lua.State.LuaState::TrySetGlobal(System.ReadOnlySpan`1<byte>)->CheatEngine.SDK.Lua.Calls.LuaStatus",
		"CheatEngine.SDK.Lua.State.LuaState::get_Top()->int32"
	];

	private const string ModuleSource =
		"""
		using CheatEngine.Client.Lua;
		using CheatEngine.SDK.Annotations.Lua;
		using CheatEngine.SDK.Lua.Calls;
		using CheatEngine.SDK.Lua.State;
		namespace TestPlugin;
		internal static partial class PluginLuaBindings
		{
			[LuaFunction("status")]
			public static string Status() => "ok";

			[LuaFunction("ping")]
			public static int Ping() => 1;

			// Stands for the SDK LuaBindings output; it adds no SDK member reference of its own.
			public static LuaStatus RegisterLuaFunctions(LuaState state) => default;
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
	public void GeneratedModuleSdkLuaSurfaceIsFrozenToTheRegisteredAdr01Exception()
	{
		GeneratorRun run = GeneratorRun.Execute(ModuleSource);
		Assert.Empty(run.Diagnostics);

		string[] actual = [.. SdkLuaMemberReferences(Emit(run.OutputCompilation)).Order(StringComparer.Ordinal)];

		string[] added = [.. actual.Except(FrozenModuleSdkLuaMembers, StringComparer.Ordinal)];
		string[] removed = [.. FrozenModuleSdkLuaMembers.Except(actual, StringComparer.Ordinal)];
		Assert.True(added.Length == 0,
			"New SDK Lua members in generated module code:" + Environment.NewLine + string.Join(Environment.NewLine, added) +
			Environment.NewLine + Adr01Guidance);
		Assert.True(removed.Length == 0,
			"These frozen SDK Lua members are no longer used; shrink the ratchet and the docs/migration/sdk-2.0.md entry:" +
			Environment.NewLine + string.Join(Environment.NewLine, removed));
		Assert.Equal(FrozenModuleSdkLuaMembers.Order(StringComparer.Ordinal), FrozenModuleSdkLuaMembers);
	}

	[Fact]
	public void OnlyTheSdkPortTouchesLuaState()
	{
		GeneratorRun run = GeneratorRun.Execute(ModuleSource);
		SyntaxTree generated = Assert.Single(run.OutputCompilation.SyntaxTrees,
			static tree => tree.FilePath.EndsWith("PluginLuaModule.CheatEngineLuaModule.g.cs", StringComparison.Ordinal));
		SemanticModel model = run.OutputCompilation.GetSemanticModel(generated);
		SyntaxNode root = generated.GetRoot(TestContext.Current.CancellationToken);
		StructDeclarationSyntax port = Assert.Single(root.DescendantNodes().OfType<StructDeclarationSyntax>(),
			static node => node.Identifier.ValueText == SdkPortName);

		List<string> violations = [];
		int portAccesses = 0;
		foreach (SimpleNameSyntax name in root.DescendantNodes().OfType<SimpleNameSyntax>())
		{
			ISymbol? symbol = model.GetSymbolInfo(name, TestContext.Current.CancellationToken).Symbol;
			if (symbol is null or ITypeSymbol || symbol.ContainingType is not { } declaringType)
			{
				continue;
			}

			string owner = declaringType.ToDisplayString();
			if (owner is "CheatEngine.SDK.Lua.State.LuaState" or "CheatEngine.SDK.Lua.References.LuaRef")
			{
				if (port.Span.Contains(name.Span))
				{
					portAccesses++;
				}
				else
				{
					violations.Add($"{owner}.{symbol.Name} at line {name.GetLocation().GetLineSpan().StartLinePosition.Line + 1}");
				}
			}
			else if (owner is "CheatEngine.SDK.Lua.Runtime.LuaRuntime" or "CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation")
			{
				MethodDeclarationSyntax? method = name.FirstAncestorOrSelf<MethodDeclarationSyntax>();
				if (method?.Identifier.ValueText is not ("Register" or "Unregister") || port.Span.Contains(name.Span))
				{
					violations.Add($"{owner}.{symbol.Name} outside the public Register/Unregister admission");
				}
			}
		}

		Assert.True(violations.Count == 0,
			"Only the generated SDK port may touch LuaState or LuaRef:" + Environment.NewLine +
			string.Join(Environment.NewLine, violations) + Environment.NewLine + Adr01Guidance);
		Assert.True(portAccesses > 0, "The SDK port was expected to use the Lua state.");
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
