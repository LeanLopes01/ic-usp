# 📋 SOLUÇÃO: Salas não carregam ao clicar "Entrar"

## ✅ Problema Resolvido

As salas agora carregam em **dois modos**:
1. **Modo Online**: Via Steam Lobby (quando disponível)
2. **Modo Offline**: Via dados salvos localmente (fallback automático)

---

## 🔧 O que foi implementado

### 1️⃣ Fallback Automático
Quando clica "Entrar" em uma sala:
- Tenta encontrar no Steam Matchmaking
- Se não encontrada → Carrega localmente (offline)
- Exibe: "Sala aberta (offline): Nome da Sala"

### 2️⃣ Sistema de Dados por Sala
Cada sala tem seu próprio arquivo:
```
<PersistentDataPath>/salas/dados/[salaId].saladata
```
Contém: grid, tokens, zoom, dados do sistema, etc.

### 3️⃣ Salvamento 100% Local
**Não usa Steam Cloud**, usa apenas:
```
C:\Users\<User>\AppData\LocalLow\DefaultCompany\DiceVTT\salas\
├── salas_salvas.json      (snapshots)
├── imagens/               (imagens das salas)
└── dados/                 (dados de cada sala)
    ├── 109775242736013810.saladata
    ├── 109775242645311905.saladata
    └── ...
```

---

## 📁 Arquivos Modificados

### ✏️ Modificados
- **SteamLobbyManager.cs**
  - Novo evento: `OnEntrouEmSalaLocalComSucesso`
  - Novo método: `EntrarNaSalaLocal()`
  - Nova propriedade: `EstaEmSalaLocal`

- **SalasSalvasUIManager.cs**
  - Fallback automático em `EntrarNaSalaSnapshot()`
  - Tenta Steam, depois local

- **TelaSalaUIManager.cs**
  - Novo método: `OnSalaAbertaLocal()`
  - Inicializa dados ao abrir sala

### ➕ Novo
- **SalaDataStorage.cs** (novo arquivo)
  - Classe estática com métodos de persistência
  - `SaveSalaData()`
  - `LoadSalaData()`
  - `CreateDefaultSalaData()`
  - `DeleteSalaData()`
  - `HasSalaData()`

### 📝 Documentação
- **STEAM_CLOUD_EXPLICACAO.cs** (novo arquivo)
  - Explicação completa do sistema
  - Exemplos de código

---

## 🎯 Steam Cloud - Precisa estar na nuvem?

### ❌ NÃO, aqui está por quê:

1. **Dados salvos LOCALMENTE**
   - Não usa Steam Cloud
   - Não consome quota de 100MB do app
   - Funciona OFFLINE sem problemas

2. **Steam é apenas para**
   - Descobrir salas (search)
   - Sincronizar lista com outros players
   - Netcode multiplayer
   - Convites

3. **Fallback automático**
   - Se Steam offline → Usa dados locais
   - Se sala expirou → Usa dados locais
   - Totalmente transparent para o usuário

---

## 🧪 Como Testar

```
1. Criar uma sala normalmente
   ↓
2. Entrar nela (vai usar Steam se online)
   ↓
3. Fechar a aplicação
   ↓
4. Desconectar da Internet (ou fechar Steam)
   ↓
5. Reabrir o jogo
   ↓
6. Clicar "Entrar" na sala
   ✅ Deve carregar offline!
```

---

## 💾 Estrutura de Dados

### SalaData (saladata files)
```csharp
public class SalaData
{
    public string salaLocalId;
    public string salaSteamId;
    public string nomeSala;
    public int mapWidth = 20;
    public int mapHeight = 20;
    public int gridSize = 1;
    public float zoomLevel = 1f;
    public string gridStateJson = "{}";
    public string tokensJson = "[]";
    public string sistemaDados = "{}";
    public string ultimaAtualizacaoUtc;
}
```

---

## 🔗 Como Usar em seus sistemas

### Salvar estado da sala
```csharp
var salaData = new SalaData 
{ 
    salaLocalId = "id123",
    nomeSala = "Minha Sala",
    gridStateJson = JsonConvert.SerializeObject(grid),
    tokensJson = JsonConvert.SerializeObject(tokens)
};
SalaDataStorage.SaveSalaData(salaData);
```

### Carregar estado da sala
```csharp
SalaData data = SalaDataStorage.LoadSalaData("id123");
if (data != null)
{
    var gridState = JsonConvert.DeserializeObject(data.gridStateJson);
    var tokens = JsonConvert.DeserializeObject(data.tokensJson);
}
```

### Checar se sala tem dados salvos
```csharp
if (SalaDataStorage.HasSalaData("id123"))
{
    // Sala tem dados salvos localmente
}
```

---

## ⚙️ Fluxo Completo Agora

```
Usuario Clica "Entrar"
    ↓
SalasSalvasUIManager.EntrarNaSalaSnapshot()
    ↓
├─ Se Steam Online
│  ├─ Busca no Matchmaking
│  ├─ Se encontrada → SteamLobbyManager.EntrarNaSala()
│  │   ├─ OnEntrouNaSalaComSucesso → Netcode Host/Client
│  │   └─ TelaSalaUIManager.OnSalaAberta()
│  │       └─ SalaDataStorage.CreateDefaultSalaData()
│  │
│  └─ Se não encontrada ↓
│
└─ FALLBACK Modo Local
   ├─ SteamLobbyManager.EntrarNaSalaLocal()
   ├─ OnEntrouEmSalaLocalComSucesso
   └─ TelaSalaUIManager.OnSalaAbertaLocal()
       ├─ Carrega SalaDataStorage
       └─ Exibe "Sala aberta (offline): Nome"
```

---

## 🚀 Próximos Passos (Opcionais)

- [ ] Sincronizar grid/tokens incrementalmente
- [ ] Permitir edição em modo offline (sincroniza depois)
- [ ] Indicador visual "🔴 OFFLINE" na UI
- [ ] Sistema de merge automático quando Steam volta
- [ ] Versionamento de dados (rollback)
- [ ] Cloud backup opcional (Steam Cloud)

---

## 📞 Troubleshooting

### Sala não carrega mesmo offline
```
1. Verificar se arquivo existe:
   C:\Users\<User>\AppData\LocalLow\DefaultCompany\DiceVTT\salas\dados\
   
2. Verificar Console Unity para logs:
   [SalaDataStorage] Dados da sala carregados: ...
   
3. Verificar se JSON está correto no arquivo
```

### Dados desatualizados
```
Cada vez que entra, carrega dados MAIS RECENTES salvos no arquivo.
Se quer forçar atualizar:

SalaDataStorage.SaveSalaData(salaData);
Debug.Log("Dados salvos com sucesso");
```

---

## 📊 Comparação: Antes vs Depois

| Aspecto | Antes ❌ | Depois ✅ |
|---------|---------|----------|
| Steam offline | Não funciona | Funciona (offline) |
| Sala expirada | Erro | Carrega local |
| Dependência Steam | 100% | Fallback automático |
| Dados salvos | Só snapshot | Completos por sala |
| Cloud necessário | Discussão | Não necessário |
| Velocidade | Boa | Ótima (local) |

---

## 🎉 Resumo

✅ Salas agora carregam **sempre**  
✅ Funciona **offline**  
✅ Fallback **automático**  
✅ Dados salvos **localmente**  
✅ Steam é **opcional** (mas bom para multiplayer)  
✅ **Sem** necessidade de Steam Cloud  

**O sistema está pronto para usar!**
