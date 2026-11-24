using System;
using UnityEngine;

/// <summary>
/// Gerenciador central de popups.
/// Os slots são PREFABS — cada chamada instancia o prefab dentro do popupContainerPai
/// e o destrói ao fechar. O container pai começa inativo e é ativado/desativado automaticamente.
///
/// Hierarquia recomendada no Canvas:
///   PopupContainerPai  (começa INATIVO — é o fundo/overlay)
///
/// Os prefabs ficam na pasta Prefabs do projeto e são arrastados para os slots.
///
/// USO:
///   PopupManager.Instance.MostrarCarregamento("Salvando...");
///   PopupManager.Instance.AtualizarProgresso(0.6f);
///   PopupManager.Instance.Fechar();
///
///   PopupManager.Instance.MostrarConfirmacao("Sair?", "Progresso será perdido.",
///       onSim: () => Application.Quit());
///
///   PopupManager.Instance.MostrarErro("Falha", "Sem conexão.");
///
///   PopupManager.Instance.MostrarEdicao("Renomear", "Novo nome:", nomeAtual,
///       onConfirmar: (texto) => AplicarNovoNome(texto));
/// </summary>
public class PopupManager : MonoBehaviour
{
    public static PopupManager Instance { get; private set; }

    [Header("Container pai (overlay — começa INATIVO)")]
    public GameObject popupContainerPai;

    [Header("Prefabs de popup — arrastar da pasta Prefabs")]
    public PopupUI popupCarregamento;
    public PopupUI popupConfirmacao;
    public PopupUI popupErro;
    public PopupUI popupInfo;
    public PopupUI popupSucesso;
    public PopupUI popupEdicao;

    private PopupUI _popupAtivo;

    // ── Singleton ────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        // Garante o estado inicial correto por código, independente de como
        // o container ficou marcado na cena (ex.: alguém deixou ativo
        // durante edição do layout e esqueceu de desmarcar). Desativar o
        // container também oculta qualquer popup deixado ativo dentro dele.
        if (popupContainerPai != null)
            popupContainerPai.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── API principal ─────────────────────────────────────────────

    /// <summary>Instancia e exibe o popup de carregamento com spinner.</summary>
    public void MostrarCarregamento(string titulo, string mensagem = "Aguarde...")
    {
        Mostrar(popupCarregamento, PopupConfig.Carregamento(titulo, mensagem));
    }

    /// <summary>Atualiza barra de progresso e texto do popup de carregamento ativo.</summary>
    /// <param name="detalhe">Linha extra opcional (ex: "12.3 MB / 45.6 MB — 2.1 MB/s").</param>
    public void AtualizarProgresso(float valor01, string texto = null, string detalhe = null)
    {
        if (_popupAtivo == null) return;
        string label = texto ?? $"{Mathf.RoundToInt(Mathf.Clamp01(valor01) * 100)}%";
        _popupAtivo.AtualizarProgresso(valor01, label, detalhe);
    }

    /// <summary>Instancia e exibe popup de confirmação com botões Sim / Não.</summary>
    public void MostrarConfirmacao(
        string titulo, string mensagem,
        Action onSim    = null,
        Action onNao    = null,
        string labelSim = "Sim",
        string labelNao = "Não")
    {
        Mostrar(popupConfirmacao, PopupConfig.Confirmacao(
            titulo, mensagem,
            onSim: () => { Fechar(); onSim?.Invoke(); },
            onNao: () => { Fechar(); onNao?.Invoke(); },
            labelSim: labelSim,
            labelNao: labelNao
        ));
    }

    /// <summary>Instancia e exibe popup de erro com botão OK.</summary>
    public void MostrarErro(string titulo, string mensagem, Action onOk = null)
    {
        Mostrar(popupErro, PopupConfig.Erro(titulo, mensagem,
            onOk: () => { Fechar(); onOk?.Invoke(); }
        ));
    }

    /// <summary>Instancia e exibe popup informativo com botão OK.</summary>
    public void MostrarInfo(string titulo, string mensagem, Action onOk = null)
    {
        Mostrar(popupInfo, PopupConfig.Info(titulo, mensagem,
            onOk: () => { Fechar(); onOk?.Invoke(); }
        ));
    }

