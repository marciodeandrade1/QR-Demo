using PdfSharp.Fonts;

/// <summary>
/// Resolvedor de fontes do PdfSharp 6 (.NET): lê os arquivos TTF instalados no
/// Windows (%windir%\Fonts) e dispensa o System.Drawing/WPF. Fontes suportadas
/// pela demonstração: Segoe UI e Consolas; qualquer outra família cai na
/// Segoe UI regular.
/// </summary>
public sealed class ResolvedorFontesWindows : IFontResolver
{
    private const string PastaFontes = "%WINDIR%\\Fonts";

    private static readonly Dictionary<string, string> Arquivos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["segoeui"] = "segoeui.ttf",
        ["segoeui-tt"] = "segoeui.ttf",
        ["segoeuib"] = "segoeuib.ttf",
        ["segoeuii"] = "segoeuii.ttf",
        ["segoeuiz"] = "segoeuiz.ttf",
        ["consola"] = "consola.ttf",
        ["consolab"] = "consolab.ttf",
        ["consolai"] = "consolai.ttf",
        ["consolaz"] = "consolaz.ttf",
    };

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var faceName = MonoFace(familyName, isBold, isItalic);
        if (!Arquivos.ContainsKey(faceName))
        {
            faceName = isBold ? "segoeuib" : "segoeui";
        }
        return new FontResolverInfo(faceName);
    }

    public byte[] GetFont(string faceName)
    {
        if (!Arquivos.TryGetValue(faceName, out var arquivo))
        {
            arquivo = "segoeui.ttf";
        }
        var caminho = Environment.ExpandEnvironmentVariables(
            $@"{PastaFontes}\{arquivo}");
        return File.ReadAllBytes(caminho);
    }

    private static string MonoFace(string familia, bool negrito, bool italico)
    {
        var face = familia.Contains("consolas", StringComparison.OrdinalIgnoreCase)
            ? "consola"
            : "segoeui";
        if (negrito && italico)
        {
            face += "z";
        }
        else if (negrito)
        {
            face += "b";
        }
        else if (italico)
        {
            face += "i";
        }
        return face;
    }
}
