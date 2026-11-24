using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Collider2D))]
public class MapTokenDraggable : MonoBehaviour, ITokenNoMapa
{
    // Trava global pra builds/cenas somente-leitura (ex: mobile companion) — desliga TODO arrasto
    // de token independente de PodeMover(), que hoje libera sozinho quando SteamClient não é válido
    // (o caso do celular, que nunca inicializa Steam). Ver DiceVTTMobileUI/SalaLanCliente.
    public static bool InteracaoDeArrastoHabilitada = true;

    // Token atualmente selecionado (clicado) no mapa — seleção única e global, mesmo padrão de
    // singleton estático já usado no projeto (SelecaoAlvoManager.Instancia). TokenButtonUI assina
    // OnSelecaoAlterada pra mostrar/esconder os controles de resize e o content de ferramentas só do
    // token selecionado. Clicar outro token troca a seleção sozinho (o antigo recebe o evento com o
    // novo valor e se desmarca); SelecaoAlvoManager.LateUpdate desmarca quando o clique cai fora de
    // qualquer token (ver CliqueConsumidoEsteFrame).
    public static MapTokenDraggable TokenSelecionado { get; private set; }
    public static event Action<MapTokenDraggable> OnSelecaoAlterada;

    // true por um frame quando ALGUM token foi o alvo topmost de um clique esquerdo — mesmo token
    // travado/sem permissão de mover conta (ainda precisa poder ser selecionado pra destravar).
    // SelecaoAlvoManager.LateUpdate lê isto (roda depois de todo Update() da cena) pra saber se um
    // clique que não confirmou nenhum alvo de fato não caiu em cima de nenhum token — só nesse caso
    // desmarca a seleção atual — e reseta pro próximo frame.
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

    private MapManager     mapManager;
    private Camera         cam;
    private Vector3        offset;
    private bool           arrastando;
    private SpriteRenderer spriteRenderer;
    private Collider2D     colisor;

    // Clique vs. arrasto — rastreado à parte de 'arrastando' porque também precisa funcionar num
    // token travado/sem permissão de mover (que nunca chega a virar um arrasto de verdade, mas ainda
    // precisa poder ser selecionado pra destravar pelo content de ferramentas — ver Update()).
    private bool    _rastreandoCliqueSelecao;
    private Vector2 _posicaoTelaInicioClique;
    // mouse.position.ReadValue() é em pixels de tela reais (não escalados pelo Canvas) — 6px era
    // baixo demais: tremor normal da mão ao clicar facilmente excede isso em telas de resolução
    // média/alta, fazendo um clique parado ser classificado como "arrasto que não saiu da célula"
    // (a posição fica igual de qualquer jeito, então parecia que nada tinha acontecido). 20px ainda
    // é bem menor que uma célula de grid típica, então não arrisca confundir um arrasto de verdade.
    private const float LimiarCliqueEmPixels = 20f;

    // ── Vida (HP) ─────────────────────────────────────────────────────────────
    // Fonte única de verdade da vida EXIBIDA deste token, em todo cliente — não é lida ao vivo da
    // ficha/monstro a cada frame (LocalFichaData de outro jogador só existe na máquina DELE, ver
    // FichaSyncManager; e monstro nunca teve HP persistido). Inicializada uma vez em Setup() a partir
    // da ficha/monstro (ou do valor salvo, se restaurando — ver vidaAtualSalva) e depois só muda via
    // DefinirVidaAtual/AplicarDano, propagada pela rede (NetworkSyncManager.NotificarDanoToken).
    public int VidaAtual  { get; private set; }
    public int VidaMaxima { get; private set; }

    // ── Flags (Lock/Esconder) ────────────────────────────────────────────────
    public bool Bloqueado { get; private set; }
    public bool Escondido { get; private set; }
    private bool _visualEscondidoAplicado;
    private bool _ultimoEstadoHostParaEscondido;

    // Lidos por MapManager durante o salvamento
    public string NomeToken   { get; private set; }
    public string ImagemPath  { get; private set; }
    // ID estável para sincronização em rede
    public string TokenId     { get; private set; }
    // SteamId (string) do jogador dono; vazio = qualquer um pode mover
    public string DonoSteamId { get; private set; }

    /// <summary>Reatribui o dono (gate de PodeMover) após o Setup inicial — ver
    /// MapManager.AplicarDonoDeToken, chamado pelo fluxo de atribuição de dono em
    /// SalaAbasFichasManager.</summary>
    public void DefinirDono(string steamId) => DonoSteamId = steamId;

