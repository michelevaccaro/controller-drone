# Controller Drone

Applicazione Windows per usare il radiocomando del DJI Mavic Mini originale (MR1SD25) come joystick nei simulatori. Legge la porta seriale USB DJI e invia gli input al dispositivo virtuale vJoy già installato.

## Avvio

1. Verifica che Windows veda il radiocomando sotto **Porte (COM e LPT)**. La porta predefinita è **COM5**.
2. In **Configure vJoy**, abilita **Device 1**, gli assi **X, Y, Rx, Ry**.
3. Chiudi DJI Assistant 2, il ponte PowerShell precedente e vJoy Feeder. Scarica il sorgente, estrai lo ZIP ed esegui **Build.cmd** per generare `dist\ControllerDrone.exe`. Il compilatore di .NET Framework è già incluso nelle installazioni standard di Windows 11 x64.
4. Avvia **ControllerDrone.exe**. Alla prima esecuzione, il programma chiede di calibrare i quattro assi degli stick. La calibrazione viene riutilizzata agli avvii successivi.
5. Controlla il risultato con **Monitor vJoy** oppure **Win + R → joy.cpl**. Assegna i comandi nelle impostazioni del simulatore. Per uscire premi **Ctrl+C** nella console dell'app.

Per usare una porta diversa o ripetere la calibrazione, avvia da PowerShell:

```powershell
.\dist\ControllerDrone.exe --port COM5 --recalibrate
```

## Comandi esposti

| Comando fisico | Input vJoy |
| --- | --- |
| Stick sinistro | X, Y |
| Stick destro | Rx, Ry |

L'app usa i quattro assi degli stick. I profili precedenti con una quinta associazione o con pulsanti vengono letti conservando la calibrazione dei quattro assi.

## Prestazioni e diagnosi

L'app usa la procedura seriale verificata sul radiocomando e invia un unico report vJoy per ogni risposta valida. La console mostra frequenza, risposte mancanti, tempo medio di risposta seriale e intervallo massimo osservato tra report. La misurazione termina all'uscita dell'app.

Nella cartella **tools** ci sono il test indipendente di COM5 e il precedente ponte PowerShell. Il test seriale sul radiocomando ha raggiunto **44,1 risposte/s (441/441)** in una fase di 10 secondi. La frequenza della nuova app va verificata su Windows con l'hardware collegato.

```powershell
.\dist\ControllerDrone.exe --self-test
```

Riferimenti del protocollo e dell'interfaccia: [miniDjiController](https://github.com/hjstn/miniDjiController), [MavicMiniControllerAsGamepad](https://github.com/RomanLut/MavicMiniControllerAsGamepad), [vJoy](https://github.com/BrunnerInnovation/vJoy).
