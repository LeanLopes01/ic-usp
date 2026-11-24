using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Fluxo "clicar numa ação/magia do tooltip → mirar → confirmar o alvo (token ou quadrado) → rolar e
/// postar no chat". Precisa desenhar/seguir uma linha de mira a cada frame, por isso é um
/// MonoBehaviour singleton (era uma classe static sem geometria nenhuma) — mesmo padrão de
/// `TokenStatBlockTooltipManager`/`BarraFerramentasManager.Instancia`. Vive num GameObject sempre
/// ativo na cena de Salas (ex: junto de MapManager).
///
/// `MapTokenDraggable.Update()` chama `ConfirmarAlvoToken` quando o clique esquerdo cai em cima de um
/// token com uma seleção pendente. Cliques que NÃO caem em nenhum token são tratados aqui mesmo, em
/// `LateUpdate()` — roda depois de TODO `Update()` da cena (inclusive o de `MapTokenDraggable`, que
/// pode já ter confirmado e cancelado a seleção neste mesmo frame), então só sobra o clique que
/// realmente não acertou nenhum token.
///
/// Fase 2 (destaque de grid): dois `GridCellHighlightPool` — um pro anel de ALCANCE (fixo, ao redor
/// do token de origem, só se `AcaoStatBlock.alcanceQuadrados` foi extraído) e um pra PRÉVIA DE ÁREA
/// (segue o mouse, raio ajustável com +/- durante a mira — fallback manual quando a extração via
/// regex não acha nada ou erra, ver AlcanceExtrator).
/// </summary>
public class SelecaoAlvoManager : MonoBehaviour
{
    public static SelecaoAlvoManager Instancia { get; private set; }

    [Header("Linha de Mira")]
    [SerializeField] private Color corLinha = Color.red;
    [SerializeField, Range(0.005f, 0.2f)] private float espessuraLinha = 0.05f;

    [Header("Destaques de Grid")]
    [SerializeField] private Color corAlcance = new Color(1f, 0.9f, 0.2f, 0.35f);
    [SerializeField] private Color corArea = new Color(0f, 0f, 0f, 140f / 255f);
    private const int RaioAreaMaximoQuadrados = 10;

    private MapTokenDraggable _origem;
    private AcaoStatBlock _acao;
    private bool _ehCura;

    private LineRenderer _linha;
    private Material _materialLinha;

    private GridCellHighlightPool _poolAlcance;
    private GridCellHighlightPool _poolArea;
    private int _raioAreaAtualQuadrados;
    private Vector3? _ultimaCelulaMouseSnapada;
    private int _ultimoRaioAreaDesenhado = -1;

    // Última lista de células da prévia de área (ver AtualizarPreviaDeArea) — reaproveitada em
    // Resolver() pra achar todos os tokens dentro da área confirmada, sem recalcular a geometria.
    private readonly List<Vector3> _celulasAreaAtual = new List<Vector3>();

    private MapManager _mapManager;
    private ChatManager _chatManager;

    public bool AguardandoAlvo { get; private set; }

    private void Awake()
    {
        Instancia = this;
        CriarLinha();
        _poolAlcance = new GridCellHighlightPool(transform, corAlcance, sortingOrder: 998);
        _poolArea = new GridCellHighlightPool(transform, corArea, sortingOrder: 999);
    }

    private void OnDestroy()
    {
        if (Instancia == this) Instancia = null;
        if (_materialLinha != null) Destroy(_materialLinha);
    }

