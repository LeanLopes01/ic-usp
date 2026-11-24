using TMPro;
using UnityEngine;

/// <summary>Componente do prefab de &lt;Section title="..."&gt; — só define o texto do título.</summary>
public class SecaoUI : MonoBehaviour
{
    [SerializeField] private TMP_Text tituloTexto;

    public void DefinirTitulo(string titulo)
    {
        if (tituloTexto != null) tituloTexto.text = titulo ?? string.Empty;
    }
}
