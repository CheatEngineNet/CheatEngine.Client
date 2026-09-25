using System.Globalization;
using System.Reflection;

using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.SourceGenerators.Lua.Tests.Infrastructure;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.DependencyInjection;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests;

public sealed class CheatEngineLuaGeneratorTests
{
	private const string ModulePrefix =
		"""
		using CheatEngine.Client.Lua;
		using CheatEngine.SDK.Annotations.Lua;
		namespace TestPlugin;
		internal static partial class PluginLuaBindings
		{
			[LuaFunction("status")]
			public static string Status() => "ok";

			[LuaFunction("ping")]
			public static int Ping() => 1;
		}
		""";

	private const string ScalarOperationSource =
		"""
		using CheatEngine.Client.Lua;
		using CheatEngine.SDK.Annotations.Lua;
		namespace TestPlugin;
		internal static partial class Globals
		{
			[CheatEngineLuaOperation]
			[LuaGlobal("getVersion")]
			public static partial int ReadVersion(int address);
		}
		""";

	private const string OutResultOperationSource =
		"""
		using CheatEngine.Client.Lua;
		using CheatEngine.SDK.Annotations.Lua;
		namespace TestPlugin;
		internal static partial class Globals
		{
			[CheatEngineLuaOperation]
			[LuaGlobal("getVersion")]
			public static partial bool TryReadVersion(int address, out int version);
		}
		""";

	private const string ModuleCompilationSource =
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

