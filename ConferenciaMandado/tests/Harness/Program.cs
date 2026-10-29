using System; using System.IO; using System.Linq; using System.Diagnostics; using System.Collections.Generic; using ConferenciaMandado;
var TIPO = Environment.GetEnvironmentVariable("TIPO") ?? "divorcio";
// uso: Harness arquivo... (txt ou pdf)  |  Harness --grifar saida.pdf arquivo.pdf [mais.pdf]
// Harness --caso pasta [saida-texto.txt]: confere todos os PDFs da pasta juntos; páginas escaneadas passam pelo tesseract (só no teste)
if (args.Length > 0 && args[0] == "--caso") {
  var dir = args[1]; var pags = new List<Pagina>();
  var cacheDir = Path.Combine(Path.GetTempPath(), "ocr-cache"); Directory.CreateDirectory(cacheDir);
  foreach (var f in Directory.GetFiles(dir).Where(Leitura.EhPdf).OrderBy(x => x)) {
    var ps = Leitura.LerPdf(f, Path.GetFileName(f));
    Console.WriteLine($"# {Path.GetFileName(f)}: {ps.Count} pág, escaneadas: {ps.Count(p => p.Escaneada)}");
    foreach (var p in ps.Where(p => p.Escaneada)) {
      var key = Path.Combine(cacheDir, Math.Abs((f + p.Numero).GetHashCode()) + "-" + new FileInfo(f).Length + ".txt");
      if (!File.Exists(key)) {
        var png = Path.Combine(cacheDir, "pg");
        Process.Start(new ProcessStartInfo("pdftoppm", $"-r 200 -gray -png -singlefile -f {p.Numero} -l {p.Numero} \"{f}\" \"{png}\"") { UseShellExecute = false })!.WaitForExit();
        Process.Start(new ProcessStartInfo("tesseract", $"\"{png}.png\" \"{key[..^4]}\" -l por") { UseShellExecute = false, RedirectStandardError = true })!.WaitForExit();
      }
      var t = File.ReadAllText(key); p.Texto = string.IsNullOrWhiteSpace(p.Texto) ? t : p.Texto + "\n" + t; p.LidaPorOcr = true;
    }
    pags.AddRange(ps);
  }
  var txt = Leitura.Juntar(pags);
  if (args.Length > 2) File.WriteAllText(args[2], txt.Replace("\f", "\n<<<<<<<<<< PAGINA >>>>>>>>>>\n"));
  var sw = Stopwatch.StartNew();
  var r = Motor.Analisar(TIPO, txt, pags.Any(p => !p.Escaneada));
  Console.WriteLine($"=== ({sw.ElapsedMilliseconds} ms)");
  foreach (var it in r.Itens) { Console.WriteLine($"{it.Chave}|{it.Status}|{it.Resumo}"); foreach (var t in it.Trechos.Take(1)) Console.WriteLine($"   [{pags[Math.Min(t.Pagina, pags.Count-1)].Arquivo} p{pags[Math.Min(t.Pagina, pags.Count-1)].Numero}] {t.Antes}<<{t.Achado}>>{t.Depois}"); }
  foreach (var e in r.Extras) Console.WriteLine($"extra|{e.Rotulo}|{e.Valor}");
  return;
}
// Harness --juntar saida.pdf a.pdf b.pdf ...: confere tudo junto e gera um PDF único grifado
if (args.Length > 0 && args[0] == "--juntar") {
  var arqs = args.Skip(2).ToList(); var pags = new List<Pagina>();
  foreach (var f in arqs) pags.AddRange(Leitura.LerPdf(f, Path.GetFileName(f)));
  var r = Motor.Analisar(TIPO, Leitura.Juntar(pags));
  var partes = arqs.Select(f => new KeyValuePair<byte[], Dictionary<int, List<Grifo.Destaque>>>(File.ReadAllBytes(f), Grifo.Destaques(r, pags, Path.GetFileName(f)))).ToList();
  Console.WriteLine("destaques por arquivo: " + string.Join(", ", partes.Select(p => p.Value.Sum(x => x.Value.Count))));
  File.WriteAllBytes(args[1], Grifo.JuntarEGrifar(partes));
  return;
}
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