    // Link com a ficha de origem — "chave de acesso" para puxar os dados completos sob demanda
    // (LocalFichaData) em vez de duplicar o JSON inteiro da ficha em cada token no tabuleiro.
    public int    FichasGeneralId { get; private set; }
    public int    FichasDndId     { get; private set; }
    public string OwnerSteamId    { get; private set; }
    public int    OwnerPlayerId   { get; private set; }
    public bool   IsLinked => FichasDndId > 0;

    // Link com o monstro de origem (catálogo local de criaturas) — mutuamente exclusivo com
    // o link de ficha: um token representa OU um personagem OU um monstro, nunca os dois.
    public string MonstroId  { get; private set; }
    public bool   IsMonstro  => !string.IsNullOrWhiteSpace(MonstroId);

    // Link com um NPC de Campanha enviado pra esta sala (CampanhaParaSalaConverter) — mutuamente
    // exclusivo com FichasDndId/MonstroId. Usado por MapManager.MostrarLoreDoNpc pra achar o XML
    // salvo em SalaDataStorage.NpcsCampanhaFolderPath. Vazio = token comum, sem lore associada.
    public string NpcIdCampanha { get; private set; }
    public bool   TemNpcDeCampanha => !string.IsNullOrWhiteSpace(NpcIdCampanha);

    // Raio de visão (metros) pra visão dinâmica — mesma convenção de CampanhaTokenDraggable.
    public float RaioVisaoMetros { get; private set; } = 18f;
    public void DefinirRaioVisao(float metros) => RaioVisaoMetros = Mathf.Max(1.5f, metros);

    // Posição recebida da rede — interpolada suavemente em LateUpdate
    private Vector3 posicaoAlvoRede;
    private bool    temPosicaoAlvoRede;

    // Tooltip hover
    private const float TempoParaTooltip = 2f;
    private float _hoverTimer;
    private bool  _tooltipVisivel;
    private bool  _mouseEstavaOver;

    // Instrumentação temporária (Bug E) — detecta transição de ModoMoverMapaAtivo pra logar só
    // quando muda de estado, evitando spam a cada frame enquanto ativado via VTTLog.ativoToken.
    private bool _ultimoModoMoverMapaAtivo;

    /// <param name="vidaAtualSalva">-1 (padrão) = spawn novo, vida inicial vem da ficha/monstro
    /// vinculado. >= 0 = restaurando de um save (TokenInstanceSaveData.vidaAtual) — usado como a
    /// vida inicial em vez da ficha/monstro, já que é o único registro de dano persistido pra
    /// monstro, e cobre também o caso de um cliente que não consegue ler a ficha vinculada (dono
    /// diferente da máquina local — ver comentário de VidaAtual).</param>
    public void Setup(MapManager manager, string nome = "", string imagemPath = "", string tokenId = "", string donoSteamId = "",
                       int fichasGeneralId = 0, int fichasDndId = 0, string ownerSteamId = "", int ownerPlayerId = 0,
                       string monstroId = "", string npcIdCampanha = "", float raioVisaoMetros = 18f,
                       int vidaAtualSalva = -1, bool bloqueadoSalvo = false, bool escondidoSalvo = false)
    {
        mapManager  = manager;
        NomeToken   = nome;
        ImagemPath  = imagemPath;
        TokenId     = string.IsNullOrWhiteSpace(tokenId)
                        ? System.Guid.NewGuid().ToString("N")
                        : tokenId;
        DonoSteamId = donoSteamId;

        FichasGeneralId = fichasGeneralId;
        FichasDndId     = fichasDndId;
        OwnerSteamId    = ownerSteamId;
        OwnerPlayerId   = ownerPlayerId;

        MonstroId = monstroId;

        NpcIdCampanha   = npcIdCampanha;
        RaioVisaoMetros = raioVisaoMetros > 0f ? raioVisaoMetros : 18f;

        spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        colisor        = GetComponent<Collider2D>();
        cam = null;

        Bloqueado = bloqueadoSalvo;
        Escondido = escondidoSalvo;
        _visualEscondidoAplicado = false;

        InicializarVida(vidaAtualSalva);
    }

    // ── Vida (HP) ─────────────────────────────────────────────────────────────

