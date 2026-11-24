using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Um segmento de parede livre — vetor do ponto A ao ponto B, sem NENHUMA relação com o grid de
/// combate. Coordenadas normalizadas (0..1) relativas ao tamanho da imagem do mapa (x: 0=esquerda,
/// 1=direita; y: 0=base, 1=topo — mesma convenção de Texture2D.GetPixels), não em unidades de
/// mundo: assim a geometria da parede continua correta mesmo se a escala/posição do mapa mudar
/// depois (ver CampanhaMapManager.NormalizadoParaMundo). A longo prazo, o usuário desenha essas
/// linhas manualmente na UI; por ora, CampanhaMapaVetorizador as gera automaticamente a partir da
/// imagem (detecção de contorno + simplificação Ramer-Douglas-Peucker).
/// </summary>
[Serializable]
public class WallLine
{
    public Vector2 pontoA;
    public Vector2 pontoB;
}

/// <summary>Um token de NPC posicionado numa cena — ver CampanhaMapaData.tokens.</summary>
[Serializable]
public class CampanhaTokenData
{
    // Id resolvido do NPC (DynamicXmlBuilder.ResolverEntryId — mesmo nome do arquivo .xml em
    // CampanhasLocalStorage.NpcsFolderPath). Se o NPC for renomeado depois, esse link quebra —
    // limitação aceita por ora, é o mesmo esquema de id usado pros hints de imagem.
    public string npcId;
    public float posX;
    public float posY;

    // Raio de visão do token, em METROS (não unidades de mundo — ver CampanhaMapManager.
    // ConverterMetrosParaMundo, que converte usando a convenção de 1 quadrado = 1.5m). 0 nos JSONs
    // salvos antes desse campo existir — CampanhaMapManager.RestaurarTokens cai no padrão nesse caso.
    public float raioVisaoMetros;

    // ID estável desta INSTÂNCIA de token (não do NPC — dois tokens do mesmo NPC têm npcId igual mas
    // tokenId diferente, ver CampanhaTokenDraggable.TokenId) — vazio em JSONs salvos antes desse
    // campo existir, CampanhaMapManager.RestaurarTokens gera um novo nesse caso (self-healing, mesmo
    // padrão de MapConfig.mapaId).
    public string tokenId;
    public bool bloqueado;
    public bool escondido;
}

/// <summary>
/// Uma "cena" da campanha: mapa (imagem + grid) + paredes vetorizadas + os tokens de NPC
/// posicionados nela. O nome do arquivo/classe ficou "Mapa" por histórico, mas guarda o estado
/// completo da cena — ver CampanhaMapManager.SalvarEstadoTokensNoMapaAtivo/RestaurarTokens.
/// </summary>
[Serializable]
public class CampanhaMapaData
{
    public string mapaId;
    public string nome;
    public string imagemPath;
    public int gridColunas = 20;
    public int gridLinhas = 20;
    public float unidadesPorCelula = 1.5f;
    public float escalaVisual = 1f;
    public bool vetorizado;
    public List<WallLine> paredes = new List<WallLine>();
    // Retângulos de fog manual (ver FogRect.cs) — presos a células do grid, não à imagem do mapa.
    public List<FogRect> fogRects = new List<FogRect>();
    // Fontes de luz estática (tocha — ver Salas/Grid/LightSource.cs) — posição normalizada, não
    // presa a célula de grid.
    public List<LightSource> fontesDeLuz = new List<LightSource>();
    public List<CampanhaTokenData> tokens = new List<CampanhaTokenData>();
    public string criadoEmUtc;
    public string atualizadoEmUtc;
}

[Serializable]
public class CampanhaMapaDataCollection
{
    public CampanhaMapaData[] mapas = Array.Empty<CampanhaMapaData>();
}
