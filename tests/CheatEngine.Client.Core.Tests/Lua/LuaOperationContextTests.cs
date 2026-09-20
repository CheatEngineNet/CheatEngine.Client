using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Tests.Lua;

public sealed class LuaOperationContextTests
{
	[Fact]
	public void ContextIsActiveUntilItIsExplicitlyExpired()
	{
		LuaOperationContext context = new(12, static () => true);

		Assert.Equal(12, context.Epoch);
		Assert.True(context.IsActive);
		context.ThrowIfExpired();

		context.Expire();

		Assert.False(context.IsActive);
		CheatEngineActivationExpiredException exception =
			Assert.Throws<CheatEngineActivationExpiredException>(context.ThrowIfExpired);
		Assert.Equal(CheatEngineFailureKind.ActivationExpired, exception.Failure.Kind);
	}

	[Fact]
	public void ContextRejectsAStaleActivationWithoutNeedingExplicitExpiration()
	{
		bool isCurrent = false;
		LuaOperationContext context = new(12, () => isCurrent);

		Assert.False(context.IsActive);
		Assert.Throws<CheatEngineActivationExpiredException>(context.ThrowIfExpired);

		isCurrent = true;

		Assert.True(context.IsActive);
	}
}