    /// <summary>Chamado pelo clique numa linha de ação/magia do tooltip (ver
    /// TokenStatBlockTooltipUI.OnClicarAcao) — abre a linha de mira, ainda sem rolar nada.</summary>
    public void IniciarSelecao(MapTokenDraggable origem, AcaoStatBlock acao, bool ehMagia, bool ehCura)
    {
        if (origem == null || acao == null) return;

        _origem = origem;
        _acao = acao;
        _ehCura = ehCura;
        AguardandoAlvo = true;

        if (_linha != null) _linha.enabled = true;

        // Prévia de área começa no valor extraído (0 = oculta até o jogador teclar +).
        _raioAreaAtualQuadrados = Mathf.Clamp(acao.raioAreaQuadrados, 0, RaioAreaMaximoQuadrados);
        _ultimaCelulaMouseSnapada = null;
        _ultimoRaioAreaDesenhado = -1;
        _celulasAreaAtual.Clear();

        // Anel de alcance é fixo (propriedade da magia, não muda durante a mira) — desenhado uma vez.
        // Sempre Circulo: alcance é inerentemente um raio, independente da forma da própria área de
        // efeito (que pode ser cone/linha/quadrado — ver acao.formaArea).
        MapManager mapManager = ObterMapManager();
        if (acao.alcanceQuadrados > 0 && !acao.origemNoToken && mapManager != null)
        {
            Vector3 origemSnapada = mapManager.AjustarPosicaoTokenAoGrid(origem.transform.position);
            float tamanhoCelula = mapManager.ObterTamanhoAtualDaCelulaEmMetros();
            _poolAlcance.Mostrar(FormaAreaGeometria.GerarCelulas(FormaAreaAcao.Circulo, origemSnapada, origemSnapada, acao.alcanceQuadrados, tamanhoCelula), tamanhoCelula);
        }
        else
        {
            _poolAlcance.Esconder();
        }

        VTTLog.Token($"[SelecaoAlvo] Mirando '{acao.nome}' de '{origem.NomeToken}' — clique num token/quadrado pra confirmar, ESC ou botão direito pra cancelar.");
    }

    public void Cancelar()
    {
        AguardandoAlvo = false;
        _origem = null;
        _acao = null;
        if (_linha != null) _linha.enabled = false;
        _poolAlcance.Esconder();
        _poolArea.Esconder();
    }

    /// <summary>Chamado por MapTokenDraggable.Update() quando o clique esquerdo cai em cima de um
    /// token com uma seleção pendente.</summary>
    public void ConfirmarAlvoToken(MapTokenDraggable alvo)
    {
        if (!AguardandoAlvo) return;
        if (alvo != null && !DentroDoAlcance(alvo.transform.position)) return;
        Resolver(alvo);
    }

    /// <summary>Chamado por este próprio LateUpdate quando o clique esquerdo não caiu em cima de
    /// nenhum token — sem área ativa, o quadrado clicado não vira um "alvo" de verdade (resolve sem
    /// nomear ninguém); com área ativa, o clique só CONFIRMA a posição — quem realmente é afetado
    /// são os tokens dentro da área (ver ObterTokensNaArea em Resolver).</summary>
    public void ConfirmarAlvoQuadrado(Vector3 posicaoMundo)
    {
        if (!AguardandoAlvo) return;
        if (!DentroDoAlcance(posicaoMundo)) return;
        Resolver(null);
    }

