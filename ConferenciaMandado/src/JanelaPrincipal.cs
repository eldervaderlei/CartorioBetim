using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ConferenciaMandado
{
    public class JanelaPrincipal : Form
    {
        readonly Panel rolagem = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Cores.Fundo };
        readonly Pilha raiz = new Pilha { Espaco = 12, BackColor = Cores.Fundo };
        readonly FlowLayoutPanel barra = new FlowLayoutPanel { Dock = DockStyle.Top, WrapContents = false, BackColor = Cores.Fundo };
        Botao bAdicionar, bVoltar, bGrifado, bCopiar, bImprimir, bTexto, bNova;

        // arquivos escolhidos (na ordem), o nome exibido de cada um e as páginas já lidas
        readonly List<string> fila = new List<string>();
        readonly Dictionary<string, string> nomeDe = new Dictionary<string, string>();
        readonly Dictionary<string, List<Pagina>> lidas = new Dictionary<string, List<Pagina>>();
        string tipo = "divorcio";

        // última conferência
        readonly List<Pagina> paginas = new List<Pagina>();
        readonly List<string> arquivos = new List<string>();
        Resultado resultado;
        bool ocupado;
        readonly List<string> avisos = new List<string>();
        enum Tela { Inicio, Fila, Progresso, Resultado }
        Tela tela;

        const string ChaveRegistro = @"Software\RCPN Betim\Conferencia de Mandados";

        public JanelaPrincipal(string[] args)
        {
            Text = "Conferência de Mandados — RCPN Betim/MG";
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.None;
            using (var g = CreateGraphics()) Escala.K = g.DpiX / 96f;
            BackColor = Cores.Fundo; ForeColor = Cores.Tinta;
            Size = new Size(Escala.D(1040), Escala.D(820));
            MinimumSize = new Size(Escala.D(640), Escala.D(480));
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            KeyPreview = true;
            tipo = LerTipoSalvo();

            var cab = new Cabecalho { Dock = DockStyle.Top, Height = Escala.D(84) };
            barra.Height = Escala.D(52);
            barra.Padding = new Padding(Escala.D(24), Escala.D(10), Escala.D(24), 0);
            bAdicionar = new Botao("Adicionar arquivos…", true);
            bVoltar = new Botao("Voltar ao resultado");
            bGrifado = new Botao("Baixar PDF grifado", true);
            bCopiar = new Botao("Copiar relatório");
            bImprimir = new Botao("Imprimir relatório");
            bTexto = new Botao("Ver texto lido");
            bNova = new Botao("Nova conferência");
            barra.Controls.AddRange(new Control[] { bAdicionar, bVoltar, bGrifado, bCopiar, bImprimir, bTexto, bNova });
            bAdicionar.Click += (s, e) => EscolherArquivos();
            bVoltar.Click += (s, e) => MostrarResultado();
            bGrifado.Click += (s, e) => BaixarGrifado();
            bCopiar.Click += (s, e) => { Clipboard.SetText(Relatorio.Texto(resultado, arquivos, paginas)); MessageBox.Show(this, "Relatório copiado. Cole onde quiser (Ctrl+V).", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); };
            bImprimir.Click += (s, e) => Relatorio.Imprimir(this, resultado, arquivos, paginas);
            bTexto.Click += (s, e) => new JanelaTexto(paginas, arquivos.Count > 1).ShowDialog(this);
            bNova.Click += (s, e) => NovaConferencia();

            raiz.Padding = new Padding(Escala.D(24), Escala.D(10), Escala.D(24), Escala.D(30));
            rolagem.Controls.Add(raiz);
            rolagem.Resize += (s, e) => AjustarLargura();
            Controls.Add(rolagem);
            Controls.Add(barra);
            Controls.Add(cab);

            HabilitarSoltar(this);

            KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.O && !ocupado) { EscolherArquivos(); e.Handled = true; } };

            MostrarInicio();
            var iniciais = (args ?? new string[0]).Where(File.Exists).ToArray();
            if (iniciais.Length > 0) Shown += (s, e) => BeginInvoke(new Action(() => Adicionar(iniciais)));
        }

        static string LerTipoSalvo()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(ChaveRegistro)) { var v = k?.GetValue("Tipo") as string; if (Motor.Tipos.Any(t => t.Chave == v)) return v; } } catch { }
            return "divorcio";
        }

        static void SalvarTipo(string t)
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(ChaveRegistro)) k?.SetValue("Tipo", t); } catch { }
        }

        void AjustarLargura()
        {
            int w = rolagem.ClientSize.Width;
            raiz.Width = Math.Min(w, Escala.D(1000));
            raiz.Left = Math.Max(0, (w - raiz.Width) / 2);
            raiz.PerformLayout();
        }

        void AtualizarBotoes()
        {
            bool res = tela == Tela.Resultado;
            bAdicionar.Visible = true; bAdicionar.Enabled = !ocupado;
            bVoltar.Visible = tela == Tela.Fila && resultado != null && MesmaFila() && resultado.Tipo == tipo;
            bGrifado.Visible = bCopiar.Visible = bImprimir.Visible = bTexto.Visible = res;
            bNova.Visible = tela == Tela.Resultado || tela == Tela.Fila;
            foreach (Control b in barra.Controls) if (b != bAdicionar) b.Enabled = !ocupado;
        }

        bool MesmaFila() => arquivos.SequenceEqual(fila.Select(f => nomeDe[f]));

        // ---------- arrastar e soltar ----------
        void HabilitarSoltar(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += AoArrastar;
            c.DragOver += AoArrastar;
            c.DragDrop += AoSoltar;
            c.DragLeave += (s, e) => MarcarZona(false);
            foreach (Control f in c.Controls) HabilitarSoltar(f);
            c.ControlAdded += (s, e) => HabilitarSoltar(e.Control);
        }

        void AoArrastar(object s, DragEventArgs e)
        {
            e.Effect = !ocupado && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            MarcarZona(e.Effect == DragDropEffects.Copy);
        }

        void AoSoltar(object s, DragEventArgs e)
        {
            MarcarZona(false);
            if (ocupado || !(e.Data.GetData(DataFormats.FileDrop) is string[] lista)) return;
            BeginInvoke(new Action(() => Adicionar(lista)));
        }

        ZonaSoltar zona;
        void MarcarZona(bool sobre) { if (zona != null && !zona.IsDisposed && zona.Sobre != sobre) { zona.Sobre = sobre; zona.Invalidate(); } }

        void EscolherArquivos()
        {
            if (ocupado) return;
            using (var d = new OpenFileDialog
            {
                Title = "Adicionar arquivos à conferência",
                Filter = "PDF ou imagem|*.pdf;*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp|PDF (*.pdf)|*.pdf|Imagens|*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp",
                Multiselect = true
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Adicionar(d.FileNames);
            }
        }

        // põe os arquivos na lista; a conferência só começa quando a pessoa clicar em "Conferir agora"
        void Adicionar(string[] lista)
        {
            var validos = lista.Where(f => File.Exists(f) && Leitura.Aceita(f)).ToList();
            var recusados = lista.Where(f => !validos.Contains(f)).Select(Path.GetFileName).ToList();
            foreach (var f in validos)
            {
                if (fila.Any(x => string.Equals(x, f, StringComparison.OrdinalIgnoreCase))) continue;
                fila.Add(f);
                nomeDe[f] = NomeUnico(Path.GetFileName(f));
            }
            if (recusados.Count > 0)
                MessageBox.Show(this, "Só é possível adicionar PDF ou imagem (JPG, PNG, TIFF, BMP). Ficaram de fora:\n\n" + string.Join("\n", recusados), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (fila.Count > 0) MostrarFila();
        }

        string NomeUnico(string n)
        {
            var usados = new HashSet<string>(nomeDe.Values);
            if (!usados.Contains(n)) return n;
            string b = Path.GetFileNameWithoutExtension(n), x = Path.GetExtension(n); int i = 2;
            while (usados.Contains(b + " (" + i + ")" + x)) i++;
            return b + " (" + i + ")" + x;
        }

        void Remover(string caminho)
        {
            fila.Remove(caminho); nomeDe.Remove(caminho); lidas.Remove(caminho);
            if (fila.Count == 0 && resultado == null) MostrarInicio(); else MostrarFila();
        }

        void NovaConferencia()
        {
            fila.Clear(); nomeDe.Clear(); lidas.Clear();
            paginas.Clear(); arquivos.Clear(); resultado = null; avisos.Clear();
            MostrarInicio();
        }

        // ---------- leitura e conferência ----------
        async Task Conferir()
        {
            if (ocupado || fila.Count == 0) return;
            ocupado = true;
            var tp = MostrarProgresso();
            var falhas = new List<string>();
            try
            {
                var lista = fila.ToList();
                for (int i = 0; i < lista.Count; i++)
                {
                    string f = lista[i], nome = nomeDe[f];
                    if (lidas.ContainsKey(f)) continue;
                    tp.Definir("Lendo " + nome + "…", (double)i / lista.Count);
                    try
                    {
                        if (Leitura.EhPdf(f)) lidas[f] = await Task.Run(() => Leitura.LerPdf(f, nome));
                        else lidas[f] = new List<Pagina> { new Pagina { Arquivo = nome, Caminho = f, EhImagem = true, Numero = 1, Total = 1, Escaneada = true } };
                    }
                    catch (Exception ex)
                    {
                        falhas.Add(nome + ": " + Explicar(ex));
                        fila.Remove(f); nomeDe.Remove(f);
                    }
                }

                // páginas escaneadas ainda não lidas: OCR do Windows
                var esc = fila.SelectMany(f => lidas[f]).Where(p => p.Escaneada && !p.OcrTentado).ToList();
                if (esc.Count > 0)
                {
                    int feitas = 0;
                    tp.Titulo("Reconhecendo o texto das páginas escaneadas…");
                    tp.Definir("Página 1 de " + esc.Count, 0);
                    try
                    {
                        string idioma = await LerEscaneadas(esc, () => { feitas++; Reportar(tp, feitas, esc.Count); });
                        if (!idioma.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
                            Avisar("O OCR do Windows está usando o idioma “" + idioma + "”, porque o reconhecimento de texto em português não está instalado. Acentos podem sair errados. Para instalar: Configurações > Hora e idioma > Idioma > Português (Brasil) > Opções > Reconhecimento óptico de caracteres (Baixar).");
                    }
                    catch (Exception ex)
                    {
                        Avisar("Não foi possível fazer o reconhecimento de texto (OCR) das páginas escaneadas: " + Explicar(ex));
                    }
                    foreach (var p in esc) p.OcrTentado = true;
                }

                var pgs = fila.SelectMany(f => lidas[f]).ToList();
                string texto = Leitura.Juntar(pgs);
                if (Leitura.LetrasUteis(texto) < 30)
                {
                    string m = falhas.Count > 0 && fila.Count == 0 ? "Não consegui ler o(s) arquivo(s):\n\n" + string.Join("\n", falhas)
                        : "Não foi possível ler texto nestes documentos. Se for um escaneamento ou foto, tente uma imagem mais nítida, bem iluminada e reta.";
                    if (avisos.Count > 0) m += "\n\n" + string.Join("\n\n", avisos);
                    ocupado = false;
                    if (fila.Count > 0) MostrarFila(); else MostrarInicio();
                    MessageBox.Show(this, m, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                tp.Titulo("Conferindo os itens…");
                tp.Definir("Analisando o texto", 1);
                bool digital = pgs.Any(p => !p.Escaneada);
                string t = tipo;
                var r = await Task.Run(() => Motor.Analisar(t, texto, digital));
                paginas.Clear(); paginas.AddRange(pgs);
                arquivos.Clear(); arquivos.AddRange(fila.Select(f => nomeDe[f]));
                resultado = r;
                ocupado = false;
                MostrarResultado();
                if (falhas.Count > 0)
                    MessageBox.Show(this, "Alguns arquivos não puderam ser lidos e ficaram fora da conferência:\n\n" + string.Join("\n", falhas), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                ocupado = false;
                MostrarFila();
                MessageBox.Show(this, "Não consegui conferir os documentos: " + Explicar(ex), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ocupado = false; AtualizarBotoes();
            }
        }

        // fica num método à parte: se o OCR do Windows não existir, só esta parte falha
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static Task<string> LerEscaneadas(List<Pagina> esc, Action aoTerminarPagina)
        {
            return Task.Run(async () =>
            {
                foreach (var grupo in esc.GroupBy(p => p.Caminho))
                {
                    var lista = grupo.ToArray();
                    if (lista[0].EhImagem)
                        foreach (var p in lista) { await OcrWindows.LerImagem(p); aoTerminarPagina(); }
                    else
                        await OcrWindows.LerPaginasPdf(grupo.Key, lista, p => aoTerminarPagina());
                }
                return OcrWindows.Idioma;
            });
        }

        void Avisar(string m) { if (!avisos.Contains(m)) avisos.Add(m); }

        void Reportar(TelaProgresso tp, int feitas, int total)
        {
            BeginInvoke(new Action(() => tp.Definir(feitas < total ? "Página " + (feitas + 1) + " de " + total : "Concluído", (double)feitas / total)));
        }

        static string Explicar(Exception ex)
        {
            var m = ex is AggregateException ag && ag.InnerException != null ? ag.InnerException.Message : ex.Message;
            if (ex.GetType().Name.Contains("Password") || m.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 || m.IndexOf("encrypt", StringComparison.OrdinalIgnoreCase) >= 0)
                return "o PDF está protegido por senha. Salve uma cópia sem senha e tente de novo.";
            return m;
        }

        // ---------- PDF grifado ----------
        async void BaixarGrifado()
        {
            if (resultado == null || ocupado) return;
            var caminhoDe = fila.Where(f => nomeDe.ContainsKey(f)).ToDictionary(f => nomeDe[f], f => f);
            var alvos = arquivos.Where(caminhoDe.ContainsKey).ToList();
            if (alvos.Count == 0) return;

            var destinos = new Dictionary<string, string>();
            if (alvos.Count == 1)
            {
                string orig = caminhoDe[alvos[0]];
                using (var d = new SaveFileDialog
                {
                    Title = "Salvar PDF grifado",
                    Filter = "PDF (*.pdf)|*.pdf",
                    FileName = Path.GetFileNameWithoutExtension(alvos[0]) + " - grifado.pdf",
                    InitialDirectory = Path.GetDirectoryName(orig),
                    OverwritePrompt = true
                })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    destinos[alvos[0]] = d.FileName;
                }
            }
            else
            {
                using (var d = new FolderBrowserDialog { Description = "Escolha a pasta onde salvar os " + alvos.Count + " PDFs grifados", SelectedPath = Path.GetDirectoryName(caminhoDe[alvos[0]]) })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    foreach (var a in alvos)
                    {
                        string b = Path.Combine(d.SelectedPath, Path.GetFileNameWithoutExtension(a) + " - grifado"), n = b + ".pdf"; int i = 2;
                        while (File.Exists(n) || destinos.ContainsValue(n)) n = b + " (" + i++ + ").pdf";
                        destinos[a] = n;
                    }
                }
            }

            ocupado = true; AtualizarBotoes(); Cursor = Cursors.WaitCursor;
            var erros = new List<string>(); var semGrifo = new List<string>();
            try
            {
                var r = resultado; var pgs = paginas.ToList();
                foreach (var a in alvos)
                {
                    string orig = caminhoDe[a], dest = destinos[a];
                    try
                    {
                        var dq = Grifo.Destaques(r, pgs, a);
                        if (dq.Count == 0) semGrifo.Add(a);
                        await Task.Run(() =>
                        {
                            byte[] pdf = Leitura.EhPdf(orig) ? File.ReadAllBytes(orig) : ImagemComoPdf(orig);
                            File.WriteAllBytes(dest, Grifo.Grifar(pdf, dq));
                        });
                    }
                    catch (Exception ex) { erros.Add(a + ": " + Explicar(ex)); destinos.Remove(a); }
                }
            }
            finally { ocupado = false; AtualizarBotoes(); Cursor = Cursors.Default; }

            string msg = destinos.Count == 0 ? "" : destinos.Count == 1 ? "PDF grifado salvo em:\n" + destinos.Values.First() : destinos.Count + " PDFs grifados salvos em:\n" + Path.GetDirectoryName(destinos.Values.First());
            if (semGrifo.Count > 0) msg += "\n\nNenhum trecho foi encontrado em: " + string.Join(", ", semGrifo) + " (salvo sem grifos).";
            if (erros.Count > 0) msg += "\n\nNão foi possível gerar:\n" + string.Join("\n", erros);
            if (destinos.Count == 0) { MessageBox.Show(this, msg.Trim(), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (MessageBox.Show(this, msg + "\n\nAbrir agora?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                try
                {
                    if (destinos.Count == 1) Process.Start(new ProcessStartInfo(destinos.Values.First()) { UseShellExecute = true });
                    else Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(destinos.Values.First()) + "\"");
                }
                catch (Exception ex) { MessageBox.Show(this, "Não consegui abrir: " + ex.Message, Text); }
            }
        }

        // foto ou escaneamento em imagem vira um PDF de uma página
        static byte[] ImagemComoPdf(string caminho)
        {
            using (var img = Image.FromFile(caminho))
            {
                bool jpeg = img.RawFormat.Equals(ImageFormat.Jpeg);
                byte[] dados;
                if (jpeg) dados = File.ReadAllBytes(caminho);
                else using (var ms = new MemoryStream()) { img.Save(ms, ImageFormat.Png); dados = ms.ToArray(); }
                return Grifo.ImagemParaPdf(dados, !jpeg, img.Width, img.Height);
            }
        }

        // ---------- telas ----------
        void Limpar()
        {
            raiz.SuspendLayout();
            foreach (Control c in raiz.Controls.Cast<Control>().ToList()) c.Dispose();
            raiz.Controls.Clear();
            zona = null;
        }

        void Pronto(Tela t)
        {
            tela = t;
            raiz.ResumeLayout();
            AjustarLargura();
            rolagem.AutoScrollPosition = new Point(0, 0);
            AtualizarBotoes();
        }

        static Label Rotulo(string t, float tam = 10f, FontStyle est = FontStyle.Regular, Color? cor = null, Padding? pad = null)
        {
            return new Label { Text = t, Font = new Font("Segoe UI", tam, est), ForeColor = cor ?? Cores.Tinta, AutoSize = false, BackColor = Color.Transparent, Padding = pad ?? Padding.Empty, UseMnemonic = false };
        }

        void MostrarInicio()
        {
            Limpar();
            zona = new ZonaSoltar { Height = Escala.D(300), Margin = new Padding(0, Escala.D(16), 0, 0) };
            var t1 = Rotulo("Arraste aqui os PDFs do processo", 15f, FontStyle.Bold);
            var t2 = Rotulo("ou clique para escolher. Pode juntar quantos arquivos quiser (sentença, mandado, termo, certidão de trânsito em julgado…) antes de conferir. Aceita PDF com texto (do WhatsApp / PJe) e PDF escaneado ou foto do papel.", 10f, FontStyle.Regular, Cores.Apagado);
            t1.TextAlign = t2.TextAlign = ContentAlignment.TopCenter;
            var bt = new Botao("Adicionar arquivos…", true);
            zona.Controls.AddRange(new Control[] { t1, t2, bt });
            zona.Resize += (s, e) =>
            {
                int w = zona.Width - Escala.D(80), w2 = Math.Min(w, Escala.D(640));
                t1.SetBounds(Escala.D(40), Escala.D(100), w, Escala.D(34));
                int h2 = Pilha.Altura(t2, w2);
                t2.SetBounds((zona.Width - w2) / 2, Escala.D(138), w2, h2);
                bt.Location = new Point((zona.Width - bt.Width) / 2, t2.Bottom + Escala.D(16));
                int alt = bt.Bottom + Escala.D(30);
                if (zona.Height != alt) { zona.Height = alt; Pilha.Relayout(zona); }
            };
            EventHandler abrir = (s, e) => EscolherArquivos();
            zona.Click += abrir; t1.Click += abrir; t2.Click += abrir; bt.Click += abrir;
            raiz.Controls.Add(zona);

            foreach (var tc in Motor.Tipos)
            {
                var l = Rotulo(tc.Nome + ": " + string.Join("  ·  ", tc.Itens), 9.5f, FontStyle.Regular, Cores.Apagado);
                l.TextAlign = ContentAlignment.TopCenter;
                raiz.Controls.Add(l);
            }
            var priv = Rotulo("Tudo é feito neste computador, sem internet. O conteúdo dos documentos não é enviado para lugar nenhum e não fica salvo pelo programa.", 9f, FontStyle.Regular, Cores.Ok);
            priv.TextAlign = ContentAlignment.TopCenter;
            raiz.Controls.Add(priv);
            Pronto(Tela.Inicio);
        }

        Label itensTipo;

        void MostrarFila()
        {
            Limpar();

            // tipo de conferência
            var ct = new Cartao { Margin = new Padding(0, Escala.D(8), 0, 0) };
            ct.Corpo.Controls.Add(Rotulo("1. O que vai ser conferido?", 12f, FontStyle.Bold));
            var linhaTipos = new FlowLayoutPanel { Height = Escala.D(34), BackColor = Color.Transparent, WrapContents = false };
            foreach (var tc in Motor.Tipos)
            {
                var rb = new RadioButton { Text = tc.Nome, Checked = tc.Chave == tipo, AutoSize = true, Font = new Font("Segoe UI", 11f), Margin = new Padding(0, Escala.D(4), Escala.D(28), 0), Tag = tc.Chave };
                rb.CheckedChanged += (s, e) =>
                {
                    if (!rb.Checked) return;
                    tipo = (string)rb.Tag; SalvarTipo(tipo);
                    itensTipo.Text = "Itens conferidos: " + string.Join("  ·  ", Motor.Tipos.First(x => x.Chave == tipo).Itens);
                    itensTipo.Height = itensTipo.Height + 1;
                    Pilha.Relayout(itensTipo);
                    AtualizarBotoes();
                };
                linhaTipos.Controls.Add(rb);
            }
            ct.Corpo.Controls.Add(linhaTipos);
            itensTipo = Rotulo("Itens conferidos: " + string.Join("  ·  ", Motor.Tipos.First(x => x.Chave == tipo).Itens), 9.5f, FontStyle.Regular, Cores.Apagado);
            ct.Corpo.Controls.Add(itensTipo);
            raiz.Controls.Add(ct);

            // arquivos
            var ca = new Cartao();
            ca.Corpo.Controls.Add(Rotulo("2. Arquivos para conferir (" + fila.Count + ")", 12f, FontStyle.Bold));
            ca.Corpo.Controls.Add(Rotulo("Junte todos os documentos do processo. Para adicionar mais, arraste para esta janela ou clique em “Adicionar mais arquivos…”.", 9.5f, FontStyle.Regular, Cores.Apagado));
            int i = 1;
            foreach (var f in fila.ToList()) ca.Corpo.Controls.Add(LinhaArquivo(f, i++));
            if (fila.Count == 0) ca.Corpo.Controls.Add(Rotulo("Nenhum arquivo na lista.", 10f, FontStyle.Italic, Cores.Apagado));

            var acoes = new Panel { Height = Escala.D(50), BackColor = Color.Transparent };
            var bMais = new Botao("Adicionar mais arquivos…");
            var bConf = new Botao("Conferir agora  →", true) { Font = new Font("Segoe UI", 11.5f, FontStyle.Bold), Enabled = fila.Count > 0 };
            acoes.Controls.Add(bMais); acoes.Controls.Add(bConf);
            acoes.Resize += (s, e) =>
            {
                bMais.Location = new Point(0, (acoes.Height - bMais.Height) / 2);
                bConf.Location = new Point(acoes.Width - bConf.Width, (acoes.Height - bConf.Height) / 2);
            };
            bMais.Click += (s, e) => EscolherArquivos();
            bConf.Click += async (s, e) => await Conferir();
            ca.Corpo.Controls.Add(acoes);
            raiz.Controls.Add(ca);
            Pronto(Tela.Fila);
        }

        Control LinhaArquivo(string caminho, int n)
        {
            var p = new Panel { Height = Escala.D(42), BackColor = Cores.Suave, Margin = new Padding(0, Escala.D(2), 0, 0) };
            string ext = Path.GetExtension(caminho).TrimStart('.').ToUpperInvariant();
            string info;
            try { long b = new FileInfo(caminho).Length; info = b >= 1 << 20 ? (b / 1048576.0).ToString("0.0") + " MB" : Math.Max(1, b / 1024) + " KB"; } catch { info = ""; }
            if (lidas.TryGetValue(caminho, out var pgs)) info += "  ·  " + (pgs[0].EhImagem ? "imagem" : pgs.Count + (pgs.Count > 1 ? " páginas" : " página")) + " (já lido)";
            var num = Rotulo(n + ".", 10f, FontStyle.Bold, Cores.Apagado); num.AutoSize = true;
            var tag = Rotulo(ext, 8.5f, FontStyle.Bold, Color.White); tag.AutoSize = true; tag.BackColor = ext == "PDF" ? Cores.Ruim : Cores.Destaque2; tag.Padding = new Padding(Escala.D(4), Escala.D(1), Escala.D(4), Escala.D(1));
            var nome = Rotulo(nomeDe[caminho], 10.5f, FontStyle.Regular); nome.AutoEllipsis = true;
            var det = Rotulo(info, 9f, FontStyle.Regular, Cores.Apagado); det.AutoSize = true;
            var rem = new Botao("Remover") { Font = new Font("Segoe UI", 9f) };
            rem.Click += (s, e) => { if (!ocupado) Remover(caminho); };
            p.Controls.AddRange(new Control[] { num, tag, nome, det, rem });
            p.Resize += (s, e) =>
            {
                int x = Escala.D(10), cy = p.Height / 2;
                num.Location = new Point(x, cy - num.Height / 2); x = num.Right + Escala.D(8);
                tag.Location = new Point(x, cy - tag.Height / 2); x = tag.Right + Escala.D(10);
                rem.Location = new Point(p.Width - rem.Width - Escala.D(8), cy - rem.Height / 2);
                det.Location = new Point(rem.Left - det.Width - Escala.D(14), cy - det.Height / 2);
                nome.SetBounds(x, cy - Escala.D(11), Math.Max(20, det.Left - x - Escala.D(10)), Escala.D(22));
            };
            return p;
        }

        class TelaProgresso
        {
            public Label Tit, Det; public ProgressBar Barra;
            public void Titulo(string t) { if (Tit.InvokeRequired) Tit.BeginInvoke(new Action(() => Tit.Text = t)); else Tit.Text = t; }
            public void Definir(string d, double frac)
            {
                if (Det.IsDisposed) return;
                Det.Text = d; Barra.Value = Math.Max(0, Math.Min(1000, (int)(frac * 1000)));
            }
        }

        TelaProgresso MostrarProgresso()
        {
            Limpar();
            var c = new Cartao { Margin = new Padding(0, Escala.D(16), 0, 0) };
            var tp = new TelaProgresso
            {
                Tit = Rotulo("Lendo os documentos…", 14f, FontStyle.Bold),
                Det = Rotulo("", 10f, FontStyle.Regular, Cores.Apagado),
                Barra = new ProgressBar { Maximum = 1000, Height = Escala.D(18), Style = ProgressBarStyle.Continuous }
            };
            c.Corpo.Controls.Add(tp.Tit);
            c.Corpo.Controls.Add(tp.Det);
            c.Corpo.Controls.Add(tp.Barra);
            c.Corpo.Controls.Add(Rotulo("Páginas escaneadas ou fotos passam pelo reconhecimento de texto (OCR) do Windows, que leva cerca de 1 segundo por página.", 9f, FontStyle.Regular, Cores.Apagado));
            raiz.Controls.Add(c);
            Pronto(Tela.Progresso);
            return tp;
        }

        // ---------- resultado ----------
        string NomePagina(int k)
        {
            if (k < 0 || k >= paginas.Count) return "";
            var p = paginas[k];
            string n = p.EhImagem ? "imagem" : "pág. " + p.Numero;
            return arquivos.Count > 1 ? Abreviar(Path.GetFileNameWithoutExtension(p.Arquivo), 26) + " · " + n : n;
        }

        string ListaPaginas(List<int> ks)
        {
            ks = ks.Where(k => k >= 0 && k < paginas.Count).ToList();
            if (ks.Count == 0) return "";
            if (arquivos.Count > 1) return string.Join(", ", ks.Select(NomePagina));
            if (paginas[ks[0]].EhImagem) return "";
            return (ks.Count > 1 ? "págs. " : "pág. ") + string.Join(", ", ks.Select(k => paginas[k].Numero));
        }

        static string Abreviar(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";

        void MostrarResultado()
        {
            if (resultado == null) { MostrarFila(); return; }
            Limpar();
            var r = resultado;

            raiz.Controls.Add(Veredito(r));

            var info = new List<string>();
            foreach (var a in arquivos)
            {
                var pg = paginas.Where(p => p.Arquivo == a).ToList();
                if (pg.Count == 0) continue;
                string tp = pg.Any(p => p.LidaPorOcr) ? (pg.All(p => p.LidaPorOcr) ? "lido por OCR" : pg.Count(p => p.LidaPorOcr) + " pág. lida(s) por OCR") : pg.Any(p => p.Escaneada) ? "escaneado, sem OCR" : "texto digital";
                info.Add(a + "  (" + (pg[0].EhImagem ? "imagem" : pg.Count + (pg.Count > 1 ? " págs." : " pág.")) + ", " + tp + ")");
            }
            raiz.Controls.Add(Rotulo("Documento(s): " + string.Join("   ·   ", info), 9.5f, FontStyle.Regular, Cores.Apagado));
            if (paginas.Any(p => p.LidaPorOcr))
                raiz.Controls.Add(Faixa("Partes deste documento foram lidas por OCR. Confira nomes, números e datas no original, pois o reconhecimento pode trocar letras.", "warn"));
            foreach (var a in avisos) raiz.Controls.Add(Faixa(a, "warn"));

            for (int i = 0; i < r.Itens.Count; i++) raiz.Controls.Add(CartaoItem(r.Itens[i], i + 1));
            if (r.Justica.Status != "nao") raiz.Controls.Add(CartaoJustica(r.Justica));
            raiz.Controls.Add(CartaoDados(r));
            raiz.Controls.Add(CartaoLegenda(r));

            raiz.Controls.Add(Rotulo("Esta conferência é automática e serve de apoio. A qualificação do título e a decisão sobre o registro/averbação cabem ao Oficial e aos escreventes.", 9f, FontStyle.Italic, Cores.Apagado, new Padding(0, Escala.D(6), 0, 0)));
            Pronto(Tela.Resultado);
        }

        Control Faixa(string texto, string st)
        {
            var c = new Cartao { BackColor = Cores.FundoDoStatus(st), Borda = Cores.FundoDoStatus(st), Faixa = Cores.DoStatus(st) };
            c.Padding = new Padding(Escala.D(14), Escala.D(8), Escala.D(14), Escala.D(8));
            c.Corpo.Controls.Add(Rotulo(texto, 9.5f, FontStyle.Regular, Cores.Tinta));
            return c;
        }

        static string Marca(string st) => st == "ok" || st == "confirmed" ? "✔ " : st == "missing" ? "✘ " : st == "na" ? "– " : "! ";

        Cartao veredito;
        Control Veredito(Resultado r)
        {
            var falta = r.Itens.Where(i => i.StatusEfetivo == "missing").ToList();
            var conf = r.Itens.Where(i => i.StatusEfetivo == "warn").ToList();
            int exigidos = r.Itens.Count(i => i.Status != "na");
            string ato = r.Tipo == "interdicao" ? "registro" : "averbação";
            string st, tit, txt;
            if (falta.Count == 0 && conf.Count == 0)
            { st = "ok"; tit = "Apto para " + ato; txt = "Os " + exigidos + " itens exigidos foram encontrados no documento."; }
            else if (falta.Count > 0)
            {
                st = "missing"; tit = "Pendências encontradas";
                txt = "Faltando: " + string.Join(", ", falta.Select(i => i.Rotulo.ToLowerInvariant())) + "." + (conf.Count > 0 ? "  Conferir: " + string.Join(", ", conf.Select(i => i.Rotulo.ToLowerInvariant())) + "." : "");
            }
            else { st = "warn"; tit = "Conferir antes de " + (ato == "registro" ? "registrar" : "averbar"); txt = "Verifique no documento: " + string.Join(", ", conf.Select(i => i.Rotulo.ToLowerInvariant())) + "."; }

            var c = new Cartao(new Selo(st, 52)) { BackColor = Cores.FundoDoStatus(st), Borda = Cores.DoStatus(st) };
            c.Corpo.Controls.Add(Rotulo(Motor.Tipos.First(t => t.Chave == r.Tipo).Nome.ToUpperInvariant(), 9f, FontStyle.Bold, Cores.Apagado));
            c.Corpo.Controls.Add(Rotulo(tit, 16f, FontStyle.Bold, Cores.DoStatus(st)));
            c.Corpo.Controls.Add(Rotulo(txt, 10.5f));
            c.Corpo.Controls.Add(Rotulo(string.Join("     ", r.Itens.Select(i => Marca(i.StatusEfetivo) + i.Rotulo)), 10f, FontStyle.Bold, Cores.Apagado));
            veredito = c;
            return c;
        }

        void AtualizarVeredito()
        {
            if (veredito == null) return;
            int i = raiz.Controls.IndexOf(veredito);
            var novo = Veredito(resultado);
            raiz.Controls.Add(novo);
            raiz.Controls.SetChildIndex(novo, i);
            var velho = raiz.Controls[i + 1]; raiz.Controls.Remove(velho); velho.Dispose();
            veredito = (Cartao)novo;
            raiz.PerformLayout();
        }

        static readonly Dictionary<string, string> RotuloStatus = new Dictionary<string, string>
            { { "ok", "✔ Encontrado" }, { "warn", "! Conferir" }, { "missing", "✘ Não encontrado" }, { "confirmed", "✔ Conferido manualmente" }, { "na", "– Não se aplica" } };

        static Color CorMarca(string chave)
        {
            if (!Grifo.Categorias.TryGetValue(chave, out var c)) return Cores.Marca;
            return Color.FromArgb((int)(c.R * 255), (int)(c.G * 255), (int)(c.B * 255));
        }

        Control CartaoItem(Item it, int n)
        {
            var st = it.StatusEfetivo;
            var selo = new Selo(st);
            var c = new Cartao(selo) { Faixa = Cores.DoStatus(st) };
            var pl = ListaPaginas(it.Paginas);

            var topo = new Panel { Height = Escala.D(30), BackColor = Color.Transparent };
            var tit = Rotulo(n + ". " + it.Rotulo, 13f, FontStyle.Bold); tit.AutoSize = true;
            var pgl = Rotulo(pl, 9.5f, FontStyle.Regular, Cores.Apagado); pgl.AutoSize = true;
            var stl = Rotulo(RotuloStatus.TryGetValue(st, out var rs) ? rs : st, 10f, FontStyle.Bold, Cores.DoStatus(st)); stl.AutoSize = true;
            topo.Controls.AddRange(new Control[] { tit, pgl, stl });
            topo.Resize += (s, e) =>
            {
                tit.Location = new Point(0, 0);
                pgl.Location = new Point(tit.Right + Escala.D(8), tit.Bottom - pgl.Height - Escala.D(2));
                stl.Location = new Point(Math.Max(pgl.Right + Escala.D(8), topo.Width - stl.Width), Escala.D(3));
            };
            c.Corpo.Controls.Add(topo);
            c.Corpo.Controls.Add(Rotulo(it.Resumo, 10.5f));
            if (!string.IsNullOrEmpty(it.Nota)) c.Corpo.Controls.Add(Rotulo(it.Nota, 10f, FontStyle.Bold, Cores.Destaque));

            if (it.Trechos.Count > 0)
                c.Corpo.Controls.Add(Rotulo("Onde encontrei:", 9f, FontStyle.Bold, Cores.Apagado, new Padding(0, Escala.D(4), 0, 0)));
            foreach (var t in it.Trechos.Take(2))
            {
                var cx = new CaixaTrecho { Font = new Font("Segoe UI", 10f) };
                cx.Mostrar(NomePagina(t.Pagina), t.Antes, t.Achado, t.Depois, CorMarca(it.Chave));
                c.Corpo.Controls.Add(cx);
            }

            if (it.Status == "warn" || it.Status == "missing")
            {
                var cb = new CheckBox { Text = "Conferi no documento e está correto", Checked = it.ConferidoManual, AutoSize = true, Font = new Font("Segoe UI", 10f), ForeColor = Cores.Tinta, BackColor = Color.Transparent, Margin = new Padding(0, Escala.D(4), 0, 0) };
                cb.CheckedChanged += (s, e) =>
                {
                    it.ConferidoManual = cb.Checked;
                    var ns = it.StatusEfetivo;
                    selo.Tipo = ns; selo.Invalidate();
                    c.Faixa = Cores.DoStatus(ns); c.Invalidate();
                    stl.Text = RotuloStatus[ns]; stl.ForeColor = Cores.DoStatus(ns);
                    topo.Width = topo.Width + 1; topo.Width -= 1;
                    AtualizarVeredito();
                };
                c.Corpo.Controls.Add(cb);
            }
            return c;
        }

        Control CartaoJustica(Item j)
        {
            string st = j.Status == "sim" ? "ok" : "warn";
            var c = new Cartao { Faixa = Cores.Hex("#2b9d8f") };
            string tit = j.Status == "sim" ? "Justiça gratuita deferida" : j.Status == "pedida" ? "Justiça gratuita pedida" : "Justiça gratuita indeferida";
            var pl = ListaPaginas(j.Paginas);
            c.Corpo.Controls.Add(Rotulo(tit + (pl != "" ? "   (" + pl + ")" : ""), 12f, FontStyle.Bold, st == "ok" ? Cores.Hex("#1e7268") : Cores.Aviso));
            c.Corpo.Controls.Add(Rotulo(j.Resumo + "  (Não é item obrigatório no divórcio; só para informação.)", 10f, FontStyle.Regular, Cores.Apagado));
            foreach (var t in j.Trechos.Take(1))
            {
                var cx = new CaixaTrecho { Font = new Font("Segoe UI", 10f) };
                cx.Mostrar(NomePagina(t.Pagina), t.Antes, t.Achado, t.Depois, CorMarca("justica"));
                c.Corpo.Controls.Add(cx);
            }
            return c;
        }

        Control CartaoDados(Resultado r)
        {
            var c = new Cartao();
            c.Corpo.Controls.Add(Rotulo("Outras informações encontradas", 12f, FontStyle.Bold));
            var linhas = new List<KeyValuePair<string, string>>();
            if (r.Tipo == "divorcio")
            {
                if (r.Partes.Count > 0) linhas.Add(new KeyValuePair<string, string>("Partes", string.Join("  e  ", r.Partes)));
                if (r.NomesSolteiro.Count > 0) linhas.Add(new KeyValuePair<string, string>("Nome a usar após o divórcio", string.Join("  ·  ", r.NomesSolteiro)));
            }
            foreach (var e in r.Extras) linhas.Add(new KeyValuePair<string, string>(e.Rotulo, e.Achou ? e.Valor : null));
            if (!string.IsNullOrEmpty(r.Cartorio)) linhas.Add(new KeyValuePair<string, string>("Cartório do casamento (cidade)", r.Cartorio));
            foreach (var kv in linhas)
            {
                c.Corpo.Controls.Add(Rotulo(kv.Key, 9f, FontStyle.Bold, Cores.Apagado, new Padding(0, Escala.D(4), 0, 0)));
                c.Corpo.Controls.Add(Rotulo(kv.Value ?? "não encontrado", 10.5f, kv.Value == null ? FontStyle.Italic : FontStyle.Regular, kv.Value == null ? Cores.Apagado : Cores.Tinta));
            }
            c.Corpo.Controls.Add(Rotulo("Use “Ver texto lido” para conferir todo o texto que o programa leu do documento.", 9f, FontStyle.Regular, Cores.Apagado, new Padding(0, Escala.D(6), 0, 0)));
            return c;
        }

        Control CartaoLegenda(Resultado r)
        {
            var chaves = r.Itens.Select(i => i.Chave).ToList();
            if (r.Justica.Status != "nao") chaves.Add("justica");
            if (r.Tipo == "divorcio") chaves.Add("dados");
            var c = new Cartao();
            c.Corpo.Controls.Add(Rotulo("Cores no PDF grifado", 11f, FontStyle.Bold));
            c.Corpo.Controls.Add(new Legenda(chaves.Distinct().Where(Grifo.Categorias.ContainsKey).Select(k => new KeyValuePair<Color, string>(CorMarca(k), Grifo.Categorias[k].Nome))));
            c.Corpo.Controls.Add(Rotulo("Clique em “Baixar PDF grifado” para salvar uma cópia do(s) PDF(s) com estes trechos marcados. O arquivo original não é alterado.", 9f, FontStyle.Regular, Cores.Apagado));
            return c;
        }
    }

    // Faixa do topo
    class Cabecalho : Control
    {
        public Cabecalho() { DoubleBuffered = true; BackColor = Cores.Fundo; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int x = Escala.D(24);
            using (var f1 = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, "REGISTRO CIVIL DAS PESSOAS NATURAIS  ·  BETIM/MG", f1, new Point(x, Escala.D(16)), Cores.Apagado);
            using (var f2 = new Font("Segoe UI Semibold", 19f))
                TextRenderer.DrawText(g, "Conferência de Mandados e Sentenças", f2, new Point(x - Escala.D(2), Escala.D(34)), Cores.Tinta);
            string s = "✔  Funciona sem internet";
            using (var f3 = new Font("Segoe UI", 9.5f))
            {
                var tam = TextRenderer.MeasureText(s, f3);
                var r = new Rectangle(Width - x - tam.Width - Escala.D(24), Escala.D(38), tam.Width + Escala.D(24), tam.Height + Escala.D(10));
                if (r.Left > Escala.D(560))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var b = new SolidBrush(Cores.OkFundo)) using (var path = Arredondado(r, r.Height / 2)) { g.FillPath(b, path); using (var p = new Pen(Cores.Hex("#c5e2cf"))) g.DrawPath(p, path); }
                    TextRenderer.DrawText(g, s, f3, r, Cores.Ok, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
            using (var p = new Pen(Cores.Tinta, Escala.D(2))) g.DrawLine(p, x, Height - Escala.D(2), Width - x, Height - Escala.D(2));
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

        static System.Drawing.Drawing2D.GraphicsPath Arredondado(Rectangle r, int raio)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath(); int d = raio * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
    }
}
