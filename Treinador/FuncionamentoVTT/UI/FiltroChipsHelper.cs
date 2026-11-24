using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Versão reutilizável do sistema de "chips" de filtro (texto + toggle, multi-seleção,
/// expansível/colapsável) que FichaGeralManager.cs já usa nas abas de Magias/Ações/Inventário/
/// Habilidades — extraído aqui pra outras telas (ex: EquipmentSelector/SpellSelector na criação de
/// ficha) reaproveitarem sem duplicar a lógica. Usa o mesmo prefab FiltroChipToggleUI (toggle+texto),
/// que já é genérico e não depende de FichaGeralManager.
/// </summary>
public static class FiltroChipsHelper
{
    /// <summary>Instancia um chip por tag dentro de 'conteudo'. Limpa e recria do zero — chamar de
    /// novo sempre que a lista de tags disponíveis mudar (ex: categorias dinâmicas de um inventário).</summary>
    public static void PopularChips(Transform conteudo, GameObject chipPrefab, IEnumerable<string> tags,
        List<GameObject> chipsInstanciados, HashSet<string> tagsAtivas, Action onFiltroAlterado)
    {
        foreach (var go in chipsInstanciados) if (go != null) UnityEngine.Object.Destroy(go);
        chipsInstanciados.Clear();
        tagsAtivas.Clear();

        if (conteudo == null || chipPrefab == null || tags == null) return;

        // Materializado uma vez — o botão direito (exclusão, ver abaixo) precisa conhecer todas as
        // tags pra marcar "todas menos esta", não só a que foi clicada.
        List<string> todasAsTags = tags.ToList();

        foreach (string tag in todasAsTags)
        {
            GameObject go = UnityEngine.Object.Instantiate(chipPrefab, conteudo, false);
            go.SetActive(true);
            chipsInstanciados.Add(go);

            FiltroChipToggleUI chip = go.GetComponent<FiltroChipToggleUI>();
            if (chip == null) continue;

            chip.Configurar(tag, (tagAlterada, isOn) =>
            {
                if (isOn) tagsAtivas.Add(tagAlterada);
                else tagsAtivas.Remove(tagAlterada);
                onFiltroAlterado?.Invoke();
            });

            // Botão direito no toggle = filtro de exclusão: ativa todas as OUTRAS tags e desativa
            // esta. Botão esquerdo continua sendo o toggle normal de sempre — Toggle já ignora
            // cliques que não sejam o botão esquerdo (Selectable/Toggle.OnPointerClick só reage a
            // InputButton.Left), então os dois convivem no mesmo GameObject sem conflito.
            if (chip.toggle != null)
            {
                string tagCapturada = tag;
                EventTrigger trigger = chip.toggle.GetComponent<EventTrigger>();
                if (trigger == null) trigger = chip.toggle.gameObject.AddComponent<EventTrigger>();

                EventTrigger.Entry entryCliqueDireito = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                entryCliqueDireito.callback.AddListener((data) =>
                {
                    if (!(data is PointerEventData pointerData) || pointerData.button != PointerEventData.InputButton.Right) return;
                    AplicarFiltroExclusao(tagCapturada, todasAsTags, chipsInstanciados, tagsAtivas, onFiltroAlterado);
                });
                trigger.triggers.Add(entryCliqueDireito);
            }
        }
    }

    // Botão direito num chip: filtro de exclusão — mostra tudo MENOS a tag clicada. Marca todas as
    // outras tags como ativas e desliga só a clicada, atualizando o toggle visual de cada chip pra
    // refletir o novo estado sem disparar de novo o callback normal (SetIsOnWithoutNotify).
    private static void AplicarFiltroExclusao(string tagExcluida, List<string> todasAsTags,
        List<GameObject> chipsInstanciados, HashSet<string> tagsAtivas, Action onFiltroAlterado)
    {
        tagsAtivas.Clear();
        foreach (string tag in todasAsTags)
            if (!string.Equals(tag, tagExcluida, StringComparison.Ordinal))
                tagsAtivas.Add(tag);

        foreach (GameObject go in chipsInstanciados)
        {
            if (go == null) continue;
            FiltroChipToggleUI chip = go.GetComponent<FiltroChipToggleUI>();
            if (chip == null || chip.toggle == null) continue;
            chip.toggle.SetIsOnWithoutNotify(tagsAtivas.Contains(chip.Tag));
        }

        onFiltroAlterado?.Invoke();
    }

    /// <summary>Liga o par de botões expandir/colapsar ao content de chips e popula os chips a
    /// partir das tags. Chama isto sempre que quiser (re)configurar um painel de filtro completo —
    /// registra os listeners dos botões só na primeira vez de fato (RemoveAllListeners evita
    /// duplicar se chamado de novo).</summary>
    public static void ConfigurarExpansao(Button btnExpandir, Button btnColapsar, Transform conteudo,
        GameObject chipPrefab, IEnumerable<string> tags, List<GameObject> chipsInstanciados,
        HashSet<string> tagsAtivas, Action onFiltroAlterado)
    {
        PopularChips(conteudo, chipPrefab, tags, chipsInstanciados, tagsAtivas, onFiltroAlterado);

        if (conteudo != null) conteudo.gameObject.SetActive(false);
        if (btnColapsar != null) btnColapsar.gameObject.SetActive(false);
        if (btnExpandir != null) btnExpandir.gameObject.SetActive(true);

        if (btnExpandir != null)
        {
            btnExpandir.onClick.RemoveAllListeners();
            btnExpandir.onClick.AddListener(() =>
            {
                if (conteudo != null) conteudo.gameObject.SetActive(true);
                btnExpandir.gameObject.SetActive(false);
                if (btnColapsar != null) btnColapsar.gameObject.SetActive(true);
            });
        }

        if (btnColapsar != null)
        {
            btnColapsar.onClick.RemoveAllListeners();
            btnColapsar.onClick.AddListener(() =>
            {
                if (conteudo != null) conteudo.gameObject.SetActive(false);
                btnColapsar.gameObject.SetActive(false);
                if (btnExpandir != null) btnExpandir.gameObject.SetActive(true);
            });
        }
    }
}
