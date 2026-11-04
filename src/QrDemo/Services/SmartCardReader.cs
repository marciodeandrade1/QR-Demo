using System.ComponentModel;
using System.Runtime.InteropServices;

namespace QrDemo.Services;

/// <summary>
/// Acesso ao leitor de cartões ACR122U (Advanced Card Systems Ltd.) pelo serviço
/// PC/SC do Windows (winscard.dll), sem dependências externas. O UID do cartão
/// MIFARE é lido com a APDU "Get Data - UID" (FF CA 00 00 00).
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
