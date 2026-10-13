using System.Text.Json.Serialization;
using WinClean.Core.Models;

namespace WinClean.Core.Rules;

/// <summary>The top-level shape of rules.json. Internal on purpose: <see cref="Rule"/> is the public surface.</summary>
internal sealed record RulesDocument
{
    [JsonPropertyName("rules")]
    public List<Rule>? Rules { get; init; }
}
