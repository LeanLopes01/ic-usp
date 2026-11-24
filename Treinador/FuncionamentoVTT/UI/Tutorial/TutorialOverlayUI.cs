using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// View "burra" do overlay de tutorial — uGUI puro. Mesmo padrão de Fichas/D&amp;D/FichasVizualização/
/// Tooltips/TooltipUI.cs e Salas/Grid/TokenTooltipUI.cs (já comprovados funcionando neste projeto):
/// o script fica DIRETO no GameObject do balão (o que se move e é mostrado/escondido via
/// SetActive), filho DIRETO do Canvas — e usa transform.parent (o Canvas) como referência de
/// tamanho/conversão de coordenadas, nunca a própria RectTransform. Isso elimina a necessidade de
/// um container "raiz" separado esticando manualmente pra cobrir a tela (fonte de bugs anteriores:
/// qualquer erro de anchor/Content Size Fitter nesse container quebrava todo o posicionamento).
///
/// Não decide nada sobre QUAL passo mostrar ou QUANDO avançar — só desenha o passo/dica que
/// TutorialManager mandar (MostrarPasso/MostrarDica) e avisa via evento quando o jogador clica em
/// Voltar/Próximo/Pular/Entendi.
///
/// Setup no Editor:
///   - Este GameObject (o balão): filho DIRETO do Canvas (não de nenhum container extra) — Pivot=
///     (0,1) e Anchor Min/Max=(0,1) (ancorado no canto superior esquerdo, sem stretch). Pode ter
///     largura fixa e altura automática (Content Size Fitter) à vontade — o script só mexe na
///     POSIÇÃO (anchoredPosition), nunca no tamanho.
///   - indicadorCima/Baixo/Direita/Esquerda: filhos deste objeto, cada um numa posição fixa (ex:
///     uma setinha grudada em cada uma das 4 bordas do balão, já configurada no Editor). O script só
///     ativa o que corresponde ao lado REAL escolhido pra aquele alvo (o preferido, ou o de fallback
///     se o preferido não coube na tela) e desativa os outros 3 — nunca reposiciona nada.
/// </summary>
public class TutorialOverlayUI : MonoBehaviour
{
    [Header("Balão")]
    [SerializeField] private TMP_Text calloutTitle;
    [SerializeField] private TMP_Text calloutBody;
    [SerializeField] private TMP_Text calloutCounter;
    [SerializeField] private Button calloutBackBtn;
    [SerializeField] private Button calloutNextBtn;
    [SerializeField] private Button calloutSkipBtn;
    [SerializeField] private Button calloutGotItBtn;

    [Header("Indicadores de lado (opcional — ligados/desligados conforme o lado escolhido)")]
    [SerializeField] private GameObject indicadorCima;
    [SerializeField] private GameObject indicadorBaixo;
    [SerializeField] private GameObject indicadorDireita;
    [SerializeField] private GameObject indicadorEsquerda;

    private RectTransform _rt;         // este objeto (o balão em si) — o que se move
    private RectTransform _parentRect; // pai (deve ser o Canvas) — referência de tamanho/conversão
    private Canvas _canvas;

    private GameObject alvoAtual;
    private DirecaoTooltip ladoPreferidoAtual = DirecaoTooltip.Direita;
    // Distância alvo↔balão do PASSO/DICA atual — customizável por passo (TutorialPassoData.
    // distanciaAlvo) em vez de um valor fixo pro componente inteiro.
    private float distanciaAtual = 12f;
    private bool visivel;
    // Evita spammar o console a cada frame enquanto o fallback centralizado estiver ativo — só
    // avisa UMA VEZ por passo (reseta em MostrarPasso/MostrarDica e quando um alvo válido é achado).
    private bool _avisoFallbackEmitido;

    public event Action OnProximo;
    public event Action OnVoltar;
    public event Action OnPular;
    public event Action OnEntendiDica;

    private void Awake()
    {
        _rt = transform as RectTransform;
        if (_rt == null)
        {
            Debug.LogError($"[TutorialOverlayUI] O GameObject '{gameObject.name}' só tem um Transform comum, " +
                "não um RectTransform — precisa estar dentro de um Canvas E ter um RectTransform de verdade. " +
                "Corrija recriando o objeto via botão direito na Hierarchy > UI > Panel (ou Add Component > " +
                "Rect Transform neste objeto).");
            return;
        }

        _parentRect = transform.parent as RectTransform;
        if (_parentRect == null)
        {
            Debug.LogError($"[TutorialOverlayUI] O PAI de '{gameObject.name}' não é um RectTransform — este " +
                "objeto precisa ser filho DIRETO de um Canvas (mesmo padrão de TooltipUI/TokenTooltipUI).");
            return;
        }

        _canvas = GetComponentInParent<Canvas>();
        RegisterCallbacks();
        Esconder();
    }

