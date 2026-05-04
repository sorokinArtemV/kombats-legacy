using System.Security.Cryptography;
using System.Text;

namespace Kombats.Bff.Application.Narration.Templates;

/// <summary>
/// Selects a template deterministically using hash(battleId, turnIndex, sequence) % count.
/// Same inputs always produce the same template selection.
/// </summary>
public sealed class DeterministicTemplateSelector : ITemplateSelector
{
    public NarrationTemplate Select(IReadOnlyList<NarrationTemplate> templates, Guid battleId, int turnIndex, int sequence)
    {
        if (templates.Count == 0)
            throw new InvalidOperationException("No templates available for selection");

        if (templates.Count == 1)
            return templates[0];

        var input = $"{battleId}:{turnIndex}:{sequence}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var index = Math.Abs(BitConverter.ToInt32(hash, 0)) % templates.Count;
        return templates[index];
    }
}
