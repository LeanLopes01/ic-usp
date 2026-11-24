using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ProficiencySelector : MonoBehaviour
{
    [Header("UI - Containers")]
    public Transform periciasContainer;
    public Transform salvaguardasContainer;
    public GameObject skillPrefab;

    [Header("UI - Resumo (Scroll + Prefab Unico)")]
    public Transform periciasResumoContainer;
    public GameObject resumoItemPrefab;
    public Transform proeficienciasResumoContainer;
    public Transform linguasResumoContainer;

    [Header("UI - Detalhes")]
    public TMP_Text detalhesTituloText;
    public TMP_Text detalhesDescricaoText;

    // --- CAIXA DE CORREIO LOCAL ---
    public static List<string> FerramentasClasseLocal = new List<string>();
    public static List<string> FerramentasAntecedenteLocal = new List<string>();
    public static List<string> FerramentasRacaLocal = new List<string>();

    public static List<string> SavesClasseLocal = new List<string>();
    public static List<string> PericiasClasseLocal = new List<string>(); // perícias escolhidas/fixas da CLASSE
    public static List<string> PericiasRacaLocal   = new List<string>(); // perícias fixas/escolhidas da RAÇA

    public static List<string> IdiomasClasseLocal = new List<string>();
    public static List<string> IdiomasAntecedenteLocal = new List<string>();
    public static List<string> IdiomasRacaLocal = new List<string>();

    public static Action OnImportacaoSolicitada;

    /// <summary>Limpa todas as listas estáticas. Deve ser chamado ao trocar/criar ficha.</summary>
    public static void ResetListasLocais()
    {
        PericiasClasseLocal.Clear();
        PericiasRacaLocal.Clear();
        FerramentasClasseLocal.Clear();
        FerramentasAntecedenteLocal.Clear();
        FerramentasRacaLocal.Clear();
        SavesClasseLocal.Clear();
        IdiomasClasseLocal.Clear();
        IdiomasAntecedenteLocal.Clear();
        IdiomasRacaLocal.Clear();
    }

    private Dictionary<string, int> modificadoresAtuais = new Dictionary<string, int>();
    private Dictionary<string, string> aliasPericias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private int bonusProficiencia = 2;
    private List<SkillUIItem> periciasInstanciadas = new List<SkillUIItem>();
    private List<SkillUIItem> savesInstanciados = new List<SkillUIItem>();

    private readonly List<string> attrOrdem = new List<string> { "FOR", "DEX", "CON", "INT", "SAB", "CAR" };

    // Última ficha vista por este componente. -1 = ainda não ativado nesta sessão do Unity (nesse
    // caso NÃO tratamos como troca de ficha — a "caixa de correio" estática já foi populada
    // legitimamente por Raça/Classe/Antecedente mais cedo NESTA MESMA sessão de criação). Só
    // dispara a limpeza defensiva quando já tínhamos uma ficha real carregada e ela mudou por
    // baixo sem passar pelo portão central (GlobalFichaData.IniciarSessaoParaFicha).
    private int lastLoadedFichaDndId = -1;

    [Serializable] private class PericiasDBResponse { public string status; public List<PericiaDB> pericias; }
    [Serializable] private class PericiaDB { public string nome_pericia; public string descricao_pericia; public string atributo_base; }
    
    [Serializable] public class SkillSaveItem { public string nome; public bool treinado; public int attrIndex; public int miscBonus; public int valorTotal; }
    
    [Serializable] public class ProficienciesInfoData 
    { 
        public List<SkillSaveItem> skills; 
        public List<SkillSaveItem> saves; 
        public List<string> armors; 
        public List<string> weapons; 
        public List<string> tools; 
        public List<string> languages; 
    }
    
    [Serializable] public class ProficienciesMergeData 
    { 
        public ProficienciesInfoData proficiencies_info; 
    }

    void Awake()
    {
        AttributeManager.OnAtributosGlobaisMudaram += AtualizarAtributosAoVivo;
        OnImportacaoSolicitada += AplicarImportacaoAoVivo;
        foreach (string attr in attrOrdem) modificadoresAtuais[attr] = 0;
    }

    void OnDestroy()
    {
        AttributeManager.OnAtributosGlobaisMudaram -= AtualizarAtributosAoVivo;
        OnImportacaoSolicitada -= AplicarImportacaoAoVivo;
    }

    void OnEnable()
    {
        GlobalFichaData.OnSessaoDeFichaIniciada += OnSessaoDeFichaIniciada;

        int fichaAnteriorId  = lastLoadedFichaDndId;
        int fichaAtualId     = GlobalFichaData.fichasDndId;
        lastLoadedFichaDndId = fichaAtualId;

        if (fichaAnteriorId != -1 && fichaAnteriorId != fichaAtualId)
        {
            // Defesa extra além do portão central: se esta tela ficou ativa e a ficha mudou por
            // baixo sem passar por GlobalFichaData.IniciarSessaoParaFicha (que já limpa esta
            // caixa de correio no início de toda sessão), não reconstruir a partir de
            // proficiências que pertencem a outra ficha.
            Debug.LogWarning($"[ProficiencySelector] Ficha ativa mudou de {fichaAnteriorId} para {fichaAtualId} sem passar pelo portão central — limpando listas locais por segurança.");
            ResetListasLocais();
        }

        CalcularBonusDeProficiencia();
        StartCoroutine(RotinaInicializacao());
    }

    void OnDisable()
    {
        GlobalFichaData.OnSessaoDeFichaIniciada -= OnSessaoDeFichaIniciada;
    }

    /// <summary>
    /// Gatilho independente de OnEnable/Start, pro caso deste passo virar um GameObject sempre
    /// ativo (mesma correção já aplicada em ClassSelector pro bug de CanvasGroup/layout preso após
    /// SetActive(false)-&gt;(true)) — nesse cenário OnEnable nunca mais dispara de novo, e sem isto
    /// as perícias/proficiências já marcadas na UI de uma ficha ANTERIOR continuariam presas aqui
    /// mesmo depois de criar/abrir outra ficha. GlobalFichaData.OnSessaoDeFichaIniciada dispara em
    /// TODA troca de ficha, independente do ciclo de vida do GameObject. Aqui é sempre um reset
    /// legítimo (o próprio portão central que disparou o evento), não precisa do aviso de log que
    /// OnEnable usa pro caso de bypass do portão.
    /// </summary>
    private void OnSessaoDeFichaIniciada()
    {
        lastLoadedFichaDndId = GlobalFichaData.fichasDndId;
        ResetListasLocais();
        CalcularBonusDeProficiencia();
        if (gameObject.activeInHierarchy) StartCoroutine(RotinaInicializacao());
    }

    private void AtualizarAtributosAoVivo(Dictionary<string, int> novosMods)
    {
        foreach (var kvp in novosMods) modificadoresAtuais[kvp.Key] = kvp.Value;
        foreach (var ui in periciasInstanciadas) if (ui != null) AtualizarMatematica(ui);
        foreach (var ui in savesInstanciados) if (ui != null) AtualizarMatematica(ui);
    }

    private void AplicarImportacaoAoVivo()
    {
        ConstruirAliasPericias();

        foreach (string sigla in SavesClasseLocal)
        {
            string siglaLimpa = sigla.Trim().ToUpper();
            string nomeBr = "";

            if (siglaLimpa == "STR" || siglaLimpa == "FOR") nomeBr = "FOR";
            else if (siglaLimpa == "DEX" || siglaLimpa == "DES") nomeBr = "DEX";
            else if (siglaLimpa == "CON") nomeBr = "CON";
            else if (siglaLimpa == "INT") nomeBr = "INT";
            else if (siglaLimpa == "WIS" || siglaLimpa == "SAB") nomeBr = "SAB";
            else if (siglaLimpa == "CHA" || siglaLimpa == "CAR") nomeBr = "CAR";

            if (nomeBr != "")
            {
                SkillUIItem uiSave = savesInstanciados.Find(x => x.nomeOriginal.ToUpper() == nomeBr);
                if (uiSave != null)
                {
                    uiSave.treinadoToggle.isOn = true;
                    AtualizarMatematica(uiSave);
                }
            }
        }

        Dictionary<string, string> tradutorPericias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"Acrobatics", "Acrobacia"}, {"Animal Handling", "Lidar com Animais"}, {"Arcana", "Arcanismo"},
            {"Athletics", "Atletismo"}, {"Deception", "Enganação"}, {"History", "História"},
            {"Insight", "Intuição"}, {"Intimidation", "Intimidação"}, {"Investigation", "Investigação"},
            {"Medicine", "Medicina"}, {"Nature", "Natureza"}, {"Perception", "Percepção"},
            {"Performance", "Atuação"}, {"Persuasion", "Persuasão"}, {"Religion", "Religião"},
            {"Sleight of Hand", "Prestidigitação"}, {"Stealth", "Furtividade"}, {"Survival", "Sobrevivência"}
        };

        // Perícias da Classe
        foreach (string nomePericiaXml in PericiasClasseLocal)
        {
            string nomeParaBuscar = ResolverNomePericia(nomePericiaXml, tradutorPericias);
            SkillUIItem uiSkill = EncontrarPericiaPorNome(nomeParaBuscar);
            if (uiSkill != null) { uiSkill.treinadoToggle.isOn = true; AtualizarMatematica(uiSkill); }
        }

        // Perícias da Raça (lista separada para não ser sobrescrita pela classe)
        foreach (string nomePericiaRaca in PericiasRacaLocal)
        {
            string nomeParaBuscar = ResolverNomePericia(nomePericiaRaca, tradutorPericias);
            SkillUIItem uiSkill = EncontrarPericiaPorNome(nomeParaBuscar);
            if (uiSkill != null) { uiSkill.treinadoToggle.isOn = true; AtualizarMatematica(uiSkill); }
        }

        AtualizarResumoPericias(tradutorPericias);
        AtualizarResumoProeficiencias();
        AtualizarResumoIdiomas();
    }

    private void ConstruirAliasPericias()
    {
        aliasPericias.Clear();
        foreach (var ui in periciasInstanciadas)
        {
            if (ui == null || string.IsNullOrWhiteSpace(ui.nomeOriginal)) continue;
            string chave = NormalizarTexto(ui.nomeOriginal);
            if (!aliasPericias.ContainsKey(chave)) aliasPericias[chave] = ui.nomeOriginal;
        }

        // aliases comuns EN -> PT e variações de nomenclatura
        AddAlias("animalhandling", "Lidar com Animais");
        AddAlias("lidarcomanimais", "Lidar com Animais");
        AddAlias("arcana", "Arcanismo");
        AddAlias("athletics", "Atletismo");
        AddAlias("deception", "Enganação");
        AddAlias("history", "História");
        AddAlias("insight", "Intuição");
        AddAlias("intimidation", "Intimidação");
        AddAlias("investigation", "Investigação");
        AddAlias("medicine", "Medicina");
        AddAlias("nature", "Natureza");
        AddAlias("perception", "Percepção");
        AddAlias("performance", "Atuação");
        AddAlias("persuasion", "Persuasão");
        AddAlias("religion", "Religião");
        AddAlias("sleightofhand", "Prestidigitação");
        AddAlias("stealth", "Furtividade");
        AddAlias("survival", "Sobrevivência");
        AddAlias("acrobatics", "Acrobacia");
        AddAlias("adestraranimais", "Lidar com Animais");

        // Fallback dinâmico: se a base usa outro nome para a perícia de animais,
        // mapeia aliases para o item real carregado da API.
        var periciaAnimal = periciasInstanciadas
            .FirstOrDefault(x => x != null && NormalizarTexto(x.nomeOriginal).Contains("animal"));
        if (periciaAnimal != null)
        {
            string nomeReal = periciaAnimal.nomeOriginal;
            AddAlias("animalhandling", nomeReal);
            AddAlias("lidarcomanimais", nomeReal);
            AddAlias("adestraranimais", nomeReal);
        }
    }

    private void AddAlias(string chave, string nomeCanonico)
    {
        string c = NormalizarTexto(chave);
        if (aliasPericias.ContainsKey(c)) return;
        aliasPericias[c] = nomeCanonico;
    }

    private string ResolverNomePericia(string entrada, Dictionary<string, string> tradutorPericias)
    {
        if (string.IsNullOrWhiteSpace(entrada)) return "";

        string nome = entrada.Trim();
        if (tradutorPericias != null && tradutorPericias.TryGetValue(nome, out var traduzido))
            nome = traduzido;

        string chave = NormalizarTexto(nome);
        if (aliasPericias.TryGetValue(chave, out var canonico))
            return canonico;

        return nome;
    }

    private SkillUIItem EncontrarPericiaPorNome(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return null;
        string chave = NormalizarTexto(nome);
        SkillUIItem exata = periciasInstanciadas.Find(x => x != null && NormalizarTexto(x.nomeOriginal) == chave);
        if (exata != null) return exata;

        // fallback semântico para divergências de nomenclatura
        if (chave.Contains("animal"))
            return periciasInstanciadas.Find(x => x != null && NormalizarTexto(x.nomeOriginal).Contains("animal"));

        if (chave.Contains("prestidigitacao") || chave.Contains("sleightofhand") || chave.Contains("maoleve"))
            return periciasInstanciadas.Find(x => {
                if (x == null) return false;
                string n = NormalizarTexto(x.nomeOriginal);
                return n.Contains("prestid") || n.Contains("maoleve") || n.Contains("sleight");
            });

        return null;
    }

    private string NormalizarTexto(string valor)
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

    private void CalcularBonusDeProficiencia()
    {
        int nivel = GlobalFichaData.levelPersonagem > 0 ? GlobalFichaData.levelPersonagem : 1;
        bonusProficiencia = Mathf.FloorToInt((nivel - 1) / 4f) + 2;
    }

    private IEnumerator RotinaInicializacao()
    {
        AttributeManager am = FindObjectOfType<AttributeManager>(true);
        if (am != null) am.AtualizarTodosAtributosUI();

        CriarSalvaguardasBase();

        ConstruirListaDePericias(ObterPericiasLocaisPadrao());

        AplicarImportacaoAoVivo();
        yield return null;
    }

    /// <summary>
    /// Descrição real de uma perícia pelo nome (usado por outras telas, ex: ClassSelector,
    /// ao mostrar detalhes da opção escolhida no passo de Proficiências).
    /// </summary>
    public static string ObterDescricaoPericia(string nomePericia)
    {
        if (string.IsNullOrWhiteSpace(nomePericia)) return null;
        string alvo = nomePericia.Trim();
        PericiaDB pericia = ObterPericiasLocaisPadrao().FirstOrDefault(p =>
            string.Equals(p.nome_pericia, alvo, StringComparison.OrdinalIgnoreCase));
        return pericia?.descricao_pericia;
    }

    private static List<PericiaDB> ObterPericiasLocaisPadrao()
    {
        return new List<PericiaDB>
        {
            new PericiaDB { nome_pericia = "Acrobacia", atributo_base = "DEX", descricao_pericia = "Equilíbrio, manobras e quedas controladas." },
            new PericiaDB { nome_pericia = "Lidar com Animais", atributo_base = "SAB", descricao_pericia = "Acalmar, treinar e conduzir animais." },
            new PericiaDB { nome_pericia = "Arcanismo", atributo_base = "INT", descricao_pericia = "Conhecimento sobre magia, planos e tradições arcanas." },
            new PericiaDB { nome_pericia = "Atletismo", atributo_base = "FOR", descricao_pericia = "Escalar, nadar, saltar e proezas físicas." },
            new PericiaDB { nome_pericia = "Enganação", atributo_base = "CAR", descricao_pericia = "Mentir, blefar e disfarçar intenções." },
            new PericiaDB { nome_pericia = "História", atributo_base = "INT", descricao_pericia = "Conhecimento de eventos, culturas e eras passadas." },
            new PericiaDB { nome_pericia = "Intuição", atributo_base = "SAB", descricao_pericia = "Ler intenções e perceber mentiras." },
            new PericiaDB { nome_pericia = "Intimidação", atributo_base = "CAR", descricao_pericia = "Coagir e impor presença." },
            new PericiaDB { nome_pericia = "Investigação", atributo_base = "INT", descricao_pericia = "Analisar pistas e deduzir fatos." },
            new PericiaDB { nome_pericia = "Medicina", atributo_base = "SAB", descricao_pericia = "Estabilizar e diagnosticar condições físicas." },
            new PericiaDB { nome_pericia = "Natureza", atributo_base = "INT", descricao_pericia = "Conhecimento sobre fauna, flora e fenômenos naturais." },
            new PericiaDB { nome_pericia = "Percepção", atributo_base = "SAB", descricao_pericia = "Notar detalhes, sons e movimentos ao redor." },
            new PericiaDB { nome_pericia = "Atuação", atributo_base = "CAR", descricao_pericia = "Cantar, tocar, interpretar e entreter." },
            new PericiaDB { nome_pericia = "Persuasão", atributo_base = "CAR", descricao_pericia = "Convencer por diálogo, etiqueta e diplomacia." },
            new PericiaDB { nome_pericia = "Religião", atributo_base = "INT", descricao_pericia = "Conhecimento sobre deuses, ritos e cultos." },
            new PericiaDB { nome_pericia = "Prestidigitação", atributo_base = "DEX", descricao_pericia = "Manipulação fina e truques de mão." },
            new PericiaDB { nome_pericia = "Furtividade", atributo_base = "DEX", descricao_pericia = "Mover-se sem ser percebido." },
            new PericiaDB { nome_pericia = "Sobrevivência", atributo_base = "SAB", descricao_pericia = "Rastrear, orientar-se e sobreviver em ermos." }
        };
    }

    private void CriarSalvaguardasBase()
    {
        if (salvaguardasContainer == null || skillPrefab == null) return;
        foreach (Transform t in salvaguardasContainer) Destroy(t.gameObject);
        savesInstanciados.Clear();

        foreach (string attr in attrOrdem)
        {
            SkillUIItem ui = InstanciarItem(attr, attr, "Salvaguarda para resistir a efeitos físicos ou mágicos.", salvaguardasContainer);
            savesInstanciados.Add(ui);
        }
    }

    private void ConstruirListaDePericias(List<PericiaDB> periciasDb)
    {
        if (periciasContainer == null || skillPrefab == null) return;
        foreach (Transform t in periciasContainer) Destroy(t.gameObject);
        periciasInstanciadas.Clear();

        foreach (var p in periciasDb)
        {
            SkillUIItem ui = InstanciarItem(p.nome_pericia, p.atributo_base, p.descricao_pericia, periciasContainer);
            periciasInstanciadas.Add(ui);
        }
    }

    private int PegarIndexDoAtributo(string atributoBanco)
    {
        if (string.IsNullOrEmpty(atributoBanco)) return 0;
        string val = atributoBanco.ToUpper().Trim();
        if (val.Contains("FOR") || val.Contains("STR")) return 0;
        if (val.Contains("DEX") || val.Contains("DES")) return 1;
        if (val.Contains("CON")) return 2;
        if (val.Contains("INT")) return 3;
        if (val.Contains("SAB") || val.Contains("WIS")) return 4;
        if (val.Contains("CAR") || val.Contains("CHA")) return 5;
        return 0;
    }

    private SkillUIItem InstanciarItem(string nome, string atributoBase, string descricao, Transform parent)
    {
        GameObject go = Instantiate(skillPrefab, parent);
        SkillUIItem ui = go.GetComponent<SkillUIItem>();

        ui.nomeOriginal = nome;
        ui.nomeText.text = nome;
        ui.descricaoDb = descricao;

        ui.atributoDropdown.value = PegarIndexDoAtributo(atributoBase);
        ui.treinadoToggle.isOn = false;

        ui.treinadoToggle.onValueChanged.AddListener((val) => { AtualizarMatematica(ui); AtualizarResumoPericias(null); });
        ui.atributoDropdown.onValueChanged.AddListener((val) => AtualizarMatematica(ui));
        ui.miscBonusInput.onValueChanged.AddListener((val) => AtualizarMatematica(ui));

        ui.invisibleButton.onClick.AddListener(() => {
            if (detalhesTituloText) detalhesTituloText.text = ui.nomeOriginal;
            if (detalhesDescricaoText) detalhesDescricaoText.text = ui.descricaoDb;
        });

        ui.miscBonusInput.text = "0";
        AtualizarMatematica(ui);
        return ui;
    }

    private void AtualizarMatematica(SkillUIItem ui)
    {
        string attrSelecionado = attrOrdem[ui.atributoDropdown.value];
        int modAtributo = modificadoresAtuais.ContainsKey(attrSelecionado) ? modificadoresAtuais[attrSelecionado] : 0;
        int bProf = ui.treinadoToggle.isOn ? bonusProficiencia : 0;
        
        ui.profBonusText.text = ui.treinadoToggle.isOn ? $"+{bonusProficiencia}" : "0";

        int misc = 0;
        int.TryParse(ui.miscBonusInput.text, out misc);

        int total = modAtributo + bProf + misc;
        string sinal = total >= 0 ? "+" : "";
        ui.totalText.text = $"{sinal}{total}";
    }

    private void AtualizarResumoPericias(Dictionary<string, string> tradutorPericias)
    {
        if (periciasResumoContainer == null || resumoItemPrefab == null) return;
        foreach (Transform t in periciasResumoContainer) Destroy(t.gameObject);
        HashSet<string> nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ui in periciasInstanciadas) if (ui != null && ui.treinadoToggle != null && ui.treinadoToggle.isOn) nomes.Add(ui.nomeOriginal.Trim());
        foreach (string nomePericiaXml in PericiasClasseLocal)
        {
            string nome = nomePericiaXml.Trim();
            if (tradutorPericias != null && tradutorPericias.ContainsKey(nome)) nome = tradutorPericias[nome];
            if (!string.IsNullOrEmpty(nome)) nomes.Add(nome);
        }
            foreach (string nomePericiaRaca in PericiasRacaLocal)
            {
                string nome = nomePericiaRaca.Trim();
                if (tradutorPericias != null && tradutorPericias.ContainsKey(nome)) nome = tradutorPericias[nome];
                if (!string.IsNullOrEmpty(nome)) nomes.Add(nome);
            }

        foreach (string nome in nomes.OrderBy(x => x)) AplicarTextoNoPrefab(Instantiate(resumoItemPrefab, periciasResumoContainer), nome);
    }

    private void AtualizarResumoIdiomas()
    {
        if (linguasResumoContainer == null || resumoItemPrefab == null) return;
        foreach (Transform t in linguasResumoContainer) Destroy(t.gameObject);
        HashSet<string> idiomas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var i in IdiomasClasseLocal) if (!string.IsNullOrWhiteSpace(i)) idiomas.Add(i.Trim());
        foreach (var i in IdiomasAntecedenteLocal) if (!string.IsNullOrWhiteSpace(i)) idiomas.Add(i.Trim());
        foreach (var i in IdiomasRacaLocal) if (!string.IsNullOrWhiteSpace(i)) idiomas.Add(i.Trim());

        foreach (string idioma in idiomas.OrderBy(x => x)) AplicarTextoNoPrefab(Instantiate(resumoItemPrefab, linguasResumoContainer), idioma);
    }

    private void AtualizarResumoProeficiencias()
    {
        if (proeficienciasResumoContainer == null || resumoItemPrefab == null) return;
        foreach (Transform t in proeficienciasResumoContainer) Destroy(t.gameObject);
        HashSet<string> proeficiencias = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in FerramentasClasseLocal) if (!string.IsNullOrWhiteSpace(p)) proeficiencias.Add(p.Trim());
        foreach (var p in FerramentasAntecedenteLocal) if (!string.IsNullOrWhiteSpace(p)) proeficiencias.Add(p.Trim());
        foreach (var p in FerramentasRacaLocal) if (!string.IsNullOrWhiteSpace(p)) proeficiencias.Add(p.Trim());

        foreach (string prof in proeficiencias.OrderBy(x => x)) AplicarTextoNoPrefab(Instantiate(resumoItemPrefab, proeficienciasResumoContainer), prof);
    }

    private void AplicarTextoNoPrefab(GameObject go, string valor)
    {
        ProeficienciaUI ui = go.GetComponent<ProeficienciaUI>();
        if (ui != null) { ui.SetTexto(valor); ui.SetupButton(() => MostrarDetalhesResumo(valor)); return; }

        TMP_Text txt = go.GetComponent<TMP_Text>();
        if (txt == null) txt = go.GetComponentInChildren<TMP_Text>();
        if (txt != null) txt.text = valor;

        Button btn = go.GetComponent<Button>();
        if (btn != null) { btn.onClick.RemoveAllListeners(); btn.onClick.AddListener(() => MostrarDetalhesResumo(valor)); }
    }

    private void MostrarDetalhesResumo(string nomeItem)
    {
        if (detalhesTituloText) detalhesTituloText.text = nomeItem;
        if (detalhesDescricaoText) detalhesDescricaoText.text = GerarDescricaoContextual(nomeItem);
    }

    public static string GerarDescricaoContextual(string nome)
    {
        string n = nome.ToLower();
        if (n.Contains("comum") || n.Contains("élfico") || n.Contains("anão") || n.Contains("dracônico") || n.Contains("goblin") || n.Contains("gigante") || n.Contains("orc") || n.Contains("infernal") || n.Contains("celestial") || n.Contains("abissal") || n.Contains("silvestre") || n.Contains("primordial") || n.Contains("subcomum") || n.Contains("cant") || n.Contains("language")) return $"Você é fluente no idioma {nome}. Você sabe falar, ler e escrever nesta língua.";
        if (n.Contains("armadura") || n.Contains("escudo") || n.Contains("light") || n.Contains("medium") || n.Contains("heavy") || n.Contains("shield") || n.Contains("armor")) return $"Você tem proficiência com {nome}. Usar essa proteção não impõe desvantagem em seus testes de Força e Destreza.";
        if (n.Contains("arma") || n.Contains("weapon") || n.Contains("martial") || n.Contains("simple")) return $"Você tem proficiência em ataques feitos com {nome}. Você adiciona seu Bônus de Proficiência nas rolagens de ataque.";
        if (n.Contains("ferramenta") || n.Contains("tools") || n.Contains("instrumento") || n.Contains("kit") || n.Contains("suprimento")) return $"Você tem proficiência com {nome}. Isso permite que você adicione seu Bônus de Proficiência ao usar essa ferramenta.";
        return $"Você possui proficiência com {nome}. Você adiciona seu Bônus de Proficiência aos testes exigidos pelo mestre ao interagir com isso.";
    }

    // =======================================================
    // SALVAMENTO DA FICHA - UNIFICADO E CATEGORIZADO
    // =======================================================

    private List<SkillSaveItem> GerarSaveList(List<SkillUIItem> uis)
    {
        List<SkillSaveItem> list = new List<SkillSaveItem>();
        foreach (var ui in uis)
        {
            int misc = 0;
            int.TryParse(ui.miscBonusInput.text, out misc);

            string attrSelecionado = attrOrdem[ui.atributoDropdown.value];
            int modAtributo = modificadoresAtuais.ContainsKey(attrSelecionado) ? modificadoresAtuais[attrSelecionado] : 0;
            int bProf = ui.treinadoToggle.isOn ? bonusProficiencia : 0;
            
            int total = modAtributo + bProf + misc;

            list.Add(new SkillSaveItem { 
                nome = ui.nomeOriginal, 
                treinado = ui.treinadoToggle.isOn, 
                attrIndex = ui.atributoDropdown.value, 
                miscBonus = misc,
                valorTotal = total
            });
        }
        return list;
    }

    // LISTA EXPANDIDA COM TERMOS DE ARMAS PARA FILTRAGEM
    private void CategorizarProficiencias(List<string> todas, out List<string> armaduras, out List<string> armas, out List<string> ferramentas)
    {
        armaduras = new List<string>();
        armas = new List<string>();
        ferramentas = new List<string>();

        string[] termosArmas = { 
            "machado", "martelo", "clava", "espada", "arco", "adaga", "lança", "azimaga", 
            "besta", "cimitarra", "rafeira", "mangual", "alabarda", "pique", "tridente", "funda", "zarabatana", "foice",
            "axe", "hammer", "club", "sword", "bow", "dagger", "spear", "crossbow", "scimitar" 
        };

        foreach (string p in todas)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            string n = p.Trim().ToLower();

            if (n.Contains("armadura") || n.Contains("escudo") || n.Contains("armor") || n.Contains("shield") || 
                n.Contains("light") || n.Contains("medium") || n.Contains("heavy"))
            {
                armaduras.Add(p.Trim());
            }
            else if (n.Contains("arma") || n.Contains("weapon") || n.Contains("martial") || n.Contains("simple") || 
                     termosArmas.Any(termo => n.Contains(termo)))
            {
                armas.Add(p.Trim());
            }
            else
            {
                ferramentas.Add(p.Trim());
            }
        }
    }

    public IEnumerator SalvarProficienciasComMerge()
    {
        List<SkillSaveItem> skillsList = GerarSaveList(periciasInstanciadas);
        List<SkillSaveItem> savesList = GerarSaveList(savesInstanciados);
        
        List<string> rawTools = new List<string>();
        rawTools.AddRange(FerramentasClasseLocal);
        rawTools.AddRange(FerramentasAntecedenteLocal);
        rawTools.AddRange(FerramentasRacaLocal);
        HashSet<string> toolsUnique = new HashSet<string>(rawTools, StringComparer.OrdinalIgnoreCase);
        
        // Passa a lista bruta pela função de separação
        CategorizarProficiencias(toolsUnique.ToList(), out List<string> armaduras, out List<string> armas, out List<string> ferramentas);

        HashSet<string> languagesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in IdiomasClasseLocal) if (!string.IsNullOrWhiteSpace(i)) languagesSet.Add(i.Trim());
        foreach (var i in IdiomasAntecedenteLocal) if (!string.IsNullOrWhiteSpace(i)) languagesSet.Add(i.Trim());
        foreach (var i in IdiomasRacaLocal) if (!string.IsNullOrWhiteSpace(i)) languagesSet.Add(i.Trim());
        
        ProficienciesInfoData profInfo = new ProficienciesInfoData
        {
            skills = skillsList,
            saves = savesList,
            armors = armaduras.OrderBy(x => x).ToList(),
            weapons = armas.OrderBy(x => x).ToList(),
            tools = ferramentas.OrderBy(x => x).ToList(),
            languages = languagesSet.OrderBy(x => x).ToList()
        };
        
        ProficienciesMergeData mergeData = new ProficienciesMergeData { proficiencies_info = profInfo };
        string json = JsonUtility.ToJson(mergeData);

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        bool ok = LocalFichaStorage.SaveProficienciesInfo(
            steamId,
            playerId,
            GlobalFichaData.fichasGeneralId,
            GlobalFichaData.fichasDndId,
            json
        );

        if (ok)
            Debug.Log("<color=green>Perícias, Saves, Armas, Armaduras e Idiomas salvos localmente!</color>");
        else
            Debug.LogError("Erro ao salvar proficiências localmente (ficha não encontrada).");

        yield return null;
    }
}