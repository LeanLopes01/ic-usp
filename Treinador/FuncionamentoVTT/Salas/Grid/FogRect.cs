using System;

/// <summary>
/// Um retângulo de "fog manual" (ferramenta estilo Owlbear) — área que fica SEMPRE 100% preta pro
/// jogador comum, independente da visão dinâmica dos tokens. Ao contrário de WallLine (coordenada
/// normalizada 0..1, relativa à IMAGEM do mapa — ver Campanhas/Grid/CampanhaMapaData.cs), um FogRect
/// é preso a ÍNDICES DE CÉLULA do grid (colMin/linMin/colMax/linMax, inclusive) — sobrevive a
/// reposicionamento/reescala da imagem do mapa, sempre recalculado a partir da config de grid atual
/// (ver MapManager.CelulaParaBoundsMundo / CampanhaMapManager.CelulaParaBoundsMundo).
/// </summary>
[Serializable]
public class FogRect
{
    public int colMin;
    public int linMin;
    public int colMax;
    public int linMax;
}
