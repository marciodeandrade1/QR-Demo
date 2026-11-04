namespace QrDemo;

/// <summary>Ponto de entrada da aplicação QrDemo.</summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--teste-pdf"))
        {
            ExecutarTestePdf();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    /// <summary>
    /// Gera o PDF de demonstração com os códigos BR-1001 a BR-1005 sem precisar
    /// do banco de dados (útil para validar a impressão dos QR Codes).
    /// Uso: QrDemo.exe --teste-pdf
    /// </summary>
    private static void ExecutarTestePdf()
    {
        const string padrao = "BR-";
        var agora = DateTime.Now;
        var amostra = Enumerable
            .Range(1001, 5)
            .Select(numero => new QrDemo.Services.Codigo(
                Id: numero - 1000,
                Valor: $"{padrao}{numero}",
                Uid: null,
                Descricao: "Demonstração QR-Demo",
                DataLeitura: agora))
            .ToList();

        var pasta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "QrDemo");
        Directory.CreateDirectory(pasta);
        var arquivo = Path.Combine(pasta, "QrDemo-teste-pdf.pdf");

        QrDemo.Services.QrPdfGenerator.GerarPdf(amostra, arquivo);
        Console.WriteLine(arquivo);
    }
}
