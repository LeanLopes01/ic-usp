using TMPro;
using UnityEngine;

/// <summary>
/// Aplica o destaque visual de uma rolagem (total + label "Crítico!") de acordo com o
/// ResultadoCritico — verde no sucesso, vermelho na falha, cor padrão (sem label) no normal. Um único
/// lugar pra essa lógica porque ela se repete 3x: ataque e dano em ChatMensagemAtaqueUI, e o total em
/// ChatMensagemTesteUI.
/// </summary>
public static class ChatCriticoUI
{
    // Público — reaproveitado por DadoRoladoUI pra colorir cada dado individual (máximo/mínimo) com
    // a MESMA cor de sucesso/falha, em vez de duplicar o verde/vermelho em outro arquivo.
    public static readonly Color CorSucesso = Color.green;
    public static readonly Color CorFalha = Color.red;

    public static void Aplicar(TMP_Text valorText, GameObject labelCriticoGO, TMP_Text labelCriticoText,
        int valor, ResultadoCritico resultado)
    {
        if (valorText != null)
        {
            valorText.text = valor.ToString();
            valorText.color = resultado switch
            {
                ResultadoCritico.Sucesso => CorSucesso,
                ResultadoCritico.Falha   => CorFalha,
                _                        => valorText.color, // cor padrão do prefab — não mexe se normal
            };
        }

        bool ehCritico = resultado != ResultadoCritico.Normal;
        if (labelCriticoGO != null) labelCriticoGO.SetActive(ehCritico);
        // Limpa o TEXTO também, não só o GameObject — reforço defensivo caso labelCriticoGO não
        // esteja arrastado no prefab (aí o SetActive acima nunca roda e o texto fixo "Crítico!"
        // ficaria sempre visível).
        if (labelCriticoText != null)
        {
            labelCriticoText.text = ehCritico ? "Crítico!" : "";
            if (ehCritico) labelCriticoText.color = resultado == ResultadoCritico.Sucesso ? CorSucesso : CorFalha;
        }
    }
}
