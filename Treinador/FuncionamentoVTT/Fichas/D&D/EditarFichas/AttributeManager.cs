using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AttributeManager : MonoBehaviour
{
    [Header("UI References")]
    public Button rolarTodosButton;
    public TMP_Text feedbackText;

    [Header("Prefab de Atributo - Rolagem")]
    public GameObject attributeItemPrefabRolagem;
    public Transform attributeItemContainerRolagem;

    [Header("Prefab de Atributo - Pontuação")]
    public GameObject attributeItemPrefabPontuacao;
    public Transform attributeItemContainerPontuacao;

    [Header("Prefab de Atributo - Valores Fixos")]
    public GameObject attributeItemPrefabValoresFixos;
    public Transform attributeItemContainerValoresFixos;

    [Header("Prefab de Texto de Bônus")]
    public GameObject bonusTextPrefab;
    public Transform bonusTextContainer;

    [Header("Painéis de Método")]
    public GameObject painelRolagem;
    public GameObject painelValoresFixos;
    public GameObject painelPontuacao;

    [Header("Texto de Informação do Método")]
    public TMP_Text infoTexto;

    [Header("Point Buy")]
    public TMP_Text pontosRestantesText;

    [Header("Referências")]
    public FichaDisplayUI fichaDisplayUI;

    private readonly List<string> nomesOficiais = new List<string> { "FOR", "DEX", "CON", "INT", "SAB", "CAR" };
    private static readonly int[] valoresFixos = { 15, 14, 13, 12, 10, 8 };

    private static readonly Dictionary<int, int> custoPointBuy = new Dictionary<int, int>
    {
        { 8, 0 }, { 9, 1 }, { 10, 2 }, { 11, 3 }, { 12, 4 }, { 13, 5 }, { 14, 7 }, { 15, 9 }
    };
    private const int TotalPontosPointBuy = 27;
    private bool modoPointBuy = false;

    private readonly Dictionary<string, string> nomesCompletos = new Dictionary<string, string>
    {
        { "FOR", "Força" }, { "DEX", "Destreza" }, { "CON", "Constituição" },
        { "INT", "Inteligência" }, { "SAB", "Sabedoria" }, { "CAR", "Carisma" }
    };

    private Dictionary<string, int> atributosAtuais = new Dictionary<string, int>();
    private Dictionary<string, int> bonusRaça = new Dictionary<string, int>();
    private Dictionary<string, int> bonusClasse = new Dictionary<string, int>();
    private Dictionary<string, int> bonusItens = new Dictionary<string, int>();
    private Dictionary<string, int> bonusTalentos = new Dictionary<string, int>();
    private Dictionary<string, int> bonusAntecedentes = new Dictionary<string, int>();

    private Dictionary<string, AttributeItemUI> atributoUIMapRolagem    = new Dictionary<string, AttributeItemUI>();
    private Dictionary<string, AttributeItemUI> atributoUIMapPontuacao  = new Dictionary<string, AttributeItemUI>();
    private Dictionary<string, AttributeItemUI> atributoUIMapValoresFixos = new Dictionary<string, AttributeItemUI>();
    private Dictionary<string, AttributeItemUI> atributoUIMap           = new Dictionary<string, AttributeItemUI>();
    private Dictionary<string, int> escolhasValoresFixos = new Dictionary<string, int>();

    public static Action<int> OnModificadorConstituicaoMudou;
    public static Action<Dictionary<string, int>> OnAtributosGlobaisMudaram;

    private static readonly List<Dictionary<string, int>> deltasASIPendentes = new List<Dictionary<string, int>>();
    private static AttributeManager instanciaAtiva;

    public static int CurrentConModifier = 0;
    public static int HitDieMaximo = 0;

    private bool inicializado = false;
    private int idDaFichaCarregadaNoBanco = -1;
    private Coroutine feedbackCoroutine;

    void Awake()
    {
        instanciaAtiva = this;
        InicializarAtributos();
    }

    void OnDestroy()
    {
        if (instanciaAtiva == this) instanciaAtiva = null;
    }

    void OnEnable()
    {
        GlobalFichaData.OnSessaoDeFichaIniciada += OnSessaoDeFichaIniciada;

        if (!inicializado) InicializarAtributos();

        // A assinatura acima só existe a partir de AGORA — se GlobalFichaData.IniciarSessaoParaFicha
        // disparou OnSessaoDeFichaIniciada ENQUANTO este objeto estava inativo (ex: criar ficha nova
        // a partir da biblioteca: o reset roda ANTES da tela de criação reativar), o evento passou
        // batido e o reset abaixo nunca aconteceu. Sem esta checagem, atributosAtuais continuava com
        // os valores da ficha ANTERIOR, e CarregarOuAtualizarParaFichaAtual() os salvava por cima da
        // ficha nova (bug real, confirmado em log: FOR=22/DEX=18 de uma ficha vazando pra a próxima
        // ficha recém-criada). Comparar idDaFichaCarregadaNoBanco aqui não depende de nenhum evento
        // ter sido pego a tempo — é só um comparação de estado, sempre correta.
        if (idDaFichaCarregadaNoBanco != GlobalFichaData.fichasDndId)
        {
            ResetarParaDefaults();
        }

        CarregarOuAtualizarParaFichaAtual();
    }

    /// <summary>
    /// Gatilho independente de OnEnable/Start: alguns passos (ex: ClassSelector) passaram a usar
    /// GameObjects sempre ativos pra evitar um bug de CanvasGroup/layout preso após
    /// SetActive(false)-&gt;(true) — o que também significa que OnEnable nunca mais dispara de novo
    /// pra eles. Sem isto, os bônus de raça/classe/antecedente da ficha ANTERIOR ficavam presos em
    /// memória (bonusRaça/bonusClasse/etc só são zerados quando o seletor correspondente chama
    /// AtualizarBonus*, o que não acontecia mais numa ficha nova se o seletor também não notasse a
    /// troca). GlobalFichaData.OnSessaoDeFichaIniciada dispara em TODA troca de ficha (nova, editar,
    /// trocar), independente do ciclo de vida de qualquer GameObject.
    /// </summary>
    private void OnSessaoDeFichaIniciada()
    {
        if (!inicializado) InicializarAtributos();
        ResetarParaDefaults();
        CarregarOuAtualizarParaFichaAtual();
    }

    private void ResetarParaDefaults()
    {
        foreach (string atributo in nomesOficiais)
        {
            atributosAtuais[atributo]    = 10;
            bonusRaça[atributo]          = 0;
            bonusClasse[atributo]        = 0;
            bonusItens[atributo]         = 0;
            bonusTalentos[atributo]      = 0;
            bonusAntecedentes[atributo]  = 0;
        }
        idDaFichaCarregadaNoBanco = -1;
    }

    private void CarregarOuAtualizarParaFichaAtual()
    {
        if (GlobalFichaData.fichasDndId > 0)
        {
            if (idDaFichaCarregadaNoBanco != GlobalFichaData.fichasDndId)
            {
                int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
                string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

                if (LocalFichaStorage.TryLoadAttributes(
                    steamId,
                    playerId,
                    GlobalFichaData.fichasGeneralId,
                    GlobalFichaData.fichasDndId,
                    out int forca,
                    out int destreza,
                    out int constituicao,
                    out int inteligencia,
                    out int sabedoria,
                    out int carisma))
                {
                    bool valoresValidos = !(forca == 10 && destreza == 10 && constituicao == 10 &&
                                          inteligencia == 10 && sabedoria == 10 && carisma == 10);

                    if (valoresValidos)
                    {
                        atributosAtuais["FOR"] = forca;
                        atributosAtuais["DEX"] = destreza;
                        atributosAtuais["CON"] = constituicao;
                        atributosAtuais["INT"] = inteligencia;
                        atributosAtuais["SAB"] = sabedoria;
                        atributosAtuais["CAR"] = carisma;
                        idDaFichaCarregadaNoBanco = GlobalFichaData.fichasDndId;
                        VTTLog.Sheet($"âœ“ Atributos restaurados de LocalFichaStorage: FOR={forca}, DEX={destreza}, CON={constituicao}, INT={inteligencia}, SAB={sabedoria}, CAR={carisma}");
                        SincronizarAtributosGlobais();
                    }
                    else
                    {
                        VTTLog.SheetWarn("Valores carregados são todos 10 (padrão vazio), ignorando");
                        idDaFichaCarregadaNoBanco = GlobalFichaData.fichasDndId;
                    }
                }
                else
                {
                    if (GlobalFichaData.forca > 0 || GlobalFichaData.destreza > 0 || GlobalFichaData.constituicao > 0 || GlobalFichaData.inteligencia > 0 || GlobalFichaData.sabedoria > 0 || GlobalFichaData.carisma > 0)
                    {
                        atributosAtuais["FOR"] = GlobalFichaData.forca > 0 ? GlobalFichaData.forca : atributosAtuais["FOR"];
                        atributosAtuais["DEX"] = GlobalFichaData.destreza > 0 ? GlobalFichaData.destreza : atributosAtuais["DEX"];
                        atributosAtuais["CON"] = GlobalFichaData.constituicao > 0 ? GlobalFichaData.constituicao : atributosAtuais["CON"];
                        atributosAtuais["INT"] = GlobalFichaData.inteligencia > 0 ? GlobalFichaData.inteligencia : atributosAtuais["INT"];
                        atributosAtuais["SAB"] = GlobalFichaData.sabedoria > 0 ? GlobalFichaData.sabedoria : atributosAtuais["SAB"];
                        atributosAtuais["CAR"] = GlobalFichaData.carisma > 0 ? GlobalFichaData.carisma : atributosAtuais["CAR"];
                        VTTLog.Sheet("Usando valores de GlobalFichaData");
                    }
                    idDaFichaCarregadaNoBanco = GlobalFichaData.fichasDndId;
                }

                AtualizarTodosAtributosUI();
                ProcessarDeltasASIPendentes();
            }
            else
            {
                AtualizarTodosAtributosUI();
                ProcessarDeltasASIPendentes();
            }
        }
        else
        {
            AtualizarTodosAtributosUI();
            ProcessarDeltasASIPendentes();
        }

        AtivarPainelMetodo(GlobalFichaData.metodoAtributos ?? string.Empty);
    }

    void OnDisable()
    {
        GlobalFichaData.OnSessaoDeFichaIniciada -= OnSessaoDeFichaIniciada;

        SincronizarAtributosGlobais();

        if (GlobalFichaData.fichasDndId > 0)
        {
            int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
            string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

            bool ok = LocalFichaStorage.SaveAttributes(
                steamId,
                playerId,
                GlobalFichaData.fichasGeneralId,
                GlobalFichaData.fichasDndId,
                atributosAtuais["FOR"],
                atributosAtuais["DEX"],
                atributosAtuais["CON"],
                atributosAtuais["INT"],
                atributosAtuais["SAB"],
                atributosAtuais["CAR"]
            );

            if (ok)
            {
                VTTLog.Save($"Atributos salvos ao desativar: FOR={atributosAtuais["FOR"]}, DEX={atributosAtuais["DEX"]}, CON={atributosAtuais["CON"]}, INT={atributosAtuais["INT"]}, SAB={atributosAtuais["SAB"]}, CAR={atributosAtuais["CAR"]}");
            }
        }
    }

    public static void AplicarDeltaASI(Dictionary<string, int> deltaMatematico)
    {
        if (deltaMatematico == null || deltaMatematico.Count == 0) return;

        deltasASIPendentes.Add(new Dictionary<string, int>(deltaMatematico));

        if (instanciaAtiva != null && instanciaAtiva.isActiveAndEnabled)
        {
            instanciaAtiva.ProcessarDeltasASIPendentes();
        }
    }

    private void ProcessarDeltasASIPendentes()
    {
        if (deltasASIPendentes.Count == 0) return;

        List<Dictionary<string, int>> pendentes = new List<Dictionary<string, int>>(deltasASIPendentes);
        deltasASIPendentes.Clear();

        foreach (var delta in pendentes)
        {
            AplicarDeltaASINoEstado(delta);
        }

        AtualizarTodosAtributosUI();
        if (gameObject.activeInHierarchy) StartCoroutine(SalvarAtributosPublico());
    }

    private void AplicarDeltaASINoEstado(Dictionary<string, int> deltaMatematico)
    {
        foreach (var kvp in deltaMatematico)
        {
            string atributo = kvp.Key;
            int mudanca = kvp.Value;
            if (!atributosAtuais.ContainsKey(atributo)) continue;

            int novoValor = atributosAtuais[atributo] + mudanca;
            atributosAtuais[atributo] = Mathf.Clamp(novoValor, 1, 30);
        }
    }

    private void InicializarAtributos()
    {
        if (inicializado) return;

        int[] defaultsFixos = valoresFixos;
        for (int i = 0; i < nomesOficiais.Count; i++)
        {
            string atributo = nomesOficiais[i];
            if (!atributosAtuais.ContainsKey(atributo)) atributosAtuais[atributo] = 10;
            bonusRaça[atributo] = 0; bonusClasse[atributo] = 0; bonusItens[atributo] = 0;
            bonusTalentos[atributo] = 0; bonusAntecedentes[atributo] = 0;
            escolhasValoresFixos[atributo] = i < defaultsFixos.Length ? defaultsFixos[i] : 10;
        }

        if (rolarTodosButton != null) rolarTodosButton.onClick.AddListener(OnRolarTodosAtributos);

        CriarItensAtributo(attributeItemPrefabRolagem,     attributeItemContainerRolagem,     atributoUIMapRolagem);
        CriarItensAtributo(attributeItemPrefabPontuacao,   attributeItemContainerPontuacao,   atributoUIMapPontuacao);
        CriarItensAtributo(attributeItemPrefabValoresFixos, attributeItemContainerValoresFixos, atributoUIMapValoresFixos);

        atributoUIMap = atributoUIMapRolagem;

        inicializado = true;
        AtualizarTodosAtributosUI();
    }

    private void CriarItensAtributo(GameObject prefab, Transform container, Dictionary<string, AttributeItemUI> mapa)
    {
        if (prefab == null || container == null) return;

        foreach (Transform child in container) Destroy(child.gameObject);
        mapa.Clear();

        foreach (string attr in nomesOficiais)
        {
            string attrCapturado = attr;
            GameObject go = UIUtils.Spawn(prefab, container, false);
            AttributeItemUI item = go.GetComponent<AttributeItemUI>();
            if (item != null)
            {
                item.Setup(attr, nomesCompletos[attr], (change) => OnAlterar(attrCapturado, change));
                mapa[attr] = item;
            }
        }
    }

    private void OnAlterar(string nomeAtributo, int change)
    {
        if (!atributosAtuais.ContainsKey(nomeAtributo)) return;
        int valorAtual = atributosAtuais[nomeAtributo];
        int novoValor = valorAtual + change;

        if (modoPointBuy)
        {
            if (novoValor < 8 || novoValor > 15) return;
            if (!custoPointBuy.ContainsKey(novoValor)) return;

            int custoAtual = custoPointBuy[valorAtual];
            int custoNovo  = custoPointBuy[novoValor];
            int pontosUsados = GetPontosGastos() - custoAtual + custoNovo;
            if (pontosUsados > TotalPontosPointBuy) return;
        }
        else
        {
            if (novoValor < 1 || novoValor > 30) return;
        }

        atributosAtuais[nomeAtributo] = novoValor;
        AtualizarTodosAtributosUI();
    }

    private int GetCustoPontuacao(int valor)
    {
        return custoPointBuy.TryGetValue(valor, out int custo) ? custo : 0;
    }

    private int GetPontosGastos()
    {
        int total = 0;
        foreach (string attr in nomesOficiais)
            total += GetCustoPontuacao(atributosAtuais[attr]);
        return total;
    }

    private void InicializarModoPointBuy()
    {
        foreach (string attr in nomesOficiais)
            atributosAtuais[attr] = 8;
        AtualizarTodosAtributosUI();
    }

    private Color _corOriginalPontos = Color.black;
    private bool _corOriginalPontosCapturada = false;

    private void AtualizarExibicaoPontos()
    {
        if (pontosRestantesText == null) return;

        if (!_corOriginalPontosCapturada)
        {
            _corOriginalPontos = pontosRestantesText.color;
            _corOriginalPontosCapturada = true;
        }

        int restantes = TotalPontosPointBuy - GetPontosGastos();
        pontosRestantesText.text  = $"Pontos: {restantes}";
        pontosRestantesText.color = restantes < 0 ? Color.red
                                  : restantes == 0 ? Color.green
                                  : _corOriginalPontos;
    }

    private void OnRolarTodosAtributos()
    {
        string metodo = (GlobalFichaData.metodoAtributos ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(metodo))
        {
            metodo = "Roll 4d6 droplowest";
        }

        string metodoNormalizado = metodo.ToLowerInvariant();
        bool usaPontos = metodoNormalizado.Contains("pont");
        bool usa3d6 = metodoNormalizado.Contains("3d6");

        if (usaPontos) { MostrarFeedback("Método Pontos: use os botões +/-", false); return; }
        foreach (string attr in nomesOficiais) atributosAtuais[attr] = usa3d6 ? Rolagem3d6() : Rolagem4d6DropLowest();
        AtualizarTodosAtributosUI();
        MostrarFeedback("Todos atributos rolados!", false);
    }

    public void AtualizarTodosAtributosUI()
    {
        if (!inicializado) InicializarAtributos();

        Dictionary<string, int> modificadoresAoVivo = new Dictionary<string, int>();

        foreach (string nome in nomesOficiais)
        {
            if (!atributosAtuais.ContainsKey(nome)) continue;

            int totalComBonus = GetTotal(nome);
            int modificador = CalcularModificador(totalComBonus);
            modificadoresAoVivo[nome] = modificador;

            bool temBonusRaca = bonusRaça[nome] != 0;

            if (atributoUIMap.TryGetValue(nome, out AttributeItemUI item))
            {
                item.AtualizarValores(totalComBonus, temBonusRaca);
                item.AtualizarTotal(totalComBonus);
            }

            if (nome == "CON")
            {
                CurrentConModifier = modificador;
                OnModificadorConstituicaoMudou?.Invoke(modificador);
            }
        }

        SincronizarAtributosGlobais();
        SalvarAtributosNoLocalStorage();
        OnAtributosGlobaisMudaram?.Invoke(modificadoresAoVivo);
        CalcularEAtualizarVida();
        AtualizarFichaDisplay();
        AtualizarListaBonusUI();
        if (modoPointBuy) AtualizarExibicaoPontos();
    }

    private void SincronizarAtributosGlobais()
    {
        GlobalFichaData.forca = atributosAtuais["FOR"];
        GlobalFichaData.destreza = atributosAtuais["DEX"];
        GlobalFichaData.constituicao = atributosAtuais["CON"];
        GlobalFichaData.inteligencia = atributosAtuais["INT"];
        GlobalFichaData.sabedoria = atributosAtuais["SAB"];
        GlobalFichaData.carisma = atributosAtuais["CAR"];
    }

    private string GerarTextoSomaDetalhada(string attr)
    {
        int _base = atributosAtuais[attr];
        int _raca = bonusRaça[attr];
        int _classe = bonusClasse[attr];
        int _antec = bonusAntecedentes[attr];
        int _talento = bonusTalentos[attr];
        int _item = bonusItens[attr];

        List<string> somaMatematica = new List<string>();
        somaMatematica.Add(_base.ToString());
        if (_raca != 0) somaMatematica.Add($"+{_raca}");
        if (_classe != 0) somaMatematica.Add($"+{_classe}");
        if (_antec != 0) somaMatematica.Add($"+{_antec}");
        if (_talento != 0) somaMatematica.Add($"+{_talento}");
        if (_item != 0) somaMatematica.Add($"+{_item}");

        return string.Join("", somaMatematica);
    }

    public void CalcularEAtualizarVida()
    {
        if (HitDieMaximo == 0) return;
        int playerLevel = GlobalFichaData.levelPersonagem > 0 ? GlobalFichaData.levelPersonagem : 1;
        int valorMedio = (HitDieMaximo / 2) + 1;
        int vidaCalculada = HitDieMaximo + CurrentConModifier;
        if (playerLevel > 1) vidaCalculada += (playerLevel - 1) * (valorMedio + CurrentConModifier);
        GlobalFichaData.vidaPersonagem = vidaCalculada.ToString();
        if (fichaDisplayUI != null) fichaDisplayUI.AtualizarVidaLocal();
    }

    private int GetTotal(string nome) { return atributosAtuais[nome] + bonusRaça[nome] + bonusClasse[nome] + bonusAntecedentes[nome] + bonusTalentos[nome] + bonusItens[nome]; }

    public int ObtainTotalAtributo(string nomeAtributo)
    {
        if (!atributosAtuais.ContainsKey(nomeAtributo))
            return 0;
        return GetTotal(nomeAtributo);
    }

    public string ObtainResumoAtributosComBonus()
    {
        return string.Format("{0} FOR, {1} DEX, {2} CON, {3} INT, {4} SAB, {5} CAR",
            GetTotal("FOR"), GetTotal("DEX"), GetTotal("CON"),
            GetTotal("INT"), GetTotal("SAB"), GetTotal("CAR"));
    }

    private void AtualizarFichaDisplay()
    {
        if (fichaDisplayUI == null) return;
        FichaDisplayUI.AtributosResponse totais = new FichaDisplayUI.AtributosResponse();
        totais.atributos.forca = GetTotal("FOR"); totais.atributos.destreza = GetTotal("DEX"); totais.atributos.constituicao = GetTotal("CON");
        totais.atributos.inteligencia = GetTotal("INT"); totais.atributos.sabedoria = GetTotal("SAB"); totais.atributos.carisma = GetTotal("CAR");

        FichaDisplayUI.BonusBreakdownData breakdown = new FichaDisplayUI.BonusBreakdownData();
        breakdown.breakdown_forca = GerarTextoSomaDetalhada("FOR"); breakdown.breakdown_destreza = GerarTextoSomaDetalhada("DEX"); breakdown.breakdown_constituicao = GerarTextoSomaDetalhada("CON");
        breakdown.breakdown_inteligencia = GerarTextoSomaDetalhada("INT"); breakdown.breakdown_sabedoria = GerarTextoSomaDetalhada("SAB"); breakdown.breakdown_carisma = GerarTextoSomaDetalhada("CAR");
        fichaDisplayUI.AtualizarDadosCompletos(totais, breakdown);
    }

    private IEnumerator CarregarAtributosExistentes()
    {
        if (GlobalFichaData.fichasDndId <= 0) yield break;

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        if (LocalFichaStorage.TryLoadAttributes(
            steamId,
            playerId,
            GlobalFichaData.fichasGeneralId,
            GlobalFichaData.fichasDndId,
            out int forca,
            out int destreza,
            out int constituicao,
            out int inteligencia,
            out int sabedoria,
            out int carisma))
        {
            atributosAtuais["FOR"] = forca;
            atributosAtuais["DEX"] = destreza;
            atributosAtuais["CON"] = constituicao;
            atributosAtuais["INT"] = inteligencia;
            atributosAtuais["SAB"] = sabedoria;
            atributosAtuais["CAR"] = carisma;
            idDaFichaCarregadaNoBanco = GlobalFichaData.fichasDndId;

            Debug.Log($"[AttributeManager] Atributos carregados do snapshot: FOR={forca}, DEX={destreza}, CON={constituicao}, INT={inteligencia}, SAB={sabedoria}, CAR={carisma}");

            AtualizarTodosAtributosUI();
        }

        ProcessarDeltasASIPendentes();

        yield return null;
    }

    public IEnumerator SalvarAtributosPublico()
    {
        int forca = atributosAtuais["FOR"];
        int destreza = atributosAtuais["DEX"];
        int constituicao = atributosAtuais["CON"];
        int inteligencia = atributosAtuais["INT"];
        int sabedoria = atributosAtuais["SAB"];
        int carisma = atributosAtuais["CAR"];

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        bool ok = LocalFichaStorage.SaveAttributes(
            steamId,
            playerId,
            GlobalFichaData.fichasGeneralId,
            GlobalFichaData.fichasDndId,
            forca, destreza, constituicao, inteligencia, sabedoria, carisma
        );

        if (ok)
        {
            idDaFichaCarregadaNoBanco = GlobalFichaData.fichasDndId;
            if (fichaDisplayUI && gameObject.activeInHierarchy) fichaDisplayUI.AtualizarTudo();
        }

        yield return null;
    }

    private int Rolagem4d6DropLowest() { List<int> d = new List<int>(); for(int i=0;i<4;i++) d.Add(UnityEngine.Random.Range(1,7)); d.Sort(); d.RemoveAt(0); int t=0; foreach(int v in d) t+=v; return t; }
    private int Rolagem3d6() { int t=0; for(int i=0;i<3;i++) t+=UnityEngine.Random.Range(1,7); return t; }
    private int CalcularModificador(int v) { return Mathf.FloorToInt((v - 10) / 2.0f); }

    private void MostrarFeedback(string msg, bool erro)
    {
        if (feedbackText == null)
        {
            if (!string.IsNullOrEmpty(msg)) Debug.Log(msg);
            return;
        }

        feedbackText.text = msg;
        feedbackText.color = erro ? Color.red : Color.green;
        feedbackText.gameObject.SetActive(true);
        if (feedbackCoroutine != null) StopCoroutine(feedbackCoroutine);
        feedbackCoroutine = StartCoroutine(EsconderFeedback(3f));
    }

    private IEnumerator EsconderFeedback(float t)
    {
        yield return new WaitForSeconds(t);
        if (feedbackText != null) feedbackText.gameObject.SetActive(false);
    }

    /// <summary>
    /// Garante que os atributos-base da ficha ATUAL já foram carregados do disco antes de qualquer
    /// coisa que dependa deles (bônus de raça/classe/antecedente). Sem isto, se algo chamar
    /// AtualizarBonus* ANTES do OnEnable/OnSessaoDeFichaIniciada deste componente ter rodado pra
    /// ESTA ficha — confirmado em log real que RaceSelector chama AtualizarBonusRaça durante o
    /// MESMO SetActive(true) que ativa a tela de criação inteira, e a ordem de ativação entre
    /// GameObjects irmãos (RaceSelector vs este AttributeManager, agora fixo fora da sequência de
    /// etapas) não é garantida pelo Unity — InicializarAtributos() cai no "primeiro toque
    /// inicializa em 10" e os valores REAIS salvos da ficha nunca chegam a ser carregados: o
    /// jogador via a ficha inteira em atributos-padrão mesmo já tendo atributos salvos em disco.
    /// </summary>
    private void GarantirCarregadoParaFichaAtual()
    {
        if (!inicializado) InicializarAtributos();
        if (GlobalFichaData.fichasDndId > 0 && idDaFichaCarregadaNoBanco != GlobalFichaData.fichasDndId)
            CarregarOuAtualizarParaFichaAtual();
    }

    public void AtualizarBonusRaça(Dictionary<string, int> b)
    {
        GarantirCarregadoParaFichaAtual();
        foreach(var key in new List<string>(bonusRaça.Keys)) bonusRaça[key] = 0;
        foreach(var k in b) if (bonusRaça.ContainsKey(k.Key)) bonusRaça[k.Key] = k.Value;
        AtualizarTodosAtributosUI();
        SalvarAtributosNoLocalStorage();
    }

    public void AtualizarBonusClasse(Dictionary<string, int> b)
    {
        GarantirCarregadoParaFichaAtual();
        foreach(var key in new List<string>(bonusClasse.Keys)) bonusClasse[key] = 0;
        foreach(var k in b) if (bonusClasse.ContainsKey(k.Key)) bonusClasse[k.Key] = k.Value;
        AtualizarTodosAtributosUI();
        SalvarAtributosNoLocalStorage();
    }

    public void AtualizarBonusAntecedente(Dictionary<string, int> b)
    {
        GarantirCarregadoParaFichaAtual();
        foreach(var key in new List<string>(bonusAntecedentes.Keys)) bonusAntecedentes[key] = 0;
        foreach(var k in b) if (bonusAntecedentes.ContainsKey(k.Key)) bonusAntecedentes[k.Key] = k.Value;
        AtualizarTodosAtributosUI();
        SalvarAtributosNoLocalStorage();
    }

    private void AtualizarListaBonusUI()
    {
        if (bonusTextPrefab == null || bonusTextContainer == null) return;

        foreach (Transform child in bonusTextContainer) Destroy(child.gameObject);

        foreach (string attr in nomesOficiais)
        {
            bonusRaça.TryGetValue(attr, out int valorRaca);
            GlobalFichaData.bonusClasseDict.TryGetValue(attr, out int valorClasse);

            if (valorRaca != 0) CriarEntradaBonus(attr, valorRaca);
            if (valorClasse != 0) CriarEntradaBonus(attr, valorClasse);
        }
    }

    private void CriarEntradaBonus(string attr, int valor)
    {
        GameObject go = Instantiate(bonusTextPrefab, bonusTextContainer, false);
        TMP_Text txt = go.GetComponent<TMP_Text>();
        if (txt == null) return;
        string sinal = valor > 0 ? "+" : "";
        txt.text = $"Atributo {sinal}{valor}: {nomesCompletos[attr]}";
    }

    public void AtivarPainelMetodo(string metodo)
    {
        string m = (metodo ?? string.Empty).ToLowerInvariant().Trim();
        bool ehFixo = m.Contains("fix") || m.Contains("standard") || m.Contains("padr");
        bool ehPontos = m.Contains("pont") || m.Contains("point");
        bool usa3d6 = m.Contains("3d6");

        if (painelRolagem != null) painelRolagem.SetActive(!ehFixo && !ehPontos);
        if (painelValoresFixos != null) painelValoresFixos.SetActive(ehFixo);
        if (painelPontuacao != null) painelPontuacao.SetActive(ehPontos);

        if (infoTexto != null)
        {
            if (ehFixo)
                infoTexto.text =
                    "<b>Padrões</b>\n\n" +
                    "Distribua livremente os seis valores abaixo entre os seus atributos:\n\n" +
                    "    <b>15 · 14 · 13 · 12 · 10 · 8</b>\n\n" +
                    "• Cada valor só pode ser atribuído a <b>um único atributo</b>\n" +
                    "• Use os menus de cada atributo para escolher qual valor ele recebe\n" +
                    "• Bônus de raça, classe e antecedente são somados ao valor escolhido\n\n" +
                    "Método recomendado para quem quer equilíbrio e previsibilidade " +
                    "sem depender da sorte nos dados.";
            else if (ehPontos)
                infoTexto.text =
                    "<b>Pontuação</b>\n\n" +
                    "Você tem <b>27 pontos</b> para distribuir. Todos os atributos começam em <b>8</b>. " +
                    "Cada aumento custa pontos conforme a tabela abaixo:\n\n" +
                    "  Valor  │  Custo acumulado\n" +
                    "  ───────┼─────────────────\n" +
                    "    8    │       0 pts\n" +
                    "    9    │       1 pt\n" +
                    "   10    │       2 pts\n" +
                    "   11    │       3 pts\n" +
                    "   12    │       4 pts\n" +
                    "   13    │       5 pts\n" +
                    "   14    │       7 pts\n" +
                    "   15    │       9 pts\n\n" +
                    "• Nenhum atributo pode ir abaixo de <b>8</b> ou acima de <b>15</b> antes dos bônus\n" +
                    "• Bônus de raça, classe e antecedente são somados após a compra\n\n" +
                    "Use os botões <b>+</b> e <b>−</b> de cada atributo. O total de pontos gastos " +
                    "é exibido em tempo real.";
            else if (usa3d6)
                infoTexto.text =
                    "<b>3d6</b>\n\n" +
                    "Para cada atributo, role <b>3 dados de 6 faces</b> e some os resultados.\n\n" +
                    "• Resultado possível: 3 a 18\n" +
                    "• Método clássico, com mais aleatoriedade\n" +
                    "• Personagens tendem a ter atributos mais modestos\n\n" +
                    "Clique em <b>Rolar Todos</b> para gerar os 6 atributos de uma vez, " +
                    "ou clique no dado individual de cada atributo para rolar separadamente.";
            else
                infoTexto.text =
                    "<b>4d6</b>\n\n" +
                    "Para cada atributo, role <b>4 dados de 6 faces</b>, descarte o resultado mais baixo " +
                    "e some os 3 dados restantes.\n\n" +
                    "• Resultado possível: 3 a 18 (média ~12,24)\n" +
                    "• Método padrão do D&D 5ª Edição\n" +
                    "• Favorece resultados mais altos que o 3d6\n\n" +
                    "Clique em <b>Rolar Todos</b> para gerar os 6 atributos de uma vez, " +
                    "ou clique no dado individual de cada atributo para rolar separadamente.";
        }

        modoPointBuy = ehPontos;

        atributoUIMap = ehFixo ? atributoUIMapValoresFixos
                      : ehPontos ? atributoUIMapPontuacao
                      : atributoUIMapRolagem;

        foreach (var kvp in atributoUIMap)
            kvp.Value.SetModo(ehFixo);

        if (ehFixo) InicializarModoValoresFixos();
        if (ehPontos) InicializarModoPointBuy();

        if (pontosRestantesText != null)
            pontosRestantesText.gameObject.SetActive(ehPontos);
    }

    private void InicializarModoValoresFixos()
    {
        foreach (string attr in nomesOficiais)
            atributosAtuais[attr] = escolhasValoresFixos[attr];

        AtualizarDropdownsValoresFixos();
        AtualizarTodosAtributosUI();
    }

    private void AtualizarDropdownsValoresFixos()
    {
        var todosValores = new List<int>(valoresFixos);

        foreach (string attr in nomesOficiais)
        {
            if (!atributoUIMapValoresFixos.TryGetValue(attr, out AttributeItemUI item)) continue;

            string attrCapturado = attr;
            item.ConfigurarDropdown(todosValores, escolhasValoresFixos[attr], v => OnDropdownValorFixoMudou(attrCapturado, v));
            item.AtualizarTotal(GetTotal(attr));
        }
    }

    private void OnDropdownValorFixoMudou(string attr, int novoValor)
    {
        int valorAntigo = escolhasValoresFixos[attr];
        if (novoValor == valorAntigo) return;

        // Se outro atributo já tem esse valor, faz swap
        foreach (string outro in nomesOficiais)
        {
            if (outro == attr) continue;
            if (escolhasValoresFixos[outro] == novoValor)
            {
                escolhasValoresFixos[outro] = valorAntigo;
                atributosAtuais[outro]      = valorAntigo;
                break;
            }
        }

        escolhasValoresFixos[attr] = novoValor;
        atributosAtuais[attr]      = novoValor;

        AtualizarDropdownsValoresFixos();
        AtualizarTodosAtributosUI();
    }

    private void SalvarAtributosNoLocalStorage()
    {
        if (GlobalFichaData.fichasDndId <= 0) return;

        bool todosDez = atributosAtuais["FOR"] == 10 && atributosAtuais["DEX"] == 10 &&
                        atributosAtuais["CON"] == 10 && atributosAtuais["INT"] == 10 &&
                        atributosAtuais["SAB"] == 10 && atributosAtuais["CAR"] == 10;

        bool todosZero = atributosAtuais["FOR"] == 0 && atributosAtuais["DEX"] == 0 &&
                         atributosAtuais["CON"] == 0 && atributosAtuais["INT"] == 0 &&
                         atributosAtuais["SAB"] == 0 && atributosAtuais["CAR"] == 0;

        if (todosDez || todosZero)
        {
            VTTLog.SaveWarn($"NÃƒO salvando atributos inválidos (todos {(todosDez ? "10" : "0")}) em LocalFichaStorage");
            return;
        }

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        bool ok = LocalFichaStorage.SaveAttributes(
            steamId,
            playerId,
            GlobalFichaData.fichasGeneralId,
            GlobalFichaData.fichasDndId,
            atributosAtuais["FOR"],
            atributosAtuais["DEX"],
            atributosAtuais["CON"],
            atributosAtuais["INT"],
            atributosAtuais["SAB"],
            atributosAtuais["CAR"]
        );

        if (ok)
        {
            VTTLog.Save($"âœ“ Atributos salvos: FOR={atributosAtuais["FOR"]}, DEX={atributosAtuais["DEX"]}, CON={atributosAtuais["CON"]}, INT={atributosAtuais["INT"]}, SAB={atributosAtuais["SAB"]}, CAR={atributosAtuais["CAR"]}");
        }
    }
}
