# SystemCare

Tägliche, protokollierte Windows-Systempflege mit Windows Update, optionalen Treiber-Updates über Windows Update, WinGet-Updates, alterssicherer Temp-Bereinigung, fester Consumer-App-Allowlist und vorsichtiger Gaming-Optimierung.

## Oberfläche

Ein Doppelklick auf `SystemCare.exe` öffnet das Dashboard im dunklen Navy-Design mit den Seiten **Automatik-Übersicht**, **Updates**, **Bereinigung**, **Empfohlene Verbesserungen** und **Einstellungen**. Updates können einzeln oder gesammelt gescannt und installiert werden. Die Bereinigung listet temporäre Dateien, alte Downloads, große Dateien sowie Duplikate von Dokumenten, Audio, Videos und Fotos auf. Elemente stehen zunächst auf **Behalten**; einzelne oder nach Kategorie ausgewählte Dateien werden nur nach Bestätigung in den Papierkorb verschoben. Die Kommandozeilenoptionen bleiben für den täglichen Hintergrund-Task und Automatisierung erhalten.

## Sichere Inbetriebnahme

```powershell
.\SystemCare.exe --self-test
.\SystemCare.exe --run-once --dry-run
```

Danach als Administrator den Task installieren:

```powershell
Start-Process powershell -Verb RunAs -ArgumentList '-File', '.\Install-DailyTask.ps1'
```

Standardzeit ist 03:15 Uhr; die Konfiguration liegt unter `%LOCALAPPDATA%\GamingSystemCare\config.json`. Im Einstellungsbereich lassen sich Uhrzeit, täglich/wöchentlich, Wochentag und alle Automatikfunktionen ändern. Es wird kein automatischer Neustart durchgeführt. Logs und Backups bleiben lokal unter `%LOCALAPPDATA%\GamingSystemCare\`.

Die Debloat-Allowlist entfernt keine Xbox-/Gaming-Services, keinen Microsoft Store und keine Security-Komponenten. BIOS-/Firmware-Updates und beliebige Internet-Downloads werden nicht automatisch ausgeführt.
