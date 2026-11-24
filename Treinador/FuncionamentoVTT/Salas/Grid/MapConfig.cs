using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NovoMapConfig", menuName = "VTT/Configuracao de Mapa")]
public class MapConfig : ScriptableObject
{
    [Header("Dados do Mapa")]
    public string nomeDoMapa;
    public Sprite imagemMapa;

    [Header("Grid")]
    [Tooltip("Quantas colunas a grade vai dividir o mapa horizontalmente.")]
    public int gridColunas = 20;
    [Tooltip("Quantas linhas a grade vai dividir o mapa verticalmente.")]
    public int gridLinhas = 20;

    [Tooltip("Tamanho em unidades de mundo de cada célula da grade. Em Unity, 1 unidade = 1 metro.")]
    public float unidadesPorCelula = 1.5f;

    [Header("Visual")]
    public float escalaVisual = 1f;

    // Caminho no disco — preenchido apenas para mapas importados em runtime (não ScriptableObjects de projeto)
    [HideInInspector]
    public string imagemPath;

    // ── Campos runtime-only, preenchidos só para mapas importados em sessão (não ScriptableObjects
    // de projeto) — mesmo padrão de imagemPath acima. Ver Salas/SalaDataStorage.cs (MapSaveData). ──

    /// <summary>Id estável do mapa (GUID) — sem isso, tokens/paredes não têm como saber a QUAL mapa
    /// pertencem entre sessões. Vazio nos MapConfig reconstruídos de saves anteriores a esse campo
    /// existir; MapManager.CarregarEstadoVTT gera e persiste um id nesse caso (self-healing).</summary>
    [HideInInspector]
    public string mapaId;

    /// <summary>Tokens posicionados NESTE mapa especificamente — cada MapConfig guarda os seus
    /// próprios, sincronizados com os GameObjects reais só enquanto este for o mapa ativo (ver
    /// MapManager.SalvarTokensNoMapaAtivo/RestaurarTokensDoMapa). Antes dessa separação,
    /// SalaData.tokensNoMapa era uma lista única pra sala inteira — trocar de mapa deixava tokens de
    /// mapas diferentes sobrepostos.</summary>
    [HideInInspector]
    public List<TokenInstanceSaveData> tokensNoMapa = new List<TokenInstanceSaveData>();

    /// <summary>Paredes vetoriais deste mapa (ver Campanhas/Grid/CampanhaMapaData.WallLine) — mesmo
    /// modelo livre de grid usado em Campanhas.</summary>
    [HideInInspector]
    public List<WallLine> paredes = new List<WallLine>();
    [HideInInspector]
    public bool vetorizado;

    /// <summary>Retângulos de fog manual (ver FogRect.cs) — presos a células do grid, não à imagem
    /// do mapa. Desenhados/apagados pela ferramenta de Fog (Salas/Grid/FogEditorTools.cs).</summary>
    [HideInInspector]
    public List<FogRect> fogRects = new List<FogRect>();

    /// <summary>Configuração de iluminação dinâmica deste mapa — ferramenta do Mestre (ver
    /// BarraFerramentasManager, content "Iluminação"). Espelha os campos [SerializeField] de mesmo
    /// nome em MapManager.cs; MapManager.CarregarMapa copia daqui pros campos locais ao trocar de
    /// mapa, e MapManager.DefinirConfigIluminacao grava de volta aqui + propaga pra rede (mesmo canal
    /// de paredes/fogRects — ver MsgParedesPayload). Valores default aqui IDÊNTICOS aos defaults de
    /// MapManager.cs, pra mapas salvos antes desses campos existirem se comportarem como antes.</summary>
    [HideInInspector]
    public float alphaMaximoVisaoDinamica = 0.85f;
    [HideInInspector]
    public float transicaoGradienteEmCelulas = 2f;
    [HideInInspector]
    public float alphaVisaoGeralTokens = 0.6f;
    [HideInInspector]
    public float alphaFogParaHost = 0.5f;
    [HideInInspector]
    public int raiosBaseVisao = 48;

    /// <summary>Filtro de cor de luz sobre a área visível (ver MapManager.corLuzVisao/LuzVisivel) —
    /// Color.white é o sentinel de "sem filtro", tem que continuar sendo o default aqui pra mapas
    /// salvos antes deste campo existir não ganharem um tingimento não pedido.</summary>
    [HideInInspector]
    public Color corLuzVisao = Color.white;

    /// <summary>Fontes de luz estática (tocha — ver LightSource.cs) — posição normalizada (0..1,
    /// relativa à imagem do mapa), não presa a célula de grid. Plantadas/removidas pela ferramenta
    /// de Luz (Salas/Grid/LightEditorTools.cs), participam da visão dinâmica geral e do fog
    /// automático de jogador exatamente como um token parado (ver
    /// MapManager.AtualizarVisaoDeTodosOsTokens/AtualizarVisaoAutomaticaDoJogador).</summary>
    [HideInInspector]
    public List<LightSource> fontesDeLuz = new List<LightSource>();
}
