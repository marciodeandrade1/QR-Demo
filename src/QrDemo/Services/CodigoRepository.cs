using MySqlConnector;

namespace QrDemo.Services;

/// <summary>Registro de código cadastrado/lido no banco DbCodigos.</summary>
public sealed record Codigo(
    int Id,
    string Valor,
    string? Uid,
    string? Descricao,
    DateTime DataLeitura);

/// <summary>
/// Operações MySQL no banco DbCodigos (tabela codigos) para a demonstração.
/// Cria o banco (utf8mb4) e a tabela automaticamente no primeiro acesso.
/// </summary>
public sealed class CodigoRepository
{
    public const string NomeDatabase = "DbCodigos";
    public const string NomeTabela = "codigos";

    private readonly string _cadeiaServidor; // sem Database=
    private readonly string _cadeiaBanco;    // com Database=DbCodigos

    public CodigoRepository(string cadeiaServidor, string cadeiaBanco)
    {
        _cadeiaServidor = cadeiaServidor;
        _cadeiaBanco = cadeiaBanco;
    }

    public static CodigoRepository Criar(DadosConfig config) =>
        new(Config.CadeiaServidor(config), Config.CadeiaBanco(config));

    /// <summary>Cria o banco DbCodigos e a tabela codigos, se ainda não existirem.</summary>
    public async Task GarantirDatabaseAsync(CancellationToken cancelamento = default)
    {
        await using var conexao = new MySqlConnection(_cadeiaServidor);
        await conexao.OpenAsync(cancelamento);

        const string sqlBanco =
            """
            CREATE DATABASE IF NOT EXISTS `DbCodigos`
                DEFAULT CHARACTER SET utf8mb4
                DEFAULT COLLATE utf8mb4_unicode_ci
            """;
        await using (var cmdBanco = conexao.CreateCommand())
        {
            cmdBanco.CommandText = sqlBanco;
            await cmdBanco.ExecuteNonQueryAsync(cancelamento);
        }

        const string sqlTabela =
            """
            CREATE TABLE IF NOT EXISTS `DbCodigos`.`codigos` (
                `id`            INT UNSIGNED NOT NULL AUTO_INCREMENT,
                `codigo`        VARCHAR(50)  NOT NULL COMMENT 'Código exibido/impresso (ex.: BR-1001)',
                `uid`           VARCHAR(32)  NULL     COMMENT 'UID do cartão RFID (ACR122U), hexadecimal',
                `descricao`     VARCHAR(200) NULL     COMMENT 'Descrição livre da associação',
                `data_leitura`  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (`id`),
                UNIQUE KEY `uq_codigos_codigo` (`codigo`),
                KEY `ix_codigos_uid` (`uid`)
            ) ENGINE = InnoDB
              DEFAULT CHARSET = utf8mb4
              COLLATE = utf8mb4_unicode_ci
            """;
        await using (var cmdTabela = conexao.CreateCommand())
        {
            cmdTabela.CommandText = sqlTabela;
            await cmdTabela.ExecuteNonQueryAsync(cancelamento);
        }
    }

    /// <summary>
    /// Insere o código; se já existir, atualiza somente os campos fornecidos
    /// (UID e/ou descrição). Devolve true quando o código é novo.
    /// </summary>
    public async Task<bool> SalvarAsync(
        string codigo, string? uid = null, string? descricao = null,
        CancellationToken cancelamento = default)
    {
        await using var conexao = new MySqlConnection(_cadeiaBanco);
        await conexao.OpenAsync(cancelamento);

        const string sql =
            """
            INSERT INTO `codigos` (`codigo`, `uid`, `descricao`)
            VALUES (@codigo, @uid, @descricao)
            ON DUPLICATE KEY UPDATE
                `uid`          = COALESCE(NULLIF(@uid, ''), `uid`),
                `descricao`    = COALESCE(NULLIF(@descricao, ''), `descricao`),
                `data_leitura` = IF(@uid = '', `data_leitura`, CURRENT_TIMESTAMP)
            """;

        await using var cmd = conexao.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@codigo", codigo);
        cmd.Parameters.AddWithValue("@uid", uid ?? "");
        cmd.Parameters.AddWithValue("@descricao", descricao ?? "");

        var linhas = await cmd.ExecuteNonQueryAsync(cancelamento);
        return linhas == 1; // 1 = inserção; 2 = atualização; 0 = nada mudou
    }

    /// <summary>Lista todos os códigos em ordem alfabética.</summary>
    public async Task<List<Codigo>> ListarAsync(CancellationToken cancelamento = default)
    {
        var lista = new List<Codigo>();

        await using var conexao = new MySqlConnection(_cadeiaBanco);
        await conexao.OpenAsync(cancelamento);

        const string sql =
            """
            SELECT `id`, `codigo`, `uid`, `descricao`, `data_leitura`
            FROM `codigos`
            ORDER BY `codigo`
            """;
        await using var cmd = conexao.CreateCommand();
        cmd.CommandText = sql;
        await using var leitor = await cmd.ExecuteReaderAsync(cancelamento);

        var ordemId = leitor.GetOrdinal("id");
        var ordemCodigo = leitor.GetOrdinal("codigo");
        var ordemUid = leitor.GetOrdinal("uid");
        var ordemDescricao = leitor.GetOrdinal("descricao");
        var ordemData = leitor.GetOrdinal("data_leitura");
        while (await leitor.ReadAsync(cancelamento))
        {
            lista.Add(new Codigo(
                Id: leitor.GetInt32(ordemId),
                Valor: leitor.GetString(ordemCodigo),
                Uid: leitor.IsDBNull(ordemUid) ? null : leitor.GetString(ordemUid),
                Descricao: leitor.IsDBNull(ordemDescricao) ? null : leitor.GetString(ordemDescricao),
                DataLeitura: leitor.GetDateTime(ordemData)));
        }

        return lista;
    }

    /// <summary>Exclui um código pelo Id. Devolve true quando algum registro foi removido.</summary>
    public async Task<bool> ExcluirAsync(int id, CancellationToken cancelamento = default)
    {
        await using var conexao = new MySqlConnection(_cadeiaBanco);
        await conexao.OpenAsync(cancelamento);

        await using var cmd = conexao.CreateCommand();
        cmd.CommandText = "DELETE FROM `codigos` WHERE `id` = @id";
        cmd.Parameters.AddWithValue("@id", id);

        return await cmd.ExecuteNonQueryAsync(cancelamento) > 0;
    }
}
