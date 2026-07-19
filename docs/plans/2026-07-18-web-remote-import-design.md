# Interface web local para importação remota

**Data:** 18 de julho de 2026  
**Status:** desenho aprovado

## Objetivo

Permitir que o Steam Import seja executado no PC-console Windows e controlado
por um navegador no MacBook conectado à mesma rede Wi-Fi ou VPN. O Windows
continua sendo o único computador que acessa a instalação da Steam, as pastas
dos jogos e o `shortcuts.vdf`.

O produto continua sendo distribuído como um único `SteamImport.exe`.

## Decisões de escopo

- O servidor fica disponível apenas na rede privada escolhida pelo usuário.
- Não haverá login, código de pareamento, sessão autenticada ou HTTPS.
- A rede Wi-Fi/VPN é a fronteira de confiança aceita para este aplicativo
  pessoal.
- Não haverá banco de dados, serviço do Windows, bandeja do sistema, QR Code,
  nuvem ou integração específica com a VPN.
- A Steam poderá estar aberta quando o usuário iniciar a importação. O
  aplicativo a encerrará normalmente, fará a alteração e a abrirá novamente.
- A operação será bloqueada se um jogo estiver em execução.
- Nome e artes serão obtidos pelo SteamGridDB após confirmação do resultado
  correto pelo usuário.

## Arquitetura

O único executável reúne:

1. uma pequena janela WPF local para configuração e estado;
2. um servidor ASP.NET Core/Kestrel;
3. os arquivos estáticos da interface web;
4. os serviços de domínio e infraestrutura existentes.

As responsabilidades permanecem separadas:

- `SteamImport.Core`: recomendação do executável, revisão do jogo, AppID e
  regras independentes de Windows e rede;
- `SteamImport.Infrastructure`: Steam, processos, VDF, backups, configuração,
  logs, SteamGridDB, download e instalação das artes;
- `SteamImport.App`: janela local, servidor HTTP, endpoints e interface web.

O servidor escuta inicialmente em `http://0.0.0.0:5050`. A janela local mostra
os endereços utilizáveis na rede. O usuário poderá precisar permitir o
executável no Firewall do Windows para redes privadas.

## Primeira execução e inicialização automática

Na primeira execução, a janela local solicita:

- a pasta raiz que contém os jogos;
- a chave de API pessoal do SteamGridDB;
- confirmação da instalação e da conta Steam detectadas.

A pasta raiz é escolhida com o seletor nativo do Windows. A configuração é
salva em `%LOCALAPPDATA%\SteamImport\config.json`. A chave do SteamGridDB é
utilizada somente pelo processo no Windows e não é enviada à interface web nem
registrada nos logs.

Depois de salvar a configuração, o aplicativo cria um atalho na pasta de
inicialização do usuário atual. Isso permite iniciar junto com o Windows sem
instalar um serviço ou exigir privilégios administrativos. Nas execuções
seguintes, a janela inicia minimizada e permite consultar o endereço, alterar a
configuração ou encerrar o servidor.

## Descoberta e identificação dos jogos

A página inicial lista cada subpasta direta da raiz configurada como um jogo
candidato. O usuário pode atualizar a lista manualmente. Para cada pasta, o
aplicativo calcula localmente:

- o nome provisório derivado da pasta;
- os executáveis encontrados;
- o executável recomendado.

A consulta ao SteamGridDB acontece somente quando um candidato é selecionado.
O servidor pesquisa pelo nome provisório e devolve resultados com nome e capa.
O usuário precisa escolher o jogo correto para evitar confusão entre edições,
remasters e títulos semelhantes.

Depois da escolha, o nome oficial passa a ser o nome sugerido para o atalho. O
aplicativo seleciona automaticamente a arte mais bem avaliada disponível em
cada categoria: grid vertical, grid horizontal, hero, logo e ícone. A primeira
versão mostra uma prévia, mas não oferece uma galeria para trocar cada imagem.
Uma categoria sem arte disponível não impede a importação; uma falha ao baixar
uma arte que foi selecionada impede a alteração da Steam.

## Fluxo de importação

Antes de tocar na Steam, o servidor:

1. valida a configuração e a conta selecionada;
2. confirma que o jogo pertence à pasta raiz configurada;
3. valida o nome e o executável revisados;
4. baixa as artes selecionadas para uma pasta temporária;
5. confirma permissão e espaço para a operação.

Depois da preparação:

1. registra se a Steam estava aberta;
2. bloqueia a operação se detectar um jogo em execução;
3. solicita o encerramento normal da Steam;
4. aguarda todos os processos da Steam terminarem, com timeout e sem forçar o
   encerramento;
5. cria o backup rotativo do `shortcuts.vdf` e das artes que seriam
   substituídas;
6. gera e valida o novo VDF em arquivo temporário;
7. substitui atomicamente o VDF e instala as artes usando o AppID do atalho;
8. abre a Steam novamente somente se ela estava aberta antes;
9. devolve o resultado à página web.

O endpoint mantém a requisição aberta durante essa operação e a página exibe um
indicador de processamento. Um bloqueio exclusivo rejeita uma segunda
importação enquanto a primeira estiver em andamento.

## Tratamento de falhas

Falhas durante configuração, pesquisa ou download são apresentadas sem alterar
a Steam. Se uma falha ocorrer depois do encerramento do cliente:

- o VDF original é restaurado;
- artes parcialmente instaladas são removidas ou restauradas;
- a Steam é reaberta caso estivesse aberta antes;
- a página recebe uma mensagem curta e acionável;
- o log recebe o contexto e o stack trace completo.

Continuam sendo mantidos os cinco logs e os cinco backups mais recentes. A
chave do SteamGridDB deve ser removida de mensagens, URLs e exceções antes de
qualquer escrita no log.

## Interface HTTP mínima

- `GET /api/status`: configuração, Steam detectada e disponibilidade para
  importar;
- `GET /api/games`: candidatos encontrados na pasta raiz;
- `GET /api/games/{candidateId}`: executáveis e recomendação local;
- `GET /api/games/{candidateId}/matches`: resultados do SteamGridDB;
- `POST /api/import`: revisão confirmada e execução da importação.

Os identificadores de candidatos são gerados pelo servidor. A API não aceita
um caminho arbitrário enviado pelo navegador.

## Testes

Os testes automatizados cobrem:

- leitura, validação e persistência da configuração;
- descoberta limitada às subpastas da raiz;
- pesquisa e erros do SteamGridDB com HTTP falso;
- seleção das artes recomendadas;
- criação da inicialização automática;
- detecção de jogo em execução;
- encerramento, espera e reabertura com processos simulados;
- preservação de atalhos existentes;
- rollback do VDF e das artes em cada ponto de falha;
- concorrência entre importações;
- endpoints HTTP e jornada completa com uma instalação Steam temporária;
- validação independente do VDF produzido.

O workflow Windows continua executando build e testes e valida que a publicação
contém somente `SteamImport.exe`. Um teste manual no Windows confirma Firewall,
inicialização automática e encerramento/reabertura reais da Steam.

## Critérios de sucesso

- Depois da configuração inicial no Windows, todo o fluxo cotidiano pode ser
  realizado pelo MacBook.
- A interface lista os jogos da raiz, identifica o título e aplica nome e artes
  após confirmação.
- Uma importação iniciada com a Steam aberta fecha e reabre o cliente sem
  apagar atalhos existentes.
- Uma falha nunca deixa um VDF parcialmente gravado nem artes parcialmente
  instaladas.
- O produto final continua sendo um único EXE sem infraestrutura externa além
  do SteamGridDB.
