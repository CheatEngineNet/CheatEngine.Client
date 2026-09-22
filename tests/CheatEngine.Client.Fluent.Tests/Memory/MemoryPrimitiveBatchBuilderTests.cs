using CheatEngine.Client.Memory;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Fluent.Tests.Memory;

public sealed class MemoryPrimitiveBatchBuilderTests
{
	[Fact]
	public void DefaultBatchBuilderRejectsEveryTerminalBeforeBuildingOrDispatchingARequest()
	{
		MemoryPrimitiveBatchBuilder<int> builder = default;
		Address[] addresses = [];
		MemoryAddressValue<int>[] values = [];

		InvalidOperationException readException = Assert.Throws<InvalidOperationException>(() =>
			builder.Read(addresses, TestContext.Current.CancellationToken));
		InvalidOperationException tryReadException = Assert.Throws<InvalidOperationException>(() =>
		{
			_ = builder.TryRead(addresses, out _, out _, TestContext.Current.CancellationToken);
		});
		InvalidOperationException writeException = Assert.Throws<InvalidOperationException>(() =>
			builder.Write(values, TestContext.Current.CancellationToken));
		InvalidOperationException tryWriteException = Assert.Throws<InvalidOperationException>(() =>
		{
			_ = builder.TryWrite(values, out _, TestContext.Current.CancellationToken);
		});

		foreach (InvalidOperationException exception in new[]
				 {
					 readException, tryReadException, writeException, tryWriteException
				 })
		{
			Assert.Contains("Memory.Batch<T>(memory)", exception.Message);
			Assert.Contains("memory.Batch<T>()", exception.Message);
		}
	}
}
