using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Painel do tooltip de detalhe de rolagem (hover no total de ataque/dano/teste no chat, ver
// RolagemTooltipTrigger) — nome (Ataque/Dano/Rolagem), notação (ex: "3d6+2") e um DadoRoladoUI por
// dado rolado, usando o prefab certo pro tamanho de cada dado (ver prefabsPorFace).
public class DadosRolagemTooltipUI : MonoBehaviour
{
    [Serializable]
    private class EntradaPrefabDado
    {
        public int faces;
        public GameObject prefab;
    }

    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private TMP_Text notacaoText;
    [SerializeField] private Transform containerDados;

    [Tooltip("Um prefab (com DadoRoladoUI) por tamanho de dado — d4/d6/d8/d10/d12/d20/etc.")]
    [SerializeField] private List<EntradaPrefabDado> prefabsPorFace = new List<EntradaPrefabDado>();

    [Tooltip("Bônus/penalidade fixo, ex: o \"+7\" de 1d20+7 — \"0\" quando não há modificador.")]
    [SerializeField] private TMP_Text modificadorText;

    public void Configurar(string nome, ResultadoRolagem resultado)
    {
        if (nomeText != null) nomeText.text = nome;
        if (notacaoText != null) notacaoText.text = resultado?.notacao ?? string.Empty;

        if (containerDados == null || resultado?.dados == null) return;

        for (int i = containerDados.childCount - 1; i >= 0; i--)
            Destroy(containerDados.GetChild(i).gameObject);

        foreach (DadoRolado dado in resultado.dados)
        {
            GameObject prefab = ObterPrefabParaFace(dado.faces);
            if (prefab == null) continue;

            GameObject go = Instantiate(prefab, containerDados);
            go.GetComponent<DadoRoladoUI>()?.Configurar(dado);
        }

        if (modificadorText != null)
            modificadorText.text = resultado.modificador > 0 ? $"+{resultado.modificador}" : resultado.modificador.ToString();
    }

    private GameObject ObterPrefabParaFace(int faces)
    {
        foreach (EntradaPrefabDado entrada in prefabsPorFace)
            if (entrada != null && entrada.faces == faces)
                return entrada.prefab;
        return null;
    }
}