    private void InicializarVida(int vidaAtualSalva)
    {
        if (IsLinked)
        {
            LocalFichaData ficha = ObterDadosDaFicha();
            VidaMaxima = ficha?.vida_maxima ?? 0;
            VidaAtual  = vidaAtualSalva >= 0 ? Mathf.Min(vidaAtualSalva, VidaMaxima) : (ficha?.vida_atual ?? 0);
        }
        else if (IsMonstro)
        {
            MonstroDado monstro = ObterDadosDoMonstro();
            VidaMaxima = monstro != null ? TokenStatBlockBuilder.ExtrairPrimeiroNumero(monstro.hp) : 0;
            VidaAtual  = vidaAtualSalva >= 0 ? Mathf.Min(vidaAtualSalva, VidaMaxima) : VidaMaxima;
        }
        else
        {
            VidaMaxima = 0;
            VidaAtual  = 0;
        }
    }

    /// <summary>Re-lê a ficha do disco (ObterDadosDaFicha() já é fresco, sem cache próprio) e atualiza
    /// VidaMaxima/NomeToken quando mudarem desde a última checagem — ex: jogador subiu de nível ou
    /// renomeou o personagem fora da Sala enquanto o token já estava no mapa (antes, InicializarVida
    /// só rodava uma vez em Setup() e nunca mais, então o token ficava com HP máximo/nome antigos pra
    /// sempre). Chamado periodicamente por Update() (ver _timerRefrescoFicha). Não mexe em VidaAtual
    /// fora do clamp de segurança abaixo — combate continua sendo autoridade própria (ver
    /// DefinirVidaAtual). Não faz nada para token de monstro/sem ficha (chamador já filtra por
    /// IsLinked) nem se a ficha não puder ser lida (ex: token de outro jogador — ver ObterDadosDaFicha).</summary>
    private void RefrescarDadosDaFichaSeNecessario()
    {
        LocalFichaData ficha = ObterDadosDaFicha();
        if (ficha == null) return;

        if (ficha.vida_maxima != VidaMaxima)
        {
            VidaMaxima = ficha.vida_maxima;
            if (VidaAtual > VidaMaxima)
                DefinirVidaAtual(VidaMaxima);
        }

        if (!string.IsNullOrWhiteSpace(ficha.name_pc) && ficha.name_pc != NomeToken)
            NomeToken = ficha.name_pc;
    }

    /// <summary>Define a vida atual (clampada em 0..VidaMaxima) — chamado tanto pela aplicação local
    /// de dano/cura (ver AplicarDano) quanto pelo recebimento da rede (MapManager.ReceberDanoTokenRede),
    /// por isso NÃO broadcasta sozinho (ver AplicarDano pra isso). Se este token tem ficha vinculada E
    /// pertence a este jogador (DonoSteamId == meu SteamId), também persiste em LocalFichaStorage —
    /// preserva vida_temporaria/dado_vida/combate_extra_json do snapshot atual, só troca vida_atual.</summary>
    public void DefinirVidaAtual(int novoValor)
    {
        int clamped = Mathf.Clamp(novoValor, 0, VidaMaxima);
        if (clamped == VidaAtual) return;
        VidaAtual = clamped;

        if (!IsLinked) return;
        bool souDono = SteamClient.IsValid && OwnerSteamId == SteamClient.SteamId.ToString();
        if (!souDono) return;

        LocalFichaData ficha = ObterDadosDaFicha();
        if (ficha == null) return;

        LocalFichaStorage.SaveCombatState(OwnerSteamId, OwnerPlayerId, FichasGeneralId, FichasDndId,
            VidaAtual, ficha.vida_temporaria, ficha.dado_vida, ficha.combate_extra_json);
    }

    /// <summary>Aplica dano (positivo) ou cura (negativo/quantidade negativa) — chamado por
    /// SelecaoAlvoManager ao resolver um ataque/resistência/cura. Aplica localmente E propaga pra
    /// todos os outros clientes (ver NetworkSyncManager — o remetente nunca recebe a própria
    /// mensagem de volta, por isso a aplicação local é sempre explícita, não só via rede).</summary>
    public void AplicarDano(int quantidade)
    {
        DefinirVidaAtual(VidaAtual - quantidade);
        NetworkSyncManager.Instancia?.NotificarDanoToken(TokenId, VidaAtual);
    }

    // ── Flags (Lock/Esconder) ────────────────────────────────────────────────

    /// <summary>Aplica os flags localmente — chamado tanto pelo clique local (ver AlternarBloqueado/
    /// AlternarEscondido) quanto pelo recebimento da rede (MapManager.ReceberFlagsTokenRede).</summary>
    public void DefinirFlags(bool bloqueado, bool escondido)
    {
        Bloqueado = bloqueado;
        Escondido = escondido;
        _visualEscondidoAplicado = false; // força reavaliar o visual no próximo Update/LateUpdate
    }