    // Alcance desconhecido (0) não restringe nada — comportamento de hoje. Só passa a restringir
    // quando AlcanceExtrator conseguiu um número de verdade pra essa ação/magia.
    private bool DentroDoAlcance(Vector3 posicaoAlvo)
    {
        // Magia "Self" não tem alvo fora de alcance — a origem é sempre o próprio token, o clique só
        // define a direção do cone/linha (ver AtualizarPreviaDeArea).
        if (_acao == null || _acao.alcanceQuadrados <= 0 || _acao.origemNoToken) return true;

        MapManager mapManager = ObterMapManager();
        if (mapManager == null || _origem == null) return true;

        float tamanhoCelula = mapManager.ObterTamanhoAtualDaCelulaEmMetros();
        if (tamanhoCelula <= 0f) return true;

        Vector3 origemSnapada = mapManager.AjustarPosicaoTokenAoGrid(_origem.transform.position);
        Vector3 alvoSnapado = mapManager.AjustarPosicaoTokenAoGrid(posicaoAlvo);

        int dx = Mathf.RoundToInt((alvoSnapado.x - origemSnapada.x) / tamanhoCelula);
        int dy = Mathf.RoundToInt((alvoSnapado.y - origemSnapada.y) / tamanhoCelula);
        int distanciaQuadrados = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));

        if (distanciaQuadrados > _acao.alcanceQuadrados)
        {
            VTTLog.Token($"[SelecaoAlvo] Alvo fora do alcance de '{_acao.nome}' ({distanciaQuadrados}/{_acao.alcanceQuadrados} quadrados) — clique ignorado.");
            return false;
        }
        return true;
    }

    // Com área ativa, TODOS os tokens dentro dela são resolvidos (um post por token); sem área,
    // só alvoUnico (ou nenhum, se foi um quadrado vazio clicado sem área). Cura/Resistência/Ataque
    // cada uma decide sozinha como tratar dano compartilhado vs. por-alvo (ver os 3 métodos abaixo).
    private void Resolver(MapTokenDraggable alvoUnico)
    {
        ChatManager chat = ObterChatManager();
        if (chat != null)
        {
            if (!chat.EstaAbertoPainel()) chat.AbrirPainel();

            bool areaAtiva = _raioAreaAtualQuadrados > 0;
            List<MapTokenDraggable> alvos = areaAtiva
                ? ObterTokensNaArea()
                : (alvoUnico != null ? new List<MapTokenDraggable> { alvoUnico } : null);

            if (_ehCura)
                ResolverCura(chat, alvos);
            else if (_acao.ehResistencia && _acao.cd > 0)
                ResolverResistencia(chat, alvos);
            else
                ResolverAtaque(chat, alvos);
        }

        string descricaoLog = _raioAreaAtualQuadrados > 0 ? $" em área" : (alvoUnico != null ? $" em {alvoUnico.NomeToken}" : "");
        VTTLog.Token($"[SelecaoAlvo] '{_origem?.NomeToken}' usou '{_acao?.nome}'{descricaoLog}.");
        Cancelar();
    }

    // Cura: mesma rolagem (compartilhada se em área — mesmo valor de cura pra todos, regra simples
    // por ora) postada uma vez por alvo, ou sem nome nenhum se não houver alvo (quadrado vazio, sem
    // área ativa).
    private void ResolverCura(ChatManager chat, List<MapTokenDraggable> alvos)
    {
        ResultadoRolagem resultadoCura = RolagemDados.RolarNotacaoDetalhada(_acao.descricao);

        if (alvos == null || alvos.Count == 0)
        {
            chat.EnviarTesteGeral($"Cura ({_acao.nome})", resultadoCura, ResultadoCritico.Normal);
            return;
        }

        // Um envelope por alvo, mas UM SÓ ServerRpc/ClientRpc pro lote inteiro (ver
        // ChatManager.EnviarEnvelopesEmLote) — evita N despachos de RPC pra uma única resolução em
        // área (era o gargalo real de performance, não os cálculos de posição/token).
        var envelopes = new List<ChatEnvelope>(alvos.Count);
        foreach (MapTokenDraggable alvo in alvos)
        {
            envelopes.Add(chat.MontarEnvelopeDeTeste($"Cura ({_acao.nome}) em {alvo.NomeToken}", resultadoCura, ResultadoCritico.Normal));
            // Cura = dano negativo (MapTokenDraggable.AplicarDano já clampa em VidaMaxima).
            alvo.AplicarDano(-resultadoCura.total);
        }
        chat.EnviarEnvelopesEmLote(envelopes);
    }

    // Resistência de verdade (5e): o dano é rolado UMA vez (compartilhado, regra de mesa padrão),
    // mas CADA alvo faz o PRÓPRIO teste (d20 + modificador daquele atributo naquele token) contra a
    // CD — sucesso = metade do dano, falha = dano cheio. Sem alvo nenhum, só posta o dano bruto (não
    // tem quem testar).
    private void ResolverResistencia(ChatManager chat, List<MapTokenDraggable> alvos)
    {
        ResultadoRolagem resultadoDano = RolagemDados.RolarNotacaoDetalhada(_acao.descricao);

        if (alvos == null || alvos.Count == 0)
        {
            chat.EnviarTesteGeral($"{_acao.nome} — Dano", resultadoDano, ResultadoCritico.Normal);
            return;
        }

        // Um envelope por alvo (cada um com sua PRÓPRIA rolagem de resistência), mas UM SÓ
        // ServerRpc/ClientRpc pro lote inteiro — ver ChatManager.EnviarEnvelopesEmLote.
        var envelopes = new List<ChatEnvelope>(alvos.Count);
        foreach (MapTokenDraggable alvo in alvos)
        {
            int modificador = ObterModificadorSalvaguarda(alvo, _acao.atributoResistencia);
            ResultadoRolagem resultadoSalva = RolagemDados.RolarNotacaoDetalhada(RolagemDados.FormatarNotacaoD20(modificador));
            bool sucesso = resultadoSalva.total >= _acao.cd;
            int danoEfetivo = sucesso ? resultadoDano.total / 2 : resultadoDano.total;
            ResultadoCritico criticoSalva = RolagemDados.ClassificarD20(resultadoSalva.dados[0].valor);

            // Reaproveita o prefab de Ataque (2 rolagens, cada uma com tooltip de dado — ver
            // DadosRolagemTooltipManager) em vez de Teste (só 1 rolagem): resistência tem TANTO o
            // teste (d20+mod, aqui no lugar de "ataque") QUANTO o dano — mesma estrutura de
            // ataque+dano, só que quem rola o "ataque" é o ALVO (o teste dele), não quem lançou.
            // O total de dano já vem AJUSTADO (metade no sucesso); os dados mostrados no tooltip são
            // os da rolagem completa original, antes de dividir — o total exibido é que reflete o
            // resultado de verdade aplicado.
            ResultadoRolagem resultadoDanoEfetivo = new ResultadoRolagem
            {
                notacao = resultadoDano.notacao,
                dados = resultadoDano.dados,
                modificador = resultadoDano.modificador,
                total = danoEfetivo,
            };

            envelopes.Add(chat.MontarEnvelopeDeAtaque(
                $"{alvo.NomeToken}: {_acao.nome} ({_acao.atributoResistencia}) — {(sucesso ? "Sucesso" : "Falha")}",
                resultadoSalva, criticoSalva,
                resultadoDanoEfetivo, ResultadoCritico.Normal));
            alvo.AplicarDano(danoEfetivo);
        }
        chat.EnviarEnvelopesEmLote(envelopes);
    }

    // Ataque (comportamento de antes desta fase, agora repetível por vários alvos em área): rola
    // ataque (d20 + bônus) e dano UMA vez (compartilhados — mesmo golpe, mesmo dado, sem verificação
    // de acerto individual por alvo, ver decisão da Fase 1) e posta um resultado por alvo.
    private void ResolverAtaque(ChatManager chat, List<MapTokenDraggable> alvos)
    {
        // Rola o d20 pelo mesmo caminho detalhado da notação — o "natural" pra classificar crítico é
        // só o único dado dessa rolagem (ver RolagemDados.FormatarNotacaoD20).
        ResultadoRolagem resultadoAtaque = RolagemDados.RolarNotacaoDetalhada(RolagemDados.FormatarNotacaoD20(_acao.bonusAtaque));
        int natural = resultadoAtaque.dados[0].valor;
        ResultadoCritico critAtaque = RolagemDados.ClassificarD20(natural);
        // Dano não duplica no crítico ainda (regra simples por ora) — só o d20 do ataque reflete o crítico.
        ResultadoRolagem resultadoDano = RolagemDados.RolarNotacaoDetalhada(_acao.descricao);

        if (alvos == null || alvos.Count == 0)
        {
            chat.EnviarRolagemDeAtaque(_acao.nome, resultadoAtaque, critAtaque, resultadoDano, ResultadoCritico.Normal);
            return;
        }

        // Um envelope por alvo (mesma rolagem compartilhada), mas UM SÓ ServerRpc/ClientRpc pro
        // lote inteiro — ver ChatManager.EnviarEnvelopesEmLote.
        var envelopes = new List<ChatEnvelope>(alvos.Count);
        foreach (MapTokenDraggable alvo in alvos)
        {
            envelopes.Add(chat.MontarEnvelopeDeAtaque($"{_acao.nome} em {alvo.NomeToken}", resultadoAtaque, critAtaque, resultadoDano, ResultadoCritico.Normal));
            // Dano compartilhado (regra simples de hoje) aplicado a cada alvo independente do
            // resultado do d20 de ataque — mesma decisão já em vigor pro texto do chat (Fase 1).
            alvo.AplicarDano(resultadoDano.total);
        }
        chat.EnviarEnvelopesEmLote(envelopes);
    }

    // Todos os tokens do mapa cuja célula (snapada) bate com alguma célula da prévia de área
    // confirmada — mesma lista já usada pra desenhar a prévia (ver AtualizarPreviaDeArea), não
    // recalcula geometria. Sem accessor público de "todos os tokens" em MapManager, busca direto.
    private List<MapTokenDraggable> ObterTokensNaArea()
    {
        var resultado = new List<MapTokenDraggable>();
        MapManager mapManager = ObterMapManager();
        if (mapManager == null || _celulasAreaAtual.Count == 0) return resultado;

        foreach (MapTokenDraggable token in FindObjectsByType<MapTokenDraggable>(FindObjectsSortMode.None))
        {
            Vector3 celulaToken = mapManager.AjustarPosicaoTokenAoGrid(token.transform.position);
            if (_celulasAreaAtual.Contains(celulaToken))
                resultado.Add(token);
        }
        return resultado;
    }

    // Modificador de salvaguarda de UM token pro atributo testado — reaproveita
    // TokenStatBlockTooltipManager.ConstruirDados (modo Testes) em vez de duplicar o branching
    // ficha/monstro. 0 se o token não tiver ficha/monstro linkado ou não achar o atributo.
    private static int ObterModificadorSalvaguarda(MapTokenDraggable token, string atributo)
    {
        if (token == null || string.IsNullOrWhiteSpace(atributo)) return 0;

        TokenStatBlockData dados = TokenStatBlockTooltipManager.ConstruirDados(token, ModoStatBlockTooltip.Testes);
        if (dados == null) return 0;

        foreach (TesteStatBlock teste in dados.salvaguardasTestaveis)
            if (string.Equals(teste.nome, atributo, System.StringComparison.OrdinalIgnoreCase))
                return teste.modificador;

        return 0;
    }

    // ── Linha de mira (mesmo padrão de Campanhas/Grid/WallEditorTools.CriarPreviewLinha) ─────────

    private void CriarLinha()
    {
        var go = new GameObject("SelecaoAlvo_LinhaDeMira");
        go.transform.SetParent(transform, false);

        _linha = go.AddComponent<LineRenderer>();
        _linha.useWorldSpace = true;
        _linha.positionCount = 2;
        _materialLinha = new Material(Shader.Find("Sprites/Default"));
        _linha.material = _materialLinha;
        _linha.startColor = corLinha;
        _linha.endColor = corLinha;
        _linha.startWidth = espessuraLinha;
        _linha.endWidth = espessuraLinha;
        _linha.numCapVertices = 4;
        _linha.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _linha.receiveShadows = false;
        _linha.generateLightingData = false;
        _linha.sortingOrder = 1000; // sempre por cima de mapa/grid/tokens/paredes
        _linha.enabled = false;
    }

    private void Update()
    {
        if (!AguardandoAlvo) return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cancelar();
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.rightButton.wasPressedThisFrame)
        {
            Cancelar();
            return;
        }

        AtualizarAjusteManualDeRaio();

        Camera cam = ObterCamera();
        MapManager mapManager = ObterMapManager();
        if (cam == null || _origem == null || _linha == null || mapManager == null) return;

        Vector2 posicaoTela = mouse.position.ReadValue();
        Vector3 mundo = mapManager.CamParaMundoToken(cam, posicaoTela);

        _linha.SetPosition(0, _origem.transform.position);
        _linha.SetPosition(1, mundo);

        AtualizarPreviaDeArea(mapManager, mundo);
    }

    // Teclado (não scroll — scroll do mouse já é usado pela câmera pra zoom, ver VTTCamController)
    // ajusta o raio da prévia de área. É o fallback "pede pro jogador" quando AlcanceExtrator não
    // achou nada (ou achou errado) pra essa ação/magia.
    private void AtualizarAjusteManualDeRaio()
    {
        if (Keyboard.current == null) return;

        bool aumentar = Keyboard.current.numpadPlusKey.wasPressedThisFrame || Keyboard.current.equalsKey.wasPressedThisFrame;
        bool diminuir = Keyboard.current.numpadMinusKey.wasPressedThisFrame || Keyboard.current.minusKey.wasPressedThisFrame;

        if (aumentar) _raioAreaAtualQuadrados = Mathf.Clamp(_raioAreaAtualQuadrados + 1, 0, RaioAreaMaximoQuadrados);
        else if (diminuir) _raioAreaAtualQuadrados = Mathf.Clamp(_raioAreaAtualQuadrados - 1, 0, RaioAreaMaximoQuadrados);
    }

    // Recalcula/reposiciona o pool de área. Pra Cone/Linha a direção muda continuamente com o mouse
    // (não só quando a célula snapada muda) — precisa recalcular todo frame pra girar suave; pra
    // Quadrado/Circulo (sem direção) mantém o cache de "só recalcula se célula ou raio mudaram".
    // Origem da forma: célula do TOKEN se acao.origemNoToken (magia Self — só a direção segue o
    // mouse), senão a célula sob o mouse (mira normal, mesma de hoje).
    private void AtualizarPreviaDeArea(MapManager mapManager, Vector3 posicaoMundoMouseContinua)
    {
        if (_raioAreaAtualQuadrados <= 0)
        {
            if (_ultimoRaioAreaDesenhado != 0) _poolArea.Esconder();
            _ultimoRaioAreaDesenhado = 0;
            _ultimaCelulaMouseSnapada = null;
            _celulasAreaAtual.Clear();
            return;
        }

        Vector3 celulaMouseSnapada = mapManager.AjustarPosicaoTokenAoGrid(posicaoMundoMouseContinua);
        Vector3 origemDaArea = _acao.origemNoToken
            ? mapManager.AjustarPosicaoTokenAoGrid(_origem.transform.position)
            : celulaMouseSnapada;

        bool formaDependeDaDirecao = _acao.formaArea == FormaAreaAcao.Cone || _acao.formaArea == FormaAreaAcao.Linha;
        bool celulaMudou = !_ultimaCelulaMouseSnapada.HasValue || _ultimaCelulaMouseSnapada.Value != celulaMouseSnapada;
        bool raioMudou = _ultimoRaioAreaDesenhado != _raioAreaAtualQuadrados;
        if (!formaDependeDaDirecao && !celulaMudou && !raioMudou) return;

        float tamanhoCelula = mapManager.ObterTamanhoAtualDaCelulaEmMetros();
        List<Vector3> celulas = FormaAreaGeometria.GerarCelulas(_acao.formaArea, origemDaArea, posicaoMundoMouseContinua, _raioAreaAtualQuadrados, tamanhoCelula);
        _poolArea.Mostrar(celulas, tamanhoCelula);

        // Guardada pra Resolver() achar todos os tokens na área confirmada, sem recalcular geometria.
        _celulasAreaAtual.Clear();
        _celulasAreaAtual.AddRange(celulas);

        _ultimaCelulaMouseSnapada = celulaMouseSnapada;
        _ultimoRaioAreaDesenhado = _raioAreaAtualQuadrados;
    }

    // Roda DEPOIS de todo Update() da cena — se MapTokenDraggable.Update() já confirmou um alvo-token
    // neste mesmo frame, AguardandoAlvo já está false aqui e nada acontece; só sobra o clique que não
    // caiu em cima de nenhum token.
    private void LateUpdate()
    {
        // Desmarca a seleção de token (ver MapTokenDraggable.TokenSelecionado) quando o clique não
        // caiu em cima de nenhum token — MapTokenDraggable.CliqueConsumidoEsteFrame já foi setado por
        // todo Update() da cena a esta altura (LateUpdate roda por último), então dá pra confiar nele
        // aqui. Não desmarca durante uma mira ativa (AguardandoAlvo) — esse clique é pra confirmar
        // alvo, não pra alterar seleção.
        Mouse mouseParaSelecao = Mouse.current;
        if (mouseParaSelecao != null && mouseParaSelecao.leftButton.wasPressedThisFrame
            && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
            && !MapTokenDraggable.CliqueConsumidoEsteFrame && !AguardandoAlvo)
        {
            MapTokenDraggable.LimparSelecao();
        }
        MapTokenDraggable.CliqueConsumidoEsteFrame = false;

        if (!AguardandoAlvo) return;

        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        Camera cam = ObterCamera();
        MapManager mapManager = ObterMapManager();
        if (cam == null || mapManager == null) return;

        Vector2 posicaoTela = mouse.position.ReadValue();
        Vector3 mundo = mapManager.CamParaMundoToken(cam, posicaoTela);
        ConfirmarAlvoQuadrado(mundo);
    }

    // ── Referências (resolvidas sob demanda, cacheadas) ─────────────────────────

    private MapManager ObterMapManager()
    {
        if (_mapManager == null) _mapManager = FindFirstObjectByType<MapManager>();
        return _mapManager;
    }

    private ChatManager ObterChatManager()
    {
        if (_chatManager == null) _chatManager = FindFirstObjectByType<ChatManager>();
        return _chatManager;
    }

    private Camera ObterCamera()
    {
        MapManager mapManager = ObterMapManager();
        if (mapManager != null && mapManager.cameraVTT != null)
            return mapManager.cameraVTT.GetComponent<Camera>();
        return Camera.main;
    }
}
