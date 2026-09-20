using System.Reflection;

using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Hosting;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.Values;

using MemoryFluent = CheatEngine.Client.Memory.Memory;

namespace CheatEngine.Client.Tests;

public sealed class FacadeGraphSmokeTests
{
	[Fact]
	public void MetaPackageReferenceProvidesTheFunctionalClientSurface()
	{
		Address address = 0x401000;
		MemoryAddressBuilder memoryOperation = MemoryFluent.At(address);
		AobPattern pattern = new("48 8B ?? 89");

		Dictionary<Type, string> entryPoints = new()
		{
			[typeof(ICheatEngineClient)] = "CheatEngine.Client",
			[typeof(ICheatEngineDispatcher)] = "CheatEngine.Client.Dispatching",
			[typeof(IProcessClient)] = "CheatEngine.Client.Processes",
			[typeof(IMemoryClient)] = "CheatEngine.Client.Memory",
			[typeof(IPatternScanner)] = "CheatEngine.Client.Scanning",
			[typeof(IValueScanner)] = "CheatEngine.Client.Scanning",
			[typeof(IInspectionClient)] = "CheatEngine.Client.Inspection",
			[typeof(ITableClient)] = "CheatEngine.Client.Tables",
			[typeof(ILuaClient)] = "CheatEngine.Client.Lua",
			[typeof(IUnsafeLuaClient)] = "CheatEngine.Client.Lua",
			[typeof(ICheatEngineRuntime)] = "CheatEngine.Client.Runtime",
			[typeof(MemoryAddressBuilder)] = "CheatEngine.Client.Memory",
			[typeof(AobScanBuilder)] = "CheatEngine.Client.Scanning",
			[typeof(CheatEngineClientBuilder)] = "CheatEngine.Client.Extensions.DependencyInjection",
			[typeof(CheatEngineClientPlugin)] = "CheatEngine.Client.Hosting"
		};

		Assert.Equal(address, memoryOperation.Address);
		Assert.Equal("48 8B ?? 89", pattern.Value);
		Assert.All(entryPoints, static entryPoint =>
			Assert.Equal(entryPoint.Value, entryPoint.Key.Namespace));
	}

	[Fact]
	public void PublicClientContractsDoNotExposeSdkOwnershipOrLuaHandles()
	{
		Type[] clientContracts =
		[
			typeof(ICheatEngineClient),
			typeof(ICheatEngineDispatcher),
			typeof(IProcessClient),
			typeof(IMemoryClient),
			typeof(IPatternScanner),
			typeof(IValueScanner),
			typeof(IValueScanSession),
			typeof(IInspectionClient),
			typeof(ITableClient),
			typeof(ILuaClient),
			typeof(IUnsafeLuaClient),
			typeof(ICheatEngineRuntime),
			typeof(IMemoryCodec<>),
			typeof(IMemoryReadContext),
			typeof(IMemoryWriteContext),
			typeof(MemoryAddressBuilder),
			typeof(CheatEngineMemoryFluentExtensions),
			typeof(MemoryFluent),
			typeof(AobScanBuilder),
			typeof(AobFirstMatchBuilder),
			typeof(AobSingleMatchBuilder),
			typeof(AobManyMatchBuilder),
			typeof(CheatEngineAobFluentExtensions),
			typeof(CheatEngineClientBuilder),
			typeof(CheatEngineClientServiceCollectionExtensions),
			typeof(CheatEngineClientPlugin)
		];

		IEnumerable<Type> signatures = clientContracts.SelectMany(GetPublicSignatureTypes);

		Assert.DoesNotContain(signatures, IsForbiddenSdkHandle);
	}

	private static IEnumerable<Type> GetPublicSignatureTypes(Type contract)
	{
		foreach (ConstructorInfo constructor in contract.GetConstructors())
		{
			foreach (ParameterInfo parameter in constructor.GetParameters())
			{
				yield return parameter.ParameterType;
			}
		}

		foreach (PropertyInfo property in contract.GetProperties(BindingFlags.Public | BindingFlags.Instance |
		                                                         BindingFlags.Static))
		{
			yield return property.PropertyType;
		}

		foreach (MethodInfo method in contract.GetMethods(BindingFlags.Public | BindingFlags.Instance |
		                                                  BindingFlags.Static))
		{
			yield return method.ReturnType;
			foreach (ParameterInfo parameter in method.GetParameters())
			{
				yield return parameter.ParameterType;
			}
		}
	}

	private static bool IsForbiddenSdkHandle(Type type)
	{
		while (type.HasElementType)
		{
			type = type.GetElementType()!;
		}

		if (type.IsGenericType && type.GetGenericArguments().Any(IsForbiddenSdkHandle))
		{
			return true;
		}

		return type.Name is "LuaState" or "LuaRef" or "CEObject" or "MemScan" or "FoundList"
		       || type.Name.StartsWith("Owned`", StringComparison.Ordinal);
	}
}
