using System.Text.Json;

namespace QrDemo.Services;

/// <summary>Parâmetros de conexão do MySQL, persistidos em config.json.</summary>
public sealed class DadosConfig
{
    public string Servidor { get; set; } = "localhost";
    public int Porta { get; set; } = 3306;
    public string Usuario { get; set; } = "root";
    public string Senha { get; set; } = "";
}

public static class Config
{
    public static string CaminhoArquivo { get; } =
        Path.Combine(AppContext.BaseDirectory, "config.json");

    private static readonly JsonSerializerOptions OpcoesJson = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Carrega o config.json; se não existir, grava o padrão
    /// (localhost:3306, root sem senha) e devolve esse padrão.
    /// </summary>
    public static DadosConfig Carregar()
    {
        if (!File.Exists(CaminhoArquivo))
        {
            var padrao = new DadosConfig();
            try
            {
                Salvar(padrao);
            }
            catch
            {
                // Sem permissão de gravação no binário: segue com o padrão em memória.
            }
            return padrao;
        }

        var json = File.ReadAllText(CaminhoArquivo);
        return JsonSerializer.Deserialize<DadosConfig>(json) ?? new DadosConfig();
    }

    public static void Salvar(DadosConfig config)
    {
        File.WriteAllText(CaminhoArquivo, JsonSerializer.Serialize(config, OpcoesJson));
    }

    public static string CadeiaServidor(DadosConfig config) =>
        $"Server={config.Servidor};Port={config.Porta};" +
        $"User ID={config.Usuario};Password={config.Senha};" +
        "DefaultCommandTimeout=30;AllowUserVariables=True;" +
        "SslMode=Disabled;AllowPublicKeyRetrieval=True";

    public static string CadeiaBanco(DadosConfig config) =>
        $"{CadeiaServidor(config)};Database={CodigoRepository.NomeDatabase}";
}
