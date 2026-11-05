using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using QRCoder;

namespace QrDemo.Services;

/// <summary>
/// Gera o arquivo PDF da demonstração: cada código (ex.: BR-1001 a BR-1005)
/// é impresso como um bloco com QR Code (QRCoder) + texto do código, UID lido
/// e data de leitura (PdfSharp).
/// </summary>
public static class QrPdfGenerator
{
    private const double LarguraPagina = 595; // A4 em pontos
    private const double AlturaPagina = 842;
    private const double Margem = 46;
    private const double tamanhoQr = 78;
    private const double AlturaBloco = 112;

    static QrPdfGenerator()
    {
        // PdfSharp 6 no .NET moderno exige um resolvedor de fontes explícito.
        GlobalFontSettings.FontResolver ??= new ResolvedorFontesWindows();
    }

    public static void GerarPdf(IEnumerable<Codigo> codigos, string caminhoArquivo)
    {
        var ordenados = codigos
            .OrderBy(codigo => codigo.Valor, StringComparer.Ordinal)
            .ToList();

        using var documento = new PdfDocument();
        documento.Info.Title = "QR-Demo - Códigos RFID em QR Code";
        documento.Info.Author = "QR-Demo";

        var fonteTitulo = new XFont("Segoe UI", 16, XFontStyleEx.Bold);
        var fonteCodigo = new XFont("Consolas", 13, XFontStyleEx.Bold);
        var fonteCampo = new XFont("Segoe UI", 9);
        var fonteRodape = new XFont("Segoe UI", 8, XFontStyleEx.Italic);

        using var geradorQr = new QRCodeGenerator();
        double y = 0;
        var pagina = NovaPagina(documento, fonteTitulo, fonteRodape, ref y);

        foreach (var codigo in ordenados)
        {
            if (y + AlturaBloco > AlturaPagina - Margem - 20)
            {
                pagina = NovaPagina(documento, fonteTitulo, fonteRodape, ref y);
            }

            var dadosQr = geradorQr.CreateQrCode(codigo.Valor, QRCodeGenerator.ECCLevel.Q);
            var matriz = dadosQr.ModuleMatrix; // List<BitArray>, inclui zona de silêncio
            var totalModulos = matriz.Count;
            var modulo = tamanhoQr / totalModulos;

            // QR vetorial: cada módulo "escuro" é um retângulo preto no PDF
            // (epsilão evita frestas de anti-aliasing entre módulos vizinhos)
            var epsilon = modulo / 20;
            for (var linhaY = 0; linhaY < totalModulos; linhaY++)
            {
                var linha = matriz[linhaY];
                for (var colunaX = 0; colunaX < linha.Count; colunaX++)
                {
                    if (!linha[colunaX])
                    {
                        continue;
                    }
                    pagina.Gfx.DrawRectangle(
                        XBrushes.Black,
                        Margem + colunaX * modulo,
                        y + linhaY * modulo,
                        modulo + epsilon,
                        modulo + epsilon);
                }
            }

            var xTexto = Margem + tamanhoQr + 12;
            pagina.Gfx.DrawString(codigo.Valor, fonteCodigo, XBrushes.Black,
                xTexto, y + 16);
            pagina.Gfx.DrawString(
                $"UID: {codigo.Uid ?? "(não associado)"}", fonteCampo,
                XBrushes.DimGray, xTexto, y + 34);
            pagina.Gfx.DrawString(
                $"Descrição: {codigo.Descricao ?? "-"}", fonteCampo,
                XBrushes.DimGray, xTexto, y + 50);
            pagina.Gfx.DrawString(
                $"Leitura: {codigo.DataLeitura:dd/MM/yyyy HH:mm:ss}", fonteCampo,
                XBrushes.Gray, xTexto, y + 66);
            if (codigo.TipoCartao is not null && codigo.DataGravacao is not null)
            {
                pagina.Gfx.DrawString(
                    $"Gravação RFID: {codigo.TipoCartao} em "
                    + $"{codigo.DataGravacao:dd/MM/yyyy HH:mm:ss}", fonteCampo,
                    XBrushes.Gray, xTexto, y + 82);
            }

            pagina.Gfx.DrawLine(XPens.LightGray,
                Margem, y + AlturaBloco - 18, LarguraPagina - Margem, y + AlturaBloco - 18);

            y += AlturaBloco;
        }

        documento.Save(caminhoArquivo);
    }

    private static PaginaTrabalho NovaPagina(
        PdfDocument documento, XFont fonteTitulo, XFont fonteRodape, ref double y)
    {
        var page = documento.AddPage();
        // A4: MediaBox com os cantos (0,0) inferior-esquerdo e (595,842) superior-direito
        page.MediaBox = new PdfRectangle(
            new XPoint(0, 0), new XPoint(LarguraPagina, AlturaPagina));
        var grafico = XGraphics.FromPdfPage(page);

        grafico.DrawString("QR-Demo - Códigos RFID", fonteTitulo, XBrushes.Black,
            Margem, Margem);
        grafico.DrawString(
            $"Gerado em {DateTime.Now:dd/MM/yyyy HH:mm} - Qtde de códigos conforme itens abaixo.",
            fonteRodape, XBrushes.Gray, Margem, Margem + 16);

        y = Margem + 42;
        return new PaginaTrabalho(page, grafico);
    }

    private sealed record PaginaTrabalho(PdfPage Page, XGraphics Gfx);
}
