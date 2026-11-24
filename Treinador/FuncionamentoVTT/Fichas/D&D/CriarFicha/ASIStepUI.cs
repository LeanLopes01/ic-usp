using UnityEngine;
using System;
using System.Collections.Generic;

// Painel de Melhoria de Atributo com seleção simultânea de até 2 atributos.
// Arraste 6 SubChoiceOptionUI na ordem: FOR, DEX, CON, INT, SAB, CAR.
public class ASIStepUI : MonoBehaviour
{
    [Tooltip("6 SubChoiceOptionUI ESTRITAMENTE nesta ordem: FOR, DEX, CON, INT, SAB, CAR")]
    public SubChoiceOptionUI[] atributoOptions;

    private static readonly string[] nomesAtributos = { "FOR", "DEX", "CON", "INT", "SAB", "CAR" };
    private static readonly Dictionary<string, string> labelsAtributos = new Dictionary<string, string>
    {
        { "FOR", "Força (FOR)" }, { "DEX", "Destreza (DEX)" }, { "CON", "Constituição (CON)" },
        { "INT", "Inteligência (INT)" }, { "SAB", "Sabedoria (SAB)" }, { "CAR", "Carisma (CAR)" }
    };

    private readonly List<string> ativos = new List<string>();
    private readonly Dictionary<string, int> bonusAplicados = new Dictionary<string, int>();
    private Action<Dictionary<string, int>> onDeltaCalculado;

    public void SetupStep(Action<Dictionary<string, int>> onDelta)
    {
        onDeltaCalculado = onDelta;
        ativos.Clear();
        bonusAplicados.Clear();

        for (int i = 0; i < atributoOptions.Length; i++)
        {
            int index = i;
            SubChoiceOptionUI opt = atributoOptions[i];
            if (opt == null) continue;

            string attrName = nomesAtributos[i];
            bonusAplicados[attrName] = 0;
            opt.Setup(attrName, labelsAtributos[attrName],
                (_) => OnOpcaoClicada(index),
                null
            );
            opt.SetVisualSelected(false);
        }
    }

    private void OnOpcaoClicada(int index)
    {
        string attrName = nomesAtributos[index];
        bool estaAtivo = ativos.Contains(attrName);

        if (estaAtivo)
        {
            ativos.Remove(attrName);
        }
        else
        {
            ativos.Add(attrName);

            // Máximo de 2: remove o mais antigo se ultrapassar
            if (ativos.Count > 2)
            {
                string maisAntigo = ativos[0];
                ativos.RemoveAt(0);
                int oldIndex = Array.IndexOf(nomesAtributos, maisAntigo);
                if (oldIndex >= 0 && atributoOptions[oldIndex] != null)
                    atributoOptions[oldIndex].SetVisualSelected(false);
            }
        }

        AtualizarVisuais();
        RecalcularESomar();
    }

    private void AtualizarVisuais()
    {
        for (int i = 0; i < atributoOptions.Length; i++)
        {
            if (atributoOptions[i] != null)
                atributoOptions[i].SetVisualSelected(ativos.Contains(nomesAtributos[i]));
        }
    }

    private void RecalcularESomar()
    {
        Dictionary<string, int> novosBonus = new Dictionary<string, int>();
        foreach (string attr in nomesAtributos) novosBonus[attr] = 0;

        if (ativos.Count == 1)      novosBonus[ativos[0]] = 2;
        else if (ativos.Count == 2) { novosBonus[ativos[0]] = 1; novosBonus[ativos[1]] = 1; }

        Dictionary<string, int> delta = new Dictionary<string, int>();
        bool teveMudanca = false;

        foreach (string attr in nomesAtributos)
        {
            int diferenca = novosBonus[attr] - bonusAplicados[attr];
            if (diferenca != 0) { delta[attr] = diferenca; teveMudanca = true; }
            bonusAplicados[attr] = novosBonus[attr];
        }

        if (teveMudanca) onDeltaCalculado?.Invoke(delta);
    }
}
