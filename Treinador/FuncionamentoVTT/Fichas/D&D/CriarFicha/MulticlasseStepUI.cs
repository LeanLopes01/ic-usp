using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// UMA LINHA de opção de classe dentro do MulticlasseGerenciador.
/// Usa o modelo híbrido (Button onClick + IPointerClickHandler) idêntico ao MainOptionButtonUI.
/// </summary>
public class MulticlasseStepUI : MonoBehaviour, IPointerClickHandler
{
    [Header("UI")]
    public TMP_Text     classeText;
    public Button       button;
    public Toggle       selectionToggle;
    public TMP_Dropdown levelDropdown;

    // ── Estado público ────────────────────────────────────────────
    public string ClasseId         { get; private set; }
    public bool   EstaSelecionada  { get; private set; }
    public int    NivelSelecionado { get; private set; }

    // ── Privados ──────────────────────────────────────────────────
    private Action<MulticlasseStepUI>      _onSingleClick;
    private Action<MulticlasseStepUI>      _onDoubleClick;
    private Action<MulticlasseStepUI, int> _onNivelAlterado;

    private int _saldoNiveis;
    private float _lastClickTime = -1f;
    private const float DoubleClickInterval = 0.3f;

    // Evita que SetVisualSelected (chamado pra refletir estado — restauração, ou a confirmação já
    // processada) dispare de novo o listener do Toggle como se fosse um clique real do jogador, o
    // que causaria confirmar→SetVisualSelected→onValueChanged→confirmar de novo. Mesmo padrão de
    // SubChoiceOptionUI.
    private bool _atualizandoToggleInternamente;

    // ── Setup ─────────────────────────────────────────────────────

    public void Setup(string classeId, string classeNome, int saldoNiveis,
                      Action<MulticlasseStepUI>      onSingleClick,
                      Action<MulticlasseStepUI>      onDoubleClick,
                      Action<MulticlasseStepUI, int> onNivelAlterado)
    {
        ClasseId         = classeId;
        EstaSelecionada  = false;
        NivelSelecionado = 0;
        _saldoNiveis     = saldoNiveis;
        _onSingleClick   = onSingleClick;
        _onDoubleClick   = onDoubleClick;
        _onNivelAlterado = onNivelAlterado;
        _lastClickTime   = -1f;

        if (classeText)      classeText.text = classeNome;
        if (selectionToggle) selectionToggle.isOn = false;
        if (levelDropdown)   levelDropdown.gameObject.SetActive(false);

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnButtonClick);
        }

        if (selectionToggle != null)
        {
            selectionToggle.interactable = true;
            selectionToggle.onValueChanged.RemoveListener(OnToggleValueChanged);
            selectionToggle.onValueChanged.AddListener(OnToggleValueChanged);
        }

        SetVisualSelected(false);
    }

    /// <summary>Restaura estado salvo sem disparar callbacks.</summary>
    public void RestaurarSemNotificar(bool selecionada, int nivel, int saldoNiveis,
                                      Action<MulticlasseStepUI>      onSingleClick,
                                      Action<MulticlasseStepUI>      onDoubleClick,
                                      Action<MulticlasseStepUI, int> onNivelAlterado)
    {
        _saldoNiveis     = saldoNiveis;
        _onSingleClick   = onSingleClick;
        _onDoubleClick   = onDoubleClick;
        _onNivelAlterado = onNivelAlterado;
        EstaSelecionada  = selecionada;
        NivelSelecionado = selecionada ? nivel : 0;
        _lastClickTime   = -1f;

        SetVisualSelected(selecionada);

        if (selecionada && levelDropdown != null)
        {
            levelDropdown.gameObject.SetActive(true);
            PopularLevelDropdown(saldoNiveis,
                selecionadoIdx: Mathf.Clamp(nivel - 1, 0, saldoNiveis - 1),
                semNotificar: true);
        }
        else if (levelDropdown != null)
        {
            levelDropdown.gameObject.SetActive(false);
        }
    }

    /// <summary>Ativa/desativa visualmente a seleção e controla o dropdown.</summary>
    public void SetSelecionado(bool selecionado, int saldoNiveis)
    {
        EstaSelecionada = selecionado;
        _saldoNiveis    = saldoNiveis;

        SetVisualSelected(selecionado);

        if (selecionado)
        {
            if (levelDropdown != null)
            {
                levelDropdown.gameObject.SetActive(true);
                PopularLevelDropdown(saldoNiveis);
            }
            NivelSelecionado = saldoNiveis;
        }
        else
        {
            NivelSelecionado = 0;
            if (levelDropdown != null)
                levelDropdown.gameObject.SetActive(false);
        }
    }

    // ── IPointerClickHandler ──────────────────────────────────────

    // Detecta duplo-clique por intervalo de tempo.
    // Usado quando Button é filho (único caminho). Se não há Button,
    // OnPointerClick trata os cliques diretamente.
    private void OnButtonClick()
    {
        float now = Time.unscaledTime;
        Debug.Log($"[MulticlasseStepUI.OnButtonClick] id={ClasseId} delta={now - _lastClickTime:F3}");
        if (_lastClickTime > 0f && (now - _lastClickTime) <= DoubleClickInterval)
        {
            _lastClickTime = -1f;
            Debug.Log($"[MulticlasseStepUI] DOUBLE CLICK → id={ClasseId}");
            _onDoubleClick?.Invoke(this);
        }
        else
        {
            _lastClickTime = now;
            Debug.Log($"[MulticlasseStepUI] SINGLE CLICK → id={ClasseId}");
            _onSingleClick?.Invoke(this);
        }
    }

    // Marcar o Toggle (clique real do jogador nele) confirma a escolha direto, sem precisar de
    // duplo clique no texto/Button.
    private void OnToggleValueChanged(bool isOn)
    {
        if (_atualizandoToggleInternamente) return;
        if (isOn) _onDoubleClick?.Invoke(this);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        // Se Button está no mesmo objeto, OnButtonClick já tratou — ignora aqui.
        if (button) return;
        Debug.Log($"[MulticlasseStepUI.OnPointerClick] id={ClasseId} clickCount={eventData.clickCount} (sem Button)");
        if (eventData.clickCount == 1)      _onSingleClick?.Invoke(this);
        else if (eventData.clickCount == 2) _onDoubleClick?.Invoke(this);
    }

    // ── Privados ──────────────────────────────────────────────────

    public void SetVisualSelected(bool selected)
    {
        if (selectionToggle == null) return;
        _atualizandoToggleInternamente = true;
        selectionToggle.isOn = selected;
        _atualizandoToggleInternamente = false;
    }

    private void PopularLevelDropdown(int saldo, int selecionadoIdx = -1, bool semNotificar = false)
    {
        if (levelDropdown == null) return;

        levelDropdown.onValueChanged.RemoveAllListeners();
        levelDropdown.ClearOptions();

        var opcoes = new List<string>();
        for (int n = 1; n <= saldo; n++)
            opcoes.Add(n.ToString());
        levelDropdown.AddOptions(opcoes);

        int idx = selecionadoIdx >= 0 ? selecionadoIdx : saldo - 1;
        if (semNotificar)
            levelDropdown.SetValueWithoutNotify(idx);
        else
            levelDropdown.value = idx;

        levelDropdown.RefreshShownValue();
        levelDropdown.onValueChanged.AddListener(OnLevelAlterado);

        if (!semNotificar)
            _onNivelAlterado?.Invoke(this, idx + 1);
    }

    private void OnLevelAlterado(int idx)
    {
        NivelSelecionado = idx + 1;
        _onNivelAlterado?.Invoke(this, NivelSelecionado);
    }
}
