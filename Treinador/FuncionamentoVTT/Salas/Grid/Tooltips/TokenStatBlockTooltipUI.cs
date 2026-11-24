using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// O painel do tooltip de stat block (prefab). Modo Hover: posiciona uma vez no canto do mouse
// (igual o menu de atalho) e some sozinho assim que o mouse se mexer — não precisa ser
// interativo. Modos Atacar/Curar (abertos pelo menu de atalho): ficam abertos até o jogador clicar
// fora ou numa ação — usados pra escolher o que usar no alvo.
public class TokenStatBlockTooltipUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Textos")]
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private TMP_Text vidaText; // um único txt, ex: "22/33 8d10"
    [SerializeField] private TMP_Text periciasTreinadasText;
    [SerializeField] private TMP_Text modificadoresAtributosText;
    [SerializeField] private TMP_Text cdMagicoText;
    [SerializeField] private GameObject containerCdMagico;

    [Header("Ações / Ataques (ou Itens de Cura no modo Curar)")]
    [SerializeField] private GameObject containerSecaoAcoes;
    [SerializeField] private TMP_Text tituloSecaoAcoes;
    [SerializeField] private Transform containerAcoes;

    [Header("Magias Preparadas (ou Magias de Cura no modo Curar)")]
    [SerializeField] private GameObject containerSecaoMagias;
    [SerializeField] private TMP_Text tituloSecaoMagias;
    [SerializeField] private Transform containerMagias;

    [Header("Prefabs da linha — mesmo script (TokenStatBlockAcaoUI), visual pode ser diferente")]
    [SerializeField] private GameObject prefabLinhaAcao;
    [SerializeField] private GameObject prefabLinhaMagia;

    [Header("Perícias / Salvaguardas (modo Testes)")]
    [SerializeField] private GameObject containerSecaoPericias;
    [SerializeField] private Transform containerPericias;
    [SerializeField] private GameObject containerSecaoSalvaguardas;
    [SerializeField] private Transform containerSalvaguardas;
    [SerializeField] private GameObject prefabLinhaTeste;

    [Header("Modo Hover")]
    [Tooltip("Quantos pixels o mouse precisa se mover pra considerar 'o jogador moveu o mouse' e esconder o painel.")]
    [SerializeField, Min(0f)] private float limiarMovimentoMouse = 4f;

    private RectTransform rectTransform;
    private TokenStatBlockTooltipManager manager;
    private MapTokenDraggable tokenOrigem;
    private ModoStatBlockTooltip modoAtual;
    private Vector2 posicaoMouseAoAbrir;
    private ChatManager chatManager;

    private void Awake()
    {
        rectTransform = transform as RectTransform;

        CanvasGroup cg = GetComponent<CanvasGroup>();
        if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
        cg.interactable = true;
        cg.blocksRaycasts = true;
    }

    public void Configurar(TokenStatBlockData dados, MapTokenDraggable origem, TokenStatBlockTooltipManager managerRef, ModoStatBlockTooltip modo)
    {
        if (dados == null) return;

        tokenOrigem = origem;
        manager = managerRef;
        modoAtual = modo;

        if (nomeText != null) nomeText.text = dados.nome;
        if (vidaText != null) vidaText.text = dados.vidaFormatada;

        if (periciasTreinadasText != null)
            periciasTreinadasText.text = dados.periciasTreinadas.Count > 0
                ? string.Join(", ", dados.periciasTreinadas)
                : "-";

        if (modificadoresAtributosText != null)
            modificadoresAtributosText.text = string.Join(", ", dados.modificadoresAtributos);

        if (containerCdMagico != null)
            containerCdMagico.SetActive(dados.temCdMagico);
        if (cdMagicoText != null && dados.temCdMagico)
            cdMagicoText.text = dados.cdMagico.ToString();

        bool ehCurar = modo == ModoStatBlockTooltip.Curar;
        if (tituloSecaoAcoes != null)
            tituloSecaoAcoes.text = ehCurar ? "Itens de Cura" : "Ações / Ataques";
        if (tituloSecaoMagias != null)
            tituloSecaoMagias.text = ehCurar ? "Magias de Cura" : "Magias Preparadas";

        bool temAcoes = dados.acoesOuAtaques.Count > 0;
        if (containerSecaoAcoes != null)
            containerSecaoAcoes.SetActive(temAcoes);
        if (temAcoes)
            PopularLista(containerAcoes, dados.acoesOuAtaques, prefabLinhaAcao, ehMagia: false);

        bool temMagias = dados.magiasPreparadas.Count > 0;
        if (containerSecaoMagias != null)
            containerSecaoMagias.SetActive(temMagias);
        if (temMagias)
            PopularLista(containerMagias, dados.magiasPreparadas, prefabLinhaMagia, ehMagia: true);

        // Modo Testes — TODAS as perícias/salvaguardas, treinadas ou não (ver TokenStatBlockBuilder).
        bool temPericias = dados.periciasTestaveis.Count > 0;
        if (containerSecaoPericias != null)
            containerSecaoPericias.SetActive(temPericias);
        if (temPericias)
            PopularListaTestes(containerPericias, dados.periciasTestaveis, prefabLinhaTeste);

        bool temSalvaguardas = dados.salvaguardasTestaveis.Count > 0;
        if (containerSecaoSalvaguardas != null)
            containerSecaoSalvaguardas.SetActive(temSalvaguardas);
        if (temSalvaguardas)
            PopularListaTestes(containerSalvaguardas, dados.salvaguardasTestaveis, prefabLinhaTeste);

        posicaoMouseAoAbrir = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        RectTransformCornerPositioner.PosicionarNoCantoDoPonto(rectTransform, posicaoMouseAoAbrir);
    }

    private void Update()
    {
        // Só o modo Hover some sozinho ao menor movimento do mouse — Atacar/Curar ficam abertos
        // até o jogador escolher uma ação ou clicar fora (catcher do manager).
        if (modoAtual != ModoStatBlockTooltip.Hover || manager == null || Mouse.current == null)
            return;

        Vector2 posicaoAtual = Mouse.current.position.ReadValue();
        if (Vector2.Distance(posicaoAtual, posicaoMouseAoAbrir) > limiarMovimentoMouse)
            manager.Esconder();
    }

    private void PopularLista(Transform container, List<AcaoStatBlock> acoes, GameObject prefabLinha, bool ehMagia)
    {
        if (container == null || prefabLinha == null) return;

        for (int i = container.childCount - 1; i >= 0; i--)
            Destroy(container.GetChild(i).gameObject);

        bool ehCura = modoAtual == ModoStatBlockTooltip.Curar;
        foreach (AcaoStatBlock acao in acoes)
        {
            GameObject go = Instantiate(prefabLinha, container);
            go.GetComponent<TokenStatBlockAcaoUI>()?.Configurar(acao,
                aoClicarEsquerdo: (a) => OnClicarAcaoMirar(a, ehMagia, ehCura),
                aoClicarDireito: (a) => OnClicarAcaoDetalhes(a, ehMagia));
        }
    }

    private void PopularListaTestes(Transform container, List<TesteStatBlock> testes, GameObject prefabLinha)
    {
        if (container == null || prefabLinha == null) return;

        for (int i = container.childCount - 1; i >= 0; i--)
            Destroy(container.GetChild(i).gameObject);

        foreach (TesteStatBlock teste in testes)
        {
            GameObject go = Instantiate(prefabLinha, container);
            go.GetComponent<TokenStatBlockTesteUI>()?.Configurar(teste, OnClicarTeste);
        }
    }

    // Clique ESQUERDO — abre a linha de mira (SelecaoAlvoManager); a rolagem de ataque/dano só
    // acontece quando o jogador confirmar um alvo, não na hora do clique (ver
    // SelecaoAlvoManager.IniciarSelecao/ConfirmarAlvoToken/ConfirmarAlvoQuadrado).
    private void OnClicarAcaoMirar(AcaoStatBlock acao, bool ehMagia, bool ehCura)
    {
        SelecaoAlvoManager.Instancia?.IniciarSelecao(tokenOrigem, acao, ehMagia, ehCura);
        manager?.Esconder();
    }

    // Clique DIREITO — só mostra a descrição no chat (nome + descrição de verdade, não a notação de
    // dano usada pra rolar — ver AcaoStatBlock.descricaoCompleta). Tempo de conjuração/alcance/
    // componentes/duração vêm de AcaoStatBlock (preenchidos em TokenStatBlockBuilder a partir dos
    // campos <casting-time>/<range>/<components>/<duration> do XML de magia, quando existem — ficam
    // vazios pras magias que ainda não têm esses campos, e sempre vazios pra item, que só tem
    // nome+descrição no catálogo local). Não entra em modo de mira.
    private void OnClicarAcaoDetalhes(AcaoStatBlock acao, bool ehMagia)
    {
        ChatManager chat = ObterChatManager();
        if (chat != null)
        {
            if (!chat.EstaAbertoPainel()) chat.AbrirPainel();

            string descricao = !string.IsNullOrWhiteSpace(acao.descricaoCompleta) ? acao.descricaoCompleta : acao.descricao;
            if (ehMagia)
                chat.EnviarDescricaoDeMagia(acao.nome, acao.tempoConjuracao, acao.alcanceTexto, "", acao.componentes, acao.duracaoTexto, descricao);
            else
                chat.EnviarDescricaoDeItem(acao.nome, "", "", "", "", descricao);
        }

        manager?.Esconder();
    }

    // Rola 1d20 + modificador e posta no chat como um teste geral — fecha o painel de testes em
    // seguida (mesmo comportamento de Atacar/Curar ao escolher uma ação).
    private void OnClicarTeste(TesteStatBlock teste)
    {
        ResultadoRolagem resultadoRolagem = RolagemDados.RolarNotacaoDetalhada(RolagemDados.FormatarNotacaoD20(teste.modificador));
        int natural = resultadoRolagem.dados[0].valor;
        ResultadoCritico resultado = RolagemDados.ClassificarD20(natural);

        ChatManager chat = ObterChatManager();
        if (chat != null)
        {
            if (!chat.EstaAbertoPainel()) chat.AbrirPainel();
            chat.EnviarTesteGeral(teste.nome, resultadoRolagem, resultado);
        }

        manager?.Esconder();
    }

    private ChatManager ObterChatManager()
    {
        if (chatManager == null)
            chatManager = FindFirstObjectByType<ChatManager>();
        return chatManager;
    }

    public void OnPointerEnter(PointerEventData eventData) => manager?.NotificarMouseEntrouNoPainel();

    public void OnPointerExit(PointerEventData eventData) => manager?.NotificarMouseSaiuDoPainel();
}
