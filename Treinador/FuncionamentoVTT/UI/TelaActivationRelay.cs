using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Coloque este componente no GameObject da tela (ex: TelaFichas).
/// Dispara OnTelaAtivada quando o objeto é ativado e OnTelaDesativada quando desativado.
/// Arraste callbacks no Inspector ou use AddListener via código.
/// </summary>
public class TelaActivationRelay : MonoBehaviour
{
    public UnityEvent OnTelaAtivada;
    public UnityEvent OnTelaDesativada;

    private void OnEnable()  => OnTelaAtivada?.Invoke();
    private void OnDisable() => OnTelaDesativada?.Invoke();
}
