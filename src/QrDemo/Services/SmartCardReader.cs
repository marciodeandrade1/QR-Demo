using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace QrDemo.Services;

/// <summary>
/// Acesso ao leitor de cartões ACR122U (Advanced Card Systems Ltd.) pelo serviço
/// PC/SC do Windows (winscard.dll), sem dependências externas. O UID do cartão
/// MIFARE é lido com a APDU "Get Data - UID" (FF CA 00 00 00); a gravação usa as
/// APDUs de autenticação (FF 86) e escrita (FF D6) do MIFARE Classic/Ultralight.
/// </summary>
public sealed class SmartCardReader : IDisposable
{
    public event Action<string>? UidLido;
    public event Action? CartaoRemovido;
    public event Action<string>? StatusAlterado;

    private const int SCARD_S_SUCCESS = 0;
    private const int SCARD_E_NO_READERS = unchecked((int)0x8010002E);
    private const uint SCARD_SCOPE_USER = 0;
    private const uint SCARD_SHARE_SHARED = 2;
    private const uint SCARD_PROTOCOL_T0 = 1;
    private const uint SCARD_PROTOCOL_T1 = 2;
    private const uint SCARD_STATE_PRESENT = 0x20;
    private const uint SCARD_LEAVE_CARD = 0;

    private IntPtr _contexto = IntPtr.Zero;
    private bool _contextoAberto;
    private IntPtr _handleCartao = IntPtr.Zero;
    private string? _leitorAtual;
    private string? _ultimoUid;
    private uint _protocoloAtivo;

    /// <summary>Último UID lido (hexadecimal, maiúsculas) ou null.</summary>
    public string? UltimoUid => _ultimoUid;

    /// <summary>Lista os leitores PC/SC do sistema (ex.: "ACS ACR122 0").</summary>
    public IReadOnlyList<string> ListarLeitores()
    {
        AbrirContexto();
        var leitores = new List<string>();

        uint tamanho = 0;
        var resultado = SCardListReaders(_contexto, string.Empty, null, ref tamanho);
        if (resultado == SCARD_E_NO_READERS)
        {
            return leitores;
        }
        if (resultado != SCARD_S_SUCCESS)
        {
            throw new InvalidOperationException(
                $"Erro ListarLeitors PC/SC 0x{resultado:X8}.");
        }

        var buffer = new byte[(int)tamanho * 2]; // string multi-terminada UTF-16
        resultado = SCardListReaders(_contexto, string.Empty, buffer, ref tamanho);
        if (resultado == SCARD_S_SUCCESS)
        {
            var pino = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var texto = Marshal.PtrToStringUni(pino.AddrOfPinnedObject());
                if (texto is not null)
                {
                    leitores.AddRange(
                        texto.Split('\0', StringSplitOptions.RemoveEmptyEntries));
                }
            }
            finally
            {
                pino.Free();
            }
        }

