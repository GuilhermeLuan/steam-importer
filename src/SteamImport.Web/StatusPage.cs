namespace SteamImport.Web;

internal static class StatusPage
{
    internal const string Html = """
        <!doctype html>
        <html lang="pt-BR">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Steam Import // Status</title>
          <link rel="stylesheet" href="/app.css">
          <script src="/app.js" defer></script>
        </head>
        <body data-endpoint="/api/status" data-games-endpoint="/api/games" data-refresh-endpoint="/api/games/refresh">
          <main>
            <header>
              <span class="prompt" aria-hidden="true">&gt;_</span>
              <div>
                <p class="eyebrow">PC-CONSOLE / REDE PRIVADA</p>
                <h1 aria-label="STEAM_IMPORT">STEAM_<span>IMPORT</span></h1>
              </div>
              <span class="link-state"><i></i> LINK LOCAL</span>
            </header>

            <section class="terminal" aria-labelledby="status-title">
              <div class="terminal-bar">
                <h2 id="status-title">STATUS_DO_SISTEMA</h2>
                <span id="clock">--:--:--</span>
              </div>
              <div class="status-grid" aria-live="polite">
                <article data-status="configurationReady">
                  <span class="index">01</span>
                  <h3>CONFIGURAÇÃO</h3>
                  <p class="value">LENDO...</p>
                </article>
                <article data-status="steamReady">
                  <span class="index">02</span>
                  <h3>STEAM</h3>
                  <p class="value">LENDO...</p>
                </article>
                <article data-status="accountReady">
                  <span class="index">03</span>
                  <h3>CONTA</h3>
                  <p class="value">LENDO...</p>
                </article>
              </div>
              <div class="summary">
                <span id="summary-mark">[···]</span>
                <p id="summary-text">SINCRONIZANDO COM O PC-CONSOLE</p>
              </div>
            </section>

            <section class="games terminal" aria-labelledby="games-title">
              <div class="terminal-bar">
                <h2 id="games-title">JOGOS_CANDIDATOS</h2>
                <button id="discover-refresh" type="button">[ ATUALIZAR DESCOBERTA ]</button>
              </div>
              <p id="games-message" class="panel-message" aria-live="polite">LENDO PASTA RAIZ...</p>
              <div id="game-list" class="game-list"></div>
            </section>

            <section id="game-review" class="review terminal" aria-labelledby="review-title" hidden>
              <div class="terminal-bar">
                <h2 id="review-title">REVISÃO_LOCAL</h2>
                <span>SEM ALTERAR A STEAM</span>
              </div>
              <form id="review-form">
                <label class="field">
                  <span>Nome do jogo</span>
                  <input id="review-name" name="displayName" autocomplete="off">
                </label>
                <fieldset>
                  <legend>Executável principal</legend>
                  <div id="executable-list" class="executable-list"></div>
                </fieldset>
                <p id="recommendation" class="recommendation"></p>
                <section class="identification" aria-labelledby="identification-title">
                  <h3 id="identification-title">Identificação SteamGridDB</h3>
                  <p id="identification-message" class="panel-message" aria-live="polite"></p>
                  <div id="match-list" class="match-list"></div>
                </section>
                <section id="artwork-preview" class="artwork-preview" aria-labelledby="artwork-title" hidden>
                  <h3 id="artwork-title">Artes recomendadas</h3>
                  <div id="artwork-list" class="artwork-list"></div>
                </section>
                <div class="review-actions">
                  <button id="cancel-review" type="button">[ CANCELAR ]</button>
                </div>
              </form>
            </section>

            <footer>
              <span>HTTP :5050</span>
              <span>SEM NUVEM // SEM TELEMETRIA</span>
              <button id="refresh" type="button">[ ATUALIZAR ]</button>
            </footer>
          </main>
        </body>
        </html>
        """;

