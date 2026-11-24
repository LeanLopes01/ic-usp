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
using UnityEngine.Networking;
using TMPro;

public class BackgroundSelector : MonoBehaviour
{
    [Header("API")]
    public string apiUrl = "https://docjediwho.com.br/database_vtt/get_antecedentes.php";
    public string saveUrl = "https://docjediwho.com.br/database_vtt/save_background_to_ficha.php";

    [Header("UI - Lista Cascata (Aurora Style)")]
    public Transform mainScrollViewContent;
    public GameObject mainStepContainerPrefab; // Prefab com isMainStep=true (MainOptionButtonUI)
    public GameObject stepContainerPrefab;     // Prefab com isMainStep=false (SubChoiceOptionUI)

    [Header("UI - Detalhes (Direita)")]
    public TMP_Text infoText; 

    [Header("Referência SRD (Opcional)")]
    public SrdBookNavigator srdBookNavigator;

    [Header("Gerenciador de Progresso")]
    public CriacaoFichaProgressaoDnd criacaoFichaProgressao;

    [Header("Referência UI")]
    public FichaDisplayUI fichaDisplayUI;

    [Header("Fonte de Dados")]
    public bool preferirDadosLocais = true;
    public string pastaLocalRelativa = "sistemas/quinta-edicao-srd/antecedentes";

    public static BackgroundInfoData RestauracaoInfo = null;

    private List<BackgroundData> antecedentesDisponiveis = new List<BackgroundData>();
    private BackgroundData antecedenteSelecionado;
    private Queue<Action> filaDePassos = new Queue<Action>();
    private int passoAtualIndex = 0; // Rastreia o índice do passo sendo criado

    private string tracoSelecionado = "";
    private string idealSelecionado = "";
    private string ligacaoSelecionada = "";
    private string defeitoSelecionado = "";
    private bool fluxoIniciado = false;
    private bool dadosLocaisCarregados = false;
    private int contextoFichaId = -1;
    private List<string> linguasEscolhidas = new List<string>();
    private List<string> equipamentoEscolhido = new List<string>();
    private List<string> ferramentasEscolhidas = new List<string>(); 

    [Serializable] public class BackgroundData { public int id; public int remoteId; public string name; public string origem; public string xml; }
    [Serializable] public class BackgroundList { public BackgroundData[] backgrounds; }
    
    [Serializable]
    public class BackgroundInfoData {
        public string nome;   // nome do antecedente — usado para restauração robusta
        public string trait;
        public string ideal;
        public string bond;
        public string flaw;
        public List<string> skills = new List<string>();
        public List<string> armors = new List<string>();
        public List<string> weapons = new List<string>();
        public List<string> tools = new List<string>();
        public List<string> languages;
        public List<string> equipment;
    }
    
    [Serializable] public class DetalhesUpdate { public BackgroundInfoData background_info; }

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
    /// Gatilho independente de OnEnable/Start, pro caso deste passo virar um GameObject sempre
    /// ativo (mesma correção já aplicada em ClassSelector pro bug de CanvasGroup/layout preso após
    /// SetActive(false)-&gt;(true)) — nesse cenário OnEnable nunca mais dispara de novo, e sem isto
    /// o antecedente já "confirmado" de uma ficha ANTERIOR continuaria preso aqui mesmo depois de
    /// criar/abrir outra ficha. GlobalFichaData.OnSessaoDeFichaIniciada dispara em TODA troca de
    /// ficha, independente do ciclo de vida do GameObject.
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
    /// (FichaLoaderUI.CarregarRestauracaoDoSnapshot -&gt; BackgroundSelector.RestauracaoInfo,
    /// GlobalFichaData.backgroundIdRestauracao). Reconstruir o fluxo agora, ainda dentro do próprio
    /// evento, pegaria RestauracaoInfo vazio — o antecedente já salvo da ficha nunca
    /// auto-selecionava. Antes desta tela virar (ou poder vir a virar) um GameObject sempre ativo,
    /// um OnEnable real logo depois (quando CarregarRestauracaoDoSnapshot já tinha rodado e setado
    /// precisaRestaurarFluxo) corrigia isso sozinho; sem esse segundo OnEnable, o adiamento de um
    /// frame faz esse papel.
    /// </summary>
    private IEnumerator TentarIniciarFluxoParaFichaAtualAdiado()
    {
        yield return null;
        TentarIniciarFluxoParaFichaAtual();
    }

