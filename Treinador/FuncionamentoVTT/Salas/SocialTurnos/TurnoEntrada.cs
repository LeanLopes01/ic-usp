using System;
using System.Collections.Generic;

/// <summary>
/// Uma entrada na lista de turnos (iniciativa) de SocialTurnosManager — um token do grid, com o
/// valor rolado e a classificação aliado/inimigo. [Serializable] pra viajar via JsonUtility dentro
/// de TurnoListaPayload nas RPCs de sincronização (mesma convenção de ChatEnvelope em ChatManager).
/// </summary>
[Serializable]
public class TurnoEntrada
{
    public string tokenId;
    public string nomeExibicao;
    public int valor;

    // Só é alternável pra tokens SEM ficha vinculada (monstros/NPCs/tokens soltos) — tokens de
    // ficha são sempre aliados, nunca marcáveis como inimigo (ver SocialTurnosManager.
    // AlternarInimigoServerRpc). Falso = aliado (verde), o padrão pra qualquer token novo.
    public bool ehInimigo;

    // Congelado no momento da rolagem — controla se o item pode alternar ehInimigo (ver acima) e
    // se o "Rolar Turno" deve usar o modificador de Destreza da ficha ou rolar 1d20 puro.
    public bool temFicha;
}

/// <summary>Payload trocado nas RPCs de SocialTurnosManager — a lista de turnos inteira +
/// qual entrada é "o turno atual" (índice em turnos, -1 = nenhuma).</summary>
[Serializable]
public class TurnoListaPayload
{
    public List<TurnoEntrada> turnos = new List<TurnoEntrada>();
    public int indiceAtual = -1;
}
