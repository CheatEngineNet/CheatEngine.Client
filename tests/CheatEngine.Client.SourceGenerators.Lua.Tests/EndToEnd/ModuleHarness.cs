using System.Reflection;

using CheatEngine.Client.Lua;
using CheatEngine.Client.SourceGenerators.Lua.Tests.Infrastructure;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.EndToEnd;

/// <summary>
///     Compiles a real generated module and the real generated registrar, replaces only the generated CheatEngine.SDK
///     adapter (<c>CheatEngineLuaRegistrationAdapter</c>) with one that drives <see cref="FakeLuaGlobals" />, then loads
///     the result and calls the module's public <c>Register</c> and <c>Unregister</c> (C1: every generated decision runs,
///     only the SDK registration set is a double).
/// </summary>
/// <remarks>
///     The adapter is replaceable because it is emitted as a file of its own and holds every SDK call: the SDK
///     <c>LuaRegistrationLease</c> has no public constructor, so no test can hand the real adapter a lease. What the real
///     adapter itself does is pinned by <c>GeneratedLuaSurfaceRatchetTests</c> (C0) and exercised by the composition test
///     with the real SDK generator; the host behaviour is the Q16 scenario.
/// </remarks>
internal sealed class ModuleHarness
{
	internal const string ModuleName = "plugin";

	/// <summary>Three exports, in descriptor order.</summary>
	internal static readonly string[] Exports = ["status", "ping", "marker"];

	internal const string ModuleSource =
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

			[LuaFunction("marker")]
			public static string Marker() => "marker";

			// Stands for the SDK LuaBindings output, which the Client generator cannot see in a test compilation.
			public static LuaRegistrationResult TryRegisterLuaFunctions(LuaState state,
				LuaRegistrationCollisionPolicy collisionPolicy = LuaRegistrationCollisionPolicy.RejectExisting) => default;
		}

		[CheatEngineLuaModule(typeof(PluginLuaBindings), "plugin")]
		internal sealed partial class PluginLuaModule : ILuaModule;
		""";

	// The replacement adapter: the same members as the generated one, over FakeLuaGlobals instead of CheatEngine.SDK.
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
			internal static LuaAdmissionStatus TryPublish(object? previous,
				System.Func<LuaState, LuaRegistrationResult> publish, out CheatEngineLuaPublication publication)
			{
				System.ArgumentNullException.ThrowIfNull(publish);
				FakeLuaGlobals globals = FakeLuaGlobals.Current;
				publication = default;
				if (globals.Admission != LuaAdmissionStatus.Admitted)
				{
					return globals.Admission;
				}

				FakePublication fake = globals.Publish((FakeLease?) previous);
				FakeLease? lease = fake.Lease;
				CheatEngineLuaRelease residual = default;
				if (lease is not null && fake.Kind != LuaRegistrationResultKind.Succeeded)
				{
					// As the generated adapter does: release the residual owner once more inside the admitted operation.
					residual = Copy(globals.Release(lease));
					lease = null;
				}

				publication = new CheatEngineLuaPublication(fake.Kind, lease, fake.FailedExport, fake.FailedStatus,
					Copy(fake.Rollback), residual);
				return LuaAdmissionStatus.Admitted;
			}

			internal static LuaAdmissionStatus TryRelease(object registration, out CheatEngineLuaRelease release)
			{
				FakeLuaGlobals globals = FakeLuaGlobals.Current;
				release = default;
				if (globals.Admission != LuaAdmissionStatus.Admitted)
				{
					return globals.Admission;
				}

				release = Copy(globals.Release((FakeLease) registration));
				return LuaAdmissionStatus.Admitted;
			}

			internal static CheatEngineLuaRelease ReleaseStale(object registration)
			{
				return Copy(FakeLuaGlobals.Current.ReleaseStale((FakeLease) registration));
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