    private void TentarIniciarFluxoParaFichaAtual()
    {
        if (preferirDadosLocais && !dadosLocaisCarregados && TentarCarregarAntecedentesLocais(out var locais) && locais.Count > 0)
        {
            dadosLocaisCarregados = true;
            IniciarFluxo(locais);
            StartCoroutine(SincronizarIdsRemotosPorNome());
            return;
        }

        if (antecedentesDisponiveis.Count > 0) IniciarFluxo(antecedentesDisponiveis);
        else { if (infoText) infoText.text = "Carregando antecedentes..."; StartCoroutine(CarregarAntecedentesEIniciar()); }
    }

    private IEnumerator CarregarAntecedentesEIniciar()
    {
        using (UnityWebRequest req = UnityWebRequest.Get(apiUrl)) {
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success) {
                string cleanJson = req.downloadHandler.text.Trim().Replace("\uFEFF", "");
                try {
                    BackgroundList lista = JsonUtility.FromJson<BackgroundList>(cleanJson);
                    if (lista != null && lista.backgrounds != null) IniciarFluxo(lista.backgrounds.ToList());
                } catch (Exception e) { Debug.LogError("Erro JSON: " + e.Message); }
            }
        }
    }

    public void IniciarFluxo(List<BackgroundData> dadosDescarregados)
    {
        fluxoIniciado = true; antecedentesDisponiveis = dadosDescarregados;
        tracoSelecionado = ""; idealSelecionado = ""; ligacaoSelecionada = ""; defeitoSelecionado = "";
        linguasEscolhidas.Clear(); equipamentoEscolhido.Clear(); ferramentasEscolhidas.Clear(); filaDePassos.Clear();
        passoAtualIndex = 0; // Reseta o índice de passos

        foreach (Transform child in mainScrollViewContent) Destroy(child.gameObject);
        if (infoText) infoText.text = "A sua história define quem você é. Escolha o seu Antecedente.";
        CriarPasso_1_Antecedentes();
    }

    private void AvancarPasso() {
        if (filaDePassos.Count > 0) { Action proximoPasso = filaDePassos.Dequeue(); proximoPasso.Invoke(); }
        else {
            if (infoText) infoText.text = "<color=green>Histórico construído com sucesso! Salvando...</color>";
            RegistrarNoLog();
            StartCoroutine(SalvarAntecedentePublico());

            if (criacaoFichaProgressao != null)
            {
                criacaoFichaProgressao.RefreshProgressaoUI();
                Debug.Log("[BackgroundSelector] ✓ CriacaoFichaProgressaoDnd.RefreshProgressaoUI() chamado para atualizar bônus");
            }

            AtualizarFichaDisplayComBonus();
        }
    }

    private void AtualizarFichaDisplayComBonus()
    {
        if (fichaDisplayUI == null)
        {
            Debug.LogWarning("[BackgroundSelector] fichaDisplayUI não atribuído - não é possível atualizar display");
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
        Debug.Log($"[BackgroundSelector] ✓ FichaDisplayUI atualizado com bônus: FOR={forcaTotal}, DEX={destrezaTotal}");
    }

    private void RegistrarNoLog()
    {
        if (antecedenteSelecionado == null) return;
        var sb = new System.Text.StringBuilder();
        sb.Append(antecedenteSelecionado.name);
        if (!string.IsNullOrEmpty(tracoSelecionado))
            sb.Append($"\nTraço: {tracoSelecionado}");
        if (!string.IsNullOrEmpty(idealSelecionado))
            sb.Append($"\nIdeal: {idealSelecionado}");
        if (!string.IsNullOrEmpty(ligacaoSelecionada))
            sb.Append($"\nVínculo: {ligacaoSelecionada}");
        if (!string.IsNullOrEmpty(defeitoSelecionado))
            sb.Append($"\nDefeito: {defeitoSelecionado}");
        if (linguasEscolhidas.Count > 0)
            sb.Append($"\nIdiomas: {string.Join(", ", linguasEscolhidas)}");
        FichaLogManager.RegistrarEscolha(1, "Antecedente", sb.ToString());
    }

    private void CriarPasso_1_Antecedentes()
    {
        var prefab = mainStepContainerPrefab != null ? mainStepContainerPrefab : stepContainerPrefab;
        GameObject go = UIUtils.Spawn(prefab, mainScrollViewContent, false);
        go.SetActive(true); StepContainerUI stepUI = go.GetComponent<StepContainerUI>();
        
        // Armazena o índice real do container
        Transform containerAtual = stepUI.transform;

        // Chave = nome (estável entre sessões); chave por ID era efêmera (int sequencial)
        Dictionary<string, string> opcoes = new Dictionary<string, string>();
        foreach (var bg in antecedentesDisponiveis)
            if (!opcoes.ContainsKey(bg.name)) opcoes.Add(bg.name, bg.name);
        
        // Callback para deselecção: remove passos posteriores
        System.Action onDeselectAntecedente = () => {
            RemoverPassosPosteriores(containerAtual.GetSiblingIndex());
            antecedenteSelecionado = null;
        };

        stepUI.SetupStep("1. ESCOLHA SEU ANTECEDENTE", opcoes, 
            (idSelecionado) => {
                var bg = antecedentesDisponiveis.FirstOrDefault(x => x.name == idSelecionado);
                if (bg != null)
                {
                    MostrarDetalhesAntecedente(bg.xml);
                    MostrarReferenciaSrd(bg.name);
                }
            },
            (idConfirmado) => {
                // chave agora é o nome — busca por nome
                antecedenteSelecionado = antecedentesDisponiveis.FirstOrDefault(x => x.name == idConfirmado);
                GlobalFichaData.currentAntecedenteId = antecedenteSelecionado.remoteId > 0 ? antecedenteSelecionado.remoteId : antecedenteSelecionado.id;
                MostrarReferenciaSrd(antecedenteSelecionado.name);

                // Aplica <metadata><modifiers> do antecedente de forma centralizada
                FichaModifierApplier.AplicarDeXml(antecedenteSelecionado.xml, antecedenteSelecionado.name);

                filaDePassos.Clear();
                string resTraco  = RestauracaoInfo?.trait;
                string resIdeal  = RestauracaoInfo?.ideal;
                string resBond   = RestauracaoInfo?.bond;
                string resFlaw   = RestauracaoInfo?.flaw;
                filaDePassos.Enqueue(() => CriarPasso_Caracteristica("traits", "2. TRAÇO DE PERSONALIDADE", (val) => tracoSelecionado = val, resTraco));
                filaDePassos.Enqueue(() => CriarPasso_Caracteristica("ideals", "3. IDEAL",                  (val) => idealSelecionado  = val, resIdeal));
                filaDePassos.Enqueue(() => CriarPasso_Caracteristica("bonds",  "4. VÍNCULO",               (val) => ligacaoSelecionada = val, resBond));
                filaDePassos.Enqueue(() => CriarPasso_Caracteristica("flaws",  "5. DEFEITO",               (val) => defeitoSelecionado = val, resFlaw));
                filaDePassos.Enqueue(() => CriarPasso_Idiomas(antecedenteSelecionado.xml));
                
                LerConfiguracaoFerramentasBackground(antecedenteSelecionado.xml);
                LerConfiguracaoEquipamentosBackground(antecedenteSelecionado.xml);

                AvancarPasso(); 
            },
            true,
            onDeselectAntecedente
        );
        if (!string.IsNullOrEmpty(GlobalFichaData.backgroundIdRestauracao))
            stepUI.AutoSelecionarOpcao(GlobalFichaData.backgroundIdRestauracao);
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

    private void CriarPasso_Caracteristica(string xmlTag, string tituloPasso, Action<string> onSalvarEscolha, string valorRestauracao = null)
    {
        XDocument doc = XDocument.Parse(antecedenteSelecionado.xml);
        var caracteristicas = doc.Descendants(xmlTag).Elements("option").ToList();
        if (caracteristicas.Count == 0) { AvancarPasso(); return; }

        GameObject go = Instantiate(stepContainerPrefab, mainScrollViewContent, false);
        go.SetActive(true);
        StepContainerUI stepUI = go.GetComponent<StepContainerUI>();

        Dictionary<string, string> opcoes = new Dictionary<string, string>();
        Dictionary<string, string> textosCompletos = new Dictionary<string, string>();
        int index = 1;
        foreach (var opt in caracteristicas)
        {
            string chave = $"opt_{index}";
            opcoes.Add(chave, $"Opção {index}");
            textosCompletos.Add(chave, opt.Value.Trim());
            index++;
        }

        stepUI.SetupStep(tituloPasso, opcoes,
            (id) => {
                if (infoText && textosCompletos.ContainsKey(id))
                    infoText.text = $"<size=120%><b>{tituloPasso}</b></size>\n\n{textosCompletos[id]}";
            },
            (idConfirmado) => { onSalvarEscolha.Invoke(textosCompletos[idConfirmado]); AvancarPasso(); },
            true
        );

        if (!string.IsNullOrEmpty(valorRestauracao))
            foreach (var kvp in textosCompletos)
                if (kvp.Value == valorRestauracao) { stepUI.AutoSelecionarOpcao(kvp.Key); break; }
    }

    private void CriarPasso_Idiomas(string xmlAntecedente)
    {
        XDocument doc = XDocument.Parse(xmlAntecedente); var langsEl = doc.Descendants("languages").FirstOrDefault();
        var langOptions = langsEl?.Elements("option").ToList() ?? langsEl?.Elements("language").ToList();
        if (langOptions == null || langOptions.Count == 0) { AvancarPasso(); return; }

        GameObject go = Instantiate(stepContainerPrefab, mainScrollViewContent, false); go.SetActive(true); StepContainerUI stepUI = go.GetComponent<StepContainerUI>();
        Dictionary<string, string> opcoes = new Dictionary<string, string>(); foreach (var opt in langOptions) { opcoes.Add(opt.Value, opt.Value); }

        stepUI.SetupStep("IDIOMAS EXTRAS", opcoes,
            (id) => { if (infoText) infoText.text = "Escolha um idioma extra concedido pelo seu antecedente."; },
            (idConfirmado) => {
                linguasEscolhidas.Add(idConfirmado);
                ProficiencySelector.IdiomasAntecedenteLocal = new List<string>(linguasEscolhidas);
                ProficiencySelector.OnImportacaoSolicitada?.Invoke();
                AvancarPasso();
            },
            true
        );
        if (RestauracaoInfo?.languages != null)
            foreach (var lang in RestauracaoInfo.languages)
                if (opcoes.ContainsKey(lang)) { stepUI.AutoSelecionarOpcao(lang); break; }
    }

    private void LerConfiguracaoFerramentasBackground(string xmlClasse)
    {
        XDocument doc = XDocument.Parse(xmlClasse); 
        var tools = doc.Descendants("tool-proficiencies").FirstOrDefault();
        if (tools == null) return;

        var fixedNode = tools.Element("fixed");
        if (fixedNode != null) {
            foreach (var item in fixedNode.Elements("item")) {
                string idAttr = item.Attribute("id")?.Value;
                if (!string.IsNullOrEmpty(idAttr)) ferramentasEscolhidas.Add(idAttr); 
                else ferramentasEscolhidas.Add($"CUSTOM:{item.Value}");
            }
        } 
        else 
        {
            foreach (var tool in tools.Elements("tool")) {
                string valLimpo = tool.Value.Trim();
                if (!string.IsNullOrEmpty(valLimpo) && valLimpo.ToLower() != "none") {
                    ferramentasEscolhidas.Add(valLimpo);
                }
            }
        }

        if (ferramentasEscolhidas.Count > 0) {
            ProficiencySelector.FerramentasAntecedenteLocal = new List<string>(ferramentasEscolhidas);
            ProficiencySelector.OnImportacaoSolicitada?.Invoke();
        }

        var choices = tools.Elements("choice").ToList();
        foreach (var choice in choices) { XElement c = choice; filaDePassos.Enqueue(() => CriarPasso_EscolhaFerramentaBackground(c)); }
    }

    private void CriarPasso_EscolhaFerramentaBackground(XElement choiceNode)
    {
        string pergunta = choiceNode.Attribute("text")?.Value ?? "Escolha uma Proficiência";
        GameObject go = Instantiate(stepContainerPrefab, mainScrollViewContent, false); go.SetActive(true); StepContainerUI stepUI = go.GetComponent<StepContainerUI>();
        Dictionary<string, string> opcoes = new Dictionary<string, string>();
        foreach (var opt in choiceNode.Elements("option")) { string dbName = opt.Attribute("id")?.Value ?? opt.Value; opcoes.Add(dbName, opt.Value); }

        stepUI.SetupStep("PROFICIÊNCIA EXTRA: " + pergunta.ToUpper(), opcoes, 
            (id) => { if (infoText) infoText.text = "Ferramentas concedidas pelo seu passado."; },
            (idConfirmado) => { 
                string[] itensDb = idConfirmado.Split(','); foreach(string i in itensDb) ferramentasEscolhidas.Add(i.Trim());
                
                ProficiencySelector.FerramentasAntecedenteLocal = new List<string>(ferramentasEscolhidas);
                ProficiencySelector.OnImportacaoSolicitada?.Invoke(); 

                AvancarPasso(); 
            },
            true
        );
    }

    private void LerConfiguracaoEquipamentosBackground(string xmlClasse)
    {
        XDocument doc = XDocument.Parse(xmlClasse); var equips = doc.Descendants("equipment").FirstOrDefault();
        if (equips == null) return;
        var fixedNode = equips.Element("fixed");
        if (fixedNode != null) {
            foreach (var item in fixedNode.Elements("item")) {
                string idAttr = item.Attribute("id")?.Value;
                if (idAttr == "Ouro" || idAttr == "GP") equipamentoEscolhido.Add($"OURO:{item.Value}");
                else if (!string.IsNullOrEmpty(idAttr)) equipamentoEscolhido.Add($"{idAttr}|{item.Value}");
                else equipamentoEscolhido.Add($"CUSTOM:{item.Value}");
            }
            EquipmentSelector.EquipamentosAntecedenteLocal = new List<string>(equipamentoEscolhido); EquipmentSelector.ImportacaoLocalPendente = true;
        }
        var choices = equips.Elements("choice").ToList();
        foreach (var choice in choices) { XElement c = choice; filaDePassos.Enqueue(() => CriarPasso_EscolhaEquipamentoBackground(c)); }
    }

    private void CriarPasso_EscolhaEquipamentoBackground(XElement choiceNode)
    {
        string pergunta = choiceNode.Attribute("text")?.Value ?? "Escolha seu equipamento";

        // Captura quantos itens existem ANTES desta escolha para evitar duplicatas ao re-confirmar.
        int contagemAntes = equipamentoEscolhido.Count;

        GameObject go = Instantiate(stepContainerPrefab, mainScrollViewContent, false); go.SetActive(true); StepContainerUI stepUI = go.GetComponent<StepContainerUI>();
        Dictionary<string, string> opcoes = new Dictionary<string, string>();
        foreach (var opt in choiceNode.Elements("option")) { string dbName = opt.Attribute("id")?.Value ?? opt.Value; opcoes.Add(dbName, opt.Value); }

        stepUI.SetupStep("EQUIPAMENTO", opcoes,
            (id) => { if (infoText) infoText.text = string.IsNullOrWhiteSpace(pergunta) ? "Equipamento inicial do seu passado." : pergunta; },
            (idConfirmado) => {
                // Remove qualquer item desta etapa adicionado anteriormente, evitando duplicatas.
                if (equipamentoEscolhido.Count > contagemAntes)
                    equipamentoEscolhido.RemoveRange(contagemAntes, equipamentoEscolhido.Count - contagemAntes);

                string[] itensDb = idConfirmado.Split(',');
                foreach (string i in itensDb) equipamentoEscolhido.Add(i.Trim());
                EquipmentSelector.EquipamentosAntecedenteLocal = new List<string>(equipamentoEscolhido);
                EquipmentSelector.ImportacaoLocalPendente = true;
                AvancarPasso();
            },
            true
        );

        // Auto-seleciona a partir do histórico salvo (restauração).
        if (RestauracaoInfo?.equipment != null)
        {
            foreach (string equip in RestauracaoInfo.equipment)
            {
                if (opcoes.ContainsKey(equip)) { stepUI.AutoSelecionarOpcao(equip); break; }
            }
        }
    }

    private void MostrarDetalhesAntecedente(string xml) {
        if (string.IsNullOrEmpty(xml)) return;
        try {
            XDocument doc = XDocument.Parse(xml); var root = doc.Element("background") ?? doc.Root;
            string nome = root.Element("name")?.Value ?? "Antecedente", summary = root.Element("summary")?.Value ?? root.Element("description")?.Value ?? "Sem descrição.";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"<size=160%><b>{nome.ToUpper()}</b></size>"); sb.AppendLine($"<i>{summary}</i>\n");

            var feature = root.Element("feature");
            if (feature != null) {
                string fName = feature.Element("name")?.Value ?? "Característica", fDesc = feature.Element("description")?.Value ?? feature.Element("text")?.Value ?? "";
                sb.AppendLine($"<size=120%><b>Habilidade: {fName}</b></size>"); sb.AppendLine($"<color=#cccccc>{fDesc}</color>");
            }
            if (infoText) infoText.text = sb.ToString();
        } catch { if (infoText) infoText.text = "Erro ao ler XML."; }
    }

    private void MostrarReferenciaSrd(string termo)
    {
        if (srdBookNavigator == null || string.IsNullOrWhiteSpace(termo)) return;
        srdBookNavigator.MostrarPorTermo(termo);
    }

    public IEnumerator SalvarAntecedentePublico() {
        if (antecedenteSelecionado == null) yield break;

        int antecedenteIdSalvar = antecedenteSelecionado.remoteId > 0 ? antecedenteSelecionado.remoteId : antecedenteSelecionado.id;
        
        BackgroundInfoData infoData = new BackgroundInfoData {
            nome      = antecedenteSelecionado.name,
            trait     = tracoSelecionado,
            ideal     = idealSelecionado,
            bond      = ligacaoSelecionada,
            flaw      = defeitoSelecionado,
            languages = linguasEscolhidas,
            equipment = equipamentoEscolhido
        };

        // Filtra ferramentas (caso algum antecedente dê proficiência em arma/armadura custom)
        foreach(var item in ferramentasEscolhidas) {
            string nomeLimpo = item.ToLower();
            if(nomeLimpo.Contains("arma") || nomeLimpo.Contains("weapon") || nomeLimpo.Contains("espada") || nomeLimpo.Contains("machado")) {
                infoData.weapons.Add(item);
            }
            else if(nomeLimpo.Contains("armadura") || nomeLimpo.Contains("escudo") || nomeLimpo.Contains("armor") || nomeLimpo.Contains("shield")) {
                infoData.armors.Add(item);
            }
            else {
                infoData.tools.Add(item);
            }
        }

        DetalhesUpdate update = new DetalhesUpdate(); 
        update.background_info = infoData;
        string json = JsonUtility.ToJson(update); 

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        bool ok = LocalFichaStorage.SaveBackgroundInfo(
            steamId,
            playerId,
            GlobalFichaData.fichasGeneralId,
            GlobalFichaData.fichasDndId,
            antecedenteIdSalvar.ToString(),
            json
        );

        if (ok) {
            GlobalFichaData.currentAntecedenteId = antecedenteIdSalvar;
            GlobalFichaData.backgroundInfoJson = json;
            Debug.Log($"<color=green>Antecedente salvo localmente! ID={antecedenteSelecionado.id}</color>");

            // Salva o inventário com os itens do antecedente imediatamente,
            // para persistir mesmo que o usuário feche sem abrir a aba de inventário.
            // ResolveInstanciaPrincipal() em vez de FindObjectOfType sozinho — a cena tem duas
            // instâncias de EquipmentSelector, uma delas delegada (nunca salva em disco), e a
            // resolução sem filtro já causou perda de itens (ver EquipmentSelector.cs).
            EquipmentSelector equipSel = EquipmentSelector.ResolveInstanciaPrincipal();
            if (equipSel != null)
            {
                EquipmentSelector.ImportacaoLocalPendente = false;
                yield return equipSel.RotinaReceberESalvarEquipamentosAntecedente(new List<string>(equipamentoEscolhido));
            }
            else
            {
                EquipmentSelector.EquipamentosAntecedenteLocal = new List<string>(equipamentoEscolhido);
                EquipmentSelector.ImportacaoLocalPendente = true;
            }

            // Só daqui em diante é seguro avançar de etapa — mesmo raciocínio de
            // ClassSelector.SalvarClassePublico: fazer isso antes (ex: junto ao RefreshProgressaoUI
            // em AvancarPasso()) poderia desativar o painel de Antecedente enquanto esta corrotina
            // ainda está suspensa em RotinaReceberESalvarEquipamentosAntecedente, matando-a no meio
            // e perdendo os itens do antecedente. Só chega aqui depois que tudo já foi salvo.
            if (criacaoFichaProgressao != null)
                criacaoFichaProgressao.AvancarSeEtapaAtualFor(TipoResumoEtapaDnd.Antecedente);
        }
        else
            Debug.LogError("Erro ao salvar antecedente localmente (ficha não encontrada).");

        yield return null;
    }

    private bool TentarCarregarAntecedentesLocais(out List<BackgroundData> lista)
    {
        lista = new List<BackgroundData>();
        try
        {
            string pasta = DndSrdLocalPath.Resolver(pastaLocalRelativa);
            if (!Directory.Exists(pasta)) return false;

            int idSequencial = 1000;
            var arquivos = ImportadorConteudoUniversal.EnumerarArquivosXmlEJson(pasta, SearchOption.TopDirectoryOnly)
                .Where(f => !Path.GetFileName(f).ToLowerInvariant().Contains("template"));

            foreach (string arquivo in arquivos)
            {
                try
                {
                    string xml;
                    if (ImportadorConteudoUniversal.EhArquivoJson(arquivo))
                    {
                        string json = ImportadorConteudoUniversal.LerTextoComFallbackDeEncoding(arquivo);
                        xml = json != null ? ImportadorConteudoUniversal.ConverterJsonParaXml(json, "background") : null;
                        if (xml == null)
                        {
                            Debug.LogWarning($"[BackgroundSelector] JSON inv\u00E1lido em '{Path.GetFileName(arquivo)}' \u2014 ignorando.");
                            continue;
                        }
                    }
                    else
                    {
                        xml = File.ReadAllText(arquivo, Encoding.UTF8).Trim().Replace("\uFEFF", "");
                    }
                    XDocument doc = XDocument.Parse(xml);
                    XElement root = doc.Element("background") ?? doc.Root;
                    if (root == null) continue;

                    string nome = root.Element("name")?.Value;
                    if (string.IsNullOrWhiteSpace(nome)) continue;

                    lista.Add(new BackgroundData
                    {
                        id = idSequencial++,
                        remoteId = 0,
                        name = nome.Trim(),
                        origem = root.Attribute("source")?.Value ?? "Local",
                        xml = xml
                    });
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[BackgroundSelector] Ignorando arquivo inv\u00E1lido '{Path.GetFileName(arquivo)}': {e.Message}");
                }
            }

            lista = lista.OrderBy(b => b.name).ToList();
            return lista.Count > 0;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[BackgroundSelector] Falha ao carregar antecedentes locais: " + e.Message);
            return false;
        }
    }

    private IEnumerator SincronizarIdsRemotosPorNome()
    {
        using (UnityWebRequest req = UnityWebRequest.Get(apiUrl))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) yield break;

            string cleanJson = req.downloadHandler.text.Trim().Replace("\uFEFF", "");
            BackgroundList listaRemota = null;
            try { listaRemota = JsonUtility.FromJson<BackgroundList>(cleanJson); } catch { }
            if (listaRemota?.backgrounds == null || listaRemota.backgrounds.Length == 0) yield break;

            var mapPorNome = new Dictionary<string, BackgroundData>();
            foreach (var remota in listaRemota.backgrounds)
            {
                string chave = NormalizarChave(remota.name);
                if (!string.IsNullOrEmpty(chave) && !mapPorNome.ContainsKey(chave))
                    mapPorNome.Add(chave, remota);
            }

            int atualizados = 0;
            foreach (var local in antecedentesDisponiveis)
            {
                string chaveLocal = NormalizarChave(local.name);
                if (string.IsNullOrEmpty(chaveLocal)) continue;
                if (!mapPorNome.TryGetValue(chaveLocal, out var remota)) continue;

                if (local.remoteId != remota.id)
                {
                    local.remoteId = remota.id;
                    atualizados++;
                }
            }

            if (atualizados > 0)
                Debug.Log($"[BackgroundSelector] IDs remotos sincronizados por nome: {atualizados}");
        }
    }

    private static string NormalizarChave(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return "";
        string semAcento = valor.Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder(semAcento.Length);
        foreach (char c in semAcento)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}