using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.CrossReference;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Writer;

namespace ConferenciaMandado
{
    // Gera uma cópia do PDF original com os trechos encontrados grifados.
    // Os grifos são marcações de destaque (anotações "Highlight") acrescentadas ao final do arquivo
    // (atualização incremental): o conteúdo original não é alterado nem perde qualidade.
    public static class Grifo
    {
        public class Categoria { public string Nome; public double R, G, B; }

        // mesmas cores da versão anterior
        public static readonly Dictionary<string, Categoria> Categorias = new Dictionary<string, Categoria>
        {
            { "mandado", new Categoria { Nome = "Força de mandado", R = 1, G = .86, B = .25 } },
            { "nome", new Categoria { Nome = "Nome", R = .55, G = .88, B = .55 } },
            { "partilha", new Categoria { Nome = "Divisão de bens", R = .6, G = .76, B = 1 } },
            { "transito", new Categoria { Nome = "Trânsito em julgado", R = 1, G = .7, B = .4 } },
            { "justica", new Categoria { Nome = "Justiça gratuita", R = .4, G = .84, B = .78 } },
            { "dados", new Categoria { Nome = "Partes, processo e registro", R = .8, G = .72, B = 1 } },
            // interdição
            { "sentenca", new Categoria { Nome = "Sentença", R = .85, G = .85, B = .85 } },
            { "decreto", new Categoria { Nome = "Decreta a interdição", R = 1, G = .6, B = .6 } },
            { "termo", new Categoria { Nome = "Termo de compromisso", R = .95, G = .75, B = .95 } },
            { "curador", new Categoria { Nome = "Curador", R = .55, G = .88, B = .55 } },
            { "interditado", new Categoria { Nome = "Interditado", R = .8, G = .72, B = 1 } },
            { "assinatura", new Categoria { Nome = "Assinatura eletrônica", R = .75, G = .9, B = .45 } },
        };

        public class Destaque
        {
            public string Categoria;
            public bool Fracao;
            public List<double[]> Linhas = new List<double[]>(); // x0, y0, x1, y1 (uma por linha de texto)
        }

        // Trechos a grifar em cada página (número da página no arquivo) de um arquivo
        public static Dictionary<int, List<Destaque>> Destaques(Resultado r, IList<Pagina> paginas, string arquivo)
        {
            var saida = new Dictionary<int, List<Destaque>>();
            if (r?.Marcas == null) return saida;
            foreach (var m in r.Marcas)
            {
                if (m.Pagina < 0 || m.Pagina >= paginas.Count) continue;
                var p = paginas[m.Pagina];
                if (p.Arquivo != arquivo) continue;
                int a = m.Inicio - p.Inicio, b = m.Fim - p.Inicio;
                var gl = p.Geo.Where(g => g.Fim > a && g.Inicio < b).OrderBy(g => g.Inicio).ToList();
                if (gl.Count == 0) continue;
                var d = new Destaque { Categoria = m.Categoria, Fracao = gl[0].Fracao };
                double[] atual = null;
                foreach (var g in gl)
                {
                    if (g.Fracao != d.Fracao) continue;
                    if (atual != null && MesmaLinha(atual, g))
                    { atual[0] = Math.Min(atual[0], g.X0); atual[1] = Math.Min(atual[1], g.Y0); atual[2] = Math.Max(atual[2], g.X1); atual[3] = Math.Max(atual[3], g.Y1); }
                    else { atual = new[] { g.X0, g.Y0, g.X1, g.Y1 }; d.Linhas.Add(atual); }
                }
                if (!saida.TryGetValue(p.Numero, out var lista)) saida[p.Numero] = lista = new List<Destaque>();
                lista.Add(d);
            }
            return saida;
        }

        static bool MesmaLinha(double[] r, Glifo g)
        {
            double h = Math.Min(r[3] - r[1], g.Y1 - g.Y0);
            double sobre = Math.Min(r[3], g.Y1) - Math.Max(r[1], g.Y0);
            return sobre > h * 0.4 && g.X0 > r[0] - h;
        }

        static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

        // fração da página como aparece na tela (0..1, origem em cima à esquerda) -> coordenadas do PDF
        static void ParaPdf(double u, double v, PdfRectangle c, int giro, out double x, out double y)
        {
            double W = c.Width, H = c.Height;
            switch (((giro % 360) + 360) % 360)
            {
                case 90: x = c.Left + v * W; y = c.Bottom + u * H; break;
                case 180: x = c.Right - u * W; y = c.Bottom + v * H; break;
                case 270: x = c.Right - v * W; y = c.Top - u * H; break;
                default: x = c.Left + u * W; y = c.Top - v * H; break;
            }
        }

