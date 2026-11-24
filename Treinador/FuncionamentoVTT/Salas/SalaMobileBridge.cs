using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using UnityEngine;

/// <summary>
/// Ponte entre a sala "de verdade" (Steam P2P/NGO, entre desktops) e o(s) celular(es) companheiro(s)
/// (LAN, via SalaLanServidor). O PC do Mestre já recebe tudo (é sempre peer, e host recebe de todos),
/// então este script só escuta os eventos que já chegam (NetworkSyncManager/SalaLanServidor) e
/// repassa pro lado que falta — sem duplicar lógica de rede em nenhum dos dois protocolos.
///
/// Coloque este script no mesmo GameObject de SalaLanServidor/AnunciadorDeSalaLocal (ex: onde hoje
/// fica o "AnunciadorDeSalaLocal (auto)"/"ServidorFichaTCP (auto)" criados por
/// BarraFerramentasManager.ToggleSincronizacaoMobile — troque essas referências por
/// SalaLanServidor/SalaMobileBridge).
/// </summary>
public class SalaMobileBridge : MonoBehaviour
{
    [SerializeField] private SalaLanServidor lanServidor;
    [SerializeField] private AnunciadorDeSalaLocal anunciador;
    [SerializeField] private MapManager mapManager;
    [SerializeField] private ChatManager chatManager;

    private void Start()
    {
        if (lanServidor == null) lanServidor = FindFirstObjectByType<SalaLanServidor>();
        if (anunciador == null) anunciador = FindFirstObjectByType<AnunciadorDeSalaLocal>();
        if (mapManager == null) mapManager = FindFirstObjectByType<MapManager>();
        if (chatManager == null) chatManager = FindFirstObjectByType<ChatManager>();

        if (lanServidor == null)
        {
            Debug.LogWarning("[SalaMobileBridge] SalaLanServidor não encontrado na cena.");
            return;
        }

        lanServidor.OnJogadorEntrou   += AoJogadorEntrar;
        lanServidor.OnChatRecebido    += AoChatRecebido;
        lanServidor.OnPedidoRolagem   += AoPedidoRolagem;
        lanServidor.OnPedidoStatBlock += AoPedidoStatBlock;

        if (chatManager != null)
        {
            chatManager.OnMensagemDistribuida -= AoMensagemDistribuidaNoChat;
            chatManager.OnMensagemDistribuida += AoMensagemDistribuidaNoChat;
        }

        if (NetworkSyncManager.Instancia != null) AssinarNetworkSync();
        else StartCoroutine(AssinarNetworkSyncQuandoDisponivel());
    }

    private IEnumerator AssinarNetworkSyncQuandoDisponivel()
    {
        while (NetworkSyncManager.Instancia == null) yield return null;
        AssinarNetworkSync();
    }

    private void AssinarNetworkSync()
    {
        NetworkSyncManager.Instancia.OnTokenMovidoRecebido   -= AoTokenMovidoNaRede;
        NetworkSyncManager.Instancia.OnTokenMovidoRecebido   += AoTokenMovidoNaRede;
        NetworkSyncManager.Instancia.OnTokenSeguradoRecebido -= AoTokenSeguradoNaRede;
        NetworkSyncManager.Instancia.OnTokenSeguradoRecebido += AoTokenSeguradoNaRede;
    }

    private void OnDestroy()
    {
        if (lanServidor != null)
        {
            lanServidor.OnJogadorEntrou   -= AoJogadorEntrar;
            lanServidor.OnChatRecebido    -= AoChatRecebido;
            lanServidor.OnPedidoRolagem   -= AoPedidoRolagem;
            lanServidor.OnPedidoStatBlock -= AoPedidoStatBlock;
        }
        if (chatManager != null)
            chatManager.OnMensagemDistribuida -= AoMensagemDistribuidaNoChat;
        if (NetworkSyncManager.Instancia != null)
        {
            NetworkSyncManager.Instancia.OnTokenMovidoRecebido   -= AoTokenMovidoNaRede;
            NetworkSyncManager.Instancia.OnTokenSeguradoRecebido -= AoTokenSeguradoNaRede;
        }
    }

    /// <summary>Liga anúncio UDP + servidor TCP juntos — chamado no lugar do antigo par
    /// AnunciadorDeSalaLocal.IniciarAnuncio + ServidorFichaTCP.IniciarServidor.</summary>
    public void LigarSincronizacao(string nomeSala)
    {
        lanServidor?.IniciarServidor();
        anunciador?.IniciarAnuncio(nomeSala);
    }

    public void DesligarSincronizacao()
    {
        anunciador?.PararAnuncio();
        lanServidor?.PararServidor();
    }

    // ── Celular -> resto do jogo ─────────────────────────────────────────────

