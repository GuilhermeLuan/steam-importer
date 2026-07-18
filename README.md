# Steam Import

Aplicativo WPF para adicionar manualmente jogos externos como atalhos não Steam,
com revisão obrigatória e preservação dos atalhos existentes.

## Primeiro fluxo disponível

1. Abra o aplicativo no Windows 10 ou 11.
2. Confirme a instalação e a conta local da Steam. Se a instalação não for
   detectada, selecione manualmente a pasta que contém `steam.exe`.
3. Selecione a pasta do jogo.
4. Revise o nome e o executável recomendado.
5. Feche completamente a Steam e confirme a importação.
6. Abra a Steam e confira o jogo na biblioteca.

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

Este primeiro corte não inclui SteamGridDB, artes, monitoramento automático de
pastas, bandeja do sistema, remoção de jogos nem encerramento automático da Steam.