        public static byte[] Grifar(byte[] original, Dictionary<int, List<Destaque>> porPagina)
        {
            using (var doc = PdfDocument.Open(original))
            {
                var st = doc.Structure;
                var trailer = st.CrossReferenceTable.Trailer;
                bool cifrado = trailer.EncryptionToken != null;
                var refs = new List<IndirectReference>();
                ColetarPaginas(st, st.Catalog.CatalogDictionary.Data.TryGetValue("Pages", out var raizPg) ? raizPg : null, refs, 0);

                var saida = new MemoryStream();
                saida.Write(original, 0, original.Length);
                if (original.Length > 0 && original[original.Length - 1] != '\n') saida.WriteByte((byte)'\n');
                long proximo = trailer.Size;
                var objetos = new SortedDictionary<long, KeyValuePair<long, int>>(); // número -> (posição, geração)

                void Objeto(long num, int ger, byte[] corpo)
                {
                    objetos[num] = new KeyValuePair<long, int>(saida.Position, ger);
                    var cab = Ascii(num + " " + ger + " obj\n"); saida.Write(cab, 0, cab.Length);
                    saida.Write(corpo, 0, corpo.Length);
                    var fim = Ascii("\nendobj\n"); saida.Write(fim, 0, fim.Length);
                }

                foreach (var kv in porPagina.OrderBy(k => k.Key))
                {
                    int n = kv.Key;
                    if (n < 1 || n > refs.Count || n > doc.NumberOfPages) continue;
                    var pagRef = refs[n - 1];
                    var dicPg = (DictionaryToken)st.GetObject(pagRef).Data;
                    var caixa = CaixaReal(st, dicPg);
                    int giro = Giro(st, dicPg);
                    var novas = new List<IToken>();
                    foreach (var d in kv.Value)
                    {
                        if (!Categorias.TryGetValue(d.Categoria, out var cat)) continue;
                        var quads = new List<double[]>();
                        foreach (var l in d.Linhas)
                        {
                            double x0 = l[0], y0 = l[1], x1 = l[2], y1 = l[3];
                            if (d.Fracao)
                            {
                                ParaPdf(l[0], l[1], caixa, giro, out var ax, out var ay);
                                ParaPdf(l[2], l[3], caixa, giro, out var bx, out var by);
                                x0 = Math.Min(ax, bx); x1 = Math.Max(ax, bx); y0 = Math.Min(ay, by); y1 = Math.Max(ay, by);
                            }
                            double folga = Math.Min(1.5, (y1 - y0) * 0.08);
                            quads.Add(new[] { x0 - folga, y0 - folga, x1 + folga, y1 + folga });
                        }
                        if (quads.Count == 0) continue;
                        double rx0 = quads.Min(q => q[0]), ry0 = quads.Min(q => q[1]), rx1 = quads.Max(q => q[2]), ry1 = quads.Max(q => q[3]);
                        string cor = F(cat.R) + " " + F(cat.G) + " " + F(cat.B);
                        string ap = "";
                        if (!cifrado)
                        {
                            // aparência própria: cor em modo "multiplicar", como caneta marca-texto
                            var sb = new StringBuilder("/G0 gs " + cor + " rg\n");
                            foreach (var q in quads) sb.Append(F(q[0]) + " " + F(q[1]) + " " + F(q[2] - q[0]) + " " + F(q[3] - q[1]) + " re\n");
                            sb.Append("f\n");
                            var conteudo = Ascii(sb.ToString());
                            long numAp = proximo++;
                            var corpo = new MemoryStream();
                            var dic = Ascii("<< /Type /XObject /Subtype /Form /BBox [" + F(rx0) + " " + F(ry0) + " " + F(rx1) + " " + F(ry1) + "] " +
                                "/Resources << /ExtGState << /G0 << /Type /ExtGState /BM /Multiply /ca 1 /CA 1 >> >> >> /Length " + conteudo.Length + " >>\nstream\n");
                            corpo.Write(dic, 0, dic.Length); corpo.Write(conteudo, 0, conteudo.Length);
                            var fimS = Ascii("\nendstream"); corpo.Write(fimS, 0, fimS.Length);
                            Objeto(numAp, 0, corpo.ToArray());
                            ap = " /AP << /N " + numAp + " 0 R >>";
                        }
                        var qp = string.Join(" ", quads.Select(q => F(q[0]) + " " + F(q[3]) + " " + F(q[2]) + " " + F(q[3]) + " " + F(q[0]) + " " + F(q[1]) + " " + F(q[2]) + " " + F(q[1])));
                        long numAn = proximo++;
                        Objeto(numAn, 0, Ascii("<< /Type /Annot /Subtype /Highlight /F 4 /Rect [" + F(rx0) + " " + F(ry0) + " " + F(rx1) + " " + F(ry1) + "] /QuadPoints [" + qp + "] /C [" + cor + "] /CA 1 /P " + pagRef.ObjectNumber + " " + pagRef.Generation + " R" + ap + " >>"));
                        novas.Add(new IndirectReferenceToken(new IndirectReference(numAn, 0)));
                    }
                    if (novas.Count == 0) continue;

                    // a página é regravada com a lista de anotações atualizada
                    var entradas = new Dictionary<NameToken, IToken>();
                    var anots = new List<IToken>();
                    foreach (var e in dicPg.Data)
                    {
                        if (e.Key == "Annots")
                        {
                            var v = e.Value is IndirectReferenceToken ir ? st.GetObject(ir.Data).Data : e.Value;
                            if (v is ArrayToken arr) anots.AddRange(arr.Data);
                        }
                        else entradas[NameToken.Create(e.Key)] = e.Value;
                    }
                    anots.AddRange(novas);
                    entradas[NameToken.Create("Annots")] = new ArrayToken(anots);
                    var ms = new MemoryStream();
                    TokenWriter.Instance.WriteToken(new DictionaryToken(entradas), ms);
                    Objeto(pagRef.ObjectNumber, pagRef.Generation, ms.ToArray());
                }

                if (objetos.Count == 0) return original;
                long anterior = UltimoStartXref(original);
                bool usaFluxo = st.CrossReferenceTable.Type == CrossReferenceType.Stream;

                var extra = new MemoryStream();
                void Tk(IToken t) => TokenWriter.Instance.WriteToken(t, extra);
                void Tx(string s) { var b = Ascii(s); extra.Write(b, 0, b.Length); }
                Tx(" /Root " + trailer.Root.ObjectNumber + " " + trailer.Root.Generation + " R");
                if (trailer.Info != null) { Tx(" /Info "); Tk(trailer.Info); }
                if (trailer.EncryptionToken != null) { Tx(" /Encrypt "); Tk(trailer.EncryptionToken); }
                if (trailer.Identifier != null && trailer.Identifier.Count > 0) { Tx(" /ID ["); foreach (var id in trailer.Identifier) { Tk((IToken)id); Tx(" "); } Tx("]"); }
                Tx(" /Prev " + anterior);
                string comuns = Encoding.ASCII.GetString(extra.ToArray());

                long inicioXref;
                if (!usaFluxo)
                {
                    inicioXref = saida.Position;
                    var sb = new StringBuilder("xref\n");
                    foreach (var grupo in Sequencias(objetos.Keys.ToList()))
                    {
                        sb.Append(grupo[0]).Append(' ').Append(grupo.Count).Append('\n');
                        foreach (var num in grupo) sb.Append(objetos[num].Key.ToString("D10")).Append(' ').Append(objetos[num].Value.ToString("D5")).Append(" n\r\n");
                    }
                    sb.Append("trailer\n<< /Size " + proximo + comuns + " >>\n");
                    var b = Ascii(sb.ToString()); saida.Write(b, 0, b.Length);
                }
                else
                {
                    long numX = proximo++;
                    inicioXref = saida.Position;
                    objetos[numX] = new KeyValuePair<long, int>(inicioXref, 0);
                    var dados = new MemoryStream();
                    var indice = new StringBuilder();
                    foreach (var grupo in Sequencias(objetos.Keys.ToList()))
                    {
                        indice.Append(grupo[0]).Append(' ').Append(grupo.Count).Append(' ');
                        foreach (var num in grupo)
                        {
                            long off = objetos[num].Key; int ger = objetos[num].Value;
                            dados.WriteByte(1);
                            dados.WriteByte((byte)(off >> 24)); dados.WriteByte((byte)(off >> 16)); dados.WriteByte((byte)(off >> 8)); dados.WriteByte((byte)off);
                            dados.WriteByte((byte)(ger >> 8)); dados.WriteByte((byte)ger);
                        }
                    }
                    var bin = dados.ToArray();
                    var cab = Ascii(numX + " 0 obj\n<< /Type /XRef /Size " + proximo + " /W [1 4 2] /Index [" + indice.ToString().Trim() + "]" + comuns + " /Length " + bin.Length + " >>\nstream\n");
                    saida.Write(cab, 0, cab.Length); saida.Write(bin, 0, bin.Length);
                    var fim = Ascii("\nendstream\nendobj\n"); saida.Write(fim, 0, fim.Length);
                }
                var final = Ascii("startxref\n" + inicioXref + "\n%%EOF\n");
                saida.Write(final, 0, final.Length);
                return saida.ToArray();
            }
        }

