using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Versão offline de Salas/Grid/MapTokenDraggable.cs — arrastar via Input System + hover→tooltip +
/// clique direito → menu de atalho + clique-sem-arrastar → seleção (ver bloco estático abaixo, mesmo
/// padrão de MapTokenDraggable). Sem checagem de dono Steam (TenhoPermissaoSobreToken é sempre true)
/// e sem interpolação de posição de rede — Campanhas é modo de preparação single-user. O menu de
/// atalho só tem Editar/Lore/Selecionar Visão/Excluir — sem Atacar/Curar/Arremessar, que não fazem
/// sentido em preparação offline (não há fluxo de combate aqui).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class CampanhaTokenDraggable : MonoBehaviour, ITokenNoMapa
{
    // Token atualmente selecionado (clicado) no mapa — seleção única e global, mesmo padrão estático
    // de MapTokenDraggable.TokenSelecionado. TokenButtonUI assina OnSelecaoAlterada pra mostrar/
    // esconder os controles de resize e o content de ferramentas só do token selecionado.
    public static CampanhaTokenDraggable TokenSelecionado { get; private set; }
    public static event Action<CampanhaTokenDraggable> OnSelecaoAlterada;

    // true por um frame quando ALGUM token foi o alvo topmost de um clique esquerdo — lido por
    // CampanhaMapManager.LateUpdate (roda depois de todo Update() da cena) pra saber se um clique
    // que não confirmou nenhum alvo de fato não caiu em cima de nenhum token — só nesse caso
    // desmarca a seleção atual — e reseta pro próximo frame. Mesmo padrão de
    // MapTokenDraggable.CliqueConsumidoEsteFrame.
    public static bool CliqueConsumidoEsteFrame;

    public static void LimparSelecao()
    {
        if (TokenSelecionado == null) return;
        TokenSelecionado = null;
        OnSelecaoAlterada?.Invoke(null);
    }

    private void AlternarSelecao()
    {
        TokenSelecionado = TokenSelecionado == this ? null : this;
        OnSelecaoAlterada?.Invoke(TokenSelecionado);
    }

    private CampanhaMapManager mapManager;
    private Camera cam;
    private Vector3 offset;
    private bool arrastando;
    private SpriteRenderer spriteRenderer;
    private Collider2D colisor;

    // Clique vs. arrasto — rastreado à parte de 'arrastando' porque também precisa funcionar num
    // token travado (que nunca chega a virar um arrasto de verdade, mas ainda precisa poder ser
    // selecionado pra destravar pelo content de ferramentas). Mesmo padrão/limiar de
    // MapTokenDraggable — ver o comentário lá sobre por que 20px (não 6px).
    private bool _rastreandoCliqueSelecao;
    private Vector2 _posicaoTelaInicioClique;
    private const float LimiarCliqueEmPixels = 20f;

    public EntryData Npc { get; private set; }
    public string NomeToken { get; private set; }

    // ── ITokenNoMapa ──────────────────────────────────────────────────────────

    public string TokenId { get; private set; }
    public bool Bloqueado { get; private set; }
    public bool Escondido { get; private set; }
    private bool _visualEscondidoAplicado;

    /// <summary>Vida informativa, lida uma vez do stat block do NPC ("npc-hp", ver
    /// NpcEntryFormatter.ObterVidaNumerica) — Campanhas não tem fluxo de combate/dano, então fica
    /// só como referência pro Mestre durante a preparação (mesmo tratamento visual do slider de vida
    /// em TokenButtonUI, só que aqui nunca muda sozinha).</summary>
    public int VidaAtual { get; private set; }
    public int VidaMaxima { get; private set; }

    /// <summary>Sempre true — Campanhas não tem conceito de dono/host (modo de preparação
    /// single-user), então nenhuma ferramenta de token precisa ficar bloqueada por permissão aqui.</summary>
    public bool TenhoPermissaoSobreToken() => true;

    private bool PodeMover() => !Bloqueado;

    /// <summary>Raio de visão deste token, em METROS — ver CampanhaMapManager.ConverterMetrosParaMundo
    /// pra conversão pra unidades de mundo (convenção: 1 quadrado = 1.5m).</summary>
    public float RaioVisaoMetros { get; private set; } = 18f;

    private const float TempoParaTooltip = 0.6f;
    private float _hoverTimer;
    private bool _tooltipVisivel;
    private bool _mouseEstavaOver;

    public void Setup(CampanhaMapManager manager, EntryData npc, string nome, float raioVisaoMetros = 18f,
        string tokenId = "", bool bloqueadoSalvo = false, bool escondidoSalvo = false)
    {
        mapManager = manager;
        Npc = npc;
        NomeToken = nome;
        TokenId = string.IsNullOrWhiteSpace(tokenId) ? Guid.NewGuid().ToString("N") : tokenId;
        DefinirRaioVisao(raioVisaoMetros);

        spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        colisor = GetComponent<Collider2D>();

        Bloqueado = bloqueadoSalvo;
        Escondido = escondidoSalvo;
        _visualEscondidoAplicado = false;

        float? vida = NpcEntryFormatter.ObterVidaNumerica(npc);
        VidaMaxima = vida.HasValue ? Mathf.Max(0, Mathf.RoundToInt(vida.Value)) : 0;
        VidaAtual = VidaMaxima;
    }

    /// <summary>Chamado pelo slider de raio de visão (em CampanhaMapManager, quando a visão deste
    /// token estiver ativa, OU pelo botão de visão em TokenButtonUI) — clampa num mínimo de 1
    /// quadrado (1.5m).</summary>
    public void DefinirRaioVisao(float metros) => RaioVisaoMetros = Mathf.Max(1.5f, metros);

    // ── Flags (Lock/Esconder) ────────────────────────────────────────────────

    /// <summary>Aplica os flags localmente — chamado pelo clique local (ver AlternarBloqueado/
    /// AlternarEscondido). Sem broadcast de rede — Campanhas é offline.</summary>
    public void DefinirFlags(bool bloqueado, bool escondido)
    {
        Bloqueado = bloqueado;
        Escondido = escondido;
        _visualEscondidoAplicado = false; // força reavaliar o visual no próximo Update
    }

    public void AlternarBloqueado() => DefinirFlags(!Bloqueado, Escondido);

    public void AlternarEscondido() => DefinirFlags(Bloqueado, !Escondido);

    // Campanhas não tem conceito de "jogador" separado do Mestre — só existe quem está preparando a
    // campanha. Por isso, diferente do esquema de 3 estados de MapTokenDraggable (normal /
    // semitransparente-pro-host / invisível-pro-jogador), aqui é só um tingimento semitransparente
    // quando Escondido==true, sem nunca desativar sprite/collider: é uma anotação visual de
    // preparação ("este token vai nascer escondido quando a cena for importada pra uma Sala"), não
    // um esconderijo de verdade — não haveria de quem esconder.
    private void AtualizarVisualEscondido()
    {
        if (_visualEscondidoAplicado) return;
        _visualEscondidoAplicado = true;

        if (spriteRenderer != null)
            spriteRenderer.color = Escondido ? new Color(1f, 1f, 1f, 0.5f) : Color.white;
    }

    private Camera ObterCamera()
    {
        if (cam != null) return cam;
        cam = Camera.main;
        return cam;
    }

    private bool EsteTokenEhOTopmost(Vector2 posicaoMundo)
    {
        Collider2D[] hits = Physics2D.OverlapPointAll(posicaoMundo);
        if (hits.Length == 0) return false;

        int melhorOrdem = int.MinValue;
        Collider2D melhorHit = null;

        foreach (Collider2D hit in hits)
        {
            SpriteRenderer sr = hit.GetComponentInChildren<SpriteRenderer>(true);
            if (sr == null) continue;
            if (sr.sortingOrder > melhorOrdem)
            {
                melhorOrdem = sr.sortingOrder;
                melhorHit = hit;
            }
        }

        return melhorHit != null && melhorHit.gameObject == gameObject;
    }

    private void OnDisable() => EsconderTooltip();

    private void EsconderTooltip()
    {
        if (_tooltipVisivel)
            CampanhaTokenTooltipManager.Instance?.Esconder();
        _tooltipVisivel = false;
        _hoverTimer = 0f;
        _mouseEstavaOver = false;
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || mapManager == null) return;

        Camera cameraAtual = ObterCamera();
        if (cameraAtual == null) return;

        Vector2 posicaoTela = mouse.position.ReadValue();

        // ── Hover tooltip ────────────────────────────────────────────────────
        if (arrastando)
        {
            EsconderTooltip();
        }
        else
        {
            bool sobreUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            bool mouseOver = false;

            if (!sobreUI)
            {
                Vector3 mundo3D = cameraAtual.ScreenToWorldPoint(
                    new Vector3(posicaoTela.x, posicaoTela.y, Mathf.Abs(cameraAtual.transform.position.z)));
                mouseOver = EsteTokenEhOTopmost(new Vector2(mundo3D.x, mundo3D.y));

                if (mouseOver)
                {
                    _hoverTimer += Time.deltaTime;
                    if (!_tooltipVisivel && _hoverTimer >= TempoParaTooltip)
                    {
                        _tooltipVisivel = true;
                        CampanhaTokenTooltipManager.Instance?.Mostrar(Npc);
                    }
                }
            }

            if (!mouseOver && _mouseEstavaOver)
                EsconderTooltip();

            _mouseEstavaOver = mouseOver;
        }

        // ── Clique direito → menu de atalho ─────────────────────────────────
        if (mouse.rightButton.wasPressedThisFrame && !arrastando)
        {
            if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
            {
                Vector3 mundo3DDireito = cameraAtual.ScreenToWorldPoint(
                    new Vector3(posicaoTela.x, posicaoTela.y, Mathf.Abs(cameraAtual.transform.position.z)));

                if (EsteTokenEhOTopmost(new Vector2(mundo3DDireito.x, mundo3DDireito.y)))
                    AbrirMenuDeAtalho(posicaoTela);
            }
        }

        // ── Clique esquerdo → começar a arrastar (ou só selecionar, ver bloco de baixo) ──────────
        if (mouse.leftButton.wasPressedThisFrame && !arrastando)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            Vector3 mundo3D = cameraAtual.ScreenToWorldPoint(
                new Vector3(posicaoTela.x, posicaoTela.y, Mathf.Abs(cameraAtual.transform.position.z)));
            Vector2 mundo2D = new Vector2(mundo3D.x, mundo3D.y);

            bool souTopmost = EsteTokenEhOTopmost(mundo2D);
            if (souTopmost)
            {
                // Independente de PodeMover() — um token travado ainda precisa poder ser
                // selecionado pra destravar (ver bloco de clique-sem-arrasto logo abaixo).
                CliqueConsumidoEsteFrame = true;
                _rastreandoCliqueSelecao = true;
                _posicaoTelaInicioClique = posicaoTela;
            }

            if (souTopmost && PodeMover())
            {
                Vector3 mouseMundo = mapManager.CamParaMundoToken(cameraAtual, posicaoTela);
                offset = transform.position - mouseMundo;
                arrastando = true;
            }
        }

        if (arrastando)
        {
            if (mouse.leftButton.isPressed)
            {
                Vector3 mouseMundo = mapManager.CamParaMundoToken(cameraAtual, posicaoTela);
                Vector3 alvo = mouseMundo + offset;
                if (mapManager.PosicaoDentroDaAreaDeTokens(alvo))
                {
                    transform.position = new Vector3(alvo.x, alvo.y, transform.position.z);
                    // Iluminação acompanha o token em tempo real durante o arrasto (não só ao soltar)
                    // — no-op se nenhuma visão ativa envolver este token (ver AtualizarVisaoSeAtivo).
                    mapManager.AtualizarVisaoSeAtivo(this);
                }
            }
            else
            {
                arrastando = false;
                _rastreandoCliqueSelecao = false;
                FinalizarInteracaoDeArrasto(posicaoTela);
            }
        }
        else if (_rastreandoCliqueSelecao && !mouse.leftButton.isPressed)
        {
            // Token travado — nunca virou um arrasto de verdade, mas ainda conta como clique de
            // seleção (senão nunca daria pra selecionar um token travado pra destravá-lo).
            _rastreandoCliqueSelecao = false;
            if (Vector2.Distance(posicaoTela, _posicaoTelaInicioClique) < LimiarCliqueEmPixels)
                AlternarSelecao();
        }

        AtualizarVisualEscondido();
    }

    // Distingue "só cliquei" (sem deslocamento de verdade) de "arrastei o token" — no primeiro caso
    // alterna a seleção em vez de finalizar um movimento. Mesmo raciocínio de
    // MapTokenDraggable.FinalizarInteracaoDeArrasto.
    private void FinalizarInteracaoDeArrasto(Vector2 posicaoTelaSolta)
    {
        if (Vector2.Distance(posicaoTelaSolta, _posicaoTelaInicioClique) < LimiarCliqueEmPixels)
        {
            AlternarSelecao();
        }
        else
        {
            transform.position = mapManager.AjustarPosicaoTokenAoGrid(transform.position);
            mapManager.SalvarEstadoTokensNoMapaAtivo();
            mapManager.AtualizarVisaoSeAtivo(this); // no-op se a visão exibida não for a deste token
        }
    }

    private void AbrirMenuDeAtalho(Vector2 posicaoTela)
    {
        if (MenuAtalhoManager.Instance == null)
        {
            VTTLog.TokenWarn("MenuAtalhoManager não encontrado na cena — não é possível abrir o menu de atalho.");
            return;
        }

        EsconderTooltip();

        MenuAtalhoManager.Instance.Mostrar(posicaoTela, new List<OpcaoMenuAtalho>
        {
            new OpcaoMenuAtalho("Editar",           () => mapManager?.PedirEdicaoDoNpc(Npc)),
            new OpcaoMenuAtalho("Lore",             () => mapManager?.PedirLoreDoNpc(Npc)),
            new OpcaoMenuAtalho("Selecionar Visão", () => mapManager?.MostrarVisaoDoToken(this)),
            new OpcaoMenuAtalho("Excluir",          () => mapManager?.RemoverTokenDoMapa(gameObject), ehExclusao: true),
        });
    }
}
