using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class AcoesUIItem : MonoBehaviour
{
    [Header("Campos Básicos (Cabeçalho)")]
    public TMP_InputField nomeInput;       // Ex: Espada Longa
    public TMP_InputField ataqueInput;     // Fórmula de rolagem do ataque. Ex: "1d20+7"
    public TMP_InputField danoInput;       // Fórmula de rolagem do dano. Ex: "1d8+3"
    public TMP_InputField distanciaInput;  // Alcance. Ex: 1,5m (5ft)
    public TMP_InputField criticoInput;    // Ex: 19-20 ou 20
    public TMP_InputField cdInput;         // CD (Classe de Dificuldade) — por enquanto sem uso funcional

    [Header("Controles")]
    public Button btnRemoverAcao;          // Botão com ícone de lixeira para deletar a ação
    public Button btnExpandirDetalhes;
    public Button btnColapsarDetalhes;
    public GameObject conteudoDetalhes;    // Content do prefab que expande/colapsa

    [Header("Conteúdo Expansível - Detalhes")]
    public TMP_InputField detalhesInput;   // Notas livres sobre o ataque, editável e salva na ficha
    public AcaoDanoSlotUI[] danosSlots = new AcaoDanoSlotUI[3]; // 3 abas de dano iguais

    /// <summary>
    /// Preenche a UI automaticamente (chamado pelo botão de "Adicionar" do Tooltip)
    /// </summary>
    public void Setup(string nomeBase, string danoBase, string tipoDano, string distancia, string critico, string ataque = "", string cd = "")
    {
        if (nomeInput) nomeInput.text = nomeBase;
        if (ataqueInput) ataqueInput.text = ataque;
        if (danoInput) danoInput.text = danoBase;
        if (distanciaInput) distanciaInput.text = distancia;
        if (criticoInput) criticoInput.text = string.IsNullOrEmpty(critico) ? "20" : critico;
        if (cdInput) cdInput.text = cd;

        ConfigurarTruncamentoComReticencias(nomeInput);
        ConfigurarTruncamentoComReticencias(ataqueInput);
        ConfigurarTruncamentoComReticencias(danoInput);
        ConfigurarTruncamentoComReticencias(distanciaInput);
        ConfigurarTruncamentoComReticencias(criticoInput);
        ConfigurarTruncamentoComReticencias(cdInput);

        ConfigurarExpansaoDetalhes();

        // Só a primeira aba de dano recebe os valores padrão da ação; as outras 2 ficam livres.
        if (danosSlots != null && danosSlots.Length > 0 && danosSlots[0] != null)
            danosSlots[0].Setup(nomeBase, ataque, danoBase, tipoDano, distancia, critico);
    }

    /// <summary>
    /// Restaura detalhes e abas de dano salvos anteriormente (sobrepõe os padrões do Setup).
    /// </summary>
    public void CarregarDetalhesEDanos(string detalhes, List<AcaoDanoData> danos)
    {
        if (detalhesInput) detalhesInput.text = detalhes ?? "";

        if (danosSlots == null || danos == null) return;
        for (int i = 0; i < danosSlots.Length && i < danos.Count; i++)
        {
            if (danosSlots[i] == null || danos[i] == null) continue;
            danosSlots[i].Setup(danos[i].nome, danos[i].ataque, danos[i].dano, danos[i].tipoDano, danos[i].distancia, danos[i].critico);
        }
    }

    // Faz o texto interno do InputField cortar com "..." em vez de estourar o layout quando
    // não cabe na largura do campo. Não é aplicado em detalhesInput, que deve quebrar linha normalmente.
    private void ConfigurarTruncamentoComReticencias(TMP_InputField campo)
    {
        if (campo == null || campo.textComponent == null) return;

        campo.textComponent.textWrappingMode = TextWrappingModes.NoWrap;
        campo.textComponent.overflowMode = TextOverflowModes.Ellipsis;
    }

    private void ConfigurarExpansaoDetalhes()
    {
        RectTransform conteudoRect = conteudoDetalhes != null ? conteudoDetalhes.GetComponent<RectTransform>() : null;
        RectTransform listaParaReconstruir = transform.parent as RectTransform;

        if (conteudoDetalhes != null) conteudoDetalhes.SetActive(false);
        if (btnColapsarDetalhes != null) btnColapsarDetalhes.gameObject.SetActive(false);
        if (btnExpandirDetalhes != null) btnExpandirDetalhes.gameObject.SetActive(true);

        if (btnExpandirDetalhes != null)
        {
            btnExpandirDetalhes.onClick.RemoveAllListeners();
            btnExpandirDetalhes.onClick.AddListener(() =>
            {
                btnExpandirDetalhes.gameObject.SetActive(false);
                if (btnColapsarDetalhes != null) btnColapsarDetalhes.gameObject.SetActive(true);
                UIAnimacao.AnimarExpansao(this, conteudoRect, true, listaParaReconstruir);
            });
        }

        if (btnColapsarDetalhes != null)
        {
            btnColapsarDetalhes.onClick.RemoveAllListeners();
            btnColapsarDetalhes.onClick.AddListener(() =>
            {
                btnColapsarDetalhes.gameObject.SetActive(false);
                if (btnExpandirDetalhes != null) btnExpandirDetalhes.gameObject.SetActive(true);
                UIAnimacao.AnimarExpansao(this, conteudoRect, false, listaParaReconstruir);
            });
        }
    }

    /// <summary>
    /// Liga o autosave em todos os campos editáveis (cabeçalho, detalhes e as 3 abas de dano),
    /// exceto distanciaInput — o Manager cuida dele à parte pois também precisa reclassificar
    /// o tipo da ação (Corpo a Corpo / Distância).
    /// </summary>
    public void RegistrarAutoSave(Action onAlterado)
    {
        RegistrarEndEdit(nomeInput, onAlterado);
        RegistrarEndEdit(ataqueInput, onAlterado);
        RegistrarEndEdit(danoInput, onAlterado);
        RegistrarEndEdit(criticoInput, onAlterado);
        RegistrarEndEdit(cdInput, onAlterado);
        RegistrarEndEdit(detalhesInput, onAlterado);

        if (danosSlots != null)
            foreach (var slot in danosSlots)
                slot?.RegistrarAutoSave(onAlterado);
    }

    private void RegistrarEndEdit(TMP_InputField campo, Action onAlterado)
    {
        if (campo == null) return;
        campo.onEndEdit.RemoveAllListeners();
        campo.onEndEdit.AddListener((_) => onAlterado?.Invoke());
    }

    /// <summary>
    /// Método prático para quando você for varrer a lista e salvar a ficha no JSON
    /// </summary>
    public AcaoData ObterDadosAtuais()
    {
        var danos = new List<AcaoDanoData>();
        if (danosSlots != null)
            foreach (var slot in danosSlots)
                if (slot != null) danos.Add(slot.ObterDadosAtuais());

        return new AcaoData
        {
            nome = nomeInput ? nomeInput.text : "",
            ataque = ataqueInput ? ataqueInput.text : "",
            dano = danoInput ? danoInput.text : "",
            distancia = distanciaInput ? distanciaInput.text : "",
            critico = criticoInput ? criticoInput.text : "",
            cd = cdInput ? cdInput.text : "",
            detalhes = detalhesInput ? detalhesInput.text : "",
            danos = danos
        };
    }
}

// Classe auxiliar de dados para facilitar a conversão para JSON depois
[Serializable]
public class AcaoData
{
    public string nome;
    public string ataque;
    public string dano;
    public string tipoDano; // mantido para compatibilidade com fichas salvas antes das abas de dano
    public string distancia;
    public string critico;
    public string cd;
    public string detalhes;
    public List<AcaoDanoData> danos = new List<AcaoDanoData>();
}
