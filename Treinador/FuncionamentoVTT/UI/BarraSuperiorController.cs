using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Coloque este componente no GameObject da tela que deve esconder a barra superior.
/// Quando a tela ativar: esconde a barra e mostra o botão de revelar.
/// Quando a tela desativar: restaura a barra e esconde o botão.
/// </summary>
public class BarraSuperiorController : MonoBehaviour
{
    [SerializeField] private GameObject barraSuperior;
    [SerializeField] private Button     botaoMostrarBarra;

    private void OnEnable()
    {
        if (barraSuperior     != null) barraSuperior.SetActive(false);
        if (botaoMostrarBarra != null)
        {
            botaoMostrarBarra.gameObject.SetActive(true);
            botaoMostrarBarra.onClick.RemoveListener(MostrarBarra);
            botaoMostrarBarra.onClick.AddListener(MostrarBarra);
        }
    }

    private void OnDisable()
    {
        if (barraSuperior     != null) barraSuperior.SetActive(true);
        if (botaoMostrarBarra != null)
        {
            botaoMostrarBarra.onClick.RemoveListener(MostrarBarra);
            botaoMostrarBarra.gameObject.SetActive(false);
        }
    }

    public void MostrarBarra()
    {
        if (barraSuperior     != null) barraSuperior.SetActive(true);
        if (botaoMostrarBarra != null) botaoMostrarBarra.gameObject.SetActive(false);
    }
}
