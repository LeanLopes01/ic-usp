using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TalentosSelector : MonoBehaviour
{
    [Header("Fonte de Dados")]
    public string pastaItensLocalRelativa = "sistemas/quinta-edicao-srd/talentos";

    [Header("UI - Lista Cascata")]
    public Transform mainScrollViewContent;
    public GameObject mainStepContainerPrefab; // Prefab com isMainStep=true (MainOptionButtonUI)
    public GameObject stepContainerPrefab;     // Prefab com isMainStep=false (SubChoiceOptionUI)

    [Header("UI - Detalhes (Direita)")]
    public TMP_Text infoText;

    [Header("Gerenciador de Progresso")]
    public CriacaoFichaProgressaoDnd criacaoFichaProgressao;

    [Header("Referência UI")]
    public FichaDisplayUI fichaDisplayUI;

    public static List<int> NiveisPendentesParaTalento = new List<int>();

    // Disparado por ClassSelector assim que um novo nível pendente de talento é registrado (ASI ->
    // escolheu "Talento"). Necessário porque, se esta tela for (ou vier a ser) um GameObject sempre
    // ativo — mesmo bug de CanvasGroup/layout já corrigido em ClassSelector —, OnEnable só roda uma
    // vez, normalmente antes de qualquer talento pendente existir, e nada mais reconfere a lista
    // depois. Sem isto, a aba de Talentos aparecia permanentemente vazia mesmo com pendência real.
    public static Action OnNiveisPendentesMudou;

    private Queue<Action> filaDePassos = new Queue<Action>();
    private int passoAtualIndex = 0;
    public static List<string> talentosEscolhidosNestaSessao = new List<string>();
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

    private List<TalentoDB> listaDeTalentos = new List<TalentoDB>();

    // Última ficha para a qual este componente carregou dados — comparada contra
    // GlobalFichaData.fichasDndId no OnEnable, mesmo padrão já usado em RaceSelector/ClassSelector/
    // BackgroundSelector/EquipmentSelector/SpellSelector. Camada extra de defesa além do portão
    // central GlobalFichaData.IniciarSessaoParaFicha: se algum ponto de entrada futuro esquecer de
    // chamar o portão, esta classe ainda se protege sozinha contra resíduo de outra ficha.
    private int lastLoadedFichaDndId = -1;

    [Serializable] public class TalentoDB { public string id_talento; public string nome_talento; public string xml; }

    void OnEnable()
    {
        GlobalFichaData.OnSessaoDeFichaIniciada += OnSessaoDeFichaIniciada;
        OnNiveisPendentesMudou += AtualizarParaFichaAtual;

        int  fichaAtualId = GlobalFichaData.fichasDndId;
        bool fichaMudou   = lastLoadedFichaDndId != fichaAtualId;
        lastLoadedFichaDndId = fichaAtualId;

        if (fichaMudou)
        {
            // Talentos "já escolhidos nesta sessão" são inerentemente por-ficha — se sobrarem de
            // uma ficha anterior eles bloqueiam essas opções (ou poluem CriacaoFichaProgressaoDnd)
            // na ficha atual sem o jogador nunca tê-los escolhido aqui.
            talentosEscolhidosNestaSessao.Clear();
        }

        AtualizarParaFichaAtual();
    }

    void OnDisable()
    {
        GlobalFichaData.OnSessaoDeFichaIniciada -= OnSessaoDeFichaIniciada;
        OnNiveisPendentesMudou -= AtualizarParaFichaAtual;
    }

    /// <summary>
    /// Gatilho independente de OnEnable/Start, pro caso deste passo virar um GameObject sempre
    /// ativo (mesma correção já aplicada em ClassSelector pro bug de CanvasGroup/layout preso após
    /// SetActive(false)-&gt;(true)) — nesse cenário OnEnable nunca mais dispara de novo, e sem isto
    /// talentos "já escolhidos" de uma ficha ANTERIOR continuariam presos aqui mesmo depois de
    /// criar/abrir outra ficha. GlobalFichaData.OnSessaoDeFichaIniciada dispara em TODA troca de
    /// ficha, independente do ciclo de vida do GameObject.
    /// </summary>
    private void OnSessaoDeFichaIniciada()
    {
        lastLoadedFichaDndId = GlobalFichaData.fichasDndId;
        talentosEscolhidosNestaSessao.Clear();
        AtualizarParaFichaAtual();
    }

    private void AtualizarParaFichaAtual()
    {
        if (NiveisPendentesParaTalento.Count > 0)
        {
            if (gameObject.activeInHierarchy) StartCoroutine(CarregarTalentosEIniciar());
        }
        else
        {
            if (infoText) infoText.text = "Você não possui talentos pendentes para escolher. Suba de nível na sua Classe para desbloquear novos talentos!";
            foreach (Transform child in mainScrollViewContent) Destroy(child.gameObject);

            // Sem isto, a etapa de Talentos ficava "presa" sempre que não havia nada pra escolher
            // (ex: personagem nível 1, antes do primeiro nível de ASI/talento da classe) — a fila de
            // passos nunca chegava a rodar (nem entrar no AvancarPasso() abaixo), então nada nunca
            // sinalizava "terminei" pra CriacaoFichaProgressaoDnd avançar sozinho.
            if (criacaoFichaProgressao != null)
                criacaoFichaProgressao.AvancarSeEtapaAtualFor(TipoResumoEtapaDnd.Talentos);
        }
    }

    private IEnumerator CarregarTalentosEIniciar()
    {
        if (infoText) infoText.text = "Carregando compêndio de talentos...";
        foreach (Transform child in mainScrollViewContent) Destroy(child.gameObject);
        filaDePassos.Clear();
        passoAtualIndex = 0;

        CarregarTalentosLocal();

        if (listaDeTalentos.Count == 0)
        {
            if (infoText) infoText.text = "<color=red>Erro: Nenhum talento encontrado em " + pastaItensLocalRelativa + ".</color>";
            yield break;
        }

        foreach (int nivel in NiveisPendentesParaTalento)
        {
            int n = nivel;
            filaDePassos.Enqueue(() => CriarPasso_EscolherTalento(n));
        }

        AvancarPasso();
        yield return null;
    }

    private void CarregarTalentosLocal()
    {
        try
        {
            listaDeTalentos.Clear();

            string pasta = DndSrdLocalPath.Resolver(pastaItensLocalRelativa);
            if (!Directory.Exists(pasta))
            {
                Debug.LogError("[TalentosSelector] Pasta local de talentos não encontrada: " + pasta);
                return;
            }

            foreach (string arquivo in Directory.GetFiles(pasta, "*.xml", SearchOption.AllDirectories))
            {
                try
                {
                    string xml = File.ReadAllText(arquivo, Encoding.UTF8).Trim().Replace("ï»¿", "");
                    XDocument doc = XDocument.Parse(xml);
                    XElement root = doc.Root;
                    if (root == null) continue;

                    string nome = root.Element("name")?.Value?.Trim() ?? Path.GetFileNameWithoutExtension(arquivo);
                    string idLogico = Path.GetFileNameWithoutExtension(arquivo);

                    listaDeTalentos.Add(new TalentoDB { id_talento = idLogico, nome_talento = nome, xml = xml });
                }
                catch (Exception eArquivo)
                {
                    Debug.LogWarning("[TalentosSelector] Erro ao carregar talento " + arquivo + ": " + eArquivo.Message);
                }
            }

            listaDeTalentos = listaDeTalentos.OrderBy(t => t.nome_talento).ToList();
            Debug.Log("[TalentosSelector] Talentos carregados localmente: " + listaDeTalentos.Count);
        }
        catch (Exception e)
        {
            Debug.LogError("[TalentosSelector] Erro ao carregar talentos: " + e.Message);
        }
    }

    private void AvancarPasso()
    {
        if (filaDePassos.Count > 0)
        {
            filaDePassos.Dequeue().Invoke();
        }
        else
        {
            if (infoText) infoText.text = "<color=green>Todos os talentos configurados!</color>";
            NiveisPendentesParaTalento.Clear();

            if (criacaoFichaProgressao != null)
            {
                criacaoFichaProgressao.RefreshProgressaoUI();
                Debug.Log("[TalentosSelector] ✓ CriacaoFichaProgressaoDnd.RefreshProgressaoUI() chamado");
                criacaoFichaProgressao.AvancarSeEtapaAtualFor(TipoResumoEtapaDnd.Talentos);
            }

            AtualizarFichaDisplayComBonus();
        }
    }

    private void AtualizarFichaDisplayComBonus()
    {
        if (fichaDisplayUI == null) return;

        int forcaTotal       = GlobalFichaData.forca        + GlobalFichaData.bonusRacaDict["FOR"] + GlobalFichaData.bonusClasseDict["FOR"] + GlobalFichaData.bonusAntecedenteDict["FOR"] + GlobalFichaData.bonusTalentoDict["FOR"];
        int destrezaTotal    = GlobalFichaData.destreza     + GlobalFichaData.bonusRacaDict["DEX"] + GlobalFichaData.bonusClasseDict["DEX"] + GlobalFichaData.bonusAntecedenteDict["DEX"] + GlobalFichaData.bonusTalentoDict["DEX"];
        int constituicaoTotal= GlobalFichaData.constituicao + GlobalFichaData.bonusRacaDict["CON"] + GlobalFichaData.bonusClasseDict["CON"] + GlobalFichaData.bonusAntecedenteDict["CON"] + GlobalFichaData.bonusTalentoDict["CON"];
        int inteligenciaTotal= GlobalFichaData.inteligencia + GlobalFichaData.bonusRacaDict["INT"] + GlobalFichaData.bonusClasseDict["INT"] + GlobalFichaData.bonusAntecedenteDict["INT"] + GlobalFichaData.bonusTalentoDict["INT"];
        int sabedoriaTotal   = GlobalFichaData.sabedoria    + GlobalFichaData.bonusRacaDict["SAB"] + GlobalFichaData.bonusClasseDict["SAB"] + GlobalFichaData.bonusAntecedenteDict["SAB"] + GlobalFichaData.bonusTalentoDict["SAB"];
        int carismaTotal     = GlobalFichaData.carisma      + GlobalFichaData.bonusRacaDict["CAR"] + GlobalFichaData.bonusClasseDict["CAR"] + GlobalFichaData.bonusAntecedenteDict["CAR"] + GlobalFichaData.bonusTalentoDict["CAR"];

        FichaDisplayUI.AtributosResponse totais = new FichaDisplayUI.AtributosResponse();
        totais.atributos.forca        = forcaTotal;
        totais.atributos.destreza     = destrezaTotal;
        totais.atributos.constituicao = constituicaoTotal;
        totais.atributos.inteligencia = inteligenciaTotal;
        totais.atributos.sabedoria    = sabedoriaTotal;
        totais.atributos.carisma      = carismaTotal;

        FichaDisplayUI.BonusBreakdownData breakdown = new FichaDisplayUI.BonusBreakdownData();
        breakdown.breakdown_forca        = $"{GlobalFichaData.forca}        + {GlobalFichaData.bonusRacaDict["FOR"] + GlobalFichaData.bonusClasseDict["FOR"] + GlobalFichaData.bonusAntecedenteDict["FOR"] + GlobalFichaData.bonusTalentoDict["FOR"]}";
        breakdown.breakdown_destreza     = $"{GlobalFichaData.destreza}     + {GlobalFichaData.bonusRacaDict["DEX"] + GlobalFichaData.bonusClasseDict["DEX"] + GlobalFichaData.bonusAntecedenteDict["DEX"] + GlobalFichaData.bonusTalentoDict["DEX"]}";
        breakdown.breakdown_constituicao = $"{GlobalFichaData.constituicao} + {GlobalFichaData.bonusRacaDict["CON"] + GlobalFichaData.bonusClasseDict["CON"] + GlobalFichaData.bonusAntecedenteDict["CON"] + GlobalFichaData.bonusTalentoDict["CON"]}";
        breakdown.breakdown_inteligencia = $"{GlobalFichaData.inteligencia} + {GlobalFichaData.bonusRacaDict["INT"] + GlobalFichaData.bonusClasseDict["INT"] + GlobalFichaData.bonusAntecedenteDict["INT"] + GlobalFichaData.bonusTalentoDict["INT"]}";
        breakdown.breakdown_sabedoria    = $"{GlobalFichaData.sabedoria}    + {GlobalFichaData.bonusRacaDict["SAB"] + GlobalFichaData.bonusClasseDict["SAB"] + GlobalFichaData.bonusAntecedenteDict["SAB"] + GlobalFichaData.bonusTalentoDict["SAB"]}";
        breakdown.breakdown_carisma      = $"{GlobalFichaData.carisma}      + {GlobalFichaData.bonusRacaDict["CAR"] + GlobalFichaData.bonusClasseDict["CAR"] + GlobalFichaData.bonusAntecedenteDict["CAR"] + GlobalFichaData.bonusTalentoDict["CAR"]}";

        fichaDisplayUI.AtualizarDadosCompletos(totais, breakdown);
        Debug.Log($"[TalentosSelector] ✓ FichaDisplayUI atualizado: FOR={forcaTotal}, DEX={destrezaTotal}");
    }

    private void CriarPasso_EscolherTalento(int nivel)
    {
        // Monta as opções agora (não antecipadamente), excluindo talentos já escolhidos
        // nesta sessão — assim, se o nível anterior escolheu "X", o próximo passo não
        // volta a oferecer "X" como opção.
        Dictionary<string, string> opcoes = new Dictionary<string, string>();
        foreach (var t in listaDeTalentos)
            if (!talentosEscolhidosNestaSessao.Contains(t.id_talento))
                opcoes.Add(t.id_talento, t.nome_talento);

        var prefab = mainStepContainerPrefab != null ? mainStepContainerPrefab : stepContainerPrefab;
        GameObject go = UIUtils.Spawn(prefab, mainScrollViewContent, false);
        go.SetActive(true);
        StepContainerUI stepUI = go.GetComponent<StepContainerUI>();

        Transform containerAtual = stepUI.transform;
        passoAtualIndex++;

        System.Action onDeselectTalento = () => RemoverPassosPosteriores(containerAtual.GetSiblingIndex());

        stepUI.SetupStep($"TALENTO (NÍVEL {nivel})", opcoes,
            (idSelecionado) => {
                var talento = listaDeTalentos.FirstOrDefault(t => t.id_talento == idSelecionado);
                if (talento != null) MostrarDetalhesDoTalento(talento.xml);
            },
            (idConfirmado) => {
                if (!talentosEscolhidosNestaSessao.Contains(idConfirmado))
                    talentosEscolhidosNestaSessao.Add(idConfirmado);

                // Remove este nível da lista de pendências assim que resolvido — evita que
                // o painel volte a oferecê-lo (ou duplique) se for reconstruído depois.
                NiveisPendentesParaTalento.Remove(nivel);

                var talento = listaDeTalentos.FirstOrDefault(t => t.id_talento == idConfirmado);

                // Persiste a escolha real no ClassSelector (substitui "TALENTO PENDENTE"),
                // para que ela sobreviva a navegações entre abas e a fechar/reabrir a ficha.
                ClassSelector classSelAtivo = FindObjectOfType<ClassSelector>(true);
                if (classSelAtivo != null && talento != null)
                    classSelAtivo.RegistrarEscolhaTalento(nivel, idConfirmado, talento.nome_talento);

                if (talento != null && !string.IsNullOrEmpty(talento.xml))
                    ProcessarXMLConfirmado(talento.xml, nivel);
                else
                    AvancarPasso();
            },
            true,
            onDeselectTalento
        );
        // Nota: não há mais auto-seleção de restauração aqui — um nível só chega a ter um
        // passo criado (ver CarregarTalentosEIniciar) se ainda estiver em
        // NiveisPendentesParaTalento, ou seja, ainda não resolvido. Re-auto-selecionar e
        // reprocessar um talento já confirmado reaplicaria seus modificadores em duplicidade.
    }

    private void RemoverPassosPosteriores(int indexoBase)
    {
        if (mainScrollViewContent == null) return;

        int totalChildren = mainScrollViewContent.childCount;
        for (int i = totalChildren - 1; i > indexoBase; i--)
            Destroy(mainScrollViewContent.GetChild(i).gameObject);

        filaDePassos.Clear();
        passoAtualIndex = indexoBase + 1;

        if (mainScrollViewContent.TryGetComponent<LayoutGroup>(out _))
            LayoutRebuilder.ForceRebuildLayoutImmediate(mainScrollViewContent as RectTransform);
    }

    private void MostrarDetalhesDoTalento(string xmlConteudo)
    {
        if (string.IsNullOrEmpty(xmlConteudo)) { if (infoText) infoText.text = "Sem dados disponíveis para este talento."; return; }

        try
        {
            XDocument doc = XDocument.Parse(xmlConteudo);
            string nome   = doc.Root?.Element("name")?.Value ?? "Talento";
            string desc   = doc.Root?.Element("description")?.Value ?? "Sem descrição.";
            string preReq = doc.Root?.Element("prerequisite")?.Value;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<size=150%><b>{nome.ToUpper()}</b></size>\n");
            if (!string.IsNullOrEmpty(preReq) && !preReq.Equals("nenhuma", StringComparison.OrdinalIgnoreCase) && !preReq.Equals("none", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine($"<color=#ffaa00><i>Pré-requisito: {preReq}</i></color>\n");
            sb.AppendLine($"<color=#cccccc>{desc}</color>\n");

            var benefits = doc.Root?.Element("benefits");
            if (benefits != null)
            {
                sb.AppendLine("<size=120%><b>Benefícios:</b></size>");
                foreach (var b in benefits.Elements("benefit")) sb.AppendLine($"• {b.Value}");
            }

            if (infoText) infoText.text = sb.ToString();
        }
        catch { if (infoText) infoText.text = "Erro na formatação dos detalhes do talento."; }
    }

    private void ProcessarXMLConfirmado(string xmlConteudo, int nivel)
    {
        try
        {
            XDocument doc = XDocument.Parse(xmlConteudo);

            // Aplica <metadata><modifiers> de forma centralizada
            XElement metadata = doc.Root?.Element("metadata");
            if (metadata != null)
                FichaModifierApplier.AplicarModificadores(metadata, doc.Root?.Element("name")?.Value ?? "talento");

            var skillChoices = doc.Descendants("skill-choices").FirstOrDefault();
            if (skillChoices != null && int.TryParse(skillChoices.Attribute("count")?.Value, out int countPericias))
            {
                for (int p = 1; p <= countPericias; p++)
                {
                    int atual = p;
                    filaDePassos.Enqueue(() => CriarPasso_ProficienciaTalento(xmlConteudo, atual, countPericias));
                }
            }

            // Schema legado (nenhum talento real usa isto hoje, mas mantido por compatibilidade
            // caso apareça em algum arquivo futuro).
            var asiNode = doc.Descendants("ability-improvement").FirstOrDefault();
            if (asiNode != null)
            {
                filaDePassos.Enqueue(() => CriarPasso_EscolhaASI_Talento(nivel, 1, 2, new Dictionary<string, int>()));
                if (infoText) infoText.text += "\n\n<color=#FFAA00><b>Nota:</b> Este talento concede +2 em atributos (+1/+1 ou +2 no mesmo).</color>";
            }

            // Schema real usado pelos talentos: <metadata><modifiers><modifier type="...">.
            // FichaModifierApplier já aplicou os tipos que não exigem escolha do jogador
            // (proficiency/skill/tool específicos, bonus, wealth, stat_increase de alvo único).
            // Aqui tratamos os que exigem uma escolha (ou que o applier genérico só loga).
            foreach (XElement mod in doc.Descendants("modifier"))
            {
                string tipoMod = mod.Attribute("type")?.Value?.ToLowerInvariant() ?? "";
                string alvoMod = mod.Attribute("target")?.Value ?? "";
                int.TryParse(mod.Attribute("value")?.Value, out int valorMod);

                if (tipoMod == "asi")
                {
                    int totalEscolhas = valorMod > 0 ? valorMod : 2;
                    filaDePassos.Enqueue(() => CriarPasso_EscolhaASI_Talento(nivel, 1, totalEscolhas, new Dictionary<string, int>()));
                    if (infoText) infoText.text += $"\n\n<color=#FFAA00><b>Nota:</b> Este talento concede +{totalEscolhas} em atributos (distribuível).</color>";
                }
                else if (tipoMod == "proficiency" && alvoMod.IndexOf("any", StringComparison.OrdinalIgnoreCase) >= 0
                          && alvoMod.IndexOf("skill", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    int totalEscolhas = valorMod > 0 ? valorMod : 1;
                    for (int p = 1; p <= totalEscolhas; p++)
                    {
                        int atual = p;
                        filaDePassos.Enqueue(() => CriarPasso_EscolhaPericiaLivre(atual, totalEscolhas));
                    }
                }
                else if (tipoMod == "stat_increase")
                {
                    List<string> candidatosEn = ExtrairCodigosAtributo(alvoMod);
                    // Alvo único e reconhecido já foi aplicado diretamente pelo FichaModifierApplier.
                    // Alvo composto ("STR_or_DEX", "INT_WIS_CHA") ou "any" precisa de escolha do jogador.
                    if (candidatosEn.Count != 1)
                    {
                        if (candidatosEn.Count == 0) candidatosEn = new List<string> { "STR", "DEX", "CON", "INT", "WIS", "CHA" };
                        int valor = valorMod > 0 ? valorMod : 1;
                        filaDePassos.Enqueue(() => CriarPasso_EscolhaStatIncrease(nivel, candidatosEn, valor));
                    }
                }
            }
        }
        catch (Exception e) { Debug.LogWarning("Erro ao processar XML: " + e.Message); }

        AvancarPasso();
    }

    // Extrai códigos de atributo em inglês (STR/DEX/CON/INT/WIS/CHA) presentes num alvo,
    // independentemente do separador usado (ex: "STR_or_DEX", "INT_WIS_CHA").
    private static List<string> ExtrairCodigosAtributo(string alvo)
    {
        var encontrados = new List<string>();
        if (string.IsNullOrWhiteSpace(alvo)) return encontrados;

        string alvoUpper = alvo.ToUpperInvariant();
        foreach (string codigo in new[] { "STR", "DEX", "CON", "INT", "WIS", "CHA" })
            if (alvoUpper.Contains(codigo)) encontrados.Add(codigo);

        return encontrados;
    }

    // Passo de escolha para modifier type="proficiency" target="any_skill_or_tool" — o talento
    // não restringe a uma lista fixa, então oferecemos as 18 perícias padrão de D&D 5e.
    private static readonly string[] PericiasPadrao =
    {
        "Acrobacia", "Lidar com Animais", "Arcanismo", "Atletismo", "Enganação", "História",
        "Intuição", "Intimidação", "Investigação", "Medicina", "Natureza", "Percepção",
        "Atuação", "Persuasão", "Religião", "Prestidigitação", "Furtividade", "Sobrevivência"
    };

    private void CriarPasso_EscolhaPericiaLivre(int atual, int total)
    {
        GameObject go = Instantiate(stepContainerPrefab, mainScrollViewContent, false);
        go.SetActive(true);
        StepContainerUI stepUI = go.GetComponent<StepContainerUI>();

        Dictionary<string, string> opcoes = new Dictionary<string, string>();
        foreach (string pericia in PericiasPadrao)
            if (!ProficiencySelector.PericiasClasseLocal.Contains(pericia))
                opcoes.Add(pericia, pericia);

        stepUI.SetupStep($"TALENTO - PROFICIÊNCIA LIVRE ({atual}/{total})", opcoes,
            (id) => { if (infoText) infoText.text = $"<size=150%><b>{id}</b></size>\n\nO talento te tornou proficiente nesta perícia."; },
            (idConfirmado) => {
                ProficiencySelector.PericiasClasseLocal.Add(idConfirmado);
                ProficiencySelector.OnImportacaoSolicitada?.Invoke();
                AvancarPasso();
            },
            true
        );
    }

    // Passo de escolha para modifier type="stat_increase" com alvo composto ("STR_or_DEX") ou "any".
    private void CriarPasso_EscolhaStatIncrease(int nivel, List<string> candidatosEn, int valor)
    {
        GameObject go = Instantiate(stepContainerPrefab, mainScrollViewContent, false);
        go.SetActive(true);
        StepContainerUI stepUI = go.GetComponent<StepContainerUI>();

        Dictionary<string, string> opcoes = new Dictionary<string, string>();
        foreach (string codigoEn in candidatosEn)
        {
            if (FichaModifierApplier.AtributoMap.TryGetValue(codigoEn, out string codigoPt) && atributosLabelASI.ContainsKey(codigoPt))
                opcoes[codigoPt] = atributosLabelASI[codigoPt];
        }

        stepUI.SetupStep($"TALENTO (NÍVEL {nivel}) - ESCOLHA DE ATRIBUTO (+{valor})", opcoes,
            (id) => { if (infoText && atributosLabelASI.ContainsKey(id)) infoText.text = $"<b>{atributosLabelASI[id]}</b> receberá +{valor}."; },
            (idConfirmado) => {
                GlobalFichaData.bonusTalentoDict[idConfirmado] += valor;
                AvancarPasso();
            },
            true
        );
    }

    private void CriarPasso_EscolhaASI_Talento(int nivel, int escolhaAtual, int escolhasTotais, Dictionary<string, int> deltaParcial)
    {
        GameObject go = Instantiate(stepContainerPrefab, mainScrollViewContent, false);
        go.SetActive(true);
        StepContainerUI stepUI = go.GetComponent<StepContainerUI>();

        Dictionary<string, string> opcoes = new Dictionary<string, string>();
        foreach (var attr in atributosOrdemASI) opcoes[attr] = atributosLabelASI[attr];

        stepUI.SetupStep($"ASI DO TALENTO (NÍVEL {nivel}) ({escolhaAtual}/{escolhasTotais})", opcoes,
            (id) => { if (infoText && atributosLabelASI.ContainsKey(id)) infoText.text = $"<b>{atributosLabelASI[id]}</b> receberá +1 neste passo."; },
            (idConfirmado) => {
                if (!deltaParcial.ContainsKey(idConfirmado)) deltaParcial[idConfirmado] = 0;
                deltaParcial[idConfirmado] += 1;
                if (escolhaAtual < escolhasTotais)
                {
                    CriarPasso_EscolhaASI_Talento(nivel, escolhaAtual + 1, escolhasTotais, new Dictionary<string, int>(deltaParcial));
                    return;
                }
                AttributeManager.AplicarDeltaASI(deltaParcial);
                AvancarPasso();
            },
            true
        );
    }

    private void CriarPasso_ProficienciaTalento(string xmlTalento, int atual, int total)
    {
        XDocument doc = XDocument.Parse(xmlTalento);
        var skillChoices = doc.Descendants("skill-choices").FirstOrDefault();

        GameObject go = Instantiate(stepContainerPrefab, mainScrollViewContent, false);
        go.SetActive(true);
        StepContainerUI stepUI = go.GetComponent<StepContainerUI>();

        Dictionary<string, string> opcoes = new Dictionary<string, string>();
        foreach (var opt in skillChoices.Elements("option"))
            if (!ProficiencySelector.PericiasClasseLocal.Contains(opt.Value))
                opcoes.Add(opt.Value, opt.Value);

        stepUI.SetupStep($"PERÍCIA DO TALENTO ({atual}/{total})", opcoes,
            (id) => { if (infoText) infoText.text = $"<size=150%><b>{id}</b></size>\n\nO talento te tornou proficiente nisto."; },
            (idConfirmado) => {
                ProficiencySelector.PericiasClasseLocal.Add(idConfirmado);
                ProficiencySelector.OnImportacaoSolicitada?.Invoke();
                AvancarPasso();
            },
            true
        );
    }
}
