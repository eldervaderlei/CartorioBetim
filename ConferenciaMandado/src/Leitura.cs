using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace ConferenciaMandado
{
    public class Pagina
    {
        public string Arquivo;      // nome exibido do arquivo
        public string Caminho;      // caminho no disco
        public bool EhImagem;
        public int Numero, Total;   // página n de total, dentro do arquivo
        public string Texto = "";
        public bool Escaneada;      // sem texto digital: precisa de OCR
        public bool LidaPorOcr;
        public bool OcrTentado;
        public int Inicio;          // onde o texto desta página começa no texto juntado
        public List<Glifo> Geo = new List<Glifo>(); // posição de cada letra/palavra, para o PDF grifado
    }

    // Trecho [Inicio, Fim) do texto da página e o retângulo onde ele está.
    // Fracao = false: coordenadas do PDF (pontos). Fracao = true: frações da página como ela aparece (0..1, origem no canto superior esquerdo), vindas do OCR.
    public class Glifo
    {
        public int Inicio, Fim;
        public double X0, Y0, X1, Y1;
        public bool Fracao;
    }

    public static class Leitura
    {
        public static readonly string[] ExtImagem = { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".gif" };
        public static bool EhPdf(string p) => string.Equals(Path.GetExtension(p), ".pdf", StringComparison.OrdinalIgnoreCase);
        public static bool EhImagem(string p) => ExtImagem.Contains(Path.GetExtension(p).ToLowerInvariant());
        public static bool Aceita(string p) => EhPdf(p) || EhImagem(p);

        // rodapés automáticos do PJe/eproc não contam como "texto" da página
        public static readonly Regex Boil = new Regex(@"(N[úu]mero do documento:.*|https?://\S+|Assinado eletronicamente por:.*|Num\.\s*\d+\s*-\s*P[áa]g\.\s*\S+|Documento assinado eletronicamente.*|Digitalizado com.*)");
        static readonly Regex Espacos = new Regex(@"\s");

        public static int LetrasUteis(string t) => Espacos.Replace(Boil.Replace(t ?? "", ""), "").Length;

        // Lê o texto digital de cada página do PDF (o que vem do PJe / WhatsApp)
        public static List<Pagina> LerPdf(string caminho, string nome)
        {
            var paginas = new List<Pagina>();
            using (var pdf = PdfDocument.Open(caminho))
            {
                int n = pdf.NumberOfPages;
                for (int i = 1; i <= n; i++)
                {
                    var pagina = new Pagina { Arquivo = nome, Caminho = caminho, Numero = i, Total = n };
                    try { TextoComPosicoes(pdf.GetPage(i), pagina); }
                    catch { pagina.Texto = ""; pagina.Geo.Clear(); }
                    pagina.Escaneada = LetrasUteis(pagina.Texto) < 60;
                    paginas.Add(pagina);
                }
            }
            return paginas;
        }

        // Monta o texto da página na ordem em que as letras aparecem no PDF (como o leitor da versão anterior),
        // guardando a posição de cada letra
        static void TextoComPosicoes(Page pg, Pagina pagina)
        {
            var sb = new StringBuilder();
            // a página como aparece (o PdfPig já entrega as letras na orientação de exibição)
            var cx = pg.CropBox.Bounds;
            double W = Math.Max(1, cx.Width), H = Math.Max(1, cx.Height);
            Letter ant = null; double antRx = 0, antRy = 0, antFim = 0;
            foreach (var l in pg.Letters)
            {
                string v = Limpar(l.Value);
                if (v.Length == 0) continue;
                double fs = l.PointSize > 0.5 ? l.PointSize : Math.Max(1, l.GlyphRectangle.Height);
                bool vazio = string.IsNullOrWhiteSpace(v);
                // eixo de leitura da letra (texto na horizontal, na vertical ou de cabeça para baixo)
                double dx = l.EndBaseLine.X - l.StartBaseLine.X, dy = l.EndBaseLine.Y - l.StartBaseLine.Y, len = Math.Sqrt(dx * dx + dy * dy);
                double c = 1, s = 0;
                if (len > 0.01) { c = dx / len; s = dy / len; }
                else if (ant != null) { double adx = ant.EndBaseLine.X - ant.StartBaseLine.X, ady = ant.EndBaseLine.Y - ant.StartBaseLine.Y, al = Math.Sqrt(adx * adx + ady * ady); if (al > 0.01) { c = adx / al; s = ady / al; } }
                double rx = l.StartBaseLine.X * c + l.StartBaseLine.Y * s, ry = -l.StartBaseLine.X * s + l.StartBaseLine.Y * c;
                double rxFim = l.EndBaseLine.X * c + l.EndBaseLine.Y * s;
                if (ant != null)
                {
                    double fsA = ant.PointSize > 0.5 ? ant.PointSize : fs;
                    // posição da letra anterior no mesmo eixo
                    double pRy = -ant.StartBaseLine.X * s + ant.StartBaseLine.Y * c, pFim = ant.EndBaseLine.X * c + ant.EndBaseLine.Y * s;
                    double salto = Math.Abs(ry - pRy), gap = rx - pFim;
                    if (salto > Math.Min(fs, fsA) * 0.5) { if (sb.Length > 0 && sb[sb.Length - 1] != '\n') { TirarEspacoFinal(sb); sb.Append('\n'); } }
                    else if ((gap > fs * 0.15 || gap < -fs * 1.5) && !vazio && sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1])) sb.Append(' ');
                }
                if (vazio) { if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1])) sb.Append(' '); }
                else
                {
                    int ini = sb.Length; sb.Append(v);
                    if (rxFim - rx < 0.1) rxFim = rx + fs * 0.5;
                    // caixa da letra no eixo de leitura, levada de volta para a página
                    double u0 = 1, v0 = 1, u1 = 0, v1 = 0;
                    foreach (var (px, py) in new[] { (rx, ry - fs * 0.22), (rxFim, ry - fs * 0.22), (rx, ry + fs * 0.8), (rxFim, ry + fs * 0.8) })
                    {
                        double x = px * c - py * s, y = px * s + py * c;
                        double u = (x - cx.Left) / W, vv = (cx.Top - y) / H;
                        u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, vv); v1 = Math.Max(v1, vv);
                    }
                    pagina.Geo.Add(new Glifo { Inicio = ini, Fim = sb.Length, X0 = u0, Y0 = v0, X1 = u1, Y1 = v1, Fracao = true });
                }
                ant = l;
            }
            pagina.Texto = sb.ToString();
        }

        static void TirarEspacoFinal(StringBuilder sb) { while (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--; }

        public static string Limpar(string t)
        {
            t = (t ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace('\f', '\n').Replace(' ', ' ').Replace("\0", "");
            // ligaduras que alguns PDFs usam
            return t.Replace("ﬁ", "fi").Replace("ﬂ", "fl").Replace("ﬀ", "ff").Replace("ﬃ", "ffi").Replace("ﬄ", "ffl");
        }

        // junta tudo num texto só (páginas separadas por \f), como na versão anterior
        public static string Juntar(IList<Pagina> paginas)
        {
            bool multi = paginas.Select(p => p.Arquivo).Distinct().Count() > 1;
            var sb = new StringBuilder(); string ant = null;
            for (int k = 0; k < paginas.Count; k++)
            {
                var p = paginas[k];
                if (k > 0) sb.Append('\f');
                if (multi && p.Arquivo != ant) sb.Append("===== ").Append(p.Arquivo).Append(" =====\n");
                p.Inicio = sb.Length;
                sb.Append(p.Texto); ant = p.Arquivo;
            }
            return sb.ToString();
        }
    }
}
