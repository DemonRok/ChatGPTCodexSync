# ChatGPTCodexSync

Utility Windows portabile per backup e ripristino della cartella `%USERPROFILE%\.codex` usata da ChatGPT Desktop / Codex.

## Release

Le release GitHub vengono generate automaticamente dal workflow `.github/workflows/release.yml`.

Per pubblicare una versione:

```text
git tag v0.1.0
git push origin v0.1.0
```

Il workflow compila, esegue i test, pubblica l'app Windows x64 self-contained e allega uno ZIP alla GitHub Release.
