using System;
using UnityEngine;
using TMPro;

// Uma das 3 "abas" de dano dentro do conteúdo expansível de uma Ação (todas iguais).
// Só a primeira (índice 0) recebe os valores padrão da arma/ataque ao criar a ação;
// as outras ficam livres para o player registrar danos adicionais (ex: um smite).
public class AcaoDanoSlotUI : MonoBehaviour
{
    [Header("Campos de Entrada")]
    public TMP_InputField nomeDanoInput;   // Ex: "Florete mais Smite"
    public TMP_InputField ataqueInput;     // Fórmula de rolagem do ataque. Ex: "1d20+7"
    public TMP_InputField danoInput;       // Fórmula de rolagem do dano. Ex: "1d8+3"
    public TMP_InputField tipoDanoInput;
    public TMP_InputField alcanceInput;
    public TMP_InputField criticoInput;

    public void Setup(string nomeDano, string ataque, string dano, string tipoDano, string alcance, string critico)
    {
        if (nomeDanoInput) nomeDanoInput.text = nomeDano ?? "";
        if (ataqueInput) ataqueInput.text = ataque ?? "";
        if (danoInput) danoInput.text = dano ?? "";
        if (tipoDanoInput) tipoDanoInput.text = tipoDano ?? "";
        if (alcanceInput) alcanceInput.text = alcance ?? "";
        if (criticoInput) criticoInput.text = critico ?? "";
    }

    public void RegistrarAutoSave(Action onAlterado)
    {
        RegistrarEndEdit(nomeDanoInput, onAlterado);
        RegistrarEndEdit(ataqueInput, onAlterado);
        RegistrarEndEdit(danoInput, onAlterado);
        RegistrarEndEdit(tipoDanoInput, onAlterado);
        RegistrarEndEdit(alcanceInput, onAlterado);
        RegistrarEndEdit(criticoInput, onAlterado);
    }

    private void RegistrarEndEdit(TMP_InputField campo, Action onAlterado)
    {
        if (campo == null) return;
        campo.onEndEdit.RemoveAllListeners();
        campo.onEndEdit.AddListener((_) => onAlterado?.Invoke());
    }

    public AcaoDanoData ObterDadosAtuais()
    {
        return new AcaoDanoData
        {
            nome = nomeDanoInput ? nomeDanoInput.text : "",
            ataque = ataqueInput ? ataqueInput.text : "",
            dano = danoInput ? danoInput.text : "",
            tipoDano = tipoDanoInput ? tipoDanoInput.text : "",
            distancia = alcanceInput ? alcanceInput.text : "",
            critico = criticoInput ? criticoInput.text : ""
        };
    }
}

[Serializable]
public class AcaoDanoData
{
    public string nome;
    public string ataque;
    public string dano;
    public string tipoDano;
    public string distancia;
    public string critico;
}
