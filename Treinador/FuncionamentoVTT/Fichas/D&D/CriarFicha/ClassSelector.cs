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

public class ClassSelector : MonoBehaviour
{
    // A cena tem múltiplas instâncias deste script (ex: Desktop Principal.unity tem uma ativa num
    // objeto raiz "sempre ativo" e outra desativada, sobra do painel de etapa "TelaClasse" antigo)
    // — mesmo motivo que já forçou EquipmentSelector.Instance: uma referência de Inspector fixa
    // (ex: FichaSaveManager.classSelector) pode acabar apontando pra instância errada/vazia, fazendo
    // o passo final de "Finalizar" salvar/reimportar equipamento de classe a partir de um
    // ClassSelector que o jogador nunca usou. Instance rastreia a última instância habilitada.
    public static ClassSelector Instance { get; private set; }

    [Header("UI - Lista Cascata")]
    public Transform mainScrollViewContent;
    public GameObject mainStepContainerPrefab; // Prefab com isMainStep=true (MainOptionButtonUI)
    public GameObject stepContainerPrefab;     // Prefab com isMainStep=false (SubChoiceOptionUI)

    [Header("UI - Detalhes (Direita)")]
    public TMP_Text infoText; 

    [Header("Gerenciador de Progresso")]
    public CriacaoFichaProgressaoDnd criacaoFichaProgressao;

    [Header("Referência SRD (Opcional)")]
    public SrdBookNavigator srdBookNavigator;

    [Header("Referência UI")]
    public FichaDisplayUI fichaDisplayUI;

    [Header("Multiclasse")]
    public GameObject gerenciadorMulticlassePrefab;

    [Header("Fonte de Dados")]
    public bool preferirDadosLocais = true;
    public string pastaLocalRelativa = "sistemas/quinta-edicao-srd/classes";
    public string pastaTalentosRelativa = "sistemas/quinta-edicao-srd/talentos";

    private List<ClassData> classesDisponiveis = new List<ClassData>(); 
    private ClassData classeSelecionada;
    private int playerLevel = 1;
    private bool fluxoIniciado = false;
    private bool dadosLocaisCarregados = false;
    private int contextoFichaId = -1;
    private Queue<Action> filaDePassos = new Queue<Action>();
    private int passoAtualIndex = 0; // Rastreia o índice do passo sendo criado
    
    private List<MulticlasseGerenciador> _gerenciadoresMulticlasse = new List<MulticlasseGerenciador>();
    private bool _fluxoMulticlasseIniciado = false;
    private readonly List<CanvasGroup> _canvasGroupsTmp = new List<CanvasGroup>();
    /// <summary>True quando SalvarCheckpointClasse() já salvou cada classe separadamente no array classes[].</summary>
    private bool _checkpointsSalvos = false;

    private List<string> proficienciasEscolhidas = new List<string>();
    private List<string> equipamentoEscolhido = new List<string>();
    private List<string> ferramentasEscolhidas = new List<string>(); 
    private List<string> atributosTalentosEscolhidos = new List<string>();
    private List<ClassPowerData> poderesClasseDesbloqueados = new List<ClassPowerData>();
    private List<ClassPowerChoiceData> escolhasPoderesClasse = new List<ClassPowerChoiceData>();
    private string subclasseSelecionadaNome = "";

    [Serializable] public class ClassData { public string id; public string remoteId; public string name; public string xml; }
    [Serializable] public class ClassList { public ClassData[] classes; }
    
    [Serializable] 
    public class ClasseInfoData { 
        public string subclass_id; 
        public string subclass_name;
        public List<string> saves = new List<string>();
        public List<string> skills = new List<string>(); 
        public List<string> armors = new List<string>(); 
        public List<string> weapons = new List<string>(); 
        public List<string> tools = new List<string>(); 
        public List<string> languages = new List<string>();
        public List<string> equipment; 
        public List<string> asi_feats;
        public List<ClassPowerData> class_powers = new List<ClassPowerData>();
        public List<ClassPowerChoiceData> class_power_choices = new List<ClassPowerChoiceData>();
        public List<ClassPowerData> subclass_powers = new List<ClassPowerData>();
    }
    [Serializable] public class DetalhesUpdate { public ClasseInfoData classe_info; }

    [Serializable]
    public class ClassPowerData
    {
        public string id;
        public string name;
        public int level;
        public string description;
    }

    [Serializable]
    public class ClassPowerChoiceData
    {
        public string feature_id;
        public string feature_name;
        public int level;
        public string selected_option;
    }

    // Uma <option> de <choices> com seus pré-requisitos opcionais (ex.: Invocações Místicas do
    // Bruxo: "prereq-level" exige nível mínimo da classe, "prereq-pact" exige um Pacto específico
    // já escolhido em pact-boon). Atributos ausentes = sem pré-requisito, comportamento igual a
    // antes (Estilo de Luta do Guerreiro etc.).
    private class OpcaoEscolhaClasse
    {
        public string Texto;
        // Parte entre parênteses do <option> original (ex.: "Rajada Agonizante (dano de Rajada
        // Mística +CAR)" -> Texto="Rajada Agonizante", Descricao="dano de Rajada Mística +CAR").
        // Texto some do parêntese pra exibição na lista; Descricao aparece embaixo do infoText
        // genérico da feature quando essa opção é selecionada.
        public string Descricao;
        public int? PrereqNivel;
        public string PrereqPacto;
    }
    
    // --> DB de Talentos <--
    [Serializable] public class TalentoDB { public string id_talento; public string nome_talento; public string resumo_talento; }
    [Serializable] public class TalentosResponse { public string status; public List<TalentoDB> talentos; }
    
    public static ClasseInfoData RestauracaoInfo = null;

    /// <summary>
    /// Mapa de restauração per-classe: class_id â†' ClasseInfoData.
    /// Populado por FichaLoaderUI.CarregarRestauracaoDoSnapshot a partir de classes_json.
    /// Permite que cada gerenciador multiclasse restaure os dados da sua própria classe.
    /// </summary>
    public static Dictionary<string, ClasseInfoData> RestauracaoInfoPorClasse = new Dictionary<string, ClasseInfoData>();

    private List<string> armasClasse = new List<string>();
    private List<string> armadurasClasse = new List<string>();
    private static readonly string[] atributosOrdemASI = { "FOR", "DEX", "CON", "INT", "SAB", "CAR" };
    private static readonly Dictionary<string, string> atributosLabelASI = new Dictionary<string, string>
    {
        { "FOR", "Força (FOR)" },
        { "DEX", "Destreza (DEX)" },
        { "CON", "Constituição (CON)" },
        { "INT", "Inteligência (INT)" },
        { "SAB", "Sabedoria (SAB)" },
        { "CAR", "Carisma (CAR)" }
    };

    void OnEnable()
    {
        Instance = this;
        GlobalFichaData.OnSessaoDeFichaIniciada += OnSessaoDeFichaIniciada;

        Debug.Log("<b><color=#00FFFF>============= [*] [ClassSelector] INÍCIO DO FLUXO DE CLASSE [*] =============</color></b>");
        int fichaAtualId = GlobalFichaData.fichasDndId;
        if (contextoFichaId != fichaAtualId || GlobalFichaData.precisaRestaurarFluxo)
        {
            contextoFichaId = fichaAtualId;
            fluxoIniciado = false;
            dadosLocaisCarregados = false;
        }

        playerLevel = GlobalFichaData.levelPersonagem > 0 ? GlobalFichaData.levelPersonagem : 1;
        if (fluxoIniciado)
        {
            // O fluxo já foi construído antes — não reconstruir (perderia os slots de multiclasse
            // já escolhidos pelo jogador). Mas o painel acabou de passar por um ciclo
            // SetActive(false)->SetActive(true) (navegação entre passos via
            // CriacaoFichaProgressaoDnd.MostrarEtapa), e o mesmo tipo de bug já visto em
            // EquipmentSelector/SpellSelector pode deixar o conteúdo já criado com CanvasGroups
            // herdados em alpha 0/não-interativo — reforça a visibilidade sem tocar em nenhum dado.
            GarantirVisibilidadeContainer(mainScrollViewContent, "ClassSelector");
            return;
        }

        if (preferirDadosLocais && !dadosLocaisCarregados && TentarCarregarClassesLocais(out var classesLocais) && classesLocais.Count > 0)
        {
            dadosLocaisCarregados = true;
            IniciarFluxo(classesLocais);
            return;
        }
        
        if (classesDisponiveis.Count > 0) IniciarFluxo(classesDisponiveis);
        else { if (infoText) infoText.text = "<color=#B00020>Nenhuma classe local encontrada em sistemas/quinta-edicao-srd/classes.</color>"; }
    }

    void OnDisable()
    {
        GlobalFichaData.OnSessaoDeFichaIniciada -= OnSessaoDeFichaIniciada;
    }

    /// <summary>
    /// Gatilho independente de OnEnable/Start: este GameObject agora fica sempre ativo (correção
    /// do bug de CanvasGroup/layout preso após SetActive(false)-&gt;(true)), o que também significa
    /// que OnEnable nunca mais dispara de novo — sem isto, a classe já "confirmada" (e a UI
    /// mostrando essa seleção) de uma ficha ANTERIOR continuava presa aqui mesmo depois de
    /// criar/abrir outra ficha, já que fluxoIniciado nunca era resetado (GlobalFichaData.bonusClasseDict
    /// em si já é zerado pelo portão central em GlobalFichaData.IniciarSessaoParaFicha — o problema
    /// aqui é só o estado local/visual deste componente). GlobalFichaData.OnSessaoDeFichaIniciada
    /// dispara em TODA troca de ficha (nova, editar, trocar), independente do ciclo de vida do
    /// GameObject.
    /// </summary>
    private void OnSessaoDeFichaIniciada()
    {
        contextoFichaId = GlobalFichaData.fichasDndId;
        fluxoIniciado = false;
        dadosLocaisCarregados = false;
        playerLevel = GlobalFichaData.levelPersonagem > 0 ? GlobalFichaData.levelPersonagem : 1;
        StartCoroutine(TentarIniciarFluxoParaFichaAtualAdiado());
    }

    /// <summary>
    /// Espera um frame antes de reconstruir o fluxo. GlobalFichaData.IniciarSessaoParaFicha (que
    /// disparou o evento acima) só RESETA os dados de restauração — quem a chamou ainda vai rodar,
    /// de forma síncrona e logo em seguida (mesma call stack), o código que os POPULA de verdade
    /// (FichaLoaderUI.CarregarRestauracaoDoSnapshot -&gt; ClassSelector.RestauracaoInfo,
    /// GlobalFichaData.classIdRestauracao/subclassIdRestauracao). Reconstruir o fluxo agora, ainda
    /// dentro do próprio evento, pegaria RestauracaoInfo vazio — o passo de classe (e,
    /// principalmente, o de subclasse/arquétipo, que só chega a ser criado bem no fim da fila de
    /// passos) nunca auto-selecionava a escolha já salva da ficha. Antes desta tela virar um
    /// GameObject sempre ativo, um OnEnable real logo depois (quando CarregarRestauracaoDoSnapshot
    /// já tinha rodado e setado precisaRestaurarFluxo) corrigia isso sozinho; agora não existe mais
    /// esse segundo OnEnable pra salvar a situação, então o adiamento de um frame faz esse papel.
    /// </summary>
    private IEnumerator TentarIniciarFluxoParaFichaAtualAdiado()
    {
        yield return null;
        TentarIniciarFluxoParaFichaAtual();
    }

    private void TentarIniciarFluxoParaFichaAtual()
    {
        if (preferirDadosLocais && TentarCarregarClassesLocais(out var classesLocais) && classesLocais.Count > 0)
        {
            dadosLocaisCarregados = true;
            IniciarFluxo(classesLocais);
            return;
        }

        if (classesDisponiveis.Count > 0) IniciarFluxo(classesDisponiveis);
        else { if (infoText) infoText.text = "<color=#B00020>Nenhuma classe local encontrada em sistemas/quinta-edicao-srd/classes.</color>"; }
    }

    public void IniciarFluxo(List<ClassData> dadosDescarregados)
    {
        Debug.Log($"<color=#ADD8E6>[ClassSelector]</color> IniciarFluxo chamado. Total de Classes Disponíveis: {dadosDescarregados.Count}. Player Level: {playerLevel}");
        fluxoIniciado = true; classesDisponiveis = dadosDescarregados;
        proficienciasEscolhidas.Clear(); equipamentoEscolhido.Clear(); ferramentasEscolhidas.Clear(); atributosTalentosEscolhidos.Clear();
        poderesClasseDesbloqueados.Clear(); escolhasPoderesClasse.Clear(); subclasseSelecionadaNome = "";
        armasClasse.Clear(); armadurasClasse.Clear();
        // Evita vazar níveis pendentes de talento de uma ficha diferente/anterior nesta mesma sessão.
        // RestaurarEstadoInternoDeRestauracaoInfo() re-semeia os níveis realmente pendentes desta ficha.
        TalentosSelector.NiveisPendentesParaTalento.Clear();
        TalentosSelector.talentosEscolhidosNestaSessao.Clear();
        GlobalFichaData.currentSubclassId = ""; filaDePassos.Clear();
        passoAtualIndex = 0;
        _fluxoMulticlasseIniciado = false;
        _checkpointsSalvos = false;

        _gerenciadoresMulticlasse.Clear();
        foreach (Transform child in mainScrollViewContent) Destroy(child.gameObject);
        if (infoText) infoText.text = $"Bem-vindo. Você está no Nível {playerLevel}. Comece por escolher a sua Classe.";
        CriarPasso_1_Classes();
    }

