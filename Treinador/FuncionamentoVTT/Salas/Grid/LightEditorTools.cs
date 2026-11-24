using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Ferramenta ativa no editor de fontes de luz — None desliga qualquer interação de mouse.</summary>
public enum LightToolType
{
    None,
    Adicionar,
    Remover,
}

/// <summary>
/// Tool Manager de fontes de luz estática (LightSource — ver Salas/Grid/LightSource.cs) sobre o
/// mapa ativo de um IGerenciadorDeParedes — CampanhaMapManager (offline) ou Salas/Grid/MapManager
/// (online). Mesmo padrão de Salas/Grid/FogEditorTools.cs (mapManagerBehaviour + cast pra interface
/// no Awake), mas com diferenças de interação:
///
/// - Adicionar é CLIQUE ÚNICO (não clique-e-arrasta como o retângulo de Fog) — mais parecido com
///   Campanhas/Grid/WallEditorTools.cs: um círculo de prévia segue o mouse mostrando o raio ATUAL
///   do slider (pedido explícito do usuário — "o UI para colocar o ponto vai ter um slider que vai
///   definir o tamanho da luz"), clicar planta a luz com esse raio.
/// - Este arquivo também é dono do slider de raio (sliderRaioLuz) — diferente de FogEditorTools,
///   que não precisa de nenhum parâmetro numérico pra desenhar. Fica self-contained aqui (mesmo
///   raciocínio de FogEditorTools já ser dono de corPreview/espessuraPreview), então
///   BarraFerramentasManager/CampanhasManager só precisam ligar 2 botões (Adicionar/Remover), não o
///   slider — ver SelecionarFerramenta.
/// - Luzes JÁ plantadas ganham DOIS elementos visuais: um marcador de UI clicável pequeno
///   (prefabMarcadorLuz — Image+Button, ver LightMarcadorUI, redimensionado em runtime pra
///   fatorReducaoMarcador vezes menor que uma célula do grid — pedido explícito do usuário) que
///   revela um Slider ao clicar, pra reajustar o raio DESSA luz sem apagar e replantar; e um CÍRCULO
///   (LineRenderer, mesma técnica da prévia de posicionamento) no raio de alcance REAL da luz, que
///   acompanha o slider ao vivo — sem ele não dava pra ver o tamanho de uma luz já plantada sem abrir
///   o painel dela (pedido explícito do usuário).
///   A visibilidade desses dois elementos é independente da ferramenta Adicionar/Remover estar ativa
///   — ver AlternarVisibilidadeMarcadores/MarcadoresVisiveis, ligado a um botão dedicado em
///   BarraFerramentasManager/CampanhasManager (mestre-only, mesma visibilidade de qualquer outra
///   ferramenta de edição de mapa).
///
/// Não mexe no dado de luz diretamente — só chama a API pública de IGerenciadorDeParedes
/// (AdicionarFonteDeLuz/RemoverFonteDeLuzEm/ObterFontesDeLuzEmMundo/Definir·/PersistirRaioDaFonteDeLuz/
/// MundoParaNormalizado/CelulaParaBoundsMundo), que segue sendo o único dono do dado e da
/// reconstrução visual/vision.
/// </summary>
public class LightEditorTools : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("Precisa ser um MonoBehaviour que implemente IGerenciadorDeParedes — CampanhaMapManager " +
        "(Campanhas) ou MapManager (Salas).")]
    [SerializeField] private MonoBehaviour mapManagerBehaviour;

    private IGerenciadorDeParedes mapManager;

    [Header("Marcador das luzes já plantadas")]
    [Tooltip("Prefab do marcador — precisa ter LightMarcadorUI (Image+Button que revela um Slider ao " +
        "clicar, pra reajustar o raio dessa luz). Instanciado uma vez por LightSource sempre que os " +
        "marcadores estiverem visíveis (ver MarcadoresVisiveis).")]
    [SerializeField] private GameObject prefabMarcadorLuz;
    [Tooltip("O ícone do marcador é redimensionado em runtime pra ficar sempre proporcional à célula " +
        "do grid ATUAL (independente do tamanho autorado no prefab, que pode ficar errado conforme o " +
        "mapa/zoom mudam) — este é o DIVISOR: 4 = marcador com 1/4 do tamanho de uma célula, mínimo " +
        "pedido pelo usuário ('ao menos 4x menor que o quadrado do grid').")]
    [SerializeField, Min(1f)] private float fatorReducaoMarcador = 4f;
    [Tooltip("sortingOrder do Canvas de cada marcador — bem alto de propósito, pra garantir que " +
        "renderize por cima de mapa/grid/fog/escuridão (mesmo raciocínio do sortingOrder da prévia de " +
        "posicionamento abaixo). Ver LightMarcadorUI.Configurar.")]
    [SerializeField] private int sortingOrderMarcador = 1001;
    [Tooltip("Cor do círculo mostrando o raio de alcance REAL de cada luz já plantada — acompanha o " +
        "slider do marcador ao vivo, enquanto arrastado.")]
    [SerializeField] private Color corCirculoRaioExistente = new Color(1f, 0.6f, 0f, 0.6f);

    [Header("Raio da Próxima Luz (metros)")]
    [Tooltip("Define o raio (em metros) da PRÓXIMA luz a ser plantada — min/max configurados no " +
        "Awake (mesmo range de sliderRaioVisao de token: 1.5m a 60m). Muda o raio da prévia " +
        "circular ao vivo, enquanto arrastado. Mesmo range é reaproveitado pelo slider de CADA " +
        "LightMarcadorUI, pra editar uma luz já plantada.")]
    [SerializeField] private Slider sliderRaioLuz;
    [SerializeField] private TMP_Text textoRaioLuz;

    [Header("Prévia de posicionamento (círculo gerado em código — nenhum prefab necessário)")]
    [SerializeField] private Color corPreviewPosicionamento = Color.yellow;
    [SerializeField, Range(0.005f, 0.2f)] private float espessuraLinha = 0.05f;
    [SerializeField, Range(8, 64)] private int segmentosCirculo = 32;

    // ── Estado ────────────────────────────────────────────────────────────────

    public LightToolType FerramentaAtiva { get; private set; } = LightToolType.None;

    // Visibilidade dos marcadores das luzes JÁ plantadas — INDEPENDENTE de FerramentaAtiva (ver
    // DeveMostrarMarcadores): liga automaticamente enquanto Adicionar/Remover estiver ativo (pra
    // evitar duplicata/mirar remoção), mas também pode ficar ligada standalone via
    // AlternarVisibilidadeMarcadores, chamado por um botão dedicado (mestre-only) em
    // BarraFerramentasManager/CampanhasManager — útil só pra CONFERIR onde as tochas estão, sem abrir
    // o modo de edição.
    public bool MarcadoresVisiveis => FerramentaAtiva != LightToolType.None || _marcadoresVisiveisManualmente;
    private bool _marcadoresVisiveisManualmente;

    // ~4 quadrados — tocha comum. 1 quadrado = 1.5m é a mesma convenção fixa de mesa usada em
    // MapManager/CampanhaMapManager (duplicada aqui de propósito, mesmo padrão de MetrosPorQuadrado
    // já ser duplicado independentemente nos dois managers, em vez de exposta na interface).
    private float raioAtualMetros = 6f;
    private const float MetrosPorQuadrado = 1.5f;
    private const float RaioMinimoMetros = MetrosPorQuadrado;
    private const float RaioMaximoMetros = MetrosPorQuadrado * 40f; // 40 quadrados — generoso o bastante pra mapas grandes

    private LineRenderer _previewPosicionamento;
    private Material _materialPreviewPosicionamento;
    private Material _materialCirculoRaioExistente;
    private Camera _camera;

    // Um marcador clicável + um círculo mostrando o raio real, por LightSource — mantidos juntos pra
    // AtualizarCirculoDoMarcador achar o círculo certo quando o slider de UM marcador muda ao vivo.
    private class MarcadorAtivo
    {
        public LightMarcadorUI Marcador;
        public LineRenderer CirculoRaio;
    }
    private readonly List<MarcadorAtivo> _marcadoresAtivos = new List<MarcadorAtivo>();

    // transform (deste LightEditorTools) NÃO é um objeto dedicado pequeno — na cena de Campanhas ele
    // é literalmente a tela raiz "TelaCampanhas" (confirmado via log, ver VTTLog.ativoBoard). O
    // marcador (prefabMarcadorLuz) TEM RectTransform, diferente do círculo de prévia (LineRenderer
    // puro, sem RectTransform) — se "TelaCampanhas" tiver qualquer LayoutGroup/ContentSizeFitter
    // próprio, adicionar um RectTransform filho novo faz ela recalcular o próprio layout em volta
    // dele, o que bate com o sintoma relatado ("tudo em volta some, fica só o grid"). Os marcadores
    // passam a nascer dentro deste container isolado (Transform puro, sem RectTransform) em vez de
    // direto em transform — um LayoutGroup só itera FILHOS DIRETOS com RectTransform, então nem o
    // container nem (por extensão) os marcadores dentro dele são varridos por ele.
    private Transform _containerMarcadores;

    private Transform ObterOuCriarContainerMarcadores()
    {
        if (_containerMarcadores != null) return _containerMarcadores;
        var go = new GameObject("LightEditor_Marcadores");
        go.transform.SetParent(transform, false);
        _containerMarcadores = go.transform;

        // TEMP debug (ver VTTLog.ativoBoard) — lista todo componente ancestral (a partir de
        // transform, subindo) que poderia explicar a tela reagindo a um RectTransform novo:
        // LayoutGroup/ContentSizeFitter (rearranjam layout), CanvasGroup (bloqueiam/escondem) e
        // Canvas (mudam render mode/sorting no meio do caminho).
        var linhas = new List<string>();
        for (Transform t = transform; t != null; t = t.parent)
        {
            var partes = new List<string>();
            foreach (var lg in t.GetComponents<LayoutGroup>()) partes.Add($"LayoutGroup:{lg.GetType().Name}");
            foreach (var csf in t.GetComponents<ContentSizeFitter>()) partes.Add("ContentSizeFitter");
            foreach (var cg in t.GetComponents<CanvasGroup>()) partes.Add($"CanvasGroup(interactable={cg.interactable},blocksRaycasts={cg.blocksRaycasts},alpha={cg.alpha})");
            foreach (var cv in t.GetComponents<Canvas>()) partes.Add($"Canvas(renderMode={cv.renderMode},sortingOrder={cv.sortingOrder},enabled={cv.enabled})");
            linhas.Add($"'{t.name}'" + (partes.Count > 0 ? $" [{string.Join(", ", partes)}]" : ""));
        }
        VTTLog.Board($"[LightEditorTools] Cadeia de ancestrais de '{transform.name}' (de baixo pra cima): {string.Join(" <- ", linhas)}");

        return _containerMarcadores;
    }

    private void Awake()
    {
        mapManager = mapManagerBehaviour as IGerenciadorDeParedes;
        if (mapManager == null && mapManagerBehaviour != null)
            Debug.LogWarning("[LightEditorTools] mapManagerBehaviour não implementa IGerenciadorDeParedes.");

        _camera = mapManager != null ? mapManager.CameraAtiva : null;
        if (_camera == null) _camera = Camera.main;

        // TEMP debug (ver VTTLog.ativoBoard) — investigando marcador de luz sumindo/cobrindo a tela.
        VTTLog.Board($"[LightEditorTools] Awake '{gameObject.name}': _camera=" +
            $"{(_camera != null ? $"{_camera.name} (depth={_camera.depth}, ortho={_camera.orthographic}, culling={_camera.cullingMask})" : "NULL")}, " +
            $"transform.parent={(transform.parent != null ? transform.parent.name : "null")}");

        if (sliderRaioLuz != null)
        {
            sliderRaioLuz.minValue = RaioMinimoMetros;
            sliderRaioLuz.maxValue = RaioMaximoMetros;
            sliderRaioLuz.wholeNumbers = false;
            sliderRaioLuz.value = raioAtualMetros;
            sliderRaioLuz.onValueChanged.AddListener(DefinirRaioAtualMetros);
        }
        AtualizarTextoRaio();

        CriarPreviewPosicionamento();
    }

    private void OnDestroy()
    {
        if (_materialPreviewPosicionamento != null) Destroy(_materialPreviewPosicionamento);
        if (_materialCirculoRaioExistente != null) Destroy(_materialCirculoRaioExistente);
    }

    // ── API pública (ligar aos botões de ferramenta pelo Inspector) ─────────

    /// <summary>Sobrecarga em int pra poder ser chamada direto por um Button.onClick no Inspector
    /// (UnityEvent não expõe enums customizados na lista de parâmetros persistentes). 0 = None,
    /// 1 = Adicionar, 2 = Remover.</summary>
    public void SelecionarFerramenta(int tipo) => SelecionarFerramenta((LightToolType)tipo);

    public void SelecionarFerramenta(LightToolType tipo)
    {
        // Clicar de novo no botão da ferramenta já ativa desliga (volta pra None) — mesmo toggle de
        // FogEditorTools.SelecionarFerramenta.
        FerramentaAtiva = FerramentaAtiva == tipo ? LightToolType.None : tipo;

        if (FerramentaAtiva == LightToolType.None && _previewPosicionamento != null)
            _previewPosicionamento.enabled = false;

        AtualizarMarcadoresExistentes();
    }

    /// <summary>Desliga a ferramenta (volta pra None) — chamado ao fechar o content da barra de
    /// ferramentas (mesmo padrão de FogEditorTools.Desativar). Não mexe em MarcadoresVisiveis
    /// manual — se o Mestre tinha ligado a visibilidade standalone, ela continua ligada.</summary>
    public void Desativar() => SelecionarFerramenta(LightToolType.None);

    /// <summary>Liga/desliga a visibilidade dos marcadores das luzes já plantadas, independente de
    /// qualquer ferramenta Adicionar/Remover estar ativa — botão dedicado (mestre-only) em
    /// BarraFerramentasManager/CampanhasManager, útil só pra conferir onde as tochas estão.</summary>
    public void AlternarVisibilidadeMarcadores()
    {
        _marcadoresVisiveisManualmente = !_marcadoresVisiveisManualmente;
        AtualizarMarcadoresExistentes();
    }

    private void DefinirRaioAtualMetros(float metros)
    {
        raioAtualMetros = metros;
        AtualizarTextoRaio();
    }

    private void AtualizarTextoRaio()
    {
        if (textoRaioLuz != null) textoRaioLuz.text = $"Raio da Luz: {raioAtualMetros:0.0}m";
    }

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (FerramentaAtiva == LightToolType.None || mapManager == null || _camera == null) return;

        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        bool sobreUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        Vector3 mundo = ObterPosicaoMouseNoMundo(mouse);

        switch (FerramentaAtiva)
        {
            case LightToolType.Adicionar:
                AtualizarFerramentaAdicionar(mouse, mundo, sobreUI);
                break;
            case LightToolType.Remover:
                if (_previewPosicionamento != null) _previewPosicionamento.enabled = false;
                AtualizarFerramentaRemover(mouse, sobreUI, mundo);
                break;
        }
    }

    private Vector3 ObterPosicaoMouseNoMundo(Mouse mouse)
    {
        Vector2 tela2D = mouse.position.ReadValue();
        Vector3 tela = new Vector3(tela2D.x, tela2D.y, Mathf.Abs(_camera.transform.position.z));
        Vector3 mundo = _camera.ScreenToWorldPoint(tela);
        mundo.z = 0f;
        return mundo;
    }

    // ── Ferramenta Adicionar (clique único, ponto-a-ponto — não arrasta) ────────

    private void AtualizarFerramentaAdicionar(Mouse mouse, Vector3 mundo, bool sobreUI)
    {
        float raioMundo = ConverterMetrosParaMundoLocal(raioAtualMetros);
        DesenharCirculo(_previewPosicionamento, mundo, raioMundo, segmentosCirculo);
        _previewPosicionamento.enabled = true;

        if (!mouse.leftButton.wasPressedThisFrame || sobreUI) return;

        var fonte = new LightSource
        {
            id = Guid.NewGuid().ToString("N"),
            posicaoNormalizada = mapManager.MundoParaNormalizado(mundo),
            raioMetros = raioAtualMetros,
        };
        mapManager.AdicionarFonteDeLuz(fonte);
        AtualizarMarcadoresExistentes();
    }

    // ── Ferramenta Remover (clique único) ─────────────────────────────────────
    //
    // Clicar exatamente EM CIMA de um marcador (LightMarcadorUI, um Button de verdade) abre/fecha o
    // painel de slider dele em vez de remover — o clique é interceptado pela UI antes de chegar aqui
    // (sobreUI=true). Clicar perto (mas fora do botão) remove normalmente. Aceito: o Mestre só
    // precisa clicar um pouco ao lado do ícone, não é preciso resolver essa ambiguidade.

    private void AtualizarFerramentaRemover(Mouse mouse, bool sobreUI, Vector3 mundo)
    {
        if (!mouse.leftButton.wasPressedThisFrame || sobreUI) return;

        if (mapManager.RemoverFonteDeLuzEm(mundo))
            AtualizarMarcadoresExistentes();
    }

    // ── Marcadores das luzes já plantadas (ícone clicável + círculo de raio) ─────────────────────

    private void AtualizarMarcadoresExistentes()
    {
        LimparMarcadoresExistentes();
        if (!MarcadoresVisiveis || mapManager == null) return;

        float tamanhoMarcadorMundo = ObterTamanhoCelulaMundo() / fatorReducaoMarcador;

        // TEMP debug (ver VTTLog.ativoBoard) — investigando marcador de luz sumindo/cobrindo a tela.
        VTTLog.Board($"[LightEditorTools] AtualizarMarcadoresExistentes: tamanhoCelulaMundo=" +
            $"{ObterTamanhoCelulaMundo():0.###}, fatorReducaoMarcador={fatorReducaoMarcador}, " +
            $"tamanhoMarcadorMundo={tamanhoMarcadorMundo:0.###}, qtdFontes={mapManager.ObterFontesDeLuzEmMundo().Count()}");

        foreach (var fonte in mapManager.ObterFontesDeLuzEmMundo())
        {
            var ativo = new MarcadorAtivo
            {
                Marcador = CriarMarcadorClicavel(fonte.id, fonte.posicaoMundo, fonte.raioMetros, tamanhoMarcadorMundo),
                CirculoRaio = CriarCirculoRaioExistente(),
            };
            DesenharCirculo(ativo.CirculoRaio, fonte.posicaoMundo, fonte.raioMundo, segmentosCirculo);
            ativo.CirculoRaio.enabled = true;

            _marcadoresAtivos.Add(ativo);
        }
    }

    private LightMarcadorUI CriarMarcadorClicavel(string id, Vector3 posicaoMundo, float raioMetrosAtual, float tamanhoMundo)
    {
        if (prefabMarcadorLuz == null) return null;

        GameObject go = UIUtils.Spawn(prefabMarcadorLuz, ObterOuCriarContainerMarcadores(), false);
        go.transform.position = posicaoMundo;
        RedimensionarMarcador(go, tamanhoMundo);

        LightMarcadorUI marcador = go.GetComponent<LightMarcadorUI>();
        if (marcador == null)
        {
            Debug.LogWarning("[LightEditorTools] prefabMarcadorLuz não possui LightMarcadorUI.");
            Destroy(go);
            return null;
        }

        marcador.Configurar(id, raioMetrosAtual, RaioMinimoMetros, RaioMaximoMetros, _camera, sortingOrderMarcador,
            aoAlterarRaioAoVivo: (fonteId, metros) =>
            {
                mapManager.DefinirRaioDaFonteDeLuz(fonteId, metros);
                AtualizarCirculoDoMarcador(fonteId, posicaoMundo, metros);
            },
            aoConfirmarRaio: (fonteId, metros) => mapManager.PersistirRaioDaFonteDeLuz(fonteId, metros));
        return marcador;
    }

    private void AtualizarCirculoDoMarcador(string id, Vector3 centro, float raioMetros)
    {
        MarcadorAtivo ativo = _marcadoresAtivos.Find(m => m.Marcador != null && m.Marcador.Id == id);
        if (ativo?.CirculoRaio == null) return;
        DesenharCirculo(ativo.CirculoRaio, centro, ConverterMetrosParaMundoLocal(raioMetros), segmentosCirculo);
    }

    private void LimparMarcadoresExistentes()
    {
        foreach (MarcadorAtivo ativo in _marcadoresAtivos)
        {
            if (ativo.Marcador != null) Destroy(ativo.Marcador.gameObject);
            if (ativo.CirculoRaio != null) Destroy(ativo.CirculoRaio.gameObject);
        }
        _marcadoresAtivos.Clear();
    }

    // Redimensiona o marcador (ícone clicável) pra ficar do tamanho de mundo pedido, independente do
    // tamanho autorado no prefab — mede a maior dimensão do RectTransform raiz e escala o
    // GameObject inteiro proporcionalmente.
    private static void RedimensionarMarcador(GameObject go, float tamanhoAlvoMundo)
    {
        RectTransform rt = go.GetComponent<RectTransform>();
        if (rt == null) return;

        // O prefab tem ContentSizeFitter (raiz) + LayoutElement (Slider) — o tamanho real de rt.rect
        // só existe DEPOIS de um passo de layout, que normalmente só roda no fim do frame. Medir
        // logo após Instantiate (como abaixo) pega rt.rect ainda no valor salvo no prefab (sizeDelta
        // 0x0 aqui), caindo no early-return e deixando localScale no padrão (1,1,1) — ou seja, SEM
        // reduzir nada. Como o marcador ganhou seu próprio Canvas (ver LightMarcadorUI.Awake), sem
        // nenhum pai compensando a escala, isso fazia o painel (autorado ~160x20) renderizar a
        // 160x20 unidades de MUNDO — gigante o bastante pra cobrir o mapa inteiro. Forçar o rebuild
        // aqui garante medir o tamanho final de verdade antes de calcular a escala.
        Vector2 tamanhoAntesDoRebuild = rt.rect.size;
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

        float maiorDimensao = Mathf.Max(rt.rect.width, rt.rect.height);

        // TEMP debug (ver VTTLog.ativoBoard) — investigando marcador de luz sumindo/cobrindo a tela.
        VTTLog.Board($"[LightEditorTools] RedimensionarMarcador '{go.name}': tamanhoAlvoMundo=" +
            $"{tamanhoAlvoMundo:0.###}, rectAntes={tamanhoAntesDoRebuild}, rectDepoisDoRebuild={rt.rect.size}, " +
            $"maiorDimensao={maiorDimensao:0.###}");

        if (maiorDimensao <= 0.0001f)
        {
            VTTLog.Board($"[LightEditorTools] RedimensionarMarcador '{go.name}': maiorDimensao inválida " +
                "— saindo SEM escalar, localScale fica no valor padrão do prefab (provável causa do painel gigante).");
            return;
        }

        float escala = tamanhoAlvoMundo / maiorDimensao;
        go.transform.localScale = new Vector3(escala, escala, 1f);

        VTTLog.Board($"[LightEditorTools] RedimensionarMarcador '{go.name}': escala calculada={escala:0.#####}, " +
            $"localScale final={go.transform.localScale}");
    }

    // Mede o tamanho (em unidades de mundo) de UMA célula do grid atual via CelulaParaBoundsMundo (já
    // exposto na interface, sem precisar de ConverterMetrosParaMundo público).
    private float ObterTamanhoCelulaMundo()
    {
        Bounds b0 = mapManager.CelulaParaBoundsMundo(0, 0);
        Bounds b1 = mapManager.CelulaParaBoundsMundo(1, 0);
        float tamanho = Vector3.Distance(b0.center, b1.center);
        return tamanho <= 0.0001f ? 1.5f : tamanho;
    }

    // Converte metros (unidade sempre usada nos sliders — ver Header) pra unidades de mundo do mapa
    // ATUAL, aplicando a mesma convenção fixa "1 quadrado = 1.5m" usada em MapManager/CampanhaMapManager.
    private float ConverterMetrosParaMundoLocal(float metros) => (metros / MetrosPorQuadrado) * ObterTamanhoCelulaMundo();

    // ── Geometria (círculos gerados em código — nenhum prefab necessário) ────────

    private void CriarPreviewPosicionamento()
    {
        var go = new GameObject("LightEditor_PreviewPosicionamento");
        go.transform.SetParent(transform, false);

        _previewPosicionamento = go.AddComponent<LineRenderer>();
        _materialPreviewPosicionamento = new Material(Shader.Find("Sprites/Default"));
        ConfigurarLineRendererCirculo(_previewPosicionamento, _materialPreviewPosicionamento, corPreviewPosicionamento, 1000);
        _previewPosicionamento.enabled = false;
    }

    private LineRenderer CriarCirculoRaioExistente()
    {
        if (_materialCirculoRaioExistente == null) _materialCirculoRaioExistente = new Material(Shader.Find("Sprites/Default"));

        var go = new GameObject("LightEditor_CirculoRaio");
        go.transform.SetParent(transform, false);

        LineRenderer lr = go.AddComponent<LineRenderer>();
        // sortingOrderMarcador - 1: logo ABAIXO do ícone clicável (que precisa vencer o raycast pro
        // clique funcionar), mas ainda acima de mapa/grid/fog.
        ConfigurarLineRendererCirculo(lr, _materialCirculoRaioExistente, corCirculoRaioExistente, sortingOrderMarcador - 1);
        return lr;
    }

    private void ConfigurarLineRendererCirculo(LineRenderer lr, Material material, Color cor, int sortingOrder)
    {
        lr.useWorldSpace = true;
        lr.loop = true; // fecha o círculo sozinho — N posições bastam, sem repetir a primeira
        lr.positionCount = segmentosCirculo;
        lr.material = material;
        lr.startColor = cor;
        lr.endColor = cor;
        lr.startWidth = espessuraLinha;
        lr.endWidth = espessuraLinha;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.generateLightingData = false;
        lr.sortingOrder = sortingOrder;
    }

    private static void DesenharCirculo(LineRenderer lr, Vector3 centro, float raio, int segmentos)
    {
        if (lr.positionCount != segmentos) lr.positionCount = segmentos;
        for (int i = 0; i < segmentos; i++)
        {
            float ang = (2f * Mathf.PI * i) / segmentos;
            lr.SetPosition(i, centro + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * raio);
        }
    }
}
