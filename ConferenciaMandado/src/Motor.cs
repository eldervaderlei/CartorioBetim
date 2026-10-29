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
        // Tipos de conferência: cada um tem seu arquivo de regras em Engine/<chave>.js
        public static readonly List<TipoConferencia> Tipos = new List<TipoConferencia>
        {
            new TipoConferencia { Chave = "divorcio", Nome = "Divórcio", Itens = new[] { "força de mandado", "nome que a parte voltará a assinar", "divisão de bens", "trânsito em julgado" } },
            new TipoConferencia { Chave = "interdicao", Nome = "Interdição (curatela)", Itens = new[] { "sentença", "decreta a interdição", "força de mandado ou mandado do juiz", "termo de compromisso", "qualificação do curador", "qualificação do interditado", "trânsito em julgado", "justiça gratuita", "assinatura eletrônica" } },
        };

        static readonly Dictionary<string, Engine> _js = new Dictionary<string, Engine>();
        static readonly object _trava = new object();

        const string Adaptador = @"
function resumoApp(raw, ctx) {
  const r = typeof conferir === 'function' ? conferir(raw, ctx) : analisar(raw);
  const sn = it => ({ key: it.key, label: it.label, status: it.status, summary: it.summary, note: it.note || '',
    pages: it.pages || [], snippets: (it.snippets || []).map(s => ({ b: s.before, m: s.match, a: s.after, p: s.page })) });
  return { itens: r.itens.map(sn), justica: r.justica ? sn(r.justica) : null,
    extras: (r.extras || []).map(e => ({ label: e.label, found: !!e.found, value: e.value || '' })), dados: r.dados || {},
    marcas: (r.marcas || []).map(m => ({ c: m.cat, a: m.a, b: m.b, p: m.page })) };
}";

        static Engine Js(string tipo)
        {
            if (_js.TryGetValue(tipo, out var e)) return e;
            // divorcio.js tem as funções básicas usadas por todos os tipos
            string fonte = Recurso("divorcio.js");
            if (tipo != "divorcio") fonte += "\n;\n" + Recurso(tipo + ".js");
            e = new Engine(o => o.LimitRecursion(4000));
            e.Execute(fonte + "\n" + Adaptador);
            return _js[tipo] = e;
        }

        static string Recurso(string nome)
        {
            using (var s = typeof(Motor).Assembly.GetManifestResourceStream(nome))
            {
                if (s == null) throw new InvalidOperationException("Regras não encontradas: " + nome);
                using (var r = new StreamReader(s)) return r.ReadToEnd();
            }
        }

        // digital = o documento tem texto digital (não é só escaneado)
        public static Resultado Analisar(string tipo, string texto, bool digital = true)
        {
            IDictionary<string, object> d;
            lock (_trava)
            {
                var js = Js(tipo);
                var ctx = new Jint.Native.JsObject(js);
                ctx.Set("digital", digital);
                d = (IDictionary<string, object>)js.Invoke("resumoApp", texto, ctx).ToObject();
            }
            var res = new Resultado
            {
                Tipo = tipo,
                Itens = ((IEnumerable)d["itens"]).Cast<IDictionary<string, object>>().Select(Item.De).ToList(),
                Justica = d["justica"] is IDictionary<string, object> jd ? Item.De(jd) : new Item { Chave = "justica", Status = "nao" },
                Extras = ((IEnumerable)d["extras"]).Cast<IDictionary<string, object>>()
                    .Select(x => new Extra { Rotulo = Str(x, "label"), Achou = x["found"] is bool b && b, Valor = Str(x, "value") }).ToList(),
            };
            res.Marcas = ((IEnumerable)d["marcas"]).Cast<IDictionary<string, object>>()
                .Select(m => new Marca { Categoria = Str(m, "c"), Inicio = Convert.ToInt32(m["a"]), Fim = Convert.ToInt32(m["b"]), Pagina = Convert.ToInt32(m["p"]) }).ToList();
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

    public class TipoConferencia { public string Chave, Nome; public string[] Itens; }

    public class Resultado
    {
        public string Tipo;
        public List<Item> Itens;
        public Item Justica;
        public List<Extra> Extras;
        public List<Marca> Marcas;
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

    // trecho a grifar no PDF: posições [Inicio, Fim) no texto juntado
    public class Marca { public string Categoria; public int Inicio, Fim, Pagina; }

    public class Trecho { public string Antes, Achado, Depois; public int Pagina; }
    public class Extra { public string Rotulo, Valor; public bool Achou; }
}
