using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ConferenciaMandado
{
    static class Cores
    {
        public static readonly Color Fundo = Hex("#f4f1ea"), Papel = Hex("#fffdf8"), Tinta = Hex("#1f2a2e"), Apagado = Hex("#5f6b6e"),
            Linha = Hex("#ddd6c8"), Suave = Hex("#f6f2e9"), Destaque = Hex("#1d4e5f"), Destaque2 = Hex("#2b6a7e"),
            Ok = Hex("#21734a"), OkFundo = Hex("#e6f3ea"), Aviso = Hex("#9a6400"), AvisoFundo = Hex("#fbf0d9"),
            Ruim = Hex("#a3302a"), RuimFundo = Hex("#f8e3e0"), Conferido = Hex("#2b6a7e"), ConferidoFundo = Hex("#e3eff2"),
            Marca = Hex("#ffe58a");

        public static Color Hex(string h) => ColorTranslator.FromHtml(h);

        public static readonly Color Neutro = Hex("#7a8487"), NeutroFundo = Hex("#eceae4");

        public static Color DoStatus(string st) => st == "ok" || st == "sim" ? Ok : st == "confirmed" ? Conferido : st == "missing" ? Ruim : st == "na" ? Neutro : Aviso;
        public static Color FundoDoStatus(string st) => st == "ok" || st == "sim" ? OkFundo : st == "confirmed" ? ConferidoFundo : st == "missing" ? RuimFundo : st == "na" ? NeutroFundo : AvisoFundo;
    }

    static class Escala
    {
        public static float K = 1f;
        public static int D(int px) => (int)Math.Round(px * K);
    }

    // Empilha os controles de cima para baixo, ocupando toda a largura, e ajusta a própria altura.
    class Pilha : Panel
    {
        public int Espaco = 8;
        public Pilha() { DoubleBuffered = true; }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int y = Padding.Top, w = Math.Max(10, ClientSize.Width - Padding.Horizontal);
            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                int cw = w - c.Margin.Horizontal;
                int h = Altura(c, cw);
                c.SetBounds(Padding.Left + c.Margin.Left, y + c.Margin.Top, cw, h);
                if (c is Pilha || c is Cartao) h = c.Height;
                y += c.Margin.Vertical + h + Espaco;
            }
            int nova = Math.Max(0, y - Espaco) + Padding.Bottom;
            if (Height != nova) Height = nova;
        }

        internal static int Altura(Control c, int w)
        {
            if (c is Pilha || c is Cartao) { if (c.Width != w) c.Width = w; else c.PerformLayout(); return c.Height; }
            if (c is CaixaTrecho ct) return ct.AlturaConteudo;
            if (c is Legenda lg) return lg.AlturaPara(w);
            if (c is Label l)
                return TextRenderer.MeasureText(string.IsNullOrEmpty(l.Text) ? " " : l.Text, l.Font, new Size(w, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + l.Padding.Vertical;
            return c.Height;
        }

        public static void Relayout(Control c)
        {
            for (var p = c?.Parent; p != null; p = p.Parent) { if (p is Pilha || p is Cartao) p.PerformLayout(); else break; }
        }
    }

    // Cartão branco com borda; à esquerda, opcionalmente, um selo (✔ / ✘ / !).
    class Cartao : Panel
    {
        public readonly Pilha Corpo = new Pilha { Espaco = 6 };
        public Selo Selo;
        public Color Borda = Cores.Linha;
        public Color Faixa = Color.Empty; // faixa colorida na esquerda

        public Cartao(Selo selo = null)
        {
            DoubleBuffered = true; BackColor = Cores.Papel;
            Padding = new Padding(Escala.D(18), Escala.D(14), Escala.D(18), Escala.D(14));
            Corpo.BackColor = Color.Transparent;
            Selo = selo;
            if (selo != null) Controls.Add(selo);
            Controls.Add(Corpo);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int x = Padding.Left + (Faixa.IsEmpty ? 0 : Escala.D(4));
            int seloW = 0;
            if (Selo != null) { seloW = Selo.Width + Escala.D(14); Selo.Location = new Point(x, Padding.Top); }
            int w = Math.Max(10, ClientSize.Width - x - seloW - Padding.Right);
            Corpo.SetBounds(x + seloW, Padding.Top, w, Corpo.Height);
            Corpo.PerformLayout();
            int h = Math.Max(Corpo.Height, Selo?.Height ?? 0) + Padding.Vertical;
            if (Height != h) Height = h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            using (var p = new Pen(Borda)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            if (!Faixa.IsEmpty) using (var b = new SolidBrush(Faixa)) g.FillRectangle(b, 0, 0, Escala.D(5), Height);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    }

    // Círculo com ✔, ✘ ou ! desenhado (não depende de fonte)
    class Selo : Control
    {
        public string Tipo; // ok, missing, warn, confirmed
        public Selo(string tipo, int tam = 40)
        {
            Tipo = tipo; DoubleBuffered = true; SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; Size = new Size(Escala.D(tam), Escala.D(tam));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = Width - 1, h = Height - 1;
            using (var b = new SolidBrush(Cores.DoStatus(Tipo))) g.FillEllipse(b, 0, 0, w, h);
            using (var p = new Pen(Color.White, Math.Max(2.5f, w / 11f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (Tipo == "ok" || Tipo == "sim" || Tipo == "confirmed")
                    g.DrawLines(p, new[] { new PointF(w * .28f, h * .52f), new PointF(w * .44f, h * .68f), new PointF(w * .73f, h * .35f) });
                else if (Tipo == "na")
                    g.DrawLine(p, w * .32f, h / 2, w * .68f, h / 2);
                else if (Tipo == "missing")
                { g.DrawLine(p, w * .33f, h * .33f, w * .67f, h * .67f); g.DrawLine(p, w * .67f, h * .33f, w * .33f, h * .67f); }
                else
                { g.DrawLine(p, w / 2, h * .26f, w / 2, h * .58f); g.DrawLine(p, w / 2, h * .74f, w / 2, h * .745f); }
            }
        }
    }

    // Trecho do documento com a parte encontrada grifada; a altura se ajusta ao texto.
    class CaixaTrecho : RichTextBox
    {
        public int AlturaConteudo = 40;

        public CaixaTrecho()
        {
            ReadOnly = true; BorderStyle = BorderStyle.None; ScrollBars = RichTextBoxScrollBars.None; WordWrap = true;
            DetectUrls = false; BackColor = Cores.Suave; ForeColor = Cores.Tinta; TabStop = false; ShortcutsEnabled = true;
            Cursor = Cursors.IBeam;
            ContentsResized += (s, e) => Ajustar(e.NewRectangle.Height);
        }

        void Ajustar(int alturaTexto)
        {
            int h = alturaTexto + Escala.D(12);
            if (h != AlturaConteudo) { AlturaConteudo = h; Height = h; Pilha.Relayout(this); }
        }

        // segunda forma de medir (posição do último caractere), caso o aviso de redimensionamento não venha
        void Medir()
        {
            if (!IsHandleCreated || TextLength == 0) return;
            int y = GetPositionFromCharIndex(TextLength - 1).Y;
            int alt = y + Font.Height + Escala.D(4);
            if (alt + Escala.D(12) > AlturaConteudo) Ajustar(alt);
        }

        int larguraMedida;
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width != larguraMedida && IsHandleCreated) { larguraMedida = Width; BeginInvoke(new Action(Medir)); }
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); BeginInvoke(new Action(Medir)); }

        public void Mostrar(string rotulo, string antes, string achado, string depois, Color marca)
        {
            Clear();
            if (!string.IsNullOrEmpty(rotulo))
            {
                SelectionFont = new Font(Font.FontFamily, Font.Size * 0.85f, FontStyle.Bold);
                SelectionColor = Cores.Apagado; AppendText(rotulo + "   ");
            }
            SelectionFont = Font; SelectionColor = Cores.Tinta; AppendText(antes);
            SelectionFont = new Font(Font, FontStyle.Bold); SelectionBackColor = marca; AppendText(achado);
            SelectionFont = Font; SelectionBackColor = BackColor; AppendText(depois);
            SelectAll(); SelectionIndent = Escala.D(8); SelectionRightIndent = Escala.D(8);
            Select(0, 0);
            if (IsHandleCreated) BeginInvoke(new Action(Medir));
        }
    }

    // Quadradinhos com as cores usadas no PDF grifado
    class Legenda : Control
    {
        readonly List<KeyValuePair<Color, string>> itens;
        public Legenda(IEnumerable<KeyValuePair<Color, string>> cores)
        {
            itens = cores.ToList(); DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = new Font("Segoe UI", 9.5f);
        }

        IEnumerable<KeyValuePair<Rectangle, int>> Posicoes(int w)
        {
            int x = 0, y = 0, q = Escala.D(14), alt = Escala.D(22);
            for (int i = 0; i < itens.Count; i++)
            {
                int tw = q + Escala.D(6) + TextRenderer.MeasureText(itens[i].Value, Font).Width + Escala.D(16);
                if (x > 0 && x + tw > w) { x = 0; y += alt; }
                yield return new KeyValuePair<Rectangle, int>(new Rectangle(x, y, tw, alt), i);
                x += tw;
            }
        }

        public int AlturaPara(int w) => itens.Count == 0 ? 0 : Posicoes(w).Max(p => p.Key.Bottom);

        protected override void OnPaint(PaintEventArgs e)
        {
            int q = Escala.D(14);
            foreach (var p in Posicoes(Width))
            {
                var r = p.Key; var it = itens[p.Value];
                using (var b = new SolidBrush(it.Key)) e.Graphics.FillRectangle(b, r.X, r.Y + (r.Height - q) / 2, q, q);
                TextRenderer.DrawText(e.Graphics, it.Value, Font, new Point(r.X + q + Escala.D(6), r.Y + (r.Height - Font.Height) / 2), Cores.Tinta);
            }
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    }

    // Área tracejada para soltar o arquivo
    class ZonaSoltar : Panel
    {
        public bool Sobre;
        public ZonaSoltar() { DoubleBuffered = true; BackColor = Cores.Papel; Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Sobre ? Cores.Destaque : Cores.Hex("#c5bba7"), Escala.D(2)) { DashStyle = DashStyle.Dash })
                g.DrawRectangle(p, Escala.D(2), Escala.D(2), Width - Escala.D(5), Height - Escala.D(5));
            // ícone de documento
            int iw = Escala.D(44), ih = Escala.D(54), x = (Width - iw) / 2, y = Escala.D(30);
            using (var p = new Pen(Cores.Destaque, Escala.D(3)) { LineJoin = LineJoin.Round })
            {
                var c = Escala.D(14);
                g.DrawPolygon(p, new[] { new Point(x, y), new Point(x + iw - c, y), new Point(x + iw, y + c), new Point(x + iw, y + ih), new Point(x, y + ih) });
                g.DrawLines(p, new[] { new Point(x + iw - c, y), new Point(x + iw - c, y + c), new Point(x + iw, y + c) });
                for (int i = 0; i < 3; i++) g.DrawLine(p, x + Escala.D(10), y + Escala.D(24) + i * Escala.D(9), x + iw - Escala.D(10), y + Escala.D(24) + i * Escala.D(9));
            }
        }
    }

    class Botao : Button
    {
        public Botao(string texto, bool principal = false)
        {
            Text = texto; FlatStyle = FlatStyle.Flat; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(Escala.D(10), Escala.D(5), Escala.D(10), Escala.D(5));
            Margin = new Padding(0, 0, Escala.D(8), 0);
            Cursor = Cursors.Hand; UseVisualStyleBackColor = false;
            Font = new Font("Segoe UI", 10f, principal ? FontStyle.Bold : FontStyle.Regular);
            if (principal)
            {
                BackColor = Cores.Destaque; ForeColor = Color.White; FlatAppearance.BorderColor = Cores.Destaque;
                FlatAppearance.MouseOverBackColor = Cores.Destaque2; FlatAppearance.MouseDownBackColor = Cores.Tinta;
            }
            else
            {
                BackColor = Cores.Papel; ForeColor = Cores.Destaque; FlatAppearance.BorderColor = Cores.Hex("#c5bba7");
                FlatAppearance.MouseOverBackColor = Cores.Hex("#eef3f4"); FlatAppearance.MouseDownBackColor = Cores.Hex("#dde8eb");
            }
        }
    }
}
