## Novidades da v0.17

**Duas versões para baixar**
- **KResetExplode-Setup.exe** (instalador): instala em C:\Program Files\KResetExplode, com atalho no Menu Iniciar e desinstalador.
  É a versão recomendada para o PC do dia a dia.
- **KResetExplode-Portatil.zip**: um único .exe para levar no pendrive. Ao abrir, confere as DLLs que descompacta na pasta
  temporária contra a cópia original dentro do .exe e não abre se alguma tiver sido alterada.
- A release traz o **SHA256SUMS.txt** para conferir o download.

**Nova página · RESET JOGOS › Checkup**
- Procura o que mais tira FPS e explica cada item: monitor abaixo da taxa máxima (ex.: 144 Hz rodando a 60 Hz, com botão para
  corrigir), monitor ligado na placa-mãe em vez da placa de vídeo, RAM em canal único ou sem XMP/EXPO, Game DVR, Modo de Jogo,
  agendamento de GPU, plano de energia, driver de vídeo desatualizado, VBS (só informa), disco cheio e cache de shaders.

**RESET FERRAMENTAS › Drivers (atualizador)**
- Windows Update com comparação: cada driver mostra "instalado → oferecido" e só vêm marcadas as atualizações reais
  (o Windows Update também oferece versões mais antigas, que antes eram instaladas junto).
- Contratos de licença do Windows Update são mostrados para você aceitar ou recusar (antes eram aceitos sozinhos).
- **Dispositivos com problema**: tudo que está com erro no Gerenciador de Dispositivos, com explicação e REINSTALAR.
- **Restaurar backup** dos drivers (depois de formatar) e **apagar instaladores baixados**.
- AMD: compara com a versão do AMD Software instalada. O app avisa ao abrir quando sai driver de vídeo novo.

**RESET OTIMIZA**
- **Limpeza**: caches de shaders (NVIDIA, AMD, DirectX), temporários e instaladores de driver que sobram no disco.
- **Criar ponto de restauração** antes de mexer em ajustes.
- A limpeza de memória não mexe mais na RAM do jogo e dos programas abertos (só nos de segundo plano), o que evitava engasgos.
- Ajustes novos, todos com "Restaurar originais":
  - **Plano Desempenho Máximo** (só em PC de mesa). Ligar troca o Alto desempenho por ele e vice-versa; desligar volta ao plano de antes.
  - **Efeitos visuais no modo desempenho**: sem animações, esmaecimentos, sombras e Espiada. O ClearType e as miniaturas continuam.
  - **Silenciar notificações durante o jogo**: os avisos do Windows e dos apps somem enquanto um jogo está em tela cheia (ou aberto pela
    Biblioteca) e voltam quando ele fecha. Precisa do RESET EXPLODE aberto (pode ficar minimizado).

**Nova página · RESET EXPLODE › Alertas**
- Aviso no canto da tela quando a CPU ou a placa de vídeo passam do limite que você escolher (por 10 s seguidos, para ignorar picos),
  quando um disco fica com saúde crítica ou quando a bateria do notebook passa do desgaste escolhido.
- Funciona enquanto o RESET EXPLODE estiver aberto, inclusive minimizado. Não instala serviço nem tarefa no Windows.
- Liga/desliga geral e por tipo, com os limites salvos; botões para testar o aviso e verificar discos e bateria na hora.

**Nova página · RESET DESEMPENHO › Comparar**
- Mostra o último resultado de cada benchmark, a diferença em % para o anterior e para o **"antes de otimizar"**.
- **MARCAR RESULTADOS ATUAIS** guarda o "antes": rode os testes, marque, aplique o RESET OTIMIZA (ou um ajuste, ou um driver novo)
  e rode de novo. Só compara o mesmo teste com as mesmas opções (resolução, preset, disco).

**Novas ferramentas**
- **Reparar Windows**: DISM + SFC com progresso, para erros e travamentos sem formatar.
- **Atualizar programas instalados** (em Instalar apps): lista o que tem versão nova pelo winget e atualiza os marcados.
- **DNS** (em Rede): mede Cloudflare, Google, Quad9 e OpenDNS daqui e troca com um clique, com VOLTAR AO ORIGINAL.

**Segurança**
- Configurações, backups e downloads ficam em C:\Program Files\KResetExplode (só administradores alteram).
- Jogos, programas auxiliares, links e o navegador abrem sem privilégio de administrador.
- Instaladores e DLLs só são aceitos de servidores oficiais e com assinatura digital válida do fabricante certo.

**Código**
- Arquivos organizados em pastas por área (Hardware, Performance, Games, Tools, Maker, Integration, Security, Views); a janela
  principal ganhou um arquivo por página e o código do Overclock foi reescrito com nomes claros. Nada muda no uso do app.

