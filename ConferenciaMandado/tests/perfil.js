const fs = require('fs'), vm = require('vm');
const ctx = { console }; vm.createContext(ctx);
vm.runInContext(fs.readFileSync('../src/Engine/divorcio.js', 'utf8') + '\n;\n' + fs.readFileSync('../src/Engine/interdicao.js', 'utf8') + '\n;this.T={prepare,segmentar,intSentenca,intDecreto,intMandado,papeis,qualificacoes,intQualificacao,checkTransito,checkJustica,conferir};', ctx);
const T = ctx.T, raw = fs.readFileSync(process.argv[2], 'utf8').replace(/\n<<<<<<<<<< PAGINA >>>>>>>>>>\n/g, '\f');
const tm = (n, f) => { const t = Date.now(); const r = f(); console.log(n, Date.now() - t, 'ms'); return r; };
const doc = tm('prepare', () => T.prepare(raw)); tm('segmentar', () => T.segmentar(doc));
tm('sentenca', () => T.intSentenca(doc)); tm('decreto', () => T.intDecreto(doc)); tm('mandado', () => T.intMandado(doc));
const pp = tm('papeis', () => T.papeis(doc)); console.log(pp.curador && pp.curador.v, '|', pp.interditado && pp.interditado.v);
tm('qual', () => T.intQualificacao(doc, 'curador', pp.curador));
for (const papel of ['curador', 'interditado']) { const q = T.intQualificacao(doc, papel, pp[papel]); console.log('  ' + papel + ': ' + q.status + ' ' + q.summary); }