    /// <summary>Instancia e exibe popup de escolha entre duas opções (reutiliza popupConfirmacao).</summary>
    public void MostrarEscolha(
        string titulo, string mensagem,
        string labelOpcao1 = "Opção 1", Action onOpcao1 = null,
        string labelOpcao2 = "Opção 2", Action onOpcao2 = null)
    {
        Mostrar(popupConfirmacao, PopupConfig.Confirmacao(
            titulo, mensagem,
            onSim:    () => { Fechar(); onOpcao1?.Invoke(); },
            onNao:    () => { Fechar(); onOpcao2?.Invoke(); },
            labelSim: labelOpcao1,
            labelNao: labelOpcao2
        ));
    }

    /// <summary>Instancia e exibe popup de subir nível com dropdown de nível.</summary>
    public void MostrarSubirNivel(int nivelAtual, Action<int> onConfirmar, Action onCancelar = null)
    {
        Mostrar(popupConfirmacao, PopupConfig.SubirNivel(
            nivelAtual,
            onConfirmar: (nivel) => { Fechar(); onConfirmar?.Invoke(nivel); },
            onCancelar:  () => { Fechar(); onCancelar?.Invoke(); }
        ));
    }

    /// <summary>Instancia e exibe popup de sucesso com botão OK.</summary>
    public void MostrarSucesso(string titulo, string mensagem, Action onOk = null)
    {
        Mostrar(popupSucesso, PopupConfig.Sucesso(titulo, mensagem,
            onOk: () => { Fechar(); onOk?.Invoke(); }
        ));
    }

    /// <summary>Instancia e exibe popup de edição com um campo de texto (ex: renomear algo).
    /// onConfirmar recebe o texto digitado no clique em "Salvar".</summary>
    public void MostrarEdicao(
        string titulo, string mensagem, string textoInicial,
        Action<string> onConfirmar,
        Action onCancelar     = null,
        string placeholder    = "",
        string labelConfirmar = "Salvar",
        string labelCancelar  = "Cancelar")
    {
        Mostrar(popupEdicao, PopupConfig.Edicao(
            titulo, mensagem, textoInicial,
            onConfirmar: (texto) => { Fechar(); onConfirmar?.Invoke(texto); },
            onCancelar:  () => { Fechar(); onCancelar?.Invoke(); },
            placeholder: placeholder,
            labelConfirmar: labelConfirmar,
            labelCancelar: labelCancelar
        ));
    }

    // ── API avançada ──────────────────────────────────────────────

    /// <summary>
    /// Instancia um prefab PopupUI dentro do container e aplica a config.
    /// Qualquer popup anterior é destruído antes de criar o novo.
    /// </summary>
    public void Mostrar(PopupUI prefab, PopupConfig config)
    {
        // Sempre destrói o popup ativo antes de criar outro (ou ao receber null)
        DestruirPopupAtivo();

        if (prefab == null)
        {
            Debug.LogWarning("[PopupManager] Prefab de popup não atribuído no Inspector.");
            if (popupContainerPai != null) popupContainerPai.SetActive(false);
            return;
        }

        // Ativa o container (overlay) ANTES de instanciar o filho
        if (popupContainerPai != null)
            popupContainerPai.SetActive(true);

        // Instancia o prefab dentro do container
        Transform parent = popupContainerPai != null ? popupContainerPai.transform : transform;
        _popupAtivo = Instantiate(prefab, parent, false);
        _popupAtivo.gameObject.SetActive(true);
        _popupAtivo.Configurar(config);
    }

    // ── Fechar ────────────────────────────────────────────────────

    /// <summary>Destrói o popup ativo e desativa o container.</summary>
    public void Fechar()
    {
        DestruirPopupAtivo();
        if (popupContainerPai != null)
            popupContainerPai.SetActive(false);
    }

    /// <summary>True se há um popup instanciado e visível.</summary>
    public bool EstaAberto => _popupAtivo != null;

    // ── Privados ──────────────────────────────────────────────────

    private void DestruirPopupAtivo()
    {
        if (_popupAtivo == null) return;
        Destroy(_popupAtivo.gameObject);
        _popupAtivo = null;
    }
}
