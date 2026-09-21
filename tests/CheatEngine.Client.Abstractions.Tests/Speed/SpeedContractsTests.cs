using CheatEngine.Client.Speed;

namespace CheatEngine.Client.Abstractions.Tests.Speed;

public sealed class SpeedContractsTests
{
	[Fact]
	public void MultiplierPreservesAFinitePositiveValue()
	{
		SpeedMultiplier multiplier = new(1.5);

		Assert.Equal(1.5, multiplier.Value);
	}

	[Theory]
	[InlineData(0d)]
	[InlineData(-1d)]
	[InlineData(double.PositiveInfinity)]
	[InlineData(double.NegativeInfinity)]
	[InlineData(double.NaN)]
	public void MultiplierRejectsNonFiniteOrNonPositiveValues(double value)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new SpeedMultiplier(value));
	}
}
