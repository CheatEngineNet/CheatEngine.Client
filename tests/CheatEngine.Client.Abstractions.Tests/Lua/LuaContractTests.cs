using System.Reflection;

using CheatEngine.Client.Lua;

namespace CheatEngine.Client.Tests.Lua;

public sealed class LuaContractTests
{
	[Fact]
	public void LuaScriptRejectsAnEmptyChunkName()
	{
		Assert.Throws<ArgumentException>(() => new LuaScript("return true", string.Empty));
	}

	[Fact]
	public void LuaScriptRejectsNullSource()
	{
		Assert.Throws<ArgumentNullException>(() => new LuaScript(null!));
	}

	[Fact]
	public void LuaScriptPreservesValidatedSourceAndChunkName()
	{
		LuaScript script = new("return 21 * 2", "=calculation");

		Assert.Equal("return 21 * 2", script.Source);
		Assert.Equal("=calculation", script.ChunkName);
	}

	[Fact]
	public void PublicLuaContractsDoNotExposeRawSdkLuaHandles()
	{
		Type[] contracts =
		[
			typeof(ILuaClient),
			typeof(ILuaModule),
			typeof(ILuaModuleLease),
			typeof(ILuaOperation<>),
			typeof(ILuaExecutionContext),
			typeof(IUnsafeLuaClient),
			typeof(LuaScript)
		];

		IEnumerable<string> signatures = contracts.SelectMany(GetPublicSignatureTypes)
			.Select(static type => type.FullName ?? type.Name);

		Assert.DoesNotContain(signatures, static typeName =>
			typeName.StartsWith("CheatEngine.SDK.Lua.State.LuaState", StringComparison.Ordinal)
			|| typeName.StartsWith("CheatEngine.SDK.Lua.References.LuaRef", StringComparison.Ordinal));
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
}
