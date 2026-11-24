using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Mostra um label flutuante ("Fulano está movendo") acima de um token enquanto alguém o arrasta —
/// funciona igual no host, nos peers desktop e no celular (que só recebe o evento via
/// SalaMobileBridge, nunca gera). Escuta NetworkSyncManager.OnTokenSeguradoRecebido, que já cobre
/// tanto o próprio arrasto local (ver MapTokenDraggable.NotificarInicioArrasto) quanto o dos outros.
/// Coloque este script em qualquer GameObject da cena de sala (ex: junto do MapManager).
/// </summary>
public class TokenPresencaOverlay : MonoBehaviour
{
    [SerializeField] private MapManager mapManager;
    [SerializeField] private Color corTexto = new Color(1f, 0.85f, 0.2f);
    [SerializeField] private float fontSize = 2.5f;
    [SerializeField] private Vector3 offsetLocal = new Vector3(0f, 0.9f, 0f);

    private readonly Dictionary<string, TextMeshPro> labelsAtivos = new Dictionary<string, TextMeshPro>();

    private void OnEnable()
    {
        if (mapManager == null) mapManager = FindFirstObjectByType<MapManager>();

        if (NetworkSyncManager.Instancia != null) Assinar();
        else StartCoroutine(AssinarQuandoDisponivel());
    }

    private IEnumerator AssinarQuandoDisponivel()
    {
        while (NetworkSyncManager.Instancia == null) yield return null;
        Assinar();
    }

    private void Assinar()
    {
        NetworkSyncManager.Instancia.OnTokenSeguradoRecebido -= AoTokenSegurado;
        NetworkSyncManager.Instancia.OnTokenSeguradoRecebido += AoTokenSegurado;
    }

    private void OnDisable()
    {
        if (NetworkSyncManager.Instancia != null)
            NetworkSyncManager.Instancia.OnTokenSeguradoRecebido -= AoTokenSegurado;

        foreach (TextMeshPro label in labelsAtivos.Values)
            if (label != null) Destroy(label.gameObject);
        labelsAtivos.Clear();
    }

    private void AoTokenSegurado(string tokenId, string nomeJogador, bool segurando) =>
        MostrarOuEsconder(tokenId, nomeJogador, segurando);

    /// <summary>Exposto pra ser chamado direto por quem não passa pelo NetworkSyncManager (ex: a
    /// cena mobile, que recebe esse mesmo dado via SalaLanCliente.OnTokenSegurado/OnTokenSolto em
    /// vez de P2P Steam — ver DiceVTTMobileUI).</summary>
    public void MostrarOuEsconder(string tokenId, string nomeJogador, bool segurando)
    {
        RemoverLabel(tokenId);
        if (!segurando || mapManager == null) return;

        MapTokenDraggable token = mapManager.EncontrarTokenPorId(tokenId);
        if (token == null) return;

        var go = new GameObject($"Presenca_{nomeJogador}");
        go.transform.SetParent(token.transform, false);
        go.transform.localPosition = offsetLocal;

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = nomeJogador;
        tmp.fontSize = fontSize;
        tmp.color = corTexto;
        tmp.alignment = TextAlignmentOptions.Center;

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sortingOrder = 500; // acima dos sprites de token/mapa

        labelsAtivos[tokenId] = tmp;
    }

    private void RemoverLabel(string tokenId)
    {
        if (labelsAtivos.TryGetValue(tokenId, out TextMeshPro label))
        {
            if (label != null) Destroy(label.gameObject);
            labelsAtivos.Remove(tokenId);
        }
    }
}
