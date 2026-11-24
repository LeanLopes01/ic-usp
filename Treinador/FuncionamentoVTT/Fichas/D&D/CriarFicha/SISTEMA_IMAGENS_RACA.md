# 🎨 Sistema de Imagens Automáticas de Raça

## Objetivo
Quando um usuário seleciona uma raça durante a criação de ficha, a imagem correspondente é carregada automaticamente (se existir). O usuário pode sempre selecionar sua própria imagem depois.

## Como Funciona

### 1. **Pasta de Retratos**
As imagens devem ser salvas em:
```
C:\Users\[SeuUsuário]\AppData\Local\Low\[CompanhiaGo]\[JogoGo]\retratos\
```

Ou no diretório equivalente (Application.persistentDataPath):
- **Windows**: `C:\Users\[User]\AppData\Local\Low\DefaultCompany\DiceVTT\retratos\`
- **Mac**: `~/Library/Application Support/DefaultCompany/DiceVTT/retratos/`
- **Linux**: `~/.config/unity3d/DefaultCompany/DiceVTT/retratos/`

### 2. **Naming Convention**
As imagens devem ter o mesmo nome da raça (sem espaços, minúsculas):
- Raça: "Half-Orc" → Arquivo: `halforc.png`
- Raça: "High Elf" → Arquivo: `highelf.png`
- Raça: "Dragonborn" → Arquivo: `dragonborn.png`

**Extensões suportadas**: `.png`, `.jpg`, `.jpeg`, `.gif`, `.bmp`

### 3. **Fluxo Automático**

```
[Usuário seleciona raça] 
    ↓
[RaceSelector.TentarCarregarImagemAutomaticaDaRaca()]
    ↓
Verifica se usuário já tem imagem?
    ├─ SIM: Mantém a imagem do usuário (não sobrescreve)
    └─ NÃO: Procura em Application.persistentDataPath/retratos/
         ├─ Encontrou: Carrega automaticamente
         └─ Não encontrou: Log de aviso, usuário escolhe manualmente
```

### 4. **Controles do Usuário**
- ✅ Sempre pode clicar em "Selecionar Imagem" para escolher outra
- ✅ Se já tem uma imagem selecionada, a raça não sobrescreve
- ✅ Pode trocar de raça (novo retrato automático, se existir)

## Exemplos de Estrutura

```
retratos/
├── human.png
├── elf.png
├── dwarf.png
├── halforc.png
├── tiefling.png
├── dragonborn.png
├── gnome.png
├── halfelf.png
├── halfling.png
└── [outros formatos]
```

## Códigos Envolvidos

### RaceSelector.cs
- `TentarCarregarImagemAutomaticaDaRaca()`: Carrega imagem caso não exista
- Chamado quando raça é confirmada em `CriarPasso_1_Racas()`

### Token ImageSelector.cs
- `DefinirImagemDoArquivo(string caminho)`: Carrega e exibe a imagem

### GlobalFichaData.cs
- `caminhoImagemToken`: Armazena o caminho da imagem selecionada

## Adicionar Imagens

1. Crie a pasta `retratos` em:
   - Local: `Application.persistentDataPath`
   - Ou manualmente no Windows: `C:\Users\[Seu Usuário]\AppData\Local\Low\DefaultCompany\DiceVTT\retratos\`

2. Salve imagens das raças com nomes correspondentes (lowercase, sem espaços)

3. Teste selecionando uma raça na criação de ficha

## Debug

Abra o Console do Unity e procure por logs:
```
[RaceSelector] ✓ Imagem de raça carregada: ...
[RaceSelector] Nenhuma imagem encontrada para raça '...'
[RaceSelector] Usuário já tem uma imagem selecionada - não carregando automática.
```

## Status: ✓ IMPLEMENTADO

O sistema está conectado e pronto para usar. Basta adicionar as imagens nas pastas corretas.
