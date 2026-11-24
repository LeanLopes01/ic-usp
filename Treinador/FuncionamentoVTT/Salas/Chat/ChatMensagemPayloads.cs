using System;
using System.Collections.Generic;

/// <summary>Tipo de mensagem de chat — decide qual prefab ChatManager instancia (ver
/// ChatManager.DistribuirMensagemClientRpc) e qual DTO abaixo desserializar de ChatEnvelope.payloadJson.</summary>
public enum TipoMensagemChat { Comum, Ataque, Magia, Item, Teste }

/// <summary>Resultado de uma rolagem pra fins de destaque visual (ver ChatCriticoUI) — enum em vez de
/// um par de bools "críticoSucesso"/"críticoFalha" pra não permitir o estado inválido dos dois true.</summary>
public enum ResultadoCritico { Normal, Sucesso, Falha }

/// <summary>
/// Envelope de toda mensagem de chat — mesma convenção de Salas/Grid/NetworkSyncManager.cs (NetMessage):
/// um discriminador de tipo + um payload em JSON específico do tipo, tudo serializado com JsonUtility
/// e mandado como um único parâmetro string nos RPCs de ChatManager. remetenteNome/SteamId são
/// preenchidos no cliente que envia; horaEnvio é carimbado no SERVIDOR (ver
/// ChatManager.EnviarMensagemServerRpc) — mesmo motivo do "carimbo no servidor" já usado em
/// NetworkSyncManager/MapManager: o relógio local de cada jogador pode estar dessincronizado.
/// </summary>
[Serializable]
public class ChatEnvelope
{
    public TipoMensagemChat tipo;
    public string remetenteNome;
    public string remetenteSteamId;
    public string horaEnvio;
    public string payloadJson;
}

/// <summary>
/// Lote de envelopes — usado por ChatManager.EnviarEnvelopesEmLote quando uma resolução em área
/// (SelecaoAlvoManager) gera várias mensagens de uma vez (uma por token atingido): em vez de um
/// ServerRpc/ClientRpc por mensagem, manda TODAS num único RPC. Cada item da lista continua sendo um
/// ChatEnvelope normal (mesmo tipo/payloadJson de sempre) — o cliente desempacota e instancia cada
/// um exatamente como já fazia em InstanciarMensagem, só que a partir de UM JSON só.
/// </summary>
[Serializable]
public class ChatEnvelopeLote
{
    public List<ChatEnvelope> mensagens = new List<ChatEnvelope>();
}

[Serializable]
public class ChatComumDTO
{
    public string texto;
}

[Serializable]
public class ChatAtaqueDTO
{
    public string nomeAtaque;
    public int totalAtaque;
    public ResultadoCritico critAtaque;
    public int totalDano;
    public ResultadoCritico critDano;

    // Detalhe por dado (ver RolagemDados.ResultadoRolagem) — pro tooltip de hover no total, ver
    // RolagemTooltipTrigger/DadosRolagemTooltipUI. modificador* é o bônus/penalidade fixo, separado
    // dos dados (ex: o "+7" de "1d20+7").
    public string notacaoAtaque;
    public List<DadoRolado> dadosAtaque = new List<DadoRolado>();
    public int modificadorAtaque;
    public string notacaoDano;
    public List<DadoRolado> dadosDano = new List<DadoRolado>();
    public int modificadorDano;
}

[Serializable]
public class ChatMagiaDTO
{
    public string nomeMagia;
    public string tempoConjuracao;
    public string alcance;
    public string alvo;
    public string componentes;
    public string duracao;
    public string descricao;
}

[Serializable]
public class ChatItemDTO
{
    public string nomeItem;
    public string tipoItem;
    public string raridadeItem;
    public string pesoItem;
    public string valorItem;
    public string descricao;
}

[Serializable]
public class ChatTesteDTO
{
    public string nomeTeste;
    public int total;
    public ResultadoCritico critico;

    // Detalhe por dado (ver RolagemDados.ResultadoRolagem) — pro tooltip de hover no total.
    public string notacaoTeste;
    public List<DadoRolado> dadosTeste = new List<DadoRolado>();
    public int modificadorTeste;
}
