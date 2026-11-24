using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Versão offline (sem Steam/rede) do battle map de Salas/Grid/MapManager.cs — usada só na
/// preparação de campanha. Reaproveita a mesma geometria (grid desenhado por LineRenderer, snap de
/// token ao grid, conversão tela→mundo) mas SEM nenhuma dependência de SteamLobbyManager/
/// NetworkSyncManager/SalaDataStorage. Tokens são linkados a um EntryData de NPC de Campanhas (ver
/// Dnd5eNpcFieldCatalog) em vez de ficha/monstro de Salas.
///
/// Paredes (Fase 2) são segmentos vetoriais LIVRES (WallLine — ver CampanhaMapaData), sem nenhuma
/// relação com o grid de combate: EdgeCollider2D reais, posicionados via coordenadas normalizadas
/// da imagem do mapa. Iluminação (Fase 3) ainda não implementada — o ponto de extensão
/// (InstanciarCadeiaDeParede) já deixa comentado onde entra o ShadowCaster2D.
/// </summary>
public class CampanhaMapManager : MonoBehaviour, IGerenciadorDeParedes, IGerenciadorDeTokens
{
    [Header("Referências da Cena")]
    [SerializeField] private Grid gridPrincipal;
    [SerializeField] private SpriteRenderer mapaRenderer;
    [SerializeField] private VTTCamController cameraVTT;

    [Header("Token")]
    [Tooltip("Mesmo prefab de Salas/Grid/MapManager.prefabTokenNoMapa — opcional. Se atribuído, é " +
        "instanciado no lugar do token gerado 100% em código (SpawnTokenDeNpc só troca o sprite do " +
        "filho 'Imagem_Sprite' e garante máscara/colisor/CampanhaTokenDraggable; qualquer estrutura " +
        "extra do prefab é preservada). Deixe vazio pra manter o token totalmente gerado em código.")]
    [SerializeField] private GameObject prefabTokenNoMapa;

    [Header("Navegação")]
    [Tooltip("Usado pelo botão 'Editar' do menu de atalho de um token, pra abrir o NPC de volta no painel de campos.")]
    [SerializeField] private CampanhasManager campanhasManager;

    [Header("Grid Automático")]
    [SerializeField] private Color corLinhasGrid = new Color(1f, 1f, 1f, 0.25f);
    [SerializeField, Range(0.001f, 0.5f)] private float espessuraLinhasGrid = 0.03f;
    [SerializeField] private Vector2 tamanhoMundoGridFixo = new Vector2(240f, 240f);
    [SerializeField] private Color corFundoGrid = new Color32(0x33, 0x33, 0x33, 0xFF);
    [SerializeField] private SpriteRenderer fundoGridRenderer;
    [SerializeField, Min(0.5f)]
    [Tooltip("Espessura mínima das linhas em pixels de tela — recalculada a cada frame conforme o zoom pra evitar que as linhas sumam (fiquem sub-pixel) ao afastar a câmera.")]
    private float larguraMinimaPixelsGrid = 1.5f;

    [Header("Paredes (vetoriais, livres de grid — ver Fase 2)")]
    [Tooltip("Cor do overlay de depuração — só a linha fina desenhada sobre cada parede detectada, " +
        "não afeta o colisor real. Ciano/magenta funcionam bem por contraste contra a maioria dos mapas.")]
    [SerializeField] private Color corOverlayParede = new Color(0f, 1f, 1f, 1f);
    [Tooltip("Roda CampanhaMapaVetorizador de novo com a calibração atual dos sliders abaixo sobre o " +
        "mapa ativo, reconstrói as paredes e garante que o overlay de depuração fique visível — pra " +
        "testar o efeito da calibração sem precisar reimportar a imagem.")]
    [SerializeField] private Button botaoRevetorizarParedes;
    [SerializeField, Range(0.005f, 0.2f)] private float espessuraOverlayParede = 0.05f;
    [Tooltip("Distância normalizada (0..1, relativa ao tamanho da imagem) usada pra reconhecer que a " +
        "ponta de um WallLine é 'a mesma' que a ponta de outro, ao reencadear a lista plana em cadeias " +
        "contíguas pra formar EdgeCollider2D únicos (ver AgruparEmCadeias). Pequena o bastante pra não " +
        "unir paredes que só estão próximas por coincidência.")]
    [SerializeField] private float toleranciaEncadeamentoParedes = 0.002f;
    [Tooltip("Layer dedicada às paredes, pra futuros Raycasts de luz/visão (Fase 3) colidirem só com " +
        "elas. Precisa existir em Edit > Project Settings > Tags and Layers — Unity não permite criar " +
        "layers em runtime; se o nome não for encontrado, as paredes ficam na layer padrão do GameObject.")]
    [SerializeField] private string nomeLayerVisionBlocker = "VisionBlocker";
    [Tooltip("Liga/desliga o GameObject com as ferramentas de edição manual de paredes (WallEditorTools " +
        "— sliders de calibração, botões Desenhar/Apagar etc.), pra não ficarem ocupando espaço na " +
        "tela quando o usuário só quer olhar o mapa.")]
    [SerializeField] private Button botaoFerramentasParede;
    [SerializeField] private GameObject painelFerramentasParede;

    [Header("Ações")]
    [Tooltip("Salva manualmente TUDO da cena ativa (tokens, paredes, metadados do mapa) num clique só. " +
        "A maioria das ações (desenhar/apagar parede, mover token, vetorizar) já salva sozinha — este " +
        "botão é um reforço explícito, útil como garantia manual antes de fechar a campanha.")]
    [SerializeField] private Button botaoSalvarTudo;

    [Header("Calibração da Vetorização (ver CampanhaMapaVetorizadorConfig)")]
    [SerializeField] private Slider sliderLimiarLuminancia;
    [SerializeField] private TMP_Text textoLimiarLuminancia;
    [SerializeField] private Slider sliderRaioDilatacao;
    [SerializeField] private TMP_Text textoRaioDilatacao;
    [SerializeField] private Slider sliderComprimentoMinimoContorno;
    [SerializeField] private TMP_Text textoComprimentoMinimoContorno;
    [SerializeField] private Slider sliderEpsilonRDP;
    [SerializeField] private TMP_Text textoEpsilonRDP;

    // Começa visível: essa é uma ferramenta de PREPARAÇÃO de mapa, o overlay de depuração das
    // paredes deveria ser a regra, não a exceção — sem isso, toda vez que uma cena recém-carregada
    // reconstruía as paredes (ReconstruirParedes), elas ficavam fisicamente lá (colisor real) mas
    // completamente invisíveis até o usuário lembrar de religar manualmente. Ver também
    // WallEditorTools.AlternarVisibilidadeParedes, o botão que liga/desliga isso em runtime.
    private bool _mostrarParedesDetectadas = true;
    private readonly CampanhaMapaVetorizadorConfig _configVetorizacao = new CampanhaMapaVetorizadorConfig();
    private Material _materialLinhaParede;

    /// <summary>Calibração atual da vetorização — lida por CampanhaMapImportUI ao importar/editar um
    /// mapa, pra usar a mesma calibração ajustada aqui em vez de sempre cair nos valores padrão.</summary>
    public CampanhaMapaVetorizadorConfig ConfigVetorizacaoAtual => _configVetorizacao;

    private void Awake()
    {
        Religar(botaoRevetorizarParedes, RevetorizarMapaAtivo);
        Religar(botaoFerramentasParede, AlternarPainelFerramentasParede);
        Religar(botaoSalvarTudo, SalvarTudoDaCenaAtiva);
        Religar(botaoVisaoTodosOsTokens, AlternarVisaoDeTodosOsTokens);
        if (painelFerramentasParede != null) painelFerramentasParede.SetActive(false);

        if (sliderRaioVisao != null)
        {
            sliderRaioVisao.minValue = MetrosPorQuadrado;
            sliderRaioVisao.maxValue = MetrosPorQuadrado * 40f; // 40 quadrados — generoso o bastante pra mapas grandes
            sliderRaioVisao.wholeNumbers = false;
            sliderRaioVisao.onValueChanged.AddListener(OnSliderRaioVisaoAlterado);
        }
        SincronizarSliderRaioVisao(); // estado inicial: nenhum token com visão spotlight ativa

        ConfigurarSlider(sliderLimiarLuminancia, 0f, 1f, _configVetorizacao.limiarLuminancia,
            (v) => { _configVetorizacao.limiarLuminancia = v; AtualizarTexto(textoLimiarLuminancia, "Limiar", v, "0.00"); });
        ConfigurarSlider(sliderRaioDilatacao, 0f, 5f, _configVetorizacao.raioDilatacao,
            (v) => { _configVetorizacao.raioDilatacao = Mathf.RoundToInt(v); AtualizarTexto(textoRaioDilatacao, "Dilatação", _configVetorizacao.raioDilatacao, "0"); },
            valoresInteiros: true);
        ConfigurarSlider(sliderComprimentoMinimoContorno, 0f, 200f, _configVetorizacao.comprimentoMinimoContorno,
            (v) => { _configVetorizacao.comprimentoMinimoContorno = Mathf.RoundToInt(v); AtualizarTexto(textoComprimentoMinimoContorno, "Contorno mín.", _configVetorizacao.comprimentoMinimoContorno, "0"); },
            valoresInteiros: true);
        ConfigurarSlider(sliderEpsilonRDP, 0.25f, 10f, _configVetorizacao.epsilonRDP,
            (v) => { _configVetorizacao.epsilonRDP = v; AtualizarTexto(textoEpsilonRDP, "Épsilon RDP", v, "0.00"); });
    }

    private static void ConfigurarSlider(Slider slider, float min, float max, float valorInicial, Action<float> aoAlterar, bool valoresInteiros = false)
    {
        if (slider == null) { aoAlterar(valorInicial); return; }

        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = valoresInteiros;
        slider.value = valorInicial;
        slider.onValueChanged.AddListener((v) => aoAlterar(v));
        aoAlterar(valorInicial); // garante que o texto/campo iniciem sincronizados com o slider
    }

    private static void AtualizarTexto(TMP_Text texto, string rotulo, float valor, string formato)
    {
        if (texto != null) texto.text = $"{rotulo}: {valor.ToString(formato)}";
    }