    internal const string Css = """
        :root {
          color-scheme: dark;
          --ink: #d9ffe8;
          --muted: #6f8d7b;
          --panel: #09110d;
          --line: #214631;
          --green: #58f08a;
          --amber: #ffca58;
          --red: #ff6b62;
        }
        * { box-sizing: border-box; }
        body {
          margin: 0;
          min-height: 100vh;
          color: var(--ink);
          background-color: #050806;
          background-image:
            linear-gradient(rgba(88, 240, 138, .035) 1px, transparent 1px),
            linear-gradient(90deg, rgba(88, 240, 138, .035) 1px, transparent 1px);
          background-size: 24px 24px;
          font-family: "Cascadia Mono", "Lucida Console", monospace;
        }
        body::after {
          content: "";
          position: fixed;
          inset: 0;
          pointer-events: none;
          background: repeating-linear-gradient(0deg, transparent 0 3px, rgba(0, 0, 0, .14) 3px 4px);
        }
        main { width: min(920px, calc(100% - 32px)); margin: 0 auto; padding: 56px 0 28px; }
        header { display: grid; grid-template-columns: auto 1fr auto; align-items: center; gap: 20px; margin-bottom: 34px; }
        .prompt { color: var(--green); font-size: clamp(2rem, 7vw, 4.5rem); line-height: 1; text-shadow: 4px 4px 0 #12351e; }
        .eyebrow { margin: 0 0 7px; color: var(--muted); font-size: .7rem; letter-spacing: .18em; }
        h1 { margin: 0; font-size: clamp(2rem, 7vw, 4.2rem); letter-spacing: -.08em; line-height: .9; }
        h1 span { color: var(--green); }
        .link-state { align-self: start; border: 1px solid var(--line); padding: 8px 10px; color: var(--muted); font-size: .68rem; }
        .link-state i { display: inline-block; width: 7px; height: 7px; margin-right: 6px; background: var(--green); box-shadow: 0 0 10px var(--green); }
        .terminal { border: 2px solid var(--line); background: rgba(9, 17, 13, .94); box-shadow: 8px 8px 0 #10271a; }
        .terminal-bar { display: flex; justify-content: space-between; gap: 16px; padding: 12px 16px; border-bottom: 2px solid var(--line); color: var(--muted); }
        h2 { margin: 0; font-size: .74rem; letter-spacing: .12em; font-weight: 400; }
        #clock { font-size: .74rem; font-variant-numeric: tabular-nums; }
        .status-grid { display: grid; grid-template-columns: repeat(3, 1fr); }
        article { min-height: 180px; padding: 22px; border-right: 1px solid var(--line); position: relative; }
        article:last-child { border-right: 0; }
        .index { color: var(--muted); font-size: .7rem; }
        h3 { margin: 36px 0 12px; font-size: clamp(.85rem, 2vw, 1rem); letter-spacing: .08em; }
        .value { margin: 0; color: var(--amber); font-weight: 700; font-size: 1.05rem; }
        article[data-ready="true"] .value { color: var(--green); }
        article[data-ready="false"] .value { color: var(--red); }
        article::after { content: ""; position: absolute; width: 9px; height: 9px; right: 18px; top: 20px; background: var(--amber); }
        article[data-ready="true"]::after { background: var(--green); box-shadow: 0 0 12px var(--green); }
        article[data-ready="false"]::after { background: var(--red); }
        .summary { display: flex; gap: 12px; align-items: center; min-height: 66px; padding: 16px 22px; border-top: 2px solid var(--line); }
        .summary p { margin: 0; font-size: .8rem; letter-spacing: .06em; }
        #summary-mark { color: var(--amber); }
        .summary.ready #summary-mark, .summary.ready p { color: var(--green); }
        .games, .review { margin-top: 28px; }
        .games .terminal-bar button { width: auto; margin: -8px 0; }
        .panel-message { margin: 0; padding: 20px 22px; color: var(--muted); }
        .panel-message.error { color: var(--red); }
        .game-list { display: grid; grid-template-columns: repeat(2, 1fr); }
        .candidate {
          margin: 0;
          padding: 20px 22px;
          border: 0;
          border-top: 1px solid var(--line);
          border-right: 1px solid var(--line);
          color: var(--ink);
          text-align: left;
        }
        .candidate:nth-child(even) { border-right: 0; }
        .candidate::before { content: "> "; color: var(--green); }
        #review-form { padding: 22px; }
        .field { display: grid; gap: 9px; color: var(--muted); font-size: .72rem; letter-spacing: .08em; }
        input {
          width: 100%;
          border: 1px solid var(--line);
          background: #050806;
          color: var(--ink);
          padding: 12px;
          font: inherit;
        }
        input:focus-visible { outline: 1px dashed var(--green); outline-offset: 3px; }
        fieldset { margin: 24px 0 0; padding: 0; border: 0; }
        legend { margin-bottom: 10px; color: var(--muted); font-size: .72rem; letter-spacing: .08em; }
        .executable-list { display: grid; gap: 8px; }
        .executable-option { display: flex; gap: 10px; align-items: center; padding: 12px; border: 1px solid var(--line); }
        .executable-option:has(input:checked) { border-color: var(--green); color: var(--green); }
        .executable-option input { width: auto; accent-color: var(--green); }
        .recommendation { color: var(--green); font-size: .72rem; }
        .identification, .artwork-preview { margin-top: 24px; border-top: 1px solid var(--line); padding-top: 4px; }
        .identification h3, .artwork-preview h3 { margin: 18px 0 10px; }
        .identification .panel-message { padding: 8px 0 14px; }
        .match-list { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; }
        .match-option { display: grid; grid-template-columns: 56px 1fr; align-items: center; gap: 12px; width: 100%; margin: 0; padding: 10px; border: 1px solid var(--line); text-align: left; }
        .match-option.no-cover { grid-template-columns: 1fr; }
        .match-option img { width: 56px; aspect-ratio: 2 / 3; object-fit: cover; background: #050806; }
        .artwork-list { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; }
        .artwork-item { min-height: 90px; margin: 0; padding: 10px; border: 1px solid var(--line); color: var(--muted); }
        .artwork-item img { display: block; width: 100%; max-height: 180px; object-fit: contain; background: #050806; }
        .artwork-item figcaption { margin-top: 8px; font-size: .68rem; letter-spacing: .06em; }
        .artwork-missing { display: grid; place-items: center; text-align: center; font-size: .68rem; }
        .review-actions { display: flex; justify-content: flex-end; border-top: 1px solid var(--line); margin-top: 22px; padding-top: 14px; }
        .review-actions button { margin: 0; }
        footer { display: flex; flex-wrap: wrap; align-items: center; gap: 18px; margin-top: 24px; color: var(--muted); font-size: .67rem; }
        button { margin-left: auto; border: 0; background: transparent; color: var(--green); font: inherit; cursor: pointer; padding: 8px 0; }
        button:hover, button:focus-visible { color: var(--ink); outline: 1px dashed var(--green); outline-offset: 5px; }
        @media (max-width: 650px) {
          main { padding-top: 30px; }
          header { grid-template-columns: auto 1fr; }
          .link-state { grid-column: 1 / -1; justify-self: start; }
          .status-grid { grid-template-columns: 1fr; }
          .game-list { grid-template-columns: 1fr; }
          .match-list, .artwork-list { grid-template-columns: 1fr; }
          .candidate { border-right: 0; }
          article { min-height: 120px; border-right: 0; border-bottom: 1px solid var(--line); }
          article:last-child { border-bottom: 0; }
          h3 { margin-top: 20px; }
          button { width: 100%; margin-left: 0; text-align: left; }
        }
        @media (prefers-reduced-motion: no-preference) {
          .link-state i { animation: blink 1.6s steps(2, end) infinite; }
          @keyframes blink { 50% { opacity: .25; } }
        }
        """;