    private void OnDestroy() => UnregisterCallbacks();

    private void RegisterCallbacks()
    {
        if (calloutBackBtn != null)  calloutBackBtn.onClick.AddListener(OnVoltarClicked);
        if (calloutNextBtn != null)  calloutNextBtn.onClick.AddListener(OnProximoClicked);
        if (calloutSkipBtn != null)  calloutSkipBtn.onClick.AddListener(OnPularClicked);
        if (calloutGotItBtn != null) calloutGotItBtn.onClick.AddListener(OnEntendiClicked);
    }

    private void UnregisterCallbacks()
    {
        if (calloutBackBtn != null)  calloutBackBtn.onClick.RemoveListener(OnVoltarClicked);
        if (calloutNextBtn != null)  calloutNextBtn.onClick.RemoveListener(OnProximoClicked);
        if (calloutSkipBtn != null)  calloutSkipBtn.onClick.RemoveListener(OnPularClicked);
        if (calloutGotItBtn != null) calloutGotItBtn.onClick.RemoveListener(OnEntendiClicked);
    }

    private void OnVoltarClicked()  => OnVoltar?.Invoke();
    private void OnProximoClicked() => OnProximo?.Invoke();
    private void OnPularClicked()   => OnPular?.Invoke();
    private void OnEntendiClicked() => OnEntendiDica?.Invoke();

    // ── API pública (chamada por TutorialManager) ───────────────────────────

    public void MostrarPasso(TutorialPassoData passo, int indice, int total, bool modoDica)
    {
        if (passo == null) return;

        visivel = true;
        gameObject.SetActive(true);
        AplicarModoDica(false);

        if (calloutTitle != null) calloutTitle.text = passo.titulo ?? "";
        if (calloutBody != null) calloutBody.text = passo.corpo ?? "";
        if (calloutCounter != null) calloutCounter.text = $"{indice}/{total}";
        if (calloutBackBtn != null) calloutBackBtn.interactable = indice > 1;

        alvoAtual = passo.alvo;
        ladoPreferidoAtual = passo.ladoPreferido;
        distanciaAtual = passo.distanciaAlvo;
        _avisoFallbackEmitido = false;
        PosicionarTudo();
    }

    public void MostrarDica(GameObject alvo, string texto, float distanciaAlvo = 12f)
    {
        visivel = true;
        gameObject.SetActive(true);
        AplicarModoDica(true);

        if (calloutBody != null) calloutBody.text = texto ?? "";

        alvoAtual = alvo;
        ladoPreferidoAtual = DirecaoTooltip.Cima;
        distanciaAtual = distanciaAlvo;
        _avisoFallbackEmitido = false;
        PosicionarTudo();
    }

    private void AvisarFallbackCentralizado(GameObject alvo, string motivo)
    {
        if (_avisoFallbackEmitido) return;
        _avisoFallbackEmitido = true;
        string nomeAlvo = alvo != null ? alvo.name : "(nenhum)";
        Debug.LogWarning($"[TutorialOverlayUI] Passo caiu no modo centralizado em vez de apontar pro alvo '{nomeAlvo}' — motivo: {motivo}.");
    }

    // Dica = só corpo + "Entendi". Passo = contador + Voltar/Próximo/Pular. "Entendi" fica SEMPRE
    // ativo nos dois modos — o jogador pode clicar nele a qualquer momento pra encerrar o tutorial
    // na hora, mesmo no meio de um passo guiado (dispara OnEntendiDica igual à dica avulsa).
    private void AplicarModoDica(bool modoDica)
    {
        if (calloutCounter != null)  calloutCounter.gameObject.SetActive(!modoDica);
        if (calloutBackBtn != null)  calloutBackBtn.gameObject.SetActive(!modoDica);
        if (calloutNextBtn != null)  calloutNextBtn.gameObject.SetActive(!modoDica);
        if (calloutSkipBtn != null)  calloutSkipBtn.gameObject.SetActive(!modoDica);
        if (calloutGotItBtn != null) calloutGotItBtn.gameObject.SetActive(true);
    }

