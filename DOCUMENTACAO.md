# Documentação do KResetExplode

Guia completo do aplicativo: o que ele faz, como baixar e usar, como compilar a partir do código e como o projeto está
organizado para quem quiser fazer alterações.

- Repositório: [github.com/RobsonGFerrarezi/KResetExplode](https://github.com/RobsonGFerrarezi/KResetExplode)
- Versão documentada: **0.17**
- Resumo rápido e novidades: [README.md](README.md) · histórico detalhado: [NOVIDADES.md](NOVIDADES.md)

## Sumário

1. [Origem do código](#1-origem-do-código)
2. [O que é o aplicativo](#2-o-que-é-o-aplicativo)
3. [Baixar e usar](#3-baixar-e-usar)
4. [Compilar e rodar a partir do código](#4-compilar-e-rodar-a-partir-do-código)
5. [Gerar as versões de distribuição](#5-gerar-as-versões-de-distribuição)
6. [Funcionalidades, página por página](#6-funcionalidades-página-por-página)
7. [Estrutura de pastas](#7-estrutura-de-pastas)
8. [Como o aplicativo funciona por dentro](#8-como-o-aplicativo-funciona-por-dentro)
9. [Regras de segurança](#9-regras-de-segurança)
10. [Onde o aplicativo guarda dados](#10-onde-o-aplicativo-guarda-dados)
11. [Como fazer alterações (receitas)](#11-como-fazer-alterações-receitas)
12. [Estilo de código e interface](#12-estilo-de-código-e-interface)
13. [Como testar](#13-como-testar)
14. [Bibliotecas e licenças](#14-bibliotecas-e-licenças)
15. [Problemas comuns](#15-problemas-comuns)

---

## 1. Origem do código

O KResetExplode foi feito a partir do código do **[RESET EXPLODE](https://github.com/GustavoReset/ResetExplode)**, criado por
**Gustavo ([GustavoReset](https://github.com/GustavoReset))** e publicado sob a **licença MIT**. A base do aplicativo (até a v0.16:
informações do sistema, sensores, placa de vídeo, benchmarks, stress tests, RESET JOGOS, RESET OTIMIZA, RESET MAKER, integração RGB etc.)
é obra do autor original.

Este repositório reorganizou, modificou e ampliou esse código (veja a lista em [README.md](README.md#origem-e-créditos)).
O nome mostrado na tela ("RESET EXPLODE", "RESET JOGOS", "RESET BENCH"...) foi mantido de propósito; o nome do projeto, do executável
e do instalador é **KResetExplode**.

A licença MIT exige que o aviso de copyright do autor original acompanhe o código e as cópias distribuídas. Ele está no arquivo
[LICENSE](LICENSE) e **não deve ser removido**. Quem fizer um fork deste repositório deve manter esse aviso também.

## 2. O que é o aplicativo

Um programa para Windows 10/11 (WPF, .NET 8) que reúne, numa janela só, ferramentas de diagnóstico, desempenho e manutenção do PC,
com foco em jogos:

- **Diagnóstico:** informações do sistema, sensores em tempo real, placa de vídeo no estilo GPU-Z, saúde dos discos (S.M.A.R.T.),
  bateria e energia, rede (lag, perda, velocidade) e alertas em segundo plano.
- **Jogos:** biblioteca que detecta jogos instalados e os abre com um perfil por jogo, Checkup do que tira FPS, FPS estimado,
  detector de gargalo e overlay de FPS por cima do jogo.
- **Desempenho:** benchmarks próprios (CPU, GPU, ray tracing, simulação, memória e disco), comparação antes/depois de otimizar,
  stress test de CPU/RAM/GPU e ajuda para overclock.
- **Ferramentas:** otimização (memória, programas em segundo plano, ajustes do Windows reversíveis, limpeza), programas que abrem com o
  Windows, instalação de apps pelo winget, drivers, reparo do Windows e iluminação RGB.
- **Maker:** limpeza de portas COM e diagnóstico/instalação de drivers de placas Arduino, ESP32 e conversores USB-serial.

O programa **sempre roda como administrador** (o `app.manifest` pede isso), porque leitura de sensores de baixo nível, ajustes do Windows
e drivers exigem esse privilégio. Por isso o código segue regras de segurança rígidas (seção 9).

## 3. Baixar e usar

### Baixar

As versões prontas ficam em **[Releases](https://github.com/RobsonGFerrarezi/KResetExplode/releases)** (release `latest`, atualizada
a cada envio para a branch `main`). Baixe só de lá: o programa roda como administrador, e uma cópia alterada de outro lugar teria controle
total do PC.

| Arquivo | Para quê |
|---|---|
| `KResetExplode-Setup.exe` | **Instalador** (recomendado para o PC do dia a dia). Instala em `C:\Program Files\KResetExplode`, cria atalho no Menu Iniciar e desinstalador. |
| `KResetExplode-Portatil.zip` | **Portátil**: um único `KResetExplode.exe` com o .NET embutido, para levar no pendrive. Não precisa instalar nada. |
| `SHA256SUMS.txt` | Hashes para conferir o download: `Get-FileHash <arquivo>` no PowerShell tem que bater com o valor do arquivo. |

### Primeiro uso

1. Abra o programa e aceite o pedido de administrador.
2. Para ver temperatura e consumo da CPU, é preciso o driver **PawnIO** (uma vez por PC): `winget install namazso.PawnIO`.
   Se faltar, o menu lateral mostra o aviso e um botão **CORRIGIR** que instala com um clique. Depois feche e abra o programa.
3. Como o `.exe` não tem assinatura digital paga, o Windows pode mostrar "O Windows protegeu o computador":
   clique em **Mais informações** › **Executar assim mesmo**.

Atalho útil: **Ctrl+Shift+O** liga e desliga o overlay de FPS.

## 4. Compilar e rodar a partir do código

### Requisitos

- Windows 10/11 64 bits.
- **SDK do .NET 8 ou mais novo** ([dotnet.microsoft.com/download](https://dotnet.microsoft.com/download)). O SDK 9 ou 10 também compila
  o projeto, que tem como alvo `net8.0-windows`.
- Para **rodar** o que foi compilado sem publicar: o **.NET 8 Desktop Runtime** (`winget install Microsoft.DotNet.DesktopRuntime.8`).
  Se você só tem um runtime mais novo (9 ou 10), use a publicação autocontida descrita abaixo, que já leva o .NET 8 junto.
- Opcional: [Inno Setup 6](https://jrsoftware.org/isinfo.php) para gerar o instalador (`winget install JRSoftware.InnoSetup`).
- Opcional: Visual Studio 2022 ou VS Code com a extensão C# Dev Kit. Não é obrigatório: tudo funciona pela linha de comando.

### Baixar o código

```bash
git clone https://github.com/RobsonGFerrarezi/KResetExplode.git
cd KResetExplode
```

### Compilar

```bash
dotnet build
```

O resultado fica em `bin\Debug\net8.0-windows\KResetExplode.exe`. O projeto compila sempre com otimização ligada (`<Optimize>true`),
porque os benchmarks precisam do código otimizado também em Debug.

### Rodar

O executável exige administrador. Escolha uma forma:

- **Abrir o .exe compilado** (`bin\Debug\net8.0-windows\KResetExplode.exe`): o Windows pede a confirmação de administrador.
- **`dotnet run` num terminal aberto como administrador.** Num terminal comum, o `dotnet run` falha com "a operação solicitada requer
  elevação", porque ele não consegue mostrar o pedido de administrador.
- **Sem o .NET 8 Runtime instalado:** publique uma versão autocontida e abra o `.exe` dela:

  ```bash
  dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publicado
  ```

  Depois abra `publicado\KResetExplode.exe`.

Para testar só a interface **sem** administrador, veja a seção 13.

## 5. Gerar as versões de distribuição

### No seu PC: `publicar.bat`

Dê dois cliques em `publicar.bat` (ou rode no terminal). Ele fecha o programa se estiver aberto, limpa a compilação anterior e gera em
`..\KResetExplode-dist` (pasta ao lado do projeto):

- `Portatil\KResetExplode.exe` + `LEIA-ME.txt`: arquivo único com o .NET embutido.
- `KResetExplode-Setup.exe`: o instalador, só se o Inno Setup 6 estiver instalado.

O instalador é definido em `installer\KResetExplode.iss`. Ele instala sempre em `C:\Program Files\KResetExplode` (a pasta não pode
ser trocada no assistente, por segurança) e tem um `AppId` próprio: não interfere com o RESET EXPLODE original se os dois estiverem no PC.

**Por que duas versões:** o `.exe` de arquivo único descompacta DLLs nativas em `%TEMP%\.net`, pasta que qualquer programa da conta pode
alterar. A versão portátil se protege conferindo cada DLL contra a cópia dentro do próprio `.exe` (`Security/BundleGuard.cs`).
O instalador não descompacta nada e fica numa pasta protegida, por isso é o recomendado para uso fixo.

### No GitHub: `.github/workflows/publicar.yml`

- A cada push na `main`, o GitHub Actions compila as duas versões, gera o `SHA256SUMS.txt` e **recria a release `latest`**,
  usando o `NOVIDADES.md` como texto da release.
- Ao enviar uma tag `v*` (ex.: `git tag v0.18` e `git push --tags`), sai também uma release fixa daquela versão.
- As actions de terceiros são fixadas por hash de commit (uma tag como `v4` pode ser movida para outro código).

### Mudar a versão

A versão fica em `KResetExplode.csproj` (`<Version>0.17.0</Version>`). Ela aparece no rodapé do menu do app, no instalador e no workflow.
Ao lançar uma versão, atualize também `NOVIDADES.md` e a seção de novidades do `README.md`.

## 6. Funcionalidades, página por página

O menu lateral tem a **Visão geral** e cinco categorias recolhíveis. Entre parênteses, os arquivos principais de cada página.

### Visão geral
Painel com gráfico CPU × GPU (temperatura, carga ou consumo; 1/5/15 min), notas do benchmark, tabela de componentes, anéis de RAM/VRAM/disco,
eventos recentes e lista de verificação da saúde do PC (`MainWindow.Dashboard.cs`).

### RESET EXPLODE (diagnóstico)
| Página | O que faz | Arquivos |
|---|---|---|
| Sistema | SO, placa-mãe/BIOS, CPU, RAM por módulo, GPU, discos (WMI); cartões por unidade; exportar relatório HTML/JSON. | `SystemInfoCollector.cs`, `DriveMap.cs`, `ReportBuilder.cs`, `MainWindow.System.cs` |
| Sensores | Todos os sensores (temperatura, clock, carga, tensão, ventoinhas, consumo), atualizados a cada 1 s, com busca e filtros. | `SensorMonitor.cs`, `SensorRow.cs`, `MainWindow.Sensors.cs` |
| Placa de vídeo | Dados no estilo GPU-Z (chip, memória, clocks, PCIe, limites, VBIOS, driver) e sensores ao vivo. | `GpuInfoCollector.cs`, `GpuSpecDb.cs`, `Nvml.cs`, `Dxgi.cs`, `MainWindow.Gpu.cs` |
| Discos | Saúde por disco físico (S.M.A.R.T.), com veredito e motivos. | `DiskHealth.cs`, `MainWindow.Disks.cs` |
| Energia | Bateria (desgaste, ciclos, autonomia), fonte e plano de energia. | `BatteryInfo.cs`, `MainWindow.Network.cs` |
| Rede | Diagnóstico de lag (latência, jitter, perda, DNS, velocidade, bufferbloat) e teste/troca de DNS. | `NetworkTest.cs`, `DnsTool.cs`, `MainWindow.Network.cs`, `MainWindow.Dns.cs` |
| Alertas | Avisos de temperatura alta, disco crítico e bateria desgastada enquanto o app está aberto (inclusive minimizado). | `MainWindow.Alerts.cs` |

Ao conectar ou remover pendrive, HD externo ou dispositivo USB, aparece um aviso no canto da tela (`ToastWindow.xaml`, `MainWindow.System.cs`).

### RESET JOGOS
| Página | O que faz | Arquivos |
|---|---|---|
| Checkup | Procura o que tira FPS (monitor em 60 Hz, cabo na placa-mãe, RAM em canal único, Game DVR, plano de energia, driver antigo, disco cheio...) e corrige o que dá. | `GameCheckup.cs`, `MainWindow.Checkup.cs` |
| Biblioteca | Detecta jogos (Steam, Epic, GOG, Ubisoft e `.exe` à mão) e abre com perfil: prioridade, plano de energia, GPU dedicada, argumentos de gráficos, programas auxiliares. | `GameLibrary.cs`, `GameLauncher.cs`, `Vdf.cs`, `MainWindow.Launcher.cs` |
| FPS estimado | FPS esperado em jogos populares para a sua CPU + GPU (API HowManyFPS com chave, ou estimativa local). | `GameCatalog.cs`, `HowManyFpsClient.cs`, `GameRow.cs`, `MainWindow.Games.cs` |
| Gargalo | Equilíbrio CPU × GPU, leitura ao vivo e análise de sessão. | `BottleneckAnalyzer.cs`, `FrameMonitor.cs`, `MainWindow.Bottleneck.cs` |
| Overlay | FPS, frametime e sensores por cima do jogo (Ctrl+Shift+O), com itens escolhidos pelo usuário. | `OverlayWindow.xaml`, `OverlayMetrics.cs`, `FrameMonitor.cs`, `MainWindow.Overlay.cs` |

### RESET DESEMPENHO
| Página | O que faz | Arquivos |
|---|---|---|
| Benchmark | Render de CPU (path tracing, estilo Cinebench), cena de jogo (Direct3D 11), ray tracing (DXR 1.1), simulação de jogo, memória/carregamento e disco. | `Performance/*Bench*.cs`, `BenchRenderer.cs`, `MainWindow.Bench.cs`, `MainWindow.BenchRt.cs`, `MainWindow.Disks.cs` |
| Comparar | Último resultado de cada teste contra o anterior e contra o "antes de otimizar" marcado pelo usuário, em %. | `MainWindow.Compare.cs` |
| Stress test | Carga de CPU/RAM e stress de GPU (estilo FurMark) com limite de temperatura e gráfico. | `StressTester.cs`, `GpuBurn.cs`, `MainWindow.Stress.cs` |
| Overclock | Limite de energia e teto de clock da GPU NVIDIA, verificador de estabilidade e guia de BIOS. | `OcCheck.cs`, `Nvml.cs`, `MainWindow.Overclock.cs` |

### RESET FERRAMENTAS
| Página | O que faz | Arquivos |
|---|---|---|
| Otimização | Limpa memória, fecha programas em segundo plano, ajustes do Windows reversíveis, Limpeza de caches e ponto de restauração. | `Optimizer.cs`, `WindowsTweaks.cs`, `Cleanup.cs`, `MainWindow.Optimize.cs`, `MainWindow.Checkup.cs` |
| Inicialização | Programas que abrem com o Windows: ativar, desativar, remover e restaurar. | `StartupManager.cs`, `MainWindow.Startup.cs` |
| Instalar apps | Essenciais pós-formatação e atualização de programas pelo winget. | `AppInstaller.cs`, `MainWindow.Apps.cs` |
| Drivers | Inventário, driver de vídeo, Windows Update com comparação de versões, dispositivos com problema, backup e restauração. | `DriverManager.cs`, `DriverUpdates.cs`, `MainWindow.Drivers.cs` |
| Reparar Windows | DISM + SFC com progresso. | `WindowsRepair.cs`, `MainWindow.Repair.cs` |
| Integração | Iluminação RGB: Corsair iCUE, OpenRGB (MSI/Gigabyte/ASUS e outras) e Razer Chroma. | `Integration/*.cs`, `MainWindow.Integration.cs` |

### RESET MAKER
| Página | O que faz | Arquivos |
|---|---|---|
| Portas COM | Remove registros de dispositivos USB/seriais ausentes e libera números COM presos (com desfazer). | `UsbCleaner.cs`, `MainWindow.MakerPorts.cs` |
| Drivers seriais | Diagnóstico de CH340, CP210x, FTDI, Prolific e Arduino; reinstalar; trocar ou remover versões; drivers oficiais embutidos; Arduino IDE. | `MakerDrivers.cs`, `Maker/Drivers/`, `MainWindow.MakerDrivers.cs` |

## 7. Estrutura de pastas

É um único projeto WPF (`KResetExplode.csproj`) com um único namespace, `KResetExplode`. As pastas separam o código por área;
o compilador inclui todos os `.cs` e `.xaml` de todas as pastas automaticamente (não é preciso registrar arquivo novo no `.csproj`).

```
KResetExplode/
├── App.xaml, App.xaml.cs        início do app (instância única) e recursos globais
├── KResetExplode.csproj         projeto: versão, pacotes NuGet, recursos embutidos
├── app.manifest                 pede administrador e declara compatibilidade com Windows 10/11
├── publicar.bat                 gera as versões portátil e instalador no seu PC
├── installer/                   script do Inno Setup (KResetExplode.iss)
├── .github/workflows/           build e release automáticos no GitHub
├── Assets/                      logo (logo.png, logo-dark.png, logo-badge.png) e ícone (app.ico)
├── Core/                        configurações do app
├── Security/                    regras de segurança reutilizadas por todo o código
├── Hardware/                    leitura de hardware (somente leitura)
├── Performance/                 benchmarks, stress tests e overclock
├── Games/                       jogos, FPS e overlay
├── Tools/                       ferramentas que alteram o Windows (sempre com desfazer)
├── Maker/                       portas COM e drivers seriais (+ Drivers/ embutidos)
├── Integration/                 iluminação RGB
├── Views/                       janelas e tema
│   ├── MainWindow/              janela principal: XAML + um arquivo por página
│   ├── Theme/                   cores, estilos e troca de tema claro/escuro
│   ├── OverlayWindow.xaml(.cs)  overlay por cima do jogo
│   └── ToastWindow.xaml(.cs)    aviso no canto da tela
├── README.md, NOVIDADES.md, DOCUMENTACAO.md, LEIA-ME.txt (vai junto com a versão portátil), LICENSE
```

### A regra principal

- **As pastas de área (`Hardware`, `Performance`, `Games`, `Tools`, `Maker`, `Integration`) têm a lógica.** São classes sem interface
  gráfica: leem o hardware, rodam testes, alteram o Windows. Podem ser usadas por qualquer página.
- **`Views/MainWindow/MainWindow.<Página>.cs` só ligam a tela à lógica:** leem os controles, chamam as classes acima (em segundo plano
  quando é demorado) e mostram o resultado. Todos esses arquivos são partes (`partial class`) da mesma classe `MainWindow`.
- **`Security` e `Core` são a base:** qualquer código que executa programa, carrega DLL, baixa arquivo ou grava dado confiável passa por eles.

### Arquivo por arquivo

**Raiz e `Core/`**
| Arquivo | Função |
|---|---|
| `App.xaml.cs` | Garante uma única instância (abrir de novo só traz a janela existente para a frente) e cria a `MainWindow`. |
| `Core/AppSettings.cs` | Configurações e históricos (JSON): benchmarks, backups dos ajustes, perfis de jogos, alertas, tema, overlay. Chave da API cifrada com DPAPI. |

**`Security/`**
| Arquivo | Função |
|---|---|
| `SecureFiles.cs` | Pasta protegida `%ProgramFiles%\KResetExplode`, pastas temporárias seguras, recusa de links/junções, caminho completo de ferramentas do System32, uso seguro do winget. |
| `ShellLauncher.cs` | Abre programas, atalhos e links **sem** administrador (pelo Explorer). |
| `Authenticode.cs` | Confere a assinatura digital de arquivos baixados e a organização do certificado. |
| `NativeLoading.cs` | Faz as DLLs nativas serem carregadas só do System32, nunca da pasta do `.exe`. |
| `BundleGuard.cs` | Protege a versão portátil, conferindo as DLLs extraídas em `%TEMP%`. |

**`Hardware/`**
| Arquivo | Função |
|---|---|
| `SensorMonitor.cs`, `SensorRow.cs` | Encapsula o LibreHardwareMonitorLib e mantém a lista de sensores atualizada. |
| `SystemInfoCollector.cs`, `InfoNode.cs` | Informações estáticas do sistema via WMI, em árvore de categorias. |
| `GpuInfoCollector.cs`, `GpuSpecDb.cs` | Dados da placa de vídeo e banco de especificações de referência. |
| `Nvml.cs` | Acesso mínimo à NVML (driver NVIDIA): sensores, limites de energia e clocks. |
| `Dxgi.cs` | Adaptadores via DXGI (VRAM real) e nível de recursos do Direct3D 12. |
| `DiskHealth.cs` | Saúde dos discos (API de armazenamento do Windows + S.M.A.R.T.). |
| `BatteryInfo.cs` | Bateria e energia via WMI. |
| `DriveMap.cs` | Cartões por letra de unidade. |
| `DisplayModes.cs` | Monitor: resolução, taxa atual e máxima, placa onde está ligado. |
| `HardwareErrors.cs` | Erros WHEA e reinícios do driver de vídeo no registro de eventos. |

**`Performance/`**
| Arquivo | Função |
|---|---|
| `BenchRenderer.cs` | Benchmark de CPU por path tracing (nota determinística). |
| `GpuGameBench.cs` | Cena de jogo em Direct3D 11. |
| `GpuRtBench.cs` | Ray tracing por hardware (Direct3D 12, DXR 1.1). |
| `GameSimBench.cs` | Simulação de jogo na CPU (milhares de unidades). |
| `MemoryBench.cs` | Latência e banda da RAM, carregamento de mapa. |
| `DiskBench.cs` | Leitura/escrita sequencial e aleatória do disco. |
| `StressTester.cs` | Carga de CPU e RAM com verificação de erros de memória. |
| `GpuBurn.cs` | Stress de GPU no estilo FurMark. |
| `OcCheck.cs` | Apoio ao Overclock: erros de hardware e identificação da placa-mãe. |

**`Games/`**
| Arquivo | Função |
|---|---|
| `GameLibrary.cs`, `Vdf.cs` | Detecta jogos instalados (Steam usa arquivos VDF/ACF). |
| `GameLauncher.cs` | Aplica o perfil, abre o jogo sem administrador e desfaz o temporário quando ele fecha. |
| `GameCheckup.cs` | Itens do Checkup para jogos. |
| `GameCatalog.cs`, `GameRow.cs`, `HowManyFpsClient.cs` | FPS estimado (catálogo local ou API HowManyFPS, com cache de 7 dias). |
| `BottleneckAnalyzer.cs` | Análise de gargalo. |
| `FrameMonitor.cs` | Mede o FPS de qualquer jogo via ETW, sem injetar código (como o PresentMon). |
| `OverlayMetrics.cs` | Itens que podem aparecer no overlay. |

**`Tools/`**
| Arquivo | Função |
|---|---|
| `Optimizer.cs` | Limpeza de memória e lista de programas que podem ser fechados. |
| `WindowsTweaks.cs` | Ajustes do Windows para jogos, cada um com leitura, aplicação e restauração do valor original. |
| `Cleanup.cs` | Limpeza de caches de shaders, temporários e instaladores de driver. |
| `StartupManager.cs` | Programas que abrem com o Windows. |
| `AppInstaller.cs` | Catálogo de apps e instalação/atualização pelo winget. |
| `DriverManager.cs`, `DriverUpdates.cs` | Inventário, driver de vídeo, Windows Update, dispositivos com problema, backup. |
| `WindowsRepair.cs` | DISM + SFC. |
| `DnsTool.cs`, `NetworkTest.cs` | DNS e diagnóstico de rede. |
| `ReportBuilder.cs` | Relatório HTML/JSON. |

**`Maker/` e `Integration/`**
| Arquivo | Função |
|---|---|
| `Maker/UsbCleaner.cs` | Dispositivos USB/seriais (inclusive "fantasmas") e reservas de números COM. |
| `Maker/MakerDrivers.cs` | Famílias de chips USB-serial, diagnóstico e instalação dos drivers embutidos. |
| `Maker/Drivers/` | Drivers oficiais e assinados (CH340, CP210x, FTDI, Arduino) embutidos no `.exe`. Veja `Maker/Drivers/LEIA-ME.md`. |
| `Integration/LightBackends.cs` | "Pontes" de iluminação (OpenRGB, Razer Chroma, Corsair) e efeitos. |
| `Integration/CorsairSdk.cs`, `CorsairSdkInstaller.cs` | SDK da Corsair (baixado só com confirmação e conferência de assinatura). |

**`Views/`**
| Arquivo | Função |
|---|---|
| `MainWindow/MainWindow.xaml` | Toda a interface da janela principal: menu lateral e todas as páginas (cada página é um elemento `Page<Nome>`). |
| `MainWindow/MainWindow.xaml.cs` | Núcleo da janela: construtor, carga do hardware em paralelo, timer de 1 s, navegação. |
| `MainWindow/MainWindow.<Página>.cs` | Código de cada página (ver tabelas da seção 6). |
| `Theme/Theme.xaml`, `Theme/ThemeManager.cs` | Cores, estilos e troca entre tema escuro e claro. |
| `OverlayWindow.xaml(.cs)` | Janela transparente do overlay, que não rouba o foco nem recebe cliques. |
| `ToastWindow.xaml(.cs)` | Aviso no canto inferior direito da tela. |

## 8. Como o aplicativo funciona por dentro

### Ciclo de vida

1. **`App.OnStartup`** confere se já há outra instância (mutex + caminho do `.exe`). Se houver, traz a janela existente para a frente e sai.
2. **Construtor da `MainWindow`**: carrega as configurações (`AppSettings.Load()`), aplica o tema, monta as definições dos ajustes
   e cria o **timer de 1 segundo**. A janela aparece imediatamente.
3. **`LoadHardwareAsync`** (no evento `Loaded`): em paralelo e fora da thread da interface, coleta as informações do sistema (WMI),
   abre os sensores e lê as placas de vídeo. Cada parte preenche a tela quando fica pronta. Depois inicializa o que depende da CPU/GPU
   (FPS estimado, gargalo, overlay, overclock).
4. **A cada segundo**, o timer atualiza os sensores e as partes ao vivo: painel de stress, Visão geral, gargalo, benchmark (se aberto),
   memória da Otimização e **alertas** (que também cuidam de silenciar notificações durante o jogo).
   Durante os benchmarks de GPU o timer fica pausado, porque consultar o driver causa engasgos no resultado.
5. **Ao fechar**, para testes em andamento, fecha overlay e avisos, devolve as notificações se estiverem silenciadas e libera os sensores.

### Navegação entre páginas

Cada item do menu é um `RadioButton` com `Tag="Page<Nome>"`. O evento `Nav_Checked` (em `MainWindow.xaml.cs`):
- mostra a página com aquele nome e esconde as outras (`ShowPage`);
- escreve "CATEGORIA · página" na barra de título;
- chama a inicialização da página, se ela tiver uma (por exemplo `InitOptimize()`). As páginas carregam dados só quando são abertas.

### Trabalho pesado fora da interface

Leituras de WMI, testes e comandos externos rodam com `await Task.Run(...)`. Benchmarks de GPU rodam numa thread dedicada
(`TaskCreationOptions.LongRunning`). Os resultados voltam à tela depois do `await`, já na thread da interface.

### Configurações

`AppSettings` é uma classe serializada em JSON (`System.Text.Json`). Cada propriedade pública com valor padrão vira um campo do arquivo;
uma propriedade nova não quebra arquivos antigos (o campo ausente fica com o padrão). Grave com `_settings.Save()` depois de alterar.

### Ajustes reversíveis

Todo ajuste do Windows (`Tools/WindowsTweaks.cs`) tem três funções: **ler** o estado atual, **aplicar** e **restaurar**. Na primeira
alteração, o valor original é guardado em `AppSettings.TweakBackup` (`"<ausente>"` quando não existia). O botão **Restaurar originais**
desfaz tudo a partir desse backup. O mesmo princípio vale para inicialização, DNS, números COM e drivers seriais: sempre há backup
antes de mudar e um botão para desfazer.

## 9. Regras de segurança

O programa roda como administrador. Qualquer arquivo que ele execute, carregue ou use para restaurar algo, se estiver numa pasta que outros
programas possam alterar, vira um caminho para um programa malicioso ganhar administrador. Por isso:

1. **Dados confiáveis só na pasta protegida** `%ProgramFiles%\KResetExplode` (`SecureFiles.ProtectedRoot`, `AppSettings.Folder`).
   Nunca em `%AppData%`, `%TEMP%`, Downloads ou na pasta do `.exe`.
2. **Ferramentas do Windows pelo caminho completo**: `SecureFiles.SystemTool("powercfg.exe")`, `SecureFiles.PowerShell`. Nunca só o nome
   do programa (um `powercfg.exe` falso na pasta atual seria executado).
3. **Programas e links do usuário sem administrador**: `ShellLauncher.Open(...)`. `Process.Start` passaria o administrador para o jogo,
   o navegador ou a ferramenta.
4. **DLLs nativas só do System32** (`NativeLoading.cs`). Não acrescente `DllImport` de DLL fora do sistema sem passar por essa regra.
5. **Downloads só de servidores oficiais e com assinatura conferida** (`Authenticode.cs`), salvos na pasta protegida.
6. **winget** só pelo `SecureFiles.AcquireWinget(...)`, que confere se ele aponta para o pacote oficial da Microsoft.
7. **Nada que reduza a segurança do Windows**: não desligar Defender, Integridade de Memória/VBS, UAC nem Windows Update.
8. **Apagar arquivos em pastas do usuário** (limpeza) é feito sem administrador e sem seguir links (`Cleanup.cs`), para não ser enganado
   por uma junção que aponte para uma pasta do sistema.

## 10. Onde o aplicativo guarda dados

| Local | Conteúdo |
|---|---|
| `%ProgramFiles%\KResetExplode\Usuarios\<SID>\settings.json` | Configurações, históricos de benchmark, backups dos ajustes, perfis de jogos, alertas (um por usuário do Windows). |
| `%ProgramFiles%\KResetExplode\Usuarios\<SID>\` (subpastas) | Cópias para "Restaurar removidos" (inicialização), backups de drivers seriais, cache do FPS estimado (`fps-cache.json`). |
| `%ProgramFiles%\KResetExplode\Downloads\Drivers` | Instaladores de driver baixados pelo app (apagáveis pela Limpeza). |
| `%ProgramFiles%\KResetExplode\iCUESDK.x64_2019.dll` | SDK da Corsair, se o usuário pediu para baixar. |
| `Documentos\KResetExplode-Drivers\<data-hora>` | Backup de drivers feito pelo usuário (para restaurar depois de formatar). |
| `%AppData%\ResetExplode\settings.json` | Local antigo (até a v0.16 do projeto original). Se existir, é importado uma vez, descartando itens que não são confiáveis. |

A pasta em Program Files fica no PC, mesmo usando a versão portátil. O desinstalador pergunta se deve apagá-la; na versão portátil,
apague à mão se quiser remover tudo.

## 11. Como fazer alterações (receitas)

### Adicionar uma página nova

1. **Menu** (`Views/MainWindow/MainWindow.xaml`): dentro do `StackPanel` da categoria (`GrpExplodeItems`, `GrpGamesItems`, `GrpPerfItems`,
   `GrpToolsItems` ou `GrpMakerItems`), acrescente um item copiando um existente:
   ```xml
   <RadioButton x:Name="NavMinhaPagina" Style="{StaticResource NavItem}" Tag="PageMinhaPagina" Checked="Nav_Checked">
       <StackPanel Orientation="Horizontal">
           <TextBlock Text="&#xE7BA;" FontFamily="{StaticResource Icons}" FontSize="14" Width="28" VerticalAlignment="Center"/>
           <TextBlock Text="Minha página" VerticalAlignment="Center"/>
       </StackPanel>
   </RadioButton>
   ```
   O ícone é um código da fonte Segoe MDL2 Assets.
2. **Página** (mesmo arquivo, na área de conteúdo junto das outras): um elemento com o nome da `Tag`, começando escondido:
   ```xml
   <ScrollViewer x:Name="PageMinhaPagina" Visibility="Collapsed" VerticalScrollBarVisibility="Auto">
       <StackPanel Margin="36,30,36,30">
           <TextBlock Text="Minha página" Style="{StaticResource H1}"/>
           <Border Style="{StaticResource CardBorder}"> ... </Border>
       </StackPanel>
   </ScrollViewer>
   ```
3. **Registrar** em `MainWindow.xaml.cs`: inclua `PageMinhaPagina` na lista do método `ShowPage` e, se a página carrega dados ao abrir,
   um `case "PageMinhaPagina": InitMinhaPagina(); break;` no `switch` do `Nav_Checked`.
4. **Código** em `Views/MainWindow/MainWindow.MinhaPagina.cs`:
   ```csharp
   namespace KResetExplode;

   /// <summary>CATEGORIA › Minha página: o que ela faz.</summary>
   public partial class MainWindow
   {
       private void InitMinhaPagina() { ... }
   }
   ```
5. Se houver lógica de verdade (ler hardware, alterar o Windows), crie uma classe na pasta de área correspondente e chame-a daqui.

### Adicionar um ajuste do Windows (RESET OTIMIZA)

Em `Tools/WindowsTweaks.cs`, acrescente um item na lista de `All(...)`. Para um valor DWORD simples do registro, use o atalho:

```csharp
RegTweak("meuajuste", "Título", "O que faz.", "Efeito esperado.",
    Registry.CurrentUser, @"Software\...\Chave", "NomeDoValor", on: 1, off: 0, absentMeansOn: false),
```

Para algo mais complexo, crie um `new Tweak { Id, Title, Description, Effect, Read, Write, Restore }` usando `SetDword`/`RestoreDword`
(que já guardam o backup). O ajuste aparece sozinho na página Otimização e entra no "Restaurar originais". Se não se aplicar a todo PC,
preencha `Unavailable` com o motivo (o interruptor fica desabilitado). Só ajustes documentados, reversíveis e que não reduzam a segurança.

### Adicionar um benchmark

1. Crie a classe do teste em `Performance/`, com uma nota em que **maior = melhor**.
2. Ao terminar, chame `SaveBenchRecord("meuteste", nota, "resumo para a tela")` (`MainWindow.Bench.cs`): grava no histórico
   (10 por teste), registra o evento e atualiza as listas.
3. Para aparecer na página **Comparar**, mapeie o código do teste em `MetricOf` e a ordem em `MetricRank` (`MainWindow.Compare.cs`).
   Se a nota depender de opções (resolução, disco...), inclua a opção na chave para só comparar resultados equivalentes.
4. Opcional: nome no relatório (`BuildBenchList` em `MainWindow.Disks.cs`) e na Visão geral (`SavedEvents` em `MainWindow.Dashboard.cs`).

### Adicionar um app em "Instalar apps"

Uma linha em `AppInstaller.Catalog()` (`Tools/AppInstaller.cs`) com o ID do winget (procure com `winget search <nome>`).

### Adicionar um driver serial embutido

Crie `Maker/Drivers/<chip>/` com o pacote oficial e assinado (`.inf`, `.cat`, `.sys`) e ligue-o em `MakerDrivers.Chips` (`Bundle`).
O `.csproj` embute a pasta inteira automaticamente. Veja `Maker/Drivers/LEIA-ME.md`.

### Adicionar uma configuração salva

Crie a propriedade em `Core/AppSettings.cs` com valor padrão (ex.: `public bool MinhaOpcao { get; set; } = true;`), leia de `_settings`
na página e chame `_settings.Save()` ao mudar.

### Adicionar um alerta

Em `MainWindow.Alerts.cs`: a verificação rápida vai em `AlertsTick` (roda a cada segundo); a lenta (WMI) vai em `CheckHealthAsync`
(roda 2 min depois de abrir e a cada 30 min). Para avisar, use `AddAlert(título, texto)`. Limites configuráveis ficam em `AlertSettings`.

### Avisos e mensagens

- Aviso no canto da tela: `Toast(título, texto, ícone)` (também registra em "Eventos recentes").
- Evento na Visão geral: `AddEvent(ícone, título, detalhe, valor)`.
- Ação que exige administrador: comece com `if (!RequireAdmin()) return;`.

## 12. Estilo de código e interface

- **Português** nos textos da tela e nos comentários; nomes de classes, métodos e variáveis em inglês, descritivos (`maxCpuTemp`, não `num2`).
- Toda classe e todo arquivo de página começa com um comentário `/// <summary>` dizendo o que faz.
- Comentários explicam o **porquê** (uma regra do Windows, um cuidado de segurança), não o óbvio.
- `Nullable` está ligado: trate `null` explicitamente (`if (x is float t)`).
- Mensagens ao usuário explicam o que aconteceu e o que fazer, sem termos técnicos desnecessários.
- **Interface:** use os estilos de `Views/Theme/Theme.xaml` (e os recursos do topo de `MainWindow.xaml`) em vez de cores fixas:
  - cartões: `CardBorder`; títulos: `H1`, `Sub`, `Label`; botões: `PrimaryButton`, `OutlineButton`; opções: `Chip`;
  - cores sempre com `DynamicResource` (`Fg`, `Ink`, `Muted`, `Faint`, `Card`, `Line`, `Bg`), para o tema claro/escuro funcionar;
  - fontes: `Mono` para números e `Icons` (Segoe MDL2 Assets) para ícones;
  - modelos prontos: `Meter` (barra de histórico, nos recursos de `MainWindow.xaml`) e `CardTemplate` (cartão com selo, usado em
    Discos/Energia, em `Theme.xaml`).

## 13. Como testar

Não há testes automatizados. O roteiro usado no projeto:

1. **Compilar sem erros novos:** `dotnet build` (hoje há 2 avisos conhecidos e nenhum erro).
2. **Abrir as telas sem administrador**, para conferir a interface sem risco de alterar o PC. Publique a versão autocontida (seção 4)
   e, no PowerShell:
   ```powershell
   $env:__COMPAT_LAYER = 'RunAsInvoker'
   .\publicado\KResetExplode.exe
   ```
   O app abre sem pedir administrador. Sensores da CPU e ações que alteram o sistema não funcionam nesse modo (o app avisa), e
   salvar configurações falha porque a pasta protegida exige administrador. Isso é esperado.
3. **Testar as ações como administrador** num PC de teste ou depois de criar um ponto de restauração (botão na página Otimização).
   Para cada ajuste: ligar, conferir o efeito, desligar e usar **Restaurar originais**.
4. Para navegar pelas páginas automaticamente dá para usar UI Automation do Windows (`System.Windows.Automation`): cada item do menu
   é um `RadioButton` com o nome da página.

## 14. Bibliotecas e licenças

| Componente | Uso | Licença |
|---|---|---|
| [RESET EXPLODE](https://github.com/GustavoReset/ResetExplode) (GustavoReset) | Código-base deste projeto | MIT |
| [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 0.9.6 | Sensores | MPL 2.0 |
| [TraceEvent](https://github.com/microsoft/perfview) 3.2.8 (Microsoft) | Medição de FPS por ETW | MIT |
| [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) 3.8.3 | Direct3D 11/12, compilador de shaders (DXC) | MIT |
| System.Management, System.Security.Cryptography.ProtectedData (Microsoft) | WMI e DPAPI | MIT |
| Drivers seriais em `Maker/Drivers` (WCH, Silicon Labs, FTDI, Arduino) | Instalação sem internet | Dos fabricantes; redistribuídos sem alteração |
| PawnIO (namazso) | Driver de leitura dos sensores da CPU (instalado à parte pelo winget) | Do autor |

Ao distribuir, mantenha o `LICENSE` (MIT) e respeite a MPL 2.0 do LibreHardwareMonitorLib.

## 15. Problemas comuns

| Sintoma | Causa e solução |
|---|---|
| Temperatura/consumo da CPU aparecem como "—" | Falta o PawnIO ou o app foi aberto sem administrador. Use o botão CORRIGIR no menu lateral e abra o app de novo. |
| `dotnet run` falha pedindo elevação | O app exige administrador: rode num terminal aberto como administrador ou abra o `.exe` compilado. |
| "You must install or update .NET to run this application" | Falta o .NET 8 Desktop Runtime. Instale-o ou use a publicação autocontida (seção 4). |
| "O Windows protegeu o computador" | O `.exe` não tem assinatura digital paga. Clique em Mais informações › Executar assim mesmo (baixe só das Releases oficiais). |
| Overlay não aparece no jogo | O jogo usa tela cheia exclusiva. Use modo janela sem bordas. |
| Algum sensor não aparece | Depende do suporte do LibreHardwareMonitor à sua placa-mãe. |
| Salvar configurações dá "acesso negado" | O app foi aberto sem administrador (por exemplo com `RunAsInvoker` para teste). |
