using System.Reflection;

using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.Client.SourceGenerators.Lua.Tests.Infrastructure;

using SdkOptional = CheatEngine.SDK.Lua.Marshalling.LuaOptional;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.EndToEnd;

/// <summary>Executes generated optional operations against SDK-shaped partial binding bodies.</summary>
public sealed class OptionalOperationEndToEndTests
{
	private const string Source =
		"""
		using CheatEngine.Client.Lua;
		using CheatEngine.SDK.Annotations.Lua;
		namespace TestPlugin;
		public readonly record struct Projection(int Value);
		public readonly struct SnapshotMapper : ILuaResultMapper<int, Projection>
		{
			public static Projection Map(int source)
			{
				Globals.MapperCalls++;
				if (Globals.MapperException is not null)
				{
					throw Globals.MapperException;
				}

				return new Projection(source);
			}
		}

		public static partial class Globals
		{
			[CheatEngineLuaOperation]
			[LuaGlobal("tryOptional")]
			public static partial bool TryReadOptional(
				global::CheatEngine.SDK.Lua.Marshalling.LuaOptional<bool> input,
				out global::CheatEngine.SDK.Lua.Marshalling.LuaOptional<int> result);

			[CheatEngineLuaOperation]
			[LuaGlobal("optionalPair")]
			public static partial int ReadOptionalPair(
				global::CheatEngine.SDK.Lua.Marshalling.LuaOptional<bool> first,
				global::CheatEngine.SDK.Lua.Marshalling.LuaOptional<int> second);

			[CheatEngineLuaOperation(typeof(SnapshotMapper))]
			[LuaGlobal("mappedOptional")]
			public static partial bool TryReadMappedOptional(
				out global::CheatEngine.SDK.Lua.Marshalling.LuaOptional<int> result);
		}
		""";

	private const string Bindings =
		"""
		namespace TestPlugin;
		public static partial class Globals
		{
			public static global::CheatEngine.SDK.Lua.Marshalling.LuaOptional<bool> CapturedInput;
			public static global::CheatEngine.SDK.Lua.Marshalling.LuaOptional<int> TryResult;
			public static global::CheatEngine.SDK.Lua.Marshalling.LuaOptional<int> MappedResult;
			public static bool TrySucceeds;
			public static int PairCalls;
			public static int MapperCalls;
			public static System.Exception? MapperException;

			public static partial bool TryReadOptional(
				global::CheatEngine.SDK.Lua.Marshalling.LuaOptional<bool> input,
				out global::CheatEngine.SDK.Lua.Marshalling.LuaOptional<int> result)
			{
				CapturedInput = input;
				result = TryResult;
				return TrySucceeds;
			}

			public static partial int ReadOptionalPair(
				global::CheatEngine.SDK.Lua.Marshalling.LuaOptional<bool> first,
				global::CheatEngine.SDK.Lua.Marshalling.LuaOptional<int> second)
			{
				if (first.IsOmitted && !second.IsOmitted)
				{
					throw new System.ArgumentException("Optional Lua arguments cannot omit a slot before a later present slot.");
				}

				PairCalls++;
				return 1;
			}

			public static partial bool TryReadMappedOptional(
				out global::CheatEngine.SDK.Lua.Marshalling.LuaOptional<int> result)
			{
				result = MappedResult;
				return true;
			}
		}
		""";

	private static readonly Lazy<Type> SGlobals = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	public void TryOptionalOperationPreservesEveryResultStateAndConvertsInput(int state)
	{
		Reset();
		LuaOptional<bool> input = state switch
		{
			0 => LuaOptional.Omitted<bool>(),
			1 => LuaOptional.Nil<bool>(),
			_ => LuaOptional.Of(false)
		};
		Set("TrySucceeds", true);
		Set("TryResult", state switch
		{
			0 => SdkOptional.Omitted<int>(),
			1 => SdkOptional.Nil<int>(),
			_ => SdkOptional.Of(0)
		});

		(bool succeeded, LuaOptional<int> result, CheatEngineFailure failure) = ExecuteTry(input);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(input.IsOmitted, CapturedInput().IsOmitted);
		Assert.Equal(input.IsNil, CapturedInput().IsNil);
		Assert.Equal(input.HasValue, CapturedInput().HasValue);
		if (input.HasValue)
		{
			Assert.False(CapturedInput().Value);
		}
		Assert.Equal(state == 0, result.IsOmitted);
		Assert.Equal(state == 1, result.IsNil);
		Assert.Equal(state == 2, result.HasValue);
		if (result.HasValue)
		{
			Assert.Equal(0, result.Value);
		}
	}

	[Fact]
	public void TryOptionalOperationMapsFalseBindingToLuaErrorFailure()
	{
		Reset();
		Set("TrySucceeds", false);
		Set("TryResult", SdkOptional.Of(0));

		(bool succeeded, LuaOptional<int> result, CheatEngineFailure failure) = ExecuteTry(LuaOptional.Of(false));

		Assert.False(succeeded);
		Assert.True(result.IsOmitted);
		Assert.Equal(CheatEngineFailureKind.LuaError, failure.Kind);
		Assert.Equal("Lua.Operation.Globals.TryReadOptional", failure.Operation);
	}

