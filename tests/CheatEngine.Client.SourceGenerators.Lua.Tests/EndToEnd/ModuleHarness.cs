using System.Reflection;

using CheatEngine.Client.Lua;
using CheatEngine.Client.SourceGenerators.Lua.Tests.Infrastructure;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.EndToEnd;

/// <summary>
///     Compiles a real generated module and the real generated registrar, replaces only the generated CheatEngine.SDK
///     adapter (<c>CheatEngineLuaRegistrationAdapter</c>) with one that forwards each of its SDK calls to
///     <see cref="FakeLuaGlobals" />, then loads the result and calls the module's public <c>Register</c> and
///     <c>Unregister</c> (C1: every generated decision runs, only the SDK calls are doubles).
/// </summary>
/// <remarks>
///     The adapter is replaceable because it is emitted as a file of its own and holds every SDK call, one call per
///     member and no decision: the SDK <c>LuaRegistrationLease</c> has no public constructor, so no test can hand the real
///     adapter a lease. The replacement keeps that shape and adds no logic, so the tests observe the registrar itself.
///     What the real adapter calls is pinned by <c>GeneratedLuaSurfaceRatchetTests</c> (C0) and compiled against the
///     real SDK generator by the composition test; the host behaviour is the Q16 scenario.
/// </remarks>
internal sealed class ModuleHarness
{
	internal const string ModuleName = "plugin";

	/// <summary>Three exports, in descriptor order.</summary>
	internal static readonly string[] Exports = ["status", "ping", "marker"];

	internal const string ModuleSource =
		"""
		using CheatEngine.Client.Lua;
		using CheatEngine.Client.SourceGenerators.Lua.Tests.EndToEnd;
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

			[LuaFunction("marker")]
			public static string Marker() => "marker";

			// Stands for the SDK LuaBindings output, which the Client generator cannot see in a test compilation: it records
			// the call, and the double's registration set supplies the result.
			public static LuaRegistrationResult TryRegisterLuaFunctions(LuaState state,
				LuaRegistrationCollisionPolicy collisionPolicy = LuaRegistrationCollisionPolicy.RejectExisting)
			{
				FakeLuaGlobals.Current.RecordBindingsRegistration(collisionPolicy);
				return default;
			}
		}

		[CheatEngineLuaModule(typeof(PluginLuaBindings), "plugin")]
		internal sealed partial class PluginLuaModule : ILuaModule;
		""";

	// The replacement adapter: the members of the generated one, each forwarding its one SDK call to FakeLuaGlobals and
	// copying the result exactly as the generated adapter copies the SDK's. It holds no decision of its own.
	private const string FakeAdapterSource =
		"""
		#nullable enable
		using CheatEngine.Client.SourceGenerators.Lua.Tests.EndToEnd;
		using CheatEngine.SDK.Lua.Registration;
		using CheatEngine.SDK.Lua.Runtime;
		using CheatEngine.SDK.Lua.State;
		namespace CheatEngine.Client.Lua.Generated;

		internal static class CheatEngineLuaRegistrationAdapter
		{
			internal static LuaAdmissionStatus TryAdmit(out LuaRuntimeOperation operation, out LuaState state)
			{
				operation = default;
				state = default;
				return FakeLuaGlobals.Current.Admit();
			}

			internal static void EndOperation(ref LuaRuntimeOperation operation)
			{
				FakeLuaGlobals.Current.EndOperation();
			}

			internal static CheatEngineLuaPublication Publish(System.Func<LuaState, LuaRegistrationResult> publish,
				LuaState state)
			{
				_ = publish(state);
				FakePublication fake = FakeLuaGlobals.Current.Publish();
				return new CheatEngineLuaPublication(fake.Kind, fake.Lease, fake.FailedExport, fake.FailedStatus,
					Copy(fake.Rollback));
			}

			internal static CheatEngineLuaRelease Release(object lease, LuaState state)
			{
				return Copy(FakeLuaGlobals.Current.Release((FakeLease) lease));
			}

			internal static CheatEngineLuaRelease ReleaseStale(object lease)
			{
				return Copy(FakeLuaGlobals.Current.ReleaseStale((FakeLease) lease));
			}

			private static CheatEngineLuaRelease Copy(FakeRelease release)
			{
				return new CheatEngineLuaRelease(release.Kind, release.RemovedCount, release.RestoredCount,
					release.ReplacementCount, release.RemainingCount, release.FailedExports);
			}
		}
		""";

	private static readonly Lazy<ModuleHarness> SShared = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

	private ModuleHarness(System.Reflection.Assembly assembly)
	{
		Assembly = assembly;
		ModuleType = assembly.GetType("TestPlugin.PluginLuaModule", true)!;
		RegistrarType = assembly.GetType(RegistrarEmitter.Namespace + "." + RegistrarEmitter.RegistrarType, true)!;
	}

	/// <summary>Gets the harness, compiled and loaded once per test run.</summary>
	internal static ModuleHarness Shared => SShared.Value;

	internal System.Reflection.Assembly Assembly
	{
		get;
	}

	internal Type ModuleType
	{
		get;
	}

	/// <summary>Gets the real generated registrar of the harness assembly.</summary>
	internal Type RegistrarType
	{
		get;
	}

	/// <summary>Creates a fresh module instance through its generated public constructor.</summary>
	internal ILuaModule CreateModule()
	{
		return (ILuaModule) Activator.CreateInstance(ModuleType)!;
	}

	/// <summary>Creates the double of the harness module's SDK registration set.</summary>
	internal static FakeLuaGlobals CreateGlobals()
	{
		return new FakeLuaGlobals(ModuleName, Exports);
	}

	/// <summary>Runs the generated public <c>Register</c> against the double.</summary>
	internal static void Register(ILuaModule module, FakeLuaGlobals globals)
	{
		using IDisposable active = globals.Activate();
		module.Register();
	}

	/// <summary>Runs the generated public <c>Unregister</c> against the double.</summary>
	internal static LuaModuleReleaseOutcome Unregister(ILuaModule module, FakeLuaGlobals globals)
	{
		using IDisposable active = globals.Activate();
		return module.Unregister();
	}

	/// <summary>Invokes one internal static method of the real generated registrar.</summary>
	internal TResult InvokeRegistrar<TResult>(string method, object argument)
	{
		MethodInfo target = RegistrarType.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static) ??
							throw new InvalidOperationException($"The generated registrar has no '{method}' method.");
		return (TResult) target.Invoke(null, [argument])!;
	}

	private static ModuleHarness Build()
	{
		GeneratorRun run = GeneratorRun.Execute([ModuleSource],
			[MetadataReference.CreateFromFile(typeof(FakeLuaGlobals).Assembly.Location)]);
		Assert.Empty(run.Diagnostics);
		SyntaxTree adapter = Assert.Single(run.OutputCompilation.SyntaxTrees, static tree =>
			tree.FilePath.EndsWith(RegistrarEmitter.AdapterHintName, StringComparison.Ordinal));
		Compilation compilation = run.OutputCompilation.ReplaceSyntaxTree(adapter,
			CSharpSyntaxTree.ParseText(FakeAdapterSource, (CSharpParseOptions) adapter.Options, adapter.FilePath));
		Diagnostic[] compilerDiagnostics =
		[
			.. compilation.GetDiagnostics()
				.Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
		];
		Assert.True(compilerDiagnostics.Length == 0, string.Join(Environment.NewLine, compilerDiagnostics));

		using MemoryStream image = new();
		EmitResult emit = compilation.Emit(image);
		Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
		return new ModuleHarness(System.Reflection.Assembly.Load(image.ToArray()));
	}
}
