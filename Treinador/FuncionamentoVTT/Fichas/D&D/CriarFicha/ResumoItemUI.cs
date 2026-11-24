using UnityEngine;
using TMPro;

public class ResumoItemUI : MonoBehaviour
{
    public TMP_Text tituloTxt;
    public TMP_Text detalhesTxt;

    public void Setup(string categoria, string detalhes)
    {
        if (tituloTxt) tituloTxt.text = categoria.ToUpper();
        if (detalhesTxt) detalhesTxt.text = detalhes;
    }
}