    private void AvancarPasso() {
        if (filaDePassos.Count > 0) {
            Debug.Log($"[.] <color=#ADD8E6>[ClassSelector - Steps]</color> Avançando para o próximo passo. Restam: {filaDePassos.Count}");
            Action proximoPasso = filaDePassos.Dequeue();
            proximoPasso.Invoke();
        }
        else {
            _fluxoMulticlasseIniciado = false;
            if (infoText) infoText.text = "<color=#1B5E20>Construção da Classe concluída! Salvando Ficha e Inventário...</color>";
            RegistrarNoLog();
            Debug.Log("<b><color=#00FFFF>============= [*] [ClassSelector] FIM DO FLUXO (CHAMANDO SAVE) [*] =============</color></b>");
            StartCoroutine(SalvarClassePublico());

            CriacaoFichaProgressaoDnd progressao = FindObjectOfType<CriacaoFichaProgressaoDnd>();
            if (progressao != null)
            {
                progressao.RefreshProgressaoUI();
            }

            AtualizarFichaDisplayComBonus();
        }
    }

    private void AtualizarFichaDisplayComBonus()
    {
        if (fichaDisplayUI == null) return;

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
    }

    private void RegistrarNoLog()
    {
        if (classeSelecionada == null) return;
        var sb = new System.Text.StringBuilder();
        sb.Append(classeSelecionada.name);
        if (!string.IsNullOrEmpty(GlobalFichaData.currentSubclassId))
            sb.Append($"\nArquétipo: {GlobalFichaData.currentSubclassId}");
        if (proficienciasEscolhidas.Count > 0)
            sb.Append($"\nPerícias: {string.Join(", ", proficienciasEscolhidas)}");
        if (armasClasse.Count > 0)
            sb.Append($"\nArmas: {string.Join(", ", armasClasse)}");
        if (armadurasClasse.Count > 0)
            sb.Append($"\nArmaduras: {string.Join(", ", armadurasClasse)}");
        FichaLogManager.RegistrarEscolha(1, "Classe", sb.ToString());
        // Registra cada ASI/Talento no nível correto
        foreach (var asiEntry in atributosTalentosEscolhidos)
        {
            int nivel = 1;
            if (asiEntry.StartsWith("Nível ") && asiEntry.Contains(":"))
            {
                string nivelStr = asiEntry.Substring(6, asiEntry.IndexOf(':') - 6).Trim();
                int.TryParse(nivelStr, out nivel);
            }
            FichaLogManager.RegistrarEscolha(nivel, "Melhoria de Atributo", asiEntry);
        }
    }

    /// <summary>
    /// Sobe a cadeia de pais de <paramref name="content"/> forçando qualquer CanvasGroup a
    /// alpha=1/interactable=true/blocksRaycasts=true. Mesmo problema (e mesma correção) já
    /// aplicados em EquipmentSelector/SpellSelector: depois de um ciclo SetActive(false)-&gt;(true)
    /// causado pela navegação entre passos, o conteúdo já construído pode ficar visualmente
    /// "preso" invisível/não-interativo mesmo estando tecnicamente ativo.
    /// </summary>
    private void GarantirVisibilidadeContainer(Transform content, string tag)
    {
        if (content == null) return;
        if (!content.gameObject.activeSelf) content.gameObject.SetActive(true);

        Transform atual = content;
        int nivel = 0;
        while (atual != null && nivel < 8)
        {
            atual.GetComponents(_canvasGroupsTmp);
            for (int i = 0; i < _canvasGroupsTmp.Count; i++)
            {
                var cg = _canvasGroupsTmp[i];
                if (cg == null) continue;
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
            _canvasGroupsTmp.Clear();
            atual = atual.parent;
            nivel++;
        }

        if (content.TryGetComponent<LayoutGroup>(out _))
            LayoutRebuilder.ForceRebuildLayoutImmediate(content as RectTransform);

        Debug.Log("[" + tag + "] Container visibilidade reforçada.");
    }

    // â"€â"€ Multiclasse â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

    private void CriarGerenciadorMulticlasse(List<string> nomesClasses, int saldoNiveis, int slotIndex)
    {
        Debug.Log($"<b><color=#FFA500>[ClassSelector - Multiclasse]</color></b> Criando Slot {slotIndex} com saldo de {saldoNiveis} nível(is). Opções disponíveis: {nomesClasses.Count}");
        if (gerenciadorMulticlassePrefab == null || nomesClasses.Count == 0 || saldoNiveis <= 0) return;

        string titulo = slotIndex == 0
            ? $"1. ESCOLHA SUA CLASSE PRINCIPAL ({saldoNiveis} nível{(saldoNiveis > 1 ? "is" : "")} disponível{(saldoNiveis > 1 ? "is" : "")})"
            : $"MULTICLASSE {slotIndex + 1} ({saldoNiveis} nível{(saldoNiveis > 1 ? "is" : "")} restante{(saldoNiveis > 1 ? "s" : "")})";

        var go = UIUtils.Spawn(gerenciadorMulticlassePrefab, mainScrollViewContent);
        go.SetActive(true);
        var gm = go.GetComponent<MulticlasseGerenciador>();
        _gerenciadoresMulticlasse.Add(gm);
        gm.Inicializar(nomesClasses, saldoNiveis, titulo, OnGerenciadorAlterado);
    }

    private void OnGerenciadorAlterado(MulticlasseGerenciador gm, string classe, int nivel)
    {
        int idx = _gerenciadoresMulticlasse.IndexOf(gm);
        if (idx < 0) return;
        Debug.Log($"<b><color=#FFA500>[ClassSelector - Multiclasse]</color></b> Alteração detectada no Slot {idx}: Classe='{classe}', Nível={nivel}");

        // Desbloqueia o fluxo para permitir reedição
        _fluxoMulticlasseIniciado = false;

        // Remove da UI todos os passos criados abaixo deste gerenciador (proficiências, subclasse, etc.)
        RemoverPassosPosteriores(gm.transform.GetSiblingIndex());

        // Destrói todos os gerenciadores abaixo deste  -  a matemática mudou
        DestruirGerenciadoresAPartirDe(idx + 1);

        // Limpa passos gerados por seleção anterior
        filaDePassos.Clear();

        if (string.IsNullOrEmpty(classe) || nivel <= 0) return;

        SincronizarMulticlasseComGlobal();

        // Calcula saldo restante
        int totalUsado    = _gerenciadoresMulticlasse.Where(g => g.NivelSelecionado > 0).Sum(g => g.NivelSelecionado);
        int saldoRestante = playerLevel - totalUsado;

        Debug.Log($"<b><color=#FFA500>[ClassSelector - Multiclasse]</color></b> Saldo atualizado: Total Usado = {totalUsado} | Saldo Restante = {saldoRestante}");

        if (saldoRestante <= 0)
        {
            Debug.Log($"<b><color=#FFA500>[ClassSelector - Multiclasse]</color></b> Níveis Esgotados. Iniciando fluxo pós-seleção.");
            IniciarFluxoPosSelecaoMulticlasse();
            return;
        }

        // Classes ainda não escolhidas
        var jaEscolhidas = _gerenciadoresMulticlasse
            .Where(g => !string.IsNullOrEmpty(g.ClasseSelecionada))
            .Select(g => g.ClasseSelecionada)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var classesRestantes = classesDisponiveis
            .Where(c => !jaEscolhidas.Contains(c.name))
            .Select(c => c.name)
            .ToList();

        if (classesRestantes.Count > 0)
            CriarGerenciadorMulticlasse(classesRestantes, saldoRestante, _gerenciadoresMulticlasse.Count);
    }

    /// <summary>
    /// Extrai poderes de classe do XML até o nível dado, sem modificar nenhum estado interno.
    /// Usada por SalvarCheckpointClasse para classes secundárias.
    /// </summary>
    private List<ClassPowerData> ExtrairPoderesDeClasseReadOnly(string xmlClasse, int nivelAtual)
    {
        var resultado = new List<ClassPowerData>();
        XDocument doc;
        try { doc = XDocument.Parse(xmlClasse.Trim().Replace("ï»¿", "")); } catch { return resultado; }
        XElement root = doc.Element("class") ?? doc.Root;
        if (root == null) return resultado;
        XElement featuresNode = root.Element("features");
        XElement progression  = root.Element("level-progression");
        if (featuresNode == null || progression == null) return resultado;

        var mapa = featuresNode.Elements("feature")
            .Where(f => f.Attribute("id") != null)
            .GroupBy(f => f.Attribute("id")?.Value)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var lvlNode in progression.Elements("level"))
        {
            if (!int.TryParse(lvlNode.Attribute("number")?.Value, out int lvl) || lvl > nivelAtual) continue;
            foreach (var fRef in lvlNode.Descendants("feature-ref"))
            {
                string refId = fRef.Attribute("id")?.Value;
                if (string.IsNullOrEmpty(refId) || !mapa.TryGetValue(refId, out var feat)) continue;
                resultado.Add(new ClassPowerData
                {
                    id          = refId,
                    name        = feat.Element("name")?.Value ?? refId,
                    level       = lvl,
                    description = feat.Element("description")?.Value ?? feat.Element("text")?.Value ?? ""
                });
            }
        }
        return resultado;
    }

    /// <summary>
    /// Salva um checkpoint de UMA classe durante o wizard multiclasse.
    /// Chamado via fila de passos APÃ"S os passos de subclasse+ASI de cada classe,
    /// capturando subclasseSelecionadaNome no momento certo (antes de ser sobrescrito pela próxima classe).
    /// </summary>
    private void SalvarCheckpointClasse(string classId, string className, int level, bool isPrimary,
        string xml, int asiFeatCountAntes, int powerChoicesCountAntes = 0)
    {
        if (GlobalFichaData.fichasDndId <= 0) return;

        int    playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId  = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        // Poderes: primária usa a lista já acumulada; secundárias extraem direto do XML
        var classePowers = isPrimary
            ? new List<ClassPowerData>(poderesClasseDesbloqueados)
            : ExtrairPoderesDeClasseReadOnly(xml, level);

        // Poderes da subclasse selecionada neste momento (correto para esta classe)
        var subclassePowers = ExtrairPoderesSubclasseDesbloqueados(xml, GlobalFichaData.currentSubclassId, level);

        // ASI/Talentos acumulados SOMENTE por esta classe (adicionados desde asiFeatCountAntes)
        var asiFeats = atributosTalentosEscolhidos.Count > asiFeatCountAntes
            ? atributosTalentosEscolhidos.GetRange(asiFeatCountAntes,
                atributosTalentosEscolhidos.Count - asiFeatCountAntes)
            : new List<string>();

        // Choices de poder acumuladas SOMENTE por esta classe (isolado por índice, igual aos ASIs)
        var powerChoices = escolhasPoderesClasse.Count > powerChoicesCountAntes
            ? new List<ClassPowerChoiceData>(
                escolhasPoderesClasse.GetRange(powerChoicesCountAntes,
                    escolhasPoderesClasse.Count - powerChoicesCountAntes))
            : new List<ClassPowerChoiceData>();

        List<string> armorsList, weaponsList, toolsList, langsList, equipList;
        if (isPrimary)
        {
            armorsList  = new List<string>(armadurasClasse);
            weaponsList = new List<string>(armasClasse);
            toolsList   = new List<string>(ferramentasEscolhidas);
            langsList   = new List<string>(ProficiencySelector.IdiomasClasseLocal);
            equipList   = new List<string>(equipamentoEscolhido);
        }
        else
        {
            ExtrairProficienciasSecundarias(xml, out armorsList, out weaponsList, out toolsList, out langsList);
            ExtrairEquipamentosSecundarios(xml, out equipList);
        }

        var classeData = new ClasseData
        {
            class_id      = classId,
            class_name    = className,
            level         = level,
            is_primary    = isPrimary,
            subclass_id   = GlobalFichaData.currentSubclassId,
            subclass_name = subclasseSelecionadaNome,          // ← correto para ESTA classe neste momento
            saves     = isPrimary ? new List<string>(ProficiencySelector.SavesClasseLocal) : new List<string>(),
            skills    = isPrimary ? new List<string>(proficienciasEscolhidas)              : new List<string>(),
            armors    = armorsList,
            weapons   = weaponsList,
            tools     = toolsList,
            languages = langsList,
            equipment = equipList,
            asi_feats = asiFeats,
        };
        foreach (var p in classePowers)   classeData.class_powers.Add(ToEntry(p));
        foreach (var p in subclassePowers) classeData.subclass_powers.Add(ToEntry(p));
        foreach (var c in powerChoices)   classeData.class_power_choices.Add(ToChoiceEntry(c));

        LocalFichaStorage.SaveClassData(steamId, playerId,
            GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId, classeData);

        _checkpointsSalvos = true;
        Debug.Log($"[ClassSelector] [OK] Checkpoint salvo: {className} | Subclasse: {subclasseSelecionadaNome}");
    }

