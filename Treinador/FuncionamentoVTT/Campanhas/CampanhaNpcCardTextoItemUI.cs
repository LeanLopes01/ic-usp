using TMPro;
using UnityEngine;

/// <summary>
/// Item genérico "título + corpo" — usado pelo prefabTextoItem de CampanhaNpcCardUI: um item por
/// campo presente na entrada (título = Label do XmlFieldDef, ex: "Vida"; corpo = valor já formatado,
/// ver NpcEntryFormatter). Somente leitura, sem lógica própria além de exibir o texto recebido.
/// </summary>
public class CampanhaNpcCardTextoItemUI : MonoBehaviour
{
    [SerializeField] private TMP_Text titulo;
    [SerializeField] private TMP_Text corpo;

    public void Configurar(string tituloTexto, string corpoTexto)
    {
        if (titulo != null) titulo.text = tituloTexto;
        if (corpo != null) corpo.text = corpoTexto;
    }
}
