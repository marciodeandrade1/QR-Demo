using System.Diagnostics;
using System.Text.RegularExpressions;

namespace QrDemo;

using QrDemo.Services;

/// <summary>
/// Formulário principal: leitor ACR122U (PC/SC), cadastro no MySQL (DbCodigos)
/// e geração do PDF com os códigos impressos em QR Code.
/// </summary>
public sealed class MainForm : Form
{
    private readonly SmartCardReader _leitor = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 400 };
    private CodigoRepository? _repositorio;

    private readonly ComboBox _comboLeitores = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 300,
    };

    /// <summary>Nome exibido no combo (traduzido) → nome bruto do PC/SC.</summary>
    private readonly Dictionary<string, string> _leitorPorExibicao = new();
    private readonly Button _btnRecarregarLeitores = new() { Text = "Recarregar" };
    private readonly Button _btnConectar = new() { Text = "Conectar", Enabled = false };
    private readonly Label _lblStatusLeitor = new()
    {
        Text = "Leitura RFID não ativa.",
        ForeColor = Color.DimGray,
        AutoSize = true,
    };
    private readonly Label _lblUidRotulo = new() { Text = "Último UID:", AutoSize = true };
    private readonly Label _lblUid = new()
    {
        Text = "-",
        Font = new Font("Consolas", 10, FontStyle.Bold),
        ForeColor = Color.DarkBlue,
        AutoSize = true,
    };
    private readonly TextBox _txtCodigo = new()
    {
        PlaceholderText = "ex.: BR-1001",
        Width = 130,
    };
    private readonly TextBox _txtDescricao = new()
    {
        PlaceholderText = "descrição da associação",
        Width = 260,
    };
    private readonly CheckBox _chkAssociarUid = new()
    {
        Text = "Associar último UID lido",
        AutoSize = true,
        Checked = true,
    };
    private readonly Button _btnCadastrar = new() { Text = "Cadastrar" };
    private readonly Button _btnAtualizar = new() { Text = "Atualizar lista" };
    private readonly Button _btnExcluir = new() { Text = "Excluir selecionado" };
    private readonly Button _btnVincularUid = new() { Text = "Vincular UID" };
    private readonly TextBox _txtPrefixo = new() { Text = "BR-", Width = 70, MaxLength = 20 };
    private readonly NumericUpDown _numInicio = new()
    {
        Minimum = 0,
        Maximum = 999999,
        Value = 1001,
        Width = 90,
    };
    private readonly NumericUpDown _numFim = new()
    {
        Minimum = 0,
        Maximum = 999999,
        Value = 1005,
        Width = 90,
    };
    private readonly Button _btnGerarLote = new()
    {
        Text = "Gerar códigos e PDF (QR Code)",
    };
    private readonly Button _btnConfig = new() { Text = "Config..." };
    private readonly Label _lblBanco = new()
    {
        Text = "Banco DbCodigos: não conectado.",
        ForeColor = Color.DimGray,
        AutoSize = true,
    };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        ReadOnly = true,
        RowHeadersVisible = false,
        AutoGenerateColumns = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        Font = new Font("Consolas", 9),
    };

    public MainForm()
    {
        Text = "QrDemo - Leitura RFID ACR122U + MySQL (DbCodigos) + PDF/QR Code";
        Font = new Font("Segoe UI", 9);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1080, 680);
        MinimumSize = new Size(820, 540);

        ConstruirInterface();

        _leitor.UidLido += AoLerUid;
        _leitor.CartaoRemovido += AoRemoverCartao;
        _leitor.StatusAlterado += mensagem => _lblStatusLeitor.Text = mensagem;

        _btnRecarregarLeitores.Click += AoClicarRecarregarLeitores;
        _btnConectar.Click += AoClicarConectar;
        _btnCadastrar.Click += AoClicarCadastrar;
        _btnAtualizar.Click += AoClicarAtualizar;
        _btnExcluir.Click += AoClicarExcluir;
        _btnVincularUid.Click += AoClicarVincularUid;
        _btnGerarLote.Click += AoClicarGerarLote;
        _btnConfig.Click += AoClicarConfig;

        _timer.Tick += (_, _) => Seguro(_leitor.VerificarCartao);

        Load += AoCarregarFormulario;
        FormClosed += (_, _) =>
        {
            _timer.Stop();
            _timer.Dispose();
            _leitor.Dispose();
        };
    }

    // ---------- interface ----------

    private void ConstruirInterface()
    {
        SuspendLayout();

        var raiz = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10),
        };
        raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        raiz.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(raiz);

        // -- grupo: leitor RFID
        var grupoLeitor = new GroupBox
        {
            Text = "Leitor RFID - ACR122U (PC/SC)",
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        var fluxoLeitor = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(6),
            WrapContents = false,
        };
        fluxoLeitor.Controls.AddRange(new Control[]
        {
            new Label { Text = "Leitor:", AutoSize = true },
            _comboLeitores,
            _btnRecarregarLeitores,
            _btnConectar,
            _lblStatusLeitor,
            new Label { Text = "  |  ", AutoSize = true },
            _lblUidRotulo,
            _lblUid,
        });
        grupoLeitor.Controls.Add(fluxoLeitor);
        raiz.Controls.Add(grupoLeitor, 0, 0);

        // -- grupo: cadastro + lote
        var grupoCadastro = new GroupBox
        {
            Text = "Cadastro e lote de códigos (MySQL DbCodigos)",
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        var fluxoCadastro = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(6),
        };
        for (var i = 0; i < 3; i++)
        {
            fluxoCadastro.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var linha1 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        linha1.Controls.AddRange(new Control[]
        {
            new Label { Text = "Código:", AutoSize = true },
            _txtCodigo,
            new Label { Text = "Descrição:", AutoSize = true },
            _txtDescricao,
            _chkAssociarUid,
            _btnCadastrar,
        });

        var linha2 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        linha2.Controls.AddRange(new Control[]
        {
            new Label { Text = "Lote - Prefixo:", AutoSize = true },
            _txtPrefixo,
            new Label { Text = "De:", AutoSize = true },
            _numInicio,
            new Label { Text = "Até:", AutoSize = true },
            _numFim,
            _btnGerarLote,
        });

        var linha3 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        linha3.Controls.AddRange(new Control[]
        {
            _btnAtualizar,
            _btnExcluir,
            _btnVincularUid,
            new Label { Text = "  |  ", AutoSize = true },
            _btnConfig,
            _lblBanco,
        });

        fluxoCadastro.Controls.Add(linha1, 0, 0);
        fluxoCadastro.Controls.Add(linha2, 0, 1);
        fluxoCadastro.Controls.Add(linha3, 0, 2);
        grupoCadastro.Controls.Add(fluxoCadastro);
        raiz.Controls.Add(grupoCadastro, 0, 1);

        // -- grupo: lista
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "id",
            DataPropertyName = nameof(Codigo.Id),
            HeaderText = "Id",
            Visible = false,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "valor",
            DataPropertyName = nameof(Codigo.Valor),
            HeaderText = "Código",
            FillWeight = 15,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "uid",
            DataPropertyName = nameof(Codigo.Uid),
            HeaderText = "UID do cartão",
            FillWeight = 25,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "descricao",
            DataPropertyName = nameof(Codigo.Descricao),
            HeaderText = "Descrição",
            FillWeight = 40,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "dataLeitura",
            DataPropertyName = nameof(Codigo.DataLeitura),
            HeaderText = "Data da leitura",
            FillWeight = 20,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "g" },
        });
        raiz.Controls.Add(_grid, 0, 2);

        ResumeLayout();
    }

    // ---------- acontecimentos ----------

    private async void AoCarregarFormulario(object? sender, EventArgs e)
    {
        RecarregarLeitoresInterface();
        await IniciarBancoAsync();
    }

    private void AoClicarRecarregarLeitores(object? sender, EventArgs e) =>
        RecarregarLeitoresInterface();

    private void RecarregarLeitoresInterface()
    {
        try
        {
            var leitores = _leitor.ListarLeitores();
            _comboLeitores.Items.Clear();
            _leitorPorExibicao.Clear();
            var usados = new HashSet<string>();
            foreach (var nomeLeitor in leitores)
            {
                var exibicao = NomeAmigavel(nomeLeitor);
                // nomes repetidos: acrescenta o nome bruto para diferenciar
                if (!usados.Add(exibicao))
                {
                    exibicao = $"{exibicao} [{nomeLeitor}]";
                }
                _leitorPorExibicao[exibicao] = nomeLeitor;
                _comboLeitores.Items.Add(exibicao);
            }
            if (leitores.Count > 0)
            {
                _comboLeitores.SelectedIndex = 0;
                _btnConectar.Enabled = true;
                _lblStatusLeitor.Text = "Leitor detectado. Clique em Conectar.";
            }
            else
            {
                _btnConectar.Enabled = false;
                _lblStatusLeitor.Text =
                    "Nenhum leitor PC/SC detectado - verifique o driver do ACR122U.";
            }
        }
        catch (Exception ex)
        {
            _btnConectar.Enabled = false;
            _lblStatusLeitor.Text = "Falha ao listar leitores: " + ex.Message;
        }
    }

    /// <summary>
    /// Traduz o nome técnico do leitor PC/SC (ex.: "ACS ACR122U PICC Interface 0")
    /// para uma apresentação em português no combo e nos avisos
    /// ("Leitor ACR122U - cartão sem contato (PICC) - nº 0").
    /// Nomes desconhecidos são mantidos como estão.
    /// </summary>
    private static string NomeAmigavel(string nomePcsc)
    {
        var correspondencia = Regex.Match(nomePcsc, @"(\d+)\s*$");
        var numero = correspondencia.Success
            ? correspondencia.Groups[1].Value
            : null;
        var nucleo = correspondencia.Success
            ? nomePcsc[..correspondencia.Index].Trim()
            : nomePcsc.Trim();

        var partes = new List<string>();
        if (nucleo.Contains("ACR122U", StringComparison.OrdinalIgnoreCase))
        {
            partes.Add("Leitor ACR122U");
        }
        else if (nucleo.Contains("ACR122", StringComparison.OrdinalIgnoreCase))
        {
            partes.Add("Leitor ACR122");
        }
        else
        {
            return nomePcsc; // hardware não identificado: exibe o bruto
        }

        if (nucleo.Contains("PICC", StringComparison.OrdinalIgnoreCase))
        {
            partes.Add("cartão sem contato (PICC)");
        }
        else if (nucleo.Contains("SAM", StringComparison.OrdinalIgnoreCase))
        {
            partes.Add("módulo de segurança (SAM)");
        }

        if (numero is not null)
        {
            partes.Add($"nº {numero}");
        }
        return string.Join(" - ", partes);
    }

    private void AoClicarConectar(object? sender, EventArgs e)
    {
        var exibicao = _comboLeitores.SelectedItem as string;
        if (string.IsNullOrEmpty(exibicao)
            || !_leitorPorExibicao.TryGetValue(exibicao, out var leitor))
        {
            return;
        }
        try
        {
            _leitor.Conectar(leitor);
            _lblStatusLeitor.Text = $"Aguardando cartão em: {exibicao}";
            _timer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Leitor RFID",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void AoLerUid(string uid)
    {
        _lblUid.Text = uid;
        _lblStatusLeitor.Text = "Cartão lido - UID exibido ao lado.";
    }

    private void AoRemoverCartao()
    {
        _lblUid.Text = "-";
    }

    private async void AoClicarCadastrar(object? sender, EventArgs e)
    {
        var repositorio = RepositorioObrigatorio();
        if (repositorio is null)
        {
            return;
        }
        var codigo = _txtCodigo.Text.Trim().ToUpperInvariant();
        if (codigo.Length == 0)
        {
            MessageBox.Show(this, "Informe o código a cadastrar (ex.: BR-1001).",
                "Cadastro", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var uid = _chkAssociarUid.Checked ? _leitor.UltimoUid : null;
            var descricao = _txtDescricao.Text.Trim();
            var novo = await repositorio.SalvarAsync(codigo, uid, descricao);
            _lblBanco.Text = $"Código '{codigo}' "
                + (novo ? "cadastrado" : "atualizado")
                + (uid is null ? " (sem UID)." : $" com UID {uid}.");
            await AtualizarListaAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Erro ao cadastrar",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void AoClicarAtualizar(object? sender, EventArgs e) =>
        await AtualizarListaComStatusAsync();

    private async void AoClicarExcluir(object? sender, EventArgs e)
    {
        var repositorio = RepositorioObrigatorio();
        if (repositorio is null || _grid.SelectedRows.Count == 0)
        {
            return;
        }
        var codigo =
            _grid.SelectedRows[0].DataBoundItem as Codigo;
        if (codigo is null)
        {
            return;
        }
        var resposta = MessageBox.Show(
            this, $"Excluir o código '{codigo.Valor}' do banco?",
            "Exclusão", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (resposta != DialogResult.Yes)
        {
            return;
        }
        try
        {
            await repositorio.ExcluirAsync(codigo.Id);
            await AtualizarListaComStatusAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Erro ao excluir",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void AoClicarVincularUid(object? sender, EventArgs e)
    {
        var repositorio = RepositorioObrigatorio();
        if (repositorio is null)
        {
            return;
        }
        if (_grid.SelectedRows.Count == 0)
        {
            MessageBox.Show(this,
                "Selecione um código na lista para vincular o último UID lido.",
                "Vincular UID", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var uid = _leitor.UltimoUid;
        if (string.IsNullOrEmpty(uid))
        {
            MessageBox.Show(this,
                "Nenhum cartão lido ainda - aproxime um cartão do leitor.",
                "Vincular UID", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var codigo =
            _grid.SelectedRows[0].DataBoundItem as Codigo;
        if (codigo is null)
        {
            return;
        }
        try
        {
            await repositorio.SalvarAsync(codigo.Valor, uid);
            _lblBanco.Text = $"UID {uid} vinculado ao código '{codigo.Valor}'.";
            await AtualizarListaAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Erro ao vincular UID",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void AoClicarGerarLote(object? sender, EventArgs e)
    {
        var repositorio = RepositorioObrigatorio();
        if (repositorio is null)
        {
            return;
        }
        var prefixo = _txtPrefixo.Text.Trim().ToUpperInvariant();
        var inicio = (int)_numInicio.Value;
        var fim = (int)_numFim.Value;
        if (inicio > fim || fim - inicio + 1 > 200)
        {
            MessageBox.Show(this,
                "Faixa inválida: início deve ser <= fim e o lote limitado a 200 códigos.",
                "Lote de códigos", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            UseWaitCursor = true;
            var novos = 0;
            for (var numero = inicio; numero <= fim; numero++)
            {
                if (await repositorio.SalvarAsync($"{prefixo}{numero}"))
                {
                    novos++;
                }
            }

            await AtualizarListaAsync();
            var listaCompleta = await repositorio.ListarAsync();
            var conjunto = new HashSet<string>();
            for (var numero = inicio; numero <= fim; numero++)
            {
                conjunto.Add($"{prefixo}{numero}");
            }
            var doLote = listaCompleta
                .Where(codigo => conjunto.Contains(codigo.Valor))
                .ToList();

            var caminhoPdf = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "QrDemo");
            Directory.CreateDirectory(caminhoPdf);
            var arquivoPdf = Path.Combine(
                caminhoPdf, $"QrDemo-{prefixo}{inicio}-a-{prefixo}{fim}.pdf");

            System.Threading.Tasks.Task.Run(() =>
                QrPdfGenerator.GerarPdf(doLote, arquivoPdf)).Wait();

            _lblBanco.Text =
                $"Lote {prefixo}{inicio}..{prefixo}{fim}: {novos} novos, "
                + $"{doLote.Count} no PDF: {arquivoPdf}";
            Process.Start(new ProcessStartInfo(arquivoPdf) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Erro ao gerar lote/PDF",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void AoClicarConfig(object? sender, EventArgs e)
    {
        var config = Config.Carregar();
        using var dialogo = new ConfigForm(config);
        if (dialogo.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            Config.Salvar(config);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Configuração",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        _ = IniciarBancoAsync();
        _lblBanco.Text = "Banco DbCodigos: reconectando...";
    }

    // ---------- banco ----------

    private async Task IniciarBancoAsync()
    {
        _lblBanco.Text = "Banco DbCodigos: conectando...";
        try
        {
            var config = Config.Carregar();
            _repositorio = CodigoRepository.Criar(config);
            await _repositorio.GarantirDatabaseAsync();
            _lblBanco.Text =
                $"Banco DbCodigos: pronto ({config.Servidor}:{config.Porta}, {config.Usuario}).";
            await AtualizarListaAsync();
        }
        catch (Exception ex)
        {
            _lblBanco.Text = "Banco DbCodigos: " + ex.Message;
        }
    }

    private async Task AtualizarListaAsync()
    {
        var repositorio = RepositorioObrigatorio(false);
        if (repositorio is null)
        {
            return;
        }
        try
        {
            _grid.DataSource = await repositorio.ListarAsync();
        }
        catch
        {
            // silencioso: o rótulo do banco já reporta o estado
        }
    }

    private async Task AtualizarListaComStatusAsync()
    {
        var repositorio = RepositorioObrigatorio();
        if (repositorio is null)
        {
            return;
        }
        try
        {
            var lista = await repositorio.ListarAsync();
            _grid.DataSource = lista;
            _lblBanco.Text = $"Banco DbCodigos: {lista.Count} código(s) cadastrado(s).";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Banco de dados",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private CodigoRepository? RepositorioObrigatorio(bool informar = true)
    {
        if (_repositorio is null && informar)
        {
            MessageBox.Show(this,
                "O banco DbCodigos ainda não está acessível. Veja o status abaixo da lista"
                + " e ajuste a conexão (botão Config...).",
                "Banco de dados", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return _repositorio;
    }

    // ---------- auxiliares ----------

    private void Seguro(Action acao)
    {
        try
        {
            acao();
        }
        catch
        {
            // um tique com falha não interrompe o polling do leitor
        }
    }
}