    private void IniciarFluxoPosSelecaoMulticlasse()
    {
        if (GlobalFichaData.multiclassesSelecionadas == null || GlobalFichaData.multiclassesSelecionadas.Count == 0) return;

        // Bloqueia callbacks tardios dos gerenciadores enquanto o fluxo estiver ativo.
        _fluxoMulticlasseIniciado = true;

        string nomeClassePrincipal = GlobalFichaData.multiclassesSelecionadas[0].className;
        classeSelecionada = classesDisponiveis.FirstOrDefault(
            c => string.Equals(c.name, nomeClassePrincipal, StringComparison.OrdinalIgnoreCase));

        if (classeSelecionada == null)
        {
            Debug.LogError($"<b><color=#FF0000>[ClassSelector - ERRO]</color></b> Classe principal '{nomeClassePrincipal}' não encontrada.");
            _fluxoMulticlasseIniciado = false;
            return;
        }

        int nivelClassePrincipal = GlobalFichaData.multiclassesSelecionadas[0].level;
        string xmlPrincipal = classeSelecionada.xml; // captura local para lambdas
        Debug.Log($"<b><color=#FFA500>[ClassSelector - Multiclasse]</color></b> Lendo Classe Principal: {classeSelecionada.name} (Nível {nivelClassePrincipal})");

        ExtrairDadoDeVidaESalvaguardas(xmlPrincipal);
        proficienciasEscolhidas.Clear();
        equipamentoEscolhido.Clear();
        ferramentasEscolhidas.Clear();
        atributosTalentosEscolhidos.Clear();
        armasClasse.Clear();
        armadurasClasse.Clear();
        filaDePassos.Clear();

        try {
            XDocument doc = XDocument.Parse(xmlPrincipal.Trim().Replace("ï»¿", ""));
            LerProficienciasFixasClasse(doc.Root);
        } catch { }

        // Proficiências, ferramentas e equipamentos: somente da classe principal (regra D&D 5e)
        int qtdProf = LerQuantidadeProficiencias(xmlPrincipal);
        for (int p = 1; p <= qtdProf; p++) { int n = p; filaDePassos.Enqueue(() => CriarPasso_Proficiencias(xmlPrincipal, n, qtdProf)); }

        LerConfiguracaoIdiomas(xmlPrincipal);
        LerConfiguracaoFerramentas(xmlPrincipal);
        LerConfiguracaoEquipamentos(xmlPrincipal);

        // Poderes de classe (choices inline): somente da classe principal
        ExtrairPoderesEClassChoices(xmlPrincipal, nivelClassePrincipal);

        // Subclasse, ASI e Checkpoint por classe: para TODAS as classes do multiclasse
        for (int i = 0; i < GlobalFichaData.multiclassesSelecionadas.Count; i++)
        {
            var mc = GlobalFichaData.multiclassesSelecionadas[i];
            ClassData dadosClasse = i == 0
                ? classeSelecionada
                : classesDisponiveis.FirstOrDefault(c => string.Equals(c.name, mc.className, StringComparison.OrdinalIgnoreCase));

            if (dadosClasse == null)
            {
                Debug.LogWarning($"[ClassSelector] Classe secundária '{mc.className}' não encontrada  -  subclasse/ASI ignorados.");
                continue;
            }

            string xmlClasse   = dadosClasse.xml;
            int    nivelClasse = mc.level;

            // Id desta classe — capturado cedo pra ser usado nos passos de subclasse/ASI abaixo,
            // que precisam resolver a restauração POR CLASSE (RestauracaoInfoPorClasse) em vez de
            // sempre ler a classe primária (RestauracaoInfo estático) durante multiclasse.
            string captClassId = !string.IsNullOrEmpty(dadosClasse.remoteId) ? dadosClasse.remoteId : dadosClasse.id;

            // Reset de subclasse: evita que currentSubclassId da classe anterior vaze para a próxima.
            bool captIsSecondary = (i > 0);
            filaDePassos.Enqueue(() => {
                GlobalFichaData.currentSubclassId = "";
                subclasseSelecionadaNome = "";
                AvancarPasso();
            });

            // Para classes secundárias:
            // 1) Captura índice de powerChoices APÓS o checkpoint da classe anterior ter executado.
            // 2) Enfileira passos de feature-choices lendo o XML (somente leitura, sem modificar).
            // A ordem espelha o fluxo da classe primária: choices → subclasse → ASI → checkpoint.
            int captPowerChoiceCount = 0;
            if (i > 0)
            {
                filaDePassos.Enqueue(() => { captPowerChoiceCount = escolhasPoderesClasse.Count; AvancarPasso(); });
                EnfileirarEscolhasParaClasseSecundaria(xmlClasse, nivelClasse, captClassId);
            }

            int subLvl = LerNivelSubclasse(xmlClasse);
            Debug.Log($"<b><color=#DA70D6>[ClassSelector - Subclasse]</color></b> Avaliando {mc.className}: Nível Atual {mc.level} | Nível Exigido p/ Subclasse: {subLvl}");
            if (nivelClasse >= subLvl && subLvl > 0)
            {
                string xmlCaptura  = xmlClasse;
                string nomeRef     = mc.className;
                bool   eSecundaria = captIsSecondary;
                string classIdCaptura = captClassId;
                filaDePassos.Enqueue(() => {
                    Debug.Log($"<b><color=#DA70D6>[ClassSelector - Subclasse]</color></b> Criando passo de subclasse para {nomeRef}.");
                    CriarPasso_Subclasse(xmlCaptura, eSecundaria, classIdCaptura);
                });
            }

            // Captura count ANTES dos passos de ASI desta classe (isolamento per-classe) — precisa
            // ser capturado NA EXECUÇÃO da fila, não aqui na construção: este for roda inteiro de
            // forma síncrona ANTES de qualquer passo enfileirado realmente rodar, então
            // atributosTalentosEscolhidos.Count sempre lia 0 pra TODAS as classes (a classe
            // anterior ainda não tinha, de fato, adicionado seu próprio ASI à lista compartilhada
            // nesse momento) — por isso a segunda classe em diante sempre herdava o ASI da classe
            // anterior dentro do próprio checkpoint (confirmado em ficha real: classe secundária
            // salva com o ASI da classe primária duplicado dentro do seu próprio asi_feats). Mesmo
            // padrão já usado acima pra captPowerChoiceCount.
            int captAsiCount = 0;
            filaDePassos.Enqueue(() => { captAsiCount = atributosTalentosEscolhidos.Count; AvancarPasso(); });

            List<int> niveisAsi = LerNiveisASI(xmlClasse, nivelClasse);
            foreach (int n in niveisAsi) { int nv = n; string classIdParaAsi = captClassId; filaDePassos.Enqueue(() => CriarPasso_ASI_Talento(nv, classIdParaAsi)); }

            // Checkpoint -- captura de closure por valor para evitar problemas no loop
            string captClassName = mc.className;
            int    captLevel     = mc.level;
            bool   captIsPrimary = (i == 0);
            string captXml       = dadosClasse.xml;

            filaDePassos.Enqueue(() => {
                SalvarCheckpointClasse(captClassId, captClassName, captLevel, captIsPrimary, captXml, captAsiCount, captPowerChoiceCount);
                AvancarPasso();
            });
        }

        Debug.Log($"[.] <color=#ADD8E6>[ClassSelector - Steps]</color> Fila pós-multiclasse montada com {filaDePassos.Count} passo(s). Iniciando Queue...");
        AvancarPasso();
    }

    private int ObterNivelClassePrincipal()
    {
        if (GlobalFichaData.modoMulticlasse
            && GlobalFichaData.multiclassesSelecionadas != null
            && GlobalFichaData.multiclassesSelecionadas.Count > 0)
            return GlobalFichaData.multiclassesSelecionadas[0].level;
        return playerLevel;
    }

    /// <summary>Retorna o nível específico de uma classe pelo nome, lendo de multiclassesSelecionadas.</summary>
    private int ObterNivelDestaClasse(string nomeClasse)
    {
        if (GlobalFichaData.multiclassesSelecionadas != null)
            foreach (var mc in GlobalFichaData.multiclassesSelecionadas)
                if (string.Equals(mc.className, nomeClasse, StringComparison.OrdinalIgnoreCase))
                    return mc.level;
        return playerLevel;
    }

    private static ClassPowerEntry ToEntry(ClassPowerData p) =>
        new ClassPowerEntry { id = p.id, name = p.name, level = p.level, description = p.description };

    private static ClassPowerChoiceEntry ToChoiceEntry(ClassPowerChoiceData c) =>
        new ClassPowerChoiceEntry { feature_id = c.feature_id, feature_name = c.feature_name, level = c.level, selected_option = c.selected_option };

    private ClasseData BuildClasseData(string classId, bool isPrimary, ClasseInfoData info)
    {
        var data = new ClasseData
        {
            class_id    = classId,
            class_name  = classeSelecionada != null ? classeSelecionada.name : GlobalFichaData.classePersonagem,
            level       = ObterNivelDestaClasse(classeSelecionada != null ? classeSelecionada.name : GlobalFichaData.classePersonagem),
            is_primary  = isPrimary,
            subclass_id   = info?.subclass_id   ?? GlobalFichaData.currentSubclassId,
            subclass_name = info?.subclass_name ?? subclasseSelecionadaNome,
        };
        if (info != null)
        {
            // Proficiências desta classe
            if (info.saves     != null) data.saves    .AddRange(info.saves);
            if (info.skills    != null) data.skills   .AddRange(info.skills);
            if (info.armors    != null) data.armors   .AddRange(info.armors);
            if (info.weapons   != null) data.weapons  .AddRange(info.weapons);
            if (info.tools     != null) data.tools    .AddRange(info.tools);
            if (info.languages != null) data.languages.AddRange(info.languages);
            if (info.equipment != null) data.equipment.AddRange(info.equipment);
            if (info.asi_feats != null) data.asi_feats.AddRange(info.asi_feats);
            // Poderes e habilidades
            foreach (var p in info.class_powers        ?? new List<ClassPowerData>())       data.class_powers.Add(ToEntry(p));
            foreach (var p in info.subclass_powers     ?? new List<ClassPowerData>())       data.subclass_powers.Add(ToEntry(p));
            foreach (var c in info.class_power_choices ?? new List<ClassPowerChoiceData>()) data.class_power_choices.Add(ToChoiceEntry(c));
        }
        return data;
    }

    private void RestaurarGerenciadoresMulticlasse(List<string> nomesClasses)
    {
        Debug.Log($"<b><color=#FFA500>[ClassSelector - Multiclasse]</color></b> Restaurando gerenciadores a partir do save (Total classes salvas: {GlobalFichaData.multiclassesSelecionadas.Count})");
        var salvas = GlobalFichaData.multiclassesSelecionadas;
        int saldoRestante = playerLevel;
        var jaEscolhidas  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < salvas.Count; i++)
        {
            var mc    = salvas[i];
            int saldo = saldoRestante;
            string titulo = i == 0
                ? $"1. ESCOLHA SUA CLASSE PRINCIPAL ({saldo} nível{(saldo > 1 ? "is" : "")} disponível{(saldo > 1 ? "is" : "")})"
                : $"MULTICLASSE {i + 1} ({saldo} nível{(saldo > 1 ? "is" : "")} restante{(saldo > 1 ? "s" : "")})";

            var classesSlot = nomesClasses.Where(n => !jaEscolhidas.Contains(n)).ToList();

            var go = UIUtils.Spawn(gerenciadorMulticlassePrefab, mainScrollViewContent);
            go.SetActive(true);
            var gm = go.GetComponent<MulticlasseGerenciador>();
            _gerenciadoresMulticlasse.Add(gm);
            gm.RestaurarSemNotificar(mc.className, mc.level, classesSlot, saldo, titulo, OnGerenciadorAlterado);

            jaEscolhidas.Add(mc.className);
            saldoRestante -= mc.level;
        }

        // Se ainda sobrou nível, cria um gerenciador vazio para o jogador continuar
        if (saldoRestante > 0)
        {
            var restantes = nomesClasses.Where(n => !jaEscolhidas.Contains(n)).ToList();
            if (restantes.Count > 0)
                CriarGerenciadorMulticlasse(restantes, saldoRestante, _gerenciadoresMulticlasse.Count);
        }

