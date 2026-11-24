using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Régua de medição padrão do grid — ligada pelo botão "Régua normal" dentro do content de
/// BarraFerramentasManager (ver BarraFerramentasManager.AlternarReguaNormal). Clicar no grid define a
/// ORIGEM (sempre o centro do quadrado clicado, nunca a posição bruta do mouse — ver
/// MapManager.AjustarPosicaoTokenAoGrid); a partir daí, a linha e o número (em pés) seguem o mouse a
/// cada frame, também sempre re-snapados ao centro do quadrado mais próximo — as duas pontas ficam
/// sempre alinhadas ao grid, nunca "soltas". Clicar de novo no grid TROCA a origem pro quadrado
/// recém-clicado (permite "andar" a régua sem precisar reabrir o modo); ESC, botão direito ou
/// alternar de novo pelo botão saem do modo.
///
/// Distância em quadrados usa a mesma regra "diagonal = 1 quadrado" (Chebyshev) já usada em
/// SelecaoAlvoManager.DentroDoAlcance — variante simplificada do 5e, não a alternada 1-2-1-2 da regra
/// oficial. 1 quadrado = 5 pés, mesma convenção de AlcanceExtrator/MapManager.MetrosPorQuadrado.
///
/// Mesmo padrão de singleton + LineRenderer orientado por Update() de SelecaoAlvoManager, mas sem
/// alvo/ação pra resolver — só mede, não confirma nada em lugar nenhum. Precisa de um GameObject
/// dedicado na cena de Salas com este componente (mesmo requisito de SelecaoAlvoManager/TutorialManager).
/// </summary>
public class ReguaMedicaoManager : MonoBehaviour
{
    public static ReguaMedicaoManager Instancia { get; private set; }

    [Header("Linha")]
    [SerializeField] private Color corLinha = Color.yellow;
    [SerializeField, Range(0.005f, 0.2f)] private float espessuraLinha = 0.05f;

    [Header("Número (pés) — TextMeshPro 3D, criado em runtime (mesmo padrão de TokenPresencaOverlay)")]
    [SerializeField] private float tamanhoFonte = 3f;
    [SerializeField] private Color corTexto = Color.white;
    [SerializeField] private Vector3 offsetNumero = new Vector3(0f, 0.3f, 0f);

    // Mesma convenção duplicada em AlcanceExtrator.PesPorQuadrado/MapManager.MetrosPorQuadrado —
    // 1 quadrado = 1.5m = 5 pés.
    private const float PesPorQuadrado = 5f;

    private LineRenderer linha;
    private Material materialLinha;
    private TextMeshPro numero;

    private MapManager mapManager;
    private Vector3? origem;

    public bool Ativo { get; private set; }

    private void Awake()
    {
        Instancia = this;
        CriarLinha();
        CriarNumero();
    }

    private void OnDestroy()
    {
        if (Instancia == this) Instancia = null;
        if (materialLinha != null) Destroy(materialLinha);
    }

    public void Alternar()
    {
        if (Ativo) Desativar();
        else Ativar();
    }

    public void Ativar()
    {
        Ativo = true;
        origem = null;
        EsconderVisual();
    }

    /// <summary>Chamado tanto pelo botão (alternar de novo) quanto por
    /// BarraFerramentasManager.ToggleContentFerramentaMapa quando o content da régua fecha (trocar de
    /// ferramenta ou fechar de vez) — sem isso a linha ficaria seguindo o mouse sem nenhum botão
    /// visível pra desligar.</summary>
    public void Desativar()
    {
        Ativo = false;
        origem = null;
        EsconderVisual();
    }

    private void EsconderVisual()
    {
        if (linha != null) linha.enabled = false;
        if (numero != null) numero.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!Ativo) return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Desativar();
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.rightButton.wasPressedThisFrame)
        {
            Desativar();
            return;
        }

        MapManager mm = ObterMapManager();
        Camera cam = mm != null ? mm.CameraAtiva : Camera.main;
        if (mm == null || cam == null) return;

        Vector2 posicaoTela = mouse.position.ReadValue();
        Vector3 mundo = mm.CamParaMundoToken(cam, posicaoTela);
        Vector3 celulaMouse = mm.AjustarPosicaoTokenAoGrid(mundo);

        bool cliqueValido = mouse.leftButton.wasPressedThisFrame
            && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject());
        if (cliqueValido)
            origem = celulaMouse;

        if (!origem.HasValue)
        {
            EsconderVisual();
            return;
        }

        Vector3 origemVal = origem.Value;
        if (linha != null)
        {
            linha.enabled = true;
            linha.SetPosition(0, origemVal);
            linha.SetPosition(1, celulaMouse);
        }

        AtualizarNumero(mm, origemVal, celulaMouse);
    }

    private void AtualizarNumero(MapManager mm, Vector3 pontoOrigem, Vector3 pontoDestino)
    {
        if (numero == null) return;

        float tamanhoCelula = mm.ObterTamanhoAtualDaCelulaEmMetros();
        if (tamanhoCelula <= 0f)
        {
            numero.gameObject.SetActive(false);
            return;
        }

        // origem/destino já vêm snapados ao mesmo grid — a diferença é sempre um múltiplo exato de
        // tamanhoCelula, então o Round aqui não perde precisão (mesma fórmula de
        // SelecaoAlvoManager.DentroDoAlcance).
        int dx = Mathf.RoundToInt((pontoDestino.x - pontoOrigem.x) / tamanhoCelula);
        int dy = Mathf.RoundToInt((pontoDestino.y - pontoOrigem.y) / tamanhoCelula);
        int quadrados = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
        int pes = Mathf.RoundToInt(quadrados * PesPorQuadrado);

        numero.text = $"{pes} pés";
        numero.gameObject.SetActive(quadrados > 0);
        numero.transform.position = ((pontoOrigem + pontoDestino) / 2f) + offsetNumero;
    }

    private void CriarLinha()
    {
        var go = new GameObject("ReguaMedicao_Linha");
        go.transform.SetParent(transform, false);

        linha = go.AddComponent<LineRenderer>();
        linha.useWorldSpace = true;
        linha.positionCount = 2;
        materialLinha = new Material(Shader.Find("Sprites/Default"));
        linha.material = materialLinha;
        linha.startColor = corLinha;
        linha.endColor = corLinha;
        linha.startWidth = espessuraLinha;
        linha.endWidth = espessuraLinha;
        linha.numCapVertices = 4;
        linha.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        linha.receiveShadows = false;
        linha.generateLightingData = false;
        linha.sortingOrder = 1000; // mesmo valor de SelecaoAlvoManager — sempre por cima de mapa/grid/tokens/paredes
        linha.enabled = false;
    }

    private void CriarNumero()
    {
        var go = new GameObject("ReguaMedicao_Numero");
        go.transform.SetParent(transform, false);

        numero = go.AddComponent<TextMeshPro>();
        numero.fontSize = tamanhoFonte;
        numero.color = corTexto;
        numero.alignment = TextAlignmentOptions.Center;

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sortingOrder = 1001; // acima da própria linha

        go.SetActive(false);
    }

    private MapManager ObterMapManager()
    {
        if (mapManager == null) mapManager = FindFirstObjectByType<MapManager>();
        return mapManager;
    }
}
