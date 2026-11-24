using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Componente compartilhado por todos os prefabs de popup.
/// Cada prefab preenche apenas os campos que usa no Inspector;
/// os demais podem ficar nulos sem causar erros.
/// Configurado via PopupConfig pelo PopupManager.
/// </summary>
public class PopupUI : MonoBehaviour
{
    [Header("Comum")]
    public TMP_Text      tituloText;
    public TMP_Text      mensagemText;
    public Button        botaoFechar;

    [Header("Progresso (popup de carregamento)")]
    public Slider        barraProgresso;
    public TMP_Text      textoProgresso;
    [Tooltip("Linha extra opcional (ex: taxa de download, MB baixados/total). Deixe vazio se não usar.")]
    public TMP_Text      textoProgressoDetalhado;
    public RectTransform spinnerIcon;

    [Header("Botões de ação")]
    public Button   botaoConfirmar;
    public TMP_Text labelBotaoConfirmar;
    public Button   botaoCancelar;
    public TMP_Text labelBotaoCancelar;

    [Header("Fechar sem ação (só fecha o popup)")]
    public Button botaoSoDismiss;

    [Header("Dropdown de nível (opcional)")]
    public TMP_Dropdown dropdownNivel;

    [Header("Campo de texto (popup de edição)")]
    public TMP_InputField inputTexto;

    [Header("Ícones de estado (opcional)")]
    public GameObject iconeErro;
    public GameObject iconeInfo;
    public GameObject iconeSucesso;
    public GameObject iconeCarregamento;

    // ── Estado interno ───────────────────────────────────────────

    private Action          _onConfirmar;
    private Action          _onCancelar;
    private Action<int>     _onNivelSelecionado;
    private Action<string>  _onTextoConfirmado;
    private Coroutine       _spinnerCoroutine;

    // ── Ciclo de vida ────────────────────────────────────────────

    private void OnDestroy()
    {
        PararSpinner();
        if (botaoConfirmar  != null) botaoConfirmar.onClick.RemoveAllListeners();
        if (botaoCancelar   != null) botaoCancelar.onClick.RemoveAllListeners();
        if (botaoFechar     != null) botaoFechar.onClick.RemoveAllListeners();
        if (botaoSoDismiss  != null) botaoSoDismiss.onClick.RemoveAllListeners();
    }

    // ── API pública ───────────────────────────────────────────────