    private static void Religar(Button btn, Action acao)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => acao());
    }

    private readonly List<LineRenderer> _linhasGrid = new List<LineRenderer>();
    private Material _materialLinhaGrid;
    private Transform _containerGrid;
    private Transform _containerTokens;
    private Transform _containerParedes;
    private Sprite _spriteFallbackTokenCache;
    private Sprite _spriteFundoBrancoCache;
    private Texture2D _texturaTracejadaGrid;
    private float _espessuraBaseLinhasMundo;
    private float _espessuraLinhasAplicada = -1f;

    private CampanhaMapaData _mapaAtivo;
    private float _escalaBaseMapaAtual = 1f;
    private int _proximaOrdemToken;
    private string _campanhaId;
    private string _sistemaCampanha;

    // Espaçamento entre sortingOrder de tokens consecutivos — deixa de ser +1 e passa a ser
    // IntervaloOrdemToken, abrindo uma "folga" (FolgaMascaraToken pra cada lado) usada pelo range
    // customizado do SpriteMask de cada token, sem risco de um token vizinho cair dentro dela (ver
    // bloco da máscara em SpawnTokenDeNpc).
    private const int IntervaloOrdemToken = 8;
    private const int FolgaMascaraToken = 3;

    /// <summary>Chamado por CampanhasManager.AbrirCampanha — necessário pra achar a pasta de NPCs da
    /// campanha ao restaurar os tokens de uma cena (ver RestaurarTokens).</summary>
    public void DefinirCampanhaId(string campanhaId) => _campanhaId = campanhaId;

    /// <summary>Chamado por CampanhasManager.AbrirCampanha, junto de DefinirCampanhaId — necessário
    /// pra RestaurarTokens também conseguir resolver tokens soltos a partir de um NPC do SISTEMA
    /// (ver CampanhaSistemaNpcLoader), não só os salvos na pasta de NPCs da própria campanha.</summary>
    public void DefinirSistemaCampanha(string sistema) => _sistemaCampanha = sistema;

    // ── Carregar mapa ─────────────────────────────────────────────────────────

    public void CarregarMapa(CampanhaMapaData mapa)
    {
        if (mapa == null || mapaRenderer == null) return;

        // Uma "cena" guarda seus próprios tokens — antes de trocar pra outra, persiste o estado
        // atual (posições podem ter mudado por drag, adição ou remoção desde o último Salvar).
        if (_mapaAtivo != null && _mapaAtivo.mapaId != mapa.mapaId)
            SalvarEstadoTokensNoMapaAtivo();

        _mapaAtivo = mapa;
        LimparTokens();
        LimparParedes();
        EsconderVisaoDoToken();
        DesligarVisaoDeTodosOsTokens();

        Sprite sprite = CarregarSpriteDeArquivo(mapa.imagemPath);
        mapaRenderer.sprite = sprite;

        AplicarEscalaNormalizadaDoMapa();
        AtualizarGridVisual();
        AjustarCamera();

        if (mapa.vetorizado)
            ReconstruirParedes(mapa.paredes);

        AtualizarMeshDeFogRects();
        RestaurarTokens(mapa.tokens);

        // Toda cena carregada vira "a última ativa" — CarregarUltimoMapaAtivo (chamado por
        // CampanhasManager.AbrirCampanha) usa isso pra reabrir a mesma cena automaticamente da
        // próxima vez que a campanha for aberta.
        CampanhaMapasStorage.SalvarMapaAtivo(_campanhaId, mapa.mapaId);
    }

    /// <summary>Chamado por CampanhasManager.AbrirCampanha — recarrega automaticamente a última cena
    /// que estava ativa nessa campanha (ver CampanhaMapasStorage.SalvarMapaAtivo/CarregarMapaAtivoId),
    /// pra não precisar reabrir manualmente o mapa toda vez que a campanha é reaberta. Sem efeito se a
    /// campanha nunca teve uma cena carregada, ou se a cena salva foi excluída desde então.</summary>
    public void CarregarUltimoMapaAtivo()
    {
        if (string.IsNullOrWhiteSpace(_campanhaId)) return;

        string mapaIdSalvo = CampanhaMapasStorage.CarregarMapaAtivoId(_campanhaId);
        if (string.IsNullOrWhiteSpace(mapaIdSalvo)) return;

        foreach (CampanhaMapaData mapa in CampanhaMapasStorage.LoadAll(_campanhaId))
        {
            if (mapa != null && mapa.mapaId == mapaIdSalvo)
            {
                CarregarMapa(mapa);
                return;
            }
        }
    }

    /// <summary>Troca pra outra cena da campanha com popup de progresso (PopupManager) — mesmo padrão
    /// de Salas/Grid/MapManager.TrocarMapaComProgresso. No-op se o mapa clicado já for o ativo (nesse
    /// caso a intenção normalmente é recarregar do zero — ver RecarregarMapaAtual).</summary>
    public void TrocarMapaComProgresso(CampanhaMapaData novoMapa)
    {
        if (novoMapa == null || (_mapaAtivo != null && _mapaAtivo.mapaId == novoMapa.mapaId)) return;
        StartCoroutine(TrocarMapaComProgressoCoroutine(novoMapa));
    }

    private IEnumerator TrocarMapaComProgressoCoroutine(CampanhaMapaData novoMapa)
    {
        PopupManager.Instance?.MostrarCarregamento("Trocando de cena", novoMapa.nome);
        yield return null;

        CarregarMapa(novoMapa);
        yield return null;

        PopupManager.Instance?.Fechar();
    }

    /// <summary>Recarrega a cena atualmente ativa do zero (grid/paredes/tokens) — usado pelo botão de
    /// reload da barra superior (ver ToggleObjects.RecarregarTelaAtual). Mesmo raciocínio de
    /// MapManager.RecarregarMapaAtual: CarregarMapa(mapa) só salva os tokens da cena que está SAINDO
    /// quando mapa.mapaId é DIFERENTE do _mapaAtivo atual — chamando com a MESMA referência essa
    /// guarda nunca dispara, e LimparTokens() destruiria os tokens sem salvar primeiro. Por isso aqui
    /// salva explicitamente ANTES de chamar CarregarMapa.</summary>
    public void RecarregarMapaAtual()
    {
        if (_mapaAtivo == null) return;
        StartCoroutine(RecarregarMapaAtualCoroutine());
    }

    private IEnumerator RecarregarMapaAtualCoroutine()
    {
        CampanhaMapaData mapaParaRecarregar = _mapaAtivo;

        PopupManager.Instance?.MostrarCarregamento("Recarregando cena", mapaParaRecarregar.nome);
        yield return null;

        // Snapshot explícito ANTES de CarregarMapa — ver justificativa no comentário do método acima.
        SalvarEstadoTokensNoMapaAtivo();
        PopupManager.Instance?.AtualizarProgresso(0.5f, "Recarregando...", mapaParaRecarregar.nome);
        yield return null;

        CarregarMapa(mapaParaRecarregar);
        yield return null;

        PopupManager.Instance?.AtualizarProgresso(1f, "Concluído");
        yield return new WaitForSeconds(0.3f);
        PopupManager.Instance?.Fechar();
    }

    // ── Salvar tudo (botão manual) ────────────────────────────────────────────
    //
    // A maioria das mutações (desenhar/apagar parede, mover token, vetorizar) já persiste sozinha
    // logo que acontece — este botão existe como reforço explícito: um clique que salva TUDO da cena
    // ativa de uma vez (tokens, paredes, metadados do mapa) e mostra passo a passo no PopupManager,
    // pra dar uma confirmação visual clara de que nada ficou só em memória.

    /// <summary>Chamado pelo clique de botaoSalvarTudo.</summary>
    public void SalvarTudoDaCenaAtiva()
    {
        if (_mapaAtivo == null || string.IsNullOrWhiteSpace(_campanhaId))
        {
            VTTLog.ForjaError("[CampanhaMapManager] Nenhuma cena ativa para salvar.");
            PopupManager.Instance?.MostrarErro("Salvar", "Nenhuma cena ativa pra salvar no momento.");
            return;
        }

        StartCoroutine(SalvarTudoCoroutine(_mapaAtivo));
    }

    private IEnumerator SalvarTudoCoroutine(CampanhaMapaData mapa)
    {
        int quantidadeTokens = _containerTokens != null ? _containerTokens.childCount : 0;

        PopupManager.Instance?.MostrarCarregamento("Salvando cena", mapa.nome);
        yield return null;

        PopupManager.Instance?.AtualizarProgresso(0.2f, "Salvando tokens...", $"{quantidadeTokens} token(s)");
        yield return null;
        // Serializa a posição/NPC de cada token pra _mapaAtivo.tokens e já persiste o CampanhaMapaData
        // inteiro (mesmo objeto de 'mapa') — paredes/vetorizado/nome vão junto nessa mesma gravação.
        SalvarEstadoTokensNoMapaAtivo();

        PopupManager.Instance?.AtualizarProgresso(0.55f, "Salvando paredes...", $"{mapa.paredes?.Count ?? 0} trecho(s) de parede");
        yield return null;
        // Chamada explícita e redundante com a de cima de propósito: garante a gravação mesmo se
        // SalvarEstadoTokensNoMapaAtivo tiver saído cedo (ex: campanhaId vazio nesse instante).
        CampanhaMapasStorage.SaveOrUpdate(_campanhaId, mapa);

        PopupManager.Instance?.AtualizarProgresso(0.85f, "Marcando cena como ativa...", mapa.nome);
        yield return null;
        CampanhaMapasStorage.SalvarMapaAtivo(_campanhaId, mapa.mapaId);

        PopupManager.Instance?.AtualizarProgresso(1f, "Concluído", $"{quantidadeTokens} token(s), {mapa.paredes?.Count ?? 0} parede(s)");
        yield return new WaitForSeconds(0.4f); // breve pausa pra o usuário ver o "concluído" antes de fechar
        PopupManager.Instance?.Fechar();

        VTTLog.Forja($"[CampanhaMapManager] Cena '{mapa.nome}' salva manualmente: {quantidadeTokens} token(s), " +
            $"{mapa.paredes?.Count ?? 0} trecho(s) de parede.");
    }

    private static Sprite CarregarSpriteDeArquivo(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho)) return null;

        byte[] bytes = File.ReadAllBytes(caminho);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(bytes))
        {
            Destroy(tex);
            return null;
        }
        return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
    }

    private void AplicarEscalaNormalizadaDoMapa()
    {
        if (mapaRenderer == null || mapaRenderer.sprite == null || _mapaAtivo == null) return;

        int colunas = Mathf.Max(1, _mapaAtivo.gridColunas);
        int linhas = Mathf.Max(1, _mapaAtivo.gridLinhas);
        float unidadesPorCelula = Mathf.Max(0.01f, _mapaAtivo.unidadesPorCelula);
        float escalaVisual = Mathf.Max(0.01f, _mapaAtivo.escalaVisual);

        Vector2 tamanhoLocal = mapaRenderer.sprite.bounds.size;
        if (tamanhoLocal.x < 0.001f || tamanhoLocal.y < 0.001f) return;

        float larguraAlvo = colunas * unidadesPorCelula * escalaVisual;
        float alturaAlvo = linhas * unidadesPorCelula * escalaVisual;
        float escalaUniforme = Mathf.Min(larguraAlvo / tamanhoLocal.x, alturaAlvo / tamanhoLocal.y);

        _escalaBaseMapaAtual = Mathf.Max(0.0001f, escalaUniforme);
        mapaRenderer.transform.localScale = new Vector3(_escalaBaseMapaAtual, _escalaBaseMapaAtual, 1f);
    }

    private void AjustarCamera()
    {
        if (cameraVTT == null) return;
        if (mapaRenderer != null && mapaRenderer.sprite != null)
            cameraVTT.FitToSprite(mapaRenderer);
        cameraVTT.DefinirLimitesMovimento(ObterBoundsGridFixo(), ObterTamanhoAtualDaCelula() * 2f);
    }

    // ── Grid visual (LineRenderer) ────────────────────────────────────────────

    private float ObterTamanhoAtualDaCelula()
        => _mapaAtivo != null && _mapaAtivo.unidadesPorCelula > 0f ? _mapaAtivo.unidadesPorCelula : 1.5f;

    private Bounds ObterBoundsGridFixo()
    {
        Vector3 centro = mapaRenderer != null ? mapaRenderer.transform.position : transform.position;
        float largura = Mathf.Max(ObterTamanhoAtualDaCelula() * 4f, tamanhoMundoGridFixo.x);
        float altura = Mathf.Max(ObterTamanhoAtualDaCelula() * 4f, tamanhoMundoGridFixo.y);
        return new Bounds(centro, new Vector3(largura, altura, 0.1f));
    }

    private void AtualizarGridVisual()
    {
        LimparLinhasGrid();
        GarantirFundoGridVisual();

        Bounds bounds = ObterBoundsGridFixo();
        float largura = bounds.size.x;
        float altura = bounds.size.y;
        if (largura < 0.001f || altura < 0.001f) return;

        float unidadesPorCelula = ObterTamanhoAtualDaCelula();
        int colunas = Mathf.Max(1, Mathf.RoundToInt(largura / unidadesPorCelula));
        int linhas = Mathf.Max(1, Mathf.RoundToInt(altura / unidadesPorCelula));
        float larguraUsada = colunas * unidadesPorCelula;
        float alturaUsada = linhas * unidadesPorCelula;

        if (gridPrincipal != null)
            gridPrincipal.cellSize = new Vector3(unidadesPorCelula, unidadesPorCelula, 1f);

        float minX = bounds.center.x - larguraUsada * 0.5f;
        float maxX = bounds.center.x + larguraUsada * 0.5f;
        float minY = bounds.center.y - alturaUsada * 0.5f;
        float maxY = bounds.center.y + alturaUsada * 0.5f;
        float z = mapaRenderer != null ? mapaRenderer.transform.position.z - 0.01f : -0.01f;

        Transform pai = ObterOuCriarContainerGrid();
        float espessuraMundo = Mathf.Clamp(unidadesPorCelula * espessuraLinhasGrid, 0.006f, unidadesPorCelula * 0.12f);
        _espessuraBaseLinhasMundo = espessuraMundo;

        for (int c = 0; c <= colunas; c++)
        {
            float x = Mathf.Lerp(minX, maxX, (float)c / colunas);
            CriarLinha(new Vector3(x, minY, z), new Vector3(x, maxY, z), pai, espessuraMundo, unidadesPorCelula);
        }
        for (int l = 0; l <= linhas; l++)
        {
            float y = Mathf.Lerp(minY, maxY, (float)l / linhas);
            CriarLinha(new Vector3(minX, y, z), new Vector3(maxX, y, z), pai, espessuraMundo, unidadesPorCelula);
        }

        // Aplica a espessura correta pro zoom atual já nesse frame, em vez de esperar o próximo
        // LateUpdate — evita um "flash" com a espessura base ao carregar/recriar o grid.
        AtualizarEspessuraLinhasPorZoom(forcar: true);
    }

    private void LateUpdate()
    {
        AtualizarEspessuraLinhasPorZoom();
        AtualizarDeselecaoPorCliqueFora();
    }

    // "Clicar fora de qualquer token desmarca a seleção atual" — mesmo padrão de
    // MapTokenDraggable.CliqueConsumidoEsteFrame em Salas (lá quem lê o flag é SelecaoAlvoManager,
    // o sistema de alvo de combate; Campanhas não tem um, então o próprio CampanhaMapManager assume
    // esse papel). Roda em LateUpdate — depois de todo Update() da cena já ter rodado neste frame,
    // inclusive o de CampanhaTokenDraggable, que é quem marca CliqueConsumidoEsteFrame — pra saber
    // com certeza se ALGUM token consumiu o clique antes de decidir desmarcar.
    private void AtualizarDeselecaoPorCliqueFora()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

        bool sobreUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (!sobreUI && !CampanhaTokenDraggable.CliqueConsumidoEsteFrame)
            CampanhaTokenDraggable.LimparSelecao();

        CampanhaTokenDraggable.CliqueConsumidoEsteFrame = false;
    }

    // Recalcula a espessura das linhas do grid a cada frame conforme o zoom da câmera, garantindo
    // uma largura mínima em PIXELS de tela (larguraMinimaPixelsGrid). Sem isso, linhas com espessura
    // fixa em unidades de mundo ficam sub-pixel ao afastar o zoom e o LineRenderer simplesmente não
    // as desenha — dando a impressão de "buracos"/linhas sumindo e reaparecendo ao dar zoom in/out.
    private void AtualizarEspessuraLinhasPorZoom(bool forcar = false)
    {
        if (cameraVTT == null || _linhasGrid.Count == 0) return;

        Camera cam = cameraVTT.GetComponent<Camera>();
        if (cam == null || !cam.orthographic || Screen.height <= 0) return;

        float mundoPorPixel = (cam.orthographicSize * 2f) / Screen.height;
        float larguraMinMundo = Mathf.Max(0.0001f, mundoPorPixel * larguraMinimaPixelsGrid);
        float larguraFinal = Mathf.Max(_espessuraBaseLinhasMundo, larguraMinMundo);

        if (!forcar && Mathf.Abs(larguraFinal - _espessuraLinhasAplicada) < 0.0005f) return;

        _espessuraLinhasAplicada = larguraFinal;
        foreach (LineRenderer lr in _linhasGrid)
        {
            if (lr == null) continue;
            lr.startWidth = larguraFinal;
            lr.endWidth = larguraFinal;
        }
    }

    // Cria/posiciona um SpriteRenderer atrás do grid preenchendo toda a área fixa (ObterBoundsGridFixo)
    // com corFundoGrid, e sincroniza a cor de "clear" da câmera VTT com a mesma cor — sem isso, dar
    // zoom out além do sprite do mapa (ou antes de importar um mapa) mostrava o azul padrão da câmera
    // por trás das linhas, reforçando a sensação de "buracos" no grid.
    private void GarantirFundoGridVisual()
    {
        if (fundoGridRenderer == null)
        {
            Transform pai = gridPrincipal != null ? gridPrincipal.transform : transform;
            Transform existente = pai.Find("Grid_Fundo");
            if (existente == null)
            {
                GameObject go = new GameObject("Grid_Fundo");
                go.transform.SetParent(pai, false);
                existente = go.transform;
            }

            fundoGridRenderer = existente.GetComponent<SpriteRenderer>();
            if (fundoGridRenderer == null)
                fundoGridRenderer = existente.gameObject.AddComponent<SpriteRenderer>();
        }

        if (fundoGridRenderer.sprite == null)
            fundoGridRenderer.sprite = ObterSpriteFundoBranco();

        Bounds boundsGrid = ObterBoundsGridFixo();
        fundoGridRenderer.transform.position = new Vector3(
            boundsGrid.center.x,
            boundsGrid.center.y,
            mapaRenderer != null ? mapaRenderer.transform.position.z : 0f);
        fundoGridRenderer.transform.localScale = new Vector3(boundsGrid.size.x, boundsGrid.size.y, 1f);
        fundoGridRenderer.color = corFundoGrid;
        fundoGridRenderer.sortingLayerID = mapaRenderer != null ? mapaRenderer.sortingLayerID : 0;
        fundoGridRenderer.sortingOrder = mapaRenderer != null ? mapaRenderer.sortingOrder - 1 : -1;
        fundoGridRenderer.gameObject.SetActive(true);

        if (cameraVTT != null)
        {
            Camera cam = cameraVTT.GetComponent<Camera>();
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = corFundoGrid;
            }
        }
    }

    private Sprite ObterSpriteFundoBranco()
    {
        if (_spriteFundoBrancoCache != null) return _spriteFundoBrancoCache;

        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        _spriteFundoBrancoCache = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        return _spriteFundoBrancoCache;
    }

    private Transform ObterOuCriarContainerGrid()
    {
        if (_containerGrid != null) return _containerGrid;
        Transform pai = gridPrincipal != null ? gridPrincipal.transform : transform;
        GameObject go = new GameObject("Grid_Linhas");
        go.transform.SetParent(pai, false);
        _containerGrid = go.transform;
        return _containerGrid;
    }

    private void CriarLinha(Vector3 inicio, Vector3 fim, Transform pai, float espessura, float tamanhoCelula)
    {
        GameObject go = new GameObject("GridLine");
        go.transform.SetParent(pai, false);

        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, inicio);
        lr.SetPosition(1, fim);
        // Material clonado por linha: mainTextureScale (usado pra ajustar o tracejado ao
        // comprimento de CADA linha) é uma propriedade do material, não da instância do
        // LineRenderer — compartilhar um único material faria todas as linhas usarem a mesma escala.
        lr.material = new Material(ObterMaterialLinhaGrid());
        lr.startColor = corLinhasGrid;
        lr.endColor = corLinhasGrid;
        lr.startWidth = espessura;
        lr.endWidth = espessura;
        lr.textureMode = LineTextureMode.Tile;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.generateLightingData = false;
        lr.alignment = LineAlignment.View;
        lr.sortingLayerID = mapaRenderer != null ? mapaRenderer.sortingLayerID : 0;
        lr.sortingOrder = mapaRenderer != null ? mapaRenderer.sortingOrder + 1 : 1;
        lr.numCapVertices = 0;

        float comprimentoLinha = Vector3.Distance(inicio, fim);
        float tamanhoTraco = Mathf.Max(espessura * 2.5f, tamanhoCelula * 0.10f);
        lr.material.mainTextureScale = new Vector2(Mathf.Max(1f, comprimentoLinha / tamanhoTraco), 1f);

        _linhasGrid.Add(lr);
    }

    private Material ObterMaterialLinhaGrid()
    {
        if (_materialLinhaGrid != null) return _materialLinhaGrid;
        _materialLinhaGrid = new Material(Shader.Find("Sprites/Default"));
        _materialLinhaGrid.mainTexture = ObterTexturaTracejadaGrid();
        return _materialLinhaGrid;
    }

    // Textura 32x1 com um traço branco a cada 4 pixels (resto transparente) — tileada ao longo de
    // cada LineRenderer (LineTextureMode.Tile) pra desenhar o grid tracejado, igual Salas/Grid/MapManager.cs.
    private Texture2D ObterTexturaTracejadaGrid()
    {
        if (_texturaTracejadaGrid != null) return _texturaTracejadaGrid;

        _texturaTracejadaGrid = new Texture2D(32, 1, TextureFormat.RGBA32, true);
        _texturaTracejadaGrid.wrapMode = TextureWrapMode.Repeat;
        _texturaTracejadaGrid.filterMode = FilterMode.Bilinear;
        _texturaTracejadaGrid.anisoLevel = 8;

        Color[] pixels = new Color[32];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = (i % 4 == 0) ? new Color(1f, 1f, 1f, 0.9f) : Color.clear;

        _texturaTracejadaGrid.SetPixels(pixels);
        _texturaTracejadaGrid.Apply(true, false);
        return _texturaTracejadaGrid;
    }

    private void LimparLinhasGrid()
    {
        foreach (LineRenderer lr in _linhasGrid)
        {
            if (lr == null) continue;
            // Cada linha tem seu próprio Material clonado (mainTextureScale é por-material — ver
            // CriarLinha); destruir só o GameObject não libera esse clone.
            if (lr.material != null) Destroy(lr.material);
            Destroy(lr.gameObject);
        }
        _linhasGrid.Clear();
        _espessuraLinhasAplicada = -1f;
    }

    // ── Tokens ────────────────────────────────────────────────────────────────

    public Vector3 CamParaMundoToken(Camera cam, Vector2 posicaoTela)
    {
        if (cam == null) return Vector3.zero;
        Vector3 mundo = cam.ScreenToWorldPoint(new Vector3(posicaoTela.x, posicaoTela.y, Mathf.Abs(cam.transform.position.z)));
        mundo.z = mapaRenderer != null ? mapaRenderer.transform.position.z - 0.02f : 0f;
        return mundo;
    }

    public bool PosicaoDentroDaAreaDeTokens(Vector3 posicaoMundo)
    {
        Bounds area = ObterBoundsGridFixo();
        return posicaoMundo.x >= area.min.x && posicaoMundo.x <= area.max.x
            && posicaoMundo.y >= area.min.y && posicaoMundo.y <= area.max.y;
    }

    // Extraído de AjustarPosicaoTokenAoGrid pra ser reaproveitado também por MundoParaCelula/
    // CelulaParaBoundsMundo (fog manual — ver FogRect.cs) sem duplicar a mesma matemática de
    // origem/dimensões do grid em três lugares.
    private void ObterGridDimensoesEOrigem(out int colunas, out int linhas, out float minX, out float minY)
    {
        Bounds area = ObterBoundsGridFixo();
        float tamanhoCelula = ObterTamanhoAtualDaCelula();

        colunas = Mathf.Max(1, Mathf.RoundToInt(area.size.x / tamanhoCelula));
        linhas = Mathf.Max(1, Mathf.RoundToInt(area.size.y / tamanhoCelula));

        minX = area.center.x - colunas * tamanhoCelula * 0.5f;
        minY = area.center.y - linhas * tamanhoCelula * 0.5f;
    }

    public Vector3 AjustarPosicaoTokenAoGrid(Vector3 posicaoMundo)
    {
        ObterGridDimensoesEOrigem(out int colunas, out int linhas, out float minX, out float minY);
        float tamanhoCelula = ObterTamanhoAtualDaCelula();

        int col = Mathf.Clamp(Mathf.FloorToInt((posicaoMundo.x - minX) / tamanhoCelula), 0, colunas - 1);
        int lin = Mathf.Clamp(Mathf.FloorToInt((posicaoMundo.y - minY) / tamanhoCelula), 0, linhas - 1);

        float x = minX + (col + 0.5f) * tamanhoCelula;
        float y = minY + (lin + 0.5f) * tamanhoCelula;
        float z = mapaRenderer != null ? mapaRenderer.transform.position.z - 0.02f : posicaoMundo.z;
        return new Vector3(x, y, z);
    }

    /// <summary>Converte uma posição de mundo na célula do grid (col, lin) que a contém — ver
    /// IGerenciadorDeParedes.MundoParaCelula.</summary>
    public (int col, int lin) MundoParaCelula(Vector3 posicaoMundo)
    {
        ObterGridDimensoesEOrigem(out int colunas, out int linhas, out float minX, out float minY);
        float tamanhoCelula = ObterTamanhoAtualDaCelula();

        int col = Mathf.Clamp(Mathf.FloorToInt((posicaoMundo.x - minX) / tamanhoCelula), 0, colunas - 1);
        int lin = Mathf.Clamp(Mathf.FloorToInt((posicaoMundo.y - minY) / tamanhoCelula), 0, linhas - 1);
        return (col, lin);
    }

    /// <summary>Bounds de mundo (centro/tamanho) da célula (col, lin) — inverso de MundoParaCelula.
    /// Ver IGerenciadorDeParedes.CelulaParaBoundsMundo.</summary>
    public Bounds CelulaParaBoundsMundo(int col, int lin)
    {
        ObterGridDimensoesEOrigem(out _, out _, out float minX, out float minY);
        float tamanhoCelula = ObterTamanhoAtualDaCelula();

        float x = minX + (col + 0.5f) * tamanhoCelula;
        float y = minY + (lin + 0.5f) * tamanhoCelula;
        float z = mapaRenderer != null ? mapaRenderer.transform.position.z : 0f;
        return new Bounds(new Vector3(x, y, z), new Vector3(tamanhoCelula, tamanhoCelula, 0.1f));
    }

    private Camera ObterCamera()
        => cameraVTT != null ? cameraVTT.GetComponent<Camera>() : Camera.main;

    /// <summary>Chamado por CampanhaNpcCardUI.OnEndDrag ao soltar um card de NPC sobre o grid.</summary>
    public void TentarSoltarTokenNoMapa(EntryData npc, Vector2 posicaoTela)
    {
        if (npc == null || mapaRenderer == null || mapaRenderer.sprite == null) return;

        Camera cam = ObterCamera();
        if (cam == null) return;

        Vector3 posicaoMundo = CamParaMundoToken(cam, posicaoTela);
        if (!PosicaoDentroDaAreaDeTokens(posicaoMundo)) return;

        posicaoMundo = AjustarPosicaoTokenAoGrid(posicaoMundo);
        SpawnTokenDeNpc(npc, posicaoMundo);
        SalvarEstadoTokensNoMapaAtivo();

        // Um token novo entra na combinação da visão geral, se ela estiver ligada no momento.
        if (_visaoTodosOsTokensAtiva) AtualizarVisaoDeTodosOsTokens();
    }

    private void SpawnTokenDeNpc(EntryData npc, Vector3 posicaoMundo, float raioVisaoMetros = RaioVisaoPadraoMetros,
        string tokenId = "", bool bloqueadoSalvo = false, bool escondidoSalvo = false)
    {
        string nome = NpcEntryFormatter.ObterNome(npc);
        if (string.IsNullOrWhiteSpace(nome)) nome = "NPC";

        Sprite sprite = CarregarSpriteDeArquivo(NpcEntryFormatter.ObterRetratoPath(npc)) ?? ObterSpriteFallbackToken();
        float tamanhoCelula = ObterTamanhoAtualDaCelula();
        Transform parentTokens = ObterOuCriarContainerTokens();

        GameObject tokenGo;
        SpriteRenderer sr;

        // Mesmo padrão de Salas/Grid/MapManager.SpawnTokenNoMapa: se houver prefab, instancia ele
        // (preservando qualquer estrutura/configuração extra que tiver) e só garante que o filho
        // "Imagem_Sprite" exista pra receber o retrato do NPC; sem prefab, monta tudo em código.
        if (prefabTokenNoMapa != null)
        {
            tokenGo = Instantiate(prefabTokenNoMapa, parentTokens, true);
            tokenGo.name = $"Token_{nome}";
            tokenGo.transform.position = posicaoMundo;

            sr = tokenGo.GetComponentInChildren<SpriteRenderer>(true);
            if (sr == null)
            {
                GameObject childSpriteExistente = new GameObject("Imagem_Sprite");
                childSpriteExistente.transform.SetParent(tokenGo.transform, false);
                sr = childSpriteExistente.AddComponent<SpriteRenderer>();
            }
            sr.sprite = sprite;
        }
        else
        {
            tokenGo = new GameObject($"Token_{nome}");
            tokenGo.transform.SetParent(parentTokens, true);
            tokenGo.transform.position = posicaoMundo;

            GameObject childSprite = new GameObject("Imagem_Sprite");
            childSprite.transform.SetParent(tokenGo.transform, false);
            sr = childSprite.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
        }

        tokenGo.transform.localScale = Vector3.one;
        sr.sortingLayerID = mapaRenderer.sortingLayerID;
        sr.sortingOrder = mapaRenderer.sortingOrder + 4 + (_proximaOrdemToken++) * IntervaloOrdemToken;

        Vector2 tamanhoLocal = sprite.bounds.size;
        float maiorDimensao = Mathf.Max(0.0001f, Mathf.Max(tamanhoLocal.x, tamanhoLocal.y));
        float escalaImagem = tamanhoCelula / maiorDimensao;
        sr.transform.localScale = new Vector3(escalaImagem, escalaImagem, 1f);

        // Token redondo mesmo com arte quadrada — mesmo SpriteMask circular gerado em código de Salas.
        // Reaproveita a máscara do prefab se ela já existir (Find), em vez de duplicar.
        Transform mascaraExistente = tokenGo.transform.Find("Mascara_Redonda");
        GameObject mascaraGo = mascaraExistente != null ? mascaraExistente.gameObject : new GameObject("Mascara_Redonda");
        if (mascaraExistente == null) mascaraGo.transform.SetParent(tokenGo.transform, false);
        mascaraGo.transform.localScale = new Vector3(tamanhoCelula, tamanhoCelula, 1f);
        SpriteMask mascara = mascaraGo.GetComponent<SpriteMask>();
        if (mascara == null) mascara = mascaraGo.AddComponent<SpriteMask>();
        mascara.sprite = TokenMascaraCircular.Obter();
        sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

        // Restringe a máscara a uma FAIXA em torno do sortingOrder deste token (±FolgaMascaraToken) —
        // sem isso, o SpriteMask afeta a Sorting Layer INTEIRA (todo token está nela), causando
        // cross-masking entre tokens que piora dependendo do que a câmera está renderizando (zoom
        // muda o que está em view/culled). Usa uma faixa LARGA (não front==back exatamente igual a
        // sortingOrder) de propósito: a documentação da Unity não deixa claro se front==back produz
        // uma faixa de um único valor válida ou uma faixa vazia (nesse segundo caso, o token nunca
        // ficaria dentro da própria máscara e sumiria por completo — foi exatamente isso que
        // aconteceu na primeira tentativa). Com IntervaloOrdemToken=8 entre tokens consecutivos e
        // ±3 de folga aqui, a faixa de um token nunca alcança a de outro (folga de 2 unidades sobra
        // de cada lado), então a ambiguidade não importa: seja qual for a semântica exata, o único
        // sortingOrder que pode cair nessa faixa é o do próprio token.
        mascara.isCustomRangeActive = true;
        mascara.frontSortingLayerID = sr.sortingLayerID;
        mascara.frontSortingOrder   = sr.sortingOrder + FolgaMascaraToken;
        mascara.backSortingLayerID  = sr.sortingLayerID;
        mascara.backSortingOrder    = sr.sortingOrder - FolgaMascaraToken;

        BoxCollider2D col = tokenGo.GetComponent<BoxCollider2D>();
        if (col == null) col = tokenGo.AddComponent<BoxCollider2D>();
        col.size = new Vector2(tamanhoCelula, tamanhoCelula);

        // CampanhaTokenDraggable precisa existir e já ter rodado Setup ANTES de
        // TokenButtonUI.InicializarComoTokenDoMapa() — esse método resolve _meuToken via
        // GetComponentInParent<CampanhaTokenDraggable>(), que retorna null se o componente ainda não
        // tiver sido adicionado ao GameObject. Com a ordem invertida, _meuToken ficava null pra
        // sempre e a seleção nunca aparecia (nada acontecia ao clicar no token) — mesma causa raiz já
        // resolvida do lado de Salas em MapManager.SpawnTokenNoMapa, onde MapTokenDraggable já é
        // adicionado antes do bloco do TokenButtonUI.
        CampanhaTokenDraggable arrastavel = tokenGo.GetComponent<CampanhaTokenDraggable>();
        if (arrastavel == null) arrastavel = tokenGo.AddComponent<CampanhaTokenDraggable>();
        arrastavel.Setup(this, npc, nome, raioVisaoMetros, tokenId, bloqueadoSalvo, escondidoSalvo);

        // O prefab de Salas (quando reaproveitado via prefabTokenNoMapa) costuma trazer um
        // TokenButtonUI (Canvas com o nome do token) — sem chamar Setup nele, o texto fica preso no
        // que veio salvo no prefab em vez do nome real do NPC. O overload em IGerenciadorDeTokens
        // (não o de MapManager, Salas-only) dá a TokenButtonUI acesso a Remover/Duplicar/notificar
        // raio de visão aqui também — antes passava null e todo o content de ferramentas (Lock/
        // Esconder/Duplicar/Deletar/vida/seleção) ficava inerte, ver ITokenNoMapa/IGerenciadorDeTokens.
        TokenButtonUI tokenUI = tokenGo.GetComponent<TokenButtonUI>();
        if (tokenUI == null) tokenUI = tokenGo.GetComponentInChildren<TokenButtonUI>(true);
        if (tokenUI != null)
        {
            tokenUI.Setup(this, nome, sprite);
            tokenUI.InicializarComoTokenDoMapa();
        }
    }

    private Sprite ObterSpriteFallbackToken()
    {
        if (_spriteFallbackTokenCache != null) return _spriteFallbackTokenCache;

        const int tamanho = 64;
        var tex = new Texture2D(tamanho, tamanho, TextureFormat.RGBA32, false);
        var pixels = new Color32[tamanho * tamanho];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(120, 120, 120, 255);
        tex.SetPixels32(pixels);
        tex.Apply();

        _spriteFallbackTokenCache = Sprite.Create(tex, new Rect(0f, 0f, tamanho, tamanho), new Vector2(0.5f, 0.5f), 100f);
        return _spriteFallbackTokenCache;
    }

    private Transform ObterOuCriarContainerTokens()
    {
        if (_containerTokens != null) return _containerTokens;
        Transform pai = gridPrincipal != null ? gridPrincipal.transform : transform;
        GameObject go = new GameObject("Tokens_No_Mapa");
        go.transform.SetParent(pai, false);
        _containerTokens = go.transform;
        return _containerTokens;
    }

    public void RemoverTokenDoMapa(GameObject tokenGo)
    {
        if (tokenGo == null) return;

        bool eraTokenComVisaoAtiva = _tokenComVisaoAtiva != null && _tokenComVisaoAtiva.gameObject == tokenGo;
        if (eraTokenComVisaoAtiva) EsconderVisaoDoToken();

        // Desanexa antes de Destroy (que só executa de fato no fim do frame) pra que
        // SalvarEstadoTokensNoMapaAtivo, chamado logo abaixo, já não conte mais esse token.
        tokenGo.transform.SetParent(null);
        Destroy(tokenGo);
        SalvarEstadoTokensNoMapaAtivo();

        // O token removido saiu da combinação da visão geral — recalcula (se já não tiver sido
        // desligada acima por ser o próprio token com o spotlight ativo).
        if (!eraTokenComVisaoAtiva && _visaoTodosOsTokensAtiva)
            AtualizarVisaoDeTodosOsTokens();
    }

    private CampanhaTokenDraggable EncontrarTokenPorId(string tokenId)
    {
        if (_containerTokens == null || string.IsNullOrWhiteSpace(tokenId)) return null;
        foreach (Transform filho in _containerTokens)
        {
            CampanhaTokenDraggable draggable = filho.GetComponent<CampanhaTokenDraggable>();
            if (draggable != null && draggable.TokenId == tokenId) return draggable;
        }
        return null;
    }

    /// <summary>Implementa IGerenciadorDeTokens — mirror de MapManager.DuplicarToken. A cópia nasce
    /// um quadrado ao lado do original, com o mesmo NPC vinculado e raio de visão, mas um TokenId
    /// novo (bloqueado/escondido começam desligados no clone, mesmo comportamento de Salas).</summary>
    public void DuplicarToken(string tokenId)
    {
        CampanhaTokenDraggable original = EncontrarTokenPorId(tokenId);
        if (original == null)
        {
            VTTLog.ForjaError($"[CampanhaMapManager] Token '{tokenId}' não encontrado para duplicar.");
            return;
        }

        float tamanhoCelula = ObterTamanhoAtualDaCelula();
        Vector3 posNova = AjustarPosicaoTokenAoGrid(original.transform.position + new Vector3(tamanhoCelula, 0f, 0f));

        SpawnTokenDeNpc(original.Npc, posNova, original.RaioVisaoMetros);
        SalvarEstadoTokensNoMapaAtivo();

        if (_visaoTodosOsTokensAtiva) AtualizarVisaoDeTodosOsTokens();
    }

    /// <summary>Implementa IGerenciadorDeTokens — acha o GameObject pelo TokenId e delega pro
    /// RemoverTokenDoMapa(GameObject) já existente (usado pelo menu de atalho), sem duplicar lógica.</summary>
    public void RemoverTokenDoMapa(string tokenId)
    {
        CampanhaTokenDraggable alvo = EncontrarTokenPorId(tokenId);
        if (alvo == null)
        {
            VTTLog.ForjaError($"[CampanhaMapManager] Token '{tokenId}' não encontrado para remover.");
            return;
        }
        RemoverTokenDoMapa(alvo.gameObject);
    }

    /// <summary>Implementa IGerenciadorDeTokens — chamado por TokenButtonUI depois de
    /// ITokenNoMapa.DefinirRaioVisao(...) no slider de visão do próprio token.</summary>
    public void NotificarRaioVisaoAlterado(ITokenNoMapa token)
    {
        if (token is CampanhaTokenDraggable draggable)
            AtualizarVisaoSeAtivo(draggable);
        SalvarEstadoTokensNoMapaAtivo();
    }

    private void LimparTokens()
    {
        if (_containerTokens == null) return;
        for (int i = _containerTokens.childCount - 1; i >= 0; i--)
            Destroy(_containerTokens.GetChild(i).gameObject);
    }

    // Serializa os tokens atualmente no grid (posição + qual NPC) pra dentro de _mapaAtivo.tokens e
    // persiste. Chamado ao trocar de mapa, depois de soltar/remover um token, e pelo próprio
    // CampanhaTokenDraggable ao terminar um drag (ver CampanhaTokenDraggable.Update).
    public void SalvarEstadoTokensNoMapaAtivo()
    {
        if (_mapaAtivo == null || string.IsNullOrWhiteSpace(_campanhaId)) return;

        var tokens = new List<CampanhaTokenData>();
        if (_containerTokens != null)
        {
            foreach (Transform filho in _containerTokens)
            {
                CampanhaTokenDraggable draggable = filho.GetComponent<CampanhaTokenDraggable>();
                if (draggable == null || draggable.Npc == null) continue;

                tokens.Add(new CampanhaTokenData
                {
                    npcId = DynamicXmlBuilder.ResolverEntryId(draggable.Npc, 0),
                    posX = filho.position.x,
                    posY = filho.position.y,
                    raioVisaoMetros = draggable.RaioVisaoMetros,
                    tokenId = draggable.TokenId,
                    bloqueado = draggable.Bloqueado,
                    escondido = draggable.Escondido,
                });
            }
        }

        _mapaAtivo.tokens = tokens;
        CampanhaMapasStorage.SaveOrUpdate(_campanhaId, _mapaAtivo);
    }

    // Recria os tokens salvos de uma cena ao carregá-la — resolve cada npcId contra os NPCs
    // atualmente salvos na campanha E os NPCs do sistema ativo (ver CampanhaSistemaNpcLoader), pra
    // um token solto a partir de um monstro de sistema (CampanhaNpcGaleriaUI) também sobreviver a um
    // reload. NPC da campanha tem prioridade em caso de mesmo id (mesmo nome) — se o mestre criou um
    // NPC próprio com o mesmo nome de um monstro do sistema, a versão dele é a que conta. Um NPC
    // excluído/renomeado desde então (e sem equivalente de sistema) é simplesmente ignorado.
    private void RestaurarTokens(List<CampanhaTokenData> tokens)
    {
        if (tokens == null || tokens.Count == 0 || string.IsNullOrWhiteSpace(_campanhaId)) return;

        string pastaNpcs = CampanhasLocalStorage.NpcsFolderPath(_campanhaId);
        List<EntryData> npcs = DynamicXmlBuilder.LoadEntriesFromFolder(pastaNpcs, "npcs", Dnd5eNpcFieldCatalog.AllFields);

        var npcsPorId = new Dictionary<string, EntryData>();
        foreach (EntryData npc in npcs)
        {
            string id = DynamicXmlBuilder.ResolverEntryId(npc, 0);
            if (!npcsPorId.ContainsKey(id)) npcsPorId[id] = npc;
        }

        if (!string.IsNullOrWhiteSpace(_sistemaCampanha))
        {
            foreach (EntryData npc in CampanhaSistemaNpcLoader.CarregarNpcsDoSistema(_sistemaCampanha))
            {
                string id = DynamicXmlBuilder.ResolverEntryId(npc, 0);
                if (!npcsPorId.ContainsKey(id)) npcsPorId[id] = npc;
            }
        }

        float z = mapaRenderer != null ? mapaRenderer.transform.position.z - 0.02f : 0f;
        foreach (CampanhaTokenData t in tokens)
        {
            if (t == null || string.IsNullOrWhiteSpace(t.npcId)) continue;
            if (!npcsPorId.TryGetValue(t.npcId, out EntryData npc)) continue;
            // raioVisaoMetros vem 0 em JSONs salvos antes desse campo existir — cai no padrão.
            float raio = t.raioVisaoMetros > 0f ? t.raioVisaoMetros : RaioVisaoPadraoMetros;
            SpawnTokenDeNpc(npc, new Vector3(t.posX, t.posY, z), raio, t.tokenId, t.bloqueado, t.escondido);
        }
    }

    /// <summary>Chamado pelo menu de atalho de um CampanhaTokenDraggable ("Editar").</summary>
    public void PedirEdicaoDoNpc(EntryData npc)
    {
        if (npc == null) return;
        campanhasManager?.AbrirNpcParaEdicao(npc);
    }

    /// <summary>Chamado pelo menu de atalho de um CampanhaTokenDraggable ("Lore").</summary>
    public void PedirLoreDoNpc(EntryData npc)
    {
        if (npc == null) return;
        campanhasManager?.AbrirLoreDoNpc(npc);
    }

    // ── Paredes (modelo VETORIAL livre — ver WallLine) ──────────────────────────
    //
    // Sem NENHUMA dependência do grid de combate: cada WallLine guarda pontoA/pontoB em coordenadas
    // NORMALIZADAS (0..1) relativas ao tamanho da imagem do mapa, não em células nem em unidades de
    // mundo — ver NormalizadoParaMundo. Isso também é o que vai permitir, no futuro, o usuário
    // desenhar/editar essas linhas manualmente na UI sem qualquer noção de "célula".
    //
    // Como CampanhaMapaData.paredes é uma lista PLANA e desordenada de segmentos (cada um podendo vir
    // de uma vetorização automática OU, futuramente, de edição manual), AgruparEmCadeias reconecta os
    // segmentos que compartilham ponta (dentro de toleranciaEncadeamentoParedes) em polilinhas
    // contíguas — cada cadeia vira UM único EdgeCollider2D com N pontos, em vez de N colliders
    // separados de 2 pontos (bem mais barato pra física engine).
    //
    // Um EdgeCollider2D real e fino, posicionado exatamente na linha detectada, é o que um futuro
    // sistema de linha de visão (Physics2D.Raycast, Fase 3 — ainda não implementada) precisaria: o
    // raio para exatamente na parede. As paredes vão pra uma Layer dedicada (nomeLayerVisionBlocker)
    // pra esses raycasts poderem filtrar só por elas — a Layer precisa existir no projeto (Unity não
    // permite criar Layers em runtime); ver ObterLayerVisionBlocker.

    private void ReconstruirParedes(List<WallLine> paredes)
    {
        LimparParedes();
        if (paredes == null || paredes.Count == 0 || mapaRenderer == null || mapaRenderer.sprite == null) return;

        int layer = ObterLayerVisionBlocker();
        foreach (CadeiaParede cadeia in AgruparEmCadeias(paredes, toleranciaEncadeamentoParedes))
            InstanciarCadeiaDeParede(cadeia, layer);
    }

    // Uma polilinha contígua (pontos já em ordem) + os WallLine originais que foram fundidos nela —
    // necessário guardar a origem porque WallEditorTools (borracha) precisa saber exatamente quais
    // WallLine remover de CampanhaMapaData.paredes quando o usuário apaga essa cadeia inteira.
    private class CadeiaParede
    {
        public readonly List<Vector2> Pontos = new List<Vector2>();
        public readonly List<WallLine> Origens = new List<WallLine>();
    }

    // Reencadeia uma lista plana e desordenada de segmentos em polilinhas contíguas, casando pontas
    // que ficam a até 'tolerancia' de distância uma da outra. O(n²) no pior caso — irrelevante pro
    // número de segmentos que um mapa de batalha real produz (dezenas a poucas centenas).
    private static List<CadeiaParede> AgruparEmCadeias(List<WallLine> linhas, float tolerancia)
    {
        var restantes = new List<WallLine>();
        foreach (WallLine l in linhas)
            if (l != null && Vector2.Distance(l.pontoA, l.pontoB) > 0.0001f)
                restantes.Add(l);

        var cadeias = new List<CadeiaParede>();

        while (restantes.Count > 0)
        {
            WallLine primeira = restantes[0];
            restantes.RemoveAt(0);

            var cadeia = new CadeiaParede();
            cadeia.Pontos.Add(primeira.pontoA);
            cadeia.Pontos.Add(primeira.pontoB);
            cadeia.Origens.Add(primeira);

            EstenderCadeia(cadeia, restantes, tolerancia, noFim: true);
            EstenderCadeia(cadeia, restantes, tolerancia, noFim: false);

            cadeias.Add(cadeia);
        }

        return cadeias;
    }

    private static void EstenderCadeia(CadeiaParede cadeia, List<WallLine> restantes, float tolerancia, bool noFim)
    {
        bool avancou = true;
        while (avancou)
        {
            avancou = false;
            Vector2 ponta = noFim ? cadeia.Pontos[cadeia.Pontos.Count - 1] : cadeia.Pontos[0];

            for (int i = 0; i < restantes.Count; i++)
            {
                WallLine l = restantes[i];
                Vector2 novoPonto;
                if (Vector2.Distance(l.pontoA, ponta) <= tolerancia) novoPonto = l.pontoB;
                else if (Vector2.Distance(l.pontoB, ponta) <= tolerancia) novoPonto = l.pontoA;
                else continue;

                if (noFim) cadeia.Pontos.Add(novoPonto);
                else cadeia.Pontos.Insert(0, novoPonto);
                cadeia.Origens.Add(l);

                restantes.RemoveAt(i);
                avancou = true;
                break;
            }
        }
    }

    private void InstanciarCadeiaDeParede(CadeiaParede cadeia, int layer)
    {
        if (cadeia == null || cadeia.Pontos.Count < 2) return;

        var mundo = new Vector3[cadeia.Pontos.Count];
        for (int i = 0; i < cadeia.Pontos.Count; i++)
            mundo[i] = NormalizadoParaMundo(cadeia.Pontos[i]);

        Vector3 centro = Vector3.zero;
        foreach (Vector3 p in mundo) centro += p;
        centro /= mundo.Length;
        centro.z = mapaRenderer.transform.position.z - 0.015f;

        var pontosLocais = new Vector2[mundo.Length];
        for (int i = 0; i < mundo.Length; i++)
            pontosLocais[i] = (Vector2)mundo[i] - (Vector2)centro;

        Transform pai = ObterOuCriarContainerParedes();
        GameObject go = new GameObject("Parede");
        go.transform.SetParent(pai, false);
        go.transform.position = centro;
        if (layer >= 0) go.layer = layer;

        EdgeCollider2D col = go.AddComponent<EdgeCollider2D>();
        col.points = pontosLocais;

        CampanhaParedeInstancia instancia = go.AddComponent<CampanhaParedeInstancia>();
        instancia.LinhasOrigem = cadeia.Origens;

        LineRenderer overlay = go.AddComponent<LineRenderer>();
        overlay.useWorldSpace = false;
        overlay.positionCount = pontosLocais.Length;
        for (int i = 0; i < pontosLocais.Length; i++)
            overlay.SetPosition(i, pontosLocais[i]);
        overlay.material = ObterMaterialLinhaParede();
        overlay.startColor = corOverlayParede;
        overlay.endColor = corOverlayParede;
        overlay.startWidth = espessuraOverlayParede;
        overlay.endWidth = espessuraOverlayParede;
        overlay.numCapVertices = 2; // pontas arredondadas — junções entre segmentos da cadeia ficam sem "dente"
        overlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        overlay.receiveShadows = false;
        overlay.generateLightingData = false;
        overlay.alignment = LineAlignment.View;
        overlay.sortingLayerID = mapaRenderer.sortingLayerID;
        overlay.sortingOrder = mapaRenderer.sortingOrder + 3; // abaixo dos tokens (+4), acima do mapa/grid
        overlay.enabled = _mostrarParedesDetectadas;

        // Fase 3 (iluminação): aqui entra um ShadowCaster2D usando os mesmos pontos do EdgeCollider2D.
    }

    // Converte um ponto normalizado (0..1, relativo ao tamanho da imagem) pra posição de mundo atual
    // do mapa — via Transform.TransformPoint, então acompanha automaticamente qualquer posição/escala
    // que o mapaRenderer tiver no momento (nenhuma dependência de unidadesPorCelula/grid).
    private Vector3 NormalizadoParaMundo(Vector2 normalizado)
    {
        Bounds b = mapaRenderer.sprite.bounds; // espaço local do sprite, sem a escala do transform
        Vector3 local = new Vector3(
            b.min.x + normalizado.x * b.size.x,
            b.min.y + normalizado.y * b.size.y,
            0f);
        return mapaRenderer.transform.TransformPoint(local);
    }

    /// <summary>Inverso de NormalizadoParaMundo — usado por WallEditorTools pra converter um clique
    /// (em posição de mundo) na coordenada normalizada (0..1) que WallLine guarda.</summary>
    public Vector2 MundoParaNormalizado(Vector3 mundo)
    {
        if (mapaRenderer == null || mapaRenderer.sprite == null) return Vector2.zero;

        Vector3 local = mapaRenderer.transform.InverseTransformPoint(mundo);
        Bounds b = mapaRenderer.sprite.bounds;
        return new Vector2(
            (local.x - b.min.x) / Mathf.Max(0.0001f, b.size.x),
            (local.y - b.min.y) / Mathf.Max(0.0001f, b.size.y));
    }

    /// <summary>Câmera do grid ativo — reaproveitada por WallEditorTools pra não precisar configurar
    /// a mesma referência de câmera duas vezes no Inspector.</summary>
    public Camera CameraAtiva => ObterCamera();

    // ── Edição manual (WallEditorTools) ──────────────────────────────────────
    //
    // Ponto de entrada usado pela ferramenta de desenho/borracha — mantém CampanhaMapManager como o
    // único dono de _mapaAtivo/persistência/reconstrução; WallEditorTools só lida com input de mouse
    // e chama esses métodos, nunca mexe em CampanhaMapaData diretamente.

    /// <summary>Adiciona uma parede desenhada manualmente ao mapa ativo, reconstrói e persiste
    /// imediatamente (diferente da borracha, um traço de desenho é uma ação só, não um arrasto contínuo).</summary>
    public void AdicionarParedeManual(WallLine linha)
    {
        if (_mapaAtivo == null || linha == null) return;

        _mapaAtivo.paredes ??= new List<WallLine>();
        _mapaAtivo.paredes.Add(linha);
        _mapaAtivo.vetorizado = true;

        ReconstruirParedes(_mapaAtivo.paredes);
        PersistirMapaAtivo();
    }

    /// <summary>Apaga só a PORÇÃO de cada WallLine candidata que cai dentro do círculo da borracha
    /// (centroMundo, raioMundo) — ao contrário de remover a linha inteira, um segmento tocado só no
    /// meio vira 1 ou 2 sub-segmentos remanescentes (o resto da parede continua existindo). Não
    /// persiste no disco (ver PersistirMapaAtivo) — WallEditorTools chama isso a cada frame que a
    /// borracha estiver ativa e só grava ao soltar o botão. Retorna true se algo mudou.</summary>
    public bool ApagarParedesNoRaio(IEnumerable<WallLine> candidatas, Vector3 centroMundo, float raioMundo)
    {
        if (_mapaAtivo == null || _mapaAtivo.paredes == null || candidatas == null) return false;

        bool alterou = false;
        var pedacos = new List<(Vector3 a, Vector3 b)>();
        var novasLinhas = new List<WallLine>();

        foreach (WallLine linha in candidatas)
        {
            // Pode já ter sido consumida por outra cadeia tocada nesse mesmo frame (o mesmo WallLine
            // não aparece em duas cadeias diferentes, mas por segurança contra duplicata no buffer).
            if (!_mapaAtivo.paredes.Contains(linha)) continue;

            Vector3 mundoA = NormalizadoParaMundo(linha.pontoA);
            Vector3 mundoB = NormalizadoParaMundo(linha.pontoB);

            pedacos.Clear();
            if (!RecortarSegmentoForaDoCirculo(mundoA, mundoB, centroMundo, raioMundo, pedacos))
                continue; // esse segmento específico não cruza o círculo — mantém intacto

            _mapaAtivo.paredes.Remove(linha);
            alterou = true;

            foreach ((Vector3 a, Vector3 b) in pedacos)
                novasLinhas.Add(new WallLine { pontoA = MundoParaNormalizado(a), pontoB = MundoParaNormalizado(b) });
        }

        if (!alterou) return false;

        _mapaAtivo.paredes.AddRange(novasLinhas);
        ReconstruirParedes(_mapaAtivo.paredes);
        return true;
    }

    // Recorta o segmento mundo a→b contra um círculo (centro, raio) e devolve, em 'pedacosRestantes',
    // as sub-partes que ficam FORA do círculo (0, 1 ou 2 pedaços — 2 quando o círculo morde bem no
    // meio do segmento, deixando um toco de cada lado). Resolve a interseção reta×círculo como uma
    // equação quadrática no parâmetro t do segmento (P(t) = a + t*(b-a), t em [0,1]); os dois pontos
    // onde a RETA cruza a circunferência viram os limites do trecho a apagar. Retorna false se o
    // círculo não encosta nesse segmento específico (nada a recortar).
    private static bool RecortarSegmentoForaDoCirculo(Vector3 a, Vector3 b, Vector3 centro, float raio, List<(Vector3, Vector3)> pedacosRestantes)
    {
        Vector2 p0 = a;
        Vector2 p1 = b;
        Vector2 c = centro;

        Vector2 d = p1 - p0;
        Vector2 f = p0 - c;

        float coefA = Vector2.Dot(d, d);
        if (coefA < 1e-10f) return false; // segmento degenerado (comprimento ~0)

        float coefB = 2f * Vector2.Dot(f, d);
        float coefC = Vector2.Dot(f, f) - raio * raio;
        float discriminante = coefB * coefB - 4f * coefA * coefC;

        if (discriminante < 0f) return false; // reta nunca cruza o círculo — segmento inteiro fora

        float raizDisc = Mathf.Sqrt(discriminante);
        float t1 = (-coefB - raizDisc) / (2f * coefA);
        float t2 = (-coefB + raizDisc) / (2f * coefA);

        float tDentroInicio = Mathf.Max(0f, t1);
        float tDentroFim = Mathf.Min(1f, t2);

        if (tDentroInicio >= tDentroFim) return false; // interseção cai fora de [0,1] — não toca o segmento

        const float margem = 0.0005f; // evita sobras de comprimento ~0 por erro de ponto flutuante
        if (tDentroInicio > margem)
            pedacosRestantes.Add((a, Vector3.Lerp(a, b, tDentroInicio)));
        if (tDentroFim < 1f - margem)
            pedacosRestantes.Add((Vector3.Lerp(a, b, tDentroFim), b));

        return true;
    }

    /// <summary>Persiste o mapa ativo no disco — chamado direto por AdicionarParedeManual, e por
    /// WallEditorTools ao concluir um arrasto de borracha (ApagarParedesNoRaio não persiste sozinho,
    /// só reconstrói em memória — ver comentário no método).</summary>
    public void PersistirMapaAtivo()
    {
        if (_mapaAtivo == null || string.IsNullOrWhiteSpace(_campanhaId)) return;
        CampanhaMapasStorage.SaveOrUpdate(_campanhaId, _mapaAtivo);
    }

    // ── Fog manual (ver Salas/Grid/FogRect.cs, FogEditorTools.cs) ───────────────────────────────
    //
    // Retângulos presos a células do grid (não à imagem do mapa) que ficam SEMPRE 100% pretos —
    // diferente da escuridão dinâmica (gradiente, ver seção de Visão Dinâmica), não depende de
    // nenhum token pra existir. Campanhas é modo de preparação single-player (sem "jogador" separado
    // do Mestre), então o alpha fica sempre 1 — ver Salas/Grid/MapManager.cs pra a versão com Mestre
    // vendo semitransparente.

    private Transform _containerFog;
    private MeshFilter _meshFilterFog;
    private MeshRenderer _meshRendererFog;
    private Material _materialFog;

    /// <summary>Adiciona um retângulo de fog manual, reconstrói a visualização e persiste
    /// imediatamente. Campanhas não tem conceito de host — sem gate, diferente da versão de
    /// Salas/Grid/MapManager.cs.</summary>
    public void AdicionarFogRect(FogRect rect)
    {
        if (_mapaAtivo == null || rect == null) return;

        _mapaAtivo.fogRects ??= new List<FogRect>();
        _mapaAtivo.fogRects.Add(rect);

        AtualizarMeshDeFogRects();
        PersistirMapaAtivo();
    }

    /// <summary>Remove fog manual dentro do retângulo de células informado — SUBTRAI essa área de
    /// qualquer FogRect já existente que a sobreponha, mantendo o resto de cada retângulo afetado
    /// (ver SubtrairRetangulo), em vez de apagar um retângulo inteiro de um clique só — mesma
    /// interação de arrastar de AdicionarFogRect, só que soltar REMOVE em vez de adicionar (ver
    /// FogEditorTools). Retorna true se algo mudou (e já persistiu). Campanhas não tem conceito de
    /// host — sem gate, diferente da versão de Salas/Grid/MapManager.cs.</summary>
    public bool ApagarFogNoRetangulo(int colMin, int linMin, int colMax, int linMax)
    {
        if (_mapaAtivo?.fogRects == null || _mapaAtivo.fogRects.Count == 0) return false;

        var novaLista = new List<FogRect>(_mapaAtivo.fogRects.Count);
        bool alterou = false;

        foreach (FogRect rect in _mapaAtivo.fogRects)
        {
            if (rect == null) continue;

            List<FogRect> restantes = SubtrairRetangulo(rect, colMin, linMin, colMax, linMax);
            if (restantes == null) { novaLista.Add(rect); continue; } // sem sobreposição — mantém intacto

            alterou = true;
            novaLista.AddRange(restantes); // 0 a 4 pedaços (0 = retângulo original apagado por inteiro)
        }

        if (!alterou) return false;

        _mapaAtivo.fogRects = novaLista;
        AtualizarMeshDeFogRects();
        PersistirMapaAtivo();
        return true;
    }

    // Subtrai o retângulo de células [eColMin..eColMax, eLinMin..eLinMax] de rect — devolve null se
    // não há sobreposição (rect fica intacto), ou a lista dos pedaços de rect que sobram FORA da
    // área subtraída (0 a 4 retângulos). Decomposição em "cruz": faixas de cima/baixo cobrem a
    // LARGURA TOTAL de rect, faixas de esquerda/direita cobrem só a ALTURA da interseção — evita
    // contar os cantos duas vezes, resultado são retângulos disjuntos que cobrem exatamente rect
    // menos a interseção.
    private static List<FogRect> SubtrairRetangulo(FogRect rect, int eColMin, int eLinMin, int eColMax, int eLinMax)
    {
        int iColMin = Mathf.Max(rect.colMin, eColMin);
        int iLinMin = Mathf.Max(rect.linMin, eLinMin);
        int iColMax = Mathf.Min(rect.colMax, eColMax);
        int iLinMax = Mathf.Min(rect.linMax, eLinMax);

        if (iColMin > iColMax || iLinMin > iLinMax) return null; // sem sobreposição

        var restantes = new List<FogRect>(4);

        if (iLinMax < rect.linMax) // faixa acima da interseção
            restantes.Add(new FogRect { colMin = rect.colMin, colMax = rect.colMax, linMin = iLinMax + 1, linMax = rect.linMax });
        if (iLinMin > rect.linMin) // faixa abaixo da interseção
            restantes.Add(new FogRect { colMin = rect.colMin, colMax = rect.colMax, linMin = rect.linMin, linMax = iLinMin - 1 });
        if (iColMin > rect.colMin) // faixa à esquerda, só na altura da interseção
            restantes.Add(new FogRect { colMin = rect.colMin, colMax = iColMin - 1, linMin = iLinMin, linMax = iLinMax });
        if (iColMax < rect.colMax) // faixa à direita, só na altura da interseção
            restantes.Add(new FogRect { colMin = iColMax + 1, colMax = rect.colMax, linMin = iLinMin, linMax = iLinMax });

        return restantes;
    }

    // ── Fontes de luz estática (tocha — ver Salas/Grid/LightSource.cs, LightEditorTools.cs) ──────
    //
    // Matematicamente idêntica à visão de um token PARADO — mesmo polígono de visão/bloqueio por
    // parede (ver AtualizarVisaoDeTodosOsTokens, que dobra um loop sobre _mapaAtivo.fontesDeLuz
    // igual ao loop que já existe pra tokens). Posição normalizada (0..1, relativa à imagem do
    // mapa — mesma convenção de WallLine), não presa a célula de grid.

    // Raio (em unidades de mundo) do marcador de edição de uma luz já plantada — usado tanto pro
    // desenho do marcador (ver LightEditorTools) quanto pro alvo de clique de RemoverFonteDeLuzEm.
    private float ObterRaioMarcadorLuz() => ObterTamanhoAtualDaCelula() * 0.3f;

    /// <summary>Adiciona uma fonte de luz, recalcula a visualização e persiste imediatamente.
    /// Campanhas não tem conceito de host — sem gate, diferente da versão de Salas/Grid/MapManager.cs.</summary>
    public void AdicionarFonteDeLuz(LightSource fonte)
    {
        if (_mapaAtivo == null || fonte == null) return;

        _mapaAtivo.fontesDeLuz ??= new List<LightSource>();
        _mapaAtivo.fontesDeLuz.Add(fonte);

        AtualizarVisaoDeTodosOsTokens();
        PersistirMapaAtivo();
    }

    /// <summary>Remove a fonte de luz mais próxima cujo marcador (ObterRaioMarcadorLuz) contém
    /// posicaoMundo. Retorna true se removeu (e já persistiu).</summary>
    public bool RemoverFonteDeLuzEm(Vector3 posicaoMundo)
    {
        if (_mapaAtivo?.fontesDeLuz == null || _mapaAtivo.fontesDeLuz.Count == 0) return false;

        float raioMarcador = ObterRaioMarcadorLuz();
        LightSource alvo = null;
        float menorDistancia = float.MaxValue;
        foreach (LightSource fonte in _mapaAtivo.fontesDeLuz)
        {
            if (fonte == null) continue;
            float distancia = Vector3.Distance(NormalizadoParaMundo(fonte.posicaoNormalizada), posicaoMundo);
            if (distancia <= raioMarcador && distancia < menorDistancia)
            {
                menorDistancia = distancia;
                alvo = fonte;
            }
        }
        if (alvo == null) return false;

        _mapaAtivo.fontesDeLuz.Remove(alvo);
        AtualizarVisaoDeTodosOsTokens();
        PersistirMapaAtivo();
        return true;
    }

    /// <summary>Todas as fontes de luz do mapa ativo, já convertidas pra mundo — usado só por
    /// LightEditorTools pra desenhar marcadores de edição (ver IGerenciadorDeParedes).</summary>
    public IEnumerable<(string id, Vector3 posicaoMundo, float raioMundo, float raioMetros)> ObterFontesDeLuzEmMundo()
    {
        if (_mapaAtivo?.fontesDeLuz == null) yield break;
        foreach (LightSource fonte in _mapaAtivo.fontesDeLuz)
        {
            if (fonte == null) continue;
            yield return (fonte.id, NormalizadoParaMundo(fonte.posicaoNormalizada), ConverterMetrosParaMundo(fonte.raioMetros), fonte.raioMetros);
        }
    }

    /// <summary>Muda o raio de uma fonte de luz já plantada, ao vivo (recalcula a visão, NÃO
    /// persiste/propaga) — ver PersistirRaioDaFonteDeLuz. Campanhas não tem conceito de host — sem
    /// gate, diferente da versão de Salas/Grid/MapManager.cs.</summary>
    public void DefinirRaioDaFonteDeLuz(string id, float raioMetros)
    {
        LightSource fonte = _mapaAtivo?.fontesDeLuz?.Find(f => f != null && f.id == id);
        if (fonte == null) return;

        fonte.raioMetros = Mathf.Max(0.1f, raioMetros);
        AtualizarVisaoDeTodosOsTokens();
    }

    /// <summary>Aplica + persiste o raio de uma fonte de luz — chamado ao soltar o slider do marcador
    /// (ver LightMarcadorUI/LightEditorTools), não a cada tick de arrasto.</summary>
    public void PersistirRaioDaFonteDeLuz(string id, float raioMetros)
    {
        DefinirRaioDaFonteDeLuz(id, raioMetros); // garante o valor final aplicado, mesmo sem onValueChanged intermediário
        PersistirMapaAtivo();
    }

    private void GarantirObjetosDeFog()
    {
        if (_containerFog != null) return;

        Transform pai = gridPrincipal != null ? gridPrincipal.transform : transform;

        GameObject goMesh = new GameObject("Visao_FogManual");
        goMesh.transform.SetParent(pai, false);
        _containerFog = goMesh.transform;

        _meshFilterFog = goMesh.AddComponent<MeshFilter>();
        _meshRendererFog = goMesh.AddComponent<MeshRenderer>();
        _materialFog = new Material(Shader.Find("Sprites/Default")) { color = new Color(0f, 0f, 0f, 1f) };
        _meshRendererFog.material = _materialFog;
        _meshRendererFog.sortingLayerID = mapaRenderer != null ? mapaRenderer.sortingLayerID : 0;
        // Acima até da escuridão dinâmica (+10) — fog manual sempre vence visualmente sobre o gradiente.
        _meshRendererFog.sortingOrder = (mapaRenderer != null ? mapaRenderer.sortingOrder : 0) + 11;
        _meshRendererFog.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _meshRendererFog.receiveShadows = false;
        _meshRendererFog.enabled = false;
    }

    /// <summary>Reconstrói o mesh dos retângulos de fog manual do mapa ativo — um quad por FogRect,
    /// chamado ao carregar o mapa (CarregarMapa) e sempre que a lista muda (Adicionar/ApagarFogNoRetangulo).</summary>
    private void AtualizarMeshDeFogRects()
    {
        if (_mapaAtivo?.fogRects == null || _mapaAtivo.fogRects.Count == 0)
        {
            if (_meshRendererFog != null) _meshRendererFog.enabled = false;
            return;
        }

        GarantirObjetosDeFog();

        var vertices = new List<Vector3>(_mapaAtivo.fogRects.Count * 4);
        var triangulos = new List<int>(_mapaAtivo.fogRects.Count * 6);
        float z = mapaRenderer != null ? mapaRenderer.transform.position.z - 0.03f : -0.03f;

        foreach (FogRect rect in _mapaAtivo.fogRects)
        {
            if (rect == null) continue;
            AdicionarRetanguloDeFogAoBuffer(rect, z, vertices, triangulos);
        }

        Mesh mesh = _meshFilterFog.sharedMesh;
        if (mesh == null)
        {
            mesh = new Mesh { name = "MeshFogManual" };
            _meshFilterFog.sharedMesh = mesh;
        }
        mesh.Clear();
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangulos.ToArray();
        mesh.RecalculateBounds();

        _meshRendererFog.enabled = true;
    }

    private void AdicionarRetanguloDeFogAoBuffer(FogRect rect, float z, List<Vector3> vertices, List<int> triangulos)
    {
        Bounds boundsMin = CelulaParaBoundsMundo(rect.colMin, rect.linMin);
        Bounds boundsMax = CelulaParaBoundsMundo(rect.colMax, rect.linMax);

        float minX = Mathf.Min(boundsMin.min.x, boundsMax.min.x);
        float minY = Mathf.Min(boundsMin.min.y, boundsMax.min.y);
        float maxX = Mathf.Max(boundsMin.max.x, boundsMax.max.x);
        float maxY = Mathf.Max(boundsMin.max.y, boundsMax.max.y);

        int baseIndex = vertices.Count;
        vertices.Add(new Vector3(minX, minY, z));
        vertices.Add(new Vector3(maxX, minY, z));
        vertices.Add(new Vector3(maxX, maxY, z));
        vertices.Add(new Vector3(minX, maxY, z));

        triangulos.Add(baseIndex); triangulos.Add(baseIndex + 1); triangulos.Add(baseIndex + 2);
        triangulos.Add(baseIndex); triangulos.Add(baseIndex + 2); triangulos.Add(baseIndex + 3);
    }

    private int ObterLayerVisionBlocker()
    {
        int layer = LayerMask.NameToLayer(nomeLayerVisionBlocker);
        if (layer < 0)
        {
            VTTLog.ForjaError($"[CampanhaMapManager] A layer '{nomeLayerVisionBlocker}' não existe no projeto — " +
                "crie-a em Edit > Project Settings > Tags and Layers (Unity não permite criar layers em runtime). " +
                "As paredes vão ficar na layer padrão do GameObject até isso ser feito.");
        }
        return layer;
    }

    private Material ObterMaterialLinhaParede()
    {
        if (_materialLinhaParede != null) return _materialLinhaParede;
        _materialLinhaParede = new Material(Shader.Find("Sprites/Default"));
        return _materialLinhaParede;
    }

    private Transform ObterOuCriarContainerParedes()
    {
        if (_containerParedes != null) return _containerParedes;
        Transform pai = gridPrincipal != null ? gridPrincipal.transform : transform;
        GameObject go = new GameObject("Paredes_Detectadas");
        go.transform.SetParent(pai, false);
        _containerParedes = go.transform;
        return _containerParedes;
    }

    private void LimparParedes()
    {
        if (_containerParedes == null) return;
        for (int i = _containerParedes.childCount - 1; i >= 0; i--)
            Destroy(_containerParedes.GetChild(i).gameObject);
    }

    /// <summary>Liga/desliga a camada de depuração das paredes detectadas — continua público caso
    /// algum botão queira só alternar a visibilidade (sem revetorizar) pelo Inspector (UnityEvent).</summary>
    public void AlternarOverlayParedes()
    {
        _mostrarParedesDetectadas = !_mostrarParedesDetectadas;
        AplicarVisibilidadeOverlayParedes();
    }

    /// <summary>Chamado pelo clique de botaoFerramentasParede — mostra/esconde o GameObject com os
    /// controles de edição manual (WallEditorTools): sliders de calibração, botões de ferramenta
    /// (Desenhar/Apagar), slider de raio da borracha etc.</summary>
    private void AlternarPainelFerramentasParede()
    {
        if (painelFerramentasParede == null) return;
        painelFerramentasParede.SetActive(!painelFerramentasParede.activeSelf);
    }

    private void AplicarVisibilidadeOverlayParedes()
    {
        if (_containerParedes == null) return;

        foreach (Transform filho in _containerParedes)
        {
            LineRenderer lr = filho.GetComponent<LineRenderer>();
            if (lr != null) lr.enabled = _mostrarParedesDetectadas;
        }
    }

    // ── Vetorização (importação inicial + revetorização) ─────────────────────────
    //
    // Roda SEMPRE aqui, em CampanhaMapManager — nunca em CampanhaMapImportUI (onde vivia antes) —
    // porque o GameObject deste componente fica ativo o tempo todo durante a campanha (garantido em
    // CampanhasManager.AbrirCampanha), enquanto CampanhaMapImportUI é escondido/reaberto o tempo
    // todo (Configurar/EditarMapa/OnClickFechar todos mexem em gameObject.SetActive). Como a
    // vetorização roda em várias frames (CampanhaMapaVetorizador.Vetorizar cede o frame periodicamente),
    // se ela tivesse sido iniciada como StartCoroutine num MonoBehaviour que pode ser desativado no
    // meio do caminho, a Unity mata a coroutine SILENCIOSAMENTE antes do SaveOrUpdate final rodar —
    // a parede aparece por um instante e some sem nunca ir pro disco. Esse era o bug de "não salva".

    private bool _vetorizacaoEmAndamento;

    /// <summary>Chamado por CampanhaMapImportUI ao importar um mapa novo (ou editar um existente sem
    /// paredes ainda) com o toggle "Vetorizar automaticamente" ligado — sem confirmação, já que não
    /// há nada pra sobrescrever.</summary>
    public void VetorizarMapa(CampanhaMapaData mapa)
    {
        if (mapa == null || string.IsNullOrWhiteSpace(_campanhaId)) return;

        if (_vetorizacaoEmAndamento)
        {
            VTTLog.ForjaError("[CampanhaMapManager] Já existe uma vetorização em andamento — aguarde ela terminar antes de iniciar outra.");
            return;
        }

        StartCoroutine(VetorizarEExecutarCoroutine(mapa));
    }

    /// <summary>Chamado pelo clique de botaoRevetorizarParedes — pede confirmação (a detecção
    /// substitui as paredes atuais da cena, que podem já ter sido ajustadas manualmente) e, se
    /// aceito, roda a vetorização de novo sobre o mapa ativo com a calibração atual dos sliders, sem
    /// precisar reimportar a imagem.</summary>
    public void RevetorizarMapaAtivo()
    {
        if (_mapaAtivo == null || string.IsNullOrWhiteSpace(_campanhaId))
        {
            VTTLog.ForjaError("[CampanhaMapManager] Nenhum mapa ativo para revetorizar.");
            return;
        }

        if (_vetorizacaoEmAndamento)
        {
            VTTLog.ForjaError("[CampanhaMapManager] Já existe uma vetorização em andamento — aguarde ela terminar antes de iniciar outra.");
            return;
        }

        CampanhaMapaData mapa = _mapaAtivo;
        int paredesAtuais = mapa.paredes?.Count ?? 0;
        string mensagem = paredesAtuais > 0
            ? $"Detectar paredes automaticamente de novo em '{mapa.nome}'?\n\nIsso substitui as {paredesAtuais} parede(s) atual(is) dessa cena pelo resultado da nova detecção."
            : $"Detectar paredes automaticamente em '{mapa.nome}'?";

        if (PopupManager.Instance != null)
        {
            PopupManager.Instance.MostrarConfirmacao(
                "Revetorizar Paredes", mensagem,
                onSim: () => StartCoroutine(VetorizarEExecutarCoroutine(mapa)),
                labelSim: "Detectar", labelNao: "Cancelar");
        }
        else
        {
            StartCoroutine(VetorizarEExecutarCoroutine(mapa));
        }
    }

    private IEnumerator VetorizarEExecutarCoroutine(CampanhaMapaData mapa)
    {
        _vetorizacaoEmAndamento = true;

        Texture2D textura = CarregarTexturaLegivelDeArquivo(mapa.imagemPath);
        if (textura == null)
        {
            VTTLog.ForjaError($"[CampanhaMapManager] Não foi possível reler a imagem '{mapa.imagemPath}' para vetorizar.");
            _vetorizacaoEmAndamento = false;
            yield break;
        }

        PopupManager.Instance?.MostrarCarregamento("Vetorizando paredes", mapa.nome);
        yield return null;

        List<WallLine> paredes = null;
        yield return StartCoroutine(CampanhaMapaVetorizador.Vetorizar(
            textura, _configVetorizacao,
            onProgresso: (p) => PopupManager.Instance?.AtualizarProgresso(p),
            onConcluido: (resultado) => paredes = resultado));

        Destroy(textura);

        // Salva IMEDIATAMENTE após calcular — antes de qualquer coisa que possa depender do estado
        // atual da UI (painel de mapa aberto, mapa ainda ativo etc.), pra garantir que o resultado vá
        // pro disco mesmo que o usuário já tenha navegado pra outro lugar enquanto isso rodava.
        //
        // Filtra qualquer WallLine com coordenada NaN/Infinity antes de salvar: JsonUtility grava
        // esses valores como os tokens literais "NaN"/"Infinity" (JSON inválido de verdade), e um só
        // segmento assim no meio de milhares pode quebrar o parser ao reler o arquivo inteiro depois
        // — os outros mapas da campanha (que não têm nada a ver com essa vetorização) sumiriam junto.
        List<WallLine> paredesValidas = SanitizarParedes(paredes);
        if (paredes != null && paredesValidas.Count < paredes.Count)
            VTTLog.ForjaError($"[CampanhaMapManager] Descartados {paredes.Count - paredesValidas.Count} " +
                "segmento(s) de parede com coordenada inválida (NaN/Infinity) antes de salvar.");

        mapa.vetorizado = true;
        mapa.paredes = paredesValidas;
        CampanhaMapasStorage.SaveOrUpdate(_campanhaId, mapa);

        // Só atualiza a visualização se esse ainda for o mapa ativo no grid — evita sobrescrever uma
        // cena diferente que o usuário tenha carregado enquanto a vetorização rodava em segundo plano.
        if (_mapaAtivo != null && _mapaAtivo.mapaId == mapa.mapaId)
        {
            ReconstruirParedes(mapa.paredes);

            // O ponto inteiro de vetorizar é ver o resultado — força o overlay visível em vez de só
            // reaplicar o estado anterior (que podia estar desligado). Também serve de confirmação
            // visual imediata de que o resultado foi computado (e salvo).
            _mostrarParedesDetectadas = true;
            AplicarVisibilidadeOverlayParedes();
        }

        PopupManager.Instance?.Fechar();
        _vetorizacaoEmAndamento = false;

        VTTLog.Forja($"Mapa '{mapa.nome}' vetorizado (limiar {_configVetorizacao.limiarLuminancia:0.00}, " +
            $"dilatação {_configVetorizacao.raioDilatacao}px, contorno mín. {_configVetorizacao.comprimentoMinimoContorno}, " +
            $"épsilon {_configVetorizacao.epsilonRDP:0.00}): {mapa.paredes.Count} trecho(s) de parede detectado(s).");
    }

    private static List<WallLine> SanitizarParedes(List<WallLine> paredes)
    {
        var validas = new List<WallLine>();
        if (paredes == null) return validas;

        foreach (WallLine l in paredes)
            if (l != null && EhFinito(l.pontoA) && EhFinito(l.pontoB))
                validas.Add(l);

        return validas;
    }

    private static bool EhFinito(Vector2 v)
        => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y);

    private static Texture2D CarregarTexturaLegivelDeArquivo(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho)) return null;

        byte[] bytes = File.ReadAllBytes(caminho);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(bytes))
        {
            Destroy(tex);
            return null;
        }
        return tex;
    }

    // ── Visão Dinâmica (Fog of War) ──────────────────────────────────────────
    //
    // "Selecionar Visão" no menu de atalho de um token (ver CampanhaTokenDraggable.AbrirMenuDeAtalho)
    // lança raios do CENTRO DO TOKEN, cada um parando no primeiro colisor da layer
    // nomeLayerVisionBlocker (mesma layer das paredes), no raio de visão do token (ver
    // ConverterMetrosParaMundo), ou na borda real da imagem do mapa (ObterLimiteExternoDoMapa) —
    // o que vier primeiro. Os pontos de impacto, ordenados por ângulo, formam um polígono "em
    // estrela" — todo ponto dele é visível em linha reta a partir do centro, por construção
    // (garantido pelo próprio raycast).
    //
    // A escuridão é um mesh "donut": pra cada setor angular entre dois raios consecutivos, um quad
    // liga o ponto de impacto (de dentro) ao mesmo ponto projetado até a borda do mapa (de fora). Como
    // a região DENTRO do polígono de impacto não tem nenhum triângulo, o mapa/tokens/paredes por baixo
    // aparecem normalmente ali; fora dela, o quad cobre tudo com preto 100% opaco.
    //
    // RAYCASTING ORIENTADO A VÉRTICE (otimização): em vez de depender só de uma grade densa de raios
    // base em ângulos fixos pra "acertar" os cantos por densidade, cada vértice relevante (ponta de
    // parede dentro do raio de visão — ver ColetarVerticesDeParedesNoRaio — e as 4 quinas do mapa)
    // ganha 3 raios exatos: no ângulo do vértice, e ±epsilonAngular. O raio central bate na quina; os
    // dois raios "de raspão" continuam até a próxima parede/borda, desenhando a sombra geométrica
    // exata por trás do obstáculo — sem depender de sorte ou de centenas de raios base pra isso.
    // raiosBaseVisao, com essa técnica, só precisa cobrir a SUAVIDADE do arco do raio de visão em
    // áreas abertas sem parede por perto (onde não há vértice nenhum pra gerar um raio exato) — por
    // isso pode ser bem menor que antes (era 360, sem essa técnica) sem perder nitidez nos cantos.
    //
    // Culling espacial (Physics2D.OverlapCircleNonAlloc, ver ColetarVerticesDeParedesNoRaio): só
    // extrai vértices das paredes que realmente caem dentro do raio de visão do token, em vez de
    // percorrer TODAS as paredes do mapa a cada recálculo — importa em mapas com centenas/milhares de
    // segmentos de parede vetorizados. Raycasts usam Physics2D.RaycastNonAlloc com um buffer
    // pré-alocado (_bufferRaycastHit) pra não gerar lixo de GC a cada frame de arrasto.

    [Header("Visão Dinâmica (Fog of War)")]
    [Tooltip("Raios base distribuídos uniformemente em 360° — só suavizam o limite CIRCULAR do raio de " +
        "visão em áreas abertas sem parede por perto. Cantos de parede/mapa já ficam exatos via " +
        "raycasting orientado a vértice (ver comentário acima), então não precisa de um número alto " +
        "aqui só por causa deles.")]
    [SerializeField, Range(16, 360)] private int raiosBaseVisao = 48;
    [Tooltip("Alpha máximo da escuridão dinâmica (spotlight) — antes era sempre 100% opaco; agora é " +
        "o teto que o gradiente atinge a partir de transicaoGradienteEmCelulas da borda da visão.")]
    [SerializeField, Range(0f, 1f)] private float alphaMaximoVisaoDinamica = 0.85f;
    [Tooltip("Distância (em células de grid) a partir da borda da visão até a escuridão atingir o " +
        "alpha máximo — 0 volta ao comportamento antigo (opaco logo na borda, sem gradiente).")]
    [SerializeField, Min(0f)] private float transicaoGradienteEmCelulas = 2f;

    // Buffers pré-alocados pra Physics2D *NonAlloc — a visão recalcula a cada frame de arrasto do
    // token (ver CampanhaTokenDraggable.Update), então gerar lixo de GC nessa frequência causaria
    // engasgos perceptíveis. MaxColisoresNoRaioDeVisao é generoso o bastante pra qualquer mapa
    // razoável; se um dia estourar (mapa com paredes extremamente densas dentro de um único raio de
    // visão), Physics2D.OverlapCircleNonAlloc simplesmente trunca — não estoura exceção.
    private const int MaxColisoresNoRaioDeVisao = 128;
    private readonly Collider2D[] _bufferColisoresNoRaio = new Collider2D[MaxColisoresNoRaioDeVisao];
    private readonly RaycastHit2D[] _bufferRaycastHit = new RaycastHit2D[1];
    [Tooltip("Slider de raio de visão (em METROS) do token com a visão spotlight ativa no momento — " +
        "só fica interagível enquanto 'Selecionar Visão' estiver ligado num token (ver MostrarVisaoDoToken).")]
    [SerializeField] private Slider sliderRaioVisao;
    [SerializeField] private TMP_Text textoRaioVisao;

    [Header("Visão Geral de Todos os Tokens")]
    [Tooltip("Alterna a visão combinada de TODOS os tokens da mesa ao mesmo tempo. Diferente de " +
        "'Selecionar Visão' num token (escuridão 100% opaca fora dele, ver MostrarVisaoDoToken), aqui a " +
        "área fora da visão combinada só fica ESCURECIDA (alphaVisaoGeralTokens), não totalmente preta " +
        "— o mestre continua enxergando o mapa por baixo. Mutuamente exclusivo com a visão de um token só.")]
    [SerializeField] private Button botaoVisaoTodosOsTokens;
    [SerializeField, Range(0f, 1f)] private float alphaVisaoGeralTokens = 0.6f;

    // 1 quadrado de combate = 1.5m reais (convenção de mesa) — usado pra converter o raio de visão
    // (sempre exibido/editado em metros na UI) pra unidades de mundo do mapa atual, independente de
    // quantas unidades de mundo esse mapa específico usa por célula (ver ConverterMetrosParaMundo).
    private const float MetrosPorQuadrado = 1.5f;
    private const float RaioVisaoPadraoMetros = 18f; // ~60 pés — visão no escuro padrão de D&D 5e

    private Transform _containerVisao;
    private MeshFilter _meshFilterVisao;
    private MeshRenderer _meshRendererVisao;
    private Material _materialEscuridaoVisao;
    private CampanhaTokenDraggable _tokenComVisaoAtiva;

    private bool _visaoTodosOsTokensAtiva;
    private Transform _containerVisaoGeral;
    private MeshFilter _meshFilterVisaoGeral;
    private MeshRenderer _meshRendererVisaoGeral;
    private Material _materialEscuridaoVisaoGeral;

    /// <summary>Chamado pela opção "Selecionar Visão" do menu de atalho de um token — alterna: clicar
    /// de novo no mesmo token esconde a visão; clicar em outro token troca pra a visão dele.</summary>
    public void MostrarVisaoDoToken(CampanhaTokenDraggable token)
    {
        if (token == null) return;

        if (_tokenComVisaoAtiva == token)
        {
            EsconderVisaoDoToken();
            return;
        }

        DesligarVisaoDeTodosOsTokens(); // mutuamente exclusivo com a visão geral (ver AlternarVisaoDeTodosOsTokens)
        _tokenComVisaoAtiva = token;
        SincronizarSliderRaioVisao();
        AtualizarVisao(token);
    }

    public void EsconderVisaoDoToken()
    {
        _tokenComVisaoAtiva = null;
        if (_meshRendererVisao != null) _meshRendererVisao.enabled = false;
        SincronizarSliderRaioVisao();
    }

    /// <summary>Chamado por CampanhaTokenDraggable a cada frame de arrasto (não só ao soltar) e ao
    /// terminar de arrastar — qualquer visão ativa que envolva esse token (spotlight dele mesmo, OU a
    /// visão geral de todos os tokens) recalcula pra acompanhar a nova posição em tempo real. No-op se
    /// nenhuma delas estiver ativa pra esse token.</summary>
    public void AtualizarVisaoSeAtivo(CampanhaTokenDraggable token)
    {
        if (token == null) return;

        if (token == _tokenComVisaoAtiva) AtualizarVisao(token);
        else if (_visaoTodosOsTokensAtiva) AtualizarVisaoDeTodosOsTokens();
    }

    private void AtualizarVisao(CampanhaTokenDraggable token)
    {
        if (mapaRenderer == null || mapaRenderer.sprite == null || token == null) return;

        Vector3 origem = token.transform.position;
        origem.z = 0f;

        float raioMundo = ConverterMetrosParaMundo(token.RaioVisaoMetros);
        List<Vector3> pontos = CalcularPoligonoDeVisao(origem, raioMundo, out List<Vector3> pontosNaBorda);
        if (pontos.Count < 3) { EsconderVisaoDoToken(); return; }

        GarantirObjetosDeVisao();
        ConstruirMeshEscuridao(origem, pontos, pontosNaBorda);
        _meshRendererVisao.enabled = true;
    }

    // Converte metros (unidade sempre usada na UI/slider/persistência) pra unidades de mundo do mapa
    // ATUAL, via a convenção fixa de mesa "1 quadrado = 1.5m" — não assume que unidadesPorCelula do
    // mapa seja 1.5 (embora normalmente seja), só que o slider sempre representa metros reais.
    private float ConverterMetrosParaMundo(float metros)
        => (metros / MetrosPorQuadrado) * ObterTamanhoAtualDaCelula();

    // Lança os raios (base + extras nos vértices de parede), ordena por ângulo — TODOS na mesma
    // convenção [0, 2π), ver NormalizarAngulo — e devolve os pontos de impacto (retorno) junto com os
    // mesmos pontos projetados até a borda real do mapa (pontosNaBordaExterna, out), usados por
    // ConstruirMeshEscuridao pra formar os quads do donut.
    //
    // BUG CORRIGIDO: os raios extras vinham de Mathf.Atan2, que devolve ângulos em (-π, π], enquanto
    // os raios base usavam [0, 2π). Ordenar essa mistura sem normalizar juntava, por exemplo, um
    // vértice "a oeste" com ângulo ≈ +π a outro "a oeste" com ângulo ≈ -π como se estivessem em
    // extremos opostos da lista — o quad "consecutivo" entre eles cortava o mapa inteiro pelo centro.
    private List<Vector3> CalcularPoligonoDeVisao(Vector3 origem, float raioMundo, out List<Vector3> pontosNaBordaExterna)
    {
        Bounds limiteMapa = ObterLimiteExternoDoMapa();

        var angulos = new List<float>(raiosBaseVisao + 32);
        float passoBase = 2f * Mathf.PI / raiosBaseVisao;
        for (int i = 0; i < raiosBaseVisao; i++)
            angulos.Add(i * passoBase);

        const float epsilonAngular = 0.0009f; // ~0.05°, evita o raio "raspar" exatamente na quina do colisor

        // Local function pra não duplicar essas 4 linhas nos dois lugares que precisam delas (vértices
        // de parede E as 4 quinas do mapa, logo abaixo) — captura origem/angulos/epsilonAngular do escopo.
        void AdicionarAnguloDeVertice(Vector3 ponto)
        {
            float ang = NormalizarAngulo(Mathf.Atan2(ponto.y - origem.y, ponto.x - origem.x));
            angulos.Add(NormalizarAngulo(ang - epsilonAngular));
            angulos.Add(ang);
            angulos.Add(NormalizarAngulo(ang + epsilonAngular));
        }

        // Raycasting orientado a vértice (ver comentário no topo da seção) — cantos exatos de parede
        // (só as que estão dentro do raio de visão, culling espacial) e das 4 quinas do mapa.
        foreach (Vector3 vertice in ColetarVerticesDeParedesNoRaio(origem, raioMundo))
            AdicionarAnguloDeVertice(vertice);

        AdicionarAnguloDeVertice(new Vector3(limiteMapa.min.x, limiteMapa.min.y, 0f));
        AdicionarAnguloDeVertice(new Vector3(limiteMapa.max.x, limiteMapa.min.y, 0f));
        AdicionarAnguloDeVertice(new Vector3(limiteMapa.max.x, limiteMapa.max.y, 0f));
        AdicionarAnguloDeVertice(new Vector3(limiteMapa.min.x, limiteMapa.max.y, 0f));

        angulos.Sort();

        int layer = ObterLayerVisionBlocker();
        int mascara = layer >= 0 ? (1 << layer) : 0;

        var pontos = new List<Vector3>(angulos.Count);
        pontosNaBordaExterna = new List<Vector3>(angulos.Count);

        float anguloAnterior = float.NegativeInfinity;
        foreach (float ang in angulos)
        {
            // Descarta duplicata angular quase idêntica (dois vértices de parede muito próximos, ou o
            // epsilon de um raio encostando no ângulo exato do próximo) — evita triângulos degenerados.
            if (ang - anguloAnterior < 1e-5f) continue;
            anguloAnterior = ang;

            Vector3 direcao = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);

            // O ponto EXTERNO do donut (usado só pra desenhar a escuridão até lá) continua sempre a
            // borda REAL do mapa, com ou sem limite de raio — é até onde a escuridão precisa cobrir.
            Vector3 pontoNaBorda = IntersetarRetangulo(origem, direcao, limiteMapa);
            float distanciaAteABorda = Vector3.Distance(origem, pontoNaBorda);

            // O ponto de VISIBILIDADE (interno) para no que vier primeiro: parede ou raio de visão do
            // token — diferente do ponto externo, que nunca é limitado pelo raio.
            float distanciaLimiteVisao = Mathf.Min(distanciaAteABorda, raioMundo);
            Vector3 pontoNoLimiteDeVisao = origem + direcao * distanciaLimiteVisao;

            Vector3 ponto = mascara != 0
                ? ObterPontoDeImpacto(origem, direcao, distanciaLimiteVisao, mascara, pontoNoLimiteDeVisao)
                : pontoNoLimiteDeVisao;

            pontos.Add(ponto);
            pontosNaBordaExterna.Add(pontoNaBorda);
        }

        return pontos;
    }

    // Normaliza qualquer ângulo (incluindo os de Mathf.Atan2, que vêm em (-π, π]) pra [0, 2π) — mesma
    // convenção dos raios base, condição necessária pra um Sort() numérico simples produzir a ordem
    // angular correta ao redor do círculo completo.
    private static float NormalizarAngulo(float ang)
    {
        const float duasVoltas = 2f * Mathf.PI;
        ang %= duasVoltas;
        if (ang < 0f) ang += duasVoltas;
        return ang;
    }

    // *NonAlloc com buffer de tamanho 1 pré-alocado (_bufferRaycastHit) — evita gerar lixo de GC a
    // cada raio (dezenas por token, recalculado a cada frame de arrasto). Pra um único raio (não um
    // overlap), o primeiro resultado já é o impacto mais próximo.
    private Vector3 ObterPontoDeImpacto(Vector3 origem, Vector3 direcao, float distanciaMaxima, int mascara, Vector3 fallback)
    {
        int qtd = Physics2D.RaycastNonAlloc(origem, direcao, _bufferRaycastHit, distanciaMaxima, mascara);
        return qtd > 0 && _bufferRaycastHit[0].collider != null ? (Vector3)_bufferRaycastHit[0].point : fallback;
    }

    // Culling espacial (Physics2D.OverlapCircleNonAlloc): só extrai vértices das paredes que realmente
    // caem dentro do raio de visão do token, em vez de percorrer TODAS as paredes da cena a cada
    // recálculo — importa em mapas com muitos segmentos de parede vetorizados onde só uma fração
    // pequena está perto o bastante do token pra ser relevante.
    private List<Vector3> ColetarVerticesDeParedesNoRaio(Vector3 origem, float raioMundo)
    {
        var vertices = new List<Vector3>();

        int layer = ObterLayerVisionBlocker();
        int mascara = layer >= 0 ? (1 << layer) : 0;
        if (mascara == 0) return vertices;

        int qtd = Physics2D.OverlapCircleNonAlloc(origem, raioMundo, _bufferColisoresNoRaio, mascara);
        for (int i = 0; i < qtd; i++)
        {
            if (_bufferColisoresNoRaio[i] is not EdgeCollider2D col) continue;

            foreach (Vector2 pontoLocal in col.points)
                vertices.Add(col.transform.TransformPoint(pontoLocal));
        }

        return vertices;
    }

    // Bounds real da imagem do mapa em espaço de mundo — Renderer.bounds já contabiliza a
    // posição/escala atuais do transform. Antes usava ObterBoundsGridFixo() (o retângulo do GRID de
    // combate, dimensionado por tamanhoMundoGridFixo), que tem proporção diferente da imagem real do
    // mapa — daí os polígonos "fora de esquadro" nas bordas.
    private Bounds ObterLimiteExternoDoMapa() => mapaRenderer.bounds;

    // Interseção raio×retângulo (método dos "slabs") assumindo origem DENTRO do retângulo.
    private static Vector3 IntersetarRetangulo(Vector3 origem, Vector3 direcao, Bounds rect)
    {
        float tMax = float.MaxValue;

        if (Mathf.Abs(direcao.x) > 1e-6f)
        {
            float t1 = (rect.min.x - origem.x) / direcao.x;
            float t2 = (rect.max.x - origem.x) / direcao.x;
            tMax = Mathf.Min(tMax, Mathf.Max(t1, t2));
        }

        if (Mathf.Abs(direcao.y) > 1e-6f)
        {
            float t1 = (rect.min.y - origem.y) / direcao.y;
            float t2 = (rect.max.y - origem.y) / direcao.y;
            tMax = Mathf.Min(tMax, Mathf.Max(t1, t2));
        }

        return origem + direcao * Mathf.Max(0f, tMax);
    }

    private void GarantirObjetosDeVisao()
    {
        if (_containerVisao != null) return;

        Transform pai = gridPrincipal != null ? gridPrincipal.transform : transform;

        GameObject goMesh = new GameObject("Visao_Escuridao");
        goMesh.transform.SetParent(pai, false);
        _containerVisao = goMesh.transform;

        _meshFilterVisao = goMesh.AddComponent<MeshFilter>();
        _meshRendererVisao = goMesh.AddComponent<MeshRenderer>();
        _materialEscuridaoVisao = new Material(Shader.Find("Sprites/Default"));
        // Alpha do MATERIAL é o teto do gradiente (multiplicado pelo alpha por-vértice, ver
        // AdicionarDonutAoBuffer) — não é mais opaco fixo.
        _materialEscuridaoVisao.color = new Color(0f, 0f, 0f, alphaMaximoVisaoDinamica);
        _meshRendererVisao.material = _materialEscuridaoVisao;
        _meshRendererVisao.sortingLayerID = mapaRenderer != null ? mapaRenderer.sortingLayerID : 0;
        // Acima de tudo (mapa/grid/paredes/tokens em +1..+4) — a escuridão precisa cobrir até os tokens.
        _meshRendererVisao.sortingOrder = (mapaRenderer != null ? mapaRenderer.sortingOrder : 0) + 10;
        _meshRendererVisao.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _meshRendererVisao.receiveShadows = false;
        _meshRendererVisao.enabled = false;
    }

    // Distância (em unidades de mundo) que o gradiente percorre a partir da borda da visão até
    // atingir o alpha máximo — ver AdicionarDonutAoBuffer.
    private float ObterDistanciaTransicaoGradiente() => transicaoGradienteEmCelulas * ObterTamanhoAtualDaCelula();

    // Constrói o mesh "donut" da escuridão de UM token e aplica no MeshFilter do spotlight.
    private void ConstruirMeshEscuridao(Vector3 origem, List<Vector3> pontos, List<Vector3> pontosExternos)
    {
        var vertices = new List<Vector3>(pontos.Count * 3);
        var triangulos = new List<int>(pontos.Count * 12);
        var cores = new List<Color>(pontos.Count * 3);
        AdicionarDonutAoBuffer(origem, pontos, pontosExternos, ObterDistanciaTransicaoGradiente(), vertices, triangulos, cores);
        AplicarBufferAoMesh(_meshFilterVisao, "MeshEscuridaoVisao", vertices, triangulos, cores);
    }

    // Escreve vertices/triangulos/cores no sharedMesh de um MeshFilter (cria o Mesh na primeira
    // chamada) — reaproveitado pelo spotlight (ConstruirMeshEscuridao) e pela visão geral de todos
    // os tokens (AtualizarVisaoDeTodosOsTokens), que antes tinham esse bloco duplicado inline.
    private static void AplicarBufferAoMesh(MeshFilter meshFilter, string nomeMesh, List<Vector3> vertices, List<int> triangulos, List<Color> cores)
    {
        Mesh mesh = meshFilter.sharedMesh;
        if (mesh == null)
        {
            mesh = new Mesh { name = nomeMesh };
            meshFilter.sharedMesh = mesh;
        }

        mesh.Clear();
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangulos.ToArray();
        mesh.colors = cores.ToArray();
        mesh.RecalculateBounds();
    }

    // Adiciona ao buffer compartilhado (vertices/triangulos/cores) os 3×N vértices e 4 triângulos
    // por setor angular consecutivo de UM donut — reaproveitado tanto pelo spotlight de um único
    // token (ConstruirMeshEscuridao) quanto pela combinação de vários tokens num mesh só (ver
    // AtualizarVisaoDeTodosOsTokens). baseIndex garante que os índices de cada donut concatenado não
    // colidam com os donuts já adicionados antes dele no mesmo buffer.
    //
    // Três anéis por setor (antes eram dois): INTERNO (borda da visão, alpha 0 — transparente ali
    // mesmo), MEIO (a distanciaTransicaoGradiente da borda da visão EM DIREÇÃO à borda do mapa —
    // Vector3.MoveTowards nunca ultrapassa o ponto externo, então clampa sozinho se o mapa for
    // pequeno) e EXTERNO (borda do mapa), os dois últimos com alpha 1. O alpha FINAL de cada pixel é
    // material.a × vertex.a (comportamento padrão do shader Sprites/Default) — como o material já
    // carrega o alpha máximo desejado (ver GarantirObjetosDeVisao/GarantirObjetosDeVisaoGeral), o
    // vertex alpha só precisa ir de 0 a 1 pra o efeito ficar "escurece progressivamente até um teto,
    // não pula direto pro opaco".
    private static void AdicionarDonutAoBuffer(Vector3 origem, List<Vector3> pontos, List<Vector3> pontosExternos,
        float distanciaTransicaoGradiente, List<Vector3> vertices, List<int> triangulos, List<Color> cores)
    {
        int n = pontos.Count;
        int baseIndex = vertices.Count;
        float z = origem.z - 0.02f;

        for (int i = 0; i < n; i++)
        {
            Vector3 pInterno = pontos[i]; pInterno.z = z;
            vertices.Add(pInterno);
            cores.Add(new Color(0f, 0f, 0f, 0f));
        }
        for (int i = 0; i < n; i++)
        {
            Vector3 pMeio = Vector3.MoveTowards(pontos[i], pontosExternos[i], distanciaTransicaoGradiente);
            pMeio.z = z;
            vertices.Add(pMeio);
            cores.Add(new Color(0f, 0f, 0f, 1f));
        }
        for (int i = 0; i < n; i++)
        {
            Vector3 pExterno = pontosExternos[i]; pExterno.z = z;
            vertices.Add(pExterno);
            cores.Add(new Color(0f, 0f, 0f, 1f));
        }

        // Cada setor conecta i ao PRÓXIMO (i+1)%n — inclusive o último ao primeiro, fechando o laço de
        // 360° pelo mesmo caminho uniforme usado em todos os outros setores (sem caso especial).
        for (int i = 0; i < n; i++)
        {
            int iProx = (i + 1) % n;
            int pi = baseIndex + i, piProx = baseIndex + iProx;
            int mi = baseIndex + n + i, miProx = baseIndex + n + iProx;
            int qi = baseIndex + 2 * n + i, qiProx = baseIndex + 2 * n + iProx;

            // Quad interno → meio (o gradiente em si)
            triangulos.Add(pi); triangulos.Add(piProx); triangulos.Add(miProx);
            triangulos.Add(pi); triangulos.Add(miProx); triangulos.Add(mi);

            // Quad meio → externo (achatado no alpha máximo)
            triangulos.Add(mi); triangulos.Add(miProx); triangulos.Add(qiProx);
            triangulos.Add(mi); triangulos.Add(qiProx); triangulos.Add(qi);
        }
    }

    // ── Visão Geral de Todos os Tokens (GM overview — ver Header no topo do arquivo) ────────────

    /// <summary>Chamado pelo clique de botaoVisaoTodosOsTokens.</summary>
    public void AlternarVisaoDeTodosOsTokens()
    {
        if (_visaoTodosOsTokensAtiva)
        {
            DesligarVisaoDeTodosOsTokens();
            return;
        }

        EsconderVisaoDoToken(); // mutuamente exclusivo com o spotlight de um único token
        _visaoTodosOsTokensAtiva = true;
        AtualizarVisaoDeTodosOsTokens();
    }

    private void DesligarVisaoDeTodosOsTokens()
    {
        _visaoTodosOsTokensAtiva = false;
        if (_meshRendererVisaoGeral != null) _meshRendererVisaoGeral.enabled = false;
    }

    // Recalcula a visão combinada de todos os tokens no mapa: um donut por token, todos concatenados
    // no MESMO mesh (mesmo material semi-transparente) — como cada donut só tem geometria FORA da
    // área visível daquele token, uma célula visível por pelo menos um token nunca recebe triângulo de
    // ninguém (fica limpa); uma célula fora da visão de N tokens recebe N camadas semi-transparentes
    // sobrepostas (escurece mais gradualmente perto das bordas de sobreposição) — efeito correto sem
    // precisar de uma união booleana real de polígonos.
    private void AtualizarVisaoDeTodosOsTokens()
    {
        if (!_visaoTodosOsTokensAtiva) return;
        if (mapaRenderer == null || mapaRenderer.sprite == null || _containerTokens == null)
        {
            DesligarVisaoDeTodosOsTokens();
            return;
        }

        var todosVertices = new List<Vector3>();
        var todosTriangulos = new List<int>();
        var todasCores = new List<Color>();
        float distanciaTransicao = ObterDistanciaTransicaoGradiente();

        foreach (Transform filho in _containerTokens)
        {
            CampanhaTokenDraggable token = filho.GetComponent<CampanhaTokenDraggable>();
            if (token == null) continue;

            Vector3 origem = token.transform.position;
            origem.z = 0f;

            float raioMundo = ConverterMetrosParaMundo(token.RaioVisaoMetros);
            List<Vector3> pontos = CalcularPoligonoDeVisao(origem, raioMundo, out List<Vector3> pontosNaBorda);
            if (pontos.Count < 3) continue;

            AdicionarDonutAoBuffer(origem, pontos, pontosNaBorda, distanciaTransicao, todosVertices, todosTriangulos, todasCores);
        }

        // Fontes de luz estática (tocha — ver LightSource.cs) — matematicamente idênticas a um token
        // parado, mesmo buffer/mesmo bloqueio por parede (ver AdicionarFonteDeLuz).
        if (_mapaAtivo?.fontesDeLuz != null)
        {
            foreach (LightSource fonte in _mapaAtivo.fontesDeLuz)
            {
                if (fonte == null) continue;

                Vector3 origem = NormalizadoParaMundo(fonte.posicaoNormalizada);
                origem.z = 0f;

                float raioMundo = ConverterMetrosParaMundo(fonte.raioMetros);
                List<Vector3> pontos = CalcularPoligonoDeVisao(origem, raioMundo, out List<Vector3> pontosNaBorda);
                if (pontos.Count < 3) continue;

                AdicionarDonutAoBuffer(origem, pontos, pontosNaBorda, distanciaTransicao, todosVertices, todosTriangulos, todasCores);
            }
        }

        GarantirObjetosDeVisaoGeral();

        if (todosVertices.Count == 0)
        {
            // Modo continua "ligado" (o botão não deve parecer ter sido desativado sozinho) — só não
            // há nada pra escurecer agora (mapa sem nenhum token, por exemplo).
            _meshRendererVisaoGeral.enabled = false;
            return;
        }

        AplicarBufferAoMesh(_meshFilterVisaoGeral, "MeshVisaoGeralTodosTokens", todosVertices, todosTriangulos, todasCores);
        _meshRendererVisaoGeral.enabled = true;
    }

    private void GarantirObjetosDeVisaoGeral()
    {
        if (_containerVisaoGeral != null) return;

        Transform pai = gridPrincipal != null ? gridPrincipal.transform : transform;

        GameObject goMesh = new GameObject("Visao_Geral_Todos_Tokens");
        goMesh.transform.SetParent(pai, false);
        _containerVisaoGeral = goMesh.transform;

        _meshFilterVisaoGeral = goMesh.AddComponent<MeshFilter>();
        _meshRendererVisaoGeral = goMesh.AddComponent<MeshRenderer>();
        _materialEscuridaoVisaoGeral = new Material(Shader.Find("Sprites/Default"));
        _materialEscuridaoVisaoGeral.color = new Color(0f, 0f, 0f, alphaVisaoGeralTokens);
        _meshRendererVisaoGeral.material = _materialEscuridaoVisaoGeral;
        _meshRendererVisaoGeral.sortingLayerID = mapaRenderer != null ? mapaRenderer.sortingLayerID : 0;
        // Mesma camada "acima de tudo" do spotlight — nunca aparecem ao mesmo tempo (mutuamente
        // exclusivos), então a ordem relativa entre os dois não importa de verdade.
        _meshRendererVisaoGeral.sortingOrder = (mapaRenderer != null ? mapaRenderer.sortingOrder : 0) + 10;
        _meshRendererVisaoGeral.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _meshRendererVisaoGeral.receiveShadows = false;
        _meshRendererVisaoGeral.enabled = false;
    }

    // ── Slider de raio de visão (metros) do token com spotlight ativo ────────────────────────────

    private void OnSliderRaioVisaoAlterado(float metros)
    {
        if (_tokenComVisaoAtiva == null) return;

        _tokenComVisaoAtiva.DefinirRaioVisao(metros);
        AtualizarTexto(textoRaioVisao, "Raio de Visão", metros, "0.0");
        AtualizarVisao(_tokenComVisaoAtiva);
        SalvarEstadoTokensNoMapaAtivo();
    }

    // Sincroniza o slider (valor + interatividade) com o token que tem o spotlight ativo no momento —
    // chamado sempre que esse estado muda (MostrarVisaoDoToken/EsconderVisaoDoToken), pra nunca deixar
    // o slider mexendo no raio de um token que não é mais o exibido.
    private void SincronizarSliderRaioVisao()
    {
        bool ativo = _tokenComVisaoAtiva != null;
        float metros = ativo ? _tokenComVisaoAtiva.RaioVisaoMetros : 0f;

        if (sliderRaioVisao != null)
        {
            sliderRaioVisao.interactable = ativo;
            sliderRaioVisao.SetValueWithoutNotify(ativo ? metros : sliderRaioVisao.minValue);
        }
        if (textoRaioVisao != null)
            textoRaioVisao.text = ativo ? $"Raio de Visão: {metros:0.0}m" : "Raio de Visão: —";
    }
}
