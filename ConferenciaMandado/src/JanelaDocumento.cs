using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ConferenciaMandado
{
    // Mostra as páginas dos documentos com os trechos grifados, para a pessoa conferir no original
    public class JanelaDocumento : Form
    {
        readonly List<Pagina> paginas;
        readonly List<string> arquivos;
        readonly Resultado resultado;
        readonly Item item;
        readonly Action marcarConferido;
        int atual;
        float zoom = 0; // 0 = ajustar à largura

        readonly Panel area = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Cores.Hex("#8a8f91") };
        readonly Folha folha = new Folha();
        readonly ComboBox cbArquivo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly Label lbPagina = new Label { AutoSize = true, ForeColor = Cores.Tinta };
        Botao bConferi;
        readonly Dictionary<int, Image> cache = new Dictionary<int, Image>();
        readonly Dictionary<string, Dictionary<int, List<Grifo.Destaque>>> destaques = new Dictionary<string, Dictionary<int, List<Grifo.Destaque>>>();

        public JanelaDocumento(List<Pagina> paginas, List<string> arquivos, Resultado resultado, int pagina, Item item, Action marcarConferido)
        {
            this.paginas = paginas; this.arquivos = arquivos; this.resultado = resultado; this.item = item; this.marcarConferido = marcarConferido;
            atual = Math.Max(0, Math.Min(paginas.Count - 1, pagina));
            Text = item != null ? "Conferir no documento — " + item.Rotulo : "Documentos";
            Font = new Font("Segoe UI", 10f);
            BackColor = Cores.Fundo;
            Size = new Size(Escala.D(980), Math.Min(Escala.D(1000), Screen.FromPoint(Cursor.Position).WorkingArea.Height - Escala.D(40)));
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            foreach (var a in arquivos) destaques[a] = Grifo.Destaques(resultado, paginas, a);

            // barra de navegação
            var barra = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Escala.D(48), Padding = new Padding(Escala.D(12), Escala.D(8), Escala.D(12), 0), WrapContents = false, BackColor = Cores.Fundo };
            cbArquivo.Width = Escala.D(260); cbArquivo.Margin = new Padding(0, Escala.D(3), Escala.D(12), 0);
            cbArquivo.Items.AddRange(arquivos.Cast<object>().ToArray());
            cbArquivo.Visible = arquivos.Count > 1;
            var bAnt = new Botao("◀ Anterior"); var bProx = new Botao("Próxima ▶");
            var bMenos = new Botao("−"); var bMais = new Botao("+"); var bAjustar = new Botao("Ajustar à largura");
            lbPagina.Margin = new Padding(Escala.D(4), Escala.D(8), Escala.D(12), 0);
            barra.Controls.AddRange(new Control[] { cbArquivo, bAnt, lbPagina, bProx, bMenos, bMais, bAjustar });
            bAnt.Click += (s, e) => IrPara(atual - 1);
            bProx.Click += (s, e) => IrPara(atual + 1);
            bMenos.Click += (s, e) => MudarZoom(0.8f);
            bMais.Click += (s, e) => MudarZoom(1.25f);
            bAjustar.Click += (s, e) => { zoom = 0; Ajustar(); };
            cbArquivo.SelectedIndexChanged += (s, e) =>
            {
                var a = (string)cbArquivo.SelectedItem;
                if (a != null && paginas[atual].Arquivo != a) IrPara(paginas.FindIndex(p => p.Arquivo == a));
            };

            // o item que está sendo conferido
            Control faixa = null;
            if (item != null)
            {
                var st = item.StatusEfetivo;
                var p = new Panel { Dock = DockStyle.Top, Height = Escala.D(58), BackColor = Cores.FundoDoStatus(st), Padding = new Padding(Escala.D(16), Escala.D(6), Escala.D(16), Escala.D(6)) };
                var l = new Label { Dock = DockStyle.Fill, Text = item.Rotulo + ":  " + item.Resumo, ForeColor = Cores.Tinta, Font = new Font("Segoe UI", 10f), UseMnemonic = false };
                var tira = new Panel { Dock = DockStyle.Left, Width = Escala.D(5), BackColor = Cores.DoStatus(st) };
                p.Controls.Add(l); p.Controls.Add(tira);
                faixa = p;
            }

            // rodapé
            var rodape = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Escala.D(54), FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(Escala.D(12), Escala.D(10), Escala.D(12), 0), BackColor = Cores.Fundo };
            var bFechar = new Botao("Fechar");
            var bExterno = new Botao("Abrir no leitor de PDF");
            bFechar.Click += (s, e) => Close();
            bExterno.Click += (s, e) => AbrirExterno();
            rodape.Controls.Add(bFechar);
            if (item != null && (item.Status == "warn" || item.Status == "missing"))
            {
                bConferi = new Botao(item.ConferidoManual ? "✔ Já marcado como conferido" : "✔ Conferi e está correto", true) { Enabled = !item.ConferidoManual };
                bConferi.Click += (s, e) => { marcarConferido?.Invoke(); bConferi.Text = "✔ Marcado como conferido"; bConferi.Enabled = false; };
                rodape.Controls.Add(bConferi);
            }
            rodape.Controls.Add(bExterno);
            var dica = new Label { AutoSize = true, ForeColor = Cores.Apagado, Margin = new Padding(0, Escala.D(8), Escala.D(16), 0), Text = "Page Up / Page Down mudam de página" };
            rodape.Controls.Add(dica);

            area.Controls.Add(folha);
            area.Resize += (s, e) => Ajustar();
            Controls.Add(area);
            if (faixa != null) Controls.Add(faixa);
            Controls.Add(barra);
            Controls.Add(rodape);

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.PageDown) { IrPara(atual + 1); e.Handled = true; }
                else if (e.KeyCode == Keys.PageUp) { IrPara(atual - 1); e.Handled = true; }
                else if (e.KeyCode == Keys.Escape) Close();
            };
            Shown += (s, e) => IrPara(atual);
            FormClosed += (s, e) => { foreach (var i in cache.Values) i.Dispose(); };
        }

        void MudarZoom(float f)
        {
            if (folha.Imagem == null) return;
            float z = zoom > 0 ? zoom : (float)folha.Width / folha.Imagem.Width;
            zoom = Math.Max(0.2f, Math.Min(4f, z * f));
            Ajustar();
        }

        void Ajustar()
        {
            if (folha.Imagem == null) { folha.Size = new Size(Math.Max(100, area.ClientSize.Width - Escala.D(40)), Escala.D(300)); folha.Location = new Point(Escala.D(20), Escala.D(20)); return; }
            float z = zoom > 0 ? zoom : (float)(area.ClientSize.Width - Escala.D(40) - SystemInformation.VerticalScrollBarWidth) / folha.Imagem.Width;
            int w = Math.Max(50, (int)(folha.Imagem.Width * z)), h = Math.Max(50, (int)(folha.Imagem.Height * z));
            var pos = area.AutoScrollPosition;
            folha.Size = new Size(w, h);
            folha.Location = new Point(Math.Max(Escala.D(20), (area.ClientSize.Width - w) / 2) + pos.X, Escala.D(20) + pos.Y);
            area.AutoScrollMinSize = new Size(w + Escala.D(40), h + Escala.D(40));
            folha.Invalidate();
        }

        async void IrPara(int k)
        {
            if (k < 0 || k >= paginas.Count) return;
            atual = k;
            var p = paginas[k];
            lbPagina.Text = (p.EhImagem ? "imagem" : "Página " + p.Numero + " de " + p.Total) + (arquivos.Count > 1 ? "   (" + (k + 1) + " de " + paginas.Count + " no total)" : "");
            if ((string)cbArquivo.SelectedItem != p.Arquivo) cbArquivo.SelectedItem = p.Arquivo;
            folha.Mensagem = "Carregando a página…"; folha.Imagem = null; folha.Destaques = null; Ajustar();
            Image img = null; string erro = null;
            if (!cache.TryGetValue(k, out img))
            {
                try
                {
                    var bytes = await Renderizar(p);
                    img = Image.FromStream(new MemoryStream(bytes));
                    cache[k] = img;
                }
                catch (Exception ex) { erro = ex.Message; }
            }
            if (atual != k) return;
            if (img == null)
            {
                folha.Mensagem = "Não consegui mostrar esta página aqui (" + erro + ").\nUse “Abrir no leitor de PDF”.";
                folha.Invalidate();
                return;
            }
            folha.Imagem = img; folha.Mensagem = null;
            folha.Destaques = destaques.TryGetValue(p.Arquivo, out var d) && d.TryGetValue(p.Numero, out var lista) ? lista : null;
            folha.Foco = item?.Chave;
            area.AutoScrollPosition = new Point(0, 0);
            Ajustar();
            // rola até o primeiro trecho do item
            var alvo = folha.Destaques?.FirstOrDefault(x => x.Categoria == item?.Chave && x.Linhas.Count > 0);
            if (alvo != null)
            {
                int y = (int)(alvo.Linhas.Min(l => Math.Min(l[1], l[3])) * folha.Height) - Escala.D(120);
                if (y > 0) area.AutoScrollPosition = new Point(0, y);
                Ajustar();
            }
        }

        static async Task<byte[]> Renderizar(Pagina p)
        {
            if (p.EhImagem)
            {
                using (var img = Image.FromFile(p.Caminho))
                using (var ms = new MemoryStream()) { img.Save(ms, System.Drawing.Imaging.ImageFormat.Png); return ms.ToArray(); }
            }
            // só para testes fora do Windows
            if (Environment.GetEnvironmentVariable("CONFERENCIA_TESTE_PDFTOPPM") == "1") return RenderizarTeste(p);
            return await RenderizarWindows(p.Caminho, p.Numero);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static Task<byte[]> RenderizarWindows(string caminho, int numero) => Task.Run(() => PaginaWindows.RenderizarPng(caminho, numero, 1500));

        static byte[] RenderizarTeste(Pagina p)
        {
            string saida = Path.Combine(Path.GetTempPath(), "pg-" + Guid.NewGuid().ToString("N"));
            var ps = Process.Start(new ProcessStartInfo("pdftoppm", "-r 130 -png -singlefile -f " + p.Numero + " -l " + p.Numero + " \"" + p.Caminho + "\" \"" + saida + "\"") { UseShellExecute = false });
            ps.WaitForExit();
            var b = File.ReadAllBytes(saida + ".png"); File.Delete(saida + ".png"); return b;
        }

        void AbrirExterno()
        {
            var p = paginas[atual];
            try
            {
                if (p.EhImagem || !File.Exists(p.Caminho)) { Process.Start(new ProcessStartInfo(p.Caminho) { UseShellExecute = true }); return; }
                // o Edge (que vem no Windows) abre o PDF já na página certa
                var url = new Uri(p.Caminho).AbsoluteUri + "#page=" + p.Numero;
                try { Process.Start(new ProcessStartInfo("msedge.exe", "\"" + url + "\"") { UseShellExecute = true }); }
                catch { Process.Start(new ProcessStartInfo(p.Caminho) { UseShellExecute = true }); }
            }
            catch (Exception ex) { MessageBox.Show(this, "Não consegui abrir: " + ex.Message, Text); }
        }

        // a página desenhada, com os grifos por cima
        class Folha : Control
        {
            public Image Imagem;
            public List<Grifo.Destaque> Destaques;
            public string Foco, Mensagem;
            public Folha() { DoubleBuffered = true; BackColor = Color.White; }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                if (Imagem == null)
                {
                    TextRenderer.DrawText(g, Mensagem ?? "", Font, ClientRectangle, Cores.Apagado, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                    return;
                }
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(Imagem, new Rectangle(0, 0, Width, Height));
                if (Destaques == null) return;
                foreach (var d in Destaques)
                {
                    if (!Grifo.Categorias.TryGetValue(d.Categoria, out var c)) continue;
                    var cor = Color.FromArgb((int)(c.R * 255), (int)(c.G * 255), (int)(c.B * 255));
                    bool foco = d.Categoria == Foco;
                    using (var b = new SolidBrush(Color.FromArgb(foco ? 110 : 70, cor)))
                    using (var pen = new Pen(Color.FromArgb(220, ControlPaint.Dark(cor, .25f)), 2))
                        foreach (var l in d.Linhas)
                        {
                            float x0 = (float)Math.Min(l[0], l[2]) * Width, x1 = (float)Math.Max(l[0], l[2]) * Width;
                            float y0 = (float)Math.Min(l[1], l[3]) * Height, y1 = (float)Math.Max(l[1], l[3]) * Height;
                            g.FillRectangle(b, x0 - 1, y0 - 1, x1 - x0 + 2, y1 - y0 + 2);
                            if (foco) g.DrawRectangle(pen, x0 - 2, y0 - 2, x1 - x0 + 4, y1 - y0 + 4);
                        }
                }
            }
        }
    }
}
