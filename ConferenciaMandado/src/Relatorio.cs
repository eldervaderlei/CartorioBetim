using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ConferenciaMandado
{
    static class Relatorio
    {
        static string Pag(List<Pagina> paginas, bool multi, int k)
        {
            if (k < 0 || k >= paginas.Count) return "";
            var p = paginas[k];
            return (multi ? Path.GetFileNameWithoutExtension(p.Arquivo) + ", " : "") + (p.EhImagem ? "imagem" : "pág. " + p.Numero);
        }

        public static string Texto(Resultado r, List<string> arquivos, List<Pagina> paginas)
        {
            if (r == null) return "";
            bool multi = arquivos.Count > 1;
            var sb = new StringBuilder();
            sb.AppendLine("REGISTRO CIVIL DAS PESSOAS NATURAIS · BETIM/MG");
            var tipo = Motor.Tipos.FirstOrDefault(t => t.Chave == r.Tipo);
            sb.AppendLine("Relatório de conferência — " + (tipo?.Nome ?? "Mandado"));
            sb.AppendLine("Conferido em " + DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
            sb.AppendLine("Documento(s): " + string.Join("; ", arquivos));
            sb.AppendLine();
            var falta = r.Itens.Where(i => i.StatusEfetivo == "missing").ToList();
            var conf = r.Itens.Where(i => i.StatusEfetivo == "warn").ToList();
            string ato = r.Tipo == "interdicao" ? "REGISTRO" : "AVERBAÇÃO";
            sb.AppendLine("RESULTADO: " + (falta.Count == 0 && conf.Count == 0 ? "APTO PARA " + ato + " (os itens exigidos foram encontrados)"
                : falta.Count > 0 ? "PENDÊNCIAS — faltando: " + string.Join(", ", falta.Select(i => i.Rotulo.ToLowerInvariant())) + (conf.Count > 0 ? "; conferir: " + string.Join(", ", conf.Select(i => i.Rotulo.ToLowerInvariant())) : "")
                : "CONFERIR — " + string.Join(", ", conf.Select(i => i.Rotulo.ToLowerInvariant()))));
            sb.AppendLine();
            int n = 1;
            foreach (var it in r.Itens)
            {
                string st = it.StatusEfetivo;
                string marca = st == "ok" ? "[✔]" : st == "confirmed" ? "[✔ conferido manualmente]" : st == "missing" ? "[✘]" : st == "na" ? "[– não se aplica]" : "[! conferir]";
                sb.AppendLine(n++ + ". " + it.Rotulo + "  " + marca);
                sb.AppendLine("   " + it.Resumo);
                if (!string.IsNullOrEmpty(it.Nota)) sb.AppendLine("   " + it.Nota);
                foreach (var t in it.Trechos.Take(2))
                    sb.AppendLine("   (" + Pag(paginas, multi, t.Pagina) + ") \"" + (t.Antes + "[" + t.Achado + "]" + t.Depois).Trim() + "\"");
                sb.AppendLine();
            }
            if (r.Justica.Status != "nao") { sb.AppendLine("Justiça gratuita: " + r.Justica.Resumo); sb.AppendLine(); }
            sb.AppendLine("Outras informações:");
            if (r.Tipo == "divorcio" && r.Partes.Count > 0) sb.AppendLine("   Partes: " + string.Join(" e ", r.Partes));
            if (r.Tipo == "divorcio" && r.NomesSolteiro.Count > 0) sb.AppendLine("   Nome a usar após o divórcio: " + string.Join(" · ", r.NomesSolteiro));
            foreach (var e in r.Extras) sb.AppendLine("   " + e.Rotulo + ": " + (e.Achou ? e.Valor : "não encontrado"));
            if (!string.IsNullOrEmpty(r.Cartorio)) sb.AppendLine("   Cartório do casamento (cidade): " + r.Cartorio);
            sb.AppendLine();
            sb.AppendLine("Conferência automática, de apoio. A qualificação do título e a decisão sobre o registro/averbação cabem ao Oficial e aos escreventes.");
            return sb.ToString();
        }

        public static void Imprimir(IWin32Window dono, Resultado r, List<string> arquivos, List<Pagina> paginas)
        {
            var linhas = Texto(r, arquivos, paginas).Replace("\r", "").Split('\n');
            int prox = 0;
            using (var doc = new PrintDocument { DocumentName = "Conferência de mandado" })
            using (var fonte = new Font("Segoe UI", 10f))
            using (var negrito = new Font("Segoe UI", 10f, FontStyle.Bold))
            {
                doc.BeginPrint += (s, e) => prox = 0;
                doc.PrintPage += (s, e) =>
                {
                    var area = e.MarginBounds; float y = area.Top;
                    var fmt = new StringFormat(StringFormatFlags.LineLimit);
                    while (prox < linhas.Length)
                    {
                        string l = linhas[prox];
                        var f = l.Length > 0 && (char.IsDigit(l[0]) || l.StartsWith("RESULTADO") || l.StartsWith("REGISTRO")) ? negrito : fonte;
                        var tam = e.Graphics.MeasureString(l.Length == 0 ? " " : l, f, area.Width, fmt);
                        if (y + tam.Height > area.Bottom && y > area.Top) { e.HasMorePages = true; return; }
                        e.Graphics.DrawString(l, f, Brushes.Black, new RectangleF(area.Left, y, area.Width, tam.Height + 2), fmt);
                        y += tam.Height; prox++;
                    }
                    e.HasMorePages = false;
                };
                using (var dlg = new PrintDialog { Document = doc, UseEXDialog = true })
                    if (dlg.ShowDialog(dono) == DialogResult.OK)
                    {
                        try { doc.Print(); }
                        catch (Exception ex) { MessageBox.Show(dono, "Não foi possível imprimir: " + ex.Message, "Imprimir", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                    }
            }
        }
    }

    // Mostra todo o texto lido, página por página
    class JanelaTexto : Form
    {
        public JanelaTexto(List<Pagina> paginas, bool multi)
        {
            Text = "Texto lido do documento";
            Font = new Font("Segoe UI", 10f);
            Size = new Size(Escala.D(860), Escala.D(700));
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            var sb = new StringBuilder();
            foreach (var p in paginas)
            {
                sb.Append("──────── ").Append(multi ? p.Arquivo + " · " : "").Append(p.EhImagem ? "imagem" : "página " + p.Numero + " de " + p.Total)
                  .Append(p.LidaPorOcr ? " (lida por OCR)" : p.Escaneada ? " (escaneada, sem texto lido)" : "").Append(" ────────\r\n");
                sb.Append((p.Texto ?? "").Replace("\n", "\r\n")).Append("\r\n\r\n");
            }
            var tb = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = true, Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10f), BackColor = Cores.Papel, Text = sb.ToString(), BorderStyle = BorderStyle.None
            };
            Controls.Add(tb);
            Shown += (s, e) => { tb.SelectionStart = 0; tb.SelectionLength = 0; };
        }
    }
}
