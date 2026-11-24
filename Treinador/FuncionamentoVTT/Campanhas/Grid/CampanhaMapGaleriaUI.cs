using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lista os mapas já salvos da campanha aberta; clicar num item carrega ele no CampanhaMapManager.
/// Usado tanto dentro do próprio painel de Mapa (galeria ao lado do grid) quanto num painel
/// separado só de listagem (ver CampanhasManager.listaMapasPainel) — o mesmo componente serve
/// pros dois casos, o segundo só usa o callback aoSelecionar pra trocar de painel.
/// </summary>
public class CampanhaMapGaleriaUI : MonoBehaviour
{
    [SerializeField] private Transform content;
    [SerializeField] private GameObject prefabItemMapa; // com CampanhaMapItemUI

    [Header("Enviar para Sala")]
    [Tooltip("Painel de seleção de sala pra 'Enviar' uma cena — sem prefab próprio, por pedido: vive " +
        "direto nesta hierarquia, aberto pelo botão \"Enviar\" de um CampanhaMapItemUI.")]
    [SerializeField] private GameObject painelEnviarSala;
    [SerializeField] private TMP_Text   enviarSalaTituloText; // opcional: "Enviar "Nome" para:"
    [SerializeField] private Transform  enviarSalaListaContent;
    [SerializeField] private GameObject prefabItemSalaEnvio; // com CampanhaEnvioSalaItemUI
    [SerializeField] private Button     botaoFecharPainelEnvio;

    private readonly List<GameObject> _instanciados = new List<GameObject>();
    private readonly List<GameObject> _salasInstanciadas = new List<GameObject>();
    private string _campanhaId;

    private void Awake()
    {
        if (painelEnviarSala != null) painelEnviarSala.SetActive(false);
        if (botaoFecharPainelEnvio != null)
        {
            botaoFecharPainelEnvio.onClick.RemoveAllListeners();
            botaoFecharPainelEnvio.onClick.AddListener(FecharPainelEnvio);
        }
    }

    /// <param name="aoSelecionar">Opcional. Chamado (além de carregar o mapa no mapManager) quando o
    /// botão "Carregar" de um item é clicado — usado pelo painel separado de listagem pra voltar ao
    /// painel do grid.</param>
    /// <param name="aoEditar">Opcional. Chamado quando o botão "Editar" de um item é clicado — quem
    /// chamou decide o que "editar" significa (normalmente abrir CampanhaMapImportUI.EditarMapa).</param>
    public void Carregar(string campanhaId, CampanhaMapManager mapManager,
        Action<CampanhaMapaData> aoSelecionar = null, Action<CampanhaMapaData> aoEditar = null)
    {
        _campanhaId = campanhaId;
        Limpar();
        if (content == null || prefabItemMapa == null || string.IsNullOrWhiteSpace(campanhaId)) return;

        // Sistema/descrição são da CAMPANHA (não do mapa — CampanhaMapaData não tem esses campos
        // próprios), mesmo valor repassado pra todo item desta lista.
        string sistemaCampanha = "";
        string descricaoCampanha = "";
        foreach (CampanhaLocalSnapshot s in CampanhasLocalStorage.LoadAll())
        {
            if (s == null || s.campanhaId != campanhaId) continue;
            sistemaCampanha = s.sistema;
            descricaoCampanha = s.descricao;
            break;
        }

        foreach (CampanhaMapaData mapa in CampanhaMapasStorage.LoadAll(campanhaId))
        {
            GameObject go = UIUtils.Spawn(prefabItemMapa, content);
            _instanciados.Add(go);

            CampanhaMapItemUI item = go.GetComponent<CampanhaMapItemUI>();
            CampanhaMapaData capturado = mapa;
            item?.Configurar(capturado, sistemaCampanha, descricaoCampanha,
                onCarregar: () =>
                {
                    // TrocarMapaComProgresso (não CarregarMapa direto) — mostra popup de progresso
                    // enquanto troca; aoSelecionar (troca de painel) dispara imediatamente e não
                    // depende do mapa já estar carregado, então não há problema de ordem aqui.
                    mapManager?.TrocarMapaComProgresso(capturado);
                    aoSelecionar?.Invoke(capturado);
                },
                onEditar: () => aoEditar?.Invoke(capturado),
                onEnviar: () => AbrirPainelEnvio(capturado));
        }
    }

    private void Limpar()
    {
        foreach (GameObject go in _instanciados)
            if (go != null) Destroy(go);
        _instanciados.Clear();
    }

    // ── Enviar para Sala ─────────────────────────────────────────────────────

    /// <summary>Abre o painel de seleção de sala pra enviar essa cena (CampanhaParaSalaConverter) —
    /// só lista salas onde o usuário é mestre. A sala de destino não precisa estar aberta: o envio
    /// grava direto nos dados salvos dela em disco (ver SalaDataStorage).</summary>
    public void AbrirPainelEnvio(CampanhaMapaData mapa)
    {
        if (mapa == null || painelEnviarSala == null) return;

        if (enviarSalaTituloText != null)
            enviarSalaTituloText.text = $"Enviar \"{mapa.nome}\" para:";

        LimparListaDeSalas();

        if (enviarSalaListaContent != null && prefabItemSalaEnvio != null)
        {
            foreach (SalaLocalSnapshot sala in SalaLocalStorage.LoadAll())
            {
                if (sala == null || sala.meuPapel != "Mestre") continue;

                GameObject go = UIUtils.Spawn(prefabItemSalaEnvio, enviarSalaListaContent);
                _salasInstanciadas.Add(go);

                SalaLocalSnapshot capturada = sala;
                go.GetComponent<CampanhaEnvioSalaItemUI>()?.Configurar(capturada, onEnviarAqui: () =>
                {
                    CampanhaParaSalaConverter.EnviarMapaParaSala(mapa, _campanhaId, capturada.salaLocalId);
                    PopupManager.Instance?.MostrarSucesso("Enviado", $"\"{mapa.nome}\" enviado para \"{capturada.nomeSala}\".");
                    // MostrarSucesso é modal (só fecha com clique em "OK") — some sozinho depois de
                    // um tempinho, pra não deixar a tela parecendo travada/quebrada (sem nenhuma
                    // campanha clicável) caso o jogador não note o popup por cima da lista.
                    StartCoroutine(FecharPopupSucessoAposDelay());
                    FecharPainelEnvio();
                });
            }
        }

        painelEnviarSala.SetActive(true);
    }

    public void FecharPainelEnvio()
    {
        if (painelEnviarSala != null) painelEnviarSala.SetActive(false);
        LimparListaDeSalas();
    }

    private void LimparListaDeSalas()
    {
        foreach (GameObject go in _salasInstanciadas)
            if (go != null) Destroy(go);
        _salasInstanciadas.Clear();
    }

    private static readonly WaitForSeconds EsperaFecharPopupSucesso = new WaitForSeconds(1.5f);

    private IEnumerator FecharPopupSucessoAposDelay()
    {
        yield return EsperaFecharPopupSucesso;
        PopupManager.Instance?.Fechar();
    }
}