    public void AlternarBloqueado()
    {
        DefinirFlags(!Bloqueado, Escondido);
        NetworkSyncManager.Instancia?.NotificarFlagsToken(TokenId, Bloqueado, Escondido);
    }

    public void AlternarEscondido()
    {
        DefinirFlags(Bloqueado, !Escondido);
        NetworkSyncManager.Instancia?.NotificarFlagsToken(TokenId, Bloqueado, Escondido);
    }

    /// <summary>
    /// Retorna os dados completos do monstro linkado (busca sob demanda no catálogo local de
    /// criaturas) — mesmo papel de ObterDadosDaFicha(), mas para tokens de monstro.
    /// </summary>
    public MonstroDado ObterDadosDoMonstro()
    {
        if (!IsMonstro)
        {
            VTTLog.TokenWarn($"Tentativa de acessar dados de monstro de um token não linkado a monstro ({NomeToken}).");
            return null;
        }

        MonstroDado dado = MonstroCatalogoLoader.CarregarPorId(MonstroId);
        if (dado == null)
            VTTLog.TokenError($"Falha ao recuperar os dados do monstro '{MonstroId}' para o token '{NomeToken}'.");

        return dado;
    }

    /// <summary>
    /// Retorna os dados completos da ficha linkada (busca sob demanda no LocalFichaStorage) —
    /// usado pelo menu de contexto (Atacar/Curar/etc.) e por qualquer outro sistema que precise
    /// dos atributos/HP/CA reais do personagem que este token representa.
    /// </summary>
    public LocalFichaData ObterDadosDaFicha()
    {
        if (!IsLinked)
        {
            VTTLog.TokenWarn($"Tentativa de acessar dados de um token não linkado ({NomeToken}).");
            return null;
        }

        if (LocalFichaStorage.TryGetFichaSnapshot(OwnerSteamId, OwnerPlayerId, FichasGeneralId, FichasDndId, out LocalFichaData ficha))
            return ficha;

        VTTLog.TokenError($"Falha ao recuperar os dados da ficha ID {FichasDndId} para o token '{NomeToken}'.");
        return null;
    }

    // Movimento respeita o lock (Bloqueado); o content de ferramentas (destravar incluído) usa
    // TenhoPermissaoSobreToken() diretamente, sem checar o lock — senão nunca daria pra destravar.
    private bool PodeMover() => TenhoPermissaoSobreToken() && !Bloqueado;

    /// <summary>Dono do token, host da sala, ou token sem dono definido — mesma regra usada pra
    /// mover, agora reaproveitada pelo content de ferramentas (Lock/Esconder/Duplicar).</summary>
    public bool TenhoPermissaoSobreToken()
    {
        // Modo offline / sem Steam — libera
        if (!SteamClient.IsValid)
        {
            VTTLog.Token($"[TenhoPermissaoSobreToken] '{NomeToken}': SteamClient inválido — libera (offline).");
            return true;
        }

        string meuId = SteamClient.SteamId.ToString();

        // Token sem dono definido → qualquer um
        if (string.IsNullOrWhiteSpace(DonoSteamId))
        {
            VTTLog.Token($"[TenhoPermissaoSobreToken] '{NomeToken}': DonoSteamId vazio — libera pra qualquer um.");
            return true;
        }

        // Dono do token pode sempre
        if (DonoSteamId == meuId)
        {
            VTTLog.Token($"[TenhoPermissaoSobreToken] '{NomeToken}': meuId ({meuId}) == DonoSteamId — libera.");
            return true;
        }

        // Host (dono do lobby) pode sempre
        if (EhHost())
        {
            VTTLog.Token($"[TenhoPermissaoSobreToken] '{NomeToken}': meuId ({meuId}) é host do lobby — libera.");
            return true;
        }

        VTTLog.Token($"[TenhoPermissaoSobreToken] '{NomeToken}': BLOQUEADO — meuId={meuId}, DonoSteamId={DonoSteamId}.");
        return false;
    }

    private SteamLobbyManager _lobbyCache;

    /// <summary>true se este cliente é o dono (host) da sala/lobby — usado tanto pela permissão de
    /// ferramentas quanto pelo visual de "escondido" (host continua vendo, semitransparente). Chamado
    /// todo frame por AtualizarVisualEscondido enquanto Escondido==true, por isso cacheia a
    /// referência do lobby em vez de refazer o FindFirstObjectByType de fallback sempre.</summary>
    public bool EhHost()
    {
        if (!SteamClient.IsValid) return false;

        if (_lobbyCache == null)
            _lobbyCache = mapManager != null ? mapManager.GetComponent<SteamLobbyManager>() : null;
        if (_lobbyCache == null)
            _lobbyCache = UnityEngine.Object.FindFirstObjectByType<SteamLobbyManager>();

        return _lobbyCache?.SalaAtual?.IsOwnedBy(SteamClient.SteamId) == true;
    }