## Novidades da v0.16

**Nova página · RESET FERRAMENTAS › Inicialização**
- Lista os programas que abrem junto com o Windows: chaves Run do registro (usuário, todos os usuários, 32 e 64 bits)
  e pastas Inicializar, com descrição, fabricante e aviso quando o arquivo do programa não existe mais.
- Interruptor para ativar/desativar, igual ao Gerenciador de Tarefas: a entrada continua lá e pode ser religada.
- Botão **REMOVER** com confirmação; o programa continua instalado e tudo pode ser desfeito em **Restaurar removidos**.

**Nova página · RESET FERRAMENTAS › Instalar apps**
- Os essenciais depois de formatar o PC: Steam, Google Chrome, Discord, Lightshot, 7-Zip, WinRAR e VLC.
- Instala pelo winget (instalador de pacotes da Microsoft que já vem no Windows): versão oficial mais recente,
  sem janelas, um app de cada vez, com o resultado de cada um na lista.
- Mostra o que já está instalado; se o winget ainda não existir (PC recém-formatado), um botão abre a Microsoft Store
  para atualizar o "Instalador de Aplicativo".

**Nova categoria · RESET MAKER** (Arduino, ESP32 e conversores USB-serial)
- **Portas COM**: a limpeza de dispositivos fantasmas e de números COM presos saiu de Otimização e ganhou página própria.
- **Drivers seriais**: diagnóstico das placas com CH340/CH341, CP2102/CP210x, FTDI, Prolific PL2303 e Arduino
  (sem driver, código 10, código 43, FTDI falsificado travado em PID 0000), com a orientação de cada caso.
- **REINSTALAR** remove o dispositivo e faz o Windows detectar de novo com o melhor driver.
- Lista as versões de driver guardadas no Windows para cada chip, com **REMOVER** (backup antes): volta para uma versão
  anterior quando a nova não funciona com clones, ou desinstala de vez. **Restaurar removidos** desfaz.
- **Drivers incluídos no app**: CH340 4.0, CP210x 11.6, FTDI 2.12.36.20 e Arduino, oficiais e assinados, instalam sem internet
  (ideal logo depois de formatar), com a placa conectada ou não. Também abre a página oficial de cada fabricante.
- **Arduino IDE**: instalar/atualizar pelo winget, fechar programas que prendem a porta COM e limpar o cache de índices
  quando a IDE trava em "Downloading index".

**Novo benchmark · Ray tracing (GPU)**
- Teste em Direct3D 12 com DXR 1.1 que usa os núcleos de ray tracing (RTX 20+, Radeon RX 6000+, Intel Arc): 4 mil objetos,
  reflexos de reflexos, sombras suaves, oclusão de ambiente e iluminação indireta, com a cena refeita a cada frame.
- Feito para comparar com confiança: aquecimento sem medir, 3 passadas iguais, nota pela mediana e variação entre passadas
  (selo de "resultado confiável" abaixo de 3%); tempo de frame medido pelo relógio da própria GPU.
- Presets Normal, Alta e Extrema em 1080p, 1440p e 4K; histórico comparando só resultados do mesmo preset e resolução.
- Referência: RX 9070, Alta 1440p = 878 pts (87,8 FPS), variação 0,5%.

**Overlay personalizável**
- Na aba Overlay, escolha o que aparece: FPS, frametime, 1% low, gráfico; GPU (uso, temperatura, hotspot, clock do núcleo,
  clock da memória, consumo, tensão, ventoinha, limitador NVIDIA); VRAM (usada, %, temperatura); CPU (uso, núcleo mais
  ocupado, temperatura, clock médio, clock máximo, consumo); RAM (GB, %); nome do jogo e hora. Atalhos SÓ FPS, PADRÃO e TUDO.
- Cada componente vira uma linha só com os itens escolhidos; a escolha fica salva e vale na hora.

**RESET JOGOS**
- Novo atalho em Extras: **-allow_third_party_software** (CS2), que libera captura de jogo do OBS, ReShade e overlays
  bloqueados pelo modo confiável; o aviso sobre o Fator de Confiança aparece ao passar o mouse.

**Abertura mais rápida**
- A janela aparece em ~0,5 s (antes ~4,6 s). Informações do sistema, sensores e placa de vídeo carregam em paralelo,
  em segundo plano, e cada página é preenchida quando a sua parte fica pronta.
- O aviso "sem sensor da CPU" não aparece mais por engano enquanto o hardware ainda está sendo detectado.

**Correção · Overlay de jogos**
- O overlay agora fica por cima de jogos em janela sem borda que também se marcam como "sempre no topo"
  (ex.: GTA V e Cyberpunk 2077), inclusive depois de Alt+Tab.
- Aparece no monitor onde o jogo está, não só no monitor principal.
