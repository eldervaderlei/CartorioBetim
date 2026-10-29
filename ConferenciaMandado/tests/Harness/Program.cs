using System; using System.IO; using System.Linq; using System.Diagnostics; using System.Collections.Generic; using ConferenciaMandado;
var TIPO = Environment.GetEnvironmentVariable("TIPO") ?? "divorcio";
// uso: Harness arquivo... (txt ou pdf)  |  Harness --grifar saida.pdf arquivo.pdf [mais.pdf]
if (args.Length > 0 && args[0] == "--grifar") {
  var pags = new List<Pagina>(); var nomes = args.Skip(2).Select(Path.GetFileName).ToList();
  foreach (var f in args.Skip(2)) pags.AddRange(Leitura.LerPdf(f, Path.GetFileName(f)));
  var r = Motor.Analisar(TIPO, Leitura.Juntar(pags));
  var d = Grifo.Destaques(r, pags, nomes[0]);
  Console.WriteLine("marcas=" + r.Marcas.Count + " destaques=" + d.Sum(x => x.Value.Count));
  File.WriteAllBytes(args[1], Grifo.Grifar(File.ReadAllBytes(args[2]), d));
  return;
}
foreach (var f in args) {
  string txt = f.EndsWith(".pdf") ? Leitura.Juntar(Leitura.LerPdf(f, Path.GetFileName(f))) : File.ReadAllText(f);
  if (f.EndsWith(".pdf")) { var ps = Leitura.LerPdf(f, "x"); Console.Error.WriteLine($"# {f}: {ps.Count} pág, escaneadas: {ps.Count(p=>p.Escaneada)}"); }
  var sw = Stopwatch.StartNew(); var r = Motor.Analisar(TIPO, txt);
  Console.WriteLine($"=== {Path.GetFileName(f)} ({sw.ElapsedMilliseconds} ms)");
  foreach (var it in r.Itens.Append(r.Justica).Where(i => i.Status != "nao" || i.Chave != "justica" || TIPO == "divorcio")) { Console.WriteLine($"{it.Chave}|{it.Status}|{it.Resumo}|pgs={string.Join(",",it.Paginas)}"); foreach (var t in it.Trechos) Console.WriteLine($"   [{t.Pagina}] {t.Antes}<<{t.Achado}>>{t.Depois}"); }
  foreach (var e in r.Extras) Console.WriteLine($"extra|{e.Rotulo}|{e.Achou}|{e.Valor}");
  Console.WriteLine($"dados|{string.Join(" / ", r.Partes)}|{string.Join(" / ", r.NomesSolteiro)}|{r.Processo}|{r.Livro}|{r.Folha}|{r.Termo}|{r.Cartorio}|{r.Quem}|{r.Bens}");
}
