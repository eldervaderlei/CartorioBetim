# Conferência de Mandado de Divórcio — RCPN Betim/MG

Programa para Windows 10/11 (.exe único, sem instalação, funciona offline) que confere os 4 itens essenciais para a averbação do divórcio:

1. força de mandado
2. nome que a parte voltará a assinar
3. divisão de bens
4. trânsito em julgado

- PDF com texto: lido com PdfPig.
- PDF escaneado / foto: OCR nativo do Windows (`Windows.Media.Ocr` + `Windows.Data.Pdf`).
- Regras: `src/Engine/motor.js` (o mesmo motor da versão HTML), executado com Jint.

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
node node-ref.js amostras/*.txt
```