    /// <summary>Aplica uma posição vinda da rede, interpolando suavemente.</summary>
    public void AplicarPosicaoRede(Vector3 pos)
    {
        posicaoAlvoRede    = pos;
        temPosicaoAlvoRede = true;
    }

    private Camera ObterCamera()
    {
        if (cam != null) return cam;
        if (mapManager != null && mapManager.cameraVTT != null)
            cam = mapManager.cameraVTT.GetComponent<Camera>();
        cam ??= Camera.main;
        return cam;
    }

    private bool EsteTokenEhOTopmost(Vector2 posicaoMundo)
    {
        Collider2D[] hits = Physics2D.OverlapPointAll(posicaoMundo);
        if (hits.Length == 0) return false;

        int       melhorOrdem = int.MinValue;
        Collider2D melhorHit  = null;

        foreach (Collider2D hit in hits)
        {
            SpriteRenderer sr = hit.GetComponentInChildren<SpriteRenderer>(true);
            if (sr == null) continue;
            if (sr.sortingOrder > melhorOrdem)
            {
                melhorOrdem = sr.sortingOrder;
                melhorHit   = hit;
            }
        }

        bool souEuOTopmost = melhorHit != null && melhorHit.gameObject == gameObject;

        // Instrumentação temporária (Bug E) — só loga quando há disputa real (mais de 1 collider no
        // ponto), pra investigar se sortingOrder diverge entre clientes em tokens sobrepostos.
        // Ativar via VTTLog.ativoToken = true.
        if (hits.Length > 1)
            VTTLog.Token($"[EsteTokenEhOTopmost] '{NomeToken}' (sortingOrder={GetComponentInChildren<SpriteRenderer>(true)?.sortingOrder}): " +
                $"{hits.Length} colliders no ponto, vencedor='{melhorHit?.gameObject.name}' (ordem={melhorOrdem}), euVenci={souEuOTopmost}.");

        return souEuOTopmost;
    }

    private void OnDisable()
    {
        EsconderTooltip();
    }

    private void EsconderTooltip()
    {
        if (_tooltipVisivel)
        {
            // SolicitarEsconder (não Esconder direto): se o mouse já entrou no próprio painel do
            // tooltip (indo clicar num botão de ação/magia), o manager ignora o pedido.
            TokenStatBlockTooltipManager.Instance?.SolicitarEsconder();
            _tooltipVisivel = false;
        }
        _hoverTimer       = 0f;
        _mouseEstavaOver  = false;
    }

    // Throttle do refresh de ficha (HP máximo/nome) — não precisa ser todo frame, ~1.5s é suficiente
    // pra parecer "automático" sem reler o JSON da ficha a cada Update().
    private float _timerRefrescoFicha;
    private const float IntervaloRefrescoFicha = 1.5f;

    // Estado do preview de caminho durante o arrasto — ver AtualizarPreviewDeCaminho.
    private (int col, int lin)  _celulaOrigemPath;
    private (int col, int lin)? _ultimoDestinoPath;
    private GridCellHighlightPool _highlightPoolCaminho;