        // Reconstrói a fila de passos (proficiências, subclasse, ASI) e auto-seleciona via
        // RestauracaoInfo  -  cada step confirma automaticamente e aparece já bloqueado na UI
        IniciarFluxoPosSelecaoMulticlasse();
    }

    private void RestaurarEstadoInternoDeRestauracaoInfo()
    {
        // Prioridade: dado per-classe (classes_json) > dado geral (class_info_json legado)
        // Resolve o bug de multiclasse onde class_info_json de Bardo sobrescrevia o de Clérigo.
        ClasseInfoData info = ResolverInfoRestauracao();
        if (info == null) return;

        Debug.Log($"<b><color=#DA70D6>[ClassSelector - Restauração]</color></b> Usando info para classe '{(classeSelecionada?.id ?? "?")}': SubclassID='{info.subclass_id}'");

        proficienciasEscolhidas     = new List<string>(info.skills    ?? new List<string>());
        armadurasClasse             = new List<string>(info.armors    ?? new List<string>());
        armasClasse                 = new List<string>(info.weapons   ?? new List<string>());
        ferramentasEscolhidas       = new List<string>(info.tools     ?? new List<string>());
        equipamentoEscolhido        = new List<string>(info.equipment ?? new List<string>());
        atributosTalentosEscolhidos = new List<string>(info.asi_feats ?? new List<string>());
        poderesClasseDesbloqueados  = new List<ClassPowerData>(info.class_powers        ?? new List<ClassPowerData>());
        escolhasPoderesClasse       = new List<ClassPowerChoiceData>(info.class_power_choices ?? new List<ClassPowerChoiceData>());
        subclasseSelecionadaNome    = info.subclass_name ?? "";
        GlobalFichaData.currentSubclassId = info.subclass_id ?? "";

        ProficiencySelector.PericiasClasseLocal = new List<string>(proficienciasEscolhidas);
        ProficiencySelector.SavesClasseLocal.Clear();
        if (info.saves != null) ProficiencySelector.SavesClasseLocal.AddRange(info.saves);
        ProficiencySelector.IdiomasClasseLocal = new List<string>(info.languages ?? new List<string>());
        ProficiencySelector.FerramentasClasseLocal = new List<string>(ferramentasEscolhidas);
        ProficiencySelector.OnImportacaoSolicitada?.Invoke();

        // Re-semeia os níveis com talento ainda NÃO escolhido (marcador "TALENTO PENDENTE")
        // para que a aba de Talentos volte a oferecer esses níveis. Níveis já resolvidos
        // ("TALENTO=<id>") não entram aqui — evita reaplicar o mesmo talento de novo.
        foreach (string entrada in atributosTalentosEscolhidos)
        {
            if (!entrada.Contains("TALENTO PENDENTE")) continue;
            if (!entrada.StartsWith("Nível ") || !entrada.Contains(":")) continue;

            string nivelStr = entrada.Substring(6, entrada.IndexOf(':') - 6).Trim();
            if (int.TryParse(nivelStr, out int nivelPendente)
                && !TalentosSelector.NiveisPendentesParaTalento.Contains(nivelPendente))
            {
                TalentosSelector.NiveisPendentesParaTalento.Add(nivelPendente);
            }
        }

        Debug.Log($"[ClassSelector] RestaurarEstadoInternoDeRestauracaoInfo: subclasse='{subclasseSelecionadaNome}' skills={proficienciasEscolhidas.Count} equipment={equipamentoEscolhido.Count}");
    }

    /// <summary>
    /// Resolve qual ClasseInfoData usar para esta instância do ClassSelector (variante sem
    /// argumento — mantida por compatibilidade, resolve o classId a partir do estado da instância).
    /// Prioridade: RestauracaoInfoPorClasse[class_id] â†' RestauracaoInfo (legado).
    /// </summary>
    private ClasseInfoData ResolverInfoRestauracao()
    {
        string classId = classeSelecionada != null
            ? (classeSelecionada.remoteId ?? classeSelecionada.id)
            : GlobalFichaData.classIdRestauracao;
        return ResolverInfoRestauracaoParaClasse(classId);
    }

    /// <summary>
    /// Resolve qual ClasseInfoData usar para uma classe ESPECÍFICA (por id) — essencial em
    /// multiclasse, onde RestauracaoInfo (estático, único) sempre aponta pra classe PRIMÁRIA.
    /// Sem isto, os passos de subclasse/ASI de uma classe SECUNDÁRIA liam os dados restaurados da
    /// classe primária durante a edição de uma ficha existente — confirmado em ficha real: um
    /// bárbaro secundário reaplicava o exato ASI (FOR+2) do bardo primário como se fosse dele
    /// (atributo final duplicado: 10+2+2=14), e o passo de subclasse da classe secundária
    /// simplesmente não auto-confirmava (guard antigo evitava usar o dado errado, mas isso travava
    /// a fila inteira esperando um clique manual do jogador — "os passos somem depois do primeiro
    /// ASI" era exatamente esse travamento).
    /// Prioridade: RestauracaoInfoPorClasse[classId] â†' RestauracaoInfo (fallback pra fichas
    /// antigas sem classes_json, ou pra classId vazio/não encontrado).
    /// </summary>
    private ClasseInfoData ResolverInfoRestauracaoParaClasse(string classId)
    {
        if (!string.IsNullOrEmpty(classId)
            && RestauracaoInfoPorClasse.TryGetValue(classId, out ClasseInfoData perClass)
            && perClass != null)
        {
            return perClass;
        }

        return RestauracaoInfo;
    }

    private void DestruirGerenciadoresAPartirDe(int idxInclusive)
    {
        for (int i = _gerenciadoresMulticlasse.Count - 1; i >= idxInclusive; i--)
        {
            if (_gerenciadoresMulticlasse[i] != null)
                Destroy(_gerenciadoresMulticlasse[i].gameObject);
            _gerenciadoresMulticlasse.RemoveAt(i);
        }
    }

    private void SincronizarMulticlasseComGlobal()
    {
        var lista = _gerenciadoresMulticlasse
            .Where(g => !string.IsNullOrEmpty(g.ClasseSelecionada) && g.NivelSelecionado > 0)
            .Select(g => new MulticlassData { className = g.ClasseSelecionada, level = g.NivelSelecionado })
            .ToList();

        GlobalFichaData.multiclassesSelecionadas = lista;
        if (lista.Count > 0)
            GlobalFichaData.classePersonagem = lista[0].className;

        int    playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId()      : 0;
        string steamId  = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        LocalFichaStorage.SaveMulticlasse(steamId, playerId,
            GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId,
            new MulticlassDataWrapper { classes = lista }.ToJson());
    }

    // â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

    private void CriarPasso_1_Classes()
    {
        // Modo multiclasse: usa gerenciadores em cascata em vez do step normal
        if (GlobalFichaData.modoMulticlasse && gerenciadorMulticlassePrefab != null)
        {
            _gerenciadoresMulticlasse.Clear();
            var nomesClasses = classesDisponiveis.Select(c => c.name).ToList();

            // Restauração: reconstrói os gerenciadores com os dados salvos sem re-executar a fila de passos
            if (GlobalFichaData.precisaRestaurarFluxo
                && GlobalFichaData.multiclassesSelecionadas != null
                && GlobalFichaData.multiclassesSelecionadas.Count > 0)
            {
                RestaurarGerenciadoresMulticlasse(nomesClasses);
            }
            else
            {
                CriarGerenciadorMulticlasse(nomesClasses, playerLevel, 0);
            }
            return;
        }

        GameObject go = InstanciateMainStepUI(out int indexoPasso); StepContainerUI stepUI = go.GetComponent<StepContainerUI>();
        
        // Armazena o índice real do container
        Transform containerAtual = stepUI.transform;
        
        Dictionary<string, string> opcoes = new Dictionary<string, string>();
        foreach (var c in classesDisponiveis) opcoes.Add(c.id, c.name);

        // Callback para deselecção: remove passos posteriores
        System.Action onDeselectClasse = () => {
            RemoverPassosPosteriores(containerAtual.GetSiblingIndex());
            classeSelecionada = null;
        };

        stepUI.SetupStep("1. ESCOLHA A SUA CLASSE", opcoes, 
            (idSelecionado) => {
                var c = classesDisponiveis.FirstOrDefault(x => x.id == idSelecionado);
                if (c != null)
                {
                    MostrarDetalhesClasse(c.xml);
                    MostrarReferenciaSrd(c.name);
                }
            },
            (idConfirmado) => {
                classeSelecionada = classesDisponiveis.FirstOrDefault(x => x.id == idConfirmado);
                if (classeSelecionada == null) {
                    Debug.LogError("[ClassSelector] Classe confirmada não encontrada para o ID: " + idConfirmado);
                    return;
                }
                Debug.Log($"[.] <color=#ADD8E6>[ClassSelector - SingleClass]</color> Classe confirmada: {classeSelecionada?.name}");

                MostrarReferenciaSrd(classeSelecionada.name);

                int currentClassId = 0;
                string idParaContexto = !string.IsNullOrEmpty(classeSelecionada.remoteId) ? classeSelecionada.remoteId : classeSelecionada.id;
                int.TryParse(idParaContexto, out currentClassId);
                GlobalFichaData.currentClassId = currentClassId;
                ExtrairDadoDeVidaESalvaguardas(classeSelecionada.xml);
                filaDePassos.Clear(); proficienciasEscolhidas.Clear(); equipamentoEscolhido.Clear(); ferramentasEscolhidas.Clear(); atributosTalentosEscolhidos.Clear();
                armasClasse.Clear(); armadurasClasse.Clear();

                try {
                    string cleanXml = classeSelecionada.xml.Trim().Replace("\uFEFF", "");
                    XDocument doc = XDocument.Parse(cleanXml);
                    LerProficienciasFixasClasse(doc.Root);
                } catch { }

                int qtdProf = LerQuantidadeProficiencias(classeSelecionada.xml);
                for (int p = 1; p <= qtdProf; p++) { int numeroAtual = p; filaDePassos.Enqueue(() => CriarPasso_Proficiencias(classeSelecionada.xml, numeroAtual, qtdProf)); }

                LerConfiguracaoIdiomas(classeSelecionada.xml);
                LerConfiguracaoFerramentas(classeSelecionada.xml); 
                LerConfiguracaoEquipamentos(classeSelecionada.xml);

                int subLvl = LerNivelSubclasse(classeSelecionada.xml);
                if (playerLevel >= subLvl && subLvl > 0) filaDePassos.Enqueue(() => CriarPasso_Subclasse(classeSelecionada.xml));

                // Extrai poderes por nível e cria passos de escolha para poderes que exigem seleção
                ExtrairPoderesEClassChoices(classeSelecionada.xml, playerLevel);

                List<int> niveisAsi = LerNiveisASI(classeSelecionada.xml, playerLevel);
                foreach (int nivel in niveisAsi) { int n = nivel; filaDePassos.Enqueue(() => CriarPasso_ASI_Talento(n)); }

                AvancarPasso(); 
            },
            true,
            onDeselectClasse
        );
        if (!string.IsNullOrEmpty(GlobalFichaData.classIdRestauracao))
            stepUI.AutoSelecionarOpcao(GlobalFichaData.classIdRestauracao);
    }

    private void LerProficienciasFixasClasse(XElement node)
    {
        if (node == null) return;
        
        ProcessarTagProficiencia(node, "armor-proficiencies", armadurasClasse);
        ProcessarTagProficiencia(node, "weapon-proficiencies", armasClasse);
        ProcessarTagProficiencia(node, "tool-proficiencies", ferramentasEscolhidas);
        ProcessarTagProficiencia(node, "skill-proficiencies", proficienciasEscolhidas);

        ProficiencySelector.PericiasClasseLocal = new List<string>(proficienciasEscolhidas);
        ProficiencySelector.FerramentasClasseLocal = new List<string>();
        ProficiencySelector.FerramentasClasseLocal.AddRange(armadurasClasse);
        ProficiencySelector.FerramentasClasseLocal.AddRange(armasClasse);
        ProficiencySelector.FerramentasClasseLocal.AddRange(ferramentasEscolhidas);

        ProficiencySelector.OnImportacaoSolicitada?.Invoke();
    }

    private void ProcessarTagProficiencia(XElement node, string tagName, List<string> listaAlvo)
    {
        var profNode = node.Descendants(tagName).FirstOrDefault();
        if (profNode != null) {
            foreach (var el in profNode.Elements()) {
                string val = el.Value.Trim();
                if (!string.IsNullOrEmpty(val) && val.ToLower() != "none" && !listaAlvo.Contains(val))
                    listaAlvo.Add(val);
            }
        }
    }

    private void ExtrairDadoDeVidaESalvaguardas(string xml) { 
        try {
            XDocument doc = XDocument.Parse(xml);
            string hitDieStr = doc.Descendants("hit-die").FirstOrDefault()?.Value ?? "d8"; 
            int hitDieValue = 0;
            if (hitDieStr.StartsWith("d") || hitDieStr.StartsWith("D")) int.TryParse(hitDieStr.Substring(1), out hitDieValue);
            else int.TryParse(hitDieStr, out hitDieValue);

            AttributeManager.HitDieMaximo = hitDieValue;
            AttributeManager am = FindObjectOfType<AttributeManager>(true);
            if (am != null) am.CalcularEAtualizarVida();

            var saveNodes = doc.Descendants("save").ToList();
            ProficiencySelector.SavesClasseLocal.Clear();
            foreach(var sn in saveNodes) ProficiencySelector.SavesClasseLocal.Add(sn.Value); 
            ProficiencySelector.OnImportacaoSolicitada?.Invoke(); 
        } catch { }
    }

    private void MostrarDetalhesClasse(string xml) {
        if (string.IsNullOrEmpty(xml)) return;
        try {
            XDocument doc = XDocument.Parse(xml); var root = doc.Element("class") ?? doc.Root;
            string nome = root.Element("name")?.Value ?? "Classe", summary = root.Element("summary")?.Value ?? "", hitDie = root.Descendants("hit-die").FirstOrDefault()?.Value ?? "";
            
            ExtrairDadoDeVidaESalvaguardas(xml); 
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"<size=160%><b>{nome.ToUpper()}</b></size>"); sb.AppendLine($"<i>{summary}</i>\n"); sb.AppendLine($"<b>Dado de Vida:</b> {hitDie}\n"); sb.AppendLine("<size=120%><b>CARACTERÍSTICAS POR NÍVEL:</b></size>");

            var progNode = root.Element("level-progression"); var todasFeatures = root.Element("features");
            if (progNode != null && todasFeatures != null) {
                foreach (var levelNode in progNode.Elements("level")) {
                    string lvlNum = levelNode.Attribute("number")?.Value; var featuresRefs = levelNode.Descendants("feature-ref").ToList();
                    if (featuresRefs.Count > 0) {
                        sb.AppendLine($"\n<color=#8C5A00><b>Nível {lvlNum}</b></color>");
                        foreach (var fRef in featuresRefs) {
                            string refId = fRef.Attribute("id")?.Value;
                            if (refId == "ability-improvement") { sb.AppendLine("• <b>Melhoria de Atributo:</b> Aumente atributos ou escolha um Talento."); continue; }
                            var featureOriginal = todasFeatures.Elements("feature").FirstOrDefault(x => x.Attribute("id")?.Value == refId);
                            if (featureOriginal != null) {
                                string fNome = featureOriginal.Element("name")?.Value ?? "Habilidade", fDesc = featureOriginal.Element("description")?.Value ?? "";
                                sb.AppendLine($"• <b>{fNome}:</b> <color=#333333>{fDesc}</color>");
                            }
                        }
                    }
                }
            }
            if (infoText) infoText.text = sb.ToString();
        } catch { if (infoText) infoText.text = "Erro ao ler XML para detalhes."; }
    }

    private void CriarPasso_Proficiencias(string xmlClasse, int atual, int total) {
        XDocument doc = XDocument.Parse(xmlClasse); var skillChoices = doc.Descendants("skill-choices").FirstOrDefault();
        if (skillChoices == null) { AvancarPasso(); return; }

        Dictionary<string, string> opcoes = new Dictionary<string, string>();
        foreach (var opt in skillChoices.Elements("option")) if (!proficienciasEscolhidas.Contains(opt.Value)) opcoes.Add(opt.Value, opt.Value);

        // Todas as opções já foram escolhidas anteriormente (ex: reconstrução da fila) — sem isto
        // o passo instanciaria com 0 opções e travaria pra sempre esperando um clique que nunca vai
        // existir (StepContainerUI.SetupStep some sem UI quando options está vazio).
        if (opcoes.Count == 0) { AvancarPasso(); return; }

        GameObject go = Instantiate(stepContainerPrefab, mainScrollViewContent, false); go.SetActive(true); StepContainerUI stepUI = go.GetComponent<StepContainerUI>();

        stepUI.SetupStep($"2. PROFICIÊNCIA ({atual}/{total})", opcoes,
            (id) => {
                if (!infoText) return;
                string descricao = ProficiencySelector.ObterDescricaoPericia(id) ?? "Você será proficiente neste tipo de teste.";
                infoText.text = $"<size=150%><b>{id}</b></size>\n\n{descricao}";
            },
            (idConfirmado) => {
                proficienciasEscolhidas.Add(idConfirmado);
                ProficiencySelector.PericiasClasseLocal = new List<string>(proficienciasEscolhidas);
                ProficiencySelector.OnImportacaoSolicitada?.Invoke();
                AvancarPasso();
            },
            true
        );
        if (RestauracaoInfo?.skills != null)
            foreach (var skill in RestauracaoInfo.skills)
                if (opcoes.ContainsKey(skill)) { stepUI.AutoSelecionarOpcao(skill); break; }
    }

    private void LerConfiguracaoIdiomas(string xmlClasse) {
        XDocument doc = XDocument.Parse(xmlClasse); var langsEl = doc.Descendants("languages").FirstOrDefault();
        if (langsEl == null) return;
        var langOptions = langsEl.Elements("option").ToList();
        if (langOptions.Count == 0) langOptions = langsEl.Elements("language").ToList();
        if (langOptions.Count > 0) {
            List<string> idiomas = new List<string>();
            foreach (var opt in langOptions) { idiomas.Add(opt.Value); }
            ProficiencySelector.IdiomasClasseLocal = idiomas;
            ProficiencySelector.OnImportacaoSolicitada?.Invoke();
        }
    }

    private void LerConfiguracaoFerramentas(string xmlClasse) {
        XDocument doc = XDocument.Parse(xmlClasse); var tools = doc.Descendants("tool-proficiencies").FirstOrDefault();
        if (tools == null) return;
        var fixedNode = tools.Element("fixed");
        if (fixedNode != null) {
            foreach (var item in fixedNode.Elements("item")) {
                string idAttr = item.Attribute("id")?.Value;
                if (!string.IsNullOrEmpty(idAttr)) ferramentasEscolhidas.Add(idAttr); else ferramentasEscolhidas.Add($"CUSTOM:{item.Value}");
            }
            ProficiencySelector.FerramentasClasseLocal = new List<string>(ferramentasEscolhidas); ProficiencySelector.OnImportacaoSolicitada?.Invoke(); 
        }
        var choices = tools.Elements("choice").ToList();
        foreach (var choice in choices) { XElement c = choice; filaDePassos.Enqueue(() => CriarPasso_EscolhaFerramenta(c)); }
    }

    private void CriarPasso_EscolhaFerramenta(XElement choiceNode) {
        string pergunta = choiceNode.Attribute("text")?.Value ?? "Escolha uma Proficiência";
        GameObject go = Instantiate(stepContainerPrefab, mainScrollViewContent, false); go.SetActive(true); StepContainerUI stepUI = go.GetComponent<StepContainerUI>();
        Dictionary<string, string> opcoes = new Dictionary<string, string>();
        foreach (var opt in choiceNode.Elements("option")) { string dbName = opt.Attribute("id")?.Value ?? opt.Value; opcoes.Add(dbName, opt.Value); }

        EquipmentSelector equipSelFerramenta = EquipmentSelector.ResolveInstanciaPrincipal();

        stepUI.SetupStep("PROFICIÊNCIA EXTRA: " + pergunta.ToUpper(), opcoes,
            (id) => {
                if (!infoText) return;
                string nomeOpcao = opcoes.TryGetValue(id, out string nomeVal) ? nomeVal : id;
                string descricao = equipSelFerramenta?.ObterDescricaoParaComando(id)
                    ?? ProficiencySelector.GerarDescricaoContextual(nomeOpcao);
                infoText.text = descricao;
            },
            (idConfirmado) => {
                string[] itensDb = idConfirmado.Split(','); foreach(string i in itensDb) ferramentasEscolhidas.Add(i.Trim());
                ProficiencySelector.FerramentasClasseLocal = new List<string>(ferramentasEscolhidas); ProficiencySelector.OnImportacaoSolicitada?.Invoke(); 
                AvancarPasso(); 
            },
            true
        );
    }

    private void LerConfiguracaoEquipamentos(string xmlClasse) {
        XDocument doc = XDocument.Parse(xmlClasse); var equips = doc.Descendants("equipment").FirstOrDefault();
        if (equips == null) return;
        var fixedNode = equips.Element("fixed");
        if (fixedNode != null) {
            foreach (var item in fixedNode.Elements("item")) {
                string idAttr = item.Attribute("id")?.Value;
                if (idAttr == "Ouro" || idAttr == "GP") equipamentoEscolhido.Add($"OURO:{item.Value}");
                else if (!string.IsNullOrEmpty(idAttr)) equipamentoEscolhido.Add($"{idAttr}|{item.Value}"); // Manda o ID e o Nome
                else equipamentoEscolhido.Add(item.Value);
            }
        }
        var choices = equips.Elements("choice").ToList();
        for (int i = 0; i < choices.Count; i++) {
            XElement c = choices[i]; int idx = i + 1; int tot = choices.Count;
            filaDePassos.Enqueue(() => CriarPasso_EscolhaEquipamento(c, idx, tot));
        }
    }

    private void CriarPasso_EscolhaEquipamento(XElement choiceNode, int atual = 1, int total = 1) {
        string titulo = total > 1 ? $"EQUIPAMENTO ({atual}/{total})" : "EQUIPAMENTO";
        GameObject go = Instantiate(stepContainerPrefab, mainScrollViewContent, false); go.SetActive(true); StepContainerUI stepUI = go.GetComponent<StepContainerUI>();
        Dictionary<string, string> opcoes = new Dictionary<string, string>();

        foreach (var opt in choiceNode.Elements("option")) {
            string dbName = opt.Attribute("id")?.Value;
            if (!string.IsNullOrEmpty(dbName)) {
                opcoes.Add($"{dbName}|{opt.Value}", opt.Value);
            } else {
                opcoes.Add(opt.Value, opt.Value);
            }
        }

        EquipmentSelector equipSelEquipamento = EquipmentSelector.ResolveInstanciaPrincipal();

        stepUI.SetupStep(titulo, opcoes,
            (id) => {
                if (!infoText) return;
                string descricao = equipSelEquipamento?.ObterDescricaoParaComando(id);
                infoText.text = !string.IsNullOrEmpty(descricao)
                    ? descricao
                    : "Escolha com sabedoria sua ferramenta de aventura.";
            },
            (idConfirmado) => {
                equipamentoEscolhido.Add(idConfirmado);
                AvancarPasso();
            },
            true
        );
        if (RestauracaoInfo?.equipment != null)
            foreach (var equip in RestauracaoInfo.equipment)
                if (opcoes.ContainsKey(equip)) { stepUI.AutoSelecionarOpcao(equip); break; }
    }

    private void CriarPasso_Subclasse(string xmlClasse, bool isSecondaryClass = false, string classIdRestauracao = null) {
        if (string.IsNullOrEmpty(xmlClasse)) {
            Debug.LogWarning("<b><color=#DA70D6>[ClassSelector - Subclasse]</color></b> XML da classe é nulo/vazio -- passo de subclasse ignorado.");
            AvancarPasso(); return;
        }
        XDocument doc;
        try { doc = XDocument.Parse(xmlClasse.Trim().Replace("ï»¿", "")); }
        catch (Exception ex) {
            Debug.LogWarning($"<b><color=#DA70D6>[ClassSelector - Subclasse]</color></b> Falha ao parsear XML: {ex.Message}");
            AvancarPasso(); return;
        }
        var subEl = doc.Descendants("subclasses").FirstOrDefault();
        if (subEl == null) { Debug.LogWarning($"<b><color=#DA70D6>[ClassSelector - Subclasse]</color></b> Elemento <subclasses> não encontrado no XML."); AvancarPasso(); return; }
        Dictionary<string, string> opcoes = new Dictionary<string, string>(); Dictionary<string, string> descricoesSub = new Dictionary<string, string>();
        foreach (var s in subEl.Elements("subclass")) {
            string id = s.Attribute("id")?.Value, nome = s.Element("name")?.Value ?? "Arquétipo", desc = s.Element("summary")?.Value ?? "Sem descrição.";
            opcoes.Add(id, nome); descricoesSub.Add(id, $"<size=150%><b>{nome}</b></size>\n\n<i>{desc}</i>");
        }
        // <subclasses> existe mas não tem nenhum <subclass> filho — sem isto o passo instanciaria
        // com 0 opções e travaria pra sempre (mesmo raciocínio de CriarPasso_Proficiencias acima).
        if (opcoes.Count == 0) {
            Debug.LogWarning($"<b><color=#DA70D6>[ClassSelector - Subclasse]</color></b> <subclasses> não tem nenhum <subclass> filho -- passo ignorado.");
            AvancarPasso(); return;
        }
        GameObject go = Instantiate(stepContainerPrefab, mainScrollViewContent, false); go.SetActive(true); StepContainerUI stepUI = go.GetComponent<StepContainerUI>();
        stepUI.SetupStep("4. ESCOLHA SEU ARQUÉTIPO", opcoes,
            (id) => {
                if (infoText) infoText.text = descricoesSub.ContainsKey(id) ? descricoesSub[id] : "";
                string nomeSub = opcoes.ContainsKey(id) ? opcoes[id] : id;
                MostrarReferenciaSrd(nomeSub);
            },
            (idConfirmado) => {
                GlobalFichaData.currentSubclassId = idConfirmado;
                var sub = subEl.Elements("subclass").FirstOrDefault(x => x.Attribute("id")?.Value == idConfirmado);
                subclasseSelecionadaNome = sub?.Element("name")?.Value ?? "";
                Debug.Log($"<b><color=#DA70D6>[ClassSelector - Subclasse]</color></b> Seleção confirmada pelo usuário: ID='{idConfirmado}' | Nome='{subclasseSelecionadaNome}'");
                MostrarReferenciaSrd(subclasseSelecionadaNome);
                AvancarPasso();
            },
            true
        );
        // Classes secundárias NÃO podem auto-selecionar via GlobalFichaData.subclassIdRestauracao
        // (esse campo aponta pra subclasse da classe PRIMÁRIA) — resolve via
        // RestauracaoInfoPorClasse[classIdRestauracao], que tem o dado certo por classe.
        string subclassIdParaRestaurar = isSecondaryClass
            ? ResolverInfoRestauracaoParaClasse(classIdRestauracao)?.subclass_id
            : GlobalFichaData.subclassIdRestauracao;

        if (!string.IsNullOrEmpty(subclassIdParaRestaurar))
        {
            Debug.Log($"<b><color=#DA70D6>[ClassSelector - Subclasse]</color></b> Tentando auto-selecionar subclasse baseada em restauração: '{subclassIdParaRestaurar}' (secundária={isSecondaryClass})");
            stepUI.AutoSelecionarOpcao(subclassIdParaRestaurar);
        }
    }

    private void MostrarReferenciaSrd(string termo)
    {
        if (srdBookNavigator == null || string.IsNullOrWhiteSpace(termo)) return;
        srdBookNavigator.MostrarPorTermo(termo);
    }

    private void ExtrairPoderesEClassChoices(string xmlClasse, int nivelAtual)
    {
        poderesClasseDesbloqueados.Clear();

        XDocument doc;
        try { doc = XDocument.Parse(xmlClasse); }
        catch { return; }

        XElement root = doc.Element("class") ?? doc.Root;
        if (root == null) return;

        XElement featuresNode = root.Element("features");
        XElement progression = root.Element("level-progression");
        if (featuresNode == null || progression == null) return;

        var mapaFeatures = featuresNode.Elements("feature")
            .Where(f => f.Attribute("id") != null)
            .GroupBy(f => f.Attribute("id")?.Value)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var lvlNode in progression.Elements("level"))
        {
            if (!int.TryParse(lvlNode.Attribute("number")?.Value, out int lvl)) continue;
            if (lvl > nivelAtual) continue;

            foreach (var fRef in lvlNode.Descendants("feature-ref"))
            {
                string refId = fRef.Attribute("id")?.Value;
                if (string.IsNullOrEmpty(refId)) continue;

                if (mapaFeatures.TryGetValue(refId, out var featureEl))
                {
                    string nome = featureEl.Element("name")?.Value ?? refId;
                    string desc = featureEl.Element("description")?.Value ?? featureEl.Element("text")?.Value ?? "";

                    poderesClasseDesbloqueados.Add(new ClassPowerData
                    {
                        id = refId,
                        name = nome,
                        level = lvl,
                        description = desc
                    });

                    int? picksOverride = int.TryParse(fRef.Attribute("picks")?.Value, out int picksLido) ? picksLido : (int?)null;
                    EnfileirarEscolhasDeFeatureSeHouver(featureEl, lvl, null, picksOverride);
                }
                else if (refId == "ability-improvement")
                {
                    poderesClasseDesbloqueados.Add(new ClassPowerData
                    {
                        id = refId,
                        name = "Melhoria de Atributo",
                        level = lvl,
                        description = "Aumente atributos ou escolha um talento."
                    });
                }
            }
        }
    }

    private void EnfileirarEscolhasDeFeatureSeHouver(XElement featureEl, int level, string classIdRestauracao = null, int? picksOverride = null)
    {
        if (featureEl == null) return;

        XElement choicesNode = featureEl.Element("choices") ?? featureEl.Element("choice");
        if (choicesNode == null) return;

        List<OpcaoEscolhaClasse> opcoes = choicesNode.Elements("option")
            .Select(o =>
            {
                string textoBruto = o.Value?.Trim();
                if (string.IsNullOrEmpty(textoBruto)) return null;

                // "Nome (descrição)" -> separa o texto exibido (sem parênteses) da descrição, que só
                // aparece no infoText quando a opção é selecionada (ver CriarPasso_EscolhaPoderClasse).
                string texto = textoBruto;
                string descricao = "";
                int idxAbre = textoBruto.IndexOf('(');
                if (idxAbre > 0 && textoBruto.EndsWith(")"))
                {
                    texto     = textoBruto.Substring(0, idxAbre).Trim();
                    descricao = textoBruto.Substring(idxAbre + 1, textoBruto.Length - idxAbre - 2).Trim();
                }

                int? prereqNivel = null;
                if (int.TryParse(o.Attribute("prereq-level")?.Value, out int pn)) prereqNivel = pn;
                return new OpcaoEscolhaClasse { Texto = texto, Descricao = descricao, PrereqNivel = prereqNivel, PrereqPacto = o.Attribute("prereq-pact")?.Value };
            })
            .Where(o => o != null)
            .GroupBy(o => o.Texto)
            .Select(g => g.First())
            .ToList();

        if (opcoes.Count == 0) return;

        // "picks" no <feature-ref> (por ocorrência de nível) tem prioridade sobre o "max" default do
        // <choices> (compartilhado por todas as ocorrências dessa feature) — é o que permite uma
        // mesma feature (ex.: eldritch-invocations) conceder quantidades diferentes de escolhas em
        // níveis diferentes (2 no nível 2, +1 no nível 5, +1 no 7...) sem precisar de um <feature id>
        // separado por nível.
        int qtdEscolhas;
        if (picksOverride.HasValue && picksOverride.Value > 0)
        {
            qtdEscolhas = picksOverride.Value;
        }
        else
        {
            qtdEscolhas = 1;
            int.TryParse(choicesNode.Attribute("max")?.Value, out qtdEscolhas);
            if (qtdEscolhas <= 0)
            {
                int minEscolhas = 1;
                int.TryParse(choicesNode.Attribute("min")?.Value, out minEscolhas);
                qtdEscolhas = Mathf.Max(1, minEscolhas);
            }
        }

        bool unicaEmTodosOsNiveis = string.Equals(choicesNode.Attribute("unique-across-levels")?.Value, "true", StringComparison.OrdinalIgnoreCase);

        string featureId = featureEl.Attribute("id")?.Value ?? "feature-choice";
        string featureName = featureEl.Element("name")?.Value ?? "Escolha de Classe";
        string featureDesc = featureEl.Element("description")?.Value ?? "Selecione uma opção.";

        for (int i = 1; i <= qtdEscolhas; i++)
        {
            int idx = i;
            int total = qtdEscolhas;
            string classIdCaptura = classIdRestauracao;
            filaDePassos.Enqueue(() => CriarPasso_EscolhaPoderClasse(featureId, featureName, featureDesc, opcoes, level, idx, total, unicaEmTodosOsNiveis, classIdCaptura));
        }
    }

    /// <summary>
    /// Lê o XML da classe e enfileira passos de escolha (choices) para cada feature desbloqueada
    /// até nivelAtual. Usado exclusivamente por classes secundárias no fluxo multiclasse.
    /// NUNCA modifica o XML -- leitura apenas.
    /// </summary>
    private void EnfileirarEscolhasParaClasseSecundaria(string xml, int nivelAtual, string classIdRestauracao = null)
    {
        if (string.IsNullOrEmpty(xml)) return;
        XDocument doc;
        try { doc = XDocument.Parse(xml.Trim().Replace("ï»¿", "")); }
        catch { return; }

        XElement root = doc.Element("class") ?? doc.Root;
        if (root == null) return;

        XElement featuresNode = root.Element("features");
        XElement progression  = root.Element("level-progression");
        if (featuresNode == null || progression == null) return;

        var mapaFeatures = featuresNode.Elements("feature")
            .Where(f => f.Attribute("id") != null)
            .GroupBy(f => f.Attribute("id")?.Value)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var lvlNode in progression.Elements("level"))
        {
            if (!int.TryParse(lvlNode.Attribute("number")?.Value, out int lvl) || lvl > nivelAtual) continue;
            foreach (var fRef in lvlNode.Descendants("feature-ref"))
            {
                string refId = fRef.Attribute("id")?.Value;
                if (string.IsNullOrEmpty(refId) || !mapaFeatures.TryGetValue(refId, out var featureEl)) continue;
                int? picksOverride = int.TryParse(fRef.Attribute("picks")?.Value, out int picksLido) ? picksLido : (int?)null;
                EnfileirarEscolhasDeFeatureSeHouver(featureEl, lvl, classIdRestauracao, picksOverride);
            }
        }
    }

    private void CriarPasso_EscolhaPoderClasse(string featureId, string featureName, string featureDesc, List<OpcaoEscolhaClasse> opcoesBrutas, int level, int escolhaAtual, int escolhaTotal, bool unicaEmTodosOsNiveis = false, string classIdRestauracao = null)
    {
        // Por padrão só evita repetir a MESMA opção dentro do MESMO nível (ex.: dois pontos de ASI no
        // nível 4 podem repetir o mesmo atributo — regra válida de 5e). unicaEmTodosOsNiveis liga o
        // modo "nunca mais oferecer de novo, em nenhum nível" — necessário pra escolhas cumulativas
        // como Invocações Místicas, que ganham novos slots em vários níveis mas nunca podem repetir a
        // mesma invocação já conhecida de um nível anterior.
        var jaEscolhidas = escolhasPoderesClasse
            .Where(x => x.feature_id == featureId && (unicaEmTodosOsNiveis || x.level == level))
            .Select(x => x.selected_option)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Pré-requisitos (ex.: invocação que exige nível mínimo e/ou um Pacto específico já escolhido
        // em pact-boon). Sem pré-requisito no atributo = sempre elegível, igual ao comportamento
        // anterior a esta feature.
        string pactoEscolhido = escolhasPoderesClasse.FirstOrDefault(c => c.feature_id == "pact-boon")?.selected_option ?? "";

        Dictionary<string, string> opcoes = new Dictionary<string, string>();
        Dictionary<string, string> descricaoPorOpcao = new Dictionary<string, string>();
        foreach (var opt in opcoesBrutas)
        {
            if (jaEscolhidas.Contains(opt.Texto)) continue;
            if (opt.PrereqNivel.HasValue && level < opt.PrereqNivel.Value) continue;
            if (!string.IsNullOrEmpty(opt.PrereqPacto) &&
                pactoEscolhido.IndexOf(opt.PrereqPacto, StringComparison.OrdinalIgnoreCase) < 0) continue;
            opcoes[opt.Texto] = opt.Texto;
            descricaoPorOpcao[opt.Texto] = opt.Descricao;
        }

        if (opcoes.Count == 0)
        {
            AvancarPasso();
            return;
        }

        GameObject go = Instantiate(stepContainerPrefab, mainScrollViewContent, false);
        go.SetActive(true);
        StepContainerUI stepUI = go.GetComponent<StepContainerUI>();

        stepUI.SetupStep($"NÍVEL {level} - {featureName.ToUpper()} ({escolhaAtual}/{escolhaTotal})", opcoes,
            (id) => {
                if (!infoText) return;
                // Mantém a explicação geral da feature (ex.: o que são Invocações Místicas) e
                // acrescenta embaixo a descrição específica da opção selecionada, se houver.
                string descOpcao = descricaoPorOpcao.TryGetValue(id ?? "", out string d) ? d : "";
                string blocoOpcao = string.IsNullOrWhiteSpace(descOpcao) ? "" : $"\n\n<b>{id}:</b> {descOpcao}";
                infoText.text = $"<size=145%><b>{featureName}</b></size>\n\n{featureDesc}{blocoOpcao}";
            },
            (idConfirmado) => {
                escolhasPoderesClasse.Add(new ClassPowerChoiceData
                {
                    feature_id = featureId,
                    feature_name = featureName,
                    level = level,
                    selected_option = idConfirmado
                });
                AvancarPasso();
            },
            true
        );

        ClasseInfoData infoRestauracaoPoder = ResolverInfoRestauracaoParaClasse(classIdRestauracao);
        if (infoRestauracaoPoder?.class_power_choices != null)
        {
            var match = infoRestauracaoPoder.class_power_choices.FirstOrDefault(c =>
                string.Equals(c.feature_id, featureId, StringComparison.OrdinalIgnoreCase) &&
                c.level == level && !jaEscolhidas.Contains(c.selected_option));
            if (match != null && opcoes.ContainsKey(match.selected_option))
                stepUI.AutoSelecionarOpcao(match.selected_option);
        }
    }

    private List<ClassPowerData> ExtrairPoderesSubclasseDesbloqueados(string xmlClasse, string subclassId, int nivelAtual)
    {
        List<ClassPowerData> poderes = new List<ClassPowerData>();
        if (string.IsNullOrEmpty(subclassId)) return poderes;

        try
        {
            XDocument doc = XDocument.Parse(xmlClasse);
            XElement root = doc.Element("class") ?? doc.Root;
            var subEl = root?.Descendants("subclass").FirstOrDefault(s => s.Attribute("id")?.Value == subclassId);
            if (subEl == null) return poderes;

            foreach (var f in subEl.Descendants("feature"))
            {
                int lvl = 0;
                int.TryParse(f.Element("level")?.Value ?? f.Attribute("level")?.Value, out lvl);
                if (lvl > 0 && lvl > nivelAtual) continue;

                poderes.Add(new ClassPowerData
                {
                    id = f.Attribute("id")?.Value,
                    name = f.Element("name")?.Value ?? "Habilidade de Subclasse",
                    level = lvl > 0 ? lvl : 1,
                    description = f.Element("description")?.Value ?? f.Element("text")?.Value ?? ""
                });
            }
        }
        catch { }

        return poderes;
    }

    private int LerQuantidadeProficiencias(string xml) { try { XDocument doc = XDocument.Parse(xml); var skillChoices = doc.Descendants("skill-choices").FirstOrDefault(); if (skillChoices != null && int.TryParse(skillChoices.Attribute("count")?.Value, out int c)) return c; } catch {} return 0; }
    private int LerNivelSubclasse(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return 3;
        try
        {
            XDocument doc = XDocument.Parse(xml.Trim().Replace("ï»¿", ""));
            XElement root = doc.Element("class") ?? doc.Root;

            // Estratégia 1: <subclasses level="N"> ou <subclasses level-gain="N">
            var subclassesEl = root?.Element("subclasses") ?? root?.Descendants("subclasses").FirstOrDefault();
            if (subclassesEl != null)
            {
                if (int.TryParse(subclassesEl.Attribute("level")?.Value,      out int l1) && l1 > 0) return l1;
                if (int.TryParse(subclassesEl.Attribute("level-gain")?.Value,  out int l2) && l2 > 0) return l2;
                if (int.TryParse(subclassesEl.Attribute("level_available")?.Value, out int l3) && l3 > 0) return l3;
            }

            // Estratégia 2: level-progression -- primeiro nível que referencia feature com "subclass"/"archetype"/"domain"/"college"
            var progression = root?.Element("level-progression");
            if (progression != null)
            {
                foreach (var lvlNode in progression.Elements("level"))
                {
                    if (!int.TryParse(lvlNode.Attribute("number")?.Value, out int lvl)) continue;
                    foreach (var fRef in lvlNode.Descendants("feature-ref"))
                    {
                        string refId = fRef.Attribute("id")?.Value ?? "";
                        if (refId.IndexOf("subclass",  StringComparison.OrdinalIgnoreCase) >= 0 ||
                            refId.IndexOf("archetype", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            refId.IndexOf("domain",    StringComparison.OrdinalIgnoreCase) >= 0 ||
                            refId.IndexOf("college",   StringComparison.OrdinalIgnoreCase) >= 0)
                            return lvl;
                    }
                }
            }

            // Estratégia 3: fallback legado -- <subclass><level-gain>
            var subNode = doc.Descendants("subclass").FirstOrDefault();
            if (subNode != null)
            {
                var lvlNode = subNode.Element("level-gain") ?? subNode.Element("level_available");
                if (lvlNode != null && int.TryParse(lvlNode.Value, out int l4)) return l4;
            }
        }
        catch { }
        return 3;
    }
    private List<int> LerNiveisASI(string xml, int lvlPlayerAtual) { List<int> niveis = new List<int>(); try { XDocument doc = XDocument.Parse(xml); var progNode = doc.Descendants("level-progression").FirstOrDefault(); if (progNode != null) { foreach (var levelNode in progNode.Elements("level")) { if (int.TryParse(levelNode.Attribute("number")?.Value, out int numLvl)) { if (numLvl <= lvlPlayerAtual) { var hasAsi = levelNode.Descendants("feature-ref").Any(f => f.Attribute("id")?.Value == "ability-improvement"); if (hasAsi) niveis.Add(numLvl); } } } } } catch {} return niveis; }

    private void CriarPasso_ASI_Talento(int nivelDoAsi, string classIdRestauracao = null)
    {
        GameObject go = Instantiate(stepContainerPrefab, mainScrollViewContent, false); go.SetActive(true); StepContainerUI stepUI = go.GetComponent<StepContainerUI>();
        Dictionary<string, string> opcoes = new Dictionary<string, string> { { "asi", "Aprimoramento de Atributo (+2 ou +1/+1)" }, { "feat", "Escolher um Talento" } };

        stepUI.SetupStep($"NÍVEL {nivelDoAsi} - ATRIBUTO OU TALENTO?", opcoes,
            (id) => {
                if (id == "asi") infoText.text = "<b><color=#1B5E20>Você fará agora 2 escolhas de atributo (+1 e +1).</color></b>";
                else infoText.text = "<b><color=#8C5A00>Um novo Talento será liberado na aba de Talentos!</color></b>";
            },
            (idConfirmado) => {
                if (idConfirmado == "asi") {
                    CriarPasso_EscolhaAtributoASI(nivelDoAsi, 1, 2, new Dictionary<string, int>(), classIdRestauracao);
                } else {
                    // Evita duplicar a entrada quando este passo é reconstruído (ex: restauração,
                    // ou o "flash" de painéis durante navegação) e o talento deste nível já foi
                    // registrado como pendente ou já resolvido anteriormente.
                    bool jaRegistrado = atributosTalentosEscolhidos.Any(f => f.StartsWith($"Nível {nivelDoAsi}:") && f.Contains("TALENTO"))
                        || TalentosSelector.NiveisPendentesParaTalento.Contains(nivelDoAsi);
                    if (!jaRegistrado)
                    {
                        atributosTalentosEscolhidos.Add($"Nível {nivelDoAsi}: TALENTO PENDENTE");
                        TalentosSelector.NiveisPendentesParaTalento.Add(nivelDoAsi);
                        // Avisa TalentosSelector AGORA — se a tela dele for (ou vier a ser) um
                        // GameObject sempre ativo, como este ClassSelector, o OnEnable dele já rodou
                        // uma vez lá atrás com a lista vazia e nunca mais dispara sozinho. Sem este
                        // aviso, a aba de Talentos ficava permanentemente vazia mesmo com pendência
                        // real registrada aqui.
                        TalentosSelector.OnNiveisPendentesMudou?.Invoke();
                    }
                    AvancarPasso();
                }
            },
            true
        );
        // Restaura escolha ASI vs Talento a partir do histórico salvo (por classe — evita ler o
        // histórico da classe primária ao restaurar uma classe secundária em multiclasse)
        ClasseInfoData infoRestauracaoAsi = ResolverInfoRestauracaoParaClasse(classIdRestauracao);
        if (infoRestauracaoAsi?.asi_feats != null)
        {
            string entrada = infoRestauracaoAsi.asi_feats.FirstOrDefault(
                f => f.StartsWith($"Nível {nivelDoAsi}:"));
            if (!string.IsNullOrEmpty(entrada))
            {
                if (entrada.Contains("TALENTO"))  stepUI.AutoSelecionarOpcao("feat");
                else if (entrada.Contains("AUMENTO")) stepUI.AutoSelecionarOpcao("asi");
            }
        }
    }

    /// <summary>
    /// Chamado pelo TalentosSelector assim que o jogador confirma qual talento escolheu
    /// para um nível pendente. Substitui o marcador "TALENTO PENDENTE" pelo talento real
    /// e persiste imediatamente, para que a escolha não se perca e não seja reprocessada
    /// (reaplicada) na próxima vez que a aba de Talentos/Classe for revisitada.
    /// </summary>
    public void RegistrarEscolhaTalento(int nivel, string idTalento, string nomeTalento)
    {
        string marcador = $"Nível {nivel}: TALENTO={idTalento} ({nomeTalento})";
        int idx = atributosTalentosEscolhidos.FindIndex(f => f.StartsWith($"Nível {nivel}:") && f.Contains("TALENTO"));
        if (idx >= 0) atributosTalentosEscolhidos[idx] = marcador;
        else atributosTalentosEscolhidos.Add(marcador);

        SalvarDadosClasseSincronamente();
    }

    private void CriarPasso_EscolhaAtributoASI(int nivelDoAsi, int escolhaAtual, int escolhasTotais, Dictionary<string, int> deltaParcial, string classIdRestauracao = null)
    {
        GameObject go = Instantiate(stepContainerPrefab, mainScrollViewContent, false); go.SetActive(true); StepContainerUI stepUI = go.GetComponent<StepContainerUI>();

        Dictionary<string, string> opcoes = new Dictionary<string, string>();
        foreach (var attr in atributosOrdemASI)
            opcoes[attr] = atributosLabelASI[attr];

        stepUI.SetupStep($"NÍVEL {nivelDoAsi} - AUMENTO DE ATRIBUTO ({escolhaAtual}/{escolhasTotais})", opcoes,
            (id) => {
                if (infoText && atributosLabelASI.ContainsKey(id))
                    infoText.text = $"<b>{atributosLabelASI[id]}</b> receberá +1 neste passo.";
            },
            (idConfirmado) => {
                if (!deltaParcial.ContainsKey(idConfirmado)) deltaParcial[idConfirmado] = 0;
                deltaParcial[idConfirmado] += 1;

                if (escolhaAtual < escolhasTotais)
                {
                    CriarPasso_EscolhaAtributoASI(nivelDoAsi, escolhaAtual + 1, escolhasTotais, new Dictionary<string, int>(deltaParcial), classIdRestauracao);
                    return;
                }

                foreach (var kvp in deltaParcial)
                    if (GlobalFichaData.bonusClasseDict.ContainsKey(kvp.Key))
                        GlobalFichaData.bonusClasseDict[kvp.Key] += kvp.Value;

                Debug.Log($"[ClassSelector] [OK] Bônus de classe salvos: {string.Join(", ", deltaParcial.Select(x => $"{x.Key}+{x.Value}"))}");
                AttributeManager.AplicarDeltaASI(deltaParcial);
                atributosTalentosEscolhidos.Add(FormatarRegistroASI(nivelDoAsi, deltaParcial));
                if (infoText) infoText.text = "<color=#1B5E20>Aumento de atributo aplicado com sucesso.</color>";
                AvancarPasso();
            },
            true
        );
        // Restaura atributo escolhido neste passo (escolhaAtual-1 no array "FOR+1, DEX+1") — por
        // classe, mesmo motivo do passo ASI/Talento acima
        ClasseInfoData infoRestauracaoAtributo = ResolverInfoRestauracaoParaClasse(classIdRestauracao);
        if (infoRestauracaoAtributo?.asi_feats != null)
        {
            string entrada = infoRestauracaoAtributo.asi_feats.FirstOrDefault(
                f => f.StartsWith($"Nível {nivelDoAsi}:") && f.Contains("AUMENTO"));
            if (!string.IsNullOrEmpty(entrada))
            {
                int ps = entrada.IndexOf('('), pe = entrada.IndexOf(')');
                if (ps >= 0 && pe > ps)
                {
                    string[] partes = entrada.Substring(ps + 1, pe - ps - 1)
                        .Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                    int idx = escolhaAtual - 1;
                    if (idx < partes.Length)
                    {
                        string atribId = partes[idx].Split('+')[0].Trim();
                        if (opcoes.ContainsKey(atribId)) stepUI.AutoSelecionarOpcao(atribId);
                    }
                }
            }
        }
    }

    private string FormatarRegistroASI(int nivel, Dictionary<string, int> delta)
    {
        List<string> partes = new List<string>();
        foreach (var attr in atributosOrdemASI)
        {
            if (!delta.TryGetValue(attr, out int valor) || valor <= 0) continue;
            partes.Add($"{attr}+{valor}");
        }

        string resumo = partes.Count > 0 ? string.Join(", ", partes) : "sem alteração";
        return $"Nível {nivel}: AUMENTO DE ATRIBUTO ({resumo})";
    }

    private IEnumerator CriarPasso_Escolha_Talento(int nivel)
    {
        if(infoText) infoText.text = "Carregando talentos locais do compêndio...";

        List<TalentoDB> talentosLocais = CarregarTalentosLocais();
        if (talentosLocais.Count == 0)
        {
            if(infoText) infoText.text = "<color=#B00020>Nenhum talento local encontrado em Dados/talentos.</color>";
            AvancarPasso();
            yield break;
        }

        GameObject go = Instantiate(stepContainerPrefab, mainScrollViewContent, false); go.SetActive(true); StepContainerUI stepUI = go.GetComponent<StepContainerUI>();
        Dictionary<string, string> opcoes = new Dictionary<string, string>();
        Dictionary<string, string> descricoes = new Dictionary<string, string>();

        foreach (var t in talentosLocais)
        {
            if (t == null || string.IsNullOrWhiteSpace(t.id_talento) || string.IsNullOrWhiteSpace(t.nome_talento)) continue;
            if (opcoes.ContainsKey(t.id_talento)) continue;

            opcoes.Add(t.id_talento, t.nome_talento);
            descricoes.Add(t.id_talento, $"<size=150%><b>{t.nome_talento}</b></size>\n\n<i>{t.resumo_talento}</i>");
        }

        if (opcoes.Count == 0)
        {
            if(infoText) infoText.text = "<color=#B00020>Talentos locais inválidos.</color>";
            AvancarPasso();
            yield break;
        }

        stepUI.SetupStep($"ESCOLHA DE TALENTO (NÍVEL {nivel})", opcoes,
            (id) => { if (infoText && descricoes.ContainsKey(id)) infoText.text = descricoes[id]; },
            (idConfirmado) => {
                atributosTalentosEscolhidos.Add($"Nível {nivel}: TALENTO={idConfirmado}");
                AvancarPasso();
            },
            true
        );

        yield return null;
    }

    public IEnumerator SalvarClassePublico() {
        Debug.Log("<b><color=#00FF00>[ClassSelector - SAVE]</color></b> Iniciando processo de salvamento de classe (Corrotina)...");
        // Em modo multiclasse, classeSelecionada não é preenchida pelo fluxo normal  - 
        // usa a classe principal de multiclassesSelecionadas como fallback.
        if (classeSelecionada == null && GlobalFichaData.modoMulticlasse
            && GlobalFichaData.multiclassesSelecionadas != null
            && GlobalFichaData.multiclassesSelecionadas.Count > 0)
        {
            string nomeClassePrincipal = GlobalFichaData.multiclassesSelecionadas[0].className;
            classeSelecionada = classesDisponiveis.FirstOrDefault(
                c => string.Equals(c.name, nomeClassePrincipal, StringComparison.OrdinalIgnoreCase));
            if (classeSelecionada != null)
            {
                ExtrairDadoDeVidaESalvaguardas(classeSelecionada.xml);
                try {
                    XDocument doc = XDocument.Parse(classeSelecionada.xml.Trim().Replace("ï»¿", ""));
                    LerProficienciasFixasClasse(doc.Root);
                } catch { }
            }
        }

        if (classeSelecionada == null) yield break;

        if (!GarantirFichaLocalDisponivel())
        {
            Debug.LogError("Erro ao preparar ficha local para salvar classe.");
            if (infoText) infoText.text = "<color=#B00020>Erro de salvamento local.</color>";
            yield break;
        }

        string classeIdSalvar = !string.IsNullOrEmpty(classeSelecionada.remoteId) ? classeSelecionada.remoteId : classeSelecionada.id;

        int vidaParaSalvar = 0;
        int.TryParse(GlobalFichaData.vidaPersonagem, out vidaParaSalvar);

        // Proteção: não sobrescrever vida_maxima com 0 se o snapshot já tem um valor
        if (vidaParaSalvar <= 0)
        {
            int pidV = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
            string sidV = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
            if (LocalFichaStorage.TryGetFichaSnapshot(sidV, pidV,
                GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId, out LocalFichaData snapV)
                && snapV != null && snapV.vida_maxima > 0)
            {
                vidaParaSalvar = snapV.vida_maxima;
                GlobalFichaData.vidaPersonagem = vidaParaSalvar.ToString();
            }
        }

        Debug.Log($"<b><color=#DA70D6>[ClassSelector - Subclasse]</color></b> Dados de Subclasse a serem injetados no JSON: ID='{GlobalFichaData.currentSubclassId}', Nome='{subclasseSelecionadaNome}'");
        ClasseInfoData infoData = new ClasseInfoData();
        infoData.subclass_id = GlobalFichaData.currentSubclassId;
        infoData.subclass_name = subclasseSelecionadaNome;
        infoData.saves = new List<string>(ProficiencySelector.SavesClasseLocal);
        
        infoData.skills = new List<string>(proficienciasEscolhidas);
        infoData.armors = new List<string>(armadurasClasse);
        infoData.weapons = new List<string>(armasClasse);
        infoData.tools = new List<string>(ferramentasEscolhidas); 
        infoData.languages = new List<string>(ProficiencySelector.IdiomasClasseLocal);
        
        infoData.equipment = new List<string>(equipamentoEscolhido); 
        infoData.asi_feats = new List<string>(atributosTalentosEscolhidos);
        infoData.class_powers = new List<ClassPowerData>(poderesClasseDesbloqueados);
        infoData.class_power_choices = new List<ClassPowerChoiceData>(escolhasPoderesClasse);
        infoData.subclass_powers = ExtrairPoderesSubclasseDesbloqueados(classeSelecionada.xml, GlobalFichaData.currentSubclassId, ObterNivelClassePrincipal());

        DetalhesUpdate update = new DetalhesUpdate();
        update.classe_info = infoData;
        string json = JsonUtility.ToJson(update); 

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        bool ok = LocalFichaStorage.SaveClassInfo(
            steamId,
            playerId,
            GlobalFichaData.fichasGeneralId,
            GlobalFichaData.fichasDndId,
            classeIdSalvar,
            vidaParaSalvar,
            json,
            classeSelecionada.name
        );

        if (ok) {
            GlobalFichaData.classePersonagem = classeSelecionada.name;
            GlobalFichaData.classInfoJson = json;
            Debug.Log($"<b><color=#00FF00>[ClassSelector - SAVE]</color></b> Classe salva com sucesso! JSON:\n{json}");

            // Salva no novo formato per-classe.
            // Se checkpoints já foram salvos (modo multiclasse), não sobrescreve com dados do save final
            // que teria subclasse da ÚLTIMA classe processada (bug). Checkpoints já salvaram correto.
            if (!_checkpointsSalvos)
            {
                bool isPrimaryAsync = !GlobalFichaData.modoMulticlasse
                    || GlobalFichaData.multiclassesSelecionadas == null
                    || GlobalFichaData.multiclassesSelecionadas.Count == 0
                    || (GlobalFichaData.multiclassesSelecionadas.Count > 0
                        && string.Equals(GlobalFichaData.multiclassesSelecionadas[0].className,
                            classeSelecionada.name, StringComparison.OrdinalIgnoreCase));
                LocalFichaStorage.SaveClassData(steamId, playerId,
                    GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId,
                    BuildClasseData(classeIdSalvar, isPrimaryAsync, infoData));
            }

            // Notifica SpellSelector para recarregar o catálogo com a nova classe.
            SpellSelector.PrecisaCarregarMagias = true;

            // Mesmo padrão de FichaSaveManager.cs/ClassSelector.cs:988,1040 — a cena tem duas
            // instâncias de EquipmentSelector, uma delas delegada (RotinaSalvarInventario nela é
            // sempre um no-op). ResolveInstanciaPrincipal() filtra a delegada mesmo no fallback:
            // Instance ainda pode estar null aqui se o jogador nunca abriu a aba "Equipamento"
            // nesta sessão (OnEnable da instância principal ainda não rodou), e um
            // FindObjectOfType sem filtro podia escolher a delegada por ordem interna do Unity —
            // itens de classe importados nela nunca chegavam no disco, mesmo aparecendo em tela.
            EquipmentSelector equipSel = EquipmentSelector.ResolveInstanciaPrincipal();
            if (equipSel != null)
            {
                // A própria rotina de sincronização é idempotente: ela remove os itens
                // "CLASS_" adicionados anteriormente antes de reimportar a lista atual,
                // então não há necessidade (nem é seguro) tentar adivinhar se o inventário
                // "já existe" - isso levava a nunca importar o equipamento da classe quando
                // o inventário só continha o item oculto de controle (import_flag).
                EquipmentSelector.PrecisaCarregarEquipamentos = true;
                yield return equipSel.RotinaReceberESalvarEquipamentos(equipamentoEscolhido);
                if (infoText) infoText.text = "<color=#1B5E20>Classe e Inventário Atualizados com Sucesso!</color>";
            }

            // Oferecer seleção de imagem do token após salvar a classe. Só daqui em diante é seguro
            // avançar de etapa: fazer isso antes (ex: junto ao RefreshProgressaoUI em AvancarPasso())
            // desativaria o painel "TelaClasse" via MostrarEtapa() enquanto esta corrotina ainda está
            // suspensa em RotinaReceberESalvarEquipamentos — na cena Mobile Android, ClassSelector vive
            // no próprio painel de Classe (diferente da cena Desktop), então isso mataria a corrotina
            // no meio e o equipamento da classe nunca seria importado no inventário. Só chega aqui
            // depois que tudo (dados de classe + equipamento) já foi salvo.
            if (criacaoFichaProgressao != null)
            {
                criacaoFichaProgressao.AtivarSeletorImagem();
                criacaoFichaProgressao.AvancarSeEtapaAtualFor(TipoResumoEtapaDnd.Classe);
            }
        } else {
            Debug.LogError("<b><color=#FF0000>[ClassSelector - SAVE]</color></b> Falha crítica ao salvar classe localmente.");
            if (infoText) infoText.text = "<color=#B00020>Erro de salvamento local.</color>";
        }
    }

    /// <summary>
    /// Versão síncrona do save de classe  -  persiste o estado atual sem corrotinas.
    /// Chamado pelo FichaSaveManager.SalvarLocalSincronamente() no OnDisable.
    /// </summary>
    public void SalvarDadosClasseSincronamente()
    {
        Debug.Log("<b><color=#00FF00>[ClassSelector - SAVE]</color></b> Salvamento Síncrono forçado acionado.");
        // Resolve classeSelecionada em modo multiclasse se necessário
        if (classeSelecionada == null && GlobalFichaData.modoMulticlasse
            && GlobalFichaData.multiclassesSelecionadas != null
            && GlobalFichaData.multiclassesSelecionadas.Count > 0)
        {
            string nomePrincipal = GlobalFichaData.multiclassesSelecionadas[0].className;
            classeSelecionada = classesDisponiveis.FirstOrDefault(
                c => string.Equals(c.name, nomePrincipal, StringComparison.OrdinalIgnoreCase));
            if (classeSelecionada != null)
            {
                ExtrairDadoDeVidaESalvaguardas(classeSelecionada.xml);
                try {
                    XDocument doc = XDocument.Parse(classeSelecionada.xml.Trim().Replace("ï»¿", ""));
                    LerProficienciasFixasClasse(doc.Root);
                } catch { }
            }
        }

        if (classeSelecionada == null || GlobalFichaData.fichasDndId <= 0) return;
        if (!GarantirFichaLocalDisponivel()) return;

        string classeId = !string.IsNullOrEmpty(classeSelecionada.remoteId) ? classeSelecionada.remoteId : classeSelecionada.id;
        int.TryParse(GlobalFichaData.vidaPersonagem, out int vida);

        // Proteção: se vidaPersonagem está vazio/zero, lê do snapshot para não apagar vida_maxima
        if (vida <= 0)
        {
            int pidVida = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
            string sidVida = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
            if (LocalFichaStorage.TryGetFichaSnapshot(sidVida, pidVida,
                GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId, out LocalFichaData snapVida)
                && snapVida != null && snapVida.vida_maxima > 0)
            {
                vida = snapVida.vida_maxima;
                GlobalFichaData.vidaPersonagem = vida.ToString();
            }
        }

        // Proteção: preserva dados da subclasse existente se o wizard não completou o passo de subclasse
        string subclasseIdFinal   = GlobalFichaData.currentSubclassId;
        string subclasseNomeFinal = subclasseSelecionadaNome;
        if (string.IsNullOrEmpty(subclasseIdFinal) && RestauracaoInfo != null
            && !string.IsNullOrEmpty(RestauracaoInfo.subclass_id))
        {
            subclasseIdFinal   = RestauracaoInfo.subclass_id;
            subclasseNomeFinal = RestauracaoInfo.subclass_name ?? subclasseNomeFinal;
        }

        Debug.Log($"<b><color=#DA70D6>[ClassSelector - Subclasse]</color></b> Dados Síncronos: ID='{subclasseIdFinal}', Nome='{subclasseNomeFinal}'");
        ClasseInfoData infoData = new ClasseInfoData
        {
            subclass_id   = subclasseIdFinal,
            subclass_name = subclasseNomeFinal,
            saves     = new List<string>(ProficiencySelector.SavesClasseLocal),
            skills    = new List<string>(proficienciasEscolhidas),
            armors    = new List<string>(armadurasClasse),
            weapons   = new List<string>(armasClasse),
            tools     = new List<string>(ferramentasEscolhidas),
            languages = new List<string>(ProficiencySelector.IdiomasClasseLocal),
            equipment = new List<string>(equipamentoEscolhido),
            asi_feats = new List<string>(atributosTalentosEscolhidos),
            class_powers        = new List<ClassPowerData>(poderesClasseDesbloqueados),
            class_power_choices = new List<ClassPowerChoiceData>(escolhasPoderesClasse),
            subclass_powers     = ExtrairPoderesSubclasseDesbloqueados(classeSelecionada.xml, GlobalFichaData.currentSubclassId, ObterNivelClassePrincipal())
        };

        string json = JsonUtility.ToJson(new DetalhesUpdate { classe_info = infoData });

        int    pid = DataManager.Instance != null ? DataManager.Instance.GetCurrentId()      : 0;
        string sid = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        bool ok = LocalFichaStorage.SaveClassInfo(sid, pid,
            GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId,
            classeId, vida, json, classeSelecionada.name);

        if (ok)
        {
            GlobalFichaData.classePersonagem = classeSelecionada.name;
            GlobalFichaData.classInfoJson    = json;

            // Salva no novo formato per-classe.
            // Se checkpoints já foram salvos (modo multiclasse), não sobrescreve  -  checkpoints
            // salvaram cada classe com a subclasse correta antes de ser sobrescrita.
            if (!_checkpointsSalvos)
            {
                bool isPrimarySync = !GlobalFichaData.modoMulticlasse
                    || GlobalFichaData.multiclassesSelecionadas == null
                    || GlobalFichaData.multiclassesSelecionadas.Count == 0
                    || (GlobalFichaData.multiclassesSelecionadas.Count > 0
                        && string.Equals(GlobalFichaData.multiclassesSelecionadas[0].className,
                            classeSelecionada.name, StringComparison.OrdinalIgnoreCase));
                LocalFichaStorage.SaveClassData(sid, pid,
                    GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId,
                    BuildClasseData(classeId, isPrimarySync, infoData));
            }

            // Salva multiclasse se aplicável
            if (GlobalFichaData.modoMulticlasse && GlobalFichaData.multiclassesSelecionadas != null)
            {
                string mcJson = JsonUtility.ToJson(new MulticlassDataWrapper
                    { classes = GlobalFichaData.multiclassesSelecionadas });
                LocalFichaStorage.SaveMulticlasse(sid, pid,
                    GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId, mcJson);
            }

            Debug.Log("<b><color=#00FF00>[ClassSelector - SAVE]</color></b> Salvamento Síncrono efetuado com sucesso.");
        }
        else
        {
            Debug.LogWarning("[ClassSelector] SalvarDadosClasseSincronamente: falha ao salvar.");
        }
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

            Debug.LogWarning("[ClassSelector] Snapshot local não encontrado. Uma nova ficha local foi criada automaticamente.");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[ClassSelector] Falha ao garantir ficha local: " + ex.Message);
            return false;
        }
    }

    private bool TentarCarregarClassesLocais(out List<ClassData> classesLocais)
    {
        classesLocais = new List<ClassData>();

        string pasta = DndSrdLocalPath.Resolver(pastaLocalRelativa);
        Debug.Log($"[ClassSelector] Buscando classes em: {pasta}");

        if (!Directory.Exists(pasta))
        {
            Debug.LogWarning($"[ClassSelector] Pasta n\u00E3o encontrada: {pasta}");
            return false;
        }

        var arquivos = ImportadorConteudoUniversal.EnumerarArquivosXmlEJson(pasta, SearchOption.TopDirectoryOnly)
            .Where(f => {
                string fn = Path.GetFileName(f).ToLowerInvariant();
                return !fn.Contains("template") && !fn.Contains(" copy");
            });

        foreach (string arquivo in arquivos)
        {
            try
            {
                string xml;
                if (ImportadorConteudoUniversal.EhArquivoJson(arquivo))
                {
                    string json = ImportadorConteudoUniversal.LerTextoComFallbackDeEncoding(arquivo);
                    xml = json != null ? ImportadorConteudoUniversal.ConverterJsonParaXml(json, "class") : null;
                    if (xml == null)
                    {
                        Debug.LogWarning($"[ClassSelector] JSON inv\u00E1lido em '{Path.GetFileName(arquivo)}' \u2014 ignorando.");
                        continue;
                    }
                }
                else
                {
                    xml = File.ReadAllText(arquivo, Encoding.UTF8).Trim().Replace("\uFEFF", "");
                }
                XDocument doc = XDocument.Parse(xml);
                XElement root = doc.Element("class") ?? doc.Root;
                if (root == null) continue;

                string id = root.Attribute("id")?.Value;
                string nome = root.Element("name")?.Value;
                if (string.IsNullOrWhiteSpace(nome)) continue;
                if (string.IsNullOrWhiteSpace(id)) id = Path.GetFileNameWithoutExtension(arquivo);

                classesLocais.Add(new ClassData { id = id, remoteId = id, name = nome.Trim(), xml = xml });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ClassSelector] Ignorando arquivo inv\u00E1lido '{Path.GetFileName(arquivo)}': {e.Message}");
            }
        }

        Debug.Log($"[ClassSelector] {classesLocais.Count} classe(s) carregada(s).");
        classesLocais = classesLocais.OrderBy(c => c.name).ToList();
        return classesLocais.Count > 0;
    }

    private List<TalentoDB> CarregarTalentosLocais()
    {
        List<TalentoDB> talentos = new List<TalentoDB>();
        try
        {
            string pasta = DndSrdLocalPath.Resolver(pastaTalentosRelativa);
            if (!Directory.Exists(pasta)) return talentos;

            var arquivos = Directory.GetFiles(pasta, "*.xml", SearchOption.TopDirectoryOnly);
            foreach (string arquivo in arquivos)
            {
                string xml = File.ReadAllText(arquivo, Encoding.UTF8).Trim().Replace("\uFEFF", "");
                XDocument doc = XDocument.Parse(xml);
                XElement root = doc.Element("feat") ?? doc.Root;
                if (root == null) continue;

                string id = root.Attribute("id")?.Value;
                if (string.IsNullOrWhiteSpace(id)) id = Path.GetFileNameWithoutExtension(arquivo);

                string nome = root.Element("name")?.Value;
                if (string.IsNullOrWhiteSpace(nome)) nome = id;

                string resumo = root.Element("summary")?.Value;
                if (string.IsNullOrWhiteSpace(resumo))
                {
                    resumo = root.Element("description")?.Value ?? root.Element("text")?.Value ?? "";
                }

                talentos.Add(new TalentoDB
                {
                    id_talento = id.Trim(),
                    nome_talento = nome.Trim(),
                    resumo_talento = resumo.Trim()
                });
            }

            talentos = talentos.OrderBy(t => t.nome_talento).ToList();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[ClassSelector] Falha ao carregar talentos locais: " + ex.Message);
        }

        return talentos;
    }

    private GameObject InstanciateMainStepUI(out int indexoPasso)
    {
        indexoPasso = passoAtualIndex;
        passoAtualIndex++;
        var prefab = mainStepContainerPrefab != null ? mainStepContainerPrefab : stepContainerPrefab;
        GameObject go = UIUtils.Spawn(prefab, mainScrollViewContent, false);
        go.SetActive(true);
        return go;
    }

    private GameObject InstanciateStepUI(out int indexoPasso)
    {
        indexoPasso = passoAtualIndex;
        passoAtualIndex++;
        GameObject go = Instantiate(stepContainerPrefab, mainScrollViewContent, false);
        go.SetActive(true);
        return go;
    }

    private void ExtrairProficienciasSecundarias(string xml,
        out List<string> armors, out List<string> weapons,
        out List<string> tools,  out List<string> languages)
    {
        armors = new List<string>(); weapons = new List<string>();
        tools  = new List<string>(); languages = new List<string>();
        try
        {
            XDocument doc = XDocument.Parse(xml.Trim().Replace("﻿", ""));
            XElement root = doc.Element("class") ?? doc.Root;
            if (root == null) return;
            ProcessarTagProficiencia(root, "armor-proficiencies",  armors);
            ProcessarTagProficiencia(root, "weapon-proficiencies", weapons);
            ProcessarTagProficiencia(root, "tool-proficiencies",   tools);
            var langsEl = root.Descendants("languages").FirstOrDefault();
            if (langsEl != null)
            {
                var opts = langsEl.Elements("option").ToList();
                if (opts.Count == 0) opts = langsEl.Elements("language").ToList();
                foreach (var o in opts) languages.Add(o.Value);
            }
        }
        catch { }
    }

    private void ExtrairEquipamentosSecundarios(string xml, out List<string> equipment)
    {
        equipment = new List<string>();
        try
        {
            XDocument doc = XDocument.Parse(xml.Trim().Replace("﻿", ""));
            var equips = doc.Descendants("equipment").FirstOrDefault();
            var fixedNode = equips?.Element("fixed");
            if (fixedNode == null) return;
            foreach (var item in fixedNode.Elements("item"))
            {
                string idAttr = item.Attribute("id")?.Value;
                if (idAttr == "Ouro" || idAttr == "GP") equipment.Add($"OURO:{item.Value}");
                else if (!string.IsNullOrEmpty(idAttr))  equipment.Add($"{idAttr}|{item.Value}");
                else                                      equipment.Add(item.Value);
            }
        }
        catch { }
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
}