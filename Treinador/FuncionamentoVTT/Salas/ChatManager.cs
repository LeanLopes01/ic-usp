using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using Steamworks; // Para pegarmos o nome/SteamId real do jogador na Steam
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Chat de sala — cada mensagem vira uma instância de PREFAB (não mais texto concatenado num
/// TMP_Text só), com um prefab diferente por tipo (ver TipoMensagemChat/Salas/Chat/
/// ChatMensagemPayloads.cs): mensagem comum, rolagem de ataque, descrição de magia, descrição de
/// item, teste geral. Todo o roteamento usa um único par de RPCs (EnviarMensagemServerRpc/
/// DistribuirMensagemClientRpc) que carregam um ChatEnvelope serializado em JSON — mesma convenção
/// já usada em Salas/Grid/NetworkSyncManager.cs (discriminador de tipo + payload em JSON).
///
/// Este script NÃO calcula rolagens/críticos — só exibe o que os métodos públicos (EnviarRolagemDe
/// Ataque/EnviarTesteGeral/etc.) recebem já prontos. Um sistema de rolagem futuro chama esses métodos.
/// </summary>
public class ChatManager : NetworkBehaviour
{
    [Header("Referências da UI")]
    [Tooltip("Content do ScrollView onde as mensagens (prefabs) são instanciadas")]
    [SerializeField] private Transform contentMensagens;

    [Tooltip("O campo onde o jogador digita a mensagem")]
    public TMP_InputField inputDaMensagem;

    [Tooltip("Botão opcional para enviar a mensagem")]
    public Button botaoEnviar;

    [Tooltip("ScrollRect do histórico para auto-scroll no fim")]
    public ScrollRect scrollDoChat;

    [Header("Prefabs de Mensagem (um por tipo — ver TipoMensagemChat)")]
    [SerializeField] private GameObject prefabMensagemComum;
    [SerializeField] private GameObject prefabMensagemAtaque;
    [SerializeField] private GameObject prefabMensagemMagia;
    [SerializeField] private GameObject prefabMensagemItem;
    [SerializeField] private GameObject prefabMensagemTeste;

    [Header("Áudio")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Tocado (PlayOneShot) em todo cliente que receber qualquer tipo de mensagem, inclusive quem enviou.")]
    [SerializeField] private AudioClip somNovaMensagem;

    [Header("Painel do Chat")]
    [SerializeField] private GameObject painelChat;
    [SerializeField] private Button botaoToggleChat;

    [Header("Notificação (badge de mensagens não lidas)")]
    [Tooltip("GameObject do badge (ícone + contador) — ativo só enquanto houver mensagem não lida " +
        "(painel fechado). Zera ao abrir o painel.")]
    [SerializeField] private GameObject notificacaoGO;
    [SerializeField] private TMP_Text notificacaoContadorText;

    private int mensagensNaoLidas;

    /// <summary>Disparado em todo cliente (incluindo o host) sempre que uma mensagem é distribuída —
    /// usado por SalaMobileBridge pra replicar toda mensagem (de qualquer origem: desktop ou mobile)
    /// pro(s) celular(es) conectado(s) via CHAT_RECEBIDO.</summary>
    public event Action<ChatEnvelope> OnMensagemDistribuida;

    // Coalesce o rebuild de UI quando várias mensagens chegam no mesmo frame (ex: dano em área
    // acertando N tokens = N ClientRpc no mesmo frame) — em vez de forçar um rebuild global de
    // Canvas por mensagem (caro, trava o frame), marca a intenção aqui e resolve UMA vez em
    // LateUpdate, depois que todas as mensagens do frame já foram instanciadas.
    private bool _scrollPendente;

    private void Start()
    {
        if (painelChat != null)
            painelChat.SetActive(false);

        // Faz com que apertar 'Enter' dispare a função de enviar
        if (inputDaMensagem != null)
        {
            inputDaMensagem.lineType = TMP_InputField.LineType.SingleLine;
            inputDaMensagem.onSubmit.RemoveListener(TentarEnviarMensagem);
            inputDaMensagem.onSubmit.AddListener(TentarEnviarMensagem);
        }

        if (botaoEnviar != null)
        {
            botaoEnviar.onClick.RemoveListener(OnBotaoEnviarClick);
            botaoEnviar.onClick.AddListener(OnBotaoEnviarClick);
        }

        if (botaoToggleChat != null)
        {
            botaoToggleChat.onClick.RemoveListener(TogglePainelChat);
            botaoToggleChat.onClick.AddListener(TogglePainelChat);
        }
    }

    private void OnDestroy()
    {
        if (inputDaMensagem != null)
            inputDaMensagem.onSubmit.RemoveListener(TentarEnviarMensagem);

        if (botaoEnviar != null)
            botaoEnviar.onClick.RemoveListener(OnBotaoEnviarClick);

        if (botaoToggleChat != null)
            botaoToggleChat.onClick.RemoveListener(TogglePainelChat);
    }

    // ── Painel ────────────────────────────────────────────────────────────────

    public bool EstaAbertoPainel() => painelChat != null && painelChat.activeSelf;

    public void AbrirPainel()
    {
        if (painelChat != null) painelChat.SetActive(true);

        mensagensNaoLidas = 0;
        AtualizarBadgeNotificacao();
    }

    public void FecharPainel()
    {
        if (painelChat != null) painelChat.SetActive(false);
    }

    public void TogglePainelChat()
    {
        if (EstaAbertoPainel())
            FecharPainel();
        else
            AbrirPainel();
    }

    // ── Envio — mensagem comum (input/botão/Enter) ──────────────────────────────

    private void OnBotaoEnviarClick()
    {
        if (inputDaMensagem == null) return;
        TentarEnviarMensagem(inputDaMensagem.text);
    }

    private void TentarEnviarMensagem(string mensagem)
    {
        if (inputDaMensagem == null) return;
        if (string.IsNullOrWhiteSpace(mensagem)) return;

        EnviarMensagemComum(mensagem);

        // Limpa a caixa de texto e mantém o cursor piscando lá dentro
        inputDaMensagem.text = "";
        inputDaMensagem.ActivateInputField();
    }

    // ── API pública — cada tipo de mensagem (ver Salas/Chat/ChatMensagemPayloads.cs) ────────────

    public void EnviarMensagemComum(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return;
        EnviarEnvelope(TipoMensagemChat.Comum, JsonUtility.ToJson(new ChatComumDTO { texto = texto.Trim() }));
    }

    /// <summary>Injeta uma mensagem comum de um remetente fora do NGO (o jogador mobile, via
    /// SalaLanServidor/SalaMobileBridge — o celular nunca entra na sessão NGO). Só o HOST deve
    /// chamar isto: ele já É o servidor, então distribui direto pros ClientRpc sem round-trip de
    /// ServerRpc (que exigiria ownership NGO que o celular não tem).</summary>
    public void DistribuirMensagemExterna(string texto, string nomeRemetente)
    {
        if (string.IsNullOrWhiteSpace(texto)) return;

        try
        {
            // NetworkManager.Singleton pode ser null se este PC não estiver de fato numa sessão NGO
            // ativa (ex: testando a ponte mobile sozinho, sem sala Steam aberta) — checa antes de
            // tocar em IsServer/ClientRpc pra não estourar exceção que derrubaria quem chamou isto
            // (ver SalaMobileBridge, chamado a partir do loop de eventos do SalaLanServidor).
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || !IsServer)
            {
                Debug.LogWarning("[Chat] DistribuirMensagemExterna chamado sem sessão NGO ativa como host — ignorado (mensagem não chega no chat, mas nada quebra).");
                return;
            }

            var envelope = new ChatEnvelope
            {
                tipo             = TipoMensagemChat.Comum,
                remetenteNome    = string.IsNullOrWhiteSpace(nomeRemetente) ? "Jogador (mobile)" : nomeRemetente,
                remetenteSteamId = "mobile",
                horaEnvio        = DateTime.Now.ToString("HH:mm"),
                payloadJson      = JsonUtility.ToJson(new ChatComumDTO { texto = texto.Trim() }),
            };

            DistribuirMensagemClientRpc(JsonUtility.ToJson(envelope));
        }
        catch (Exception e)
        {
            Debug.LogError($"[Chat] DistribuirMensagemExterna falhou: {e}");
        }
    }

    /// <summary>Chamado por um futuro sistema de rolagem — este script só exibe, não calcula.
    /// resultadoAtaque/resultadoDano carregam o detalhe por dado (ver RolagemDados.
    /// RolarNotacaoDetalhada) pro tooltip de hover no total (ver RolagemTooltipTrigger); o total já
    /// vem dentro de cada ResultadoRolagem, não precisa repetir como int separado.</summary>
    public void EnviarRolagemDeAtaque(string nomeAtaque, ResultadoRolagem resultadoAtaque, ResultadoCritico critAtaque,
        ResultadoRolagem resultadoDano, ResultadoCritico critDano)
    {
        EnviarEnvelope(TipoMensagemChat.Ataque, JsonUtility.ToJson(
            ConstruirAtaqueDTO(nomeAtaque, resultadoAtaque, critAtaque, resultadoDano, critDano)));
    }

    /// <summary>Monta (sem enviar) o envelope de uma rolagem de ataque — usado por
    /// EnviarEnvelopesEmLote quando várias mensagens (ex: uma por token atingido numa área) precisam
    /// ir no MESMO ServerRpc/ClientRpc em vez de um por alvo (ver SelecaoAlvoManager.Resolver*).</summary>
    public ChatEnvelope MontarEnvelopeDeAtaque(string nomeAtaque, ResultadoRolagem resultadoAtaque, ResultadoCritico critAtaque,
        ResultadoRolagem resultadoDano, ResultadoCritico critDano)
    {
        return new ChatEnvelope
        {
            tipo = TipoMensagemChat.Ataque,
            payloadJson = JsonUtility.ToJson(ConstruirAtaqueDTO(nomeAtaque, resultadoAtaque, critAtaque, resultadoDano, critDano)),
        };
    }

    private static ChatAtaqueDTO ConstruirAtaqueDTO(string nomeAtaque, ResultadoRolagem resultadoAtaque, ResultadoCritico critAtaque,
        ResultadoRolagem resultadoDano, ResultadoCritico critDano)
    {
        return new ChatAtaqueDTO
        {
            nomeAtaque    = nomeAtaque,
            totalAtaque   = resultadoAtaque?.total ?? 0,
            critAtaque    = critAtaque,
            totalDano     = resultadoDano?.total ?? 0,
            critDano      = critDano,
            notacaoAtaque = resultadoAtaque?.notacao ?? string.Empty,
            dadosAtaque   = resultadoAtaque?.dados ?? new List<DadoRolado>(),
            modificadorAtaque = resultadoAtaque?.modificador ?? 0,
            notacaoDano   = resultadoDano?.notacao ?? string.Empty,
            dadosDano     = resultadoDano?.dados ?? new List<DadoRolado>(),
            modificadorDano = resultadoDano?.modificador ?? 0,
        };
    }

    public void EnviarDescricaoDeMagia(string nomeMagia, string tempoConjuracao, string alcance,
        string alvo, string componentes, string duracao, string descricao)
    {
        EnviarEnvelope(TipoMensagemChat.Magia, JsonUtility.ToJson(new ChatMagiaDTO
        {
            nomeMagia       = nomeMagia,
            tempoConjuracao = tempoConjuracao,
            alcance         = alcance,
            alvo            = alvo,
            componentes     = componentes,
            duracao         = duracao,
            descricao       = descricao,
        }));
    }

    public void EnviarDescricaoDeItem(string nomeItem, string tipoItem, string raridadeItem,
        string pesoItem, string valorItem, string descricao)
    {
        EnviarEnvelope(TipoMensagemChat.Item, JsonUtility.ToJson(new ChatItemDTO
        {
            nomeItem      = nomeItem,
            tipoItem      = tipoItem,
            raridadeItem  = raridadeItem,
            pesoItem      = pesoItem,
            valorItem     = valorItem,
            descricao     = descricao,
        }));
    }

    /// <summary>Teste geral (perícia, resistência, cura, etc.) — mesma lógica de crítico das
    /// rolagens de ataque/dano. resultado carrega o detalhe por dado pro tooltip de hover no total
    /// (ver RolagemTooltipTrigger); o total já vem dentro dele.</summary>
    public void EnviarTesteGeral(string nomeTeste, ResultadoRolagem resultado, ResultadoCritico critico)
    {
        EnviarEnvelope(TipoMensagemChat.Teste, JsonUtility.ToJson(ConstruirTesteDTO(nomeTeste, resultado, critico)));
    }

    /// <summary>Monta (sem enviar) o envelope de um teste geral — mesmo motivo de
    /// MontarEnvelopeDeAtaque, usado por EnviarEnvelopesEmLote (ex: cura em área, uma mensagem por
    /// alvo curado, num só RPC).</summary>
    public ChatEnvelope MontarEnvelopeDeTeste(string nomeTeste, ResultadoRolagem resultado, ResultadoCritico critico)
    {
        return new ChatEnvelope
        {
            tipo = TipoMensagemChat.Teste,
            payloadJson = JsonUtility.ToJson(ConstruirTesteDTO(nomeTeste, resultado, critico)),
        };
    }

    private static ChatTesteDTO ConstruirTesteDTO(string nomeTeste, ResultadoRolagem resultado, ResultadoCritico critico)
    {
        return new ChatTesteDTO
        {
            nomeTeste     = nomeTeste,
            total         = resultado?.total ?? 0,
            critico       = critico,
            notacaoTeste  = resultado?.notacao ?? string.Empty,
            dadosTeste    = resultado?.dados ?? new List<DadoRolado>(),
            modificadorTeste = resultado?.modificador ?? 0,
        };
    }

    // ── Envio — ponto único (barreiras de rede + remetente) ─────────────────────

    private bool PodeEnviarMensagem()
    {
        if (!IsSpawned)
        {
            Debug.LogWarning("<color=yellow>Você precisa criar ou entrar em uma sala antes de usar o chat!</color>");
            return false;
        }

        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsClient)
        {
            Debug.LogWarning("[Chat] NetworkManager não iniciado ou cliente não conectado.");
            return false;
        }

        return true;
    }

    private void EnviarEnvelope(TipoMensagemChat tipo, string payloadJson)
    {
        if (!PodeEnviarMensagem()) return;

        ObterRemetente(out string nome, out string steamId);

        string envelopeJson = JsonUtility.ToJson(new ChatEnvelope
        {
            tipo             = tipo,
            remetenteNome    = nome,
            remetenteSteamId = steamId,
            horaEnvio        = "", // carimbado no servidor, ver EnviarMensagemServerRpc
            payloadJson      = payloadJson,
        });

        try
        {
            EnviarMensagemServerRpc(envelopeJson);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Chat] Falha ao enviar mensagem (RPC): " + e.Message);
        }
    }

    /// <summary>Manda VÁRIOS envelopes (ver MontarEnvelopeDeAtaque/MontarEnvelopeDeTeste) num ÚNICO
    /// ServerRpc/ClientRpc — usado quando uma resolução em área (SelecaoAlvoManager) gera uma
    /// mensagem por token atingido: em vez de pagar o despacho de RPC + serialização do ENVELOPE
    /// (não do payload individual, que é conteúdo de verdade e não dava pra evitar) uma vez por
    /// alvo, manda tudo junto. Remetente/hora são preenchidos aqui, uma vez só pro lote inteiro —
    /// todas as mensagens de uma mesma resolução compartilham o mesmo remetente e o mesmo instante.</summary>
    public void EnviarEnvelopesEmLote(List<ChatEnvelope> envelopesParciais)
    {
        if (envelopesParciais == null || envelopesParciais.Count == 0) return;

        // Só 1 mensagem — o caminho de lote não compensa, usa o de sempre.
        if (envelopesParciais.Count == 1)
        {
            EnviarEnvelope(envelopesParciais[0].tipo, envelopesParciais[0].payloadJson);
            return;
        }

        if (!PodeEnviarMensagem()) return;

        ObterRemetente(out string nome, out string steamId);
        foreach (ChatEnvelope envelope in envelopesParciais)
        {
            envelope.remetenteNome = nome;
            envelope.remetenteSteamId = steamId;
            envelope.horaEnvio = ""; // carimbado no servidor, ver EnviarLoteDeMensagensServerRpc
        }

        string loteJson = JsonUtility.ToJson(new ChatEnvelopeLote { mensagens = envelopesParciais });

        try
        {
            EnviarLoteDeMensagensServerRpc(loteJson);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Chat] Falha ao enviar lote de mensagens (RPC): " + e.Message);
        }
    }

    private static void ObterRemetente(out string nome, out string steamId)
    {
        nome = "Jogador";
        steamId = "0";
        try
        {
            if (SteamClient.IsValid)
            {
                nome = SteamClient.Name;
                steamId = SteamClient.SteamId.Value.ToString();
            }
        }
        catch { }
    }

    // ==========================================
    // 1. O CLIENTE FALA COM O SERVIDOR (MESTRE)
    // RequireOwnership = false permite que qualquer jogador mande mensagem
    // ==========================================
    [ServerRpc(RequireOwnership = false)]
    private void EnviarMensagemServerRpc(string envelopeJson)
    {
        ChatEnvelope envelope = JsonUtility.FromJson<ChatEnvelope>(envelopeJson);
        if (envelope == null) return;

        // Carimbo de hora AUTORITATIVO no servidor — não confia no relógio local de quem enviou,
        // mesmo motivo do carimbo por SteamServerTime já usado em NetworkSyncManager/MapManager.
        envelope.horaEnvio = DateTime.Now.ToString("HH:mm");

        DistribuirMensagemClientRpc(JsonUtility.ToJson(envelope));
    }

    // ==========================================
    // 2. O SERVIDOR FALA COM TODOS OS CLIENTES
    // ==========================================
    [ClientRpc]
    private void DistribuirMensagemClientRpc(string envelopeJson)
    {
        ChatEnvelope envelope = JsonUtility.FromJson<ChatEnvelope>(envelopeJson);
        if (envelope == null) return;

        InstanciarMensagem(envelope);
        OnMensagemDistribuida?.Invoke(envelope);

        // Som toca sempre, painel aberto ou fechado — o badge abaixo é só a notificação visual pra
        // quando o jogador não está olhando o chat no momento.
        if (audioSource != null && somNovaMensagem != null)
            audioSource.PlayOneShot(somNovaMensagem);

        if (!EstaAbertoPainel())
        {
            mensagensNaoLidas++;
            AtualizarBadgeNotificacao();
        }

        if (scrollDoChat != null)
            _scrollPendente = true;
    }

    // ==========================================
    // 3. VERSÃO EM LOTE — mesmo par de RPCs acima, só que carregando uma LISTA de envelopes (ver
    // EnviarEnvelopesEmLote/ChatEnvelopeLote) em vez de um só. Só paga o custo de despacho de RPC +
    // (de)serialização do envelope-container UMA vez, não uma vez por mensagem.
    // ==========================================
    [ServerRpc(RequireOwnership = false)]
    private void EnviarLoteDeMensagensServerRpc(string loteJson)
    {
        ChatEnvelopeLote lote = JsonUtility.FromJson<ChatEnvelopeLote>(loteJson);
        if (lote?.mensagens == null || lote.mensagens.Count == 0) return;

        // Mesmo carimbo AUTORITATIVO do servidor pra TODAS as mensagens do lote — vieram da mesma
        // resolução, então compartilhar o instante é correto, não só uma otimização.
        string horaAutoritativa = DateTime.Now.ToString("HH:mm");
        foreach (ChatEnvelope envelope in lote.mensagens)
            envelope.horaEnvio = horaAutoritativa;

        DistribuirLoteDeMensagensClientRpc(JsonUtility.ToJson(lote));
    }

    [ClientRpc]
    private void DistribuirLoteDeMensagensClientRpc(string loteJson)
    {
        ChatEnvelopeLote lote = JsonUtility.FromJson<ChatEnvelopeLote>(loteJson);
        if (lote?.mensagens == null) return;

        foreach (ChatEnvelope envelope in lote.mensagens)
            InstanciarMensagem(envelope);

        // Mesma cauda de DistribuirMensagemClientRpc, só que UMA vez pro lote inteiro (um som só,
        // não N sons empilhados; um incremento só do contador de não lidas, com a contagem certa).
        if (audioSource != null && somNovaMensagem != null)
            audioSource.PlayOneShot(somNovaMensagem);

        if (!EstaAbertoPainel())
        {
            mensagensNaoLidas += lote.mensagens.Count;
            AtualizarBadgeNotificacao();
        }

        if (scrollDoChat != null)
            _scrollPendente = true;
    }

    // Resolve o rebuild de Canvas + scroll-pro-fim UMA vez por frame, não uma vez por mensagem —
    // se 20 mensagens chegarem no mesmo frame (ex: AoE acertando 20 tokens), o Canvas só é
    // forçado a atualizar depois que TODAS já foram instanciadas em DistribuirMensagemClientRpc.
    private void LateUpdate()
    {
        if (!_scrollPendente || scrollDoChat == null) return;

        Canvas.ForceUpdateCanvases();
        scrollDoChat.verticalNormalizedPosition = 0f;
        _scrollPendente = false;
    }

    private void AtualizarBadgeNotificacao()
    {
        if (notificacaoGO != null) notificacaoGO.SetActive(mensagensNaoLidas > 0);
        if (notificacaoContadorText != null) notificacaoContadorText.text = mensagensNaoLidas.ToString();
    }

    private void InstanciarMensagem(ChatEnvelope envelope)
    {
        if (contentMensagens == null) return;

        GameObject prefab = envelope.tipo switch
        {
            TipoMensagemChat.Comum  => prefabMensagemComum,
            TipoMensagemChat.Ataque => prefabMensagemAtaque,
            TipoMensagemChat.Magia  => prefabMensagemMagia,
            TipoMensagemChat.Item   => prefabMensagemItem,
            TipoMensagemChat.Teste  => prefabMensagemTeste,
            _                       => null,
        };

        if (prefab == null)
        {
            Debug.LogWarning($"[Chat] Nenhum prefab configurado para o tipo '{envelope.tipo}'.");
            return;
        }

        GameObject go = Instantiate(prefab, contentMensagens);

        switch (envelope.tipo)
        {
            case TipoMensagemChat.Comum:
                go.GetComponent<ChatMensagemComumUI>()?.Configurar(envelope, JsonUtility.FromJson<ChatComumDTO>(envelope.payloadJson));
                break;
            case TipoMensagemChat.Ataque:
                go.GetComponent<ChatMensagemAtaqueUI>()?.Configurar(envelope, JsonUtility.FromJson<ChatAtaqueDTO>(envelope.payloadJson));
                break;
            case TipoMensagemChat.Magia:
                go.GetComponent<ChatMensagemMagiaUI>()?.Configurar(envelope, JsonUtility.FromJson<ChatMagiaDTO>(envelope.payloadJson));
                break;
            case TipoMensagemChat.Item:
                go.GetComponent<ChatMensagemItemUI>()?.Configurar(envelope, JsonUtility.FromJson<ChatItemDTO>(envelope.payloadJson));
                break;
            case TipoMensagemChat.Teste:
                go.GetComponent<ChatMensagemTesteUI>()?.Configurar(envelope, JsonUtility.FromJson<ChatTesteDTO>(envelope.payloadJson));
                break;
        }
    }
}
