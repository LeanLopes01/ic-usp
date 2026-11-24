# 🎮 Configuração de GameObjects - Sistema de Imagens

## 📍 Hierarquia de GameObjects Necessária

```
TelaCriarFichaDnd (Cena)
├── CriacaoFichaProgressaoDnd (Script)
│   ├── [SerializeField] tokenImageSelector ← AQUI DRAGGA TokenImageSelector
│   ├── [SerializeField] criacaoFichaProgressao
│   └── ... outros fields
│
├── RaceSelector (Script)
│   └── Chama: CriacaoFichaProgressaoDnd.ObterSeletorImagem()
│       └── Chama: TokenImageSelector.DefinirImagemDoArquivo()
│
└── TokenImageSelector (GameObject com Script)
    ├── [SerializeField] botaoSelecionarImagem
    ├── [SerializeField] imagemTokenDisplay (Image)
    ├── [SerializeField] textoNomeArquivo (TextMeshPro)
    └── [SerializeField] caminhoImagemInput (InputField)
```

## 🔧 O que você precisa fazer:

### 1. **Verificar se CriacaoFichaProgressaoDnd tem TokenImageSelector atribuído:**

- Abra sua cena **"TelaCriarFichaDnd"** (ou similar)
- Procure pelo GameObject que tem o script **CriacaoFichaProgressaoDnd**
- No Inspector, procure pelo campo **"Token Image Selector"** (em vermelho se não atribuído)
- **ARRASTE** o GameObject com **TokenImageSelector** para este campo

### 2. **Verificar TokenImageSelector:**

- Ele deve ter um script chamado **TokenImageSelector**
- Deve ter referências para:
  - ✓ Botão "Selecionar Imagem"
  - ✓ Image Component para exibição
  - ✓ TextMeshPro para nome do arquivo

### 3. **Verificar RaceSelector:**

- Tem um método público: `TentarCarregarImagemAutomaticaDaRaca()`
- Chamado quando raça é confirmada
- Procura por `CriacaoFichaProgressaoDnd` via `FindObjectOfType`

---

## 📋 Fluxo de Carregamento

```
[Usuário seleciona raça]
    ↓
[RaceSelector.CriarPasso_1_Racas() - idConfirmado]
    ↓
[RaceSelector.TentarCarregarImagemAutomaticaDaRaca()]
    ↓ Debug.Log "INICIANDO CARREGAMENTO DE IMAGEM"
    ↓
[Verifica se GlobalFichaData.caminhoImagemToken vazio?]
    ├─ NÃO VAZIO: Log "Usuário já tem imagem"
    └─ VAZIO: Continua...
    ↓ Debug.Log "Pasta de retratos: [caminho]"
    ↓ Debug.Log "Arquivos encontrados: [lista]"
    ↓
[Tenta encontrar arquivo: anao.jpg, anao.png, etc]
    ↓ Debug.Log "Procurando: anao.jpg"
    ↓ Debug.Log "ENCONTRADO: [caminho]"
    ↓
[FindObjectOfType<CriacaoFichaProgressaoDnd>()]
    ├─ NULL: Log "❌ CriacaoFichaProgressaoDnd não encontrado"
    └─ ENCONTRADO: Continua
    ↓ Debug.Log "obtendo TokenImageSelector"
    ↓
[progressao.ObterSeletorImagem()]
    ├─ NULL: Log "❌ TokenImageSelector é NULL" 
    └─ ENCONTRADO: Continua
    ↓ Debug.Log "✓ TokenImageSelector encontrado"
    ↓
[tokenSelector.DefinirImagemDoArquivo(caminhoImagem)]
    ↓ Debug.Log "✓✓✓ SUCESSO! Imagem...carregada"
```

---

## 🐛 Debug - O que procurar no Console:

### ✅ Sucesso esperado:
```
[RaceSelector] ===== INICIANDO CARREGAMENTO DE IMAGEM =====
[RaceSelector] Raça selecionada: Anão
[RaceSelector] Nome normalizado para busca: 'anao'
[RaceSelector] Pasta de retratos: C:\Users\leanl\AppData\LocalLow\Default...\retratos
[RaceSelector] Pasta existe? True
[RaceSelector] Arquivos encontrados na pasta: 9
[RaceSelector] Procurando: anao.jpg
[RaceSelector] ✓ ENCONTRADO: C:\Users\leanl\AppData\LocalLow\...
[RaceSelector] Obtendo CriacaoFichaProgressaoDnd...
[RaceSelector] ✓ CriacaoFichaProgressaoDnd encontrado
[RaceSelector] Obtendo TokenImageSelector...
[CriacaoFichaProgressaoDnd] ✓ TokenImageSelector encontrado e disponível
[RaceSelector] ✓ TokenImageSelector encontrado
[RaceSelector] Carregando imagem: C:\Users\leanl\...
[RaceSelector] ✓✓✓ SUCESSO! Imagem de raça carregada
[RaceSelector] ===== FIM DO CARREGAMENTO DE IMAGEM =====
```

### ❌ Erro 1 - TokenImageSelector não atribuído:
```
[CriacaoFichaProgressaoDnd] ⚠ tokenImageSelector NÃO foi atribuído no Inspector!
[CriacaoFichaProgressaoDnd] Procure por CriacaoFichaProgressaoDnd no cenário...
[RaceSelector] ❌ TokenImageSelector é NULL!
```

**Solução:** Arraste TokenImageSelector no Inspector

### ❌ Erro 2 - Imagem não encontrada:
```
[RaceSelector] Pasta existe? False
```

**Solução:** Crie a pasta `retratos` em:
`C:\Users\[User]\AppData\LocalLow\DefaultCompany\DiceVTT\retratos\`

### ❌ Erro 3 - Nome não bate:
```
[RaceSelector] Procurando: anao.jpg
[RaceSelector] Procurando: anao.png
[RaceSelector] ❌ Nenhuma imagem encontrada para raça 'anao'
```

**Solução:** Verifique se o arquivo tem exatamente o nome da raça (minúsculas, sem espaços/hífens)

---

## ✓ Checklist antes de testar:

- [ ] CriacaoFichaProgressaoDnd tem TokenImageSelector atribuído no Inspector
- [ ] TokenImageSelector existe no cenário
- [ ] Pasta `C:\Users\...\AppData\LocalLow\DefaultCompany\DiceVTT\retratos\` existe
- [ ] As imagens estão com nomes corretos (anao.jpg, draconato.jpg, etc)
- [ ] Console do Unity aberto para ver logs
