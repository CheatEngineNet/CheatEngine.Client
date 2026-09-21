using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Abstractions.Tests.Lua;

public sealed class LuaContractTests
{
	/// <summary>Rejects a chunk identity that cannot identify an executed Lua source.</summary>
	[Fact]
	public void LuaScriptRejectsAnEmptyChunkName()
	{
		Assert.Throws<ArgumentException>(() => new LuaScript("return true", string.Empty));
	}

	/// <summary>Rejects absent Lua source before a script contract can be constructed.</summary>
	[Fact]
	public void LuaScriptRejectsNullSource()
	{
		Assert.Throws<ArgumentNullException>(() => new LuaScript(null!));
	}

	/// <summary>Retains validated source text and the caller-supplied diagnostic chunk name.</summary>
	[Fact]
	public void LuaScriptPreservesValidatedSourceAndChunkName()
	{
		LuaScript script = new("return 21 * 2", "=calculation");

		Assert.Equal("return 21 * 2", script.Source);
		Assert.Equal("=calculation", script.ChunkName);
	}

	/// <summary>Preserves copied module metadata and rejects duplicate Lua global ownership declarations.</summary>
	[Fact]
	public void LuaModuleDescriptorOwnsAStableIdentityAndUniqueCopiedExports()
	{
		LuaModuleDescriptor descriptor = new(
			"plugin",
			ImmutableArray.Create(new LuaExportDescriptor("plugin_status"), new LuaExportDescriptor("plugin_ping")));

		Assert.Equal("plugin", descriptor.Name);
		Assert.Equal(["plugin_status", "plugin_ping"], descriptor.Exports.Select(static export => export.Name));
		Assert.Throws<ArgumentException>(() => new LuaModuleDescriptor(
			"plugin",
			ImmutableArray.Create(new LuaExportDescriptor("duplicate"), new LuaExportDescriptor("duplicate"))));
	}

	/// <summary>Ensures public Lua contracts remain handle-free at every reflected signature boundary.</summary>
	[Fact]
	public void PublicLuaContractsDoNotExposeRawSdkLuaHandles()
	{
		Type[] contracts =
		[
			typeof(ILuaClient),
			typeof(ILuaModule),
			typeof(ILuaModuleLease),
			typeof(IDescribedLuaModule),
			typeof(ILuaOperation<>),
			typeof(ILuaResultMapper<,>),
			typeof(ILuaExecutionContext),
			typeof(IUnsafeLuaClient),
			typeof(LuaScript),
			typeof(LuaModuleDescriptor),
			typeof(LuaExportDescriptor),
			typeof(CheatEngineLuaModuleAttribute),
			typeof(CheatEngineLuaOperationAttribute)
		];

		IEnumerable<string> signatures = contracts.SelectMany(GetPublicSignatureTypes)
			.Select(static type => type.FullName ?? type.Name);

		Assert.DoesNotContain(signatures, static typeName =>
			typeName.StartsWith("CheatEngine.SDK.Lua.State.LuaState", StringComparison.Ordinal)
			|| typeName.StartsWith("CheatEngine.SDK.Lua.References.LuaRef", StringComparison.Ordinal));
	}

	/// <summary>Infers an operation result through the Lua client interface and forwards its successful result.</summary>
	[Fact]
	public void InterfaceExecutionInfersTheOperationResultTypeAndForwardsSuccess()
	{
		ILuaClient client = new ForwardingLuaClient();
		ConstantIntLuaOperation operation = new(42);

		int executeResult = client.Execute(operation, TestContext.Current.CancellationToken);
		bool tryExecuteSucceeded = client.TryExecute(
			operation,
			out int tryExecuteResult,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.Equal(42, executeResult);
		Assert.True(tryExecuteSucceeded);
		Assert.Equal(42, tryExecuteResult);
		Assert.Equal(default, failure);
		Assert.Equal(2, operation.ExecutionCount);
	}

	/// <summary>Preserves the value-type operation overload for third-party Lua client implementations.</summary>
	[Fact]
	public void DefaultValueOperationOverloadsRemainCompatibleWithExistingImplementations()
	{
		ILuaClient client = new ForwardingLuaClient();
		StructIntLuaOperation operation = new(17);

		bool succeeded = client.TryExecute(
			operation,
			out int result,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(17, result);
		Assert.Equal(default, failure);
		Assert.Equal(17, client.Execute<StructIntLuaOperation, int>(operation, TestContext.Current.CancellationToken));
	}

	/// <summary>Uses static abstract mapper dispatch without reflection or an SDK handle in the result.</summary>
	[Fact]
	public void LuaResultMapperUsesTheDeclaredStaticMapContract()
	{
		Assert.Equal("value:42", TextMapper.Map(new MappingSource(42)));
	}

	private static IEnumerable<Type> GetPublicSignatureTypes(Type type)
	{
		foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance |
		                                                     BindingFlags.Static))
		{
			yield return property.PropertyType;
		}

		foreach (MethodInfo method in
		         type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
		{
			yield return method.ReturnType;
			foreach (ParameterInfo parameter in method.GetParameters())
			{
				yield return parameter.ParameterType;
			}
		}
	}

	private sealed class ForwardingLuaClient : ILuaClient
	{
		private readonly ILuaExecutionContext _context = new ActiveLuaExecutionContext();

		public bool TryRegisterModule(
			ILuaModule luaModule,
			[NotNullWhen(true)] out ILuaModuleLease? lease,
			out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(luaModule);
			lease = null;
			failure = new CheatEngineFailure(
				CheatEngineFailureKind.InvalidState,
				"Lua.RegisterModule",
				"Module registration is outside this forwarding test double.");
			return false;
		}

		public ILuaModuleLease RegisterModule(ILuaModule luaModule, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(luaModule);
			throw new NotSupportedException("Module registration is outside this forwarding test double.");
		}

		public bool TryExecute<TResult>(
			ILuaOperation<TResult> operation,
			[MaybeNullWhen(false)] out TResult result,
			out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(operation);
			return operation.TryExecute(_context, out result, out failure);
		}

		public TResult Execute<TResult>(ILuaOperation<TResult> operation, CancellationToken cancellationToken = default)
		{
			if (TryExecute(operation, out TResult? result, out CheatEngineFailure failure, cancellationToken))
			{
				return result!;
			}

			failure.Throw();
			throw new InvalidOperationException("A failed Lua operation must throw its mapped exception.");
		}
	}

	private sealed class ConstantIntLuaOperation(int result) : ILuaOperation<int>
	{
		public int ExecutionCount
		{
			get;
			private set;
		}

		public bool TryExecute(ILuaExecutionContext context, out int operationResult, out CheatEngineFailure failure)
		{
			ArgumentNullException.ThrowIfNull(context);
			context.ThrowIfExpired();
			ExecutionCount++;
			operationResult = result;
			failure = default;
			return true;
		}
	}

	private readonly struct StructIntLuaOperation(int value) : ILuaOperation<int>
	{
		public bool TryExecute(ILuaExecutionContext context, out int result, out CheatEngineFailure failure)
		{
			ArgumentNullException.ThrowIfNull(context);
			context.ThrowIfExpired();
			result = value;
			failure = default;
			return true;
		}
	}

	private readonly record struct MappingSource(int Value);

	private readonly struct TextMapper : ILuaResultMapper<MappingSource, string>
	{
		public static string Map(MappingSource source)
		{
			return $"value:{source.Value}";
		}
	}

	private sealed class ActiveLuaExecutionContext : ILuaExecutionContext
	{
		public long Epoch => 1;

		public bool IsActive => true;

		public void ThrowIfExpired()
		{
		}
	}
}
