namespace QrDemo;

using QrDemo.Services;

/// <summary>Formulário de ajuste das credenciais MySQL da demonstração.</summary>
public sealed class ConfigForm : Form
{
    private readonly TextBox _txtServidor = new() { Width = 200 };
    private readonly TextBox _txtPorta = new() { Width = 70, Text = "3306" };
    private readonly TextBox _txtUsuario = new() { Width = 200, Text = "root" };
    private readonly TextBox _txtSenha = new()
    {
        Width = 200,
        UseSystemPasswordChar = true,
    };
    private readonly Button _btnSalvar = new()
    {
        Text = "Salvar",
        DialogResult = DialogResult.OK,
    };
    private readonly Button _btnCancelar = new()
    {
        Text = "Cancelar",
        DialogResult = DialogResult.Cancel,
    };

    public ConfigForm(DadosConfig config)
    {
        Text = "Configuração MySQL";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9);
        ClientSize = new Size(360, 220);

        _txtServidor.Text = config.Servidor;
        _txtPorta.Text = config.Porta.ToString();
        _txtUsuario.Text = config.Usuario;
        _txtSenha.Text = config.Senha;

        var painel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(12),
        };
        painel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        painel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 6; i++)
        {
            painel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var fluxoPorta = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        fluxoPorta.Controls.Add(_txtPorta);

        void Adicionar(int linha, string rotulo, Control controle)
        {
            painel.Controls.Add(
                new Label
                {
                    Text = rotulo,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                }, 0, linha);
            painel.Controls.Add(controle, 1, linha);
        }

        Adicionar(0, "Servidor:", _txtServidor);
        Adicionar(1, "Porta:", fluxoPorta);
        Adicionar(2, "Usuário:", _txtUsuario);
        Adicionar(3, "Senha:", _txtSenha);

        var fluxoBotoes = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
        };
        fluxoBotoes.Controls.Add(_btnSalvar);
        fluxoBotoes.Controls.Add(_btnCancelar);
        painel.Controls.Add(fluxoBotoes, 0, 4);
        painel.SetColumnSpan(fluxoBotoes, 2);

        Controls.Add(painel);

        AcceptButton = _btnSalvar;
        CancelButton = _btnCancelar;

        _btnSalvar.Click += (_, _) =>
        {
            if (!int.TryParse(_txtPorta.Text.Trim(), out var porta) || porta <= 0)
            {
                MessageBox.Show(this, "Informe uma porta válida.",
                    "Configuração", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            config.Servidor = _txtServidor.Text.Trim();
            config.Porta = porta;
            config.Usuario = _txtUsuario.Text.Trim();
            config.Senha = _txtSenha.Text;
            DialogResult = DialogResult.OK;
        };
    }
}
