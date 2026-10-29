using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Jint;

namespace ConferenciaMandado
{
    // Motor de análise: o mesmo motor de regras da versão em HTML (motor.js),
    // executado dentro do programa pelo interpretador Jint. Nada sai do computador.
    public static class Motor
    {
        static Engine _js;
        static readonly object _trava = new object();

        const string Adaptador = @"
function resumoApp(raw) {
  const r = analisar(raw);
  const sn = it => ({ key: it.key, label: it.label, status: it.status, summary: it.summary, note: it.note || '',
    pages: it.pages || [], snippets: (it.snippets || []).map(s => ({ b: s.before, m: s.match, a: s.after, p: s.page })) });
  return { itens: r.itens.map(sn), justica: sn(r.justica),
    extras: r.extras.map(e => ({ label: e.label, found: !!e.found, value: e.value || '' })), dados: r.dados };
}";

        static Engine Js()
        {
            if (_js != null) return _js;
            string fonte;
            using (var s = typeof(Motor).Assembly.GetManifestResourceStream("motor.js"))
            using (var r = new StreamReader(s)) fonte = r.ReadToEnd();
            var e = new Engine(o => o.LimitRecursion(4000));
            e.Execute(fonte + "\n" + Adaptador);
            return _js = e;
        }

        public static Resultado Analisar(string texto)
        {
            IDictionary<string, object> d;
            lock (_trava) d = (IDictionary<string, object>)Js().Invoke("resumoApp", texto).ToObject();
            var res = new Resultado
            {
                Itens = ((IEnumerable)d["itens"]).Cast<IDictionary<string, object>>().Select(Item.De).ToList(),
                Justica = Item.De((IDictionary<string, object>)d["justica"]),
                Extras = ((IEnumerable)d["extras"]).Cast<IDictionary<string, object>>()
                    .Select(x => new Extra { Rotulo = Str(x, "label"), Achou = x["found"] is bool b && b, Valor = Str(x, "value") }).ToList(),
            };
            var dd = (IDictionary<string, object>)d["dados"];
            res.Partes = Lista(dd, "partes");
            res.NomesSolteiro = Lista(dd, "nomes");
            res.Processo = Str(dd, "processo");
            res.Livro = Str(dd, "livro"); res.Folha = Str(dd, "folha"); res.Termo = Str(dd, "termo");
            res.Cartorio = Str(dd, "cartorio");
            res.Quem = Str(dd, "quem");
            res.Bens = Str(dd, "bens");
            return res;
        }

        internal static string Str(IDictionary<string, object> d, string k) => d.TryGetValue(k, out var v) && v != null ? Convert.ToString(v) : "";
        static List<string> Lista(IDictionary<string, object> d, string k) =>
            d.TryGetValue(k, out var v) && v is IEnumerable e && !(v is string) ? e.Cast<object>().Select(Convert.ToString).ToList() : new List<string>();
    }

    public class Resultado
    {
        public List<Item> Itens;
        public Item Justica;
        public List<Extra> Extras;
        public List<string> Partes, NomesSolteiro;
        public string Processo, Livro, Folha, Termo, Cartorio, Quem, Bens;
    }

    public class Item
    {
        public string Chave, Rotulo, Status, Resumo, Nota;
        public List<int> Paginas = new List<int>();
        public List<Trecho> Trechos = new List<Trecho>();
        public bool ConferidoManual;

        // ok / warn / missing (itens) — sim / pedida / indeferida / nao (justiça gratuita)
        public string StatusEfetivo => Status != "ok" && ConferidoManual ? "confirmed" : Status;

        internal static Item De(IDictionary<string, object> d)
        {
            var it = new Item { Chave = Motor.Str(d, "key"), Rotulo = Motor.Str(d, "label"), Status = Motor.Str(d, "status"), Resumo = Motor.Str(d, "summary"), Nota = Motor.Str(d, "note") };
            if (d.TryGetValue("pages", out var p) && p is IEnumerable pe) it.Paginas = pe.Cast<object>().Select(Convert.ToInt32).ToList();
            if (d.TryGetValue("snippets", out var s) && s is IEnumerable se)
                foreach (IDictionary<string, object> x in se)
                    it.Trechos.Add(new Trecho { Antes = Motor.Str(x, "b"), Achado = Motor.Str(x, "m"), Depois = Motor.Str(x, "a"), Pagina = Convert.ToInt32(x["p"]) });
            return it;
        }
    }

    public class Trecho { public string Antes, Achado, Depois; public int Pagina; }
    public class Extra { public string Rotulo, Valor; public bool Achou; }
}
