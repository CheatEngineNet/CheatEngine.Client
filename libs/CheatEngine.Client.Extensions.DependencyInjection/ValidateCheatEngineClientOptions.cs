using Microsoft.Extensions.Options;

namespace CheatEngine.Client.Extensions.DependencyInjection;

/// <summary>Provides generated, trimming-safe validation for <see cref="CheatEngineClientOptions" />.</summary>
/// <remarks>A composition detail: <c>AddCheatEngineClient</c> registers it next to the semantic validator.</remarks>
[OptionsValidator]
internal sealed partial class ValidateCheatEngineClientOptions : IValidateOptions<CheatEngineClientOptions>
{
}