	[Fact]
	public void OptionalArgumentHoleIsClassifiedAsABindingErrorBeforeTheBindingRuns()
	{
		Reset();
		object operation = SGlobals.Value.GetMethod("CreateReadOptionalPairLuaOperation", BindingFlags.Public | BindingFlags.Static)!
			.Invoke(null, [LuaOptional.Omitted<bool>(), LuaOptional.Of(1)])!;
		ILuaOperation<int> typed = Assert.IsType<ILuaOperation<int>>(operation, exactMatch: false);

		bool succeeded = typed.TryExecute(new ActiveContext(), out int result, out CheatEngineFailure failure);

		Assert.False(succeeded);
		Assert.Equal(0, result);
		Assert.Equal(CheatEngineFailureKind.BindingError, failure.Kind);
		Assert.IsType<ArgumentException>(failure.Exception);
		Assert.Equal(0, (int) Get("PairCalls")!);
	}

	[Fact]
	public void OptionalMapperSkipsOmittedAndNilButMapsPresentAndPropagatesItsOwnException()
	{
		Reset();
		Set("MappedResult", SdkOptional.Omitted<int>());
		Assert.True(IsOptional(ExecuteMapped(), "IsOmitted"));
		Set("MappedResult", SdkOptional.Nil<int>());
		Assert.True(IsOptional(ExecuteMapped(), "IsNil"));
		Assert.Equal(0, (int) Get("MapperCalls")!);

		Set("MappedResult", SdkOptional.Of(42));
		object projected = ExecuteMapped();
		Assert.True(IsOptional(projected, "HasValue"));
		Assert.Equal(42, (int) projected.GetType().GetProperty("Value")!.GetValue(projected)!.GetType()
			.GetProperty("Value")!.GetValue(projected.GetType().GetProperty("Value")!.GetValue(projected)!)!);
		Assert.Equal(1, (int) Get("MapperCalls")!);

		InvalidOperationException expected = new("mapper");
		Set("MapperException", expected);
		Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => ExecuteMapped()));
	}

	private static (bool Succeeded, LuaOptional<int> Result, CheatEngineFailure Failure) ExecuteTry(LuaOptional<bool> input)
	{
		object operation = SGlobals.Value.GetMethod("CreateTryReadOptionalLuaOperation", BindingFlags.Public | BindingFlags.Static)!
			.Invoke(null, [input])!;
		ILuaOperation<LuaOptional<int>> typed = Assert.IsType<ILuaOperation<LuaOptional<int>>>(operation, exactMatch: false);
		bool succeeded = typed.TryExecute(new ActiveContext(), out LuaOptional<int> result, out CheatEngineFailure failure);
		return (succeeded, result, failure);
	}

	private static object ExecuteMapped()
	{
		object operation = SGlobals.Value.GetMethod("CreateTryReadMappedOptionalLuaOperation", BindingFlags.Public | BindingFlags.Static)!
			.Invoke(null, null)!;
		MethodInfo execute = operation.GetType().GetMethod(nameof(ILuaOperation<>.TryExecute))!;
		object?[] arguments = [new ActiveContext(), null, null];
		bool succeeded;
		try
		{
			succeeded = (bool) execute.Invoke(operation, arguments)!;
		}
		catch (TargetInvocationException exception) when (exception.InnerException is not null)
		{
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
			throw;
		}

		Assert.True(succeeded);
		Assert.Equal(default(CheatEngineFailure), (CheatEngineFailure) arguments[2]!);
		return arguments[1]!;
	}

	private static CheatEngine.SDK.Lua.Marshalling.LuaOptional<bool> CapturedInput()
	{
		CheatEngine.SDK.Lua.Marshalling.LuaOptional<bool> value =
			(CheatEngine.SDK.Lua.Marshalling.LuaOptional<bool>) Get("CapturedInput")!;
		return value;
	}

	private static bool IsOptional(object value, string property)
	{
		return (bool) value.GetType().GetProperty(property)!.GetValue(value)!;
	}

	private static void Reset()
	{
		Set("CapturedInput", default(CheatEngine.SDK.Lua.Marshalling.LuaOptional<bool>));
		Set("TryResult", default(CheatEngine.SDK.Lua.Marshalling.LuaOptional<int>));
		Set("MappedResult", SdkOptional.Omitted<int>());
		Set("TrySucceeds", false);
		Set("PairCalls", 0);
		Set("MapperCalls", 0);
		Set("MapperException", null);
	}

	private static object? Get(string field)
	{
		return SGlobals.Value.GetField(field, BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
	}

	private static void Set(string field, object? value)
	{
		SGlobals.Value.GetField(field, BindingFlags.Public | BindingFlags.Static)!.SetValue(null, value);
	}

	private static Type Build()
	{
		GeneratorRun run = GeneratorRun.Execute(Source);
		Assert.Empty(run.Diagnostics);
		Compilation compilation = run.OutputCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(Bindings,
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
