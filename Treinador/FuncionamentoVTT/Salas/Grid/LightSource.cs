using System;
using UnityEngine;

/// <summary>
/// Um ponto de luz estática (ex: uma tocha) plantado pelo Mestre — ver LightEditorTools.cs. É
/// matematicamente idêntico à visão de um token PARADO (mesmo polígono de visão, mesmo bloqueio por
/// parede via raycasting — ver MapManager/CampanhaMapManager.AtualizarVisaoDeTodosOsTokens), só que
/// com origem fixa em vez de acompanhar um token.
///
/// Posição em coordenada NORMALIZADA (0..1, relativa ao tamanho da imagem do mapa — mesma convenção
/// de WallLine, ver Campanhas/Grid/CampanhaMapaData.cs), não em célula de grid como FogRect: uma
/// tocha é plantada clicando livremente no mapa, não precisa ficar presa a uma célula — e sobrevive
/// a reposicionamento/reescala da imagem via NormalizadoParaMundo.
/// </summary>
[Serializable]
public class LightSource
{
    public string id; // GUID — identifica a luz pra Remover/edição futura
    public Vector2 posicaoNormalizada;
    // Raio de alcance, em METROS (mesma convenção de MapTokenDraggable.RaioVisaoMetros — ver
    // MapManager.ConverterMetrosParaMundo, convenção fixa de mesa "1 quadrado = 1.5m").
    public float raioMetros;
}
