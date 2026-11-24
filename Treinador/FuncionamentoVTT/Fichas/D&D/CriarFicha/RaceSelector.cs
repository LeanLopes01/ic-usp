using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RaceSelector : MonoBehaviour
{
    [Header("UI - Lista Cascata")]
    public Transform mainScrollViewContent;
    public GameObject mainStepContainerPrefab; // Prefab com isMainStep=true (MainOptionButtonUI)
    public GameObject stepContainerPrefab;     // Prefab com isMainStep=false (SubChoiceOptionUI)

    [Header("UI - Detalhes (Direita)")]
    public TMP_Text infoText; 

    [Header("Fonte de Dados")]
    public bool preferirDadosLocais = true;
    public string pastaLocalRelativa = "sistemas/quinta-edicao-srd/racas";

    [Header("Referências")]
    public FichaDisplayUI fichaDisplayUI;

    [Header("Referência SRD (Opcional)")]
    public SrdBookNavigator srdBookNavigator;

    private List<RaceData> racasDisponiveis = new List<RaceData>(); 
    private RaceData racaSelecionada;
    private SubraceData subracaSelecionada;
    
    private bool fluxoIniciado = false;
    private bool dadosLocaisCarregados = false;
    private int contextoFichaId = -1;
    private Queue<Action> filaDePassos = new Queue<Action>();
    private int passoAtualIndex = 0; // Rastreia o índice do passo sendo criado
    
    public static RacaInfoData RestauracaoInfo = null;

    private List<string> proficienciasRacaEscolhidas = new List<string>();
    private List<string> idiomasRacaEscolhidos = new List<string>();
    private List<string> ferramentasRacaEscolhidas = new List<string>();
    
    // Novas listas para categorizar
    private List<string> armasRaca = new List<string>();
    private List<string> armadurasRaca = new List<string>();

    [Serializable] public class RaceData { public string id; public string remoteId; public string nome; public string name; public string xml; }
    [Serializable] public class RaceList { public RaceData[] races; }
    [Serializable] public class SubraceData { public string id; public string name; public XElement xmlElement; }
    
    [Serializable] 
    public class RacaInfoData { 
        public string subrace_id; 
        public string subrace_name; 
        public List<string> skills = new List<string>();
        public List<string> armors = new List<string>();
        public List<string> weapons = new List<string>();
        public List<string> tools = new List<string>();
        public List<string> languages = new List<string>();
    }

    void OnEnable()
    {
        GlobalFichaData.OnSessaoDeFichaIniciada += OnSessaoDeFichaIniciada;

        int fichaAtualId = GlobalFichaData.fichasDndId;
        if (contextoFichaId != fichaAtualId || GlobalFichaData.precisaRestaurarFluxo)
        {
            contextoFichaId = fichaAtualId;
            fluxoIniciado = false;
            dadosLocaisCarregados = false;
        }

        if (fluxoIniciado) return;

        TentarIniciarFluxoParaFichaAtual();
    }

    void OnDisable()
    {
        GlobalFichaData.OnSessaoDeFichaIniciada -= OnSessaoDeFichaIniciada;
    }

    /// <summary>
    /// Gatilho independente de OnEnable/Start: alguns passos (ex: ClassSelector) passaram a usar
    /// GameObjects sempre ativos pra evitar um bug de CanvasGroup/layout preso após
    /// SetActive(false)-&gt;(true) — o que também significa que OnEnable nunca mais dispara de novo
    /// pra eles. Sem isto, a raça (e o bônus já aplicado em AttributeManager.AtualizarBonusRaça)
    /// de uma ficha ANTERIOR continuava "confirmada" aqui mesmo depois de criar/abrir outra ficha,
    /// já que fluxoIniciado nunca era resetado. GlobalFichaData.OnSessaoDeFichaIniciada dispara em
    /// TODA troca de ficha (nova, editar, trocar), independente do ciclo de vida do GameObject.
    /// </summary>
    private void OnSessaoDeFichaIniciada()
    {
        contextoFichaId = GlobalFichaData.fichasDndId;
        fluxoIniciado = false;
        dadosLocaisCarregados = false;
        StartCoroutine(TentarIniciarFluxoParaFichaAtualAdiado());
    }

    /// <summary>
    /// Espera um frame antes de reconstruir o fluxo. GlobalFichaData.IniciarSessaoParaFicha (que
    /// disparou o evento acima) só RESETA os dados de restauração — quem a chamou ainda vai rodar,
    /// de forma síncrona e logo em seguida (mesma call stack), o código que os POPULA de verdade
    /// (FichaLoaderUI.CarregarRestauracaoDoSnapshot -&gt; RaceSelector.RestauracaoInfo,
    /// GlobalFichaData.raceIdRestauracao/subraceIdRestauracao). Reconstruir o fluxo agora, ainda
    /// dentro do próprio evento, pegaria RestauracaoInfo vazio — a raça/subraça já salva da ficha
    /// nunca auto-selecionava. Antes desta tela virar (ou poder vir a virar) um GameObject sempre
    /// ativo, um OnEnable real logo depois (quando CarregarRestauracaoDoSnapshot já tinha rodado e
    /// setado precisaRestaurarFluxo) corrigia isso sozinho; sem esse segundo OnEnable, o adiamento
    /// de um frame faz esse papel.
    /// </summary>
    private IEnumerator TentarIniciarFluxoParaFichaAtualAdiado()
    {
        yield return null;
        TentarIniciarFluxoParaFichaAtual();
    }

    private void TentarIniciarFluxoParaFichaAtual()
    {
        if (preferirDadosLocais && !dadosLocaisCarregados && TentarCarregarRacasLocais(out var racasLocais) && racasLocais.Count > 0)
        {
            dadosLocaisCarregados = true;
            IniciarFluxo(racasLocais);
            return;
        }

        if (racasDisponiveis.Count > 0) IniciarFluxo(racasDisponiveis);
        else { if (infoText) infoText.text = "<color=#B00020>Nenhuma raça local encontrada em sistemas/quinta-edicao-srd/racas.</color>"; }
    }

    public void IniciarFluxo(List<RaceData> dadosDescarregados)
    {
        fluxoIniciado = true; racasDisponiveis = dadosDescarregados ?? new List<RaceData>();
        proficienciasRacaEscolhidas.Clear();
        idiomasRacaEscolhidos.Clear();
        ferramentasRacaEscolhidas.Clear();
        armasRaca.Clear();
        armadurasRaca.Clear();
        
        filaDePassos.Clear();
        passoAtualIndex = 0; // Reseta o índice de passos
        ProficiencySelector.PericiasRacaLocal.Clear();
        racaSelecionada = null;
        subracaSelecionada = null;

        if (mainScrollViewContent == null)
        {
            Debug.LogError("[RaceSelector] Referência 'mainScrollViewContent' não atribuída.");
            if (infoText) infoText.text = "Erro de configuração de UI: conteúdo da lista de raças não encontrado.";
            return;
        }

        foreach (Transform child in mainScrollViewContent) Destroy(child.gameObject);
        if (infoText) infoText.text = "Bem-vindo. Escolha sua Raça ancestral.";
        
        CriarPasso_1_Racas();
    }

    private void AvancarPasso() {
        if (filaDePassos.Count > 0) { 
            Action proximoPasso = filaDePassos.Dequeue(); 
            proximoPasso.Invoke(); 
        }
        else { 
            if (infoText) infoText.text = "<color=#1B5E20>Construção da Raça concluída! Pode salvar a ficha.</color>"; 
            RegistrarNoLog();
            CalcularEEnviarBonusDeAtributos();
            StartCoroutine(SalvarRacaPublico());
            
            // Forçar sincronização dos atributos após subraca ser selecionada e bônus aplicados
            AutoSaveAttributesManager autoSave = FindObjectOfType<AutoSaveAttributesManager>();
            if (autoSave != null)
            {
                autoSave.ForcarRecarregamento();
                Debug.Log("[RaceSelector] ✓ AutoSaveAttributesManager.ForcarRecarregamento() chamado após seleção de subraca");
            }

            // Atualizar UI de progressão para mostrar bônus de raça
            CriacaoFichaProgressaoDnd progressao = FindObjectOfType<CriacaoFichaProgressaoDnd>();
            if (progressao != null)
            {
                progressao.RefreshProgressaoUI();
                Debug.Log("[RaceSelector] ✓ CriacaoFichaProgressaoDnd.RefreshProgressaoUI() chamado para atualizar bônus");
            }

            // Atualizar FichaDisplayUI com os valores + bônus
            AtualizarFichaDisplayComBonus();
        }
    }

    private void AtualizarFichaDisplayComBonus()
    {
        if (fichaDisplayUI == null)
        {
            Debug.LogWarning("[RaceSelector] fichaDisplayUI não atribuído - não é possível atualizar display");
            return;
        }

        // Calcular totais com bônus
        int forcaTotal = GlobalFichaData.forca + GlobalFichaData.bonusRacaDict["FOR"] + GlobalFichaData.bonusClasseDict["FOR"] + GlobalFichaData.bonusAntecedenteDict["FOR"] + GlobalFichaData.bonusTalentoDict["FOR"];
        int destrezaTotal = GlobalFichaData.destreza + GlobalFichaData.bonusRacaDict["DEX"] + GlobalFichaData.bonusClasseDict["DEX"] + GlobalFichaData.bonusAntecedenteDict["DEX"] + GlobalFichaData.bonusTalentoDict["DEX"];
        int constituicaoTotal = GlobalFichaData.constituicao + GlobalFichaData.bonusRacaDict["CON"] + GlobalFichaData.bonusClasseDict["CON"] + GlobalFichaData.bonusAntecedenteDict["CON"] + GlobalFichaData.bonusTalentoDict["CON"];
        int inteligenciaTotal = GlobalFichaData.inteligencia + GlobalFichaData.bonusRacaDict["INT"] + GlobalFichaData.bonusClasseDict["INT"] + GlobalFichaData.bonusAntecedenteDict["INT"] + GlobalFichaData.bonusTalentoDict["INT"];
        int sabedoriaTotal = GlobalFichaData.sabedoria + GlobalFichaData.bonusRacaDict["SAB"] + GlobalFichaData.bonusClasseDict["SAB"] + GlobalFichaData.bonusAntecedenteDict["SAB"] + GlobalFichaData.bonusTalentoDict["SAB"];
        int carismaTotal = GlobalFichaData.carisma + GlobalFichaData.bonusRacaDict["CAR"] + GlobalFichaData.bonusClasseDict["CAR"] + GlobalFichaData.bonusAntecedenteDict["CAR"] + GlobalFichaData.bonusTalentoDict["CAR"];

        // Criar response com totais
        FichaDisplayUI.AtributosResponse totais = new FichaDisplayUI.AtributosResponse();
        totais.atributos.forca = forcaTotal;
        totais.atributos.destreza = destrezaTotal;
        totais.atributos.constituicao = constituicaoTotal;
        totais.atributos.inteligencia = inteligenciaTotal;
        totais.atributos.sabedoria = sabedoriaTotal;
        totais.atributos.carisma = carismaTotal;

        // Criar breakdown com bônus detalhados
        FichaDisplayUI.BonusBreakdownData breakdown = new FichaDisplayUI.BonusBreakdownData();
        breakdown.breakdown_forca = $"{GlobalFichaData.forca} + {GlobalFichaData.bonusRacaDict["FOR"] + GlobalFichaData.bonusClasseDict["FOR"] + GlobalFichaData.bonusAntecedenteDict["FOR"] + GlobalFichaData.bonusTalentoDict["FOR"]}";
        breakdown.breakdown_destreza = $"{GlobalFichaData.destreza} + {GlobalFichaData.bonusRacaDict["DEX"] + GlobalFichaData.bonusClasseDict["DEX"] + GlobalFichaData.bonusAntecedenteDict["DEX"] + GlobalFichaData.bonusTalentoDict["DEX"]}";
        breakdown.breakdown_constituicao = $"{GlobalFichaData.constituicao} + {GlobalFichaData.bonusRacaDict["CON"] + GlobalFichaData.bonusClasseDict["CON"] + GlobalFichaData.bonusAntecedenteDict["CON"] + GlobalFichaData.bonusTalentoDict["CON"]}";
        breakdown.breakdown_inteligencia = $"{GlobalFichaData.inteligencia} + {GlobalFichaData.bonusRacaDict["INT"] + GlobalFichaData.bonusClasseDict["INT"] + GlobalFichaData.bonusAntecedenteDict["INT"] + GlobalFichaData.bonusTalentoDict["INT"]}";
        breakdown.breakdown_sabedoria = $"{GlobalFichaData.sabedoria} + {GlobalFichaData.bonusRacaDict["SAB"] + GlobalFichaData.bonusClasseDict["SAB"] + GlobalFichaData.bonusAntecedenteDict["SAB"] + GlobalFichaData.bonusTalentoDict["SAB"]}";
        breakdown.breakdown_carisma = $"{GlobalFichaData.carisma} + {GlobalFichaData.bonusRacaDict["CAR"] + GlobalFichaData.bonusClasseDict["CAR"] + GlobalFichaData.bonusAntecedenteDict["CAR"] + GlobalFichaData.bonusTalentoDict["CAR"]}";

        fichaDisplayUI.AtualizarDadosCompletos(totais, breakdown);
        Debug.Log($"[RaceSelector] ✓ FichaDisplayUI atualizado com bônus: FOR={forcaTotal}, DEX={destrezaTotal}");
    }

    private void RegistrarNoLog()
    {
        if (racaSelecionada == null) return;
        var sb = new System.Text.StringBuilder();
        sb.Append(racaSelecionada.nome);
        if (subracaSelecionada != null)
            sb.Append($"\nSubraça: {subracaSelecionada.name}");
        if (proficienciasRacaEscolhidas.Count > 0)
            sb.Append($"\nPerícias: {string.Join(", ", proficienciasRacaEscolhidas)}");
        if (idiomasRacaEscolhidos.Count > 0)
            sb.Append($"\nIdiomas: {string.Join(", ", idiomasRacaEscolhidos)}");
        if (armasRaca.Count > 0)
            sb.Append($"\nArmas: {string.Join(", ", armasRaca)}");
        if (armadurasRaca.Count > 0)
            sb.Append($"\nArmaduras: {string.Join(", ", armadurasRaca)}");
        FichaLogManager.RegistrarEscolha(1, "Raça", sb.ToString());
    }

    // ==========================================================
    // PASSO 1: ESCOLHA DA RAÇA PRINCIPAL
    // ==========================================================
    private void CriarPasso_1_Racas()
    {
        if (!TryCriarMainStepUI(out StepContainerUI stepUI, out int indexoPasso)) return;
        
        // Armazena o índice real do container
        Transform containerAtual = stepUI.transform;
        
        Dictionary<string, string> opcoes = new Dictionary<string, string>();
        foreach (var r in racasDisponiveis)
        {
            if (r == null || string.IsNullOrWhiteSpace(r.id)) continue;
            string nomeExibicao = !string.IsNullOrWhiteSpace(r.nome) ? r.nome : (r.name ?? r.id);
            if (!opcoes.ContainsKey(r.id)) opcoes.Add(r.id, nomeExibicao);
        }

        if (opcoes.Count == 0)
        {
            if (infoText) infoText.text = "Nenhuma raça disponível para seleção.";
            Debug.LogWarning("[RaceSelector] Nenhuma opção de raça encontrada para criar o passo 1.");
            return;
        }

        // Callback para deselecção: remove passos posteriores
        System.Action onDeselectRaca = () => {
            RemoverPassosPosteriores(containerAtual.GetSiblingIndex());
        };

        stepUI.SetupStep("1. ESCOLHA SUA RAÇA", opcoes, 
            (idSelecionado) => { 
                var r = racasDisponiveis.FirstOrDefault(x => x.id == idSelecionado); 
                if (r != null)
                {
                    MostrarDetalhesRaca(r.xml, null);
                    MostrarReferenciaSrd(ObterNomeExibicaoRaca(r));
                }
            },
            (idConfirmado) => {
                racaSelecionada = racasDisponiveis.FirstOrDefault(x => x.id == idConfirmado);
                if (racaSelecionada == null) {
                    Debug.LogError("[RaceSelector] Raça confirmada não encontrada para o ID: " + idConfirmado);
                    return;
                }

                // Tentar carregar imagem automática da raça
                TentarCarregarImagemAutomaticaDaRaca();

                MostrarReferenciaSrd(ObterNomeExibicaoRaca(racaSelecionada));

                int currentRacaId = 0;
                string idParaContexto = !string.IsNullOrEmpty(racaSelecionada.remoteId) ? racaSelecionada.remoteId : racaSelecionada.id;
                int.TryParse(idParaContexto, out currentRacaId);
                GlobalFichaData.currentRacaId = currentRacaId;
                filaDePassos.Clear(); 
                
                // Limpa variáveis
                proficienciasRacaEscolhidas.Clear();
                idiomasRacaEscolhidos.Clear();
                ferramentasRacaEscolhidas.Clear();
                armasRaca.Clear();
                armadurasRaca.Clear();

                try {
                    string cleanXml = racaSelecionada.xml.Trim().Replace("\uFEFF", "");
                    XDocument doc = XDocument.Parse(cleanXml);
                    
                    LerIdiomasDaRaca(cleanXml);
                    LerProficienciasFixas(doc.Root);
                    ExtrairDeslocamento(doc.Root);
                } catch (Exception e) {
                    Debug.LogWarning("Falha ao analisar XML da Raça: " + e.Message);
                }

                // Verifica se tem Subraça
                List<SubraceData> subracas = LerSubracas(racaSelecionada.xml);
                if (subracas.Count > 0) {
                    filaDePassos.Enqueue(() => CriarPasso_2_Subraca(subracas));
                }

                // Verifica se a raça dá escolha de proficiências de perícia
                int qtdProf = LerQuantidadeProficiencias(racaSelecionada.xml);
                if (qtdProf > 0) {
                    for (int p = 1; p <= qtdProf; p++) { 
                        int atual = p; 
                        filaDePassos.Enqueue(() => CriarPasso_ProficienciasExtras(racaSelecionada.xml, atual, qtdProf)); 
                    }
                }

                // NOVO: Verifica se a raça dá escolha de idiomas extras (como Humano, Meio-Elfo)
                int qtdIdiomasExtras = LerQuantidadeIdiomasExtras(racaSelecionada.xml);
                if (qtdIdiomasExtras > 0) {
                    for (int p = 1; p <= qtdIdiomasExtras; p++) {
                        int atual = p;
                        filaDePassos.Enqueue(() => CriarPasso_EscolhaIdioma(atual, qtdIdiomasExtras));
                    }
                }

                AvancarPasso(); 
            },
            true,
            onDeselectRaca
        );
        if (!string.IsNullOrEmpty(GlobalFichaData.raceIdRestauracao))
            stepUI.AutoSelecionarOpcao(GlobalFichaData.raceIdRestauracao);
    }

    // ==========================================================
    // PASSO 2: ESCOLHA DA SUBRAÇA (SE EXISTIR)
    // ==========================================================
    private void CriarPasso_2_Subraca(List<SubraceData> subracas)
    {
        if (!TryCriarStepUI(out StepContainerUI stepUI, out int indexoPasso)) return;
        
        // Armazena o índice real do container
        Transform containerAtual = stepUI.transform;
        
        Dictionary<string, string> opcoes = new Dictionary<string, string>();
        foreach (var s in subracas)
        {
            if (s == null || string.IsNullOrWhiteSpace(s.id)) continue;
            string nomeExibicao = string.IsNullOrWhiteSpace(s.name) ? s.id : s.name;
            if (!opcoes.ContainsKey(s.id)) opcoes.Add(s.id, nomeExibicao);
        }

        if (opcoes.Count == 0)
        {
            AvancarPasso();
            return;
        }

        // Callback para deselecção: remove passos posteriores
        System.Action onDeselectSubraca = () => {
            RemoverPassosPosteriores(containerAtual.GetSiblingIndex());
            subracaSelecionada = null;
        };

        stepUI.SetupStep("2. ESCOLHA SUA SUBRAÇA", opcoes, 
            (idSelecionado) => { 
                var s = subracas.FirstOrDefault(x => x.id == idSelecionado); 
                if (s != null && racaSelecionada != null)
                {
                    MostrarDetalhesRaca(racaSelecionada.xml, s);
                    MostrarReferenciaSrd(s.name);
                }
            },
            (idConfirmado) => {
                subracaSelecionada = subracas.FirstOrDefault(x => x.id == idConfirmado);
                
                if (subracaSelecionada != null && subracaSelecionada.xmlElement != null) {
                    LerProficienciasFixas(subracaSelecionada.xmlElement);
                }

                if (subracaSelecionada != null) MostrarReferenciaSrd(subracaSelecionada.name);
                
                AvancarPasso(); 
            },
            true,
            onDeselectSubraca
        );
        if (!string.IsNullOrEmpty(GlobalFichaData.subraceIdRestauracao))
            stepUI.AutoSelecionarOpcao(GlobalFichaData.subraceIdRestauracao);
    }

    private void CriarPasso_ProficienciasExtras(string xml, int atual, int total)
    {
        XDocument doc = XDocument.Parse(xml); var skillChoices = doc.Descendants("skill-choices").FirstOrDefault();
        if (skillChoices == null) { AvancarPasso(); return; }

        if (!TryCriarStepUI(out StepContainerUI stepUI, out int indexoPasso)) return;
        
        // Armazena o índice real do container
        Transform containerAtual = stepUI.transform;
        
        Dictionary<string, string> opcoes = new Dictionary<string, string>();
        foreach (var opt in skillChoices.Elements("option")) 
            if (!proficienciasRacaEscolhidas.Contains(opt.Value) && !ProficiencySelector.PericiasClasseLocal.Contains(opt.Value)) 
                opcoes.Add(opt.Value, opt.Value); 

        if (opcoes.Count == 0)
        {
            AvancarPasso();
            return;
        }

        // Callback para deselecção: remove passos posteriores
        System.Action onDeselectProf = () => {
            RemoverPassosPosteriores(containerAtual.GetSiblingIndex());
        };

        stepUI.SetupStep($"PROFICIÊNCIA DA RAÇA ({atual}/{total})", opcoes,
            (id) => { if (infoText) infoText.text = $"<size=150%><b>{id}</b></size>\n\nVocê será proficiente neste tipo de teste por conta da sua descendência."; },
            (idConfirmado) => {
                proficienciasRacaEscolhidas.Add(idConfirmado);
                if (!ProficiencySelector.PericiasRacaLocal.Contains(idConfirmado))
                    ProficiencySelector.PericiasRacaLocal.Add(idConfirmado);
                ProficiencySelector.OnImportacaoSolicitada?.Invoke();
                AvancarPasso();
            },
            true,
            onDeselectProf
        );
        if (RestauracaoInfo?.skills != null)
            foreach (var skill in RestauracaoInfo.skills)
                if (opcoes.ContainsKey(skill)) { stepUI.AutoSelecionarOpcao(skill); break; }
    }

    private void CriarPasso_EscolhaIdioma(int atual, int total)
    {
        if (!TryCriarStepUI(out StepContainerUI stepUI, out int indexoPasso)) return;
        
        // Armazena o índice real do container
        Transform containerAtual = stepUI.transform;
        
        Dictionary<string, string> idiomasDnd = new Dictionary<string, string> {
            {"Abissal", "Abissal"}, {"Anão", "Anão"}, {"Celestial", "Celestial"},
            {"Comum", "Comum"}, {"Dracônico", "Dracônico"}, {"Élfico", "Élfico"},
            {"Gigante", "Gigante"}, {"Gnomo", "Gnomo"}, {"Goblin", "Goblin"},
            {"Halfling", "Halfling"}, {"Infernal", "Infernal"}, {"Orc", "Orc"},
            {"Primordial", "Primordial"}, {"Silvestre", "Silvestre"}, {"Subcomum", "Subcomum"}
        };

        Dictionary<string, string> opcoes = new Dictionary<string, string>();
        foreach (var kvp in idiomasDnd) {
            if (!idiomasRacaEscolhidos.Contains(kvp.Key) && 
                !ProficiencySelector.IdiomasAntecedenteLocal.Contains(kvp.Key) &&
                !ProficiencySelector.IdiomasClasseLocal.Contains(kvp.Key)) 
            {
                opcoes.Add(kvp.Key, kvp.Value);
            }
        }

        if (opcoes.Count == 0)
        {
            AvancarPasso();
            return;
        }

        // Callback para deselecção: remove passos posteriores
        System.Action onDeselectIdioma = () => {
            RemoverPassosPosteriores(containerAtual.GetSiblingIndex());
        };

        stepUI.SetupStep($"IDIOMA EXTRA DA RAÇA ({atual}/{total})", opcoes,
            (id) => { if (infoText) infoText.text = $"<size=150%><b>{id}</b></size>\n\nVocê aprendeu esse idioma extra pela sua cultura ou vivência."; },
            (idConfirmado) => {
                idiomasRacaEscolhidos.Add(idConfirmado);
                ProficiencySelector.IdiomasRacaLocal = new List<string>(idiomasRacaEscolhidos);
                ProficiencySelector.OnImportacaoSolicitada?.Invoke();
                AvancarPasso();
            },
            true,
            onDeselectIdioma
        );
        if (RestauracaoInfo?.languages != null)
            foreach (var lang in RestauracaoInfo.languages)
                if (opcoes.ContainsKey(lang)) { stepUI.AutoSelecionarOpcao(lang); break; }
    }

    // ==========================================================
    // LEITURA DO XML E DISPLAY
    // ==========================================================

    private void ExtrairDeslocamento(XElement root)
    {
        if (root == null) return;
        var speedEl = root.Descendants("speed").FirstOrDefault();
        if (speedEl != null && int.TryParse(speedEl.Value.Trim(), out int velocidade))
            GlobalFichaData.deslocamento = velocidade;
    }

    private void LerProficienciasFixas(XElement node)
    {
        if (node == null) return;

        var profs = node.Descendants("proficiencies").FirstOrDefault();
        if (profs != null) {
            foreach (var el in profs.Elements()) {
                string valLimpo = el.Value.Trim();
                if (string.IsNullOrEmpty(valLimpo)) continue;

                string tag = el.Name.LocalName.ToLower();
                
                // Categoriza lendo a Tag ou palavras-chave
                if (tag.Contains("weapon") || valLimpo.ToLower().Contains("machado") || valLimpo.ToLower().Contains("martelo") || valLimpo.ToLower().Contains("espada")) {
                    armasRaca.Add(valLimpo);
                }
                else if (tag.Contains("armor") || valLimpo.ToLower().Contains("armadura")) {
                    armadurasRaca.Add(valLimpo);
                }
                else if (tag == "skill") {
                    if (!proficienciasRacaEscolhidas.Contains(valLimpo)) {
                        proficienciasRacaEscolhidas.Add(valLimpo);
                    }
                        // Escreve na lista PRÓPRIA da raça para não ser sobrescrita pela classe
                        if (!ProficiencySelector.PericiasRacaLocal.Contains(valLimpo))
                            ProficiencySelector.PericiasRacaLocal.Add(valLimpo);
                } else {
                    if (!ferramentasRacaEscolhidas.Contains(valLimpo)) {
                        ferramentasRacaEscolhidas.Add(valLimpo);
                    }
                }
            }
        }

        ProficiencySelector.FerramentasRacaLocal = new List<string>();
        ProficiencySelector.FerramentasRacaLocal.AddRange(armadurasRaca);
        ProficiencySelector.FerramentasRacaLocal.AddRange(armasRaca);
        ProficiencySelector.FerramentasRacaLocal.AddRange(ferramentasRacaEscolhidas);
        
        ProficiencySelector.OnImportacaoSolicitada?.Invoke();
    }

    private List<SubraceData> LerSubracas(string xml) {
        List<SubraceData> lista = new List<SubraceData>();
        try {
            XDocument doc = XDocument.Parse(xml);
            var subEl = doc.Descendants("subraces").FirstOrDefault();
            if (subEl != null) {
                foreach(var s in subEl.Elements("subrace")) {
                    lista.Add(new SubraceData { id = s.Attribute("id")?.Value, name = s.Element("name")?.Value, xmlElement = s });
                }
            }
        } catch { }
        return lista;
    }

    private int LerQuantidadeProficiencias(string xml) { 
        try { XDocument doc = XDocument.Parse(xml); var choices = doc.Descendants("skill-choices").FirstOrDefault(); 
        if (choices != null && int.TryParse(choices.Attribute("count")?.Value, out int c)) return c; } catch {} return 0; 
    }

    private int LerQuantidadeIdiomasExtras(string xml) {
        try {
            XDocument doc = XDocument.Parse(xml);
            var langNode = doc.Descendants("languages").FirstOrDefault();
            
            if (langNode != null) {
                var choice = langNode.Element("choice") ?? langNode.Element("language-choices");
                if (choice != null) {
                    var countAttr = choice.Attribute("count") ?? choice.Attribute("amount");
                    if (countAttr != null && int.TryParse(countAttr.Value, out int c)) return c;
                    return 1; 
                }
            }
            
            var lChoices = doc.Descendants("language-choices").FirstOrDefault();
            if (lChoices != null) {
                var countAttr = lChoices.Attribute("count");
                if (countAttr != null && int.TryParse(countAttr.Value, out int c)) return c;
                return 1;
            }
        } catch {}
        return 0;
    }

    private void LerIdiomasDaRaca(string xml) {
        try {
            XDocument doc = XDocument.Parse(xml);
            var langsEl = doc.Descendants("languages").FirstOrDefault();
            if (langsEl == null) return;
            
            var langOptions = langsEl.Elements("option").ToList();
            if (langOptions.Count == 0) langOptions = langsEl.Elements("language").ToList();
            
            foreach (var opt in langOptions) {
                string valor = opt.Value.Trim();
                if(valor.ToLower() != "any") {
                    idiomasRacaEscolhidos.Add(valor);
                }
            }
            
            if (idiomasRacaEscolhidos.Count > 0) {
                ProficiencySelector.IdiomasRacaLocal = new List<string>(idiomasRacaEscolhidos);
                ProficiencySelector.OnImportacaoSolicitada?.Invoke();
            }
        } catch { }
    }

    private void MostrarDetalhesRaca(string xmlRaca, SubraceData subPreview)
    {
        if (string.IsNullOrEmpty(xmlRaca)) return;
        try {
            XDocument doc = XDocument.Parse(xmlRaca); 
            XElement root = doc.Root;

            StringBuilder sb = new StringBuilder();
            
            string nome = root.Element("name")?.Value ?? "Raça";
            string summary = root.Element("summary")?.Value ?? "";
            sb.AppendLine($"<size=160%><b>{nome.ToUpper()}</b></size>");
            if (!string.IsNullOrEmpty(summary)) sb.AppendLine($"<i>{summary}</i>\n");
            
            ProcessarAtributos(root, sb);
            ProcessarElementoXml(root, sb);

            if (subPreview != null && subPreview.xmlElement != null) {
                sb.AppendLine("\n<color=#8C5A00>-----------------------------</color>");
                sb.AppendLine($"<size=140%><b>SUBRAÇA: {subPreview.name.ToUpper()}</b></size>");
                string subSum = subPreview.xmlElement.Element("summary")?.Value;
                if (!string.IsNullOrEmpty(subSum)) sb.AppendLine($"<i>{subSum}</i>\n");
                
                ProcessarAtributos(subPreview.xmlElement, sb);
                ProcessarElementoXml(subPreview.xmlElement, sb);
            }

            if (infoText) infoText.text = sb.ToString();
        } catch { 
            if (infoText) infoText.text = "Erro ao ler detalhes da raça."; 
        }
    }

    private string TraduzirAtributo(string attr) {
        if (string.IsNullOrEmpty(attr)) return "";
        attr = attr.ToUpper().Trim();
        if (attr == "STR") return "FOR";
        if (attr == "WIS") return "SAB";
        if (attr == "CHA") return "CAR";
        return attr; 
    }

    private void ProcessarAtributos(XElement container, StringBuilder sb) {
        var abilities = container.Element("ability_scores");
        if(abilities != null) {
            List<string> bonuses = new List<string>();
            foreach(var ab in abilities.Elements("ability")) {
                string nome = TraduzirAtributo(ab.Attribute("name")?.Value); 
                string mod = ab.Attribute("mod")?.Value;
                if(!string.IsNullOrEmpty(nome) && !string.IsNullOrEmpty(mod)) {
                    string sinal = (!mod.Contains("-") && !mod.Contains("+")) ? "+" : ""; 
                    bonuses.Add($"<color=#1B5E20>{nome} {sinal}{mod}</color>");
                }
            }
            if(bonuses.Count > 0) sb.AppendLine($"<b>Bônus de Atributo:</b> {string.Join(", ", bonuses)}\n");
        }
    }

    private void ProcessarElementoXml(XElement container, StringBuilder sb) {
        var traitsContainer = container.Element("traits") ?? container.Element("features");
        List<XElement> poderes = new List<XElement>();
        
        if (traitsContainer != null) {
            poderes.AddRange(traitsContainer.Elements("trait"));
            poderes.AddRange(traitsContainer.Elements("feature"));
        } else {
            poderes.AddRange(container.Elements("trait"));
            poderes.AddRange(container.Elements("feature"));
        }

        if(poderes.Count > 0) {
            sb.AppendLine("<size=120%><b>CARACTERÍSTICAS:</b></size>");
            foreach(var t in poderes) {
                string tNome = t.Element("name")?.Value;
                string tDesc = t.Element("description")?.Value ?? t.Element("text")?.Value;
                if (!string.IsNullOrEmpty(tNome)) {
                    sb.AppendLine($"• <b>{tNome}:</b> <color=#333333>{tDesc?.Trim()}</color>");
                }
            }
        }
    }

    private void CalcularEEnviarBonusDeAtributos()
    {
        if (racaSelecionada == null) return;
        Dictionary<string, int> bonusTotais = new Dictionary<string, int>();

        try {
            XDocument doc = XDocument.Parse(racaSelecionada.xml);
            var abBase = doc.Root?.Element("ability_scores");
            if (abBase != null) SomarAtributosNoDicionario(abBase, bonusTotais);

            if (subracaSelecionada != null && subracaSelecionada.xmlElement != null) {
                var abSub = subracaSelecionada.xmlElement.Element("ability_scores");
                if (abSub != null) SomarAtributosNoDicionario(abSub, bonusTotais);
            }

            // Armazenar bônus em GlobalFichaData para exibição em progressão
            foreach (var kvp in bonusTotais)
            {
                if (GlobalFichaData.bonusRacaDict.ContainsKey(kvp.Key))
                    GlobalFichaData.bonusRacaDict[kvp.Key] = kvp.Value;
            }
            Debug.Log($"[RaceSelector] ✓ Bônus de raça salvos em GlobalFichaData: {string.Join(", ", bonusTotais.Select(x => $"{x.Key}+{x.Value}"))}");

            AttributeManager am = FindObjectOfType<AttributeManager>(true);
            if (am != null) am.AtualizarBonusRaça(bonusTotais);

        } catch { Debug.LogWarning("Erro ao extrair bônus de atributo da raça."); }
    }

    private void SomarAtributosNoDicionario(XElement abilityNode, Dictionary<string, int> dict) {
        foreach(var ab in abilityNode.Elements("ability")) {
            string nomeTraduzido = TraduzirAtributo(ab.Attribute("name")?.Value); 
            
            if (int.TryParse(ab.Attribute("mod")?.Value, out int valor)) {
                if (dict.ContainsKey(nomeTraduzido)) dict[nomeTraduzido] += valor;
                else dict[nomeTraduzido] = valor;
            }
        }
    }

    private string ObterNomeExibicaoRaca(RaceData raca)
    {
        if (raca == null) return string.Empty;
        if (!string.IsNullOrWhiteSpace(raca.nome)) return raca.nome;
        if (!string.IsNullOrWhiteSpace(raca.name)) return raca.name;
        return raca.id ?? string.Empty;
    }

    private void MostrarReferenciaSrd(string termo)
    {
        if (srdBookNavigator == null || string.IsNullOrWhiteSpace(termo)) return;
        srdBookNavigator.MostrarPorTermo(termo);
    }

    // Remove todos os passos (containers) criados após o índice especificado
    private void RemoverPassosPosteriores(int indexoBase)
    {
        if (mainScrollViewContent == null) return;

        // Remove apenas os containers POSTERIORES ao índice base (índices maiores)
        int totalChildren = mainScrollViewContent.childCount;
        for (int i = totalChildren - 1; i > indexoBase; i--)
        {
            Transform child = mainScrollViewContent.GetChild(i);
            Destroy(child.gameObject);
        }

        // Limpa a fila de passos posteriores
        filaDePassos.Clear();
        passoAtualIndex = indexoBase + 1;

        // Força ajuste dos layouts
        if (mainScrollViewContent.TryGetComponent<LayoutGroup>(out var layout))
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(mainScrollViewContent as RectTransform);
        }
    }

    public IEnumerator SalvarRacaPublico()
    {
        if (racaSelecionada == null) yield break;

        if (!GarantirFichaLocalDisponivel())
        {
            Debug.LogError("Erro ao preparar ficha local para salvar raça.");
            yield break;
        }

        string racaIdSalvar = !string.IsNullOrEmpty(racaSelecionada.remoteId) ? racaSelecionada.remoteId : racaSelecionada.id;

        RacaInfoData infoData = new RacaInfoData();
        if (subracaSelecionada != null) {
            infoData.subrace_id = subracaSelecionada.id;
            infoData.subrace_name = subracaSelecionada.name;
        }

        infoData.skills = new List<string>(proficienciasRacaEscolhidas);
        infoData.armors = new List<string>(armadurasRaca);
        infoData.weapons = new List<string>(armasRaca);
        infoData.tools = new List<string>(ferramentasRacaEscolhidas);
        infoData.languages = new List<string>(idiomasRacaEscolhidos);

        string json = JsonUtility.ToJson(infoData);

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        bool ok = LocalFichaStorage.SaveRaceInfo(
            steamId,
            playerId,
            GlobalFichaData.fichasGeneralId,
            GlobalFichaData.fichasDndId,
            racaIdSalvar,
            json,
            racaSelecionada.nome
        );

        if (ok)
        {
            GlobalFichaData.racaPersonagem = racaSelecionada.nome;
            // IMPORTANTE: Guardar o JSON com dados da subrace em GlobalFichaData para que persista entre etapas
            GlobalFichaData.detalhesPrincipaisJson = json;
            Debug.Log($"<color=green>Raça salva localmente! ID={racaSelecionada.id}, JSON: {json}</color>");
            Debug.Log($"[RaceSelector] GlobalFichaData.detalhesPrincipaisJson atualizado com subrace info");
            if (fichaDisplayUI && gameObject.activeInHierarchy) fichaDisplayUI.AtualizarTudo();

            // Só daqui em diante é seguro avançar de etapa — mesmo raciocínio de ClassSelector/
            // BackgroundSelector: SalvarRacaPublico() não faz nenhum yield antes daqui, então não há
            // risco de corrotina morta ao trocar de etapa, mas o avanço só deve acontecer depois que
            // a raça (e subraça) realmente foi persistida com sucesso.
            CriacaoFichaProgressaoDnd progressaoParaAvancar = FindObjectOfType<CriacaoFichaProgressaoDnd>();
            if (progressaoParaAvancar != null)
                progressaoParaAvancar.AvancarSeEtapaAtualFor(TipoResumoEtapaDnd.Raca);
        }
        else
        {
            Debug.LogError("Erro ao salvar raça localmente (ficha não encontrada).");
        }

        yield return null;
    }

    private bool GarantirFichaLocalDisponivel()
    {
        try
        {
            int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
            string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

            int fichasGeneralId = GlobalFichaData.fichasGeneralId;
            int fichasDndId = GlobalFichaData.fichasDndId;

            if ((fichasGeneralId > 0 || fichasDndId > 0) &&
                LocalFichaStorage.TryGetFichaSnapshot(steamId, playerId, fichasGeneralId, fichasDndId, out _))
            {
                return true;
            }

            string nome = !string.IsNullOrWhiteSpace(GlobalFichaData.nomePersonagem)
                ? GlobalFichaData.nomePersonagem
                : "Sem Nome";
            int level = GlobalFichaData.levelPersonagem > 0 ? GlobalFichaData.levelPersonagem : 1;

            LocalFichaData ficha = LocalFichaStorage.SaveFicha(
                steamId,
                playerId,
                1,
                nome,
                level,
                GlobalFichaData.metodoAtributos
            );

            if (ficha == null) return false;

            GlobalFichaData.fichasGeneralId = ficha.fichas_general_id;
            GlobalFichaData.fichasDndId = ficha.fichas_dnd_id;

            Debug.LogWarning("[RaceSelector] Snapshot local não encontrado. Uma nova ficha local foi criada automaticamente.");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[RaceSelector] Falha ao garantir ficha local: " + ex.Message);
            return false;
        }
    }

    private bool TentarObterRacaIdRemotoValido(out string idRemoto)
    {
        idRemoto = null;
        if (racaSelecionada == null) return false;

        string candidato = !string.IsNullOrEmpty(racaSelecionada.remoteId) ? racaSelecionada.remoteId : racaSelecionada.id;
        if (int.TryParse(candidato, out int idInt) && idInt > 0)
        {
            idRemoto = idInt.ToString();
            return true;
        }
        return false;
    }

    private bool TentarCarregarRacasLocais(out List<RaceData> racasLocais)
    {
        racasLocais = new List<RaceData>();
        try
        {
            string pasta = DndSrdLocalPath.Resolver(pastaLocalRelativa);
            if (!Directory.Exists(pasta)) return false;

            var arquivos = ImportadorConteudoUniversal.EnumerarArquivosXmlEJson(pasta, SearchOption.TopDirectoryOnly)
                .Where(f => !Path.GetFileName(f).ToLowerInvariant().Contains("template"));

            foreach (string arquivo in arquivos)
            {
                // Try/catch POR ARQUIVO (antes s\u00F3 existia o try/catch externo ao m\u00E9todo inteiro,
                // linha ~945) \u2014 sem isso, um \u00FAnico arquivo malformado (mais prov\u00E1vel em JSON escrito
                // \u00E0 m\u00E3o do que no XML oficial) derrubaria a carga de TODAS as ra\u00E7as locais de uma vez.
                try
                {
                    string xml;
                    if (ImportadorConteudoUniversal.EhArquivoJson(arquivo))
                    {
                        string json = ImportadorConteudoUniversal.LerTextoComFallbackDeEncoding(arquivo);
                        xml = json != null ? ImportadorConteudoUniversal.ConverterJsonParaXml(json, "race") : null;
                        if (xml == null)
                        {
                            Debug.LogWarning($"[RaceSelector] JSON inv\u00E1lido em '{Path.GetFileName(arquivo)}' \u2014 ignorando.");
                            continue;
                        }
                    }
                    else
                    {
                        xml = File.ReadAllText(arquivo, Encoding.UTF8).Trim().Replace("\uFEFF", "");
                    }
                    XDocument doc = XDocument.Parse(xml);
                    XElement root = doc.Element("race") ?? doc.Root;
                    if (root == null) continue;

                    string id = root.Attribute("id")?.Value;
                    string nome = root.Element("name")?.Value;
                    if (string.IsNullOrWhiteSpace(nome)) continue;
                    if (string.IsNullOrWhiteSpace(id)) id = Path.GetFileNameWithoutExtension(arquivo);

                    racasLocais.Add(new RaceData { id = id, remoteId = id, nome = nome.Trim(), name = nome.Trim(), xml = xml });
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RaceSelector] Ignorando arquivo inv\u00E1lido '{Path.GetFileName(arquivo)}': {e.Message}");
                }
            }

            racasLocais = racasLocais.OrderBy(r => r.nome).ToList();
            return racasLocais.Count > 0;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[RaceSelector] Falha ao carregar raças locais: " + e.Message);
            return false;
        }
    }

    private bool TryCriarStepUI(out StepContainerUI stepUI)
    {
        stepUI = null;

        if (stepContainerPrefab == null)
        {
            Debug.LogError("[RaceSelector] Referência 'stepContainerPrefab' não atribuída.");
            if (infoText) infoText.text = "Erro de configuração de UI: prefab de etapa não encontrado.";
            return false;
        }

        if (mainScrollViewContent == null)
        {
            Debug.LogError("[RaceSelector] Referência 'mainScrollViewContent' não atribuída.");
            if (infoText) infoText.text = "Erro de configuração de UI: conteúdo da lista não encontrado.";
            return false;
        }

        GameObject go = Instantiate(stepContainerPrefab, mainScrollViewContent, false);
        go.SetActive(true);
        stepUI = go.GetComponent<StepContainerUI>();

        if (stepUI == null)
        {
            Debug.LogError("[RaceSelector] O prefab de etapa não possui o componente StepContainerUI.");
            if (infoText) infoText.text = "Erro de configuração de UI: componente da etapa ausente no prefab.";
            Destroy(go);
            return false;
        }

        return true;
    }

    private bool TryCriarMainStepUI(out StepContainerUI stepUI, out int indexoPasso)
    {
        indexoPasso = passoAtualIndex;
        passoAtualIndex++;
        stepUI = null;

        var prefab = mainStepContainerPrefab != null ? mainStepContainerPrefab : stepContainerPrefab;
        if (prefab == null || mainScrollViewContent == null)
        {
            Debug.LogError("[RaceSelector] mainStepContainerPrefab ou mainScrollViewContent não atribuído.");
            return false;
        }

        GameObject go = UIUtils.Spawn(prefab, mainScrollViewContent, false);
        go.SetActive(true);
        stepUI = go.GetComponent<StepContainerUI>();
        if (stepUI == null) { Destroy(go); return false; }
        return true;
    }

    private bool TryCriarStepUI(out StepContainerUI stepUI, out int indexoPasso)
    {
        indexoPasso = passoAtualIndex;
        passoAtualIndex++;
        return TryCriarStepUI(out stepUI);
    }

    /// <summary>
    /// Carrega a imagem de retrato da raça selecionada automaticamente.
    /// Se o usuário já tiver escolhido uma imagem, não sobrescreve.
    /// </summary>
    public void TentarCarregarImagemAutomaticaDaRaca()
    {
        Debug.Log("[RaceSelector] ===== INICIANDO CARREGAMENTO DE IMAGEM =====");

        if (!string.IsNullOrEmpty(GlobalFichaData.caminhoImagemToken))
        {
            Debug.Log("[RaceSelector] ⚠ Usuário já tem uma imagem selecionada - não carregando automática.");
            Debug.Log($"[RaceSelector] Imagem existente: {GlobalFichaData.caminhoImagemToken}");
            return;
        }

        if (racaSelecionada == null)
        {
            Debug.LogWarning("[RaceSelector] ❌ Nenhuma raça selecionada");
            return;
        }

        Debug.Log($"[RaceSelector] Raça selecionada: {ObterNomeExibicaoRaca(racaSelecionada)}");

        try
        {
            // Nome da imagem: remover espaços, hífens e converter para minúsculas
            string nomeRaca = ObterNomeExibicaoRaca(racaSelecionada)
                .Replace(" ", "")
                .Replace("-", "")
                .ToLower();
            
            Debug.Log($"[RaceSelector] Nome normalizado para busca: '{nomeRaca}'");
            
            // Mesma prioridade usada por DndSrdLocalPath.Resolver: persistentDataPath
            // (onde o bootstrapper externo instala/atualiza conteúdo) primeiro,
            // caindo para StreamingAssets apenas como fallback (Editor ou instalação sem bootstrapper).
            string pastaRetratosPersistente = Path.Combine(Application.persistentDataPath, "retratos");
            string pastaRetratosStreaming = Path.Combine(Application.streamingAssetsPath, "retratos");
            string pastaRetratos = Directory.Exists(pastaRetratosPersistente) ? pastaRetratosPersistente : pastaRetratosStreaming;
            Debug.Log($"[RaceSelector] Pasta de retratos (persistente): {pastaRetratosPersistente} | existe? {Directory.Exists(pastaRetratosPersistente)}");
            Debug.Log($"[RaceSelector] Pasta de retratos escolhida: {pastaRetratos}");
            Debug.Log($"[RaceSelector] Pasta existe? {Directory.Exists(pastaRetratos)}");
            
            if (Directory.Exists(pastaRetratos))
            {
                string[] arquivos = Directory.GetFiles(pastaRetratos);
                Debug.Log($"[RaceSelector] Arquivos encontrados na pasta: {arquivos.Length}");
                foreach (string arquivo in arquivos)
                {
                    Debug.Log($"  - {Path.GetFileName(arquivo)}");
                }
            }
            
            // Tentar várias extensões
            string[] extensoes = { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };
            string caminhoImagem = null;

            foreach (string ext in extensoes)
            {
                string caminho = Path.Combine(pastaRetratos, nomeRaca + ext);
                Debug.Log($"[RaceSelector] Procurando: {nomeRaca + ext}");
                
                if (File.Exists(caminho))
                {
                    caminhoImagem = caminho;
                    Debug.Log($"[RaceSelector] ✓ ENCONTRADO: {caminho}");
                    break;
                }
            }

            if (caminhoImagem != null && File.Exists(caminhoImagem))
            {
                Debug.Log("[RaceSelector] Obtendo CriacaoFichaProgressaoDnd...");
                
                // Obter TokenImageSelector da progressão
                CriacaoFichaProgressaoDnd progressao = FindObjectOfType<CriacaoFichaProgressaoDnd>();
                
                if (progressao != null)
                {
                    Debug.Log("[RaceSelector] ✓ CriacaoFichaProgressaoDnd encontrado");
                    Debug.Log("[RaceSelector] Obtendo TokenImageSelector...");
                    
                    TokenImageSelector tokenSelector = progressao.ObterSeletorImagem();
                    
                    if (tokenSelector != null)
                    {
                        Debug.Log("[RaceSelector] ✓ TokenImageSelector encontrado");
                        Debug.Log($"[RaceSelector] Carregando imagem: {caminhoImagem}");
                        
                        tokenSelector.DefinirImagemDoArquivo(caminhoImagem);
                        
                        Debug.Log($"[RaceSelector] ✓✓✓ SUCESSO! Imagem de raça carregada: {caminhoImagem}");
                    }
                    else
                    {
                        Debug.LogError("[RaceSelector] ❌ TokenImageSelector é NULL!");
                    }
                }
                else
                {
                    Debug.LogError("[RaceSelector] ❌ CriacaoFichaProgressaoDnd não encontrado!");
                }
            }
            else
            {
                Debug.LogWarning($"[RaceSelector] ❌ Nenhuma imagem encontrada para raça '{nomeRaca}' em {pastaRetratos}/");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RaceSelector] ❌ ERRO ao tentar carregar imagem automática: {ex.Message}");
            Debug.LogError($"[RaceSelector] Stack trace: {ex.StackTrace}");
        }
        
        Debug.Log("[RaceSelector] ===== FIM DO CARREGAMENTO DE IMAGEM =====");
    }
}