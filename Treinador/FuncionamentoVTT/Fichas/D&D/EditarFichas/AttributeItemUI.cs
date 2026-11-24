using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AttributeItemUI : MonoBehaviour
{
    [Header("UI - Modo Rolagem/Pontuação")]
    public TMP_Text nomeText;
    public TMP_Text valorText;
    public TMP_Text modificadorText;
    public Toggle racaSubracaToggle;
    public Button aumentarButton;
    public Button diminuirButton;

    [Header("UI - Modo Valores Fixos")]
    public TMP_Dropdown dropdownValor;
    public TMP_Text totalComBonusText;

    public string NomeAtributo { get; private set; }

    private List<int> _opcoesAtuais = new List<int>();

    public void Setup(string nomeAbreviado, string nomeCompleto, System.Action<int> onAlterar)
    {
        NomeAtributo = nomeAbreviado;
        if (nomeText != null) nomeText.text = nomeCompleto;
        if (racaSubracaToggle != null) racaSubracaToggle.interactable = false;
        if (aumentarButton != null) aumentarButton.onClick.AddListener(() => onAlterar(1));
        if (diminuirButton != null) diminuirButton.onClick.AddListener(() => onAlterar(-1));
    }

    public void AtualizarValores(int totalComBonus, bool temBonusRaca)
    {
        if (valorText != null) valorText.text = totalComBonus.ToString();

        int mod = Mathf.FloorToInt((totalComBonus - 10) / 2.0f);
        if (modificadorText != null)
        {
            string sinal = mod >= 0 ? "+" : "";
            modificadorText.text = $"{sinal}{mod}";
        }

        if (racaSubracaToggle != null) racaSubracaToggle.isOn = temBonusRaca;
    }

    public void AtualizarTotal(int total)
    {
        if (totalComBonusText != null) totalComBonusText.text = total.ToString();

        int mod = Mathf.FloorToInt((total - 10) / 2.0f);
        if (modificadorText != null)
        {
            string sinal = mod >= 0 ? "+" : "";
            modificadorText.text = $"{sinal}{mod}";
        }
    }

    public void SetModo(bool usarDropdown)
    {
        if (dropdownValor != null) dropdownValor.gameObject.SetActive(usarDropdown);
        if (totalComBonusText != null) totalComBonusText.gameObject.SetActive(usarDropdown);
        if (valorText != null) valorText.gameObject.SetActive(!usarDropdown);
        if (aumentarButton != null) aumentarButton.gameObject.SetActive(!usarDropdown);
        if (diminuirButton != null) diminuirButton.gameObject.SetActive(!usarDropdown);
    }

    public void ConfigurarDropdown(List<int> opcoes, int valorSelecionado, System.Action<int> onValorMudou)
    {
        if (dropdownValor == null) return;

        _opcoesAtuais = new List<int>(opcoes);
        dropdownValor.ClearOptions();

        var optionData = new List<TMP_Dropdown.OptionData>();
        foreach (int v in opcoes) optionData.Add(new TMP_Dropdown.OptionData(v.ToString()));
        dropdownValor.AddOptions(optionData);

        int idx = opcoes.IndexOf(valorSelecionado);
        dropdownValor.SetValueWithoutNotify(idx >= 0 ? idx : 0);

        dropdownValor.onValueChanged.RemoveAllListeners();
        dropdownValor.onValueChanged.AddListener(i =>
        {
            if (i >= 0 && i < _opcoesAtuais.Count)
                onValorMudou(_opcoesAtuais[i]);
        });
    }

    public int GetValorDropdownAtual()
    {
        if (dropdownValor == null || _opcoesAtuais.Count == 0) return 0;
        int idx = dropdownValor.value;
        return idx >= 0 && idx < _opcoesAtuais.Count ? _opcoesAtuais[idx] : _opcoesAtuais[0];
    }
}