        static void ColetarPaginas(Structure st, IToken no, List<IndirectReference> refs, int prof)
        {
            if (no == null || prof > 64) return;
            IndirectReference? r = no is IndirectReferenceToken ir ? ir.Data : (IndirectReference?)null;
            var obj = r.HasValue ? st.GetObject(r.Value).Data : no;
            if (!(obj is DictionaryToken d)) return;
            if (d.Data.TryGetValue("Kids", out var kids))
            {
                var arr = (kids is IndirectReferenceToken kr ? st.GetObject(kr.Data).Data : kids) as ArrayToken;
                if (arr != null) foreach (var k in arr.Data) ColetarPaginas(st, k, refs, prof + 1);
            }
            else if (r.HasValue) refs.Add(r.Value);
        }

        // valor da página ou herdado das páginas-pai (/Parent)
        static IToken Herdado(Structure st, DictionaryToken d, string chave)
        {
            for (int i = 0; d != null && i < 64; i++)
            {
                if (d.Data.TryGetValue(chave, out var v)) return v is IndirectReferenceToken r ? st.GetObject(r.Data).Data : v;
                if (!d.Data.TryGetValue("Parent", out var p)) break;
                d = (p is IndirectReferenceToken pr ? st.GetObject(pr.Data).Data : p) as DictionaryToken;
            }
            return null;
        }

