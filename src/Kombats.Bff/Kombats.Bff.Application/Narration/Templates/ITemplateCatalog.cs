namespace Kombats.Bff.Application.Narration.Templates;

public interface ITemplateCatalog
{
    IReadOnlyList<NarrationTemplate> GetTemplates(string category);
    IReadOnlyList<string> GetCategories();
}