    private void AoJogadorEntrar(TcpClient cliente, string nome)
    {
        Debug.Log($"[SalaMobileBridge] '{nome}' entrou pelo celular.");

        if (mapManager == null) return;

        // Mapa primeiro — o TCP garante a ordem de entrega na mesma conexão, e os tokens (enviados
        // em seguida) precisam do grid/mapa já carregado pra AjustarPosicaoTokenAoGrid fazer sentido.
        byte[] imagemMapa = mapManager.ObterImagemDoMapaAtivoParaRede();
        if (imagemMapa != null)
            lanServidor.EnviarPara(cliente, LanMsgTipo.MapaInicial, new LanMapaInicialPayload { imagemBytes = imagemMapa });
        else
            Debug.LogWarning("[SalaMobileBridge] Sem imagem de mapa ativo disponível — celular vai ver só os tokens, sem o grid/mapa.");

        lanServidor.EnviarPara(cliente, LanMsgTipo.EstadoInicial,
            new EstadoTokenArray { itens = mapManager.ObterEstadoTokensParaRede() });
    }

    private void AoChatRecebido(TcpClient cliente, string texto)
    {
        chatManager?.DistribuirMensagemExterna(texto, lanServidor.NomeDoJogador(cliente));
    }

    /// <summary>Replica toda mensagem de chat "comum" (texto simples) pro(s) celular(es) — inclusive
    /// a que o próprio celular acabou de mandar (fecha o loop) e as de outros desktops. Mensagens
    /// ricas (Ataque/Magia/Item/Teste) ainda não têm UI no celular — v1 só relaia texto puro.</summary>
    private void AoMensagemDistribuidaNoChat(ChatEnvelope envelope)
    {
        if (envelope == null || envelope.tipo != TipoMensagemChat.Comum) return;

        var dto = JsonUtility.FromJson<ChatComumDTO>(envelope.payloadJson);
        if (dto == null) return;

        lanServidor.EnviarParaTodos(LanMsgTipo.ChatRecebido, new LanChatRecebidoPayload
        {
            remetente = envelope.remetenteNome,
            texto = dto.texto,
            hora = envelope.horaEnvio,
        });
    }

    private void AoPedidoRolagem(TcpClient cliente, string notacao, string contexto)
    {
        ResultadoRolagem resultado = RolagemDados.RolarNotacaoDetalhada(notacao);
        string nomeJogador = lanServidor.NomeDoJogador(cliente);
        string detalhe = FormatarDetalheDados(resultado);

        lanServidor.EnviarPara(cliente, LanMsgTipo.RolagemResultado, new LanRolagemResultadoPayload
        {
            notacao = notacao,
            contexto = contexto,
            total = resultado.total,
            detalhe = detalhe,
        });

        string rotulo = string.IsNullOrWhiteSpace(contexto) ? notacao : $"{contexto} ({notacao})";
        chatManager?.DistribuirMensagemExterna($"🎲 rolou {rotulo}: {resultado.total} [{detalhe}]", nomeJogador);
    }

    private static string FormatarDetalheDados(ResultadoRolagem resultado)
    {
        if (resultado.dados == null || resultado.dados.Count == 0) return "-";

        var partes = new List<string>();
        foreach (DadoRolado dado in resultado.dados) partes.Add(dado.valor.ToString());
        string detalhe = string.Join(" + ", partes);

        if (resultado.modificador != 0)
            detalhe += $" {(resultado.modificador > 0 ? "+" : "")}{resultado.modificador}";

        return detalhe;
    }

    private void AoPedidoStatBlock(TcpClient cliente, string tokenId)
    {
        if (mapManager == null) return;

        MapTokenDraggable token = mapManager.EncontrarTokenPorId(tokenId);
        if (token == null) return;

        // Testes = TODAS as perícias/salvaguardas testáveis vêm preenchidas (ver ModoStatBlockTooltip)
        // — é o que faz sentido pro celular, que não tem menu de atalho Atacar/Curar próprio.
        TokenStatBlockData dados = TokenStatBlockTooltipManager.ConstruirDados(token, ModoStatBlockTooltip.Testes);
        if (dados == null) return;

        lanServidor.EnviarPara(cliente, LanMsgTipo.StatBlockDados, dados);
    }

    // ── Resto do jogo -> celular ─────────────────────────────────────────────

    private void AoTokenMovidoNaRede(string tokenId, Vector3 pos)
    {
        lanServidor.EnviarParaTodos(LanMsgTipo.TokenMovido,
            new MsgMoverToken { tokenId = tokenId, x = pos.x, y = pos.y, z = pos.z });
    }

    private void AoTokenSeguradoNaRede(string tokenId, string nomeJogador, bool segurando)
    {
        lanServidor.EnviarParaTodos(segurando ? LanMsgTipo.TokenSegurado : LanMsgTipo.TokenSolto,
            new MsgTokenSegurado { tokenId = tokenId, nomeJogador = nomeJogador });
    }
}
