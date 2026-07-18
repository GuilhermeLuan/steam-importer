# Steam Import — Design

**Data:** 18 de julho de 2026  
**Plataforma:** Windows 10/11  
**Tecnologia:** .NET 10 LTS com WPF

## Objetivo

Criar um aplicativo simples para Windows que monitore uma ou mais pastas de
jogos externos, identifique novos jogos instalados e, após revisão obrigatória
do usuário, adicione-os como atalhos de jogos não Steam com artes estáticas do
SteamGridDB.

O aplicativo não baixa jogos, não contorna DRM e não altera executáveis. Ele
automatiza a funcionalidade oficial de adicionar jogos não Steam e a aplicação
de artes personalizadas.

## Premissas

- Cada jogo fica em uma subpasta própria, por exemplo
  `D:\Jogos\Nome do Jogo\`.
- Nenhuma importação ou remoção acontece sem confirmação do usuário.
- A Steam precisa estar fechada durante a atualização de `shortcuts.vdf`.
- O usuário fornecerá uma chave pessoal da API do SteamGridDB.
- Somente imagens estáticas serão usadas.
- O conjunto recomendado priorizará a arte de maior pontuação compatível com
  cada tipo e proporção.

## Experiência do usuário

### Primeira execução

1. Detectar a instalação da Steam.
2. Detectar contas locais e pedir uma seleção se houver mais de uma.
3. Solicitar a chave da API do SteamGridDB.
4. Solicitar uma ou mais pastas de jogos.
5. Oferecer inicialização automática junto com o Windows.
6. Iniciar minimizado na bandeja do sistema.

### Detecção

O aplicativo observa as pastas configuradas. Ao surgir uma nova subpasta, ele
aguarda até que os arquivos parem de mudar para não processar uma instalação em
andamento. Em seguida:

1. Procura executáveis candidatos.
2. Descarta instaladores, desinstaladores, crash reporters e configuradores
   conhecidos.
3. Classifica os candidatos por nome, localização e características do arquivo.
4. Pesquisa o nome normalizado da pasta no SteamGridDB.
5. Prepara uma revisão e mostra uma notificação na bandeja.

### Revisão obrigatória

A tela de revisão apresenta:

- nome que aparecerá na Steam;
- pasta do jogo;
- executável recomendado e outros candidatos;
- capa vertical;
- grid/banner horizontal;
- hero de fundo;
- logo transparente;
- ícone.

O conjunto inicial usa, para cada tipo, a imagem estática de maior pontuação que
respeite o formato esperado. O usuário pode trocar qualquer arte, corrigir o
nome, escolher outro resultado do SteamGridDB ou selecionar outro executável.

Se não houver uma correspondência confiável, o usuário precisa escolher o jogo
correto. Se uma categoria de arte estiver ausente, a importação pode prosseguir
com as demais e o item fica marcado como arte incompleta.

### Importação

Depois da confirmação:

1. Verificar se a Steam está aberta.
2. Pedir autorização para fechá-la, quando necessário.
3. Criar um backup rotativo de `shortcuts.vdf`.
4. Calcular um AppID estável para o atalho.
5. Preservar todos os atalhos existentes e adicionar somente o novo registro.
6. Baixar e validar as artes selecionadas.
7. Gravar as artes na pasta `grid` da conta Steam com os nomes esperados.
8. Salvar `shortcuts.vdf` de forma atômica.
9. Reabrir a Steam.

Se a gravação falhar, o backup é restaurado e o usuário recebe uma mensagem
clara. A chave do SteamGridDB nunca aparece em logs.

### Remoção

Se uma subpasta desaparecer, o aplicativo cria uma sugestão de remoção. O atalho
e suas artes só são removidos depois de confirmação explícita. Atalhos que não
foram criados pelo aplicativo nunca são removidos automaticamente.

## Arquitetura

### `SteamImport.App`

Aplicação WPF: assistente inicial, janela principal, tela de revisão, ícone da
bandeja, notificações e comandos do usuário.

### `SteamImport.Core`

Regras independentes de interface:

- descoberta e estabilização de pastas;
- classificação de executáveis;
- normalização de nomes;
- correspondência de jogos;
- seleção e ordenação de artes;
- criação do plano de importação.

### `SteamImport.Infrastructure`

Integrações com:

- sistema de arquivos e `FileSystemWatcher`;
- processos do Windows;
- Steam e arquivos VDF;
- API v2 do SteamGridDB;
- armazenamento de configurações;
- inicialização com o Windows;
- proteção local da chave da API.

As alterações da Steam serão representadas primeiro como um plano imutável. A
interface mostra esse plano e somente o executa depois da confirmação.

## Persistência e segurança

- Preferências e estado ficam em `%LOCALAPPDATA%\SteamImport`.
- A chave da API é protegida usando recursos do Windows e vinculada ao usuário
  atual.
- Backups ficam em `%LOCALAPPDATA%\SteamImport\Backups`.
- Escritas importantes usam arquivo temporário, validação e substituição
  atômica.
- O aplicativo não precisa de privilégios administrativos no uso normal.
- Downloads têm limite de tamanho, timeout e validação de tipo de conteúdo.

## Tratamento de erros

- Steam não encontrada: permitir seleção manual da pasta.
- Várias contas: exigir seleção explícita.
- API indisponível ou sem chave: manter a revisão pendente e permitir tentar
  novamente.
- Nome ambíguo: exigir escolha manual.
- Executável ambíguo: exigir escolha manual.
- Steam aberta: não escrever; pedir autorização para encerrá-la.
- VDF inválido: interromper, preservar o original e oferecer diagnóstico.
- Arte ausente: permitir importação parcial, claramente sinalizada.
- Jogo movido: sugerir atualização do atalho em vez de criar uma duplicata.

## Testes

### Unidade

- normalização de nomes e remoção de sufixos de edição/versão;
- filtragem e classificação de executáveis;
- estabilidade e determinismo do AppID;
- ranking por tipo, formato, natureza estática e pontuação;
- detecção de duplicatas;
- serialização e leitura de atalhos VDF.

### Integração

- preservar atalhos preexistentes em fixtures de `shortcuts.vdf`;
- adicionar, atualizar e remover somente atalhos gerenciados;
- mapear respostas simuladas da API do SteamGridDB;
- gravar corretamente todos os tipos de arte;
- restaurar backup após falha simulada.

### Validação no Windows

- bandeja e notificações;
- inicialização automática;
- detecção de instalação concluída;
- múltiplas contas Steam;
- Steam aberta e fechada;
- biblioteca Desktop e Big Picture;
- instalador e executável portátil.

## Escopo da primeira versão

Incluído:

- pastas locais com uma subpasta por jogo;
- monitor na bandeja;
- revisão obrigatória;
- seleção de executável;
- artes estáticas recomendadas por pontuação;
- importação e sugestão de remoção;
- backup e restauração.

Fora do escopo inicial:

- download ou instalação de jogos;
- contorno de DRM;
- conquistas, tempo de jogo sincronizado ou cloud saves;
- artes animadas;
- importação automática sem confirmação;
- Steam Deck, macOS ou Linux;
- edição de controles e templates do Steam Input.

## Critérios de sucesso

- Um jogo novo é detectado sem varrer arquivos misturados de outros jogos.
- O usuário consegue revisar e importar em poucos cliques.
- Todas as artes disponíveis aparecem corretamente na Steam.
- Nenhum atalho existente é perdido ou alterado acidentalmente.
- Uma falha durante a gravação é recuperável por backup.
- O aplicativo permanece leve e silencioso na bandeja.
