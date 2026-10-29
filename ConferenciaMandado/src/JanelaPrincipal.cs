using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ConferenciaMandado
{
    public class JanelaPrincipal : Form
    {
        readonly Panel rolagem = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Cores.Fundo };
        readonly Pilha raiz = new Pilha { Espaco = 12, BackColor = Cores.Fundo };
        readonly FlowLayoutPanel barra = new FlowLayoutPanel { Dock = DockStyle.Top, WrapContents = false, BackColor = Cores.Fundo };
        Botao bAbrir, bAdicionar, bCopiar, bImprimir, bTexto, bNova;

        // estado da conferência
        readonly List<Pagina> paginas = new List<Pagina>();
        readonly List<string> arquivos = new List<string>();
        Resultado resultado;
        string textoLido = "";
        bool ocupado;
        readonly List<string> avisos = new List<string>();

        public JanelaPrincipal(string[] args)
        {
            Text = "Conferência de Mandado de Divórcio — RCPN Betim/MG";
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.None;
            using (var g = CreateGraphics()) Escala.K = g.DpiX / 96f;
            BackColor = Cores.Fundo; ForeColor = Cores.Tinta;
            Size = new Size(Escala.D(1040), Escala.D(820));
            MinimumSize = new Size(Escala.D(640), Escala.D(480));
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            KeyPreview = true;

            var cab = new Cabecalho { Dock = DockStyle.Top, Height = Escala.D(84) };
            barra.Height = Escala.D(52);
            barra.Padding = new Padding(Escala.D(24), Escala.D(10), Escala.D(24), 0);
            bAbrir = new Botao("Abrir PDF…", true);
            bAdicionar = new Botao("Adicionar arquivo…");
            bCopiar = new Botao("Copiar relatório");
            bImprimir = new Botao("Imprimir relatório");
            bTexto = new Botao("Ver texto lido");
            bNova = new Botao("Nova conferência");
            barra.Controls.AddRange(new Control[] { bAbrir, bAdicionar, bCopiar, bImprimir, bTexto, bNova });
            bAbrir.Click += (s, e) => EscolherArquivos(false);
            bAdicionar.Click += (s, e) => EscolherArquivos(true);
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
            ControlAdded += (s, e) => HabilitarSoltar(e.Control);

            KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.O && !ocupado) { EscolherArquivos(false); e.Handled = true; } };

            MostrarInicio();
            var iniciais = (args ?? new string[0]).Where(File.Exists).ToArray();
            if (iniciais.Length > 0) Shown += (s, e) => BeginInvoke(new Action(async () => await Processar(iniciais, false)));
        }

        void AjustarLargura()
        {
            int w = rolagem.ClientSize.Width;
            int max = Escala.D(1000);
            raiz.Width = Math.Min(w, max);
            raiz.Left = Math.Max(0, (w - raiz.Width) / 2);
            raiz.PerformLayout();
        }

        void AtualizarBotoes()
        {
            bool tem = resultado != null && !ocupado;
            bAbrir.Enabled = !ocupado;
            bAdicionar.Visible = bCopiar.Visible = bImprimir.Visible = bTexto.Visible = bNova.Visible = resultado != null;
            bAdicionar.Enabled = bCopiar.Enabled = bImprimir.Enabled = bTexto.Enabled = bNova.Enabled = tem;
        }

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

        async void AoSoltar(object s, DragEventArgs e)
        {
            MarcarZona(false);
            if (ocupado || !(e.Data.GetData(DataFormats.FileDrop) is string[] lista)) return;
            // com uma conferência aberta, o que for solto entra junto (ex.: a certidão de trânsito em julgado)
            bool adicionar = false;
            if (resultado != null)
            {
                var r = MessageBox.Show(this, "Juntar este(s) arquivo(s) à conferência aberta?\n\nSim: adiciona aos documentos já lidos (ex.: a certidão de trânsito em julgado).\nNão: começa uma nova conferência.",
                    Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                adicionar = r == DialogResult.Yes;
            }
            await Processar(lista, adicionar);
        }

        ZonaSoltar zona;
        void MarcarZona(bool sobre) { if (zona != null && zona.Sobre != sobre) { zona.Sobre = sobre; zona.Invalidate(); } }

        async void EscolherArquivos(bool adicionar)
        {
            if (ocupado) return;
            using (var d = new OpenFileDialog
            {
                Title = adicionar ? "Adicionar arquivo à conferência" : "Abrir mandado de divórcio",
                Filter = "PDF ou imagem do mandado|*.pdf;*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp|PDF (*.pdf)|*.pdf|Imagens|*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp",
                Multiselect = true
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                await Processar(d.FileNames, adicionar);
            }
        }

        void NovaConferencia()
        {
            paginas.Clear(); arquivos.Clear(); resultado = null; textoLido = ""; avisos.Clear();
            MostrarInicio();
        }

        // ---------- leitura ----------
        string NomeUnico(string n)
        {
            if (!arquivos.Contains(n)) return n;
            string b = Path.GetFileNameWithoutExtension(n), x = Path.GetExtension(n); int i = 2;
            while (arquivos.Contains(b + " (" + i + ")" + x)) i++;
            return b + " (" + i + ")" + x;
        }

        async Task Processar(string[] lista, bool adicionar)
        {
            var validos = lista.Where(f => File.Exists(f) && Leitura.Aceita(f)).ToArray();
            if (validos.Length == 0)
            {
                MessageBox.Show(this, "Escolha um arquivo PDF (ou uma foto/escaneamento JPG, PNG ou TIFF) do mandado.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var antesPaginas = paginas.ToList(); var antesArquivos = arquivos.ToList(); var antesAvisos = avisos.ToList();
            var antesResultado = resultado;
            if (!adicionar) { paginas.Clear(); arquivos.Clear(); avisos.Clear(); }

            ocupado = true; AtualizarBotoes();
            var tela = MostrarProgresso();
            var falhas = new List<string>();
            try
            {
                var novas = new List<Pagina>();
                for (int i = 0; i < validos.Length; i++)
                {
                    string f = validos[i], nome = NomeUnico(Path.GetFileName(f));
                    tela.Definir("Lendo " + nome + "…", (double)i / validos.Length);
                    try
                    {
                        List<Pagina> pgs;
                        if (Leitura.EhPdf(f)) pgs = await Task.Run(() => Leitura.LerPdf(f, nome));
                        else pgs = new List<Pagina> { new Pagina { Arquivo = nome, Caminho = f, EhImagem = true, Numero = 1, Total = 1, Escaneada = true } };
                        arquivos.Add(nome); novas.AddRange(pgs); paginas.AddRange(pgs);
                    }
                    catch (Exception ex)
                    {
                        falhas.Add(nome + ": " + Explicar(ex));
                    }
                }

                // páginas escaneadas: OCR do Windows
                var esc = novas.Where(p => p.Escaneada).ToList();
                if (esc.Count > 0)
                {
                    int feitas = 0;
                    tela.Titulo("Reconhecendo o texto das páginas escaneadas…");
                    tela.Definir("Página 1 de " + esc.Count, 0);
                    try
                    {
                        string idioma = await LerEscaneadas(esc, () => { feitas++; Reportar(tela, feitas, esc.Count); });
                        if (!idioma.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
                            Avisar("O OCR do Windows está usando o idioma “" + idioma + "”, porque o reconhecimento de texto em português não está instalado. Acentos podem sair errados. Para instalar: Configurações > Hora e idioma > Idioma > Português (Brasil) > Opções > Reconhecimento óptico de caracteres (Baixar).");
                    }
                    catch (Exception ex)
                    {
                        Avisar("Não foi possível fazer o reconhecimento de texto (OCR) das páginas escaneadas: " + Explicar(ex));
                    }
                }

                textoLido = Leitura.Juntar(paginas);
                if (Leitura.LetrasUteis(textoLido) < 30)
                {
                    string m = falhas.Count > 0 ? "Não consegui ler o(s) arquivo(s):\n\n" + string.Join("\n", falhas)
                        : "Não foi possível ler texto neste documento. Se for um escaneamento ou foto, tente uma imagem mais nítida, bem iluminada e reta.";
                    if (avisos.Count > 0) m += "\n\n" + string.Join("\n\n", avisos);
                    Restaurar(antesPaginas, antesArquivos, antesAvisos, antesResultado);
                    MessageBox.Show(this, m, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                tela.Titulo("Conferindo os itens do mandado…");
                tela.Definir("Analisando o texto", 1);
                string t = textoLido;
                resultado = await Task.Run(() => Motor.Analisar(t));
                ocupado = false;
                MostrarResultado();
                if (falhas.Count > 0)
                    MessageBox.Show(this, "Alguns arquivos não puderam ser lidos e ficaram fora da conferência:\n\n" + string.Join("\n", falhas), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Restaurar(antesPaginas, antesArquivos, antesAvisos, antesResultado);
                MessageBox.Show(this, "Não consegui ler o documento: " + Explicar(ex), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        void Restaurar(List<Pagina> pg, List<string> arq, List<string> av, Resultado r)
        {
            paginas.Clear(); paginas.AddRange(pg); arquivos.Clear(); arquivos.AddRange(arq); avisos.Clear(); avisos.AddRange(av);
            resultado = r; textoLido = Leitura.Juntar(paginas);
            ocupado = false;
            if (r != null) MostrarResultado(); else MostrarInicio();
        }

        void Reportar(TelaProgresso tela, int feitas, int total)
        {
            BeginInvoke(new Action(() => tela.Definir(feitas < total ? "Página " + (feitas + 1) + " de " + total : "Concluído", (double)feitas / total)));
        }

        static string Explicar(Exception ex)
        {
            var m = ex is AggregateException ag && ag.InnerException != null ? ag.InnerException.Message : ex.Message;
            if (ex.GetType().Name.Contains("Password") || m.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 || m.IndexOf("encrypt", StringComparison.OrdinalIgnoreCase) >= 0)
                return "o PDF está protegido por senha. Salve uma cópia sem senha e tente de novo.";
            return m;
        }

        // ---------- telas ----------
        void Limpar()
        {
            raiz.SuspendLayout();
            foreach (Control c in raiz.Controls.Cast<Control>().ToList()) c.Dispose();
            raiz.Controls.Clear();
            zona = null;
        }

        void Pronto()
        {
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
            var t1 = Rotulo("Arraste aqui o PDF do mandado", 15f, FontStyle.Bold);
            var t2 = Rotulo("ou clique para escolher o arquivo. Aceita PDF com texto (do WhatsApp / PJe) e PDF escaneado ou foto do papel. Pode abrir vários arquivos juntos (segure Ctrl), por exemplo a sentença e a certidão de trânsito em julgado.", 10f, FontStyle.Regular, Cores.Apagado);
            t1.TextAlign = t2.TextAlign = ContentAlignment.TopCenter;
            var bt = new Botao("Abrir PDF…", true);
            zona.Controls.AddRange(new Control[] { t1, t2, bt });
            zona.Resize += (s, e) =>
            {
                int w = zona.Width - Escala.D(80);
                t1.SetBounds(Escala.D(40), Escala.D(100), w, Escala.D(34));
                int h2 = Pilha.Altura(t2, Math.Min(w, Escala.D(640)));
                t2.SetBounds((zona.Width - Math.Min(w, Escala.D(640))) / 2, Escala.D(138), Math.Min(w, Escala.D(640)), h2);
                bt.Location = new Point((zona.Width - bt.Width) / 2, t2.Bottom + Escala.D(16));
                int alt = bt.Bottom + Escala.D(30);
                if (zona.Height != alt) { zona.Height = alt; Pilha.Relayout(zona); }
            };
            zona.Click += (s, e) => EscolherArquivos(false);
            t1.Click += (s, e) => EscolherArquivos(false);
            t2.Click += (s, e) => EscolherArquivos(false);
            bt.Click += (s, e) => EscolherArquivos(false);
            raiz.Controls.Add(zona);

            var itens = Rotulo("O programa confere os 4 itens essenciais para a averbação:   1. força de mandado   ·   2. nome que a parte voltará a assinar   ·   3. divisão de bens   ·   4. trânsito em julgado", 10f, FontStyle.Regular, Cores.Apagado);
            itens.TextAlign = ContentAlignment.TopCenter;
            raiz.Controls.Add(itens);
            var priv = Rotulo("Tudo é feito neste computador, sem internet. O conteúdo dos mandados não é enviado para lugar nenhum e não fica salvo pelo programa.", 9f, FontStyle.Regular, Cores.Ok);
            priv.TextAlign = ContentAlignment.TopCenter;
            raiz.Controls.Add(priv);
            Pronto();
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
            var tela = new TelaProgresso
            {
                Tit = Rotulo("Lendo o documento…", 14f, FontStyle.Bold),
                Det = Rotulo("", 10f, FontStyle.Regular, Cores.Apagado),
                Barra = new ProgressBar { Maximum = 1000, Height = Escala.D(18), Style = ProgressBarStyle.Continuous }
            };
            c.Corpo.Controls.Add(tela.Tit);
            c.Corpo.Controls.Add(tela.Det);
            c.Corpo.Controls.Add(tela.Barra);
            c.Corpo.Controls.Add(Rotulo("Páginas escaneadas ou fotos passam pelo reconhecimento de texto (OCR) do Windows, que leva cerca de 1 segundo por página.", 9f, FontStyle.Regular, Cores.Apagado));
            raiz.Controls.Add(c);
            Pronto();
            return tela;
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

        public static readonly string[] Ordem = { "mandado", "nome", "partilha", "transito" };

        void MostrarResultado()
        {
            Limpar();
            var r = resultado;

            raiz.Controls.Add(Veredito(r));

            // arquivos lidos
            var info = new List<string>();
            foreach (var a in arquivos)
            {
                var pg = paginas.Where(p => p.Arquivo == a).ToList();
                string tipo = pg.Any(p => p.LidaPorOcr) ? (pg.All(p => p.LidaPorOcr) ? "lido por OCR" : pg.Count(p => p.LidaPorOcr) + " pág. lida(s) por OCR") : pg.Any(p => p.Escaneada) ? "escaneado, sem OCR" : "texto digital";
                info.Add(a + "  (" + (pg[0].EhImagem ? "imagem" : pg.Count + (pg.Count > 1 ? " págs." : " pág.")) + ", " + tipo + ")");
            }
            raiz.Controls.Add(Rotulo("Documento(s): " + string.Join("   ·   ", info), 9.5f, FontStyle.Regular, Cores.Apagado));
            if (paginas.Any(p => p.LidaPorOcr))
                raiz.Controls.Add(Faixa("Partes deste documento foram lidas por OCR. Confira nomes, números e datas no original, pois o reconhecimento pode trocar letras.", "warn"));
            foreach (var a in avisos) raiz.Controls.Add(Faixa(a, "warn"));

            for (int i = 0; i < r.Itens.Count; i++) raiz.Controls.Add(CartaoItem(r.Itens[i], i + 1));

            if (r.Justica.Status != "nao") raiz.Controls.Add(CartaoJustica(r.Justica));

            raiz.Controls.Add(CartaoDados(r));

            raiz.Controls.Add(Rotulo("Esta conferência é automática e serve de apoio. A qualificação do título e a decisão sobre a averbação cabem ao Oficial e aos escreventes.", 9f, FontStyle.Italic, Cores.Apagado, new Padding(0, Escala.D(6), 0, 0)));
            Pronto();
        }

        Control Faixa(string texto, string st)
        {
            var c = new Cartao { BackColor = Cores.FundoDoStatus(st), Borda = Cores.FundoDoStatus(st), Faixa = Cores.DoStatus(st) };
            c.Padding = new Padding(Escala.D(14), Escala.D(8), Escala.D(14), Escala.D(8));
            c.Corpo.Controls.Add(Rotulo(texto, 9.5f, FontStyle.Regular, Cores.Tinta));
            return c;
        }

        Cartao veredito;
        Control Veredito(Resultado r)
        {
            var falta = r.Itens.Where(i => i.StatusEfetivo == "missing").ToList();
            var conf = r.Itens.Where(i => i.StatusEfetivo == "warn").ToList();
            string st, tit, txt;
            if (falta.Count == 0 && conf.Count == 0)
            { st = "ok"; tit = "Apto para averbação"; txt = "Os 4 itens essenciais foram encontrados no documento."; }
            else if (falta.Count > 0)
            {
                st = "missing"; tit = "Pendências encontradas";
                txt = "Faltando: " + string.Join(", ", falta.Select(i => i.Rotulo.ToLowerInvariant())) + "." + (conf.Count > 0 ? "  Conferir: " + string.Join(", ", conf.Select(i => i.Rotulo.ToLowerInvariant())) + "." : "");
            }
            else { st = "warn"; tit = "Conferir antes de averbar"; txt = "Verifique no documento: " + string.Join(", ", conf.Select(i => i.Rotulo.ToLowerInvariant())) + "."; }

            var c = new Cartao(new Selo(st, 52)) { BackColor = Cores.FundoDoStatus(st), Borda = Cores.DoStatus(st) };
            c.Corpo.Controls.Add(Rotulo(tit, 16f, FontStyle.Bold, Cores.DoStatus(st)));
            c.Corpo.Controls.Add(Rotulo(txt, 10.5f));
            // resumo ✔/✘ dos 4 itens numa linha
            var linha = string.Join("     ", r.Itens.Select(i => (i.StatusEfetivo == "ok" || i.StatusEfetivo == "confirmed" ? "✔ " : i.StatusEfetivo == "missing" ? "✘ " : "! ") + i.Rotulo));
            c.Corpo.Controls.Add(Rotulo(linha, 10f, FontStyle.Bold, Cores.Apagado));
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
            { { "ok", "✔ Encontrado" }, { "warn", "! Conferir" }, { "missing", "✘ Não encontrado" }, { "confirmed", "✔ Conferido manualmente" } };

        static Color CorMarca(string chave) =>
            chave == "mandado" ? Cores.Hex("#ffdc40") : chave == "nome" ? Cores.Hex("#b5e6b5") : chave == "partilha" ? Cores.Hex("#bcd3ff") :
            chave == "transito" ? Cores.Hex("#ffc58f") : Cores.Hex("#9fe3d8");

        Control CartaoItem(Item it, int n)
        {
            var st = it.StatusEfetivo;
            var selo = new Selo(st);
            var c = new Cartao(selo) { Faixa = Cores.DoStatus(st) };
            var pl = ListaPaginas(it.Paginas);

            var topo = new Panel { Height = Escala.D(30), BackColor = Color.Transparent };
            var tit = Rotulo(n + ". " + it.Rotulo, 13f, FontStyle.Bold);
            tit.AutoSize = true;
            var pgl = Rotulo(pl, 9.5f, FontStyle.Regular, Cores.Apagado); pgl.AutoSize = true;
            var stl = Rotulo(RotuloStatus[st], 10f, FontStyle.Bold, Cores.DoStatus(st)); stl.AutoSize = true;
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

            if (it.Status != "ok")
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
            c.Corpo.Controls.Add(Rotulo(j.Resumo + "  (Não é item obrigatório; só para informação.)", 10f, FontStyle.Regular, Cores.Apagado));
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
            if (r.Partes.Count > 0) linhas.Add(new KeyValuePair<string, string>("Partes", string.Join("  e  ", r.Partes)));
            if (r.NomesSolteiro.Count > 0) linhas.Add(new KeyValuePair<string, string>("Nome a usar após o divórcio", string.Join("  ·  ", r.NomesSolteiro)));
            foreach (var e in r.Extras) linhas.Add(new KeyValuePair<string, string>(e.Rotulo, e.Achou ? e.Valor : null));
            if (!string.IsNullOrEmpty(r.Cartorio)) linhas.Add(new KeyValuePair<string, string>("Cartório do casamento (cidade)", r.Cartorio));
            foreach (var kv in linhas)
            {
                var l = Rotulo(kv.Key, 9f, FontStyle.Bold, Cores.Apagado, new Padding(0, Escala.D(4), 0, 0));
                var v = Rotulo(kv.Value ?? "não encontrado", 10.5f, kv.Value == null ? FontStyle.Italic : FontStyle.Regular, kv.Value == null ? Cores.Apagado : Cores.Tinta);
                c.Corpo.Controls.Add(l); c.Corpo.Controls.Add(v);
            }
            c.Corpo.Controls.Add(Rotulo("Use “Ver texto lido” para conferir todo o texto que o programa leu do documento.", 9f, FontStyle.Regular, Cores.Apagado, new Padding(0, Escala.D(6), 0, 0)));
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
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int x = Escala.D(24);
            using (var f1 = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, "REGISTRO CIVIL DAS PESSOAS NATURAIS  ·  BETIM/MG", f1, new Point(x, Escala.D(16)), Cores.Apagado);
            using (var f2 = new Font("Segoe UI Semibold", 19f))
                TextRenderer.DrawText(g, "Conferência de Mandado de Divórcio", f2, new Point(x - Escala.D(2), Escala.D(34)), Cores.Tinta);
            // selo "sem internet"
            string s = "✔  Funciona sem internet";
            using (var f3 = new Font("Segoe UI", 9.5f))
            {
                var tam = TextRenderer.MeasureText(s, f3);
                var r = new Rectangle(Width - x - tam.Width - Escala.D(24), Escala.D(38), tam.Width + Escala.D(24), tam.Height + Escala.D(10));
                if (r.Left > Escala.D(520))
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
