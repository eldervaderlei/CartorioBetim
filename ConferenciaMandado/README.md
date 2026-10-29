# Conferência de Mandados e Sentenças — RCPN Betim/MG

Programa para Windows 10/11 (.exe único, sem instalação, funciona offline) que confere mandados e sentenças. Tipos: **divórcio** e **interdição (curatela)**. Divórcio:

1. força de mandado
2. nome que a parte voltará a assinar
3. divisão de bens
4. trânsito em julgado

- PDF com texto: lido com PdfPig.
- PDF escaneado / foto: OCR nativo do Windows (`Windows.Media.Ocr` + `Windows.Data.Pdf`).
Interdição: sentença, decreta a interdição, motivo da interdição, força de mandado ou mandado do juiz, termo de curatela ou de compromisso, qualificação do curador e do interditado (endereço em Betim), trânsito em julgado, justiça gratuita e assinatura eletrônica.

- Regras: `src/Engine/divorcio.js` (motor da versão HTML, com as funções básicas) e `src/Engine/interdicao.js`, executados com Jint.
- PDF grifado: anotações de destaque acrescentadas ao final do PDF original (`src/Grifo.cs`).
- Arquivos de entrada: vários por conferência, montados numa lista antes de conferir.

## Compilar

```bash
cd src
dotnet build -c Release
```

Gera `src/bin/Release/net48/ConferenciaMandado.exe` com as bibliotecas embutidas. O executável pronto fica em `dist/`.

## Testes do motor

```bash
cd tests/Harness && dotnet build -c Release && cd ..
dotnet Harness/bin/Release/net8.0/Harness.dll amostras/*.txt
node node-ref.js amostras/[0-9]*.txt
TIPO=interdicao dotnet Harness/bin/Release/net8.0/Harness.dll amostras/int-*.txt
```
