// roda a conferência de interdição direto no Node, sobre um texto já extraído
const fs = require('fs'), vm = require('vm');
const ctx = { console }; vm.createContext(ctx);
vm.runInContext(fs.readFileSync('../src/Engine/divorcio.js', 'utf8') + '\n;\n' + fs.readFileSync('../src/Engine/interdicao.js', 'utf8') + '\n;this.conferir=conferir;', ctx);
for (const f of process.argv.slice(2)) {
  const raw = fs.readFileSync(f, 'utf8').replace(/\n<<<<<<<<<< PAGINA >>>>>>>>>>\n/g, '\f');
  const r = ctx.conferir(raw, { digital: true });
  console.log('## ' + require('path').basename(f));
  for (const it of r.itens) if (['motivo', 'termo'].includes(it.key)) console.log('  ' + it.key + ': ' + it.status + ' — ' + it.summary);
}
