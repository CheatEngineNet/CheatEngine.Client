using System.Runtime.CompilerServices;

using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.SDK.Engine.Values;

using MemoryFluent = CheatEngine.Client.Memory.Memory;

namespace CheatEngine.Client.Tests;

/// <summary>Protects measured, pure fluent paths from accidental managed allocations.</summary>
public sealed class AllocationGateTests
{
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
		return MemoryFluent.At(address);
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
}