    internal const string JavaScript = """
        const cards = [...document.querySelectorAll('[data-status]')];
        const summary = document.querySelector('.summary');
        const summaryMark = document.querySelector('#summary-mark');
        const summaryText = document.querySelector('#summary-text');
        const endpoint = document.body.dataset.endpoint;
        const gamesEndpoint = document.body.dataset.gamesEndpoint;
        const refreshEndpoint = document.body.dataset.refreshEndpoint;
        const gameList = document.querySelector('#game-list');
        const gamesMessage = document.querySelector('#games-message');
        const reviewPanel = document.querySelector('#game-review');
        const reviewName = document.querySelector('#review-name');
        const executableList = document.querySelector('#executable-list');
        const recommendation = document.querySelector('#recommendation');
        const identificationMessage = document.querySelector('#identification-message');
        const matchList = document.querySelector('#match-list');
        const artworkPreview = document.querySelector('#artwork-preview');
        const artworkList = document.querySelector('#artwork-list');

        function render(status) {
          cards.forEach((card) => {
            const ready = Boolean(status[card.dataset.status]);
            card.dataset.ready = ready;
            card.querySelector('.value').textContent = ready ? 'PRONTO' : 'PENDENTE';
          });
          summary.classList.toggle('ready', status.ready);
          summaryMark.textContent = status.ready ? '[ OK ]' : '[ !! ]';
          summaryText.textContent = status.ready
            ? 'PC-CONSOLE PRONTO PARA RECEBER COMANDOS'
            : 'AÇÃO NECESSÁRIA NA JANELA LOCAL DO WINDOWS';
        }

        async function refresh() {
          try {
            const response = await fetch(endpoint, { cache: 'no-store' });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            render(await response.json());
          } catch {
            cards.forEach((card) => {
              card.dataset.ready = 'false';
              card.querySelector('.value').textContent = 'OFFLINE';
            });
            summary.classList.remove('ready');
            summaryMark.textContent = '[ XX ]';
            summaryText.textContent = 'SEM RESPOSTA DO PC-CONSOLE';
          }
        }

        function closeReview() {
          reviewPanel.hidden = true;
          document.querySelector('#review-form').reset();
          executableList.replaceChildren();
          recommendation.textContent = '';
          identificationMessage.textContent = '';
          identificationMessage.classList.remove('error');
          matchList.replaceChildren();
          artworkPreview.hidden = true;
          artworkList.replaceChildren();
        }

        function cancelReview() {
          closeReview();
          gamesMessage.classList.remove('error');
          gamesMessage.textContent = 'REVISÃO CANCELADA // NENHUMA ALTERAÇÃO NA STEAM';
        }

        async function readProblem(response) {
          try {
            const problem = await response.json();
            return problem.detail || problem.title || `HTTP ${response.status}`;
          } catch {
            return `HTTP ${response.status}`;
          }
        }

        async function openReview(candidateId) {
          gamesMessage.classList.remove('error');
          gamesMessage.textContent = 'ANALISANDO EXECUTÁVEIS...';
          try {
            const response = await fetch(`${gamesEndpoint}/${candidateId}`, { cache: 'no-store' });
            if (!response.ok) throw new Error(await readProblem(response));
            const review = await response.json();
            reviewName.value = review.provisionalName;
            executableList.replaceChildren(...review.executables.map((executable) => {
              const label = document.createElement('label');
              label.className = 'executable-option';
              const input = document.createElement('input');
              input.type = 'radio';
              input.name = 'executableId';
              input.value = executable.executableId;
              input.checked = executable.executableId === review.recommendedExecutableId;
              label.append(input, document.createTextNode(executable.relativePath));
              return label;
            }));
            const recommended = review.executables.find(
              executable => executable.executableId === review.recommendedExecutableId);
            recommendation.textContent = recommended
              ? `RECOMENDADO // ${recommended.relativePath}`
              : '';
            reviewPanel.hidden = false;
            gamesMessage.textContent = 'REVISE O EXECUTÁVEL E ESCOLHA O TÍTULO CORRETO.';
            reviewName.focus();
            await loadMatches(candidateId);
          } catch (error) {
            closeReview();
            gamesMessage.classList.add('error');
            gamesMessage.textContent = error.message;
          }
        }

        async function loadMatches(candidateId) {
          identificationMessage.classList.remove('error');
          identificationMessage.textContent = 'PESQUISANDO TÍTULOS...';
          matchList.replaceChildren();
          artworkPreview.hidden = true;
          artworkList.replaceChildren();
          try {
            const response = await fetch(`${gamesEndpoint}/${candidateId}/matches`, { cache: 'no-store' });
            if (!response.ok) throw new Error(await readProblem(response));
            const matches = await response.json();
            matchList.replaceChildren(...matches.map((match) => {
              const button = document.createElement('button');
              button.type = 'button';
              button.className = `match-option${match.coverUrl ? '' : ' no-cover'}`;
              if (match.coverUrl) {
                const cover = document.createElement('img');
                cover.src = match.coverUrl;
                cover.alt = '';
                button.append(cover);
              }
              const name = document.createElement('span');
              name.textContent = match.officialName;
              button.append(name);
              button.addEventListener('click', () => loadArtwork(candidateId, match.gameId));
              return button;
            }));
            identificationMessage.textContent = matches.length === 0
              ? 'NENHUM TÍTULO ENCONTRADO // REVISE O NOME E TENTE NOVAMENTE.'
              : 'ESCOLHA EXPLICITAMENTE A EDIÇÃO CORRETA.';
          } catch (error) {
            identificationMessage.classList.add('error');
            identificationMessage.textContent = error.message;
          }
        }

        async function loadArtwork(candidateId, gameId) {
          identificationMessage.classList.remove('error');
          identificationMessage.textContent = 'CARREGANDO ARTES ESTÁTICAS...';
          artworkPreview.hidden = true;
          artworkList.replaceChildren();
          try {
            const response = await fetch(
              `${gamesEndpoint}/${candidateId}/matches/${gameId}/artwork`,
              { cache: 'no-store' });
            if (!response.ok) throw new Error(await readProblem(response));
            const artwork = await response.json();
            reviewName.value = artwork.officialName;
            const categories = [
              ['Grid vertical', artwork.verticalGrid],
              ['Grid horizontal', artwork.horizontalGrid],
              ['Hero', artwork.hero],
              ['Logo', artwork.logo],
              ['Ícone', artwork.icon],
            ];
            artworkList.replaceChildren(...categories.map(([label, asset]) => {
              if (!asset) {
                const missing = document.createElement('p');
                missing.className = 'artwork-item artwork-missing';
                missing.textContent = `${label.toLocaleUpperCase('pt-BR')} // AUSENTE`;
                return missing;
              }

              const figure = document.createElement('figure');
              figure.className = 'artwork-item';
              const image = document.createElement('img');
              image.src = asset.previewUrl;
              image.alt = `${label} de ${artwork.officialName}`;
              const caption = document.createElement('figcaption');
              caption.textContent = `${label.toLocaleUpperCase('pt-BR')} // SCORE ${asset.score}`;
              figure.append(image, caption);
              return figure;
            }));
            artworkPreview.hidden = false;
            identificationMessage.textContent = 'TÍTULO CONFIRMADO // PRÉVIA SEM ALTERAR A STEAM.';
          } catch (error) {
            identificationMessage.classList.add('error');
            identificationMessage.textContent = error.message;
          }
        }

        function renderCandidates(candidates) {
          gameList.replaceChildren(...candidates.map((candidate) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'candidate';
            button.textContent = candidate.provisionalName;
            button.addEventListener('click', () => openReview(candidate.candidateId));
            return button;
          }));
          gamesMessage.classList.remove('error');
          gamesMessage.textContent = candidates.length === 0
            ? 'NENHUMA SUBPASTA ENCONTRADA.'
            : `${candidates.length} CANDIDATO(S) ENCONTRADO(S).`;
        }

        async function loadGames(forceRefresh = false) {
          closeReview();
          gamesMessage.classList.remove('error');
          gamesMessage.textContent = forceRefresh ? 'ATUALIZANDO DESCOBERTA...' : 'LENDO PASTA RAIZ...';
          try {
            const response = await fetch(forceRefresh ? refreshEndpoint : gamesEndpoint, {
              method: forceRefresh ? 'POST' : 'GET',
              cache: 'no-store',
            });
            if (!response.ok) throw new Error(await readProblem(response));
            renderCandidates(await response.json());
          } catch (error) {
            gameList.replaceChildren();
            gamesMessage.classList.add('error');
            gamesMessage.textContent = error.message;
          }
        }

        function tick() {
          document.querySelector('#clock').textContent = new Date().toLocaleTimeString('pt-BR');
        }

        document.querySelector('#refresh').addEventListener('click', refresh);
        document.querySelector('#discover-refresh').addEventListener('click', () => loadGames(true));
        document.querySelector('#cancel-review').addEventListener('click', cancelReview);
        tick();
        refresh();
        loadGames();
        setInterval(tick, 1000);
        setInterval(refresh, 5000);
        """;
}
