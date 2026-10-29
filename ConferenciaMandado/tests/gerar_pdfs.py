import sys, textwrap
from reportlab.lib.pagesizes import A4
from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
import glob
fonte = (glob.glob('/usr/share/fonts/**/DejaVuSans.ttf', recursive=True) or glob.glob('/usr/share/fonts/**/*.ttf', recursive=True))[0]
pdfmetrics.registerFont(TTFont('F', fonte))
def pdf(txt, saida):
    c = canvas.Canvas(saida, pagesize=A4); W, H = A4
    for pagina in txt.split('\n\n\n'):
        y = H - 60
        for par in pagina.split('\n'):
            for linha in (textwrap.wrap(par, 88) or ['']):
                c.setFont('F', 10.5); c.drawString(50, y, linha); y -= 15
        c.showPage()
    c.save()
t = open('amostras/1-completo.txt', encoding='utf8').read().replace('\nCERTIDÃO', '\n\n\nCERTIDÃO')
pdf(t, 'amostras/mandado-digital.pdf')
