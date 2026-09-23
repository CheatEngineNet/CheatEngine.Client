using System.Reflection;

using CheatEngine.Client.Lua;
using CheatEngine.Client.SourceGenerators.Lua.Tests.Infrastructure;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.EndToEnd;

/// <summary>
///     Compiles a real generated module together with a second <c>partial</c> part of the same class that implements the
///     generated private port with <see cref="FakeLuaGlobals" />, then loads it and drives the emitted
///     <c>RegisterCore</c>/<c>UnregisterCore</c> algorithm (C1: the emitted code executes, only the Lua state is a double).
/// </summary>
internal sealed class ModuleHarness
{
	internal const string ModuleName = "plugin";

	/// <summary>Three exports, in descriptor order.</summary>
	internal static readonly string[] Exports = ["status", "ping", "marker"];

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

			[LuaFunction("marker")]
			public static string Marker() => "marker";

			// Stands for the SDK LuaBindings output, which the Client generator cannot see in a test compilation.
			public static LuaStatus RegisterLuaFunctions(LuaState state) => default;
		}

		[CheatEngineLuaModule(typeof(PluginLuaBindings), "plugin")]
		internal sealed partial class PluginLuaModule : ILuaModule;
		""";

	// The second partial part: it sees the generated private port, ownership enum, descriptor and core methods.
	private const string HarnessSource =
		"""
		#nullable enable
		using CheatEngine.Client.SourceGenerators.Lua.Tests.EndToEnd;
		namespace TestPlugin;
		internal sealed partial class PluginLuaModule
		{
			internal static void HarnessRegister(object module, FakeLuaGlobals globals)
			{
				HarnessPort port = new HarnessPort(globals);
				((PluginLuaModule) module).__CheatEngineLuaRegisterCore<HarnessPort, FakeLuaPin>(ref port);
			}

			internal static void HarnessUnregister(object module, FakeLuaGlobals globals)
			{
				HarnessPort port = new HarnessPort(globals);
				((PluginLuaModule) module).__CheatEngineLuaUnregisterCore<HarnessPort, FakeLuaPin>(ref port);
			}

			private sealed class HarnessPort : __CheatEngineLuaIGlobalPort<FakeLuaPin>
			{
				private readonly FakeLuaGlobals _globals;

				public HarnessPort(FakeLuaGlobals globals)
				{
					_globals = globals;
				}

				public System.Exception? ProbeVacant(int export, out bool vacant)
				{
					return _globals.ProbeVacant(Name(export), out vacant);
				}

				public System.Exception? Publish()
				{
					string[] names = new string[s_descriptor.Exports.Length];
					for (int index = 0; index < names.Length; index++)
					{
						names[index] = Name(index);
					}

					return _globals.Publish(names, s_descriptor.Name);
				}

				public System.Exception? Capture(int export, out FakeLuaPin? token)
				{
					return _globals.Capture(Name(export), out token);
				}

				public bool IsCurrent(FakeLuaPin token)
				{
					return _globals.IsCurrent(token);
				}

				public __CheatEngineLuaOwnership Observe(int export, FakeLuaPin token, out System.Exception? failure)
				{
					switch (_globals.Observe(Name(export), token, out failure))
					{
						case FakeObservation.ReadFailed:
							return __CheatEngineLuaOwnership.ReadFailed;
						case FakeObservation.Absent:
							return __CheatEngineLuaOwnership.Absent;
						case FakeObservation.Owned:
							return __CheatEngineLuaOwnership.Owned;
						case FakeObservation.Replaced:
							return __CheatEngineLuaOwnership.Replaced;
						default:
							return __CheatEngineLuaOwnership.OwnershipUnresolvable;
					}
				}

				public System.Exception? Clear(int export)
				{
					return _globals.Clear(Name(export));
				}

				public System.Exception? Release(FakeLuaPin token)
				{
					return _globals.Release(token);
				}

				// The port index is the descriptor order: the generated export order is part of the contract.
				private static string Name(int export)
				{
					return s_descriptor.Exports[export].Name;
				}
			}
		}
		""";

	private static readonly Lazy<ModuleHarness> SShared = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

	private readonly Action<object, FakeLuaGlobals> _register;
	private readonly Action<object, FakeLuaGlobals> _unregister;

	private ModuleHarness(Type moduleType, Action<object, FakeLuaGlobals> register,
		Action<object, FakeLuaGlobals> unregister)
	{
		ModuleType = moduleType;
		_register = register;
		_unregister = unregister;
	}

	/// <summary>Gets the harness, compiled and loaded once per test run.</summary>
	internal static ModuleHarness Shared => SShared.Value;

	internal Type ModuleType
	{
		get;
	}

	/// <summary>Creates a fresh module instance through its generated public constructor.</summary>
	internal IOwnershipAwareLuaModule CreateModule()
	{
		return (IOwnershipAwareLuaModule) Activator.CreateInstance(ModuleType)!;
	}

	/// <summary>Runs the emitted registration algorithm against the double.</summary>
	internal void Register(IOwnershipAwareLuaModule module, FakeLuaGlobals globals)
	{
		_register(module, globals);
	}

	/// <summary>Runs the emitted release algorithm against the double.</summary>
	internal void Unregister(IOwnershipAwareLuaModule module, FakeLuaGlobals globals)
	{
		_unregister(module, globals);
	}

	private static ModuleHarness Build()
	{
		GeneratorRun run = GeneratorRun.Execute([ModuleSource, HarnessSource],
			[MetadataReference.CreateFromFile(typeof(FakeLuaGlobals).Assembly.Location)]);
		Assert.Empty(run.Diagnostics);
		Diagnostic[] compilerDiagnostics =
		[
			.. run.OutputCompilation.GetDiagnostics()
				.Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
		];
		Assert.True(compilerDiagnostics.Length == 0, string.Join(Environment.NewLine, compilerDiagnostics));

		using MemoryStream image = new();
		EmitResult emit = run.OutputCompilation.Emit(image);
		Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
		System.Reflection.Assembly assembly = System.Reflection.Assembly.Load(image.ToArray());
		Type moduleType = assembly.GetType("TestPlugin.PluginLuaModule", true)!;
		const BindingFlags Internal = BindingFlags.NonPublic | BindingFlags.Static;
		return new ModuleHarness(
			moduleType,
			moduleType.GetMethod("HarnessRegister", Internal)!.CreateDelegate<Action<object, FakeLuaGlobals>>(),
			moduleType.GetMethod("HarnessUnregister", Internal)!.CreateDelegate<Action<object, FakeLuaGlobals>>());
	}
}