    public void Esconder()
    {
        visivel = false;
        alvoAtual = null;
        gameObject.SetActive(false);
    }

    // Recalcula a cada frame enquanto visível — barato, e evita ter que reagir corretamente a
    // resize de janela/alt-tab/fullscreen toggle. Só roda enquanto o GameObject está ativo (Unity
    // não chama Update de objeto inativo), então nem precisa checar "visivel" de fato, mas mantém
    // o campo pra clareza/uso externo.
    private void Update()
    {
        if (!visivel) return;
        PosicionarTudo();
    }

    // ── Posicionamento ───────────────────────────────────────────────────────

    private void PosicionarTudo()
    {
        if (_rt == null || _parentRect == null) return; // erro já logado em Awake

        if (alvoAtual == null)
        {
            AvisarFallbackCentralizado(null, "TutorialPassoData.alvo não foi definido pra este passo (campo vazio no Inspector)");
            AtivarIndicador(null); // sem alvo, sem lado pra apontar
            PosicionarCentralizado();
            return;
        }

        Rect telaAlvo = BarraFerramentasManager.ObterRetanguloTela(alvoAtual);
        if (telaAlvo.width <= 0f || telaAlvo.height <= 0f)
        {
            // Alvo inválido/destruído/inativo — não trava o tutorial, só cai pro modo centralizado.
            AvisarFallbackCentralizado(alvoAtual,
                $"retângulo de tela inválido ({telaAlvo}) — objeto provavelmente inativo na hierarquia " +
                "ou dentro de uma tela fechada pelo ToggleObjects no momento em que o passo aparece");
            AtivarIndicador(null);
            PosicionarCentralizado();
            return;
        }
        _avisoFallbackEmitido = false; // alvo válido desta vez — permite avisar de novo se voltar a falhar

        // Os dois cantos são convertidos SEPARADAMENTE e só depois recombinados com Min/Max —
        // TelaParaLocalTopoEsquerda inverte Y (tela cresce pra cima, nosso espaço local cresce pra
        // baixo a partir do topo), então fazer Min/Max em coordenada de tela antes de converter
        // produz um retângulo invertido/degenerado.
        Vector2 c1 = TelaParaLocalTopoEsquerda(new Vector2(telaAlvo.xMin, telaAlvo.yMin));
        Vector2 c2 = TelaParaLocalTopoEsquerda(new Vector2(telaAlvo.xMax, telaAlvo.yMax));
        Vector2 min = Vector2.Min(c1, c2);
        Vector2 max = Vector2.Max(c1, c2);
        Rect alvoPanel = new Rect(min, max - min);

        float largura = _parentRect.rect.width;
        float altura = _parentRect.rect.height;

        PosicionarJuntoAoAlvo(alvoPanel, largura, altura);
    }

    // Converte um ponto em ESPAÇO DE TELA (Y crescendo pra cima, origem embaixo-esquerda — mesmo
    // espaço que BarraFerramentasManager.ObterRetanguloTela devolve) pra espaço LOCAL do PAI (Canvas)
    // com origem no canto SUPERIOR ESQUERDO e Y crescendo pra BAIXO — mesma convenção usada por
    // anchoredPosition abaixo. Mesma técnica de TooltipUI.AtualizarPosicao (ScreenPointToLocalPointInRectangle
    // contra o RectTransform do PAI, não do próprio objeto que se move).
    private Vector2 TelaParaLocalTopoEsquerda(Vector2 pontoTela)
    {
        Camera cam = (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? _canvas.worldCamera : null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_parentRect, pontoTela, cam, out Vector2 local);
        Rect r = _parentRect.rect;
        return new Vector2(local.x - r.xMin, r.yMax - local.y);
    }

    // Só posiciona (o TAMANHO deste objeto nunca é mexido por código, fica por conta do próprio
    // layout/Content Size Fitter configurado no Editor). Requer Pivot=(0,1) e Anchor Min/Max=(0,1).
    private void Posicionar(float x, float y) => _rt.anchoredPosition = new Vector2(x, -y);

