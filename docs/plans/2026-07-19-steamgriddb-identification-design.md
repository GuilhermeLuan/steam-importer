# Identificação e prévia de artes do SteamGridDB

**Data:** 19 de julho de 2026

**Issue:** #4

**Base:** `2026-07-18-web-remote-import-design.md`

## Objetivo e fluxo público

Ao selecionar um candidato descoberto pelo servidor, a revisão local continua
mostrando nome provisório e executáveis. A interface então chama
`GET /api/games/{candidateId}/matches`. O servidor resolve o nome revisado do
candidato e pesquisa o SteamGridDB; nenhum caminho ou chave é enviado ao
navegador. Cada match contém o identificador público do SteamGridDB, o nome
oficial e, quando disponível, a melhor capa vertical estática.

Nenhum match é escolhido automaticamente. Depois de uma ação explícita do
usuário, `GET /api/games/{candidateId}/matches/{gameId}/artwork` confirma que o
resultado pertence à pesquisa daquele candidato e devolve o nome oficial como
sugestão do atalho, além da recomendação de grid vertical, grid horizontal,
hero, logo e ícone. Uma categoria ausente é representada sem URL e não impede
as demais. Esta issue termina na prévia; nenhum endpoint de identificação
escreve na Steam.

## Integração e segurança

`SteamImport.Infrastructure` terá um gateway pequeno sobre `HttpClient`. A
chave protegida é lida da configuração somente no PC-console e enviada no
header `Authorization: Bearer`; ela nunca integra URL, resposta, DOM ou mensagem
de erro. Seguindo a API v2 oficial, a busca usa
`/search/autocomplete/{term}` e as artes usam os endpoints `grids`, `heroes`,
`logos` e `icons` com `types=static`. Grids verticais e horizontais são
consultados separadamente pelas dimensões aceitas. Entre os itens compatíveis,
vence o maior `score`, com ID como desempate determinístico.

Respostas 401, 429, timeout, rede e 5xx são traduzidas para erros curtos e
acionáveis. Nenhuma falha parcial produz estado persistente. URLs de prévia são
restritas a HTTPS antes de serem devolvidas, e a política de conteúdo permite
imagens HTTPS sem liberar scripts ou conexões externas no navegador.

## Estratégia de testes

Os ciclos TDD atravessam interfaces públicas. O gateway é testado com um
`HttpMessageHandler` falso para observar caminhos, filtros, ranking,
autenticação e sigilo. Os endpoints são exercitados por HTTP real em porta
efêmera para escolha explícita, IDs inválidos, categorias ausentes e erros. Um
teste Playwright cobre a jornada visível: candidato, matches, escolha, adoção
do nome oficial e preview responsivo das artes disponíveis.
