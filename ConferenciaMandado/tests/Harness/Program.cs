using System; using System.IO; using System.Linq; using System.Diagnostics; using ConferenciaMandado;
foreach (var f in args) {
  string txt = f.EndsWith(".pdf") ? Leitura.Juntar(Leitura.LerPdf(f, Path.GetFileName(f))) : File.ReadAllText(f);
  if (f.EndsWith(".pdf")) { var ps = Leitura.LerPdf(f, "x"); Console.Error.WriteLine($"# {f}: {ps.Count} pág, escaneadas: {ps.Count(p=>p.Escaneada)}"); }
  var sw = Stopwatch.StartNew(); var r = Motor.Analisar(txt);
  Console.WriteLine($"=== {Path.GetFileName(f)} ({sw.ElapsedMilliseconds} ms)");
  foreach (var it in r.Itens.Append(r.Justica)) { Console.WriteLine($"{it.Chave}|{it.Status}|{it.Resumo}|pgs={string.Join(",",it.Paginas)}"); foreach (var t in it.Trechos) Console.WriteLine($"   [{t.Pagina}] {t.Antes}<<{t.Achado}>>{t.Depois}"); }
  foreach (var e in r.Extras) Console.WriteLine($"extra|{e.Rotulo}|{e.Achou}|{e.Valor}");
  Console.WriteLine($"dados|{string.Join(" / ", r.Partes)}|{string.Join(" / ", r.NomesSolteiro)}|{r.Processo}|{r.Livro}|{r.Folha}|{r.Termo}|{r.Cartorio}|{r.Quem}|{r.Bens}");
}
