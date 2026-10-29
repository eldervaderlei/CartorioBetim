using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
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
                    string t = "";
                    try
                    {
                        var pg = pdf.GetPage(i);
                        try { t = ContentOrderTextExtractor.GetText(pg); }
                        catch { t = pg.Text; }
                    }
                    catch { t = ""; }
                    t = Limpar(t);
                    paginas.Add(new Pagina { Arquivo = nome, Caminho = caminho, Numero = i, Total = n, Texto = t, Escaneada = LetrasUteis(t) < 60 });
                }
            }
            return paginas;
        }

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
                sb.Append(p.Texto); ant = p.Arquivo;
            }
            return sb.ToString();
        }
    }
}
