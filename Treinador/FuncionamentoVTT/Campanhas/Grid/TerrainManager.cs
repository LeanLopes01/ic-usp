using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Terreno tático estilo Draw Steel Codex — overlay colorido (Tilemap) só pro Mestre + VFX (ex:
/// calor da lava) sempre visível pra todos. Fica em Campanhas/Grid por convenção de pasta, mas é uma
/// "referência direta" compartilhada por Salas E Campanhas (mesmo raciocínio de não-singleton já
/// usado em WallEditorTools/FogEditorTools/LightEditorTools — cada cena arrasta sua PRÓPRIA instância
/// no Inspector; um singleton estático destruiria uma das duas se ambas existissem ao mesmo tempo).
/// Não depende de MapManager/CampanhaMapManager — é auto-contido: dono do próprio Tilemap, sabe
/// converter mundo↔célula sozinho (via Tilemap.WorldToCell/GetCellCenterWorld) e não precisa de
/// coordenadas de fora. Quem chama (BarraFerramentasManager em Salas, CampanhasManager em Campanhas)
/// só converte clique-de-mouse → posição de mundo e chama PintarTerreno/ApagarTerreno.
///
/// Dois sistemas independentes sobre a MESMA célula:
/// - Dado lógico (_dadosTerreno): sempre existe, pra regras de jogo (custo de movimento, dano etc) —
///   funciona igual pro Mestre e pro Jogador, INDEPENDENTE do overlay estar visível.
/// - Overlay visual (tilemapOverlay): só o Mestre vê — uma ÚNICA tile branca, colorida por célula via
///   Tilemap.SetColor (não precisa de uma Tile por cor — mais leve que um SpriteRenderer por célula).
/// - VFX (_vfxAtivos): GameObjects de verdade instanciados por cima do tile, num container À PARTE do
///   Tilemap — nunca são afetados por SetRole, então continuam visíveis pro jogador mesmo com o
///   overlay tático desligado.
/// </summary>
public class TerrainManager : MonoBehaviour
{
    public enum TerrainType
    {
        Nenhum,
        Lava,
        Agua,
        // adicionar novos tipos aqui conforme necessário
    }

    [Serializable]
    private class ConfiguracaoVisual
    {
        public TerrainType tipo;
        public Color corOverlay = Color.white;
        [Tooltip("Opcional — só tipos com um efeito visível pra TODOS (ex: calor da lava) precisam " +
            "de um prefab aqui. Deixe vazio pra terrenos só-Mestre (ex: água rasa, terreno difícil).")]
        public GameObject prefabVfx;
    }

    [Header("Overlay tático (Tilemap — só o Mestre vê)")]
    [Tooltip("Tilemap dedicado ao overlay de terreno — não reaproveite o Tilemap do fundo do mapa. " +
        "SetRole desliga só o Renderer dele, não o GameObject inteiro, pra continuar existindo (com " +
        "os dados) mesmo invisível.")]
    [SerializeField] private Tilemap tilemapOverlay;
    [SerializeField] private TilemapRenderer tilemapRenderer;
    [Tooltip("Sprite quadrado branco — a MESMA tile pra todo terreno, colorida por célula via " +
        "Tilemap.SetColor. Não precisa de uma Tile por cor.")]
    [SerializeField] private TileBase tileQuadradoBranco;

    [Header("Container dos VFX (ex: calor de lava — visível pra todos)")]
    [Tooltip("Pai de todo VFX instanciado — GameObject à parte do Tilemap, nunca desativado por " +
        "SetRole. Criado automaticamente se ficar vazio.")]
    [SerializeField] private Transform containerVfx;

    [Header("Configuração por tipo de terreno")]
    [SerializeField] private List<ConfiguracaoVisual> configuracoesVisuais = new List<ConfiguracaoVisual>();

    // Dado lógico — funciona pra todo mundo (Mestre e Jogador), independente do overlay estar visível.
    private readonly Dictionary<Vector3Int, TerrainType> _dadosTerreno = new Dictionary<Vector3Int, TerrainType>();
    // Um VFX ativo por célula — permite trocar/remover sem duplicar (ver AtualizarVfxDaCelula).
    private readonly Dictionary<Vector3Int, GameObject> _vfxAtivos = new Dictionary<Vector3Int, GameObject>();

    private Dictionary<TerrainType, Color> _corPorTipo;
    private Dictionary<TerrainType, GameObject> _vfxPrefabPorTipo;

    // Default true: em Campanhas (preparação single-user, sem conceito de jogador) SetRole nunca
    // precisa ser chamado e o Mestre sempre pode pintar; em Salas, quem chama seta explicitamente via
    // SetRole conforme SouHost()/SouMestre() da cena.
    private bool _souMestre = true;

    private void Awake()
    {
        if (tilemapOverlay == null) tilemapOverlay = GetComponent<Tilemap>();
        if (tilemapRenderer == null) tilemapRenderer = GetComponent<TilemapRenderer>();

        if (containerVfx == null)
        {
            var go = new GameObject("Terrenos_VFX");
            go.transform.SetParent(transform, false);
            containerVfx = go.transform;
        }

        _corPorTipo = new Dictionary<TerrainType, Color>();
        _vfxPrefabPorTipo = new Dictionary<TerrainType, GameObject>();
        foreach (ConfiguracaoVisual cfg in configuracoesVisuais)
        {
            if (cfg == null) continue;
            _corPorTipo[cfg.tipo] = cfg.corOverlay;
            if (cfg.prefabVfx != null) _vfxPrefabPorTipo[cfg.tipo] = cfg.prefabVfx;
        }
    }

    // ── Pintura ───────────────────────────────────────────────────────────────

    /// <summary>Pinta (ou repinta) uma célula com um tipo de terreno — atualiza dado lógico, overlay
    /// visual (tint da mesma tile branca) e VFX (troca o antigo pelo novo, se houver um configurado
    /// pro tipo). TerrainType.Nenhum equivale a apagar. Só o Mestre pode pintar (ver SetRole).</summary>
    public void PintarTerreno(Vector3Int celula, TerrainType tipo)
    {
        if (!_souMestre)
        {
            VTTLog.BoardWarn("[TerrainManager] Apenas o Mestre pode pintar terreno.");
            return;
        }

        if (tipo == TerrainType.Nenhum)
        {
            ApagarTerreno(celula);
            return;
        }

        _dadosTerreno[celula] = tipo;
        AtualizarTileVisual(celula, tipo);
        AtualizarVfxDaCelula(celula, tipo);
    }

    /// <summary>Só o Mestre pode apagar (ver SetRole) — mesma regra de PintarTerreno.</summary>
    public void ApagarTerreno(Vector3Int celula)
    {
        if (!_souMestre)
        {
            VTTLog.BoardWarn("[TerrainManager] Apenas o Mestre pode apagar terreno.");
            return;
        }

        if (!_dadosTerreno.Remove(celula)) return;

        tilemapOverlay.SetTile(celula, null);
        RemoverVfxDaCelula(celula);
    }

    /// <summary>Reset completo (ex: trocar de mapa) — mesma regra de permissão de PintarTerreno.</summary>
    public void LimparTudo()
    {
        if (!_souMestre)
        {
            VTTLog.BoardWarn("[TerrainManager] Apenas o Mestre pode limpar o terreno.");
            return;
        }

        tilemapOverlay.ClearAllTiles();
        _dadosTerreno.Clear();

        foreach (GameObject vfx in _vfxAtivos.Values)
            if (vfx != null) Destroy(vfx);
        _vfxAtivos.Clear();
    }

    /// <summary>Consultado pelas regras de jogo (custo de movimento, dano de lava etc) — funciona
    /// igual pro Mestre e pro Jogador, independente do overlay estar visível ou não. Sem gate de
    /// permissão de propósito: LER o terreno nunca deve depender de quem está perguntando.</summary>
    public TerrainType ObterTerrenoEm(Vector3Int celula) =>
        _dadosTerreno.TryGetValue(celula, out TerrainType tipo) ? tipo : TerrainType.Nenhum;

    /// <summary>Converte uma posição de mundo (ex: clique do mouse já projetado) na célula do overlay
    /// correspondente — atalho pra quem chama não precisar guardar a referência do Tilemap.</summary>
    public Vector3Int MundoParaCelula(Vector3 posicaoMundo) => tilemapOverlay.WorldToCell(posicaoMundo);

    private void AtualizarTileVisual(Vector3Int celula, TerrainType tipo)
    {
        if (tilemapOverlay == null || tileQuadradoBranco == null) return;

        tilemapOverlay.SetTile(celula, tileQuadradoBranco);
        // Tiles nascem com TileFlags.LockColor por padrão — sem isso, SetColor abaixo não tem efeito
        // nenhum (gotcha clássico da API de Tilemap).
        tilemapOverlay.SetTileFlags(celula, TileFlags.None);
        tilemapOverlay.SetColor(celula, _corPorTipo.TryGetValue(tipo, out Color cor) ? cor : Color.white);
    }

    // ── VFX (ex: calor de lava) ──────────────────────────────────────────────

    private void AtualizarVfxDaCelula(Vector3Int celula, TerrainType tipo)
    {
        RemoverVfxDaCelula(celula); // troca de tipo na mesma célula não deve empilhar VFX

        if (!_vfxPrefabPorTipo.TryGetValue(tipo, out GameObject prefab) || prefab == null) return;

        Vector3 posicaoMundo = tilemapOverlay.GetCellCenterWorld(celula);
        _vfxAtivos[celula] = Instantiate(prefab, posicaoMundo, Quaternion.identity, containerVfx);
    }

    private void RemoverVfxDaCelula(Vector3Int celula)
    {
        if (!_vfxAtivos.TryGetValue(celula, out GameObject vfx)) return;
        if (vfx != null) Destroy(vfx);
        _vfxAtivos.Remove(celula);
    }

    // ── Visão Mestre / Jogador ───────────────────────────────────────────────

    /// <summary>Liga/desliga o overlay tático — SÓ o Renderer, nunca o GameObject do Tilemap (os
    /// dados de terreno continuam acessíveis via ObterTerrenoEm pros dois papéis, e o próprio Mestre
    /// continua conseguindo pintar/apagar independente do overlay estar visível no momento — ver
    /// _souMestre, que é quem realmente controla a permissão de PintarTerreno/ApagarTerreno).
    /// containerVfx NUNCA é tocado aqui — é assim que o calor da lava continua visível pro jogador
    /// mesmo com o overlay desligado.</summary>
    public void SetRole(bool isGM)
    {
        _souMestre = isGM;
        if (tilemapRenderer != null) tilemapRenderer.enabled = isGM;
    }
}
