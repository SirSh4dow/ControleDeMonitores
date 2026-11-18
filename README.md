# Controle de Monitores

Utilitário desktop leve e nativo para **Windows 10/11** desenvolvido em **C# (.NET 8 + Windows Forms)** usando diretamente as APIs nativas Win32 CCD (`QueryDisplayConfig` / `SetDisplayConfig`) e GDI (`EnumDisplaySettings` / `ChangeDisplaySettingsEx`).

Permite gerenciar múltiplos monitores de forma rápida, sem depender de painéis pesados de drivers.

---

## Funcionalidades

- **Posicionamento Visual Interativo (Drag & Drop)**:
  - Arraste e solte as telas para posicionar à esquerda, direita, acima ou abaixo com **encaixe magnético** de bordas e guias de alinhamento (topo, centro e base).
  - **Ajuste fino pelo teclado**: selecione uma tela no painel e use as setas direcionais (`20 px` padrão, `1 px` com `Shift`, `100 px` com `Ctrl`).
  - Prevenção automática contra sobreposições e "buracos" entre monitores, preservando a Tela Principal ancorada em `(0, 0)`.
- **Ativação e Isolamento Rápido**:
  - **Ativar / Desativar** monitores individualmente sem ligar telas indesejadas.
  - **Somente este**: desliga todas as outras telas e mantém apenas o monitor escolhido ativo.
  - **Restabelecer telas**: reativa rapidamente todos os monitores conectados em modo estendido.
  - **Tornar principal**: define qualquer monitor ativo como Tela Principal com 1 clique mantendo o layout relativo.
- **Resolução, Taxa de Atualização (Hz) e Orientação**:
  - Alteração de resolução, frequência e rotação (`Paisagem`, `Retrato`, `Paisagem virado`, `Retrato virado`).
  - **Proteção contra tela preta**: diálogo de confirmação com reversão automática em **15 segundos** caso a configuração não seja compatível com o monitor/cabo.
- **Identificação Visual nas Telas**:
  - Botão **Identificar** exibe o número, modelo e tipo de conexão (`DisplayPort`, `HDMI`, `DVI`, `USB-C`, etc.) diretamente em cada monitor ativo.
- **Bandeja do Sistema (System Tray) e Detecção Automática**:
  - Menu rápido próximo ao relógio do Windows e atualização automática ao conectar/desconectar cabos ou usar `Win + P`.
  - Suporte completo a telas High-DPI (`PerMonitorV2`).

---

## Download (Versão Standalone)

Não é necessário instalar o .NET para usar a versão standalone:

1. Acesse a aba [**Releases**](../../releases/latest).
2. Baixe o arquivo **`ControleDeMonitores-v1.0.0-win-x64-standalone.exe`**.
3. Execute diretamente em qualquer Windows 10 ou 11 (64 bits).

---

## Compilando a partir do Código-Fonte

Pré-requisito: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) no Windows.

```powershell
# Executar em modo de desenvolvimento
dotnet run

# Gerar executável único standalone (Self-Contained Single-File)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

---

## Estrutura do Projeto

- `Program.cs` — Ponto de entrada com controle de instância única (`Mutex`).
- `Models/` — Modelos de domínio (`DisplayDevice`, `DisplayMode`, `DisplayOrientation`, `DisplayResult`).
- `Native/` — Interop P/Invoke com `user32.dll` (CCD & GDI).
- `Services/` — `DisplayService` (gerenciamento de hardware/topologia) e `DisplayLayoutEngine` (motor geométrico de snapping e conectividade).
- `UI/` — Interface responsiva High-DPI (`MainForm`, `LayoutCanvasCard`, `MonitorCard`, `IdentifyOverlayForm`, `ConfirmModeDialog`, `Theme`).
