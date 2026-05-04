namespace Kombats.Bff.Application.Narration;

public interface INarrationRenderer
{
    string Render(string template, Dictionary<string, string> placeholders);
}
