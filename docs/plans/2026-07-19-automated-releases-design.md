# Releases automatizadas por tag

**Data:** 19 de julho de 2026

## Objetivo

Transformar uma tag semântica `vX.Y.Z` em uma GitHub Release verificável, sem
publicar automaticamente todo merge da `main`. O fluxo cotidiano continua
usando pull requests e o workflow Windows atual; uma release exige a decisão
explícita de criar e enviar uma tag.

## Desenho

O workflow `windows.yml` também reage a pushes de tags `v*.*.*`. O job
`build-test-publish` permanece único e continua restaurando, compilando,
testando e produzindo `SteamImport-win-x64.zip`. Isso garante que o binário da
release foi construído e testado a partir do commit apontado pela própria tag.

Um segundo job, `release`, depende do build e só executa quando a referência
começa com `refs/tags/v`. Esse job baixa o artefato do mesmo workflow, gera
`SHA256SUMS.txt` e chama `gh release create` com `--verify-tag` e
`--generate-notes`. Apenas esse job recebe `contents: write`; o restante do
workflow conserva `contents: read`. Falha de build, testes, empacotamento ou
checksum impede a publicação.

## Operação e validação

Depois que a automação estiver na `main`, uma versão será lançada com:

```bash
git switch main
git pull --ff-only
git tag -a v0.2.0 -m "Steam Import v0.2.0"
git push origin v0.2.0
```

A validação local confere a sintaxe do workflow, os filtros de evento, a
dependência entre jobs, as permissões mínimas e os argumentos do GitHub CLI. A
validação definitiva será um push de tag posterior ao merge; o job deverá
produzir ZIP, checksum e Release associados ao mesmo SHA.