			public static LuaRegistrationResult TryRegisterLuaFunctions(LuaState state,
				LuaRegistrationCollisionPolicy collisionPolicy = LuaRegistrationCollisionPolicy.RejectExisting) => default;
		}

		[CheatEngineLuaModule(typeof(PluginLuaBindings), "plugin")]
		internal sealed partial class PluginLuaModule : ILuaModule;
		""";

	private const string MappedOperationSource =
		"""
		using CheatEngine.Client.Lua;
		using CheatEngine.SDK.Annotations.Lua;
		namespace TestPlugin;
		internal sealed class SdkSnapshot
		{
		}

		internal readonly record struct Snapshot(int Value);

		internal readonly struct SnapshotMapper : ILuaResultMapper<SdkSnapshot, Snapshot>
		{
			public static Snapshot Map(SdkSnapshot source) => new(0);
		}

		internal static partial class Globals
		{
			[CheatEngineLuaOperation(typeof(SnapshotMapper))]
			[LuaGlobal("getSnapshot")]
			public static partial SdkSnapshot ReadSnapshot();
		}
		""";

	private static readonly string[] InvalidModuleDiagnosticIds = ["CECLUA1001", "CECLUA1005"];

	[Fact]
	public void ModuleAdapterWithMultipleExportsCompilesAgainstTheSdkRegistrationContract()
	{
		GeneratorRun run = GeneratorRun.Execute(ModuleCompilationSource);

		Diagnostic[] diagnostics =
		[
			.. run.OutputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
				.Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
		];
		Assert.Empty(diagnostics);
	}

	[Fact]
	public void GeneratedInternalModuleHasAPublicConstructorAndResolvesViaAddLuaModule()
	{
		GeneratorRun run = GeneratorRun.Execute(ModuleCompilationSource);
		Assert.Empty(run.Diagnostics);
		Assert.Contains("public PluginLuaModule()", run.GeneratedText("PluginLuaModule.CheatEngineLuaModule.g.cs"),
			StringComparison.Ordinal);

		using MemoryStream image = new();
		EmitResult emitResult =
			run.OutputCompilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
		System.Reflection.Assembly generatedAssembly = System.Reflection.Assembly.Load(image.ToArray());
		Type? discoveredModuleType = generatedAssembly.GetType("TestPlugin.PluginLuaModule", false);
		Assert.NotNull(discoveredModuleType);
		Type moduleType = discoveredModuleType!;
		Assert.False(moduleType.IsPublic);
		Assert.NotNull(moduleType.GetConstructor(BindingFlags.Instance | BindingFlags.Public, null,
			Type.EmptyTypes, null));

		ServiceCollection services = new();
		CheatEngineClientBuilder builder = services.AddCheatEngineClient();
		MethodInfo addLuaModule = Assert.Single(
			typeof(CheatEngineClientBuilder).GetMethods(BindingFlags.Instance | BindingFlags.Public),
			static method => method.Name == nameof(CheatEngineClientBuilder.AddLuaModule) &&
							 method.IsGenericMethodDefinition);
		Assert.Same(builder, addLuaModule.MakeGenericMethod(moduleType).Invoke(builder, null));

		using ServiceProvider provider = services.BuildServiceProvider();
		object module = provider.GetRequiredService(moduleType);
		Assert.Equal(moduleType, module.GetType());
	}

	[Fact]
	public void ExplicitPublicConstructorIsPreservedForDependencyInjection()
	{
		GeneratorRun run = GeneratorRun.Execute(ModulePrefix +
												"""
		                                        public sealed class ModuleDependency;

		                                        [CheatEngineLuaModule(typeof(PluginLuaBindings), "plugin")]
		                                        internal sealed partial class PluginLuaModule : ILuaModule
		                                        {
		                                            public PluginLuaModule(ModuleDependency dependency)
		                                            {
		                                            }
		                                        }
		                                        """);

		Assert.Empty(run.Diagnostics);
		string generated = run.GeneratedText("PluginLuaModule.CheatEngineLuaModule.g.cs");
		Assert.DoesNotContain("public PluginLuaModule()", generated, StringComparison.Ordinal);
	}

	[Fact]
	public void OnlyNonPublicExplicitConstructorsProduceAnActionableDiagnostic()
	{
		GeneratorRun run = GeneratorRun.Execute(ModulePrefix +
												"""
		                                        [CheatEngineLuaModule(typeof(PluginLuaBindings), "plugin")]
		                                        internal sealed partial class PluginLuaModule : ILuaModule
		                                        {
		                                            internal PluginLuaModule()
		                                            {
		                                            }
		                                        }
		                                        """);

		Assert.Contains(run.Diagnostics, static diagnostic => diagnostic.Id == "CECLUA1006");
		Assert.Empty(run.GeneratedSources);
	}

	[Fact]
	public void ScalarOperationAdapterEmitsAReadonlyValueAndTypedFactory()
	{
		GeneratorRun run = GeneratorRun.Execute(ScalarOperationSource);

		Assert.Empty(run.Diagnostics);
		string generated = run.GeneratedText("ReadVersion.CheatEngineLuaOperation.g.cs");
		Assert.Contains(
			"public static ReadVersionLuaOperation CreateReadVersionLuaOperation(global::System.Int32 address)",
			generated, StringComparison.Ordinal);
		Assert.Contains("internal readonly record struct ReadVersionLuaOperation(global::System.Int32 _address)",
			generated, StringComparison.Ordinal);
		Assert.Contains("global::TestPlugin.Globals.ReadVersion(_address)", generated, StringComparison.Ordinal);
		Assert.Contains("CheatEngineFailureKind.LuaError", generated, StringComparison.Ordinal);
		Assert.Contains(
			"public static global::System.Int32 Execute(this global::CheatEngine.Client.Lua.ILuaClient client, in ReadVersionLuaOperation operation,",
			generated, StringComparison.Ordinal);
		Assert.Contains("return client.Execute<ReadVersionLuaOperation, global::System.Int32>(in operation, cancellationToken);",
			generated, StringComparison.Ordinal);
		Assert.Contains("public static bool TryExecute(this global::CheatEngine.Client.Lua.ILuaClient client, in ReadVersionLuaOperation operation,",
			generated, StringComparison.Ordinal);
		Assert.DoesNotContain("LuaState", generated, StringComparison.Ordinal);
		Assert.DoesNotContain("LuaRef", generated, StringComparison.Ordinal);
	}

	[Fact]
	public void GeneratedOperationExtensionsInferTheOperationAndResultTypes()
	{
		GeneratorRun run = GeneratorRun.Execute(ScalarOperationSource);
		Assert.Empty(run.Diagnostics);

		// Without the generated extensions, ILuaClient.Execute cannot infer TResult from the operation alone.
		Compilation compilation = run.OutputCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
			"""
			namespace TestPlugin;
			internal static partial class Globals
			{
				public static partial int ReadVersion(int address) => address;
			}

			internal static class Consumer
			{
				internal static int Run(CheatEngine.Client.Lua.ILuaClient client)
				{
					Globals.ReadVersionLuaOperation operation = Globals.CreateReadVersionLuaOperation(4);
					int value = client.Execute(operation);
					return client.TryExecute(operation, out int second, out _) ? value + second : value;
				}
			}
			""", new CSharpParseOptions(LanguageVersion.CSharp14),
			cancellationToken: TestContext.Current.CancellationToken));

		AssertNoCompilerDiagnostics(compilation);
	}

	[Fact]
	public void OutResultOperationProjectsTheSingleSdkOutValueAndMapsFalseToAFailure()
	{
		GeneratorRun run = GeneratorRun.Execute(OutResultOperationSource);

		Assert.Empty(run.Diagnostics);
		string generated = run.GeneratedText("TryReadVersion.CheatEngineLuaOperation.g.cs");
		Assert.Contains("global::System.Int32 source;", generated, StringComparison.Ordinal);
		Assert.Contains("if (!global::TestPlugin.Globals.TryReadVersion(_address, out source))", generated,
			StringComparison.Ordinal);
		Assert.Contains("CheatEngineFailureKind.LuaError", generated, StringComparison.Ordinal);
		Assert.Contains("result = source;", generated, StringComparison.Ordinal);
	}

	[Fact]
	public void MappedOperationUsesTheDeclaredStaticMapperAndProjectsTheResultType()
	{
		GeneratorRun run = GeneratorRun.Execute(MappedOperationSource);

		Assert.Empty(run.Diagnostics);
		string generated = run.GeneratedText("ReadSnapshot.CheatEngineLuaOperation.g.cs");
		Assert.Contains("ILuaOperation<global::TestPlugin.Snapshot>", generated, StringComparison.Ordinal);
		Assert.Contains("global::TestPlugin.SnapshotMapper.Map(source)", generated, StringComparison.Ordinal);
	}

	[Fact]
	public void TheMapperRunsOutsideTheBindingFailureClassification()
	{
		// The mapper is application code: an exception it throws must leave TryExecute unchanged, never be classified
		// as a binding failure, so its call follows the catch clauses that classify the SDK binding call.
		GeneratorRun run = GeneratorRun.Execute(MappedOperationSource);

		Assert.Empty(run.Diagnostics);
		string generated = run.GeneratedText("ReadSnapshot.CheatEngineLuaOperation.g.cs");
		int binding = generated.IndexOf("global::TestPlugin.Globals.ReadSnapshot()", StringComparison.Ordinal);
		int lastCatch = generated.LastIndexOf("catch (global::System.Exception exception)", StringComparison.Ordinal);
		int mapper = generated.IndexOf("global::TestPlugin.SnapshotMapper.Map(source)", StringComparison.Ordinal);
		Assert.True(binding >= 0 && binding < lastCatch, generated);
		Assert.True(mapper > lastCatch, generated);

		// The SDK source value is assigned inside the try block and read after it.
		AssertNoCompilerDiagnostics(run.OutputCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
			"""
			namespace TestPlugin;
			internal static partial class Globals
			{
				public static partial SdkSnapshot ReadSnapshot() => new();
			}
			""", new CSharpParseOptions(LanguageVersion.CSharp14),
			cancellationToken: TestContext.Current.CancellationToken)));
	}

	[Fact]
	public void AnOutResultOperationReadsItsSourceAfterTheBindingFailureClassification()
	{
		GeneratorRun run = GeneratorRun.Execute(OutResultOperationSource);

		Assert.Empty(run.Diagnostics);
		string generated = run.GeneratedText("TryReadVersion.CheatEngineLuaOperation.g.cs");
		int declaration = generated.IndexOf("global::System.Int32 source;", StringComparison.Ordinal);
		int binding = generated.IndexOf("global::TestPlugin.Globals.TryReadVersion(_address, out source)",
			StringComparison.Ordinal);
		int lastCatch = generated.LastIndexOf("catch (global::System.Exception exception)", StringComparison.Ordinal);
		Assert.True(declaration >= 0 && declaration < binding && binding < lastCatch, generated);
		Assert.True(generated.IndexOf("result = source;", StringComparison.Ordinal) > lastCatch, generated);
		AssertNoCompilerDiagnostics(run.OutputCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
			"""
			namespace TestPlugin;
			internal static partial class Globals
			{
				public static partial bool TryReadVersion(int address, out int version)
				{
					version = address;
					return true;
				}
			}
			""", new CSharpParseOptions(LanguageVersion.CSharp14),
			cancellationToken: TestContext.Current.CancellationToken)));
	}

	[Theory]
	[InlineData(
		"[CheatEngineLuaModule(typeof(PluginLuaBindings), \" \")] internal sealed partial class PluginLuaModule { }")]
	[InlineData(
		"[CheatEngineLuaModule(typeof(PluginLuaBindings), \"plugin\")] internal sealed class PluginLuaModule { }")]
	public void InvalidModuleFormsProduceActionableDiagnostics(string moduleDeclaration)
	{
		GeneratorRun run = GeneratorRun.Execute(ModulePrefix + moduleDeclaration);

		Diagnostic diagnostic =
			Assert.Single(run.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
		Assert.Contains(diagnostic.Id, InvalidModuleDiagnosticIds);
		Assert.Empty(run.GeneratedSources);
	}

	[Theory]
	[InlineData("abstract", "concrete")]
	[InlineData("file", "file-local")]
	public void AbstractOrFileLocalModulesProduceDeterministicShapeDiagnostics(string modifier,
		string expectedMessageFragment)
	{
		string source = ModulePrefix +
						"[CheatEngineLuaModule(typeof(PluginLuaBindings), \"plugin\")] " + modifier +
						" partial class PluginLuaModule { }";

		Diagnostic first = Assert.Single(GeneratorRun.Execute(source).Diagnostics
			.Where(static diagnostic => diagnostic.Id == "CECLUA1001"));
		Diagnostic second = Assert.Single(GeneratorRun.Execute(source).Diagnostics
			.Where(static diagnostic => diagnostic.Id == "CECLUA1001"));

		Assert.Equal(first.GetMessage(CultureInfo.InvariantCulture), second.GetMessage(CultureInfo.InvariantCulture));
		Assert.Contains(expectedMessageFragment, first.GetMessage(CultureInfo.InvariantCulture),
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("file static partial class FileLuaBindings", "typeof(FileLuaBindings)", "file-local")]
	[InlineData("internal static partial class GenericLuaBindings<T>", "typeof(GenericLuaBindings<>)", "non-generic")]
	public void FileLocalOrGenericBindingsProduceDeterministicDiagnostics(string bindingsDeclaration,
		string bindingsType, string expectedMessageFragment)
	{
		string source =
			$$"""
			  using CheatEngine.Client.Lua;
			  using CheatEngine.SDK.Annotations.Lua;
			  namespace TestPlugin;
			  {{bindingsDeclaration}}
			  {
			    [LuaFunction("status")]
			    public static string Status() => "ok";
			  }

			  [CheatEngineLuaModule({{bindingsType}}, "plugin")]
			  internal sealed partial class PluginLuaModule
			  {
			  }
			  """;

		Diagnostic first = Assert.Single(GeneratorRun.Execute(source).Diagnostics
			.Where(static diagnostic => diagnostic.Id == "CECLUA1002"));
		Diagnostic second = Assert.Single(GeneratorRun.Execute(source).Diagnostics
			.Where(static diagnostic => diagnostic.Id == "CECLUA1002"));

		Assert.Equal(first.GetMessage(CultureInfo.InvariantCulture), second.GetMessage(CultureInfo.InvariantCulture));
		Assert.Contains(expectedMessageFragment, first.GetMessage(CultureInfo.InvariantCulture),
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("file static partial class Globals", "file-local")]
	[InlineData("internal static partial class Globals<T>", "non-generic")]
	public void FileLocalOrGenericOperationContainersProduceDeterministicDiagnostics(string containerDeclaration,
		string expectedMessageFragment)
	{
		string source =
			$$"""
			  using CheatEngine.Client.Lua;
			  using CheatEngine.SDK.Annotations.Lua;
			  namespace TestPlugin;
			  {{containerDeclaration}}
			  {
			    [CheatEngineLuaOperation]
			    [LuaGlobal("readVersion")]
			    public static partial int ReadVersion();
			  }
			  """;

		Diagnostic first = Assert.Single(GeneratorRun.Execute(source).Diagnostics
			.Where(static diagnostic => diagnostic.Id == "CECLUA1101"));
		Diagnostic second = Assert.Single(GeneratorRun.Execute(source).Diagnostics
			.Where(static diagnostic => diagnostic.Id == "CECLUA1101"));

		Assert.Equal(first.GetMessage(CultureInfo.InvariantCulture), second.GetMessage(CultureInfo.InvariantCulture));
		Assert.Contains(expectedMessageFragment, first.GetMessage(CultureInfo.InvariantCulture),
			StringComparison.Ordinal);
		Assert.Empty(GeneratorRun.Execute(source).GeneratedSources);
	}

	[Fact]
	public void DuplicateModuleExportsProduceAnActionableDiagnostic()
	{
		GeneratorRun run = GeneratorRun.Execute(
			"""
			using CheatEngine.Client.Lua;
			using CheatEngine.SDK.Annotations.Lua;
			namespace TestPlugin;
			internal static partial class PluginLuaBindings
			{
				[LuaFunction("status")]
				public static string First() => "first";

				[LuaFunction("status")]
				public static string Second() => "second";
			}

			[CheatEngineLuaModule(typeof(PluginLuaBindings), "plugin")]
			internal sealed partial class PluginLuaModule
			{
			}
			""");

		Diagnostic diagnostic =
			Assert.Single(run.Diagnostics.Where(static diagnostic => diagnostic.Id == "CECLUA1004"));
		Assert.Contains("duplicate", diagnostic.GetMessage(CultureInfo.InvariantCulture),
			StringComparison.OrdinalIgnoreCase);
		Assert.Empty(run.GeneratedSources);
	}

	[Fact]
	public void OperationWithoutLuaGlobalProducesAnActionableDiagnostic()
	{
		GeneratorRun run = GeneratorRun.Execute(
			"""
			using CheatEngine.Client.Lua;
			namespace TestPlugin;
			internal static partial class Globals
			{
				[CheatEngineLuaOperation]
				public static partial int ReadVersion();
			}
			""");

		Diagnostic diagnostic =
			Assert.Single(run.Diagnostics.Where(static diagnostic => diagnostic.Id == "CECLUA1101"));
		Assert.Contains("[LuaGlobal]", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
	}

	[Fact]
	public void MapperCannotReturnRawLuaStateAcrossTheClientBoundary()
	{
		GeneratorRun run = GeneratorRun.Execute(
			"""
			using CheatEngine.Client.Lua;
			using CheatEngine.SDK.Annotations.Lua;
			using CheatEngine.SDK.Lua.State;
			namespace TestPlugin;
			internal readonly struct UnsafeMapper : ILuaResultMapper<LuaState, LuaState>
			{
				public static LuaState Map(LuaState source) => source;
			}

			internal static partial class Globals
			{
				[CheatEngineLuaOperation(typeof(UnsafeMapper))]
				[LuaGlobal("unsafe")]
				public static partial LuaState GetUnsafe();
			}
			""");

		Assert.Contains(run.Diagnostics, static diagnostic => diagnostic.Id == "CECLUA1106");
		Assert.Empty(run.GeneratedSources);
	}

	[Fact]
	public void MapperRejectsForbiddenTypesNestedInArraysGenericsTuplesAndDtos()
	{
		string[] sources =
		[
			CreateMappedOperationSource("global::CheatEngine.SDK.Lua.References.LuaRef[]"),
			CreateMappedOperationSource(
				"global::System.Collections.Immutable.ImmutableArray<global::System.Collections.Immutable.ImmutableArray<global::CheatEngine.SDK.Lua.References.LuaRef>>"),
			CreateMappedOperationSource("(int Code, global::CheatEngine.SDK.Engine.Objects.CEObject Handle)"),
			CreateMappedOperationSource(
				"global::CheatEngine.SDK.Engine.Objects.Owned<global::CheatEngine.SDK.Engine.Objects.CEObject>"),
			CreateMappedOperationSource("LeakingSnapshot",
				"internal sealed class LeakingSnapshot { private global::CheatEngine.SDK.Lua.References.LuaRef _reference; }"),
			CreateMappedOperationSource("UnsafeCallback",
				"internal delegate void UnsafeCallback(global::CheatEngine.SDK.Lua.References.LuaRef reference);"),
			CreateMappedOperationSource("ConstrainedSnapshot<IUnsafeConstraint>",
				"internal interface IUnsafeConstraint { global::CheatEngine.SDK.Lua.References.LuaRef Reference { get; } }" +
				Environment.NewLine +
				"internal sealed class ConstrainedSnapshot<T> where T : IUnsafeConstraint { }")
		];

		foreach (string source in sources)
		{
			GeneratorRun run = GeneratorRun.Execute(source);
			Diagnostic diagnostic =
				Assert.Single(run.Diagnostics.Where(static candidate => candidate.Id == "CECLUA1106"));
			Assert.Equal("CECLUA1106", diagnostic.Id);
			Assert.Empty(run.GeneratedSources);
		}
	}

	[Fact]
	public void MapperRejectsOpaqueFrameworkAndReflectionTypesEvenInsideApprovedCollections()
	{
		(string ResultType, string ExpectedType)[] cases =
		[
			("global::System.Collections.ArrayList", "System.Collections.ArrayList"),
			("global::System.Collections.IEnumerable", "System.Collections.IEnumerable"),
			("global::System.Runtime.InteropServices.GCHandle", "System.Runtime.InteropServices.GCHandle"),
			("global::Microsoft.Win32.SafeHandles.SafeFileHandle", "Microsoft.Win32.SafeHandles.SafeFileHandle"),
			("global::System.Reflection.MemberInfo", "System.Reflection.MemberInfo"),
			("global::System.Buffers.IMemoryOwner<byte>", "System.Buffers.IMemoryOwner<T>"),
			("global::System.Collections.Immutable.ImmutableArray<global::Microsoft.Win32.SafeHandles.SafeFileHandle>",
				"Microsoft.Win32.SafeHandles.SafeFileHandle")
		];

		foreach ((string resultType, string expectedType) in cases)
		{
			GeneratorRun run = GeneratorRun.Execute(CreateMappedOperationSource(resultType));
			Diagnostic diagnostic =
				Assert.Single(run.Diagnostics.Where(static candidate => candidate.Id == "CECLUA1106"));
			Assert.Contains(expectedType, diagnostic.GetMessage(CultureInfo.InvariantCulture),
				StringComparison.Ordinal);
			Assert.Empty(run.GeneratedSources);
		}
	}

	[Fact]
	public void MapperAllowsClosedImmutableAndReadOnlyFrameworkCollections()
	{
		string[] resultTypes =
		[
			"global::System.Collections.Generic.IEnumerable<int>",
			"global::System.Collections.Generic.IReadOnlyList<global::System.Collections.Immutable.ImmutableArray<int>>",
			"global::System.Collections.Immutable.ImmutableDictionary<string, global::System.Collections.Generic.IReadOnlySet<int>>",
			"global::System.Collections.ObjectModel.ReadOnlyDictionary<string, int>"
		];

		foreach (string resultType in resultTypes)
		{
			GeneratorRun run = GeneratorRun.Execute(CreateMappedOperationSource(resultType));
			Assert.Empty(run.Diagnostics);
			Assert.Single(run.GeneratedSources);
			Compilation generatedConsumer = run.OutputCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
				"""
				namespace TestPlugin;
				internal static partial class Globals
				{
					public static partial SdkSnapshot GetUnsafe() => new();
				}
				""",
				new CSharpParseOptions(LanguageVersion.CSharp14),
				cancellationToken: TestContext.Current.CancellationToken));
			AssertNoCompilerDiagnostics(generatedConsumer);
		}
	}

	[Fact]
	public void RecursiveBoundaryDiagnosticsAreDeterministic()
	{
		string source = CreateMappedOperationSource("ConstrainedSnapshot<IUnsafeConstraint>",
			"internal interface IUnsafeConstraint { global::CheatEngine.SDK.Lua.References.LuaRef Reference { get; } }" +
			Environment.NewLine +
			"internal sealed class ConstrainedSnapshot<T> where T : IUnsafeConstraint { }");

		Diagnostic first = Assert.Single(GeneratorRun.Execute(source).Diagnostics
			.Where(static candidate => candidate.Id == "CECLUA1106"));
		Diagnostic second = Assert.Single(GeneratorRun.Execute(source).Diagnostics
			.Where(static candidate => candidate.Id == "CECLUA1106"));

		Assert.Equal(first.GetMessage(CultureInfo.InvariantCulture),
			second.GetMessage(CultureInfo.InvariantCulture));
		Assert.Contains("forbidden SDK lifetime or interop type",
			first.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
	}

	[Fact]
	public void MapperRejectsForbiddenSourcesAndFunctionPointerResults()
	{
		GeneratorRun unsafeSource = GeneratorRun.Execute(
			CreateMappedOperationSource(
				"int",
				string.Empty,
				"global::CheatEngine.SDK.Lua.References.LuaRef[]"));
		Assert.Contains(unsafeSource.Diagnostics, static candidate => candidate.Id == "CECLUA1106");
		Assert.Empty(unsafeSource.GeneratedSources);

		GeneratorRun functionPointer = GeneratorRun.Execute(
			"""
			using CheatEngine.Client.Lua;
			using CheatEngine.SDK.Annotations.Lua;
			namespace TestPlugin;
			internal static partial class Globals
			{
				[CheatEngineLuaOperation]
				[LuaGlobal("pointer")]
				public static unsafe partial delegate* unmanaged[Cdecl]<int, int> ReadPointer();
			}
			""");
		Assert.Contains(functionPointer.Diagnostics, static candidate => candidate.Id == "CECLUA1104");
		Assert.Empty(functionPointer.GeneratedSources);
	}

	[Fact]
	public void ApprovedSdkValuesAndClosedDtosCompileThroughTheGeneratedConsumerBoundary()
	{
		GeneratorRun run = GeneratorRun.Execute(
			"""
			using CheatEngine.Client.Lua;
			using CheatEngine.SDK.Annotations.Lua;
			using CheatEngine.SDK.Engine.Values;
			namespace TestPlugin;
			public sealed class SdkSnapshot { }
			public readonly record struct SafeSnapshot(Address Address, int Version);
			internal readonly struct SafeMapper : ILuaResultMapper<SdkSnapshot, System.Collections.Immutable.ImmutableArray<SafeSnapshot>>
			{
				public static System.Collections.Immutable.ImmutableArray<SafeSnapshot> Map(SdkSnapshot source) => default;
			}

			public static partial class Globals
			{
				[CheatEngineLuaOperation(typeof(SafeMapper))]
				[LuaGlobal("snapshot")]
				public static partial SdkSnapshot ReadSnapshot();
			}
			""");

		Assert.Empty(run.Diagnostics);
		Compilation generatedConsumer = run.OutputCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
			"""
			namespace TestPlugin;
			public static partial class Globals
			{
				public static partial SdkSnapshot ReadSnapshot() => new();
			}
			""",
			new CSharpParseOptions(LanguageVersion.CSharp14),
			cancellationToken: TestContext.Current.CancellationToken));
		AssertNoCompilerDiagnostics(generatedConsumer);

		using MemoryStream generatedImage = new();
		EmitResult generatedEmit = generatedConsumer.Emit(generatedImage,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(generatedEmit.Success, string.Join(Environment.NewLine, generatedEmit.Diagnostics));

		Compilation downstreamConsumer = GeneratorRun.CreateConsumerCompilation(
			"""
			using System.Collections.Immutable;
			using CheatEngine.Client.Lua;
			using TestPlugin;
			namespace Consumer;
			public static class GeneratedApiConsumer
			{
				public static ILuaOperation<ImmutableArray<SafeSnapshot>> Create() =>
					Globals.CreateReadSnapshotLuaOperation();
			}
			""",
			generatedImage.ToArray());
		AssertNoCompilerDiagnostics(downstreamConsumer);

		using MemoryStream downstreamImage = new();
		EmitResult downstreamEmit = downstreamConsumer.Emit(downstreamImage,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(downstreamEmit.Success, string.Join(Environment.NewLine, downstreamEmit.Diagnostics));
	}

	private static string CreateMappedOperationSource(string resultType, string? additionalDeclarations = null,
		string sourceType = "SdkSnapshot")
	{
		return string.Join(
			Environment.NewLine, "using CheatEngine.Client.Lua;", "using CheatEngine.SDK.Annotations.Lua;",
			"namespace TestPlugin;", "internal sealed class SdkSnapshot { }", additionalDeclarations ?? string.Empty,
			"internal readonly struct UnsafeMapper : ILuaResultMapper<" + sourceType + ", " + resultType + ">", "{",
			"\tpublic static " + resultType + " Map(" + sourceType + " source) => default;", "}",
			"internal static partial class Globals", "{", "\t[CheatEngineLuaOperation(typeof(UnsafeMapper))]",
			"\t[LuaGlobal(\"unsafe\")]", "\tpublic static partial " + sourceType + " GetUnsafe();", "}");
	}

	private static void AssertNoCompilerDiagnostics(Compilation compilation)
	{
		Diagnostic[] diagnostics =
		[
			.. compilation.GetDiagnostics(TestContext.Current.CancellationToken)
				.Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
		];
		Assert.Empty(diagnostics);
	}
}