        static double Num(Structure st, IToken t) => (t is IndirectReferenceToken r ? st.GetObject(r.Data).Data : t) is NumericToken n ? n.Double : 0;

        // CropBox (ou MediaBox) original, sem o giro da página
        static PdfRectangle CaixaReal(Structure st, DictionaryToken pg)
        {
            var arr = (Herdado(st, pg, "CropBox") ?? Herdado(st, pg, "MediaBox")) as ArrayToken;
            if (arr == null || arr.Data.Count < 4) return new PdfRectangle(0, 0, 612, 792);
            double a = Num(st, arr.Data[0]), b = Num(st, arr.Data[1]), c = Num(st, arr.Data[2]), d = Num(st, arr.Data[3]);
            return new PdfRectangle(Math.Min(a, c), Math.Min(b, d), Math.Max(a, c), Math.Max(b, d));
        }

        static int Giro(Structure st, DictionaryToken pg) => (int)Math.Round(Num(st, Herdado(st, pg, "Rotate") ?? new NumericToken(0)));

        static IEnumerable<List<long>> Sequencias(List<long> nums)
        {
            var atual = new List<long>();
            foreach (var n in nums)
            {
                if (atual.Count > 0 && n != atual[atual.Count - 1] + 1) { yield return atual; atual = new List<long>(); }
                atual.Add(n);
            }
            if (atual.Count > 0) yield return atual;
        }

        static long UltimoStartXref(byte[] pdf)
        {
            int ini = Math.Max(0, pdf.Length - 4096);
            var cauda = Encoding.ASCII.GetString(pdf, ini, pdf.Length - ini);
            var ms = Regex.Matches(cauda, @"startxref\s+(\d+)");
            if (ms.Count == 0) throw new InvalidDataException("Não encontrei o índice (startxref) do PDF.");
            return long.Parse(ms[ms.Count - 1].Groups[1].Value);
        }

        // Junta vários PDFs num só (na ordem dada) e grifa cada parte nas páginas certas
        public static byte[] JuntarEGrifar(IList<KeyValuePair<byte[], Dictionary<int, List<Destaque>>>> partes)
        {
            var arquivos = partes.Select(p => p.Key).ToList();
            var todos = new Dictionary<int, List<Destaque>>();
            int desloc = 0;
            foreach (var p in partes)
            {
                int n;
                using (var d = PdfDocument.Open(p.Key)) n = d.NumberOfPages;
                foreach (var kv in p.Value) todos[kv.Key + desloc] = kv.Value;
                desloc += n;
            }
            var junto = PdfMerger.Merge(arquivos, null, PdfAStandard.None, null);
            return Grifar(junto, todos);
        }

        // Foto/escaneamento em imagem: vira um PDF de uma página para poder ser grifado
        public static byte[] ImagemParaPdf(byte[] imagem, bool png, int larguraPx, int alturaPx)
        {
            double w = 595, h = 595.0 * alturaPx / Math.Max(1, larguraPx);
            using (var b = new PdfDocumentBuilder())
            {
                var pg = b.AddPage(w, h);
                var area = new PdfRectangle(0, 0, w, h);
                if (png) pg.AddPng(imagem, area); else pg.AddJpeg(imagem, area);
                return b.Build();
            }
        }
    }
}
