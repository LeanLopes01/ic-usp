/*
DOCUMENTAÇÃO: STEAM + SALVAMENTO LOCAL
=======================================

Resposta à pergunta: "Steam aceita fazer esse estilo? Precisa estar na nuvem?"

✅ SIM, o sistema funciona assim:

  ==========================================
  1. DADOS SALVOS LOCALMENTE (sem Steam Cloud)
  ==========================================
  
  📁 Local Storage Path:
     <Application.persistentDataPath>/salas/
     ├── salas_salvas.json         (snapshots)
     ├── imagens/                  (imagens das salas)
     └── dados/                    (dados de sala)
         ├── [salaId].saladata     ← Novo sistema
         └── [salaId].saladata
  
  ✅ Vantagens:
     - Funciona OFFLINE (sem precisar de conexão Steam)
     - Rápido (acesso direto ao disco local)
     - Não consome quota de Steam Cloud (100MB/app)
     - Dados persistem mesmo se conta Steam deletar lobby
  
  ==========================================
  2. COMO FUNCIONA COM STEAM AGORA
  ==========================================
  
  Fluxo Ao Entrar em Sala:
  
  ┌─ Entrar em Sala
  │
  ├─ Tentar via STEAM (online)
  │  ├─ Se encontrada → Join + Netcode (Hub de Lobby)
  │  └─ Se não encontrada ↓
  │
  └─ FALLBACK → Modo Local (offline)
     ├─ Carrega SalaLocalSnapshot
     ├─ Carrega SalaData do arquivo local
     └─ Exibe: "Sala aberta (offline): Nome"
  
  ==========================================
  3. DADOS SINCRONIZADOS vs LOCAIS
  ==========================================
  
  Snapshot da Sala (salas_salvas.json):
  {
    "salaSteamId": "109775242736013810",     ← ID do Lobby Steam (pode expirar)
    "nomeSala": "TESTE1",
    "mestreNome": "Anakin Skywalker",
    "imagemSalaPath": "...saladata",
    ... (dados básicos)
  }
  
  Dados Completos (dados/[salaId].saladata):
  {
    "salaLocalId": "abc123",
    "nomeSala": "TESTE1",
    "mapWidth": 20,
    "mapHeight": 20,
    "gridStateJson": "{}",
    "tokensJson": "[...]",
    "sistemaDados": "{...}",
    "ultimaAtualizacaoUtc": "2026-04-22T20:03:40..."
  }
  
  ==========================================
  4. O QUE STEAM FAZ AGORA
  ==========================================
  
  Steam Lobby é apenas para:
  ✅ Descobrir salas (lobby search)
  ✅ Sincronizar lista de salas com outros players
  ✅ Netcode multiplayer (se ambos estão online)
  ✅ Convites via overlay (Shift+Tab)
  
  Steam Cloud NÃO é usado:
  ❌ Dados de sala (já salvos localmente)
  ❌ Estado do grid
  ❌ Tokens
  ❌ Mapa
  
  ==========================================
  5. LIMITE DE STEAM CLOUD
  ==========================================
  
  Steam Cloud Quota: 100MB por app
  
  Se quiser usar Steam Cloud no futuro:
  - Não é necessário agora
  - Poderia sincronizar dados opcionalmente
  - Local storage é a fonte de verdade
  - Fallback automático se Steam indisponível
  
  ==========================================
  6. ESTRUTURA DE PERSISTÊNCIA
  ==========================================
  
  Caminhos Salvos:
  
  Windows:
  %AppData%/LocalLow/DefaultCompany/DiceVTT/salas/
  C:\\Users\\<User>\\AppData\\LocalLow\\DefaultCompany\\DiceVTT\\salas\\
  
  Linux:
  ~/.config/unity3d/DefaultCompany/DiceVTT/salas/
  
  macOS:
  ~/Library/Application Support/DefaultCompany/DiceVTT/salas/
  
  ==========================================
  7. CÓDIGO PARA USAR O SISTEMA
  ==========================================
  
  // Ao entrar em uma sala (automático agora):
  OnEntrouEmSalaLocalComSucesso?.Invoke(salaLocalSnapshot);
  
  // Salvar dados de sala:
  var salaData = new SalaData {
    salaLocalId = "id123",
    nomeSala = "Minha Sala",
    mapWidth = 30,
    mapHeight = 30,
    gridStateJson = JsonConvert.SerializeObject(gridState),
    tokensJson = JsonConvert.SerializeObject(tokens)
  };
  SalaDataStorage.SaveSalaData(salaData);
  
  // Carregar dados:
  SalaData data = SalaDataStorage.LoadSalaData("id123");
  if (data != null) {
    var gridState = JsonConvert.DeserializeObject(data.gridStateJson);
    var tokens = JsonConvert.DeserializeObject(data.tokensJson);
  }
  
  ==========================================
  8. CHECKLIST: ESTÁ PRONTO PARA USAR?
  ==========================================
  
  ✅ Salvamento local: SIM
  ✅ Fallback offline: SIM
  ✅ Dados por sala: SIM
  ✅ Snapshots: SIM
  ✅ Imagens persistidas: SIM
  
  ⏳ Ainda Pode Fazer (Opcional):
     - Sincronizar com Steam Cloud
     - Merge de dados quando Steam volta
     - Versionamento de dados
  
  ==========================================
  
EXEMPLOS DE CÓDIGO (usar em seus sistemas):
  
// Salvar estado da sala
var salaData = new SalaData 
{ 
  salaLocalId = "id123",
  nomeSala = "Minha Sala",
  gridStateJson = JsonConvert.SerializeObject(grid),
  tokensJson = JsonConvert.SerializeObject(tokens)
};
SalaDataStorage.SaveSalaData(salaData);

// Carregar estado da sala
SalaData data = SalaDataStorage.LoadSalaData("id123");
if (data != null)
{
  var gridState = JsonConvert.DeserializeObject(data.gridStateJson);
  var tokens = JsonConvert.DeserializeObject(data.tokensJson);
}

// Checar se sala tem dados salvos
if (SalaDataStorage.HasSalaData("id123"))
{
  // Sala tem dados salvos localmente
}

*/