    /// <summary>Aplica todas as configurações de um PopupConfig.</summary>
    public void Configurar(PopupConfig cfg)
    {
        if (cfg == null) return;

        _onConfirmar        = cfg.OnConfirmar;
        _onCancelar         = cfg.OnCancelar;
        _onNivelSelecionado = cfg.OnNivelSelecionado;
        _onTextoConfirmado  = cfg.OnTextoConfirmado;

        // Re-registra listeners aqui também: garante que funcionam mesmo que
        // Awake tenha rodado antes do botão ser atribuído no Inspector.
        if (botaoConfirmar != null)
        {
            botaoConfirmar.onClick.RemoveAllListeners();
            botaoConfirmar.onClick.AddListener(OnClickConfirmar);
        }
        if (botaoCancelar != null)
        {
            botaoCancelar.onClick.RemoveAllListeners();
            botaoCancelar.onClick.AddListener(OnClickCancelar);
        }
        // botaoSoDismiss: fecha o popup sem disparar nenhum callback
        if (botaoSoDismiss != null)
        {
            botaoSoDismiss.onClick.RemoveAllListeners();
            botaoSoDismiss.onClick.AddListener(() => PopupManager.Instance?.Fechar());
        }

        // botaoFechar só aparece quando há apenas um botão de ação (sem escolha dupla)
        bool temEscolhaDupla = cfg.MostrarBotaoConfirmar && cfg.MostrarBotaoCancelar;
        if (botaoFechar != null)
        {
            botaoFechar.gameObject.SetActive(!temEscolhaDupla && cfg.MostrarBotaoCancelar);
            botaoFechar.onClick.RemoveAllListeners();
            if (!temEscolhaDupla) botaoFechar.onClick.AddListener(OnClickCancelar);
        }

        // Textos
        if (tituloText   != null) tituloText.text   = cfg.Titulo    ?? "";
        if (mensagemText != null) mensagemText.text  = cfg.Mensagem  ?? "";

        // Botão confirmar
        if (botaoConfirmar != null) botaoConfirmar.gameObject.SetActive(cfg.MostrarBotaoConfirmar);
        if (labelBotaoConfirmar != null && !string.IsNullOrEmpty(cfg.LabelConfirmar))
            labelBotaoConfirmar.text = cfg.LabelConfirmar;

        // Botão cancelar
        if (botaoCancelar != null) botaoCancelar.gameObject.SetActive(cfg.MostrarBotaoCancelar);
        if (labelBotaoCancelar != null && !string.IsNullOrEmpty(cfg.LabelCancelar))
            labelBotaoCancelar.text = cfg.LabelCancelar;

        // Botão fechar (segue cancelar, ou regra própria se quiser)
        if (botaoFechar != null) botaoFechar.gameObject.SetActive(cfg.MostrarBotaoCancelar);

        // Ícones
        if (iconeErro         != null) iconeErro.SetActive(cfg.MostrarIconeErro);
        if (iconeInfo         != null) iconeInfo.SetActive(cfg.MostrarIconeInfo);
        if (iconeSucesso      != null) iconeSucesso.SetActive(cfg.MostrarIconeSucesso);
        if (iconeCarregamento != null) iconeCarregamento.SetActive(cfg.MostrarSpinner);

        // Progresso
        if (barraProgresso  != null) barraProgresso.value = 0f;
        if (textoProgresso  != null) textoProgresso.text  = "";
        if (textoProgressoDetalhado != null) textoProgressoDetalhado.text = "";

        // Spinner
        if (cfg.MostrarSpinner) IniciarSpinner();
        else                    PararSpinner();

        // Dropdown de nível
        if (dropdownNivel != null)
        {
            dropdownNivel.gameObject.SetActive(cfg.MostrarDropdownNivel);
            if (cfg.MostrarDropdownNivel)
            {
                dropdownNivel.ClearOptions();
                var opcoes = new List<string>();
                for (int n = cfg.NivelMinDropdown; n <= 20; n++)
                    opcoes.Add($"Nível {n}");
                dropdownNivel.AddOptions(opcoes);
                dropdownNivel.value = 0;
                dropdownNivel.RefreshShownValue();
            }
        }

        // Campo de texto (popup de edição)
        if (inputTexto != null)
        {
            inputTexto.gameObject.SetActive(cfg.MostrarInputTexto);
            if (cfg.MostrarInputTexto)
            {
                inputTexto.text = cfg.TextoInicial ?? "";
                if (inputTexto.placeholder is TMP_Text placeholderTexto)
                    placeholderTexto.text = cfg.PlaceholderTexto ?? "";
            }
        }
    }

    /// <summary>Atualiza a barra de progresso em tempo real.</summary>
    public void AtualizarProgresso(float valor01, string texto, string detalhe = null)
    {
        if (barraProgresso != null) barraProgresso.value = Mathf.Clamp01(valor01);
        if (textoProgresso != null) textoProgresso.text  = texto ?? "";
        if (textoProgressoDetalhado != null) textoProgressoDetalhado.text = detalhe ?? "";
    }

    // ── Privados ─────────────────────────────────────────────────

    private void IniciarSpinner()
    {
        if (spinnerIcon == null) return;
        if (_spinnerCoroutine != null) StopCoroutine(_spinnerCoroutine);
        _spinnerCoroutine = StartCoroutine(RotacionarSpinner());
    }

    private void PararSpinner()
    {
        if (_spinnerCoroutine == null) return;
        StopCoroutine(_spinnerCoroutine);
        _spinnerCoroutine = null;
    }

    private IEnumerator RotacionarSpinner()
    {
        while (true)
        {
            spinnerIcon.Rotate(0f, 0f, -360f * Time.unscaledDeltaTime);
            yield return null;
        }
    }

    // Evento estático: qualquer sistema pode escutar confirmações de popup
    public static event Action AoConfirmar;
    public static event Action AoCancelar;

    public void OnClickConfirmar()
    {
        if (_onNivelSelecionado != null && dropdownNivel != null && dropdownNivel.gameObject.activeSelf)
        {
            string textoOpcao = dropdownNivel.options[dropdownNivel.value].text; // "Nível X"
            if (int.TryParse(textoOpcao.Replace("Nível ", "").Trim(), out int nivelEscolhido))
                _onNivelSelecionado.Invoke(nivelEscolhido);
        }

        if (_onTextoConfirmado != null && inputTexto != null && inputTexto.gameObject.activeSelf)
            _onTextoConfirmado.Invoke(inputTexto.text);

        _onConfirmar?.Invoke();
        AoConfirmar?.Invoke();
    }

    public void OnClickCancelar()
    {
        _onCancelar?.Invoke();
        AoCancelar?.Invoke();
    }
}
