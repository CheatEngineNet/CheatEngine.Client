using Microsoft.Extensions.Options;

namespace CheatEngine.Client.Extensions.DependencyInjection;

/// <summary>Provides generated, trimming-safe validation for <see cref="CheatEngineClientOptions" />.</summary>
[OptionsValidator]
public sealed partial class ValidateCheatEngineClientOptions : IValidateOptions<CheatEngineClientOptions>
{
}
