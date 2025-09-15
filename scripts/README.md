# 🚀 MyAtas Deployment Scripts

Scripts automatizados para compilar y desplegar a ATAS.

## 📦 Scripts disponibles

### `deploy_all.ps1` ⭐ (Recomendado)
Despliega tanto indicadores como estrategias en orden correcto.
```powershell
./scripts/deploy_all.ps1
```

### `deploy_indicators.ps1` 
Solo despliega indicadores a `%APPDATA%\ATAS\Indicators\`
```powershell
./scripts/deploy_indicators.ps1  
```

### `deploy_strategies.ps1`
Solo despliega estrategias a `%APPDATA%\ATAS\Strategies\`
```powershell
./scripts/deploy_strategies.ps1
```

### `clean_atas.ps1`
Limpia archivos MyAtas viejos de ambas carpetas ATAS
```powershell
./scripts/clean_atas.ps1
```

## 🔄 Flujo de trabajo típico

1. **Desarrollo**: Modifica código en `MyAtas.Indicators/` o `MyAtas.Strategies/`
2. **Deploy**: `./scripts/deploy_all.ps1`  
3. **Test en ATAS**: Cierra/abre ATAS y verifica logs

## 🎯 Qué hace cada script

- **Compila** los proyectos en modo Debug
- **Copia** DLLs + PDBs a carpetas ATAS correctas
- **Incluye dependencias** (MyAtas.Shared.dll)
- **Logs coloridos** para seguimiento visual

## 📋 Archivos desplegados

**Indicators folder:**
- MyAtas.Indicators.dll + .pdb
- MyAtas.Shared.dll

**Strategies folder:**  
- MyAtas.Strategies.dll + .pdb
- MyAtas.Indicators.dll (referencia)
- MyAtas.Shared.dll