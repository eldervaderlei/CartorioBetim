using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace ConferenciaMandado
{
    static class Program
    {
        // As bibliotecas (leitor de PDF e motor de regras) vão dentro do próprio .exe:
        // basta copiar o arquivo e abrir com dois cliques.
        static readonly Dictionary<string, Assembly> Carregadas = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        [STAThread]
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += Resolver;
            Iniciar(args);
        }

        static Assembly Resolver(object s, ResolveEventArgs e)
        {
            var nome = new AssemblyName(e.Name).Name;
            lock (Carregadas)
            {
                if (Carregadas.TryGetValue(nome, out var a)) return a;
                using (var st = typeof(Program).Assembly.GetManifestResourceStream("lib." + nome + ".dll"))
                {
                    if (st == null) return null;
                    var ms = new MemoryStream(); st.CopyTo(ms);
                    a = Assembly.Load(ms.ToArray());
                    Carregadas[nome] = a;
                    return a;
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Iniciar(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Erro(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Erro(e.ExceptionObject as Exception);
            Application.Run(new JanelaPrincipal(args));
        }

        static void Erro(Exception ex)
        {
            MessageBox.Show("Ocorreu um erro inesperado:\n\n" + (ex?.Message ?? "desconhecido"), "Conferência de Mandado", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
