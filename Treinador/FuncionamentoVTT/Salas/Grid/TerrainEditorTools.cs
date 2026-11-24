using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Tool Manager de pintura de terreno (ver Campanhas/Grid/TerrainManager.cs) sobre o mapa ativo.
/// Mesmo padrão de FogEditorTools/LightEditorTools/WallEditorTools — referência DIRETA, não
/// singleton, porque um TerrainManager também pode existir em Campanhas na mesma sessão do editor;
/// cada cena arrasta sua própria instância deste tool.
///
/// Diferente do fog manual (retângulo clique-e-arrasta): aqui é um PINCEL — segurar o botão esquerdo
/// e arrastar pinta CADA célula sob o cursor com o tipo selecionado, uma vez por célula nova visitada
/// nesta passada (TerrainManager.PintarTerreno é idempotente, mas evitar chamadas repetidas na MESMA
/// célula todo frame economiza trabalho à toa). Selecionar TerrainType.Nenhum funciona como
/// "borracha" — PintarTerreno já redireciona esse caso pra ApagarTerreno internamente, sem precisar
/// de um modo separado aqui.
///
/// Não usa IGerenciadorDeParedes: TerrainManager é auto-contido, dono do próprio Tilemap, converte
/// mundo->célula sozinho (TerrainManager.MundoParaCelula) — não precisa de nenhuma coordenada do
/// mapManager de Salas/Campanhas. Só precisa de uma Camera pra converter posição de tela em mundo
/// (mesmo fallback Camera.main de FogEditorTools/WallEditorTools).
/// </summary>
public class TerrainEditorTools : MonoBehaviour
{
    [Tooltip("TerrainManager desta cena (Salas ou Campanhas) — referência DIRETA, não singleton, " +
        "mesmo raciocínio de FogEditorTools/LightEditorTools (um singleton estático destruiria um " +
        "dos dois se ambos existissem ao mesmo tempo).")]
    [SerializeField] private TerrainManager terrainManager;

    [Tooltip("Câmera usada pra converter clique de tela em posição de mundo — deixe vazio pra cair " +
        "em Camera.main automaticamente (ver Awake).")]
    [SerializeField] private Camera cameraAtiva;

    // null = ferramenta desligada; Nenhum/Lava/Agua = pincel ativo pintando esse tipo (Nenhum apaga).
    public TerrainManager.TerrainType? FerramentaAtiva { get; private set; }

    private Vector3Int? _ultimaCelulaPintada;

    private void Awake()
    {
        if (cameraAtiva == null) cameraAtiva = Camera.main;
    }

    // ── API pública (ligar aos botões de ferramenta pelo Inspector/BarraFerramentasManager) ────

    /// <summary>Sobrecarga em int pra poder ser chamada direto por um Button.onClick no Inspector
    /// (UnityEvent não expõe enums customizados na lista de parâmetros persistentes) — mesmo
    /// raciocínio de FogEditorTools.SelecionarFerramenta(int). Ordem bate com o enum TerrainType:
    /// 0 = Nenhum (borracha), 1 = Lava, 2 = Agua.</summary>
    public void SelecionarFerramenta(int tipo) => SelecionarFerramenta((TerrainManager.TerrainType)tipo);

    public void SelecionarFerramenta(TerrainManager.TerrainType tipo)
    {
        // Clicar de novo no tipo já ativo desliga a ferramenta — mesmo toggle de
        // FogEditorTools/WallEditorTools.SelecionarFerramenta.
        FerramentaAtiva = FerramentaAtiva == tipo ? (TerrainManager.TerrainType?)null : tipo;
        _ultimaCelulaPintada = null;
    }

    /// <summary>Desliga a ferramenta — chamado ao fechar o content da barra de ferramentas (mesmo
    /// padrão de FogEditorTools.Desativar/LightEditorTools.Desativar/ReguaMedicaoManager.Desativar).</summary>
    public void Desativar() => FerramentaAtiva = null;

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!FerramentaAtiva.HasValue || terrainManager == null || cameraAtiva == null) return;

        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        if (!mouse.leftButton.isPressed)
        {
            _ultimaCelulaPintada = null;
            return;
        }

        // Só pinta se o clique não caiu sobre UI (botão/painel) — mesmo cuidado de
        // FogEditorTools/WallEditorTools.
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        Vector2 tela2D = mouse.position.ReadValue();
        Vector3 tela = new Vector3(tela2D.x, tela2D.y, Mathf.Abs(cameraAtiva.transform.position.z));
        Vector3 mundo = cameraAtiva.ScreenToWorldPoint(tela);
        mundo.z = 0f;

        Vector3Int celula = terrainManager.MundoParaCelula(mundo);
        if (_ultimaCelulaPintada.HasValue && _ultimaCelulaPintada.Value == celula) return; // já pintada nesta passada do pincel

        terrainManager.PintarTerreno(celula, FerramentaAtiva.Value);
        _ultimaCelulaPintada = celula;
    }
}
