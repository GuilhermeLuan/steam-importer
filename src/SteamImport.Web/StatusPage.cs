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
        <body data-endpoint="/api/status">
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
        footer { display: flex; flex-wrap: wrap; align-items: center; gap: 18px; margin-top: 24px; color: var(--muted); font-size: .67rem; }
        button { margin-left: auto; border: 0; background: transparent; color: var(--green); font: inherit; cursor: pointer; padding: 8px 0; }
        button:hover, button:focus-visible { color: var(--ink); outline: 1px dashed var(--green); outline-offset: 5px; }
        @media (max-width: 650px) {
          main { padding-top: 30px; }
          header { grid-template-columns: auto 1fr; }
          .link-state { grid-column: 1 / -1; justify-self: start; }
          .status-grid { grid-template-columns: 1fr; }
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

        function tick() {
          document.querySelector('#clock').textContent = new Date().toLocaleTimeString('pt-BR');
        }

        document.querySelector('#refresh').addEventListener('click', refresh);
        tick();
        refresh();
        setInterval(tick, 1000);
        setInterval(refresh, 5000);
        """;
}
