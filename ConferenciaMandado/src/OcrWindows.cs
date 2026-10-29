using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ConferenciaMandado
{
    // OCR que já vem no Windows 10/11 (Windows.Media.Ocr) e renderização de PDF do próprio
    // Windows (Windows.Data.Pdf). Nada é instalado e nada sai do computador.
    public static class OcrWindows
    {
        static OcrEngine _motor;
        public static string Idioma { get; private set; } = "";
        public static bool EmPortugues => Idioma.StartsWith("pt", StringComparison.OrdinalIgnoreCase);

        public static OcrEngine Motor()
        {
            if (_motor != null) return _motor;
            foreach (var tag in new[] { "pt-BR", "pt-PT", "pt" })
            {
                try
                {
                    var l = new Language(tag);
                    if (OcrEngine.IsLanguageSupported(l)) { _motor = OcrEngine.TryCreateFromLanguage(l); if (_motor != null) break; }
                }
                catch { }
            }
            if (_motor == null) _motor = OcrEngine.TryCreateFromUserProfileLanguages();
            if (_motor == null)
            {
                var l = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
                if (l != null) _motor = OcrEngine.TryCreateFromLanguage(l);
            }
            if (_motor == null) throw new InvalidOperationException("O reconhecimento de texto (OCR) do Windows não está disponível neste computador.");
            Idioma = _motor.RecognizerLanguage.LanguageTag;
            return _motor;
        }

        static async Task<IRandomAccessStream> Fluxo(string caminho)
        {
            var mem = new InMemoryRandomAccessStream();
            using (var w = new DataWriter(mem.GetOutputStreamAt(0)))
            {
                w.WriteBytes(File.ReadAllBytes(caminho));
                await w.StoreAsync();
                await w.FlushAsync();
                w.DetachStream();
            }
            mem.Seek(0);
            return mem;
        }

        // Lê por OCR as páginas indicadas (números a partir de 1) de um PDF
        public static async Task LerPaginasPdf(string caminho, Pagina[] paginas, Action<Pagina> aoTerminarPagina)
        {
            var motor = Motor();
            using (var fluxo = await Fluxo(caminho))
            {
                var doc = await PdfDocument.LoadFromStreamAsync(fluxo);
                foreach (var p in paginas)
                {
                    try { await LerPagina(doc, p, motor); p.LidaPorOcr = true; }
                    catch { /* uma página com problema não impede a leitura das outras */ }
                    aoTerminarPagina?.Invoke(p);
                }
            }
        }

        static async Task LerPagina(PdfDocument doc, Pagina p, OcrEngine motor)
        {
            using (var pg = doc.GetPage((uint)(p.Numero - 1)))
            {
                // ~300 dpi, respeitando o tamanho máximo que o OCR aceita
                double escala = 300.0 / 96.0;
                double w = pg.Size.Width * escala, h = pg.Size.Height * escala;
                double max = OcrEngine.MaxImageDimension;
                if (w > max || h > max) { double k = max / Math.Max(w, h); w *= k; h *= k; }
                var opc = new PdfPageRenderOptions { DestinationWidth = (uint)Math.Max(1, w), DestinationHeight = (uint)Math.Max(1, h) };
                using (var img = new InMemoryRandomAccessStream())
                {
                    await pg.RenderToStreamAsync(img, opc);
                    p.Texto = Juntar(p.Texto, await ReconhecerImagem(img, motor));
                }
            }
        }

        public static async Task LerImagem(Pagina p)
        {
            var motor = Motor();
            using (var fluxo = await Fluxo(p.Caminho))
                p.Texto = await ReconhecerImagem(fluxo, motor);
            p.LidaPorOcr = true;
        }

        static string Juntar(string digital, string ocr) => string.IsNullOrWhiteSpace(digital) ? ocr : digital + "\n" + ocr;

        static async Task<string> ReconhecerImagem(IRandomAccessStream img, OcrEngine motor)
        {
            img.Seek(0);
            var dec = await BitmapDecoder.CreateAsync(img);
            string melhor = ""; int melhorLetras = -1;
            // página escaneada de lado ou de cabeça para baixo: tenta girar se quase nada for lido
            foreach (var giro in new[] { BitmapRotation.None, BitmapRotation.Clockwise90Degrees, BitmapRotation.Clockwise270Degrees, BitmapRotation.Clockwise180Degrees })
            {
                string t = await Reconhecer(dec, giro, motor);
                int letras = t.Count(char.IsLetter);
                if (letras > melhorLetras) { melhor = t; melhorLetras = letras; }
                if (letras >= 200 && PareceTexto(t)) break;
            }
            return melhor;
        }

        // texto de verdade tem muitas palavras comuns do português; lixo de página girada não
        static bool PareceTexto(string t)
        {
            var palavras = t.ToLowerInvariant().Split(new[] { ' ', '\n', ',', '.', ';', ':' }, StringSplitOptions.RemoveEmptyEntries);
            int comuns = palavras.Count(w => w == "de" || w == "da" || w == "do" || w == "e" || w == "a" || w == "o" || w == "que" || w == "em" || w == "os" || w == "as" || w == "para" || w == "com");
            return palavras.Length > 0 && comuns * 12 >= palavras.Length;
        }

        static async Task<string> Reconhecer(BitmapDecoder dec, BitmapRotation giro, OcrEngine motor)
        {
            // a escala é aplicada antes do giro, então vale para a imagem como está no arquivo
            uint w = dec.PixelWidth, h = dec.PixelHeight;
            double max = OcrEngine.MaxImageDimension, k = 1;
            if (Math.Max(w, h) > max) k = max / Math.Max(w, h);
            else if (Math.Max(w, h) < 1600) k = Math.Min(2.5, 1600.0 / Math.Max(1, Math.Max(w, h))); // foto pequena: amplia um pouco
            var tr = new BitmapTransform { Rotation = giro, ScaledWidth = (uint)Math.Max(1, w * k), ScaledHeight = (uint)Math.Max(1, h * k), InterpolationMode = BitmapInterpolationMode.Fant };
            using (var bmp = await dec.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, tr, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage))
            {
                var r = await motor.RecognizeAsync(bmp);
                var sb = new StringBuilder();
                foreach (var linha in r.Lines) sb.Append(linha.Text).Append('\n');
                return sb.ToString();
            }
        }
    }
}
