# SystemCare

Tägliche, protokollierte Windows-Systempflege mit Windows Update, optionalen Treiber-Updates über Windows Update, WinGet-Updates, alterssicherer Temp-Bereinigung, fester Consumer-App-Allowlist und vorsichtiger Gaming-Optimierung.

## Oberfläche

Ein Doppelklick auf `SystemCare.exe` öffnet das Dashboard mit Task-Status, nächstem Lauf, Dry-Run-Prüfung sowie Schaltflächen für Konfiguration und Logs. Die Kommandozeilenoptionen bleiben für den täglichen Hintergrund-Task und Automatisierung erhalten.

## Sichere Inbetriebnahme

```powershell
.\SystemCare.exe --self-test
.\SystemCare.exe --run-once --dry-run
```

Danach als Administrator den Task installieren:

```powershell
Start-Process powershell -Verb RunAs -ArgumentList '-File', '.\Install-DailyTask.ps1'
```

Standardzeit ist 03:15 Uhr; die Konfiguration liegt unter `%LOCALAPPDATA%\GamingSystemCare\config.json`. Es wird kein automatischer Neustart durchgeführt. Logs und Backups bleiben lokal unter `%LOCALAPPDATA%\GamingSystemCare\`.

Die Debloat-Allowlist entfernt keine Xbox-/Gaming-Services, keinen Microsoft Store und keine Security-Komponenten. BIOS-/Firmware-Updates und beliebige Internet-Downloads werden nicht automatisch ausgeführt.
