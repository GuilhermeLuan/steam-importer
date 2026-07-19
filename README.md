# Steam Import

Aplicativo WPF para adicionar manualmente jogos externos como atalhos não Steam,
com revisão obrigatória e preservação dos atalhos existentes.

## Primeiro fluxo disponível

1. Abra o aplicativo no PC-console com Windows 10 ou 11.
2. Selecione a pasta raiz dos jogos, informe sua chave pessoal do SteamGridDB e
   confirme a instalação e a conta local da Steam. Se a instalação não for
   detectada, selecione manualmente a pasta que contém `steam.exe`.
3. Salve a configuração e abra, em outro dispositivo da rede privada, um dos
   endereços `http://<IP-DO-PC>:5050` mostrados na janela.
4. Consulte na página remota se configuração, Steam e conta estão prontas.
5. Na página remota, atualize a descoberta, selecione uma subpasta e revise o
   executável recomendado. Escolha explicitamente o título correto entre os
   resultados do SteamGridDB; o nome oficial e a melhor arte estática de cada
   categoria disponível aparecem em uma prévia. Cancelar a revisão não altera
   a Steam.
6. Para a importação local existente, selecione a pasta do jogo, revise nome e
   executável, feche completamente a Steam e confirme a operação.

A configuração fica em `%LOCALAPPDATA%\SteamImport\config.json`. A chave do
SteamGridDB é protegida para o usuário atual do Windows, não é enviada ao
navegador e não é registrada nos logs. Ao salvar, o aplicativo também registra
sua inicialização automática para esse usuário.

A importação é bloqueada enquanto `steam.exe` está em execução. Antes de alterar
um `shortcuts.vdf` existente, o aplicativo mantém até cinco backups em
`%LOCALAPPDATA%\SteamImport\Backups\<conta>`.

## Logs

Cada execução cria um arquivo em `%LOCALAPPDATA%\SteamImport\Logs`. O aplicativo
mantém os cinco logs mais recentes e registra as etapas principais da detecção e
da importação, além do stack trace completo quando ocorre uma falha. Não há envio
de telemetria. Para diagnosticar um erro, reproduza-o e compartilhe o arquivo de
log mais recente.

## Desenvolvimento

Requisitos:

- SDK .NET 10;
- Windows para executar a interface WPF.

```powershell
dotnet restore SteamImport.sln
dotnet build SteamImport.sln
dotnet test SteamImport.sln
dotnet run --project src/SteamImport.App
```

O workflow `Windows build` executa build e testes e publica um ZIP contendo um
único `SteamImport.exe` self-contained para `win-x64`.

## Escopo atual

O fluxo remoto atual identifica o jogo e pré-visualiza artes do SteamGridDB,
mas ainda não importa pelo navegador. Monitoramento automático de pastas,
bandeja do sistema, remoção de jogos e encerramento automático da Steam também
continuam fora do escopo atual.
