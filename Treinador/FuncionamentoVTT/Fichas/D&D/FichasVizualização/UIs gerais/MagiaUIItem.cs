using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;

public class MagiaUIItem : MonoBehaviour
{
    [Header("Resumo (Cabeçalho)")]
    public TMP_InputField nomeInput;
    public TMP_InputField nivelInput;
    public TMP_InputField tempoInput;         // Tempo de conjuração (1 ação, 1 bônus, 1 reação, 1 minuto, etc.)
    public TMP_InputField alcanceInput;       // Alcance (Toque, 9m, 18m, Pessoal, etc.)
    public TMP_InputField duracaoInput;       // Duração (Instantânea, Concentração até 1 minuto, etc.)
    public TMP_InputField componentesInput;   // V, S, M, C (Concentração) ou R (Ritual)
    public Toggle preparadaToggle;

    [Header("Controles")]
    public Button btnExpandirDetalhes;
    public Button btnColapsarDetalhes;
    public GameObject conteudoDetalhes;

    [Header("Conteúdo Expansível - Detalhes")]
    public TMP_InputField nivelDetalheInput;
    public TMP_InputField tempoDetalheInput;
    public TMP_InputField alcanceDetalheInput;
    public TMP_InputField duracaoDetalheInput;
    public TMP_InputField componentesDetalheInput;
    public TMP_InputField descricaoCompletaInput;

    public void Setup(string nome, string nivel, string tempo, string alcance, string duracao, string componentes,
        bool preparada, Action<bool> onPreparadaChanged, bool lockToggle, string descricaoCompleta = null)
    {
        if (nomeInput != null) nomeInput.SetTextWithoutNotify(nome);
        if (nivelInput != null) nivelInput.SetTextWithoutNotify(nivel);
        if (tempoInput != null) tempoInput.SetTextWithoutNotify(string.IsNullOrEmpty(tempo) ? "-" : tempo);
        if (alcanceInput != null) alcanceInput.SetTextWithoutNotify(string.IsNullOrEmpty(alcance) ? "-" : alcance);
        if (duracaoInput != null) duracaoInput.SetTextWithoutNotify(string.IsNullOrEmpty(duracao) ? "-" : duracao);
        if (componentesInput != null) componentesInput.SetTextWithoutNotify(string.IsNullOrEmpty(componentes) ? "-" : componentes);

        if (preparadaToggle != null)
        {
            preparadaToggle.onValueChanged.RemoveAllListeners();
            preparadaToggle.SetIsOnWithoutNotify(preparada);
            preparadaToggle.interactable = !lockToggle;

            if (!lockToggle && onPreparadaChanged != null)
                preparadaToggle.onValueChanged.AddListener((value) => onPreparadaChanged.Invoke(value));
        }

        if (nivelDetalheInput != null) nivelDetalheInput.SetTextWithoutNotify(nivel);
        if (tempoDetalheInput != null) tempoDetalheInput.SetTextWithoutNotify(string.IsNullOrEmpty(tempo) ? "-" : tempo);
        if (alcanceDetalheInput != null) alcanceDetalheInput.SetTextWithoutNotify(string.IsNullOrEmpty(alcance) ? "-" : alcance);
        if (duracaoDetalheInput != null) duracaoDetalheInput.SetTextWithoutNotify(string.IsNullOrEmpty(duracao) ? "-" : duracao);
        if (componentesDetalheInput != null) componentesDetalheInput.SetTextWithoutNotify(string.IsNullOrEmpty(componentes) ? "-" : componentes);
        if (descricaoCompletaInput != null)
            descricaoCompletaInput.SetTextWithoutNotify(string.IsNullOrEmpty(descricaoCompleta) ? "Sem descrição." : descricaoCompleta);

        ConfigurarExpansaoDetalhes();
    }

    /// <summary>
    /// Liga o autosave genérico do cabeçalho e do detalhe (nome, nível, tempo, alcance, duração, componentes, descrição completa).
    /// </summary>
    public void RegistrarAutoSave(Action onAlterado)
    {
        RegistrarEndEdit(nomeInput, onAlterado);
        RegistrarEndEdit(nivelInput, onAlterado);
        RegistrarEndEdit(tempoInput, onAlterado);
        RegistrarEndEdit(alcanceInput, onAlterado);
        RegistrarEndEdit(duracaoInput, onAlterado);
        RegistrarEndEdit(componentesInput, onAlterado);
        RegistrarEndEdit(nivelDetalheInput, onAlterado);
        RegistrarEndEdit(tempoDetalheInput, onAlterado);
        RegistrarEndEdit(alcanceDetalheInput, onAlterado);
        RegistrarEndEdit(duracaoDetalheInput, onAlterado);
        RegistrarEndEdit(componentesDetalheInput, onAlterado);
        RegistrarEndEdit(descricaoCompletaInput, onAlterado);
    }

    private void RegistrarEndEdit(TMP_InputField campo, Action onAlterado)
    {
        if (campo == null) return;
        campo.onEndEdit.RemoveAllListeners();
        campo.onEndEdit.AddListener((_) => onAlterado?.Invoke());
    }

    /// <summary>
    /// Coleta os valores atuais. Prioriza o campo do detalhe expandido quando ele foi editado
    /// por último (o detalhe é o mais completo); cai no cabeçalho como padrão.
    /// </summary>
    public (string nome, string nivel, string tempo, string alcance, string duracao, string componentes, string descricao) ObterDadosAtuais()
    {
        return (
            nomeInput ? nomeInput.text : "",
            nivelDetalheInput ? nivelDetalheInput.text : (nivelInput ? nivelInput.text : ""),
            tempoDetalheInput ? tempoDetalheInput.text : (tempoInput ? tempoInput.text : ""),
            alcanceDetalheInput ? alcanceDetalheInput.text : (alcanceInput ? alcanceInput.text : ""),
            duracaoDetalheInput ? duracaoDetalheInput.text : (duracaoInput ? duracaoInput.text : ""),
            componentesDetalheInput ? componentesDetalheInput.text : (componentesInput ? componentesInput.text : ""),
            descricaoCompletaInput ? descricaoCompletaInput.text : "");
    }

    private void ConfigurarExpansaoDetalhes()
    {
        RectTransform conteudoRect = conteudoDetalhes != null ? conteudoDetalhes.GetComponent<RectTransform>() : null;
        RectTransform listaParaReconstruir = transform.parent as RectTransform;

        if (conteudoDetalhes != null) conteudoDetalhes.SetActive(false);
        if (btnColapsarDetalhes != null) btnColapsarDetalhes.gameObject.SetActive(false);
        if (btnExpandirDetalhes != null) btnExpandirDetalhes.gameObject.SetActive(true);

        if (btnExpandirDetalhes != null)
        {
            btnExpandirDetalhes.onClick.RemoveAllListeners();
            btnExpandirDetalhes.onClick.AddListener(() =>
            {
                btnExpandirDetalhes.gameObject.SetActive(false);
                if (btnColapsarDetalhes != null) btnColapsarDetalhes.gameObject.SetActive(true);
                UIAnimacao.AnimarExpansao(this, conteudoRect, true, listaParaReconstruir);
            });
        }

        if (btnColapsarDetalhes != null)
        {
            btnColapsarDetalhes.onClick.RemoveAllListeners();
            btnColapsarDetalhes.onClick.AddListener(() =>
            {
                btnColapsarDetalhes.gameObject.SetActive(false);
                if (btnExpandirDetalhes != null) btnExpandirDetalhes.gameObject.SetActive(true);
                UIAnimacao.AnimarExpansao(this, conteudoRect, false, listaParaReconstruir);
            });
        }
    }
}
