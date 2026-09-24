using System.Reflection;

using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.Client.SourceGenerators.Lua.Tests.Infrastructure;
using CheatEngine.SDK.Lua.Calls;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.EndToEnd;

/// <summary>
///     CRIT-07, the result side: CheatEngine.SDK 2.0.0 refuses a Lua float at or above 2^53 where an integer or an address
///     is declared, instead of rounding it. The throwing form of the binding raises the <see cref="LuaException" /> its
///     <c>ThrowUnexpectedResult</c> helper raises; the Try form returns <see langword="false" />. The generated Client
///     operation classifies both as a <see cref="CheatEngineFailureKind.LuaError" /> failure and never lets the SDK
///     exception escape its <c>TryExecute</c>.
/// </summary>
/// <remarks>
///     The bindings bodies stand for the SDK-generated ones and raise exactly what the SDK raises for such a refusal;
///     <c>RealSdkGeneratorCompositionTests</c> proves that the real bindings read integers with the refusing marshallers.
/// </remarks>
public sealed class OperationRefusalEndToEndTests
{
	private const string RefusedValue =
		"The Lua global 'getVersion' returned a number value, not an integer.";

	private const string Source =
		"""
		using CheatEngine.Client.Lua;
		using CheatEngine.SDK.Annotations.Lua;
		namespace TestPlugin;
		public static partial class Globals
		{
			[CheatEngineLuaOperation]
			[LuaGlobal("getVersion")]
			public static partial long ReadVersion(long address);

			[CheatEngineLuaOperation]
			[LuaGlobal("tryGetVersion")]
			public static partial bool TryReadVersion(long address, out long version);
		}
		""";

	// What the SDK-generated bodies do when the integer marshaller refuses 9007199254740992.0 (2^53).
	private const string RefusingBindings =
		"""
		namespace TestPlugin;
		public static partial class Globals
		{
			public static partial long ReadVersion(long address) =>
				throw new CheatEngine.SDK.Lua.Calls.LuaException(
					"The Lua global 'getVersion' returned a number value, not an integer.");

			public static partial bool TryReadVersion(long address, out long version)
			{
				version = 0;
				return false;
			}
		}
		""";

	private static readonly Lazy<Type> SGlobals = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

	[Fact]
	public void AThrowingBindingThatRefusesTheValueIsALuaErrorFailure()
	{
		(bool succeeded, long result, CheatEngineFailure failure) = Execute("CreateReadVersionLuaOperation");

		Assert.False(succeeded);
		Assert.Equal(0, result);
		Assert.Equal(CheatEngineFailureKind.LuaError, failure.Kind);
		Assert.Equal("Lua.Operation.Globals.ReadVersion", failure.Operation);
		Assert.Equal(RefusedValue, failure.Message);
		LuaException refusal = Assert.IsType<LuaException>(failure.Exception);
		// No Lua error was raised: the call returned and the SDK refused what it returned.
		Assert.Equal(LuaStatus.Ok, refusal.Status);
	}

	[Fact]
	public void ATryBindingThatRefusesTheValueIsALuaErrorFailure()
	{
		(bool succeeded, long result, CheatEngineFailure failure) = Execute("CreateTryReadVersionLuaOperation");

		Assert.False(succeeded);
		Assert.Equal(0, result);
		Assert.Equal(CheatEngineFailureKind.LuaError, failure.Kind);
		Assert.Equal("Lua.Operation.Globals.TryReadVersion", failure.Operation);
		Assert.Null(failure.Exception);
	}

	private static (bool Succeeded, long Result, CheatEngineFailure Failure) Execute(string factory)
	{
		object operation = SGlobals.Value.GetMethod(factory, BindingFlags.Public | BindingFlags.Static)!
			.Invoke(null, [9007199254740992L])!;
		ILuaOperation<long> typed = Assert.IsAssignableFrom<ILuaOperation<long>>(operation);
		bool succeeded = typed.TryExecute(new ActiveContext(), out long result, out CheatEngineFailure failure);
		return (succeeded, result, failure);
	}

	private static Type Build()
	{
		GeneratorRun run = GeneratorRun.Execute(Source);
		Assert.Empty(run.Diagnostics);
		Compilation compilation = run.OutputCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(RefusingBindings,
			new CSharpParseOptions(LanguageVersion.CSharp14)));
		using MemoryStream image = new();
		EmitResult emit = compilation.Emit(image);
		Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
		return System.Reflection.Assembly.Load(image.ToArray()).GetType("TestPlugin.Globals", true)!;
	}

	private sealed class ActiveContext : ILuaExecutionContext
	{
		public long Epoch => 1;

		public bool IsActive => true;

		public void ThrowIfExpired()
		{
		}
	}
}
