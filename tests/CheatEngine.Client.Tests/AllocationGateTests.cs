using System.Reflection;
using System.Runtime.CompilerServices;

using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Tests;

/// <summary>Protects measured, pure fluent paths from accidental managed allocations.</summary>
public sealed class AllocationGateTests
{
	private static readonly IMemoryClient UnusedMemory = DispatchProxy.Create<IMemoryClient, UnusedMemoryProxy>();

	[Fact]
	public void PureMemoryBuilderConstructionDoesNotAllocateAfterWarmup()
	{
		Address address = 0x401000UL;
		_ = CreateBuilder(address);

		long before = GC.GetAllocatedBytesForCurrentThread();
		MemoryAddressBuilder builder = default;
		for (int index = 0; index < 1_024; index++)
		{
			builder = CreateBuilder(address);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(address, builder.Address);
		Assert.Equal(0, allocated);
	}

	[Fact]
	public void ScalarLuaResultMapperDoesNotAllocateAfterWarmup()
	{
		_ = MapScalar<IdentityInt32Mapper>(42);

		long before = GC.GetAllocatedBytesForCurrentThread();
		int result = 0;
		for (int index = 0; index < 1_024; index++)
		{
			result = MapScalar<IdentityInt32Mapper>(index);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(1_023, result);
		Assert.Equal(0, allocated);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static MemoryAddressBuilder CreateBuilder(Address address)
	{
		return UnusedMemory.At(address);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int MapScalar<TMapper>(int source)
		where TMapper : ILuaResultMapper<int, int>
	{
		return TMapper.Map(source);
	}

	private readonly struct IdentityInt32Mapper : ILuaResultMapper<int, int>
	{
		public static int Map(int source)
		{
			return source;
		}
	}

	/// <summary>The memory service a measured builder is bound to; building never calls it.</summary>
	public class UnusedMemoryProxy : DispatchProxy
	{
		protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
		{
			throw new NotSupportedException("Building a Fluent memory operation must not call the memory service.");
		}
	}
}
