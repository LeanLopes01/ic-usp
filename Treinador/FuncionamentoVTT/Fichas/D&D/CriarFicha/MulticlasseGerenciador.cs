using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Prefab container de um "slot" de escolha de classe em multiclasse.
/// Comportamento idêntico ao StepContainerUI (esconde os outros ao confirmar e permite desfazer).
/// </summary>
public class MulticlasseGerenciador : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text      tituloText;
    public RectTransform optionsContainer;
    public Button        titleButton; // NOVO: Botão no título para desfazer a escolha (Igual ao StepContainer)

    [Header("Prefab de linha de classe")]
    public GameObject optionPrefab;

    // ── Estado público ────────────────────────────────────────────
    public string ClasseSelecionada { get; private set; }
    public int    NivelSelecionado  { get; private set; }

    public Action<MulticlasseGerenciador, string, int> OnAlterado;

    // ── Privados ──────────────────────────────────────────────────
    private List<MulticlasseStepUI> _linhas = new List<MulticlasseStepUI>();
    private MulticlasseStepUI       _linhaSelecionada;
    private int                     _saldoNiveis;
    private bool                    _passoConfirmado;

    // ── API pública ───────────────────────────────────────────────

    public void Inicializar(List<string> classes, int saldoNiveis, string titulo,
                            Action<MulticlasseGerenciador, string, int> onAlterado)
    {
        OnAlterado        = onAlterado;
        _saldoNiveis      = saldoNiveis;
        ClasseSelecionada = null;
        NivelSelecionado  = 0;
        _linhaSelecionada = null;
        _passoConfirmado  = false;

        if (tituloText) tituloText.text = titulo;

        // Configura o botão de desfazer
        if (titleButton != null)
        {
            titleButton.onClick.RemoveAllListeners();
            titleButton.onClick.AddListener(() => { if (_passoConfirmado) DesfazerSelecao(); });
        }

        LimparLinhas();
        CriarLinhas(classes, saldoNiveis);
        RebuildLayout();
    }

    public void RestaurarSemNotificar(string classe, int nivel, List<string> classes,
                                      int saldoNiveis, string titulo,
                                      Action<MulticlasseGerenciador, string, int> onAlterado)
    {
        OnAlterado        = onAlterado;
        _saldoNiveis      = saldoNiveis;
        ClasseSelecionada = classe;
        NivelSelecionado  = nivel;
        _linhaSelecionada = null;
        _passoConfirmado  = false;

        if (tituloText) tituloText.text = titulo;

        if (titleButton != null)
        {
            titleButton.onClick.RemoveAllListeners();
            titleButton.onClick.AddListener(() => { if (_passoConfirmado) DesfazerSelecao(); });
        }

        LimparLinhas();

        foreach (var c in classes)
        {
            var linha = CriarLinha(c, saldoNiveis);
            bool ehSelecionada = string.Equals(c, classe, StringComparison.OrdinalIgnoreCase);
            
            if (ehSelecionada)
            {
                linha.RestaurarSemNotificar(true, nivel, saldoNiveis, OnLinhaSelecionada, OnLinhaDuploClique, OnNivelAlterado);
                _linhaSelecionada = linha;
                _passoConfirmado = true;
            }
        }

        // Se restaurou um passo já confirmado, esconde as outras opções
        if (_passoConfirmado)
        {
            foreach (var l in _linhas)
            {
                if (l != _linhaSelecionada) l.gameObject.SetActive(false);
            }
        }

        RebuildLayout();
    }

    // ── Privados ──────────────────────────────────────────────────

    private void CriarLinhas(List<string> classes, int saldoNiveis)
    {
        foreach (var c in classes)
            CriarLinha(c, saldoNiveis);
    }

    private MulticlasseStepUI CriarLinha(string classeId, int saldoNiveis)
    {
        // Aqui usamos o Instantiate padrão da Unity (mas você pode manter o UIUtils.Spawn se preferir)
        var go = Instantiate(optionPrefab, optionsContainer, false);
        go.SetActive(true);
        var linha = go.GetComponent<MulticlasseStepUI>();
        
        linha.Setup(classeId, classeId, saldoNiveis, OnLinhaSelecionada, OnLinhaDuploClique, OnNivelAlterado);
        
        _linhas.Add(linha);
        return linha;
    }

    private void LimparLinhas()
    {
        foreach (Transform child in optionsContainer) Destroy(child.gameObject);
        _linhas.Clear();
    }

    // Callback 1: Clique Único (Apenas expande o dropdown)
    private void OnLinhaSelecionada(MulticlasseStepUI linha)
    {
        if (_passoConfirmado) return; // Se já confirmou, ignora clique único

        VTTLog.Sheet($"[MulticlasseGerenciador] OnLinhaSelecionada (single click): classe={linha.ClasseId}");

        if (_linhaSelecionada != null && _linhaSelecionada != linha)
            _linhaSelecionada.SetSelecionado(false, 0);

        _linhaSelecionada = linha;
        ClasseSelecionada = linha.ClasseId;
        linha.SetSelecionado(true, _saldoNiveis);
    }

    // Callback 2: Duplo Clique (Confirma a classe e esconde as outras)
    private void OnLinhaDuploClique(MulticlasseStepUI linha)
    {
        VTTLog.Sheet($"[MulticlasseGerenciador] OnLinhaDuploClique: classe={linha.ClasseId} passoConfirmado={_passoConfirmado}");
        if (_passoConfirmado)
        {
            DesfazerSelecao();
            return;
        }

        ConfirmarClasse(linha, linha.NivelSelecionado > 0 ? linha.NivelSelecionado : _saldoNiveis);
    }

    // Callback 3: Quando escolhe um nível no Dropdown
    private void OnNivelAlterado(MulticlasseStepUI linha, int nivel)
    {
        if (_passoConfirmado)
        {
            // Já confirmado — apenas atualiza o nível escolhido
            NivelSelecionado = nivel;
            VTTLog.Sheet($"[MulticlasseGerenciador] OnNivelAlterado (pós-confirmação): classe={ClasseSelecionada} nivel={nivel}");
            OnAlterado?.Invoke(this, ClasseSelecionada, nivel);
        }
        else
        {
            // Apenas registra visualmente o nível pré-selecionado, sem confirmar.
            // A confirmação acontece somente via duplo clique (OnLinhaDuploClique).
            VTTLog.Sheet($"[MulticlasseGerenciador] OnNivelAlterado (pré-confirmação, ignorado): classe={linha.ClasseId} nivel={nivel}");
        }
    }

    // ── Lógica de Confirmação e UI (Idêntico ao StepContainer) ────

    private void ConfirmarClasse(MulticlasseStepUI escolha, int nivel)
    {
        _passoConfirmado  = true;
        _linhaSelecionada = escolha;
        ClasseSelecionada = escolha.ClasseId;
        NivelSelecionado  = nivel;
        VTTLog.Sheet($"[MulticlasseGerenciador] ConfirmarClasse: classe={ClasseSelecionada} nivel={NivelSelecionado}");

        // A Mágica: Esconde todas as outras opções!
        foreach (var o in _linhas)
        {
            if (o != escolha) o.gameObject.SetActive(false);
        }

        escolha.SetSelecionado(true, _saldoNiveis);
        RebuildLayout();

        OnAlterado?.Invoke(this, ClasseSelecionada, NivelSelecionado);
    }

    private void DesfazerSelecao()
    {
        _passoConfirmado  = false;
        _linhaSelecionada = null;
        ClasseSelecionada = null;
        NivelSelecionado  = 0;

        // Mostra todo mundo de novo
        foreach (var o in _linhas)
        {
            o.gameObject.SetActive(true);
            o.SetSelecionado(false, 0);
        }

        RebuildLayout();
        
        // Passar nivel 0 avisa o ClassSelector que a escolha foi deletada
        OnAlterado?.Invoke(this, null, 0); 
    }

    private void RebuildLayout()
    {
        RectTransform rect = GetComponent<RectTransform>();
        if (rect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

        if (transform.parent != null)
        {
            RectTransform parentRect = transform.parent.GetComponent<RectTransform>();
            if (parentRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
        }
    }
}