    private void Update()
    {
        // Independente de InteracaoDeArrastoHabilitada (não depende de mouse/arrasto) — mantém
        // nome/vida máxima do token em sincronia com a ficha vinculada.
        if (IsLinked)
        {
            _timerRefrescoFicha += Time.deltaTime;
            if (_timerRefrescoFicha >= IntervaloRefrescoFicha)
            {
                _timerRefrescoFicha = 0f;
                RefrescarDadosDaFichaSeNecessario();
            }
        }

        // Cena somente-leitura (ex: mobile companion) — nenhuma interação de arrasto/menu/tooltip
        // por mouse faz sentido ali (toque abre stat block por outro caminho, ver DiceVTTMobileUI).
        if (!InteracaoDeArrastoHabilitada) return;

        Mouse mouse = Mouse.current;
        if (mouse == null || mapManager == null) return;

        if (mapManager.ModoMoverMapaAtivo != _ultimoModoMoverMapaAtivo)
        {
            VTTLog.Token($"[Update] '{NomeToken}': ModoMoverMapaAtivo mudou para {mapManager.ModoMoverMapaAtivo}.");
            _ultimoModoMoverMapaAtivo = mapManager.ModoMoverMapaAtivo;
        }
        if (mapManager.ModoMoverMapaAtivo) { arrastando = false; EsconderTooltip(); return; }

        Camera cameraAtual = ObterCamera();
        if (cameraAtual == null) return;

        Vector2 posicaoTela = mouse.position.ReadValue();

        // ── Hover tooltip ────────────────────────────────────────────────────
        if (arrastando)
        {
            EsconderTooltip();
            _mouseEstavaOver = false;
        }
        else
        {
            // Só acumula timer / mostra se o EventSystem não está consumindo o clique
            bool sobreUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            bool mouseOver = false;

            if (!sobreUI)
            {
                Camera cameraHover = ObterCamera();
                if (cameraHover != null)
                {
                    Vector3 mundo3D = cameraHover.ScreenToWorldPoint(
                        new Vector3(posicaoTela.x, posicaoTela.y, Mathf.Abs(cameraHover.transform.position.z)));
                    mouseOver = EsteTokenEhOTopmost(new Vector2(mundo3D.x, mundo3D.y));
                }

                if (mouseOver)
                {
                    _hoverTimer += Time.deltaTime;
                    if (!_tooltipVisivel && _hoverTimer >= TempoParaTooltip)
                        MostrarTooltip();
                }
            }

            // Hide sempre roda — independente do EventSystem
            if (!mouseOver && _mouseEstavaOver)
                EsconderTooltip();

            _mouseEstavaOver = mouseOver;
        }

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

        if (mouse.leftButton.wasPressedThisFrame && !arrastando)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            Vector3 mundo3D = cameraAtual.ScreenToWorldPoint(
                new Vector3(posicaoTela.x, posicaoTela.y, Mathf.Abs(cameraAtual.transform.position.z)));
            Vector2 mundo2D = new Vector2(mundo3D.x, mundo3D.y);

            // Existe uma ação/magia esperando um alvo (linha de mira aberta pelo tooltip) — este
            // clique confirma o alvo em vez de começar a arrastar o token.
            if (SelecaoAlvoManager.Instancia != null && SelecaoAlvoManager.Instancia.AguardandoAlvo && EsteTokenEhOTopmost(mundo2D))
            {
                SelecaoAlvoManager.Instancia.ConfirmarAlvoToken(this);
                return;
            }

            // Instrumentação temporária (Bug E: "jogador não consegue arrastar o token") — separa as
            // duas causas possíveis de um clique que não inicia o arrasto: não ser o topmost no ponto
            // (ver log em EsteTokenEhOTopmost) vs. PodeMover() bloquear por dono/host. Ativar via
            // VTTLog.ativoToken = true.
            bool souTopmost = EsteTokenEhOTopmost(mundo2D);
            if (souTopmost)
            {
                // Marca "este frame teve um clique que caiu em algum token" (lido por
                // SelecaoAlvoManager.LateUpdate pra não desmarcar a seleção à toa) e começa a
                // rastrear clique-vs-arrasto — independente de PodeMover(), já que um token
                // travado/sem permissão ainda precisa poder ser selecionado pra destravar (ver
                // bloco de clique-sem-arrasto logo abaixo do if (arrastando)).
                CliqueConsumidoEsteFrame = true;
                _rastreandoCliqueSelecao = true;
                _posicaoTelaInicioClique = posicaoTela;
            }

            if (souTopmost && !PodeMover())
                VTTLog.Token($"[Update] '{NomeToken}': era o topmost no clique mas PodeMover() bloqueou — arrasto não iniciado.");

            if (souTopmost && PodeMover())
            {
                Vector3 mouseMundo = mapManager.CamParaMundoToken(cameraAtual, posicaoTela);
                offset    = transform.position - mouseMundo;
                arrastando = true;
                // Parar interpolação de rede enquanto o jogador arrasta localmente
                temPosicaoAlvoRede = false;

                // Guarda a célula de origem do path — o preview recalcula a partir daqui a cada
                // mudança de célula-alvo (ver AtualizarPreviewDeCaminho).
                _celulaOrigemPath = mapManager.MundoParaCelula(transform.position);
                _ultimoDestinoPath = null;

                // Avisa os outros (e a ponte mobile) de quem começou a segurar este token
                NetworkSyncManager.Instancia?.NotificarInicioArrasto(TokenId);
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
                    mapManager.AtualizarVisaoDinamicaGeral(); // acompanha o arrasto em tempo real
                    AtualizarPreviewDeCaminho(alvo);
                }
            }
            else
            {
                arrastando = false;
                _rastreandoCliqueSelecao = false;
                EsconderPreviewDeCaminho();
                FinalizarInteracaoDeArrasto(posicaoTela);
            }
        }
        else if (_rastreandoCliqueSelecao && !mouse.leftButton.isPressed)
        {
            // Token travado ou sem PodeMover() — nunca virou um arrasto de verdade, mas ainda
            // conta como clique de seleção (senão nunca daria pra selecionar um token travado
            // pra destravá-lo).
            _rastreandoCliqueSelecao = false;
            if (Vector2.Distance(posicaoTela, _posicaoTelaInicioClique) < LimiarCliqueEmPixels)
                AlternarSelecao();
        }

        AtualizarVisualEscondido();
    }

    // Distingue "só cliquei" (sem deslocamento de verdade) de "arrastei o token" — no primeiro
    // caso alterna a seleção em vez de finalizar um movimento (nada moveu, não há por que notificar
    // a rede nem salvar o estado).
    private void FinalizarInteracaoDeArrasto(Vector2 posicaoTelaSolta)
    {
        if (Vector2.Distance(posicaoTelaSolta, _posicaoTelaInicioClique) < LimiarCliqueEmPixels)
        {
            AlternarSelecao();
        }
        else
        {
            transform.position = mapManager.AjustarPosicaoTokenAoGrid(transform.position);
            NetworkSyncManager.Instancia?.NotificarMovimentoToken(TokenId, transform.position);
            mapManager.SalvarEstadoVTT();
        }

        // Sempre notifica o fim do "segurando" (independente de ter sido clique ou arrasto de
        // verdade) — quem estava mostrando o rótulo "X está segurando" nos outros clientes precisa
        // saber que soltou de qualquer forma.
        NetworkSyncManager.Instancia?.NotificarFimArrasto(TokenId);
        mapManager.AtualizarVisaoDinamicaGeral();
    }

    /// <summary>Recalcula e mostra a trilha do menor caminho (Dijkstra, ver PathfindingGrid) da origem
    /// do arrasto até a célula sob o cursor — só recalcula quando a célula-alvo muda (não todo frame),
    /// reaproveitando GridCellHighlightPool (mesmo pool já usado por SelecaoAlvoManager pra range de
    /// magia/AoE). Esconde o preview se não houver caminho (ex: destino cercado por paredes).</summary>
    private void AtualizarPreviewDeCaminho(Vector3 alvoMundo)
    {
        (int col, int lin) destino = mapManager.MundoParaCelula(alvoMundo);
        if (_ultimoDestinoPath.HasValue && _ultimoDestinoPath.Value == destino) return;
        _ultimoDestinoPath = destino;

        List<(int col, int lin)> caminho = mapManager.CalcularCaminhoDeToken(_celulaOrigemPath, destino);
        if (caminho == null || caminho.Count == 0)
        {
            EsconderPreviewDeCaminho();
            return;
        }

        _highlightPoolCaminho ??= new GridCellHighlightPool(mapManager.transform, new Color(1f, 1f, 1f, 0.35f), 900);

        var centros = new List<Vector3>(caminho.Count);
        foreach ((int col, int lin) celula in caminho)
            centros.Add(mapManager.CelulaParaBoundsMundo(celula.col, celula.lin).center);

        _highlightPoolCaminho.Mostrar(centros, mapManager.ObterTamanhoAtualDaCelulaEmMetros());
    }

    private void EsconderPreviewDeCaminho() => _highlightPoolCaminho?.Esconder();

    // Sprite/collider deste token conforme o flag Escondido — Mestre continua vendo (semitransparente,
    // ainda clicável/selecionável), jogadores comuns não veem nem conseguem mirar nele (sprite e
    // collider desativados). Só reaplica quando o estado relevante muda (flag OU se este cliente virou/
    // deixou de ser host), pra não escrever em spriteRenderer/collider todo frame à toa.
    private void AtualizarVisualEscondido()
    {
        bool souHostAgora = Escondido && EhHost();
        if (_visualEscondidoAplicado && souHostAgora == _ultimoEstadoHostParaEscondido) return;

        _visualEscondidoAplicado = true;
        _ultimoEstadoHostParaEscondido = souHostAgora;

        if (!Escondido)
        {
            if (spriteRenderer != null) { spriteRenderer.enabled = true; spriteRenderer.color = Color.white; }
            if (colisor != null) colisor.enabled = true;
        }
        else if (souHostAgora)
        {
            if (spriteRenderer != null) { spriteRenderer.enabled = true; spriteRenderer.color = new Color(1f, 1f, 1f, 0.5f); }
            if (colisor != null) colisor.enabled = true;
        }
        else
        {
            if (spriteRenderer != null) spriteRenderer.enabled = false;
            if (colisor != null) colisor.enabled = false;
        }
    }

    // Tokens não linkados (nem ficha, nem monstro) não têm de onde puxar perícias/ações/magias —
    // o stat block fica sem sentido pra eles, então simplesmente não mostra nada.
    private void MostrarTooltip()
    {
        if (TokenStatBlockTooltipManager.Instance == null || (!IsLinked && !IsMonstro))
            return;

        _tooltipVisivel = true;
        TokenStatBlockTooltipManager.Instance.Mostrar(this, ModoStatBlockTooltip.Hover);
    }

    // Clique direito no token — abre o menu de atalho genérico (UI/MenuAtalho). Atacar/Curar abrem
    // o stat block completo (TokenStatBlockTooltipManager) já filtrado pro modo escolhido; a
    // resolução em si (rolar, aplicar dano/cura na vida do alvo) só acontece depois que o jogador
    // clica numa ação/magia do stat block e escolhe o alvo (ver SelecaoAlvoManager.AplicarDano).
    private void AbrirMenuDeAtalho(Vector2 posicaoTela)
    {
        if (MenuAtalhoManager.Instance == null)
        {
            VTTLog.TokenWarn("MenuAtalhoManager não encontrado na cena — não é possível abrir o menu de atalho.");
            return;
        }

        EsconderTooltip();

        var opcoes = new List<OpcaoMenuAtalho>
        {
            new OpcaoMenuAtalho("Atacar",     () => TokenStatBlockTooltipManager.Instance?.Mostrar(this, ModoStatBlockTooltip.Atacar)),
            new OpcaoMenuAtalho("Curar",      () => TokenStatBlockTooltipManager.Instance?.Mostrar(this, ModoStatBlockTooltip.Curar)),
            // Todas as perícias/salvaguardas (treinadas ou não) — clicar rola 1d20 + modificador e
            // posta no chat (ver TokenStatBlockTooltipUI.OnClicarTeste).
            new OpcaoMenuAtalho("Testes",     () => TokenStatBlockTooltipManager.Instance?.Mostrar(this, ModoStatBlockTooltip.Testes)),
            // Rola iniciativa (1d20 + mod. de Destreza, se o token tiver ficha vinculada) e manda
            // pro chat + lista de turnos (ver SocialTurnosManager.RolarTurnoPara).
            new OpcaoMenuAtalho("Rolar Turno", () => SocialTurnosManager.Instance?.RolarTurnoPara(this)),
            new OpcaoMenuAtalho("Arremessar", () => VTTLog.Token($"[MenuAtalho] Arremessar com '{NomeToken}' (ainda não implementado)")),
            new OpcaoMenuAtalho("Editar",     () => VTTLog.Token($"[MenuAtalho] Editar '{NomeToken}' (ainda não implementado)")),
            // Ferramenta do HOST (spotlight de visão dinâmica, ver MapManager.MostrarVisaoDoToken) —
            // não afeta o fog-of-war automático dos jogadores comuns, que já é sempre calculado.
            new OpcaoMenuAtalho("Selecionar Visão", () => mapManager?.MostrarVisaoDoToken(this)),
        };

        // Só aparece pra tokens de NPC enviados de Campanha (ver CampanhaParaSalaConverter) —
        // visualização somente-leitura do stat block/lore completo (ver SalaNpcStatBlockLoreUI).
        if (TemNpcDeCampanha)
            opcoes.Add(new OpcaoMenuAtalho("Lore", () => mapManager?.MostrarLoreDoNpc(this)));

        // Remove só esta instância do tabuleiro — o token continua na lista de tokens do
        // catálogo (pra arrastar de novo) e a ficha/monstro linkado não é tocado.
        opcoes.Add(new OpcaoMenuAtalho("Excluir", () => mapManager?.RemoverTokenDoMapa(TokenId), ehExclusao: true));

        MenuAtalhoManager.Instance.Mostrar(posicaoTela, opcoes);
    }

    private void LateUpdate()
    {
        // Interpola suavemente em direção à posição recebida da rede
        if (!temPosicaoAlvoRede || arrastando) return;

        transform.position = Vector3.MoveTowards(
            transform.position,
            posicaoAlvoRede,
            Time.deltaTime * 12f);
        mapManager?.AtualizarVisaoDinamicaGeral(); // acompanha o movimento recebido da rede em tempo real

        if (Vector3.Distance(transform.position, posicaoAlvoRede) < 0.001f)
        {
            transform.position = posicaoAlvoRede;
            temPosicaoAlvoRede = false;
        }
    }
}