        return leitores;
    }

    /// <summary>Seleciona o leitor e arma o polling (chame VerificarCartao no timer).</summary>
    public void Conectar(string leitor)
    {
        if (string.IsNullOrWhiteSpace(leitor))
        {
            throw new InvalidOperationException("Nenhum leitor PC/SC selecionado.");
        }
        AbrirContexto();
        _leitorAtual = leitor;
        _handleCartao = IntPtr.Zero;
        _ultimoUid = null;
        StatusAlterado?.Invoke("Aguardando cartão...");
    }

    public void Desconectar()
    {
        DesconectarCartao();
        _leitorAtual = null;
        StatusAlterado?.Invoke("Leitor desconectado.");
    }

    /// <summary>Chamado pelo timer do formulário: detecta cartão, lê UID e saída de cartão.</summary>
    public void VerificarCartao()
    {
        if (!_contextoAberto || _leitorAtual is null)
        {
            return;
        }

        if (_handleCartao == IntPtr.Zero)
        {
            if (!TentarConectarCartaoPorLeitor(_leitorAtual))
            {
                return; // cartão fora do campo (ou leitor ocupado): tenta no próximo tique
            }
        }

        var nomeLeitor = new byte[512];
        var atr = new byte[512];
        uint tamanhoNome = (uint)nomeLeitor.Length;
        uint tamanhoAtr = (uint)atr.Length;
        var resultado = SCardStatus(
            _handleCartao, nomeLeitor, ref tamanhoNome, out var estado,
            out _, atr, ref tamanhoAtr);
        if (resultado != SCARD_S_SUCCESS)
        {
            DesconectarCartao();
            return;
        }

        var presente = (estado & SCARD_STATE_PRESENT) != 0;
        if (!presente)
        {
            if (_ultimoUid is not null)
            {
                _ultimoUid = null;
                DesconectarCartao();
                CartaoRemovido?.Invoke();
                StatusAlterado?.Invoke("Aguardando cartão...");
            }
            return;
        }

        if (_ultimoUid is null)
        {
            var uid = LerUid();
            if (uid is not null)
            {
                _ultimoUid = uid;
                UidLido?.Invoke(uid);
            }
        }
    }

    private bool TentarConectarCartaoPorLeitor(string leitor)
    {
        var protocolos = SCARD_PROTOCOL_T0 | SCARD_PROTOCOL_T1;
        var resultado = SCardConnect(
            _contexto, leitor, SCARD_SHARE_SHARED, protocolos,
            out _handleCartao, out _protocoloAtivo);
        if (resultado != SCARD_S_SUCCESS)
        {
            _handleCartao = IntPtr.Zero;
            return false;
        }
        return true;
    }

    private string? LerUid()
    {
        // APDU "Get Data - UID" do ACR122U
        var resposta = Transmitir(new byte[] { 0xFF, 0xCA, 0x00, 0x00, 0x00 });
        if (resposta.Length < 2)
        {
            return null;
        }
        if (resposta[^2] != 0x90 || resposta[^1] != 0x00)
        {
            return null;
        }
        return Convert.ToHexString(resposta[..^2]);
    }

    // ----- gravação RFID (ACR122U / MIFARE) -----

    /// <summary>
    /// Números dos bytes de memória usados na demonstração: bloco 4 (setor 1,
    /// MIFARE Classic, 16 bytes) ou páginas 4-7 (MIFARE Ultralight/NTAG,
    /// 16 bytes em 4 páginas de 4). Nunca o bloco 0 (UID) nem os blocos de
    /// chaves finais dos setores (7, 11, 15...).
    /// </summary>
    private const byte BlocoPaginaInicial = 4;
    private const int TamanhoGravacao = 16;

    public enum TipoCartaoEstimado
    {
        Classic,
        Ultralight,
        Desconhecido,
    }

    public sealed record ResultadoGravacao(
        TipoCartaoEstimado Tipo,
        string TextoGravado,
        string TextoReleido);

    /// <summary>
    /// Detecta o tipo do cartão presente: autentica o bloco 4 do MIFARE Classic
    /// com a chave-padrão de transporte; sem sucesso, tenta a leitura de 4
    /// páginas do MIFARE Ultralight/NTAG (que não precisa de autenticação).
    /// </summary>
    public TipoCartaoEstimado DetectarTipoCartao()
    {
        GarantirCartaoConectado();

        // Carrega a chave-padrão de transporte (FF FF FF FF FF FF) no slot 00
        Tentar(new byte[] { 0xFF, 0x82, 0x00, 0x00, 0x06,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });

        // Autenticar o bloco 4 (setor 1) com a Chave A do slot 00
        if (Tentar(new byte[] { 0xFF, 0x86, 0x00, 0x00, 0x05,
            0x01, 0x00, BlocoPaginaInicial, 0x60, 0x00 }))
        {
            return TipoCartaoEstimado.Classic;
        }

        // Ultralight/NTAG: leitura de 4 páginas (16 bytes) de uma vez
        if (TentarLeitura(new byte[] { 0xFF, 0xB0, 0x00, BlocoPaginaInicial, 0x10 },
                out var dados) && dados.Length >= TamanhoGravacao)
        {
            return TipoCartaoEstimado.Ultralight;
        }

        return TipoCartaoEstimado.Desconhecido;
    }

    /// <summary>
    /// Grava o texto (ASCII, até 16 bytes) no cartão presente — bloco 4 no
    /// MIFARE Classic ou páginas 4-7 no Ultralight/NTAG — e confere relendo.
    /// </summary>
    public ResultadoGravacao GravarCartao(string texto)
    {
        GarantirCartaoConectado();

        var tipo = DetectarTipoCartao();
        if (tipo == TipoCartaoEstimado.Desconhecido)
        {
            throw new InvalidOperationException(
                "Tipo de cartão não suportado para gravação "
                + "(MIFARE Classic com chave-padrão ou MIFARE Ultralight/NTAG).");
        }

        if (Encoding.ASCII.GetBytes(texto).Length > TamanhoGravacao)
        {
            throw new InvalidOperationException(
                $"O texto do cartão deve ter até {TamanhoGravacao} caracteres.");
        }

        var dados = new byte[TamanhoGravacao];
        Encoding.ASCII.GetBytes(texto).CopyTo(dados, 0);

        if (tipo == TipoCartaoEstimado.Classic)
        {
            // Grava o bloco 4 inteiro (16 bytes) após autenticar o setor 1
            var autenticar = new byte[] { 0xFF, 0x86, 0x00, 0x00, 0x05,
                0x01, 0x00, BlocoPaginaInicial, 0x60, 0x00 };
            TransmitirOk(autenticar);

            var gravar = new byte[] { 0xFF, 0xD6, 0x00, BlocoPaginaInicial, 0x10 }
                .Concat(dados).ToArray();
            TransmitirOk(gravar);
        }
        else
        {
            // Ultralight/NTAG: 4 páginas de 4 bytes, páginas 4 a 7,
            // sem autenticação
            for (var indice = 0; indice < TamanhoGravacao / 4; indice++)
            {
                var gravar = new byte[] { 0xFF, 0xD6, 0x00,
                    (byte)(BlocoPaginaInicial + indice), 0x04 }
                    .Concat(dados[(indice * 4)..((indice + 1) * 4)]).ToArray();
                TransmitirOk(gravar);
            }
        }

        // Conferência: lê de volta e devolve o conteúdo conferido
        var gravadoReleido = LerMemoria(tipo);
        return new ResultadoGravacao(tipo, texto, gravadoReleido);
    }

    /// <summary>
    /// Lê os 16 bytes de memória do cartão presente (bloco 4 / páginas 4-7)
    /// como texto ASCII e informa o tipo estimado do cartão.
    /// </summary>
    public string LerDadosGravados(out TipoCartaoEstimado tipo)
    {
        tipo = DetectarTipoCartao();
        return LerMemoria(tipo);
    }

    /// <summary>Lê os 16 bytes de memória (bloco 4 / páginas 4-7) como texto ASCII.</summary>
    private string LerMemoria(TipoCartaoEstimado tipo)
    {
        GarantirCartaoConectado();
        if (tipo == TipoCartaoEstimado.Desconhecido)
        {
            throw new InvalidOperationException(
                "Tipo de cartão não suportado para leitura "
                + "(MIFARE Classic com chave-padrão ou MIFARE Ultralight/NTAG).");
        }
        var resposta = Transmitir(new byte[] { 0xFF, 0xB0, 0x00,
            BlocoPaginaInicial, 0x10 });
        VerificarResposta(resposta);
        if (resposta.Length - 2 < TamanhoGravacao)
        {
            throw new InvalidOperationException(
                "Resposta insuficiente do cartão na leitura do bloco de memória.");
        }
        return TextoAscii(resposta[..^2]);
    }

    // ----- auxiliares de gravação -----

    private void GarantirCartaoConectado()
    {
        if (_handleCartao == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Nenhum cartão conectado - aproxime o cartão no leitor.");
        }
    }

    /// <summary>Transmite a APDU e devolve true se a resposta trouxer SW 9000.</summary>
    private bool Tentar(byte[] apdu)
    {
        try
        {
            VerificarResposta(Transmitir(apdu));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Transmite e, se SW 9000, devolve os dados iniciais (sem SW).</summary>
    private bool TentarLeitura(byte[] apdu, out byte[] dados)
    {
        dados = Array.Empty<byte>();
        try
        {
            var resposta = Transmitir(apdu);
            if (resposta[^2] == 0x90 && resposta[^1] == 0x00)
            {
                dados = resposta[..^2];
                return true;
            }
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void TransmitirOk(byte[] apdu)
    {
        VerificarResposta(Transmitir(apdu));
    }

    private void VerificarResposta(byte[] resposta)
    {
        if (resposta.Length < 2
            || resposta[^2] != 0x90 || resposta[^1] != 0x00)
        {
            var sw = resposta.Length >= 2
                ? $"0x{resposta[^2]:X2}{resposta[^1]:X2}"
                : "vazia";
            throw new InvalidOperationException(
                $"O cartão recusou o comando (status {sw}).");
        }
    }

    private static string TextoAscii(byte[] bytes)
    {
        var caracteres = Encoding.ASCII
            .GetString(bytes)
            .Where(caractere => !char.IsControl(caractere))
            .ToArray();
        var texto = new string(caracteres);
        var fim = texto.IndexOf('\0');
        return fim >= 0 ? texto[..fim] : texto.TrimEnd();
    }


    private byte[] Transmitir(byte[] apdu)
    {
        var pci = Marshal.AllocHGlobal(8);
        var pciResposta = Marshal.AllocHGlobal(264);
        try
        {
            // SCARD_IO_REQUEST {.dwProtocol, .cbPciLength=8}
            var pciBytes = new byte[8];
            BitConverter.GetBytes(_protocoloAtivo).CopyTo(pciBytes, 0);
            BitConverter.GetBytes(8).CopyTo(pciBytes, 4);
            Marshal.Copy(pciBytes, 0, pci, 8);

            var recebido = new byte[264];
            uint tamanho = (uint)recebido.Length;
            var resultado = SCardTransmit(
                _handleCartao, pci, apdu, (uint)apdu.Length, pciResposta,
                recebido, ref tamanho);
            if (resultado != SCARD_S_SUCCESS)
            {
                throw new InvalidOperationException(
                    $"Erro PC/SC na transmissão APDU (código 0x{resultado:X8}).");
            }
            Array.Resize(ref recebido, (int)tamanho);
            return recebido;
        }
        finally
        {
            Marshal.FreeHGlobal(pci);
            Marshal.FreeHGlobal(pciResposta);
        }
    }

    private void DesconectarCartao()
    {
        if (_handleCartao != IntPtr.Zero)
        {
            SCardDisconnect(_handleCartao, SCARD_LEAVE_CARD);
            _handleCartao = IntPtr.Zero;
        }
    }

    private void AbrirContexto()
    {
        if (_contextoAberto)
        {
            return;
        }
        var resultado = SCardEstablishContext(
            SCARD_SCOPE_USER, IntPtr.Zero, IntPtr.Zero, out _contexto);
        if (resultado != SCARD_S_SUCCESS)
        {
            throw new InvalidOperationException(
                $"Falha ao inicializar o serviço PC/SC (código 0x{resultado:X8}).");
        }
        _contextoAberto = true;
    }

    public void Dispose()
    {
        DesconectarCartao();
        if (_contextoAberto)
        {
            SCardReleaseContext(_contexto);
            _contextoAberto = false;
        }
        GC.SuppressFinalize(this);
    }

    // ----- API do serviço PC/SC (winscard.dll) -----

    [DllImport("winscard.dll", SetLastError = true)]
    private static extern int SCardEstablishContext(
        uint dwScope, IntPtr pvReserved1, IntPtr pvReserved2, out IntPtr phContext);

    [DllImport("winscard.dll", SetLastError = true)]
    private static extern int SCardReleaseContext(IntPtr hContext);

    [DllImport("winscard.dll", SetLastError = true)]
    private static extern int SCardListReaders(
        IntPtr hContext, string? mszGroups, byte[]? mszReaders, ref uint pcchReaders);

    [DllImport("winscard.dll", SetLastError = true)]
    private static extern int SCardConnect(
        IntPtr hContext, string szReader, uint dwShareMode,
        uint dwPreferredProtocols, out IntPtr phCard, out uint pdwActiveProtocol);

    [DllImport("winscard.dll", SetLastError = true)]
    private static extern int SCardDisconnect(IntPtr hCard, uint dwDisposition);

    [DllImport("winscard.dll", SetLastError = true)]
    private static extern int SCardStatus(
        IntPtr hCard, byte[]? mszReaderName, ref uint pcchReaderLen, out uint pdwState,
        out uint pdwProtocol, byte[]? pbAtr, ref uint pcbAtrLen);

    [DllImport("winscard.dll", SetLastError = true)]
    private static extern int SCardTransmit(
        IntPtr hCard, IntPtr pioSendPci, byte[] pbSendBuffer, uint cbSendLength,
        IntPtr pioRecvPci, byte[] pbRecvBuffer, ref uint pcbRecvLength);
}
