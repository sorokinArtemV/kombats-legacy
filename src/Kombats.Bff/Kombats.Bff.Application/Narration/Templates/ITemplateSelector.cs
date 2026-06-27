namespace Kombats.Bff.Application.Narration.Templates;

public interface ITemplateSelector
{
    NarrationTemplate Select(IReadOnlyList<NarrationTemplate> templates, Guid battleId, int turnIndex, int sequence);
}
