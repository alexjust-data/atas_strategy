

# grep-cheatsheet 

> Supongo el archivo `EMERGENCY_ATAS_LOG.txt` en el cwd. Cambia patrones/hora/bar/uid según necesites.

## 0) Filtrar por hora/minuto

```bash
grep -n "00:48:" EMERGENCY_ATAS_LOG.txt | head
grep -nE "00:48:(2[0-9]|3[0-9])" EMERGENCY_ATAS_LOG.txt
```

## 1) Traza completa de una señal + ejecución (UID)

```bash
UID="61eb71ef"   # pon tu uid base (los logs suelen truncarlo)
grep -nE "CAPTURE|PENDING|PROCESSING PENDING|EXPIRED|MARKET submitted|CONF#|ABORT ENTRY|GUARD|ZOMBIE|BRACKETS|OnOrderChanged|Trade candado" EMERGENCY_ATAS_LOG.txt \
| grep "$UID" -n
```

## 2) Diagnóstico rápido N→N+1 (una ventana de bars)

```bash
BAR=17621
grep -nE "CAPTURE.*$BAR|PENDING.*$BAR|PROCESSING PENDING|EXPIRED|MARKET submitted" EMERGENCY_ATAS_LOG.txt -n
```

## 3) Confluencias

```bash
grep -nE "CONF#1|Genial slope|CONF#2|EMA8 vs W8" EMERGENCY_ATAS_LOG.txt
```

## 4) Guardia / anti-solapes / cooldown

```bash
grep -nE "OnlyOnePosition|ZOMBIE CANCEL|cooldown|COOLDOWN" EMERGENCY_ATAS_LOG.txt
```

## 5) Entrada y brackets (core)

```bash
grep -nE "MARKET submitted|BRACKETS ATTACHED|BRACKETS NOT ATTACHED|ReconcileBracketsWithNet|Removed .* from _liveOrders" EMERGENCY_ATAS_LOG.txt
```

## 6) OnOrderChanged / estados y cancelaciones

```bash
grep -nE "OnOrderChanged: 468ENTRY|OnOrderChanged: 468TP|OnOrderChanged: 468SL|status=Filled|status=PartlyFilled|status=Placed|status=Cancelled" EMERGENCY_ATAS_LOG.txt
```

## 7) “Secuencia mortal” (detección rápida)

```bash
# 7.1 localizar anexado de brackets en la ventana problemática
grep -n "BRACKETS ATTACHED (from net=" EMERGENCY_ATAS_LOG.txt | tail -20

# 7.2 buscar inmediatamente después: net=0, liberación de candado y cancelaciones
grep -nE "GetNetPosition: returning 0|Trade candado RELEASED|ANTI-FLAT|status=Cancelled" EMERGENCY_ATAS_LOG.txt | sed -n 'p'
```

## 8) Anti-flat window / confirmación de plano real

```bash
grep -nE "ANTI-FLAT|flat confirmed|FLAT CONFIRMED|Trade lock RELEASED" EMERGENCY_ATAS_LOG.txt
```

## 9) Fallbacks de net (cuando portfolio va tarde)

```bash
grep -nE "FALLBACK: Using FilledQuantity|FALLBACK: Using order\.QuantityToFill|POST-FILL CHECK: net=" EMERGENCY_ATAS_LOG.txt
```

## 10) Auditoría compacta por operación (junto la secuencia típica)

```bash
grep -nE "CAPTURE|PROCESSING PENDING @N\+1|CONF#|ABORT ENTRY|MARKET submitted|OnOrderChanged|BRACKETS ATTACHED|ReconcileBracketsWithNet|ANTI-FLAT|Trade (candado|lock) RELEASED|status=Cancelled" EMERGENCY_ATAS_LOG.txt
```


