# QR-Demo

Demonstração em **C# (Windows Forms)** para leitura de cartões RFID com o leitor
**ACR122U** (Advanced Card Systems Ltd.), cadastro de códigos em um banco de
dados MySQL (**DbCodigos**) e geração de um **PDF** com as leituras impressas em
**QR Code** — por exemplo, os códigos `BR-1001` a `BR-1005`.

## Funcionalidades

- Detecção do leitor PC/SC conectado — usa a `winscard.dll` nativa do Windows,
  sem dependências externas para o hardware.
- Leitura contínua (polling) do UID do cartão MIFARE via APDU `FF CA 00 00 00`.
- Cadastro de códigos no banco `DbCodigos`, associando código, UID, descrição e
  data da leitura.
- **Gravação RFID**: escreve o código selecionado na memória do cartão
  (bloco 4 em MIFARE Classic; páginas 4-7 em MIFARE Ultralight/NTAG) e confere
  relendo; o tipo do cartão e a data da gravação são registrados no banco.
- Lote de demonstração: gera e cadastra a faixa informada (ex.: `BR-1001` a
  `BR-1005`) e imprime cada código como **QR Code** em um arquivo PDF.
- O banco `DbCodigos` e a tabela `codigos` são criados automaticamente no
  primeiro acesso.

## Pré-requisitos

- Windows com o SDK do .NET (`dotnet`) instalado.
- Leitor **ACR122U** da Advanced Card Systems Ltd. com o driver PC/SC instalado.
- Servidor MySQL acessível (padrão: `localhost:3306`, usuário `root`).

## Como executar

```powershell
dotnet run --project src\QrDemo
```

Sem o leitor/banco, é possível validar a impressão dos QR Codes com:

```powershell
dotnet run --project src\QrDemo -- --teste-pdf
```

O arquivo gerado fica em `%USERPROFILE%\Documents\QrDemo\QrDemo-teste-pdf.pdf`,
com os códigos de exemplo `BR-1001` a `BR-1005`.

1. Na área **Leitor RFID**, selecione o leitor detectado e clique em
   **Conectar**.
2. Aproxime o cartão — o UID aparece automaticamente.
3. Em **Cadastro**, informe o código (ex.: `BR-1001`), a descrição e clique em
   **Cadastrar**; o código é gravado no banco `DbCodigos` junto do UID.
4. Selecione uma linha da lista e clique em **Vincular UID** para associar o
   último cartão lido a um código já cadastrado.
5. Em **Gravação RFID**, com o cartão ainda no leitor e uma linha selecionada:
   - **Gravar código no cartão** escreve o texto do código na memória do cartão
     (bloco 4 no MIFARE Classic; páginas 4-7 no Ultralight/NTAG) e confere
     relendo; tipo e data ficam registrados no banco.
   - **Ler gravação do cartão** mostra o texto gravado e seleciona o código
     correspondente na lista.
6. Em **Lote de códigos**, confirme o prefixo (`BR-`) e a faixa (`1001` a
   `1005`) e clique em **Gerar códigos e PDF (QR Code)**:
   os códigos são gravados no banco e o PDF com os QR Codes é aberto
   (salvo em `%USERPROFILE%\Documents\QrDemo\`).

## Configuração do banco

A connectionString é montada a partir do arquivo `config.json` criado ao lado do
executável:

```json
{
  "Servidor": "localhost",
  "Porta": 3306,
  "Usuario": "root",
  "Senha": ""
}
```

No primeiro uso o aplicativo recalcula e grava esse arquivo com os valores
padrão acima. Em ambiente de demonstração os padrões locais do README valem
(`localhost`, `root`, sem senha).

## Estrutura

| Caminho                                | Conteúdo                                        |
| -------------------------------------- | ----------------------------------------------- |
| `src/QrDemo/MainForm.cs`               | Interface principal (WinForms)                  |
| `src/QrDemo/Services/SmartCardReader.cs` | Acesso PC/SC nativo: leitura de UID e gravação (Classic/Ultralight) |
| `src/QrDemo/Services/CodigoRepository.cs` | Operações MySQL (DbCodigos)                  |
| `src/QrDemo/Services/QrPdfGenerator.cs`  | PDF com QR Codes vetoriais (QRCoder + PdfSharp) |
| `src/QrDemo/Services/Config.cs`          | Leitura/gravação do `config.json`              |
| `Script/`                              | SQL de criação do banco, tabela e carga demo    |

## Observações

- Todos os arquivos de código, script e documentação deste repositório são
  **UTF-8** (sem BOM) — inclusive os textos de interface em português. Ao
  editar via script, não use `Get-Content`/`Set-Content` do Windows PowerShell
  5.1 (que regravam UTF-8 como CP1252); prefira as ferramentas de edição ou o
  acesso explícito pelo .NET.
- `config.json` contém as credenciais localmente e não deve ser versionado.