    // Mesmo conceito de BarraFerramentasManager.EscolherPosicaoTela (tenta o lado preferido, cai
    // pros demais em ordem fixa) mas checando contra o retângulo do Canvas. Também decide qual
    // indicador de lado (seta) fica ativo, já que é o mesmo cálculo que decide onde o balão foi
    // parar de fato (pode não ser o lado preferido, se ele não coube).
    private void PosicionarJuntoAoAlvo(Rect alvoPanel, float largura, float altura)
    {
        Vector2 tamanho = _rt.rect.size;

        Vector2 cima     = new Vector2(alvoPanel.center.x - tamanho.x / 2f, alvoPanel.yMin - distanciaAtual - tamanho.y);
        Vector2 baixo    = new Vector2(alvoPanel.center.x - tamanho.x / 2f, alvoPanel.yMax + distanciaAtual);
        Vector2 direita  = new Vector2(alvoPanel.xMax + distanciaAtual, alvoPanel.center.y - tamanho.y / 2f);
        Vector2 esquerda = new Vector2(alvoPanel.xMin - distanciaAtual - tamanho.x, alvoPanel.center.y - tamanho.y / 2f);

        Vector2 posicao = EscolherPosicao(cima, baixo, direita, esquerda, tamanho, largura, altura, out DirecaoTooltip ladoEscolhido);
        float x = Mathf.Clamp(posicao.x, 0f, Mathf.Max(0f, largura - tamanho.x));
        float y = Mathf.Clamp(posicao.y, 0f, Mathf.Max(0f, altura - tamanho.y));
        Posicionar(x, y);
        AtivarIndicador(ladoEscolhido);
    }

    private void PosicionarCentralizado()
    {
        float largura = _parentRect.rect.width;
        float altura = _parentRect.rect.height;
        Vector2 tamanho = _rt.rect.size;
        Posicionar(Mathf.Max(0f, (largura - tamanho.x) / 2f), Mathf.Max(0f, (altura - tamanho.y) / 2f));
    }

    // Ativa só o indicador do lado passado (ou nenhum, se lado==null) e desativa os outros 3 — os
    // 4 GameObjects ficam parados onde foram colocados no Editor, isso nunca mexe em posição.
    private void AtivarIndicador(DirecaoTooltip? lado)
    {
        if (indicadorCima != null)     indicadorCima.SetActive(lado == DirecaoTooltip.Cima);
        if (indicadorBaixo != null)    indicadorBaixo.SetActive(lado == DirecaoTooltip.Baixo);
        if (indicadorDireita != null)  indicadorDireita.SetActive(lado == DirecaoTooltip.Direita);
        if (indicadorEsquerda != null) indicadorEsquerda.SetActive(lado == DirecaoTooltip.Esquerda);
    }

    private Vector2 EscolherPosicao(Vector2 cima, Vector2 baixo, Vector2 direita, Vector2 esquerda,
        Vector2 tamanho, float largura, float altura, out DirecaoTooltip ladoEscolhido)
    {
        Vector2 preferido = ObterPontoParaDirecao(ladoPreferidoAtual, cima, baixo, direita, esquerda);
        if (CabeNoPainel(preferido, tamanho, largura, altura))
        {
            ladoEscolhido = ladoPreferidoAtual;
            return preferido;
        }

        DirecaoTooltip[] ordemFallback = { DirecaoTooltip.Cima, DirecaoTooltip.Baixo, DirecaoTooltip.Direita, DirecaoTooltip.Esquerda };
        foreach (DirecaoTooltip direcao in ordemFallback)
        {
            if (direcao == ladoPreferidoAtual) continue;
            Vector2 ponto = ObterPontoParaDirecao(direcao, cima, baixo, direita, esquerda);
            if (CabeNoPainel(ponto, tamanho, largura, altura))
            {
                ladoEscolhido = direcao;
                return ponto;
            }
        }

        ladoEscolhido = ladoPreferidoAtual;
        return preferido;
    }

    private static Vector2 ObterPontoParaDirecao(DirecaoTooltip direcao, Vector2 cima, Vector2 baixo, Vector2 direita, Vector2 esquerda)
    {
        switch (direcao)
        {
            case DirecaoTooltip.Cima: return cima;
            case DirecaoTooltip.Baixo: return baixo;
            case DirecaoTooltip.Direita: return direita;
            default: return esquerda;
        }
    }

    private static bool CabeNoPainel(Vector2 pos, Vector2 tamanho, float largura, float altura)
        => pos.x >= 0f && pos.y >= 0f && pos.x + tamanho.x <= largura && pos.y + tamanho.y <= altura;
}
